# RE batch 5A (2026-06-12): combat action executors

> Cluster: Move/path-finding, ranged ammo + reach rules, "Summon", Insane/Panicking/Fleeing
> turns, Morale (sheet+0x0F), battle-view hover bob. All radare2 against `albion_aaa`,
> region ~0x4B000-0x57000. CONFIRMED = read from disassembly with consistent xrefs;
> INFERRED = strong pattern, one detail unverified. Conventions per docs/re/RE_COMBAT.md
> (combatant tables 0x15e6ac party / 0x15e944 monsters, 110 B stride; grid
> `0x176d74 + row*84 + col*14`; tile index = row*6+col; action kind at +0x4E).

## Combatant-struct fields pinned this batch (CONFIRMED)

| Offset | Field |
|---|---|
| +0x00 | kind: 1=party, 2=monster (0=dead slot) |
| +0x02 | 1-based index into its table |
| +0x04 | flags: bit0 Hurry (AP×2), bit1 anim-reverse, bit2 **panic/insane auto-pilot**, bit3 self-animating, bit4 **removed from battle** (fled/unconscious) |
| +0x06 | AI attempt mask (bit0 magic, bit1 melee, bit2 ranged) — known |
| +0x08 | sheet handle |
| +0x10 | **render class** (vtable_2 row 1..4) = MONCHAR byte sheet+0x0D, 0 defaults to 1 — resolves the old open item |
| +0x12 | **behaviour/strategy id** (1-based row of table 0x13e1f0) = MONCHAR byte sheet+0x0C |
| +0x14 | persistent target combatant ptr (re-picked when invalid) |
| +0x42/+0x44 | col / row (0xFFFF when out of battle) |
| +0x4E | queued action kind |
| +0x50 | weapon item id used by attack anims |
| +0x52.. | per-kind payload: Move = {count, waypointTile[count]}; Attack(2/3) = {targetTile, weaponSlot}; Cast(5) = {school, number, FFFF×3, casterSheet(+0x5C), self(+0x60), targetTileMask(+0x66), targetMode(+0x6A)} |

**vtable_1 (0x13e194, 6 B entries: u16 cancelMask + u32 fn) — full dump (CONFIRMED):**

| kind | fn | cancel mask (conditions present at execution → action silently cleared) |
|---|---|---|
| 0 | NULL | — |
| 1 Move | 0x4e6d1 | 0x231 = Unconscious\|Paralysed\|Fleeing\|Asleep |
| 2 Melee | 0x4e9f5 | 0x331 = + Panicking |
| 3 Ranged | 0x4ef8b | 0x331 |
| 4 Retreat | 0x4f5d3 | 0x211 = Unconscious\|Paralysed\|Asleep (Panicking MAY retreat) |
| 5 Cast | 0x4f6a2 | 0xF31 = + Panicking\|Insane\|**Irritated(0x800)** — Irritation blocks casting |
| 6 UseItem | 0x4f829 | 0x500 = Panicking\|Insane |

Both attack planners (0x4e9f5 / 0x4ef8b) run **`byte sheet[+0x11]` strikes per action**
(set by ApplyLevelUp `clamp(level/w[sheet+0xE2],1,4)`), **doubled when flag+4 bit0 (Hurry)**;
each strike schedules deferred ctx 0x13e1be/0x13e1d6 (completion cb 0x4eac1/0x4f057) and the
loop aborts when the stop flag `word[0x15f140]` is set (set on: target tile empty at strike
time → snd 453, weapon broke, out-of-ammo-reserve, dead evade branch).

---

## 1. Move + path-finding (CONFIRMED)

**There is no BFS/A*.** `fcn.00051b51` is NOT pathfinding — it is the party Move
**destination-marker animation** (vtable_2 row 0 sub 4: gfx 0x28/#48, anchor 50/50, 150 %
scale, 1 frame per engine frame). `fcn.00053871` is the generic **anim-slot allocator**
(1000 × 58 B pool at 0x168a84) — unrelated to routing.

Three path producers, all writing {count at +0x52, tile list at +0x54}:

