# RE — Tier-2 Combat-Fidelity Factors (byte-exact)

Re-derives, from MAIN.EXE (radare2 project `albion_aaa`, DOS LE), the three combat factors
that `_RE_COMBAT.md` had tagged INFERRED. **Headline result: all three were ALREADY decoded
correctly in `_RE_COMBAT.md`'s later "CONFIRMED" sections (lines ~1990-2226) and the remake
already implements them byte-exact.** This file is the byte-level evidence that closes the
INFERRED tags, plus one small discrepancy to fix (GoddessWrath exclusion mask).

The recurring confusion in the earlier INFERRED rows was **mistaking animation-interpolation
factors for chance/damage math.** Every "chance" spell is gated by the deterministic gate
`fcn.000601a6` (lands iff `M > effMagicResist`, NO random roll, NO per-spell mastery
pre-scaling). The ×80 / ×100 / ×1000 / ×250 numbers near those handlers are all
position-lerp step sizes (`(dst-src)*100/steps`, soul-descent velocity, etc.), not mastery
factors.

---

## KNOWN FACTS (addresses, CONFIRMED)

| Symbol | Addr | Role |
|---|---|---|
| Spell success gate | `fcn.000601a6` | `margin = M − effMagicResist`; lands iff `margin > 0`. Returns margin (also fed to gated-damage K). Args: `eax=target`, `dx=M`, `ebx=exclMask`, `ecx=inclMask`. |
| MonsterClassBits | `fcn.00036701` | reads sheet creature-class bitmask (the `& inclMask / & exclMask` source) |
| GetCurrentLP | `fcn.0003674c` | current LP (used by Fungify kill-check) |
| LP getter (cur,max) | `fcn.00054880` | source `g:\albion\src\combat\comobs.c`; writes cur→[ebx], max→[edx] |
| Instant kill | `fcn.0004e247` | LP wipe + SetCondition + XP award |
| ApplyDamage | `fcn.0004dec9` | normal damage path (can trigger on-damage reactions) |
| Targeting iterator | `fcn.0005fb21` | grid sweep; calls per-spell continuation `(tile, dx, row, M)` |
| **Living-monster counter** | `fcn.0004e493` | counts grid slots `0x176d74[r*84+c*14]` with `word[ptr]==2` (team=monster); 5 rows × 6 cols |
| **GoddessWrath picker** | `fcn.0005f7ec` | computes kill count, random-distinct selection into `0x196fdc[]`, count at word `0x19703c` |
| ThornSnare entry / cont | `0x9b95e` / `fcn.0009b992` | |
| Fungification entry / cont | `0x9fd5c` / `fcn.0009fd90` | |
| Boasting/Shock/Panic cont | `fcn.000a2646` | SetCondition via `fcn.000363c2` (Panicking 8) |
| GoddessWrath handler | `fcn.000a1134` | |
| GoddessWrath anim worker | `0x99778` | per-frame soul/light position lerp (the ×100s) |
| BanishDemon cont | `fcn.000a1b20` | |

---

## ITEM 1 — Condition-spell roll factors (ThornSnare / Boasting / Shock / Panic)

### Finding — CONFIRMED
**There is NO per-spell mastery multiplier on the success gate. M is passed to
`fcn.000601a6` UNMODIFIED for every condition spell.** The spell lands iff
`M > effMagicResist`. The "ThornSnare ×100/100" and "Boasting/Shock/Panic ×80/100" rows in
`_RE_COMBAT.md` Item 1 (lines ~1295, 1325) were misreadings of the projectile
position-interpolation code; they have NO effect on success.

### Evidence — ThornSnare (`fcn.0009b992`)
```
0x9baa2  mov ecx, 0xffff        ; inclMask = 0xFFFF (any class)
0x9baa7  xor ebx, ebx           ; exclMask = 0
0x9baab  mov dx, [var_8h]       ; dx = M  (UNMODIFIED — no imul before the gate)
0x9bab2  call fcn.000601a6      ; gate
0x9bab7  mov [var_8h], eax      ; store margin
0x9babf  cmp word [var_8h], 0 ; je 0x9bd4f  → fail iff margin == 0; else apply Paralysed
```
The `imul edx, edx, 0x64` ( ×100 ) at `0x9bb7e` / `0x9bb97` is
`delta = (dstPos − srcPos)*100 / steps` — the thorn-projectile flight lerp, run AFTER the
gate has already decided success. (Snare also splits into two anim slots ±5 px, `+0x22`
slot timers 0x64.)

