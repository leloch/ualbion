# RE Fidelity batch 1 — SP-shortfall, Encumbrance, Surrender boss

> Read-only reverse-engineering of Albion `MAIN.EXE` (radare2 project `albion_aaa`, DOS LE).
> Three independent fidelity questions for the C# remake. CONFIRMED = instructions/bytes read;
> INFERRED = reasoned but not byte-verified.

## KNOWN FACTS (running list)

- Stat getter `fcn.00035c77(handle, statIdx)` = **EffectiveStat**: locks handle, reads
  `word[inner + statIdx*8 + 0x2a]` as base, sums modifiers. statIdx 0=Strength, 3=Speed, **4=Stamina**.
- Handle helpers: `fcn.0008b739` LockHandle→inner ptr, `fcn.0008b808` Unlock.
- Sheet field offsets (PRTCHAR, on-disk == in-memory for the common block):
  - +0x0C = `UnkownC` (u8, behaviour-strategy byte) — values 0..9, non-zero only for monsters & Drirr.
  - +0x11 = ActionPoints (u8); +0x12 = EventSetId (u16).
  - +0xCA = current LifePoints (u16); +0xD0 = current SpellPoints (u16).
  - +0xFA = Weight total (Int32) — **never read/written by the engine** (computed on demand instead).
- LP getter `fcn.0003674c` = `word[sheet+0xCA]`; LP setter `fcn.00036799` (clamps to [0, MaxLP]).
- SP getter `fcn.00036a2a` = `word[sheet+0xD0]`; SP setter `fcn.00036a77`.
- SP-cost lookup `fcn.00060a1e(school, number)` = byte `spelldat[(school*30 + number-1)*5 + 1]`
  (SPELLDAT buffer handle at `0x177594`, 5-byte records).
- Magic-cheat global `0x147130`: when non-zero, casting is free (no SP/LP cost).
- Watcom rand at `fcn.00094326` (LCG 0x41C64E6D / +0x3039, 15-bit output).

---

## TASK 1 — SP-SHORTFALL (over-cast pays Life Points) — **CONFIRMED**

### Verdict
The original does **NOT refuse** a cast when SP is insufficient. It lets you **over-cast**: it
deducts all available SP and pays the *remaining* cost in **Life Points, scaled by (100 − Stamina)**.
The cast is only refused if even your LP cannot cover the shortfall (you would die).

The remake's current `Battle.cs:1209` `if (cost>0 && sp<cost) return;` is **WRONG** — it refuses
every under-funded cast. It should over-cast (LP surcharge) and only block the would-die case.

### The surcharge function — `fcn.0006042c(sheetHandle, school, number) → lpDamage`
(296 B; called from cast core `fcn.0005fdf7` @0x5fe71 and from the spell-menu builder
`fcn.00060b98` @0x60d87.)

```c
int ComputeLpSurcharge(handle sheet, u16 school, u16 number) {
    if (!IsValidSpell(school, number)) return 0;       // fcn.00061b96 — NULL fn => no spell
    if (word[0x147130] != 0) return 0;                 // magic cheat => free, no surcharge

    int LP   = word[sheet+0xCA];                        // fcn.0003674c  (current Life Points)
    int SP   = word[sheet+0xD0];                        // fcn.00036a2a  (current Spell Points)
    int cost = SpellSpCost(school, number);             // fcn.00060a1e
    int Sta  = EffectiveStat(sheet, 4);                 // fcn.00035c77(sheet, 4) — STAMINA

    if (cost <= SP) return 0;                           // 0x604bb jbe — enough SP, no surcharge

    // Stamina scaling factor (LP cost as a % of the SP shortfall):
    int staminaFactor = (100 - Sta) * 3 / 2 + 50;      // 0x604c1..0x604da
    if (staminaFactor <= 1) staminaFactor = 1;         // max(1, ...)

    int shortfall = cost - SP;                          // SP you are missing
    int lpDamage  = shortfall * staminaFactor / 100;    // 0x60522 imul, 0x6052f idiv 100

    if (lpDamage > LP) return 0xFFFF;                   // 0x6053b — would die => "can't cast" sentinel
    return lpDamage;
}
```

Byte evidence (key lines): `0x604bb jbe` (cost<=SP skip), `0x604ce lea edx,[edx+edx*2]` (×3),
`0x604d8 sar eax,1` (/2), `0x604da add eax,0x32` (+50), `0x604c7/0x604cc` `100 - Sta`,
`0x60522 imul edx,eax`, `0x6052f idiv ebx(=100)`, `0x60537 cmp ax,[var_1ch=LP]; 0x6053b jbe`,
`0x6053d mov [var_14h],0xffff`.