1. **Player (UI fn at 0x56c1a)**: candidates = `fcn.0004d85b` (the known Chebyshev flood:
   range clamp(Speed/30,1,3), team row masks, only the DESTINATION must be empty)
   `& ~fcn.0004db61` (tiles already claimed by queued same-team moves). If empty → sound 442,
   no action. Else interactive tile picker `fcn.0005772a(441, render cb 0x578f3, valid,
   claimed)`; on pick queue **action 1 with count=1, single waypoint**. The player never gets
   a multi-tile path.
2. **AI approach — `fcn.00050887(self, targetCol)`** (also reached when the melee-preferring
   AI finds no adjacent enemy: attack intent converts to a move):
   ```
   steps = clamp(Speed/30, 1, 3); cand = moveMask & ~claimed
   repeat steps times (party row direction mirrored):
       if row >= 3 (monster already at contact rows):
            col = StepTowardCol(col, row, targetCol, cand)        // fcn.00050a2c, sideways only
       else if cand has (col, row+1):  row++                      // straight at the party
       else if (c2 = StepTowardCol(col, row+1, targetCol, cand)) != col:  col = c2; row++   // diagonal
       else if (c2 = StepTowardCol(col, row,   targetCol, cand)) != col:  col = c2          // sideways
       else break                                                  // stuck: PARTIAL path kept
       cand &= ~bit(previousTile); append waypoint
   if any waypoint: action = 1, count, list
   ```
   `fcn.00050a2c(col,row,targetCol,mask)` steps one column toward targetCol if that tile bit
   is free; with no target (0xFFFF) it tries left/right in 50/50 random order
   (`fcn.00050e84` left / `fcn.00050ee9` right). **Greedy single pass — no backtracking; a
   blocked funnel simply truncates the move.**
3. **Insane random move `fcn.0004c256`**: one uniformly random bit of the candidate mask
   (`fcn.000512e7`), count=1. Panic flee paths use the same greedy stepper inline
   (see item 4).

**Execution — vtable_1 kind 1 = 0x4e6d1 (CONFIRMED):**
- Party (kind 1): play sound 444; take the **last** waypoint only; if that tile is occupied
  *now* → sound 443 and the move is **cancelled entirely** (no partial); else clear old grid
  cell, write self into the new one, update +0x42/+0x44 (state teleport — the only visual is
  the destination marker, party sprites are never drawn).
- Monster: play sound 444; **re-validate the waypoints in order and truncate at the first
  occupied tile → PARTIAL move**; if ≥1 tile remains, play vtable_2 sub 3 **WalkPath
  (0x52019)** with the validated list — per waypoint the sprite is linearly interpolated
  over N engine frames (N = Move-anim length, StepAnim every engine frame, flag+4 bit3 set);
  then the grid is committed to the **last** validated waypoint.

**No route exists** → the action is simply never queued (AI tries its other options /
player hears 442). Traps never block (known); occupancy is checked only at the destination
when planning and re-checked as above at execution.

*Remake implications:* replace `Battle.MoveCombatant` teleport with: candidate mask & claim
set at queue time; greedy 3-branch stepper for AI approach/flee; monster partial-move
truncation vs party all-or-nothing cancel (sound 443); WalkPath lerp at engine-frame rate,
N = Move anim length per tile.

## 2. Ranged attack: validation, ammo, reach — and the melee reach rule (CONFIRMED)

**(a) Validation.** The weapon is always **equipment slot 4 (right hand)** at execution —
`fcn.0004f057` hardcodes slot 4 and checks `item[+1] typeid == 6`; if the right hand is
empty/not a LongRangeWeapon the strike still happens **bare-handed** (weapon id 0, sound 455
instead of 446, LongRange skill roll + normal damage math). Planning side:
- Player Attack command (UI 0x56b00..): `slot = FindEquippedItemOfType(sheet, 6)`
  (`fcn.00049cc1`); if found, the attack is FORCED ranged — usability `fcn.000513bf`
  fails → sound 433 and **no melee fallback**; only when NO typeid-6 item is equipped does
  the UI offer melee.
- AI ranged-preferring chooser `fcn.00050488` uses the same pair, else auto-equips from
  backpack (`fcn.00050f52`, known).