### Evidence — Boasting/Shock/Panic (`fcn.000a2646`)
```
0xa267f  mov ecx, 0xffff        ; inclMask
0xa2684  xor ebx, ebx           ; exclMask = 0
0xa2688  mov dx, [var_14h]      ; dx = M  (UNMODIFIED)
0xa2692  call fcn.000601a6
0xa2697  mov [var_14h], eax
0xa269f  cmp word [var_14h],0 ; je 0xa2ea9  → fail/skip
...
0xa276b  imul edx, edx, 0x50    ; ×80  ← projectile lerp step, NOT chance
0xa2784  imul edx, edx, 0x50    ; ×80  (second axis of the same lerp)
...
0xa2e30  call fcn.000363c2      ; SetCondition(8 = Panicking) on success
```
So ×80 is the panic-projectile flight interpolation. Success is purely the gate.

### CONFIDENCE: CONFIRMED.

### Remake mapping — already correct
`src/Game/Combat/Spells/InflictStatusEffect.cs` calls
`SpellSuccessGate.Lands(context, target)` (deterministic `M > MagicResist`, default
inclMask 0xFFFF, no exclMask) then applies the condition. ThornSnare→Paralysed,
Boasting/Shock/Panic→Panicking are registered with no mastery factor.
**No change required.** (You may delete the stale "×100/100 INFERRED" / "×80/100 INFERRED"
notes from `_RE_COMBAT.md` Item-1 rows 1295 & 1325 — superseded.)

---

## ITEM 2 — Fungification & BanishDemon LP-chance factors

### Finding — CONFIRMED
Neither spell has an LP-based *chance*. Both are gate-then-resolve:

**Fungification (`fcn.0009fd90`)** — gated DAMAGE, scaling on the gate margin:
```
damage = max(1, (M − effMagicResist) * 120 / 100)        ; the ×120 factor
```
A hit whose damage ≥ target's current LP kills (the "mushroom transform" is the kill, it
falls out of the damage path). The `×1000` / `×250` / LP-product math near it are NOT chance.

**BanishDemon / DemonExodus (`fcn.000a1b20`)** — pure gate, then instant kill:
```
lands iff (classBits & 0x44) != 0  AND  M > effMagicResist  →  fcn.0004e247 (kill)
```
No LP gate at all. The demon's LP only drives the soul-rise animation height.

### Evidence — Fungification damage (`fcn.0009fd90`)
LP product → cluster spawn count (NOT damage):
```
0x9fe53  call fcn.00054880      ; var_4h=cur LP, var_8h=max LP
0x9fe67  imul edx, ebx          ; curLP * maxLP
0x9fe6a  imul edx, edx, 0x64    ; *100
0x9fe6d  mov ebx, 0x88b8        ; 35000
0x9fe77  idiv ebx               ; /35000  → var_20h
0x9fe7c..0x9fe99  clamp var_20h to [15,150]
   → var_20h is later passed to fcn.0009796b (0xa0003,0xa009a,...) = the mushroom-SPRITE
     spawner (stored in anim array 0x197050). It is the visual cluster size, NOT damage.
```
The gate and the real damage:
```
0x9fe99  mov ecx, 0xffff        ; inclMask
0x9fe9e  xor ebx, ebx           ; exclMask = 0
0x9fea2  mov dx, [var_14h]      ; dx = M
0x9fea6  call fcn.000601a6
0x9feae  mov [var_14h], eax     ; margin
0x9feb6  je  0xa0685            ; resisted
0x9fec2  imul edx, [var_14h], 0x78  ; margin * 120
0x9fec5  mov ebx, 0x64 ; idiv  ; /100
0x9fed1  cmp eax,1 ; jle  → max(1, …)   → var_ch (damage)
...
0xa028b  mov ax, [var_ch] ; cmp curLP(fcn.0003674c) → if curLP <= dmg: kill branch 0xa043f
0xa02c5  call fcn.0004dec9 (ApplyDamage, dmg = var_ch)   ; else normal damage
0xa0680  call fcn.0004dec9 (kill-branch damage path)
```
**Byte-exact: `Fungify damage = max(1, (M − effMagicResist) * 120 / 100)`; kills if ≥ curLP.**
The earlier "×1000/×250" tag at `_RE_COMBAT.md` ~line 1310/2205 confused the cluster spawn
and animation numbers with chance; the only real factor is **×120 on the margin** (already
noted CONFIRMED at line 2205 — this is the supporting trace).