**Stamina factor table** (LP paid per 1 SP of shortfall = staminaFactor/100):

| Stamina | staminaFactor = (100−Sta)·3/2 + 50 | LP per missing SP |
|---|---|---|
| 0   | 200 | 2.00 |
| 25  | 162 | 1.62 |
| 50  | 125 | 1.25 |
| 75  |  87 | 0.87 |
| 100 |  50 | 0.50 |

So high Stamina ≈ cheap over-casting (½ LP per SP), low Stamina ≈ punishing (2 LP per SP).
`lpDamage = (cost − SP) × ((100 − Stamina)·3/2 + 50) / 100`, integer (truncating) division.

### How the cast core applies it — `fcn.0005fdf7` (cast core, 540 B)
```c
lpDamage = ComputeLpSurcharge(sheet, school, number);   // var_14h
spCost   = SpellSpCost(school, number);                 // var_10h
if (lpDamage > 0) {                                      // unsigned cmp word, 0; jbe skip
    slot = FindPartyMemberSlot(sheet);                  // fcn.000357f3 -> slot+1, or 0xFFFF if not a party-table member
    if (slot != 0xFFFF) {
        ApplyDamageToPartyMember(slot, lpDamage);       // fcn.000375eb — sets LP, handles KO/death sound 725
    } else {
        SetLifePoints(sheet, max(0, LP - lpDamage));    // fcn.00036799 — generic in-combat path
    }
}
// SP deduction ALWAYS happens (both branches converge here at 0x5ff2a):
SetSpellPoints(sheet, max(0, SP - spCost));             // fcn.00036a77
// then mastery multiplier + mastery growth as already documented in docs/re/RE_COMBAT.md.
```

Note: the `0xFFFF` would-die sentinel makes `lpDamage` look like a large value here; in the cast
core path the upstream caller (spell selection) is what gates it (see below), so by the time the
core runs the spell is castable. The authoritative refuse/allow decision is made in the menu builder.

### The selection gate — `fcn.00060b98` (spell-menu builder, 920 B)
For every known spell it writes a per-entry **status code** (offset +0 of a 10-byte menu record):

| status | meaning | written when |
|---|---|---|
| 1 | normal SP cast (or free) | surcharge==0 (enough SP) |
| **2** | **castable, costs Life Points** (LP cost stored at entry+0x18) | surcharge != 0 and != 0xFFFF |
| **3** | **un-castable — would kill the caster** | surcharge == 0xFFFF |

Byte evidence: `0x60da9 cmp eax,0xffff; 0x60dae jne` → status **3**; else `0x60dcd cmp [var_14h],0;
0x60dcd je` → status **2** (and stores `lpCost` at entry+0x10/+0x18). So the UI explicitly offers
over-cast (status 2, shows an LP cost) and only forbids the lethal case (status 3).

### C# implementation recommendation (TASK 1)
Replace `Battle.CastQueuedSpell` lines ~1207-1213:

```csharp
int cost = spell?.Cost ?? 0;
int sp   = SpellPoints(caster);
if (cost > sp) {
    int sta = caster.Effective?.Attributes?.Stamina.Current ?? 100;
    int staminaFactor = Math.Max(1, (100 - sta) * 3 / 2 + 50);
    int lpDamage = (cost - sp) * staminaFactor / 100;       // truncating
    int lp = LifePoints(caster);
    if (lpDamage > lp)
        return;                                             // would-die: refuse (the only refusal)
    ApplyLifePointDamage(caster, lpDamage);                 // existing LP-damage applier (KO/death handling)
}
SetSpellPoints(caster, Math.Max(0, sp - cost));             // always deduct (clamped to 0)
```

- Use **Effective** Stamina (post-equipment) since the original uses the effective-stat getter.
- Integer truncating division throughout.
- For AI casters (`Battle.cs:1170-1194` candidate filter) the `spell.Cost > sp` exclusion should be
  relaxed too if you want monsters to over-cast — though the original AI's cast availability uses
  the same school masks; keep AI conservative unless you specifically want over-cast monsters.
- Optionally surface status 2 (LP-cost) in the spell-pick UI later; not required for combat fidelity.