**`fcn.000513bf(self, slot)` = RangedUsable**: weapon typeid must be 6; if weapon
`ammoType (item+0x0E) == 0` → **usable immediately (intrinsic ammo)**; else scan **all 9
equipment slots** for a non-broken (`slotFlags & 2` clear) item with `typeid == 7` and
`item[+0x0E] == weapon.ammoType`. No quantity check beyond presence. **No line-of-sight or
row term exists anywhere in it** (the old "ammo/line" guess is half wrong).

**(b) Ammo consumption — `fcn.0004f3e2(self, slot)`, called ONCE PER STRIKE** (so up to
`sheet[+0x11]`×Hurry times per action), invoked right after the projectile anim launches and
**before the to-hit roll — misses burn ammo**:
```
if weapon.ammoType != 0 && ammoSlot(sheet+0x304, = equip slot 6) not empty
   && ammoItem.typeid == 7 && ammoItem.ammoType == weapon.ammoType:
       ConsumeOne(slot 6)                       // fcn.0004f4da
else if weapon flags (item+0x1B) & 0x10:        // consume-on-use weapons (thrown)
       ConsumeOne(weapon slot 4)
// (intrinsic-ammo weapons without flag 0x10 consume nothing)
```
`fcn.0004f4da` = **refill-from-backpack**: scans the 24 backpack slots (+0x31C) for the same
item id; if found, decrements the BACKPACK stack (equipped stack stays topped up); if the
backpack has none, decrements the equipped stack itself, plays **sound 454** and sets
`0x15f140 = 1` (no further strikes this action). Note the asymmetry: usability accepts
matching ammo in ANY equipment slot, but consumption only draws from the dedicated ammo
slot sheet+0x304 — ammo parked elsewhere validates but is never consumed (edge case, the
UI normally forces ammo into that slot).

**(c) Reach / row restrictions:**
- **Ranged: NONE.** Target mask `fcn.0004d68c` = every tile occupied by the opposing kind,
  whole 6×5 grid. Used identically by player UI (0x56b44) and AI. Back row shoots fine; no
  LoS, no friendly-blocking.
- **MELEE HAS A REACH RULE WE MISSED**: `fcn.0004d512` → `fcn.0004d55e(col,row,kind)` =
  the **8 Chebyshev-adjacent tiles only** (direction table 0x134bf4), tile must hold a
  combatant of the other kind. This mask gates the player UI melee picker (0x56ba0), the
  AI (`fcn.00050756`) and the insane handler. When the AI wants melee but the mask is
  empty it queues an approach Move instead (item 1). The melee EXECUTOR (0x4eac1) never
  re-checks distance — it strikes whatever occupies +0x52 (empty → sound 453) — so
  adjacency is purely a planning-time rule.
- `0x50b51` (no recognized callers; INFERRED tactical-screen highlight helper) computes the
  union of adjacency masks over every reachable move destination — it is NOT used by the
  action queueing path above.

*Remake implications:* restrict melee to Chebyshev-1 targets at queue time (no recheck at
strike); ranged needs typeid-6 in the right hand + matching non-broken typeid-7 ammo
(ammoType 0 = self-sufficient); consume 1 ammo per strike before the to-hit roll, backpack
refill first, sound 454 + stop multi-strike when reserve is gone; flag 0x10 weapons consume
themselves; with a usable bow equipped the Attack command must be ranged-only.

## 3. "Summon" — fcn.0004f6a2: THERE IS NO SUMMON; kind 5 = CastSpell (CONFIRMED)

`fcn.0004f6a2` (action kind 5, 391 B) is the **spell-cast executor**:

1. Reads the queued cast block at +0x52.. (school, number, target mask, mode — filled by the
   player cast UI via `fcn.0005ed68` @0x56cee, or the monster magic AI `fcn.0004fd56`).
2. Prints "X casts Y" (caster name `fcn.000364fa`, spell name `fcn.00060b30` = ptr table
   `0x15db4c[(school*30+number)·4]`, format string handle 0x15d994, formatter 0x81f42).
3. Plays vtable_2 sub 7 (StartAnim(Magic) + WaitAnimEnd).
4. `fcn.0005eea8(self)`: memcpy 28 B from self+0x52 into the known cast globals
   (`0x1775a0` school, `0x1775a2` number, `0x1775aa` caster sheet, `0x1775ae` caster ptr,
   `0x1775b4` target-tile bitmask, `0x1775b8` mode) and schedules deferred ctx `0x13e9da`,
   which runs the known cast core / SpellDispatch chain (fcn.0005fdf7 → 0x5ecda).