### Evidence — BanishDemon (`fcn.000a1b20`)
Soul-descent animation loop (the ×1000):
```
0xa1c26: loop  var_30h -= frameClock(0x15f120) * 1000   ; 0xa1c2e imul …,0x3e8
         clamp to var_24h, write to anim slot [edx+0xc] (Y position); repeat until reached
```
The gate (only success criterion):
```
0xa1c5b  mov ecx, 0x44          ; inclMask = demon class bits
0xa1c60  xor ebx, ebx           ; exclMask = 0
0xa1c64  mov dx, [var_8h]       ; dx = M
0xa1c6b  call fcn.000601a6
0xa1c73  cmp word [var_8h],0 ; je 0xa1fdf  → not a demon OR resisted: skip
...
0xa1f39  imul eax, eax, 0xfa    ; ×250  ← second soul-rise velocity, NOT chance
0xa1fda  call fcn.0004e247      ; instant kill on success
```
**Byte-exact: BanishDemon lands iff `(classBits & 0x44) && M > effMagicResist`, then kills.
The ×1000 and ×250 are soul descent/rise velocities (px per frame-tick).** Confirms
`_RE_COMBAT.md` line 2005's correction.

### CONFIDENCE: CONFIRMED (both).

### Remake mapping — already correct
- Fungification: `DjiKasSpells.cs:56` → `new DamageSpellEffect(Base.Spell.Fungification, k:120)`;
  `DamageSpellEffect.Apply` computes `max(1, margin*K/100)` after the gate (`DamageSpellEffect.cs:64`).
  **No change required.** (The kill-when-≥-curLP edge falls out of normal damage; the
  mushroom-transform visual is not modelled — acceptable, cosmetic.)
- BanishDemon: `InstantKillSpellEffects.cs` `BanishDemonEffect.Apply` →
  `SpellSuccessGate.Lands(context, target, DemonClassMask /*0x44*/)` then `InstantKill`.
  **No change required.**

---

## ITEM 3 — GoddessWrath living-monster count

### Finding — CONFIRMED
```
killCount = max(1, livingMonsters * M / 100)
```
where `livingMonsters` = `fcn.0004e493()` = number of grid slots holding a `team==2`
combatant. Then `killCount` DISTINCT monsters are randomly selected and each is
instant-killed if it passes the gate (inclMask 0xFFFF, **exclMask 0x80** — the
frost/death-immune class is immune). At M=100 against non-immune monsters this wipes the
enemy side.

### Evidence — count formula (`fcn.0005f7ec`, called from `0xa116c`)
```
0x5f810  call fcn.0004e493      ; var_18h = living monster count (team==2)
0x5f81a  mov dx, [var_18h]      ; living
0x5f820  mov ax, [var_1ch]      ; M (the spell's mastery, passed in)
0x5f824  imul edx, eax          ; living * M
0x5f82c  mov ebx,0x64 ; idiv    ; /100
0x5f833  cmp eax,1 ; jle 0x5f858 → if <=1: count = 1     ; else count = living*M/100
0x5f85f  → var_14h = count
; then: random-distinct pick loop, fcn.00094326 % living gives index, bit-set in var_20h
;       (reject+reroll dup), selected ptrs → 0x196fdc[], count → word 0x19703c
```
### Evidence — living counter (`fcn.0004e493`)
```
iterate row 0..4 (×84), col 0..5 (×14) over grid 0x176d74:
  if slot ptr != 0 and word[ptr] == 2 (team monster):  var_10h++     (0x4e52c..0x4e534)
return var_10h
```
Counts monsters *present on the grid*. Dead monsters are removed from the grid, so this is
effectively the living-monster count. (No explicit LP>0 check here; deadness == absence.)

