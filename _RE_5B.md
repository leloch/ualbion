# RE batch 5B (2026-06-12): SPELLDAT records, target areas, GoddessWrath picker, item-cast strength, school 4/6, line-spell insurance

> Cluster: SPELLDAT record semantics + spell dispatcher region (~0x9f000-0xa9000, cast core
> ~0x5fd00-0x60900). All radare2 against `albion_aaa`, plus a direct parse of
> `ALBION/CD/XLDLIBS/SPELLDAT.DAT` (1050 bytes = 7 schools x 30 spells x 5 bytes, exact).
> CONFIRMED = read from disassembly with consistent cross-references; INFERRED = strong
> pattern, one detail unverified. This file is owned by agent 5B; companion to _RE_COMBAT.md.

## 1. SPELLDAT record layout + target-area enumeration (CONFIRMED)

### 1.1 Record layout (5 bytes) — engine accessors

| Offset | Field | Engine accessor | Notes |
|---|---|---|---|
| +0 | Environments | `fcn.0006099b(school, number)` | bitmask, see 1.2 |
| +1 | SP cost | `fcn.00060a1e` | (already known) |
| +2 | LevelRequirement | `fcn.00060aac` | (already known) |
| +3 | Targets | `fcn.00060917(school, number)` | bitmask, see 1.3 |
| +4 | "Unused" | **no accessor exists** | non-zero only in school 6 (values 1/2/4); never read by any code — truly dead |

All accessors lock the SPELLDAT buffer handle `0x177594` and index `((school*30 + number) - 1) * 5`.

**Per-school spell-count table `0x13e7de` (7 u16): {21, 11, 10, 15, 0, 4, 0}.**
`fcn.00061b26(school)` = IsSchoolUsable: `school < 7 && count[school] != 0 && fnTable[0x13e930 + school*4] != NULL`.
`fcn.00061b96(school, number)` = IsSpellImplemented: IsSchoolUsable && 1 <= number <= 30 && fn != NULL.
Schools 4 and 6 fail both tests permanently (count 0 **and** NULL table) — see §4.

### 1.2 Environments byte semantics (CONFIRMED)

`fcn.00060554` returns the **current environment index**: 5 when in combat
(`word[0x13e142] != 0`), else the map RestMode `word[0x147990]` = `(mapFlags & 0xC) >> 2`.
The spell-list UI (`fcn.00060b98`) and monster AI test `envByte & (1 << envIndex)`:

| Bit | Value | Environment |
|---|---|---|
| 0 | 0x01 | City (RestMode 0) |
| 1 | 0x02 | Dungeon (RestMode 1) |
| 2 | 0x04 | Wilderness (RestMode 2) |
| 3 | 0x08 | Interior (RestMode 3) |
| 4 | 0x10 | never produced by fcn.00060554 — unused ("Camp" per the old asm comment) |
| 5 | 0x20 | Combat |
| 6 | 0x40 | never produced — unused (appears only in dead school-5 slots 6-9 / school-6 records) |

=> UAlbion `SpellEnvironments` names are mis-ordered (`Indoors/Outdoors/Dungeon/Inventory`
should be `City/Dungeon/Wilderness/Interior`); the asm comment block already in that file is
the correct mapping. The monster AI requires `env & 0x20` (combat) — see §4.

### 1.3 Targets byte semantics + cast-time enumeration (CONFIRMED)

Two-stage system. Stage 1 — **`fcn.0005ef24` = BuildTargetMask** (called by both cast entries
`fcn.0005eb7d` menu / `fcn.0005ed68` combat): switches on the Targets byte and produces
- a 30-bit **tile mask** -> `dword[0x1775b4]` (bit = row*6 + col, rows 0-4, cols 0-5),
- a **filter mode** -> `word[0x1775b8]`,
- (non-combat) a 6-bit **party-slot mask** -> `word[0x1775b2]`.

Stage 2 — **`fcn.0005fb21` = ForEachTargetTile(M, continuation)**: iterates rows 0..4 x cols
0..5 in row-major order; for each masked tile reads occupant `grid[0x176d74 + row*84 + col*14]`
and applies the mode filter, then calls `continuation(tilePtr, dx=col, bx=row, cx=M)`:

| mode | Filter | Used by |
|---|---|---|
| 1 | tile occupied (any team) | **no writer found — unused/vestigial** |
| 2 | occupied && occupant.kind == caster.kind (allies) | party-member/party-area targets |
| 3 | occupied && occupant.kind != caster.kind (enemies) | monster targets (and monster-AI casts, ctx +0x18 = 3) |
| 4 | every masked tile, occupied or not | traps/mines (custom-area fns set it) |

If **zero** continuations fired: fizzle sound 698 and the action is NOT consumed (`+0x4E` stays).

**Per-Targets-bit behaviour in combat** (`word[0x13e142] != 0`):

| Targets | Meaning | Mask construction | Mode |
|---|---|---|---|
| 0x01 | one party member | candidates = `fcn.0004dc4b` (tiles rows 3-4 with kind==1 occupant); UI tile pick `fcn.0005772a(0x57, cursors 0x57825, mask)`; mask = 1<<tile | 2 |
| 0x02 | one party ROW | UI row pick over 0x3FFC0000; mask = 6 bits of picked row | 2 (no spell in data uses it) |
| 0x04 | whole party | mask = **0x3FFC0000** (rows 3-4, all 12 party tiles), no pick | 2 |
| 0x08 | one monster | candidates = `fcn.0004dd23` (tiles rows 0-3 with kind==2 occupant); UI pick (0x58); mask = 1<<tile | 3 |
| 0x10 | row of monsters | UI **row** pick (0x59, row cursor 0x57a5e) over **0x00FFFFFF** (rows 0-3); `rowStart = tile - tile%6`; mask = the **entire 6-tile grid row** of the picked tile | 3 |
| 0x20 | all monsters | mask = **0x00FFFFFF** (rows 0-3, all 24 tiles), no pick | 3 |
| 0x40 | an inventory item | party-member picker `fcn.0007e1d2` + item picker `fcn.0007ec65`; mask = 1<<(member's tile); picked item slot -> `word[0x1775a6]` | 2 |
| 0x80 | map tile / custom area | per-spell custom fn from table `0x13e7f4` (see below) | set by the fn (4) |

Non-combat branch: 0x01 -> member picker, partyMask = 1<<member; 0x04 -> partyMask = 0x7E
(slots 1-6); 0x40 -> member+item picker; 0x80 -> custom fn from the **map-mode table
`0x13e7ec` which contains only the 0xFFFF terminator — no map-mode area spells exist**;
all other bits do nothing outside combat.

### 1.4 The targets=0x80 custom-area table at `0x13e7f4` (CONFIRMED)

8-byte entries {u16 school, u16 number, u32 fn}, 0xFFFF/0xFFFF-terminated:

| School.slot | Global spell | Area fn |
|---|---|---|
| 0.14 ThornTrap, 0.15 RemoveTrapDK, 3.7 LightningTrap(97), 3.9 LightningMine(99), 3.15 RemoveTrapKK(105) | | **`0x5f597`** |
| 3.8 BigLightningTrap(98), 3.10 BigLightningMine(100) | | **`0x5f60b`** |
| 1.4 Teleporter(34) | | `0x5f6c5` |

- **`0x5f597` (normal)**: UI single-tile pick (prompt id 0x85) over mask 0xFFFFFF (rows 0-3,
  any tile, occupied or not); returns `1 << tile`; sets mode = 4.
- **`0x5f60b` (Big)**: UI **row** pick (0x59) over 0xFFFFFF; `rowStart = tile - tile%6`;
  returns **all 6 bits of the picked row**; mode = 4.
  => **"Big" trap/mine = one whole 6-tile grid row, not a blob/cross.** The K constants are
  shared with the non-Big spells (30 trap / 42 mine) because the continuations are shared
  (0xa659b / 0xa7038) — only the placement area differs.
- **`0x5f6c5` (Teleporter)**: tile pick (prompt 0x1ac=428) over mask **0x3FFFFFFF (all 30
  tiles incl. party rows)**; then re-derives row/col and validates against the grid
  (caster relocation, not a damage area).

### 1.5 Exact tile-enumeration pseudocode per pattern

```c
// (a) RowOfMonsters (targets 0x10 — FrostCrystal 7, BlindingRay 11, BanishDemons 63,
//     Shock 69, FireRain 93, Thunderbolt 94):
row   = PlayerPicksTileWithRowCursor(rows 0..3);      // monster AI: current target's row,
                                                      // or the party row (3 vs 4) with MORE members (fcn.00050175)
mask  = bits [row*6 .. row*6+5];                      // ALL 6 columns of that grid row
mode  = 3;                                            // enemies only
for (r = 0; r < 5; r++) for (c = 0; c < 6; c++)       // row-major
    if (mask & (1 << (r*6+c)) && grid[r][c] && grid[r][c]->kind != caster->kind)
        continuation(grid[r][c], c, r, M);            // per target: own gate + own magnitude

// (b) AllMonsters (targets 0x20 — FrostAvalanche 8, BlindingStorm 12, GoddessWrath 39,
//     DemonExodus 64, Panic 70, FireHail 95, Thunderstorm 96):
mask = 0x00FFFFFF; mode = 3;                          // rows 0-3; party caster
// (monster caster: mask = 0x3FFC0000, rows 3-4)
// -> every living enemy gets ONE continuation call; each runs the deterministic gate
//    fcn.000601a6 itself (margin differs per target's MagicResist), then max(1, margin*K/100).
//    No randomness anywhere (exception: GoddessWrath ignores the mask path - see §2).

// (c) Trap/mine area (targets 0x80):
tile = PlayerPicksAnyTile(rows 0..3, occupied or empty);
mask = isBig ? bits[rowStart .. rowStart+5]           // Big: whole picked row (6 tiles)
             : 1 << tile;                             // normal: single tile
mode = 4;                                             // continuation runs on EVERY masked tile,
                                                      // occupied or not (places trap objects)
```

**Remake implications:** UAlbion `SpellTargets` is mostly named right; `DeadParty` (0x04)
is actually **whole living party**; 0x40 = item-targeting; 0x02 = party-row (unused).
RowOfMonsters = full 6-tile row of the picked tile; AllMonsters = whole enemy area with one
deterministic gate per enemy; Big traps occupy a full row of 6 tiles, normal traps 1 tile.
A cast that affects nobody must not consume the combatant's action and should play fizzle 698.

## 2. GoddessWrath random picker `fcn.0005f7ec` (CONFIRMED)

Called by handler 0xa1134 with `eax = M`. Output: victim list at `0x196fdc` (dword ptrs),
count at `word[0x19703c]`.

```c
void PickGoddessWrathVictims(int M) {
    word[0x19703c] = 0;
    living   = fcn.0004e493();                  // grid scan: count occupants with kind == 2
    kills    = max(1, living * M / 100);        // M = mastery multiplier 1..100
    picked   = 0;                               // 32-bit ordinal bitmask
    for (i = 0; i < kills; i++) {
        do { r = rand() % living; }             // Watcom rand fcn.00094326, UNIFORM
        while (picked & (1 << r));              // rejection re-roll on duplicates
        picked |= 1 << r;
    }
    // materialise: scan grid row-major (row 0..4, col 0..5); ordinal = running index of
    // monsters (kind==2) encountered; if picked bit set -> append combatant ptr to 0x196fdc
    ord = 0;
    for (r = 0; r < 5; r++) for (c = 0; c < 6; c++) {
        m = grid[0x176d74 + r*84 + c*14];
        if (!m || m->kind != 2) continue;
        if (picked & (1 << ord)) list[word[0x19703c]++] = m;
        ord++;
    }
}
```

- Selection is **uniform without replacement** over living monsters (sample-by-rejection).
- The output list is in **grid order** (row-major), not pick order — the kill animations
  therefore sweep the field front-to-back regardless of RNG order.
- The handler then runs each victim through the gate (`fcn.000601a6` @ 0xa1340, incl mask
  0xFFFF) before the instant kill `fcn.0004e247` — a high-MagicResist monster can still
  resist after being picked (no re-pick of a substitute).
- `fcn.0004e493` counts **monsters present on the grid** (dead ones are removed from the
  grid, so this equals living monsters).

**Remake implications:** implement as `count = max(1, living*M/100)`, repeated
`rng.Generate(living)` with re-roll on duplicates (to match RNG stream), victims applied in
grid order, each individually gated (margin = M − EffMagicResist must be > 0).

## 3. Per-item cast strength — **M = 50, hard-coded** (CONFIRMED)

Both cast entries (`fcn.0005eb7d` menu / `fcn.0005ed68` combat) take `(combatant, slotIdx)`
with **slotIdx = 0xFFFF for spellbook casts**; the slot is stored at `word[0x1775a4]`
(combatant ctx mirror: `+0x56`). For item casts the entry resolves the 6-byte inventory slot
(equip `sheet+0x2E6`, backpack `sheet+0x31C`), looks up ITEMLIST via `fcn.0004a521` and
copies the item's **spell class (`item+0x15`) / spell number (`item+0x16`)** into the cast
globals `0x1775a0/0x1775a2` (= UAlbion `ItemData.Spell`).

Cast core `fcn.0005fdf7` first branch (@ 0x5fe0f):

```c
if (word[0x1775a4] != 0xFFFF) {        // ITEM CAST
    fcn.00060013(sheetHandle, slot);   // consume charge (below)
    return 50;                         // M = 0x32 — FLAT, @ 0x5fe34
}
// spellbook path: LP surcharge + SP cost + M = max(1,(mastery+50)/100) + mastery growth
```

- **The magnitude multiplier for every item-cast spell is the constant 50** — equivalent to
  5000/10000 mastery. No ITEMLIST byte feeds the strength; no SP is spent; no LP surcharge;
  no mastery growth. (So an item Fireball does `gate(50) -> max(1,(50−resist)*22/100)`.)
- **Charge consumption `fcn.00060013(sheet, slot)`:**
  - slot empty or charges (`slot+1`) == 0 -> do nothing;
  - if `item[+0x1B] & 0x04` -> item is **removed entirely after one use** (`fcn.00049584`,
    count−1) + inventory UI refresh;
  - else `if (charges != 0xFF) charges--`; when charges hit 0 **and** `item[+0x1B] & 0x10`
    -> item removed. (`item+0x1B` = low byte of the ITEMLIST flags word at 0x1B —
    bit2 = "single use", bit4 = "vanishes when discharged".)
- 0x1775a6 (`ctx +0x58`) is a different global: the **target** item slot for
  item-targeting spells (Targets 0x40), not the casting item.

**Remake implications:** replace the `M=100 PLACEHOLDER` with **M=50** for all item casts
(combat action 6 and inventory activation both funnel into fcn.0005fdf7); decrement charges
and honour flags bits 0x04/0x10 for destruction; never charge SP or grow mastery on item use.

## 4. School 6 (and school 4) spell list — DEAD DATA; the AI does NOT try school 6 first (CONFIRMED)

### 4.1 The records exist but cannot ever fire

SPELLDAT parse (direct file read):

- **School 4 ("Unk4")**: slots 1-4 populated (globals 121-124): env=0x20, cost=1, lvl=0,
  targets=0x08, unused=0 — identical shape to the zombie breezes.
- **School 6**: slots 1-21 populated (globals 181-201): cost=1, lvl=1, targets mostly 0x01
  (a few 0x00), env values 0x21/0x4F-0xFF (bits 6/7 set — bits no environment ever produces),
  and the **+4 "unused" byte = 1, 2 or 4** (the only school where it is non-zero).

But in the engine:
- per-school **function-table pointer array `0x13e930`**: S4 = NULL, S6 = NULL;
- per-school **count table `0x13e7de`**: S4 = 0, S6 = 0;
- => `fcn.00061b26` (IsSchoolUsable) is false, so the spell-class bit test `fcn.000371d3`
  (which calls it first) is false, the AI school loop skips them, the spell-list UI never
  shows them, and the dispatcher `0x5ecda` returns 0 on the NULL table (no effect, no cost);
- the **spell names for 121-150 and 181-210 are EMPTY strings** in SYSTEXTS (ids 323-352 /
  383-412) — even the text assets are blank;
- the +4 byte values (1/2/4) have **no reader anywhere in MAIN.EXE** (no accessor for
  SPELLDAT offset +4 exists). Probably a leftover editor/AI-classification field from
  development.

Same verdict for the school-5 tail: records 155-159 exist (cost 0 or env without the combat
bit, targets 0x40/0x01) but the school-5 fn table `0x13e920` has exactly **4 entries**
(0xa95a4/0xa96fc/0xa9854/0xa99ac = breezes 151-154); slots 5+ are unreachable (AI requires
cost != 0 and env & 0x20; UI requires fn != NULL via fcn.00061b96's 30-entry bound... the
count table caps school 5 at 4, so even the record lookups never go past slot 4).

### 4.2 The "tries school 6 FIRST" premise — explained

`fcn.0004c2cd` (insane-monster ActionB) calls **`fcn.00049cc1(sheet, 6)`** first — but
fcn.00049cc1 is `FindEquippedItemOfType` and **6 = ItemType.LongRangeWeapon** (5 =
CloseRangeWeapon is the fallback at 0x4c368). This is the same kind-2/3 mislabelling
("Cast school 5/6") that _RE_COMBAT.md's punch-list already corrected for the action vtable.
No spell-school priority exists there.

The real monster magic AI `fcn.0004fd56` picks its school by scanning **ascending 0..6**
and taking the **first** school that is usable *and* whose bit is set in the monster's
spell-class byte (`sheet+4`). Then it builds a candidate mask over spells 1..count[school]
requiring: spell known (`fcn.00037244`, sheet+0xF2 bits) AND `env & 0x20` AND `cost != 0`
AND `targets & 0x38` (offensive only — monsters never self-buff via SPELLDAT targets
0x01/0x04). Spell choice = uniform random over candidates matching the preferred targets bit
(8, then 0x10, then 0x20 when it has a current target; any of 0x38 otherwise) via
`fcn.00050360` (rand() % matchCount). Target tile mask as in §1.5; ctx mode = 3.

**Remake implications:** do not implement schools 4/6 (or zombie slots 155-159) — they are
unreachable dead data; UAlbion's `Unused1xx` enum names are exactly right. Monster casting
uses the lowest known school index, not school 6.

## 5. Insurance checks (CONFIRMED, with one surprise)

### 5a. Big fire/lightning lines: gate + margin scaling — 5 of 6 confirmed; **LightningStrike is UNGATED**

School-3 fn table `0x13e8e4` confirmed: 91 Fireball=0xa2f17, 92 LightningStrike=0xa33d2,
93 FireRain=0xa43c6, 94 Thunderbolt=0xa477a, 95 FireHail=0xa50ef, 96 Thunderstorm=0xa5aad,
97-100 traps/mines, 101 StealLife, 102 StealMagic, 103 PersProt, 104 KamulosGaze, 105 RemoveTrap.

Per-target continuations, gate call -> margin -> `max(1, margin*K/100)` -> ApplyDamage
(`fcn.0004dec9`) verified by direct disassembly:

| Spell | Gate call | K multiply | ApplyDamage | Scales on |
|---|---|---|---|---|
| Fireball (91) | 0xa32e1 | `imul edx,edx,0x16` @ 0xa3361 (x22) | (kill/damage path) | **margin** [ebp-8] |
| FireRain (93) | 0xa4708 | 0xa471d/0xa4737 (x22) | 0xa475d | **margin** [ebp-0xC] |
| Thunderbolt (94) | 0xa4e1d | 0xa4ff7/0xa5011 (x36) | 0xa5040 | **margin** [ebp-0xC] |
| FireHail (95) | 0xa5970 | 0xa599e/0xa59b8 (x20) | 0xa59e7 | **margin** [ebp-0xC] |
| Thunderstorm (96) | 0xa62aa | 0xa6492/0xa64ac (x40) | 0xa64db | **margin** [ebp-0xC] |
| **LightningStrike (92)** | **NONE** | 0xa3e42/0xa3e5c (x33) | 0xa3e8b | **raw M** |

**LightningStrike (92) never calls `fcn.000601a6`.** Its handler 0xa33d2 is a 9-line stub:
`ForEachTargetTile(M, continuation 0xa3406)`; the continuation (0xa3406..0xa43c5, the
12-segment bolt animation) stores the incoming `cx = M` at [ebp-0x14] and computes
`damage = max(1, M*33/100)` directly — **no MagicResist check, no creature-class mask, no
margin**. Exhaustive: `axt @ fcn.000601a6` lists 30 call sites and none lies in
[0xa33d2, 0xa43c6); a full instruction scan of the range shows no other resist read.
LightningStrike is the only damage spell in school 3 with this property (single-target,
targets 0x08 — presumably "lightning ignores magic resistance" was intentional).

The other gated handlers all follow the exact frost-line codegen (`xor edx,edx; mov dx,
[gateResult]; imul edx,edx,K; idiv 100; max 1`) — the byte pattern `31 d2 66 8b 55 f4 6b d2 K`
was verified at each site. This upgrades the previous "INFERRED: rest of the K-table follows"
to CONFIRMED for the whole fire/lightning line, with the LightningStrike exception.

### 5b. Active-spell type-1 "strength" (+0) pool — NOT vestigial: it is a DURATION IN GAME HOURS

Full accessor family decoded (table `0x153b3e`, 12 B per party slot = 3 types x {u16 strength,
u16 percent}; member->slot via `0x153cbc`; the pair at `0x153b3a/0x153b3c` is a 13th
"ambient/light" entry):

| Fn | Role |
|---|---|
| `fcn.000607da(member, type, strength, pct)` | add entry — **only when the entry is EMPTY** (`strength == 0`); if an entry is active the call is silently dropped (no stack, no refresh) @ 0x60829 |
| `fcn.00060898(entry, strength, pct)` | low-level merge (would stack strength / max pct) — but the only callers are 0x607da's empty branch and the Light spell's ambient entry `fcn.0006085d`; per-member stacking is unreachable |
| `fcn.000606b5(member, type)` | read percent (+2) **gated on strength != 0** |
| `fcn.00060738(member, type)` | read percent ungated (raw) |
| `fcn.00060796()` | read ambient (light) percent, gated — callers are the 3D/2D lighting code (0x147xx/0x1aaxx/0x3d5xx) |
| **`fcn.000605ed`** | called from **`fcn.00043acc` = the GAME-HOUR tick**: for the ambient entry and all 6x3 member entries, run `fcn.00060670` |
| `fcn.00060670(entry)` | `if (strength) { strength--; if (strength == 0) pct = 0; }` |

So the `+0` word **is read** — by the hourly decrementer and as the "entry active" flag.
The MagicShield/PersonalProtection writes (`strength = max(1, M*10/100)`, `pct = M`) mean:
**the buff lasts max(1, M*10/100) game hours (1..10), its percent stays constant for the
whole duration, and re-casting while active does nothing.** There is no in-combat round
countdown (combat freezes the game clock), matching the earlier "persists for the battle"
observation. The earlier note "+0 strength stacks across casts" is wrong for member entries —
the write-if-empty check makes stacking unreachable (only the Light spell's ambient entry
can accumulate via fcn.0006085d).

Readers of the percent: melee/ranged defense boost (0x4eea9/0x4ef17 in the attack callbacks,
type 1), spell-gate MagicResist boost (`fcn.000601a6` @ 0x60254, type 2:
`resist += resist*pct/100`), and four save/UI sites at 0x288xx. No other consumer exists.

**Remake implications:** model MagicShield/PersonalProtection as out-of-combat-clock buffs:
duration_hours = max(1, M/10), percent = M, expire on the hour tick (zero the percent),
reject re-casts while active. Type 0 (physical-attack percent) still has no writer — that
half remains vestigial.

## New key globals / functions (this batch)

| Addr | Meaning |
|---|---|
| `0x13e7de` | per-school spell count u16[7] = {21,11,10,15,0,4,0} |
| `0x13e7ec` | map-mode targets-0x80 area-fn table — EMPTY (terminator only) |
| `0x13e7f4` | combat targets-0x80 area-fn table {school,num,fn} x8 |
| `0x5f597` / `0x5f60b` / `0x5f6c5` | area fns: single tile / whole row ("Big") / teleporter (30-tile pick) |
| `fcn.0005ef24` | BuildTargetMask (switch on SPELLDAT targets byte) |
| `fcn.0005fb21` | ForEachTargetTile(M, cont) — modes 1 any / 2 allies / 3 enemies / 4 raw tiles |
| `fcn.0004dc4b` / `fcn.0004dd23` | candidate masks: party-occupied tiles (rows 3-4) / monster-occupied tiles (rows 0-3) |
| `fcn.0005772a(uiId, cursorTbl, mask)` | combat target-pick UI (0x57/0x58 tile, 0x59 row, 0x85 trap tile, 0x1ac teleport) |
| `fcn.0005eb7d` / `fcn.0005ed68` | cast entries (menu / combat): zero ctx 0x1775a0(28B), set school/number/slot/caster, BuildTargetMask, dispatch |
| `fcn.0005eea8` | UseItem finisher: copies combatant ctx +0x52(28B) -> 0x1775a0, queues script ctx 0x13e9da |
| `0x1775a4` / `0x1775a6` | cast globals: SOURCE item slot (0xFFFF = spellbook) / TARGET item slot (targets 0x40) |
| `fcn.00060013(sheet, slot)` | item charge consumption (flags +0x1B: 0x04 single-use, 0x10 vanish-at-0) |
| `fcn.0006099b` / `fcn.00060917` | SPELLDAT byte +0 (env) / +3 (targets) accessors |
| `fcn.00060554` | current environment index (combat=5, else RestMode 0..3) |
| `fcn.00061b26` / `fcn.00061b96` | IsSchoolUsable / IsSpellImplemented |
| `fcn.0005f7ec` | GoddessWrath victim picker (out: list 0x196fdc, count 0x19703c) |
| `fcn.0004e493` | living-monster count (grid scan kind==2) |
| `fcn.00050360(school, candMask, tgtFilter)` | AI: uniform-random spell among candidates matching targets filter |
| `fcn.000501ff(self)` | AI: uniform-random enemy tile index (rand % enemyCount, grid order) |
| `fcn.00050175(self, row)` | AI: count enemies in a grid row (row-pick tiebreak: more-populated of rows 3/4) |
| `0x15db4c` | dword[210] per-global-spell table read by UseItem (passed to anim script builder 0x81f42) — spell cast-animation ref |
| `fcn.000605ed` / `fcn.00060670` | hourly active-spell decrementer / per-entry `strength--, pct=0 at 0` |
| `fcn.00060738` / `fcn.00060796` | active-spell percent raw read / ambient(light) read |
| `0xa3406` | LightningStrike per-target continuation (UNGATED, raw M x33/100) |

## Corrections to _RE_COMBAT.md claims (do not edit that file from this agent)

- "active-spell +0 strength **stacks** max(1,M*10/100)" -> write-once while empty; the value
  is a **duration in game hours**, decremented by the hour tick; pct zeroed at expiry (§5b).
- "the +0 strength pool's depletion mechanism not located — possibly unused" -> located:
  `fcn.00043acc -> fcn.000605ed -> fcn.00060670` (§5b).
- The K-table row "92 LightningStrike dmg max(1, M*33/100)" is literally correct — it is the
  **only** line spell where M is NOT replaced by the gate margin (no gate at all) (§5a).
- `fcn.0004c2cd` "tries school 6 first" (punch-list phrasing) -> it equips/uses ItemType 6
  (long-range weapon); spell schools are uninvolved (§4.2).
- UAlbion `SpellTargets.DeadParty` (0x04) is "whole living party"; `SpellEnvironments`
  first four flags should read City/Dungeon/Wilderness/Interior (§1.2/1.3).