**The "summon-looking" prologue is dead code**: it is gated on `word[0x13e7dc] != 0`, a
zero-initialised global with exactly **one reference in the whole binary (this read; byte
scan `/x dce71300` confirms no writer)**. If it could run it would: find the FIRST live
monster, copy the caster's 28-byte cast block into it, set the target mask to the caster's
own tile, credit the caster's sheet, and have that monster perform the cast — a leftover
debug/"cast by proxy" path. No monster id is ever copied, no combatant slot is ever
created. (0x13e7dc sits immediately before the per-school spell-count table at 0x13e7de,
supporting "vestigial config word".)

Monster magic AI targeting (`fcn.0004fd56` tail, for completeness): spell picked from the
known-spell loop, then by SPELLDAT target byte (`fcn.00060917` & 0x38): 0x08 single →
random enemy tile (fcn.000501ff), 0x10 row → the FULLER of party rows 3/4 (per-row count
fcn.00050175), 0x20 all → mask 0x3FFC0000 (all 12 party tiles); mode word = 3; cancel mask
0xF31 applies at execution.

*Remake implications:* delete the Summon TODO — map action kind 5 to the existing spell
pipeline; nothing else to implement.

## 4. Insane / Panicking / Fleeing turn behaviour (CONFIRMED)

**Wiring (round controller `fcn.0004b032`):** each round first `fcn.0004bdf2` runs the
normal behaviour AI (`fcn.0004fb63`) for every monster WITHOUT flag+4 bit2, then
`fcn.0004be7f` runs the status gate `fcn.0004bf77` for every party member AND monster WITH
flag+4 bit2. Bit2 is set by `fcn.0004b2ed` (runs after every turn) when
`conds & 0x500 (Panicking|Insane)` — i.e. **panicking/insane party members are auto-piloted
by the same code as monsters**; the same sweep **removes Unconscious combatants from the
battle** (fcn.0004b49e) and, for monsters, clears bit2 again when the condition is gone.
If the party LEADER is flagged/absent, `fcn.000354ac` is called (leader handover, INFERRED).

**Gate `fcn.0004bf77`** (priority order): `conds & 0x200` Asleep → **turn skipped** (action
stays 0); `& 0x100` Panicking → `fcn.0004bff7`; `& 0x400` Insane → `fcn.0004c1e4`.