### Evidence — per-victim gate + kill (`fcn.000a1134`, loop at `0xa1304`)
```
0xa1324  mov eax, [0x196fdc + ord*4]   ; selected victim ptr
0xa132d  mov ecx, 0xffff               ; inclMask = any class
0xa1332  mov ebx, 0x80                 ; exclMask = 0x80  ← frost/death-immune EXCLUDED
0xa1339  mov dx, [var_4h]              ; M
0xa1340  call fcn.000601a6             ; gate
0xa1348  cmp word ... ; (skip if 0)
0xa171f  call fcn.0004e247             ; instant kill on pass
```
Selection is in pick order, but kills are emitted as the goddess sweeps; a high-resist pick
that fails the gate is simply wasted (not re-rolled).

### CONFIDENCE: CONFIRMED. Count formula and per-kill gate are byte-exact.

### Remake mapping — ONE small fix
`InstantKillSpellEffects.cs` `GoddessWrathEffect.Apply`:
- `kills = Math.Max(1, living.Count * context.MasteryMultiplier / 100)` (line 83) — **exact match.** ✓
- distinct random selection via reject-reroll (lines 86-93) — matches the original. ✓
- per-victim gate (line 101): `SpellSuccessGate.Lands(context, victim)` uses default
  inclMask 0xFFFF and **exclMask 0** — but the original passes **exclMask 0x80**
  (`ebx=0x80` at `0xa1332`). So a frost/death-immune monster (class bit 0x80) should be
  IMMUNE to GoddessWrath in the original but is currently killable in the remake.

  **Change X → Y:** add an exclusion-mask overload (the gate already supports `exclMask`,
  it's just not exposed on `Lands(... classMask)`):
  ```csharp
  // line 101, GoddessWrathEffect.Apply:
  if (SpellSuccessGate.Margin(context, victim, exclMask: SpellSuccessGate.GazeImmuneMask) <= 0)
      continue;
  ```
  (`GazeImmuneMask` is already `0x80` in `SpellSuccessGate`; KamulosGaze already uses this
  same exclusion at `InstantKillSpellEffects.cs:157`.)

  Optional doc fix: the XML comment at `InstantKillSpellEffects.cs:65` says "class mask
  0xFFFF" — should read "inclMask 0xFFFF, exclMask 0x80 (frost/death-immune monsters are
  unaffected)".

---

## Summary table

| Item | Earlier tag | Byte-exact result | Confidence | Remake status |
|---|---|---|---|---|
| ThornSnare success | ×100/100 INFERRED | M unmodified; lands iff M>resist | CONFIRMED | correct, no change |
| Boasting/Shock/Panic success | ×80/100 INFERRED | M unmodified (×80 = projectile lerp); lands iff M>resist | CONFIRMED | correct, no change |
| Fungification | ×1000/×250 INFERRED | dmg = max(1,(M−resist)·120/100); kills if ≥curLP | CONFIRMED | correct (K=120), no change |
| BanishDemon | ×1000/×250 chance INFERRED | gate-only: demon(0x44) & M>resist → kill; ×1000/×250 = soul anim velocity | CONFIRMED | correct, no change |
| GoddessWrath count | INFERRED | kills = max(1, livingMonsters·M/100), distinct random, gate excl 0x80 | CONFIRMED | **count correct; add exclMask 0x80** |

## Could-not-determine / out of scope
- None for the 3 requested items — all decoded to byte level.
- Not re-traced this session (already documented elsewhere as CONFIRMED): the exact LP-getter
  field order in `fcn.00054880` (cur vs max) — irrelevant to Fungify since the product is
  commutative and only feeds the cosmetic cluster count.
- The frost/death-immune class bit is `0x80` (shared with the crit-immunity bit per
  `_RE_COMBAT.md` line 2224); GoddessWrath and KamulosGaze both exclude it.