CONFIDENCE: **CONFIRMED** (full disassembly of `fcn.0006042c`, `fcn.0005fdf7`, `fcn.00060b98`,
plus the LP/SP/cost/stat helpers). COULD NOT DETERMINE: nothing material — the rounding is plain
truncating integer division; the would-die sentinel path is unambiguous.

---

## TASK 2 — ENCUMBRANCE (over-weight penalty?) — **CONFIRMED: hard cap only, no graded penalty**

### Verdict
Albion has **NO graded over-weight penalty**. Carried weight feeds exactly one boolean
("over-encumbered" = `weight >= Strength*1000`), which only sets a cosmetic status flag + portrait
state and raises a warning message. It does **not** slow movement, reduce action points, or change
any stat. The remake's "hard cap, no penalty" behaviour is correct as-is.

### The weight triad (all CONFIRMED — instructions read)
1. **CurrentWeight `fcn.00036e89`** — computed on demand, never stored:
   - 9 inventory slots @ sheet+0x2E6 (stride 6): `total += word[itemRec + 0x1E]`
   - equipped slots @ sheet+0x31C (stride 6): `+= word[itemRec+0x1E] * count`
   - Gold: `+= word[sheet+0x18] * 2`; Rations: `+= word[sheet+0x1A] * 250` (`imul …,0xFA`)
   - item record via `fcn.0004a521` (id at slot+4 → table + (id-1)*0x28; weight at +0x1E).
2. **MaxWeight `fcn.00036fe6`** = `EffectiveStrength * 1000`:
   `0x37006 call fcn.00035c77` (statIdx 0=STR), `0x3700c imul eax,eax,0x3e8` (×1000).
3. **IsOverweight `fcn.00035ac8`** — the only comparison, returns a bool:
   `0x35ae6 call CurrentWeight; 0x35af0 call MaxWeight; 0x35af5 cmp edx,eax; 0x35af7 jl →0 else 1`.
   Strictly `weight >= Strength*1000`. No excess/ratio/scaling.

### Every consumer (CONFIRMED via `axt`)
- CurrentWeight read by: IsOverweight + 0x277f7, 0x2797f (inventory-screen weight display).
- MaxWeight read by: IsOverweight + 0x27809, 0x27941 (inventory display).
- IsOverweight called from 3 sites, all UI:
  - `0x12830` (inventory): raises message id 0x2bb (699) — over-encumbrance warning text.
  - `0x21d96` / `0x21fa3` (character/portrait UI): `or byte[sheet+0x28], 4` (status bit) and
    `word[sheet+0x2a] = 0x1e9` (portrait/animation state). Both cosmetic.

### Movement / Combat / Stats are weight-free (CONFIRMED)
Traced all 15 callers of `fcn.00035c77`: ActionPoints (sheet+0x11) come from `fcn.00037c22`
(stat 1 + sheet+0xe0/+0xe4, no weight term). Movement speed `fcn.0004d773` = `clamp(Speed/30, 1, 3)`
(flat tier, no weight). Combat functions read only Stamina/Speed/idx6/idx7. None reads
CurrentWeight / MaxWeight / IsOverweight / sheet+0xFA. Searches for any reg-displacement read of
sheet+0xFA returned zero — the +0xFA "weight" field is never populated by the engine (it is a
remake-port convenience only).

### C# implementation recommendation (TASK 2)
No change needed for combat/movement fidelity — keep the hard pickup cap (`InventoryManager.cs`
~655-671) and `MaxWeight = Strength * 1000` (`EffectiveSheetCalculator.cs:76`). For 1:1 polish you
*could* add the cosmetic "over-encumbered" status flag + warning message (status mirrors
`weight >= Strength*1000`), but it has no gameplay effect. Do **not** add any speed/AP penalty —
that would be a deviation from the original.

CONFIDENCE: **CONFIRMED**. COULD NOT DETERMINE (non-load-bearing): exact "kg bar" formatting at the
two display sites, and which sprite frame portrait codes 0x1e9 vs 0x219 select — both irrelevant to
the penalty question.

---

## TASK 3 — SURRENDER BOSS IDENTITY — **CONFIRMED: Monster id 52 ("AI"), the only strategy==9 monster**