**Insane — `fcn.0004c1e4`:** `rand()%8 >= 4` (exact 50/50) → try **RandomMove** then
**Attack**, else Attack then RandomMove (second only if the first queued nothing).
- RandomMove `fcn.0004c256`: uniform random bit of (moveMask & ~claimed), 1-tile Move.
- Attack `fcn.0004c2cd`: prefers the equipped LongRangeWeapon (slot via fcn.00049cc1 +
  ammo check fcn.000513bf) → else CloseRangeWeapon (bare hands allowed). **Target selection
  spoofs the combatant's own kind to 3** so `fcn.0004d68c`/`fcn.0004d512` treat EVERY
  combatant as an enemy: the target is a **uniformly random occupied tile of EITHER side**
  (melee: among the 8 adjacent tiles; ranged: the whole grid — the mask even includes the
  caster's own tile, so an insane archer can shoot itself). Not a 50/50 side split —
  uniform over valid target tiles.

**Panicking — forced flight toward the OWN back edge:**
- Party `fcn.0004bff7`: if already at **row 4** and `!(conds & 0x211)` → queue **Retreat
  (kind 4)**. Else build a greedy path toward row 4 — per step: straight (row+1, same col)
  if free, else diagonal into row+1 via `fcn.00050dbb` (random left/right order), else
  sideways in the same row, else stop; up to clamp(Speed/30,1,3) steps → queue Move.
- Monster: routed to `fcn.00050c15` — exact mirror toward **row 0** (Retreat when at row 0
  and not 0x211-blocked).

**Fleeing / leaving the battle:**
- Retreat executor `0x4f5d3` (kind 4): only acts when the combatant is AT its edge row
  (party row 4 / monster row 0), else no-op. Plays sound 445, `SetCondition(5 Fleeing)`,
  then `fcn.0004b49e` = **RemoveFromBattle** (grid cell cleared, col/row = 0xFFFF,
  flag+4 |= 0x10, action 0, hide via fcn.0004bda2). **Monsters additionally add their XP
  reward `u16 sheet[+0x20]` to the battle XP pool 0x15f104** (a fled monster still pays
  XP!) and play the Flee animation (vtable_2 sub 6 @0x52243: Move anim + 20 logic ticks of
  z += 50/tick running into the distance, then the sprite is freed).
- So the combatant **leaves the battle the same round its Retreat executes**; the
  preceding rounds are spent on the forced moves. There is no extra "stand at the edge one
  more turn" rule beyond initiative timing.
- Battle outcome — `fcn.0004b256` (checked after every single turn): with ≥1 "active"
  party combatant on the grid (kind 1, `!(conds & 0x401 Unconscious|Insane)`) and 0 living
  monsters → `0x15f112 = 1` (victory); with 0 active party: ≥1 party member having the
  Fleeing condition (`fcn.0004e640`) → `= 2` (party escaped), else `= 3` (defeat);
  `= 4` reserved for scripted aborts. The round loop stops immediately.

**Monsters DO flee voluntarily** — not via Panicking but via the per-monster behaviour
predicate (item 5): when it returns "stop fighting", the turn goes to the same
`fcn.00050c15` flee-mover. Class-bit 0x80 creatures (the frost/death-immune class group,
`fcn.00036701`) **never** flee.

*Remake implications:* skip turn on Asleep; auto-pilot Panicking (greedy path to own back
row, Retreat at the edge, leave battle on execution + Fleeing condition) and Insane (50/50
move-vs-attack, uniform target over BOTH sides, ranged preferred); monsters award XP even
when they flee; battle ends with "fled" result when the last active party member is gone but
someone fled; honor the vtable cancel masks (e.g. falling Asleep cancels the queued action).

## 5. Fleeing / morale — sheet+0x0F (CONFIRMED)

Exactly **one** combat read of the Morale byte: `0x515a5` inside
**`fcn.00051506(self)` = morale check**, used as the "keep fighting?" predicate:

```
deadPct  = 100 - livingMonsters()*100 / word[0x15f118]    // % of monster side dead
lostPct  = 100 - LP*100 / MaxLP                            // % of own LP lost
threat   = (deadPct + lostPct) / 2
return threat < byte sheet[+0x0F]          // 1 = keep fighting, 0 = FLEE
```
**Flee when `(deadSidePct + lostLpPct)/2 >= Morale`.** Morale 100 ⇒ practically never
flees; Morale 0 ⇒ flees on its first turn.

**Hook-up:** monster turn AI `fcn.0004fb63` (called per monster by `fcn.0004bdf2`):
```
row = 0x13e1f0 + (combatant[+0x12]-1)*16          // 10 rows × 4 fn ptrs; +0x12 = MONCHAR byte sheet+0x0C
fight = (classBits & 0x80) ? 1                     // demon/undead class group never flees
      : row.fn0 ? row.fn0(self) : 1
if (!fight)  { FleeMove(self); return; }           // fcn.00050c15 → row 0 → Retreat → leaves, XP credited
refresh persistent target (+0x14) via row.fn1 when current is gone/removed
fcn.0004fc99(self)                                  // known weighted magic/melee/ranged dispatcher
```

**Behaviour-row table 0x13e1f0 (strategy id 1..9 → {fn0 fight-predicate, fn1 target-picker,
fn2 on-hit rider, fn3=NULL}):**

| id | fn0 | meaning |
|---|---|---|
| 1 | 0x51506 | morale formula above |
| 2 | 0x515db | fights while NO monster has died yet (living == total), then flees |
| 3 | 0x51506 + fn1 0x51717 | morale + custom target picker |
| 4/5/6 | 0x51506 + fn2 0x518c7/0x51910/0x51991 | morale + on-hit rider |
| 7 | 0x51620 | fights while livingMonsters ≥ livingParty AND LP% ≥ (row+1)·25 (deeper rows give up sooner) |
| 8 | 0x516b2 | spellcaster: with SP left, "fights" only while in rows 0-1 (retreat-moves back when it strays to rows ≥2); with no SP falls back to the morale formula |
| 9 | 0x51506 + fn1 0x517e5 + fn2 0x51a2e | morale + picker + rider |

fn2 is invoked from BOTH attack callbacks (0x4f368 / melee equivalent) after damage lands,
monster attackers only — INFERRED to be the poison/disease-on-hit style riders (bodies not
traced this session). Party members have no fn0 — they only flee via Panicking/Retreat.

*Remake implications:* per-monster flee check at the start of its AI turn:
`(deadMonsterPct + lostLpPct)/2 >= Morale(sheet+0x0F)` (variants per behaviour byte
sheet+0x0C as above; class-bit 0x80 immune), then reuse the panic flee-move path.

## 6. Battle-view hover bob (CONFIRMED)

On top of the static hover offset (slot.y = −(i16)sheet[0x4B8] ·100, Unk152 — known),
there IS a periodic bob, selected by **render class = MONCHAR byte sheet+0x0D**
(→ combatant +0x10 → vtable_2 row; write site found in setup `fcn.0004c937` @0x4cb42,
0 defaults to 1 — closes the old "row selector not located" item):

| class | Show variant | oscillators attached |
|---|---|---|
| 1 ground | 0x51e0d | none — ground monsters stand perfectly still when idle |
| 2 ghostly | 0x526e1 | render kind 8 (translucent) + sine on **worldY** + sine on **worldX** |
| 3 flying | 0x5283f | sine on **worldY** only |
| 4 sway variant | 0x52a23 | sine on **worldY** + sine on **worldX** (opaque) |

Each oscillator is a behaviour-queue record (cb `0x54eeb`, run every 20 Hz logic tick),
created with independently rolled parameters:

```
period    = rand()%40 + 60         // 60..99 logic ticks = 3.00..4.95 s
phase0    = rand()% period         // random start phase (monsters bob out of sync)
amplitude = rand()%200 + 400       // x100 fixed point = 4.00..5.99 world units (tile = 64)
per tick: v = round(sin(2π·phase/period) · amplitude)      // true FPU sine (π const @0x10bcf), no table
          coord += v − vPrev;  vPrev = v;  phase = (phase+1) % period
```

It is a pure sine (applied incrementally as deltas, asserts at comobs.c:1781), peak ±4-6
world units ≈ ±6-9 % of a tile; on screen that is `amp · 148/(worldZ+148)` px ≈ 2.1 px
(row 0) to 4.2 px (row 2) of vertical drift. The X sway of classes 2/4 uses the same
distribution. The ground shadow does NOT bob — its sync behaviour (0x55265) copies x/z and
frame only, y stays 0. The flying-class Die handler (0x528fc) clones the slot (dropping the
oscillators) and applies the known gravity fall (vy −= 0.16/tick², floor at y = −40).

*Remake implications:* idle monsters: no animation cycling, but classes 2/3/4 get a sine
bob: amplitude uniform 4-6 world units (out of 64/tile), period uniform 3.0-4.95 s, random
phase, 20 Hz update; classes 2 and 4 add an equal X sway; class 2 is drawn translucent;
shadows stay glued to the ground.

---

## New key globals / functions (this batch)

| Addr | Meaning |
|---|---|
| `fcn.0004bdf2` / `fcn.0004be7f` | per-round: normal monster AI loop / panic-insane override loop (flag+4 bit2) |
| `fcn.0004bf77` | status gate: Asleep skip → Panic `fcn.0004bff7` → Insane `fcn.0004c1e4` |
| `fcn.0004bff7` / `fcn.00050c15` | panic flee-move party→row 4 / monster→row 0 (greedy stepper, Retreat at edge) |
| `fcn.0004c2cd` / `fcn.0004c256` | insane attack (kind-3 spoof = both sides targetable) / insane random 1-tile move |
| `fcn.0004d512`→`fcn.0004d55e` | **melee target mask: 8 adjacent tiles**, opposing kind only (dir table 0x134bf4) |
| `fcn.0004d68c` | ranged target mask: every enemy-occupied tile, no range/LoS limit |
| `fcn.00050887` / `fcn.00050a2c` | AI greedy approach-path builder / step-1-column-toward-target helper |
| `fcn.00050dbb` / `fcn.00050e84` / `fcn.00050ee9` | random-order sideways step / try-left / try-right (bit tests) |
| `0x4e6d1` Move executor | party: last-waypoint only, cancel+snd 443 if occupied; monster: truncate at first blocked waypoint (partial move), WalkPath anim |
| `0x56c1a` / `0x56b00..` | player Move UI (single tile via picker `fcn.0005772a`, snd 442 none) / player Attack UI (ranged-priority; melee = adjacent mask; snds 433/435/436) |
| `fcn.000513bf` | RangedUsable: typeid 6 + (ammoType 0 intrinsic OR matching non-broken typeid-7 in any equip slot) |
| `fcn.0004f3e2` / `fcn.0004f4da` | consume ammo per strike (ammo slot sheet+0x304 only; weapon self-consume flag item+0x1B & 0x10) / decrement with backpack refill (snd 454 + stop flag when reserve empty) |
| `0x15f140` | stop-multi-strike flag (also set by no-target 453, weapon break) |
| `byte sheet[+0x11]` | strikes per attack action (1..4), ×2 under Hurry |
| `fcn.0004f6a2` + `fcn.0005eea8` + `fcn.00060b30` | CastSpell executor (kind 5) / copy cast block to 0x1775a0 globals / spell-name lookup 0x15db4c |
| `0x13e7dc` | dead global gating the never-used "first monster casts by proxy" branch (single xref) |
| `fcn.0004fb63` / `fcn.0004faeb` | monster turn AI (flee predicate → target refresh → fcn.0004fc99) / behaviour-row lookup |
| `0x13e1f0` | behaviour table 10×16 B {fight-predicate, target-picker, on-hit rider, NULL}; id = MONCHAR sheet+0x0C (combatant+0x12) |
| `fcn.00051506` | **morale check: flee when (deadMonsterPct + lostLpPct)/2 ≥ byte sheet[+0x0F]** |
| `0x515db` / `0x51620` / `0x516b2` | predicate variants: no-deaths-yet / outnumbered+LP%≥(row+1)·25 / caster stay-back |
| `fcn.0004b49e` | RemoveFromBattle (grid clear, col/row 0xFFFF, flag+4\|=0x10) |
| `0x4f5d3` Retreat executor | edge-row only; Fleeing cond; monster flee still adds sheet[+0x20] XP to 0x15f104 + vtable_2 sub 6 run-away anim |
| `fcn.0004b256` / `0x15f112` | battle-end check after every turn / outcome 1 victory, 2 party-fled, 3 defeat, 4 scripted |
| `fcn.0004e640` / `fcn.0004e54d` | count party with Fleeing cond / count active party (! conds & 0x401) |
| `0x13e194` | vtable_1 cancel-mask words (per-kind condition masks, see table) |
| `0x54eeb` | sine oscillator behaviour: ±(4-6) world units, 60-99 ticks, random phase, modes x/y/z/scales |
| MONCHAR sheet+0x0C / +0x0D / +0x0F | behaviour-strategy id / render class (vtable_2 row) / Morale 0..100 |

## Corrections to docs/re/RE_COMBAT.md claims (do not edit that file from this batch)

- "fcn.0004f6a2 = Summon" → it is the **CastSpell** executor; Albion has no summon action.
- "fcn.000513bf (ammo/line)" → ammo only; ranged attacks have **no** line/row restriction.
- "Insane → 50/50 random Move-vs-Attack" stands, but target choice is uniform over ALL
  occupied tiles of both sides (kind-spoof), not a coin flip between sides.
- "fcn.0004c2cd = AI school choice" (punch-list KNOWN list) → it is the insane ATTACK
  chooser; the magic AI school logic lives in fcn.0004fd56.
- vtable_2 row selector (open item) → MONCHAR byte sheet+0x0D, written in fcn.0004c937.
- Behaviour ids: MONCHAR sheet+0x0C indexes 0x13e1f0 — this byte, not Unk37, is the real
  "AI strategy" field.