### Verdict
Exactly **one** monster in the game data has behaviour-strategy byte `UnkownC` (offset 0x0C) == 9:
**MONCHAR record 52** (remake asset `Monster.52` of `monster.1-59`). Its German name is **"AI"**
(English/French name fields blank in the sheet — it is *the AI*, Albion's final boss). This is the
monster that triggers behaviour-row 8 → the surrender check (`fcn.0x51a2e` → combat outcome 4),
per docs/re/RE_ASK_SURRENDER.md. The surrender win-condition therefore **can** fire, and only in the
final-boss encounter.

### Evidence (CONFIRMED — parsed the real game data)
Parsed `<game-data>/CD/XLDLIBS/MONCHAR0.XLD` directly (XLD0I container:
magic "XLD0I" + pad + u16 count(=59) + 59×int32 lengths, then concatenated 1214-byte records).
Read byte +0x0C (`UnkownC`) of every record. Non-zero values and their distribution:

```
UnkownC value : monster record-ids (1-based)
  1 : (default — most mobs)
  2 : 8, 23, 24
  3 : 5, 19, 20
  4 : 7
  5 : 21
  6 : 22
  7 : 38 (Beastmaster), 53, 54, 55
  8 : 10, 27, 28 (Magician 1/2/3)
  9 : 52  <-- UNIQUE
```
(This matches CharacterSheet.cs:215's recorded distribution of non-zero UnkownC values
`42,3,4,1,1,1,4,3,1` for values 1..9 — value 9 has count 1.)

**Monster id 52 full dump:** Type=2 (Monster), Race=14, Class=31, Level=50, Morale=99,
UnkownC(+0x0C)=**9**, UnkownE(+0x0E)=130 (matches CharacterSheet.cs note: "Ai,Ai2,AiBody22=130"),
LifePoints(+0xCA)=**4950**, ExperienceReward(+0x20)=**0** (you never kill it for XP — it is the
unkillable finale boss), German name="AI".

### Note on the offset mapping
docs/re/RE_ASK_SURRENDER.md reads the runtime behaviour key from combatant `[+0x12]` and notes the
disk-MONCHAR `+0x0C → runtime +0x12` copy was INFERRED, not traced. The data here is unambiguous at
the source: `UnkownC` (disk +0x0C) is the strategy field, and **value 9 is unique to monster 52**.
(Disk +0x12 is EventSetId = 0 for this monster, so the runtime read is definitely the copied
strategy byte, not EventSetId.) So whichever runtime offset the engine uses, the source discriminator
is `UnkownC == 9` and it identifies exactly one monster.

### C# implementation recommendation (TASK 3)
- The surrender win-condition gate should key off `CharacterSheet.UnkownC == 9` (rename suggested:
  `BehaviourStrategy` / `CombatTactic`). Only `Monster.52` ("AI") qualifies in shipped data.
- Wire it into post-strike combat AI exactly as docs/re/RE_ASK_SURRENDER.md recommends: when an attacking
  monster with `UnkownC == 9` deals damage, run the surrender check
  `conscious <= max(1, partySize - 2)` → set a 4th `BattleOutcome.Surrender`, end the battle, clear
  conditions, and chain the boss encounter's follow-on event (the scripted ending). The boss is
  effectively unkillable (4950 LP, 0 XP reward) so surrender is the intended end state.
- For test/cockpit purposes: to make the finale win fire, the encounter must contain `Monster.52`
  and the party must be whittled to `max(1, partySize-2)` conscious members.

CONFIDENCE: **CONFIRMED** (parsed the actual MONCHAR0.XLD bytes; uniqueness verified across all 59
records). The link from strategy-9 → surrender check is CONFIRMED in docs/re/RE_ASK_SURRENDER.md.
COULD NOT DETERMINE here: the exact ending map/event id chained after surrender (it is supplied at
runtime by the boss encounter's event-chain args, not a constant — see docs/re/RE_ASK_SURRENDER.md).

---

## Summary

| Task | Finding | Confidence | Remake action |
|---|---|---|---|
| 1 SP-shortfall | Over-cast allowed; pays `(cost−SP) × ((100−Sta)·3/2 + 50)/100` LP; refuse only if that exceeds current LP | CONFIRMED | Fix `Battle.cs:1209` to over-cast with LP surcharge |
| 2 Encumbrance | Hard cap `weight ≥ Strength·1000` only; cosmetic over-weight flag; **no** movement/AP/stat penalty | CONFIRMED | None (current behaviour correct); optional cosmetic flag |
| 3 Surrender boss | `UnkownC==9` is unique to **Monster.52 "AI"** (Lvl 50, 4950 LP, 0 XP) — the final boss | CONFIRMED | Key surrender gate off `UnkownC==9` |
