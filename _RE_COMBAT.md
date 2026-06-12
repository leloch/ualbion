# Combat module reverse-engineering — initial findings

> Live document. Updated as we decode functions.

## Initiative order — DECODED (Phase 2.2)

Built in `fcn.0004c3f5` (549 B) via the 3rd prep call from the round controller.

```c
void BuildInitiativeOrder() {
    word_15f10e = 0;     // total combatant count

    // Party loop (6 fixed slots at 0x15e6ac, 110-byte stride):
    for (i = 0; i < 6; i++) {
        Combatant* p = (Combatant*)(0x15e6ac + i * 110);
        if (p->kind != 1) continue;                      // empty slot
        int initiative = fcn.00035c77(p->sheet, 3);       // stat #3 = SPEED
        Slot* s = &combatants[word_15f10e++];
        s->team        = 0;       // party
        s->initiative  = (ushort)initiative;
        s->ptr         = p;
    }

    // Monster loop (dynamic at 0x15e944, count at 0x15f118):
    for (i = 0; i < word_15f118; i++) {
        Combatant* m = (Combatant*)(0x15e944 + i * 110);
        if (m->kind != 2) continue;
        int initiative = fcn.00035c77(m->sheet, 3);       // same stat #3
        Slot* s = &combatants[word_15f10e++];
        s->team        = 0;       // (same '0' marker as party — re-purposed by sentinels)
        s->initiative  = (ushort)initiative;
        s->ptr         = m;
    }

    // SENTINEL #1: phase divider between fast/slow combatants
    Slot* s1 = &combatants[word_15f10e++];
    s1->team       = 1;       // phase-1 sentinel marker
    s1->initiative = 0x33;    // 51 — combatants with Speed > 51 act before this divider
    s1->ptr        = NULL;

    // SENTINEL #2: end-of-round
    Slot* s2 = &combatants[word_15f10e++];
    s2->team       = 2;
    s2->initiative = 0;       // sorts to the bottom
    s2->ptr        = NULL;

    // Sort descending by initiative (qsort).
    qsort(combatants, word_15f10e, /*item_size*/8, /*cmp*/fcn.0004c698, /*swap*/fcn.0004c61a);
}
```

The comparator (`fcn.0004c698`, 91 B) returns 1 iff `a.initiative < b.initiative` — i.e. **sort descending** so higher Speed acts first.

The round controller then walks the sorted table in order. When it hits a sentinel, it calls a phase-event handler instead of `vtable_1[action_kind]`:

| Sentinel | Handler | Likely purpose |
|---|---|---|
| `team=1, initiative=51` | `fcn.0004b605` | Phase divider — UI prompt / between-phase event |
| `team=2, initiative=0` | `fcn.0004b755` | End-of-round bookkeeping |

**Slot layout (8 bytes per entry, table at `0x15e5d8`):**

| Offset | Type | Field |
|---|---|---|
| +0x00 | u16 | team / sentinel marker (0=combatant, 1=phase divider, 2=end-of-round) |
| +0x02 | u16 | initiative value |
| +0x04 | u32 | pointer to Combatant struct (NULL for sentinels) |

Total count cached at `0x15f10e` (u16). The 51 threshold is a hard-coded constant — there's no rule scaling it with party level.


## How we identified these

The original DOS `MAIN.EXE` retains Watcom's `assert()` macro `__FILE__` strings — they were not stripped from the production build. By extracting all string addresses matching `g:\albion\src\combat\*.c` and using `radare2 axt @ <addr>` we found every function that contains a failed-assertion handler citing the combat module.

Workflow:
```sh
# (one-time analysis, save project)
radare2 -e bin.relocs.apply=true -q -c 'aaa; Ps albion_aaa' MAIN.EXE
# Get string addresses
grep -E 'combat\\(combat|comshow|comobs)\.c' _r2_strings.tsv | awk '$3 ~ /^0x/ {print $3}' > combat_addrs.txt
# Find xrefs to each
awk '{print "axt @ "$1}' combat_addrs.txt > axt.r2
radare2 -e bin.relocs.apply=true -q -p albion_aaa -i axt.r2 MAIN.EXE > xrefs.txt
```

## Module → function map (combat module = 22 functions, ~8 KB)

### `combat.c` (the main combat loop module)

| Function | Size | Notes |
|---|---|---|
| `fcn.0004c6f3` | 479 B | 1 assert |
| `fcn.0004c937` | **1014 B** | 3 asserts. Opens with a loop initialising 18 × 110-byte slots at global `0x15e944`. **18 × 110 bytes = combat-participant array** (18 = CombatRowsForMobs × CombatColumns = 6×3; OR all-participants 30 - party-region 12 = 18). Each slot is 110 bytes → that's the in-memory `Combatant` struct. Strong candidate for **combat round / setup**. |

### `comshow.c` (combat presentation/UI)

| Function | Size | Notes |
|---|---|---|
| `fcn.00051e0d` | 465 B | 2 asserts |
| `fcn.00052b71` | 704 B | 1 assert (raw string ref) |

### `comobs.c` (combat objects / per-action behaviour — 17 functions)

| Function | Size | Notes |
|---|---|---|
| `fcn.000542c9` | **1463 B** | Largest. Likely the **per-combatant action dispatcher** — the per-mob update routine that decides attack vs. cast vs. move. |
| `fcn.00053ce9` | 562 B | |
| `fcn.00053fd6` | 527 B | |
| `fcn.00053554` | 446 B | |
| `fcn.00054a6c` | 336 B | |
| `fcn.0005395f` | 326 B | |
| `fcn.000558d2` | 267 B | |
| `fcn.00054880` | 266 B | |
| `fcn.00053ba3` | 258 B | |
| `fcn.00053871` | 238 B | |
| `fcn.000541e5` | 228 B | |
| `fcn.0005498a` | 226 B | |
| `fcn.00054ce7` | 187 B | |
| `fcn.00054bbc` | 177 B | |
| `fcn.00053aa5` | 124 B | |
| `fcn.00054c6d` | 122 B | |
| `fcn.00053b21` | 65 B | |
| `fcn.00053b62` | 65 B | |

## Key global addresses

- `0x15e944` — base of an **18-entry × 110-byte array** initialised by `fcn.0004c937`. Likely the **combatant table** (party + mob slots). Each slot 110 bytes.

## Open questions / next traces

1. **What's the 110-byte Combatant struct layout?** Cross-reference field accesses (`mov [eax+OFFSET]`) to figure out offsets. Hypothesis: subset of `CharacterSheet` + combat-runtime fields (CombatPosition, target, queued action).
2. **What's at `0x4ac45` (the `(nofunc)` xref)?** Outside any detected function — could be an unrecognised function start. Worth investigating manually with `pd 32 @ 0x4ac45`.
3. **Combat-round entry point** — find a function that calls `fcn.0004c937` (init) followed by per-combatant updates. Use `axt @ fcn.0004c937` once we re-run.
4. **AP-cost constants** — the binary should have small immediate values (1, 2, 3, 5) that gate action selection. Trace these in `fcn.000542c9`.

## Source-file paths found in MAIN.EXE (full list)

All Blue Byte `__FILE__` paths visible in the binary:

```
g:\albion\src\combat\combat.c     -- this module (combat round logic)
g:\albion\src\combat\comobs.c     -- combatant per-action behaviour
g:\albion\src\combat\comshow.c    -- combat presentation
g:\albion\src\map\2d_map.c        -- 2D map rendering / walking
g:\albion\src\map\2d_path.c       -- 2D pathfinding!
g:\albion\src\map\3d_prep.c       -- 3D map preparation
g:\albion\src\map\map.c           -- common map code
g:\albion\src\map\map_pum.c       -- map ??? (events?)
g:\albion\src\map\specitem.c      -- special items (compass, monster-eye)
g:\albion\src\ui\boolreq.c        -- yes/no dialog
g:\albion\src\ui\buttons.c        -- buttons
g:\albion\src\ui\colours.c        -- palette
g:\albion\src\ui\control.c        -- input dispatch
g:\albion\src\ui\inputnr.c        -- number input
g:\albion\src\ui\itemlist.c       -- inventory list dialog
g:\albion\src\ui\itemsel.c        -- item selection dialog
g:\albion\src\ui\membrsel.c       -- party-member selection dialog
g:\albion\src\ui\popup.c          -- popup dialog
g:\albion\src\ui\selwin.c         -- selection window
g:\albion\src\ui\userface.c       -- top-level UI dispatcher
```

**Modules with NO retained `__FILE__` strings** (asserts stripped at compile time for that module):
- spell / magic engine
- save/load
- character-sheet logic
- inventory mutation
- NPC AI
- 3D rendering / lighting
- script interpreter

For those modules, we'll have to find functions via **string xrefs from format strings** (e.g. `"%d gold"`, `"You have learned %s"`) rather than `__FILE__` xrefs.

## Combat magic numbers (from constant frequency analysis)

Extracted by `_extract_constants.ps1` from the 22-function disassembly dump. Frequencies count occurrences of immediate constants in `cmp / mov / imul / add / sub / and / or / test / push / shl / shr / sar` instructions across all combat functions.

| Imm | Hex | Count | Hypothesis |
|---|---|---|---|
| 2 | 0x2 | 23 | Type discriminator (party/NPC/monster=0/1/2); or 2-stride increments for word-sized arrays |
| 31 | 0x1F | 17 | Bit mask (5 bits) — could be slot index 0..29 with mask 0x1F; or byte field with 5-bit max |
| **100** | **0x64** | **17** | **Hit-chance modulus** — `random % 100 < threshold` pattern. Or HP/SP scaling base. |
| 8 | 0x8 | 16 | Bit mask / shift count |
| 4 | 0x4 | 16 | Stride for dword arrays; or bit position |
| 6 | 0x6 | 14 | `CombatColumns` (6 columns) — combat grid math |
| 1 | 0x1 | 10 | Bool / increment |
| 16 | 0x10 | 9 | 16-byte alignment; or stat boundary |
| 12 | 0xC | 8 | Party combat-tile count (party = rows 3-4 = 12 slots) |
| 255 | 0xFF | 5 | Byte max (stat caps); or `Direction.Unchanged` sentinel |
| 50 | 0x32 | 5 | Possible hit-chance baseline (50 %) |
| 3 | 0x3 | 4 | CombatRowsForMobs |
| 800 | 0x320 | 4 | Damage cap? Or screen-coord pixel width |
| 40 | 0x28 | 4 | |
| 24 | 0x18 | 4 | Possible struct size (24-byte sub-record) |
| **18** | **0x12** | **4** | **Mob count cap** — confirmed by `fcn.0004c937` init loop |
| 1000 | 0x3E8 | 3 | Big number — possible XP per-level base |
| 32 | 0x20 | 3 | |
| 25 | 0x19 | 3 | |
| 772 | 0x304 | 2 | |
| **110** | **0x6E** | **2** | **Combatant struct size on heap** (confirmed) |
| 247 | 0xF7 | 2 | |
| 41 | 0x29 | 2 | |

## Top-level combat entry — `fcn.0x4ac00` (added manually, r2 missed it)

**This is the early-source-line combat function** (line 419 of combat.c — vs. line 2384+ for fcn.0004c937). Likely `combat_init()` or `combat_begin_battle()`. 392 bytes. Call sequence:

```
fcn.0004ac00 :=
    *0x15f122 = 0xc7660000                       ; some marker / version
    [0x15f116] += eax                            ; (constant init?)
    fcn.00061e9a()                               ; reset something
    fcn.00062629(eax=0)                          ; reset something else
    fcn.00062765(eax=0)                          ; reset combat tables?
    dx = *0x15f10c                                ; battle config index
    p = allocate(39 bytes, type=eax)              ; via fcn.000233b8 (a typed allocator)
    *0x15e940 = p
    assert(p, file=combat.c:419, expr=4)          ; line 419!
    eax = word[0x13e150 + *0x15f10c * 2]           ; lookup into 0x13e150 word array
    var_ch = eax
    fcn.00053554(eax)                              ; call into comobs.c
    ...                                            ; (continues past this snippet)
    ...
    fcn.0004c937()                                 ; CALLS THE SETUP FUNCTION at 0x4acbe
```

So `0x13e150` is **another lookup table** (word-indexed by battle config). Probably mapping `battle_id → asset_id` or similar.

## Function map summary

```
0x0004c6f3  fcn.0004c6f3   479 B   combat.c   probably combat_init or related setup
0x0004c937  fcn.0004c937  1014 B   combat.c   *** allocates 18-slot Combatant table, loads monster group, ***
                                              builds compact slot map at var_54h, counts mobs at 0x15f118.
                                              Strong candidate for combat_setup_battle(MonsterGroupId).
0x00051e0d  fcn.00051e0d   465 B   comshow.c  combat display
0x00052b71  fcn.00052b71   704 B   comshow.c  combat display, large
0x000542c9  fcn.000542c9  1463 B   comobs.c   *** LARGEST — likely per-combatant action dispatcher ***
0x00053ce9  fcn.00053ce9   562 B   comobs.c
0x00053fd6  fcn.00053fd6   527 B   comobs.c
0x00053554  fcn.00053554   446 B   comobs.c
... 14 smaller comobs.c functions
```

## RNG located: `fcn.00094326` = `rand()` (Watcom LCG)

- Address: `0x00094326`, 36 bytes, cdecl, 3 basic blocks, 12 instructions.
- Identified by Watcom rand LCG multiplier **`0x41C64E6D`** found at `0x00094332` (little-endian `6d4ec641`).
- 81 callers across the binary. Every call is a "roll dice" operation.
- **Combat-specific callers** (functions in / near the combat code range that call rand()):
  - `fcn.0004c1e4` (114 B) — see "AI choice" below
  - `fcn.0004cdb0` (632 B) — combat.c range, large RNG user. Hypothesis: **hit-check / damage-roll**.
  - `fcn.000512e7` (216 B) — comshow.c range, possibly attack-animation random variation.

## Decoded: `fcn.0004bf77` — Per-combatant status-condition gate

```c
void PerCombatantTurnGate(Combatant *self) {
    self->action_kind = 0;                              // [+0x4E] = 0 (clear)
    uint16_t conds = GetStatusConditions(self->sheet);  // fcn.000364b0
                                                         // (returns PlayerConditions ushort)
    if (conds & 0x200) return;                          // Asleep — skip turn
    if (conds & 0x100) { Panic(self); return; }         // Panicking — fcn.0004bff7
    if (conds & 0x400) { InsaneAct(self); return; }     // Insane — fcn.0004c1e4
    // No turn-blocking status — caller's normal AI path takes over.
}
```

**This means `fcn.0004c1e4` (the 50/50 melee-or-spell function I decoded above) is the INSANE-MONSTER behavior, not normal AI**. Insane combatants pick random actions; normal mobs are handled elsewhere.

The bit values match UAlbion's `PlayerConditions` flags exactly:
- 0x100 = `PlayerConditions.Panicking`
- 0x200 = `PlayerConditions.Asleep`
- 0x400 = `PlayerConditions.Insane`

So when implementing combat behaviour, the per-combatant turn flow is:
1. Check status conditions — Asleep/Paralysed/Unconscious skip turn
2. Check Panicking → flee
3. Check Insane → random action (the 50/50 melee/spell behaviour)
4. Otherwise → normal AI / player input

## Decoded: `fcn.0004c1e4` — InsaneAct() — Monster random-action (50/50)

```c
void MonsterAiChoice(Combatant *self) {
    int r = rand();
    int sign = r >> 31;        // sar edx, 0x1f
    int rem = (r - sign) % 8;  // signed idiv by 8

    if (rem >= 4) {
        ActionA(self);              // fcn.0004c256 (119 B)
        if (self->u4e == 0)
            ActionB(self);          // fcn.0004c2cd (296 B)
    } else {
        ActionB(self);
        if (self->u4e == 0)
            ActionA(self);
    }
}
```

So:
- **Combatant struct field `[+0x4E]` is the "did-an-action-succeed" flag** (probably `chosen_action_kind` or `target_acquired`). 0 = no action chosen yet.
- The monster has TWO action handlers: ActionA (`fcn.0004c256`) and ActionB (`fcn.0004c2cd`). It picks one at 50/50, falls back to the other if the first didn't set `u4e`.
- ActionA is 119 B (small / simple). ActionB is 296 B (larger). Likely **melee attack** vs **ranged or cast**.

This is mob AI for "I have two attack options, pick one randomly, fall back to the other if I can't use my first pick (out of range, no spell points, etc)".

## Action handlers (vtable_1) — what we now know

| `action_kind` | Address | Decoded purpose |
|---|---|---|
| 1 | `0x0004e6d1` (804 B) | **MeleeResolve**: validates target tile, plays sound 444 (melee swing), calls `fcn.00051ab5` |
| sub | `0x00051b51` (320 B) | **MeleeAnimation start** (sub_idx 0 of action_kind 1) — allocates a 40-byte animation slot via `fcn.000233b8(40, 48)`, then a 100-byte buffer via `fcn.00053871(100)`. Writes baseline coords X/Y = (50, 150) and starts the frame-tick loop calling `fcn.00075e71` for each frame (0..N). Does **not** roll damage — pure visual. |
| sub | `0x00051c91` (71 B) | **MeleeStrike dispatcher** (sub_idx 1 of action_kind 1) — reads target struct fields at +0, +2, +4 (target tile / mob ref / something) and tail-calls `fcn.00052b71` (704 B) with them. **`fcn.00052b71` is the actual hit-roll + damage-roll resolver** — the largest combat handler and the right next target for Phase 2.3/2.4 decoding. |

**MeleeResolve (`fcn.0004e6d1`, 804 B) is NOT the hit-roller** — despite its name suggesting otherwise, it's purely the **movement step** that walks an attacker into melee range:

1. Plays sound 444 (or 443 once movement succeeds — different cue per branch)
2. Reads `self[+0x52]` = target tile in the 6×5 combat grid
3. Validates the tile is in-bounds (col < 6, row < 5)
4. Reads the tile slot at `grid[+0x176d74 + row*84 + col*14]`
5. Clears old grid slot for `self`, writes `self` to new grid slot
6. Updates `self[+0x42] = col`, `self[+0x44] = row`
7. Sets `self->action_kind[+0x4e] = 0` (action consumed)

So the actual swing/hit-resolve happens **later**, when sub_idx 1 fires through `vtable_2[action_kind=1, sub_idx=1] = fcn.00051c91 → fcn.00052b71`. That's the function to decode next for the hit-chance formula.

### `fcn.00052b71` — ranged-attack animation builder (corrected via freealbion-wiki cross-check)

704 B. Called as `BuildRangedAttackAnim(attacker, weaponSlotIdx, targetCol, targetRow)` from `fcn.00051c91`. Early-exit when `weaponSlotIdx == 0`. The prologue reads two **inventory slots** off the attacker's character sheet:

| Sheet offset | Slot size | Purpose |
|---|---|---|
| `sheet + 0x2e6` (742) | 6 bytes × N | **Equipment slot row #1** — slots 1..9 (Neck/Head/Tail/L-hand/Chest/R-hand/L-finger/Feet/R-finger per the wiki). `fcn.0004a521(sheet + 0x2e6 + (slot-1)*6)` resolves to the item-data record. |
| `sheet + 0x304` (772) | 6 bytes | **Right-hand slot** — specifically. |

The item-data struct fields read (offsets relative to the resolved item from `fcn.0004a521` — corrected against the wiki's ITEMLIST layout):

| Item offset | Wiki field name | Purpose |
|---|---|---|
| `+0x01` | `typeid` | Item type / category (compared to 7 — likely the long-range-weapon class) |
| `+0x0e` | `ammoType` | Required ammo type (arrows / bolts / etc.) |
| `+0x14` | `ammoAnim` | **Ammo-flight animation index** (0..9), NOT damage. Asserted in range at `comshow.c:1608`. |

**Previous interpretation was wrong** — I had `+0x14` as "damage rating" based on radare2 alone. The wiki confirms this is `ammoAnim`, a ranged-weapon animation lookup. The actual physical damage rating lives at `item[+0x0d]` per the wiki. So `fcn.00052b71` is genuinely **the ranged-projectile animation builder**, not a damage calculator. That explains why all the inner functions are projection / animation math and never touch HP.

The actual damage rating (item[+0x0d]) being applied to the queued damage event must happen in the **non-ranged** path — likely whichever sub_idx handler the melee path takes after `MeleeResolve`. Combat with a long-range weapon goes through this animation builder; melee with a regular weapon goes through a different path.

**Next-session bite:** decode `fcn.0004a584` (the RNG helper after the weapon-rating read) and the comparator that gates "did the hit land?" — those two together give us Phase 2.3 and 2.4.

### Static RE breakthrough — 2026-05-20

After staring at this for a while, the architecture finally clicked: **combat resolves in two passes, not one**.

```
+----------------+         queue        +-------------------+
|  Round         |  --> 800 × 44 B -->  | Animator pass     |
|  controller    |       at 0x15f144    | (drains events,   |
|  +planning     |                      |  displays them)   |
+----------------+                      +-------------------+
```

1. **Planning pass** (`fcn.0004b032` → `vtable_1[action_kind]` → e.g. `MeleeResolve`): each combatant's action_kind handler **only does setup** — walks into range, **rolls the hit/damage**, and **queues a result event** into the 800-entry buffer at `0x15f144` (44 bytes per entry). No animation runs.
2. **Animator pass** (`fcn.00052b71` / `fcn.00051b51` family): drains the queue, plays the corresponding sprite/sound, calls `fcn.0005395f` cleanup.

That's why `fcn.0004e6d1` and `fcn.00052b71` look like they should contain the damage roll but don't — **damage is already baked into the queue entry by the time the animator picks it up**. The animator just plays a pre-rolled outcome.

**This reframes the search for the hit/damage formula:** look for code that **writes** into `0x15f144[idx*44]`, not code that reads from it. Search:

```
axt 0x15f144   # find writers
axt 0x15f148   # find ptr-field-1 writers
```

The function that **builds** these queue entries is what holds the hit/damage formula. Likely candidates in the MeleeResolve call chain (or one of the unexplored `comshow.c`/`combat.c` functions that the round controller calls right after movement). 800 × 44 B = 35.2 kB of event queue space, enough for hundreds of combatants × multiple events each.

### Deferred-action dispatcher (`fcn.0002fd69`, 308 B) — fully decoded

This is the **bytecode interpreter for combat scripts / event chains**. Pseudocode:

```c
int DispatchDeferredAction() {
    Context* ctx = (Context*)(0x153930 + word[0x13d708] * 24);  // 24-byte context records
    while (true) {
        ushort opcode = *(ushort*)ctx->ip;
        if (opcode == 0xFFFF) {                   // end-of-script marker
            if (ctx->flags & 1 || ctx->flags & 2) break;
            if (ctx->completion_cb && ctx->completion_cb())
                ctx->flags |= 8;                  // mark callback-succeeded
            ctx->flags |= 1;                      // mark completed
            break;
        }
        ushort op   = *(ushort*)(ctx->ip + 0);
        ushort arg  = *(ushort*)(ctx->ip + 2);
        ctx->ip += 4;                              // advance IP
        opcode_handlers[op](arg);                  // dispatch via 0x13d810[op*4]
        if (ctx->flags & 1) break;
    }
    return (ctx->flags & 9) == 9;                  // both completed (1) and callback-OK (8)?
}
```

Context-record layout (24 bytes per record, table base `0x153930`):

| Offset | Type | Field |
|---|---|---|
| +0x00 | u16 | flags — bit 0 = completed, bit 1 = ?, bit 3 = callback-OK |
| +0x0c | u32 | completion-callback function pointer |
| +0x14 | u32 | bytecode instruction pointer (current opcode address) |

**Opcode handler table at `0x13d810`** (10 entries × 4 bytes; strings start at +0x28):

| Op | Handler |
|---|---|
| 0 | `0x0002fe9d` |
| 1 | `0x0002ff5b` |
| 2 | `0x00030745` |
| 3 | `0x000307da` |
| 4 | `0x0003086f` |
| 5 | `0x00030992` |
| 6 | `0x00030a1c` |
| 7 | `0x00030ade` |
| 8 | `0x00030b93` |
| 9 | `0x00030c95` |

Error strings adjacent: `"Illegal event block."` (0x13d83a) and `"Endless loop in event chain."` (0x13d862) — confirming this is the event-chain interpreter that UAlbion's `EventChainManager` mirrors at a higher level.

### Action handler vtable — completed

`fcn.0004f829` (247 B, action_kind=6) — **Use Item**. Reads `self[+0x56]` as a slot index:
- Slots 1..9 → `sheet + 0x2e6 + (slot-1)*6` — **inventory slots, 6 bytes per slot**
- Slots 10+ → `sheet + 0x31c + (slot-10)*6` — **equipped slots**

Then calls `fcn.0004a5ad(item)` to load item-data, dispatches the use through `fcn.00081f42` (animation builder, same path as Summon) with a 300-byte buffer.

**This pins down the per-character inventory layout:** 9 main inventory slots at sheet+0x2E6, plus equipped slots at sheet+0x31C, each 6 bytes wide (item id + state).

`fcn.0004eac1` (890 B, action_kind=7) — **Targeted-tile action** (likely Throw or Ranged-attack). Reads `event[+2]` (team), `event[+4]` (combatant index) and indexes into either party table `0x15e6ac` or monster table `0x15e944` (110-byte stride) to get the target; then reads `target[+0x52]` (grid pos), `mod 6` = col, `div 6` = row. Full body not yet decoded.

### Updated vtable_1 with `comshow.c` provenance

| `action_kind` | Address | Decoded purpose |
|---|---|---|
| 0 | — | No-op (no action this turn) |
| 1 | `0x0004e6d1` | **MeleeResolve** (movement into range) |
| 2 | `0x0004e9f5` | **Cast school 5** |
| 3 | `0x0004ef8b` | **Cast school 6** |
| 4 | `0x0004f5d3` | **Defend / Apply self-condition** (plays sound 445) |
| 5 | `0x0004f6a2` | **Summon** |
| 6 | `0x0004f829` | **Use Item** ✱ newly decoded |
| 7 | `0x0004eac1` | **Targeted-tile** (Throw / Ranged) ✱ partial decode |

### `prtlogic.c` triage — completed

| Function | Size | Purpose |
|---|---|---|
| `fcn.00034894` | 240 B | (uncategorised; calls assertion at prtlogic.c) |
| `fcn.00034b66` | 843 B | **Party-state init** — zeroes 0x5BB8 bytes at 0x153b28, then writes seed constants (300, 31, 76) and entry flags. Called only from `fcn.00010bd7`. **NOT level-up.** |
| `fcn.00034f81` | 379 B | **9-case dispatch wrapper** that picks (bitset address, bit-count) and calls `fcn.000350a5` to zero the bitset. Used at game-start / new-game. |
| `fcn.000350a5` | 78 B | **Bitset-zero** primitive — clears `(bits + 7) / 8` bytes at supplied pointer. |
| `fcn.000350fc` | 417 B | **SetBit-by-category** (20 callsites) — switch on category 0..8 picking (bitset_addr, bit_count). |
| `fcn.0003529d` | 489 B | **TestBit-by-category** (30 callsites) — same switch, returns the bit value. |

**Bitset categories (8 of 9 decoded):**

| Cat | Bitset addr | Bit count | Probable purpose |
|---|---|---|---|
| 0 | `0x153d9a` | 1024 | Visited-events / chain-flags |
| 1 | `0x1594ba` | 999  | Different per-map visited bits |

(Rest of the cases are in the switch tables at `0x3511c` and `0x352c0` — dump those for the full mapping if needed.)

**None of these are the XP / level-up function** I was hoping to find. Level-up logic is likely outside `prtlogic.c` — possibly in `fcn.0003529d`'s caller or in `_apres.c` (the "AP-related" source file referenced at `0x13171e`).

### Animation/sound table at `0x13e370` — 10 entries × 14 bytes

Indexed by `weapon_damage_class (= item[+0x14])`, asserted in range 0..9 at line 1608 of `comshow.c`. Each 14-byte entry is 7 ushorts; the offsets read by `fcn.00052b71` are:

| Field offset | Read from | Purpose |
|---|---|---|
| +0 | `0x13e370` | sound/anim id on full-miss branch |
| +4 | `0x13e374` | sound/anim id on hit-but-blocked branch |
| +8 | `0x13e378` | written to anim+0x24 (X coord? always 0x96 in observed data) |
| +10 | `0x13e37a` | written to anim+0x26 (Y coord? always 0x96 in observed data) |

The 10 raw entries (`weapon_damage_class → 4×u16 ids + 2×0x96 + frame_count`):

| Class | Field-0 | Field-2 | Field-4 | Field-6 | Frames |
|---|---|---|---|---|---|
| 0 | 0x33 / 51 | 0x4C / 76 | 0x4B / 75 | 0x34 / 52 | 10 |
| 1 | 0x31 / 49 | 0x4E / 78 | 0x4D / 77 | 0x32 / 50 | 10 |
| 2 | 0x35 / 53 | 0x35 / 53 | 0x35 / 53 | 0x35 / 53 | 10 |
| 3 | 0x3A / 58 | 0x4A / 74 | 0x49 / 73 | 0x3B / 59 | 10 |
| 4 | 0x3C / 60 | 0x51 / 81 | 0x50 / 80 | 0x3D / 61 | 15 |
| 5 | 0x3E / 62 | 0x4F / 79 | 0x4F / 79 | 0x3E / 62 | 15 |
| 6 | 0x3F / 63 | 0x3F / 63 | 0x3F / 63 | 0x3F / 63 | 7  |
| 7 | 0x40 / 64 | 0x40 / 64 | 0x40 / 64 | 0x40 / 64 | 7  |
| 8 | 0x45 / 69 | 0x53 / 83 | 0x52 / 82 | 0x46 / 70 | 12 |
| 9 | 0x48 / 72 | 0x55 / 85 | 0x54 / 84 | 0x47 / 71 | 12 |

The mirror-symmetry (col 0 ≈ col 3, col 1 ≈ col 2 in most rows) suggests this is **4 cardinal directions** of the same animation per damage class. Classes 2/6/7 have all four columns identical = the animation looks the same from every angle (probably explosion / magic-burst types).

### Constants confirmed

| Address | Value | Purpose |
|---|---|---|
| `0x13e3fe` | u16 = 148 | Perspective focal length for combat-screen projection |
| `0x13e400` | u32 = 83 << 16 | Camera Z offset for combat-screen projection |
| `0x15f10e` | u16 | Total combatants this battle (incl. sentinels) |
| `0x15f118` | u16 | Monster count |
| `0x15e6ac` | base | Party combatant table — 6 × 110 B |
| `0x15e944` | base | Monster combatant table — N × 110 B |
| `0x15f144` | base | **Event/effect queue** — 800 × 44 B |
| `0x13d708` | u16 | Current event-chain context index |
| `0x153930` | base | Event-chain context table — 24 B each |

## **Actual damage formula — FULLY DECODED** (Phase 2.3 + 2.4 ground truth)

After the wiki-cross-check pointed out that `item[+0x14]` is `ammoAnim` (not damage), the search shifted to *who reads `item[+0x0D]` (real damage byte)*. Three call sites; one of them lives in `combat.c` at `fcn.0004ee3b` (336 B) — **the actual damage roll**.

### `fcn.00037455` (203 B) — `ComputeTotalDamage(sheet) → ushort`

Called from combat **and** inventory UI. Returns sheet's base damage + sum of damage bonuses from all 9 equipment slots:

```c
ushort ComputeTotalDamage(SheetHandle h) {
    Sheet* sheet = LockHandle(h);
    int total = sheet[+0xDA];                         // PRTCHAR base damage (wiki offset 218)
    for (int slot = 0; slot < 9; slot++) {
        ushort itemId = GetItemId(sheet + 0x2E6 + slot * 6);  // fcn.0004a49c
        if (itemId != 0) {
            ItemData* item = LookupItem(sheet + 0x2E6 + slot * 6);  // fcn.0004a521
            total += item[+0x0D];                                    // ITEMLIST damage byte
        }
    }
    UnlockHandle(h);
    return total;
}
```

A parallel function `fcn.00037520` does the same for defense (reading `item[+0x0C]` = protection byte and sheet[+0xD6] = base protection).

### `fcn.00035c22` (85 B) — `RandomVary(value) → int`

```c
int RandomVary(int value) {
    int roll = rand() % 51;            // [0, 50]
    return ((roll + 50) * value) / 100; // multiplier in [50, 100]
}
```

**This is the damage/defense variance the original engine uses — 50..100 % uniform.** Confirmed by reading the function with the assert string pointing into `combat.c`'s damage code. Applied independently to both attacker damage *and* defender protection.

### `fcn.0004ee3b` (336 B) — `RollDamageVsDefense(attacker_ctx, defender_ctx) → int`

The big one. Pseudocode:

```c
int RollDamageVsDefense(CombatCtx* atk, CombatCtx* def) {
    // --- Attacker side ---
    int str = GetEffectiveStat(atk->sheet, 0);              // fcn.00035c77, stat 0 = STR
    int raw = ComputeTotalDamage(atk->sheet) + str / 25;    // base + gear + STR/25
    if (atk->team == 1) {                                    // party
        int mod = ClassDamageModifier(atk->classIdx, 0);     // fcn.000606b5(class, type=0)
        raw = raw * mod / 100;
    } else {
        raw += word[0x13e190];                                // monster-side damage bonus
    }
    int variedDmg = RandomVary(raw);

    // --- Defender side (mirror) ---
    int rawDef = ComputeTotalDefense(def->sheet);
    if (def->team == 1) {
        int mod = ClassDamageModifier(def->classIdx, 1);     // fcn.000606b5(class, type=1)
        rawDef = rawDef * mod / 100;
    } else {
        rawDef += word[0x13e192];                             // monster-side defense bonus
    }
    int variedDef = RandomVary(rawDef);

    // --- Final ---
    int delta = variedDmg - variedDef;
    return delta > 0 ? delta : 0;        // no negative damage
}
```

**The complete formula:**

```
finalDamage = max(0, RandomVary(rawDmg) - RandomVary(rawDef))

rawDmg   = (sheet[+0xDA]  + sum(equip[+0x0D]) + STR/25) × classMod[class, 0] / 100   for party
         = (sheet[+0xDA]  + sum(equip[+0x0D]) + STR/25) + word[0x13e190]              for monsters

rawDef   = (sheet[+0xD6?] + sum(equip[+0x0C]))          × classMod[class, 1] / 100   for party
         = (sheet[+0xD6?] + sum(equip[+0x0C]))          + word[0x13e192]              for monsters

RandomVary(v) = (rand()%51 + 50) × v / 100              // 50..100 % uniform
classMod      = word[0x153cbc + classIdx*2 → table lookup at 0x153b3e/0x153b40]
```

**Key constants:**

| Address | Meaning |
|---|---|
| `0x13e190` | u16 monster-side damage bonus added to attack |
| `0x13e192` | u16 monster-side defense bonus added to protection |
| `0x153cbc` | Class data index — `classIdx * 2` → entry offset |
| `0x153b3e` | Class damage modifier table — 12-byte stride per class |
| `0x153b40` | Class secondary modifier (for stat type=1) |
| PRTCHAR +0xDA | Base damage (matches wiki "Base damage at 218") |
| PRTCHAR +0xD6 | Base protection (wiki "Base protection at 214") |

`UAlbion.Game.Combat.DamageCalculator.VaryDamage` updated to the real `(rand%51 + 50) × v / 100` formula. Battle.cs now calls `rng.Generate(51)` for the roll. **Phase 2.3 and 2.4 are now decoded for real, not placeholders.**

### `fcn.0004ed77` (142 B) — `MeleeHitOrMiss(atk, def)` — and the "no separate hit-roll" finding

The damage-applier is upstream:

```c
void MeleeHitOrMiss(CombatCtx* atk, CombatCtx* def) {
    int delta = RollDamageVsDefense(atk, def);             // fcn.0004ee3b
    if (delta == 0) {                                       // MISS
        // play "miss" sound 451 via fcn.0004ddfb
        return;
    }
    // HIT
    if (def->team == 2) {                                   // monster target
        Sheet* targetSheet = fcn.0004faeb(def);             // get target's combat info
        if (targetSheet->sheet_handle != 0) {
            // Call damage-apply via vtable on the target object: dword [object + 8]
            target_object[+8](attacker, defender, damage_applier);
        }
    }
}
```

**Phase 2.3 final answer: there is NO separate hit-chance roll in the original engine.** The "hit chance" emerges naturally from the variance overlap — when `RandomVary(rawAtk) - RandomVary(rawDef) <= 0` the swing is a miss. Heavily armoured targets get more misses because the defense roll often exceeds the attack roll; powerful attackers rarely whiff because their min-roll (50 % of base) is still high.

This means UAlbion's previous `DamageCalculator.HitChance` / `RollHit` / `RollParry` were architecturally wrong — those are separate-roll concepts not present in Albion. `Battle.ApplyMeleeAttack` updated to:

```csharp
int atkRoll = rng.Generate(51);
int defRoll = rng.Generate(51);
int variedAtk = VaryDamage(TotalAttack(a), atkRoll);
int variedDef = VaryDamage(TotalDefense(d), defRoll);
int adjusted = Math.Max(0, variedAtk - variedDef);
if (adjusted <= 0) return;   // implicit miss
```

### Critical Hit skill — UI-only (probable)

`Critical hit` (PRTCHAR offset 138 = 0x8A) is only read at two addresses in MAIN.EXE — both inside `fcn.0007faf7` (767 B), which is in the UI/stats-display range (~0x7Fxxx). **It is NOT read by any combat function** (no hits in `0x4Axxx-0x5Cxxx`). Working hypothesis: the Critical Hit skill is *display-only* in Albion, or used to populate a tooltip rather than to roll crits. Combat damage variance alone provides the "swingy" feel without a separate crit mechanic.

UAlbion's `DamageCalculator.RollCrit` is therefore PLACEHOLDER — the original engine likely has no crit roll. Battle keeps the placeholder for now because a 5 % damage-double feels right for player feedback even if it's not period-accurate.

### XP / Level-up — partial decode

ExperiencePoints at sheet+0xEE (matches wiki PRTCHAR offset 238). Only one site in MAIN.EXE reads this field directly: `fcn.00036d08` (76 B) — `GetExperience(sheet)` accessor with 7 xrefs.

The level-up logic lives in `fcn.00037aaa` (376 B), iterating the 6 party slots:

```c
void LevelUpCheck() {
    for (int slot = 0; slot < 6; slot++) {
        if (slotInactive(slot)) continue;
        if (sheetFlagBit0(slot)) continue;
        int classIdx  = GetClass(sheet);                          // fcn.0003658a — sheet[+3]
        int classMul  = dword[classIdx * 4 + 0x13d9e4];           // per-class base
        int xp        = GetExperience(sheet);                     // fcn.00036d08
        int slotData  = dword[slot * 4 + 0x159756];               // runtime per-slot factor
        int threshold = slotData * classMul;
        if (xp >= threshold) {                                    // level up triggered
            // ... level-up logic ...
        }
    }
}
```

**Per-class XP threshold base** at `0x13d9e4` (10 × dword) — confirmed via `pxw`:

| classIdx | Value | Class (UAlbion's `PlayerClass` enum) |
|---|---|---|
| 0 | 25 | Pilot |
| 1 | 35 | Scientist |
| 2 | 30 | Iskai-Warrior |
| 3 | 25 | Dji-Kas-Mage |
| 4 | 25 | Druid |
| 5 | 20 | Enlightened-One |
| 6 | 40 | Technician |
| 7 |  0 | (unused / Wraith?) |
| 8 | 25 | Oqulo-Kamulos |
| 9 | 35 | Warrior |

(Mapping is approximate — verify by cross-referencing class index used in radare2 to UAlbion's `PlayerClass` enum order.)

The **per-slot factor** at `0x159756 + slot*4` is loaded at runtime (zero at game-start) — likely from PRTCHAR's `SpellLearningPointsPerLevel` or one of the still-unknown ushort fields at sheet offsets 0xE0-0xEC (the 18-byte unknown PRTCHAR block at 220-237). Cannot determine statically without seeing populated runtime state.

**Implication for UAlbion's level-up:** the `(L+1)² × 100` placeholder is wrong shape. Real formula is multiplicative: `threshold = perClassConstant × perCharacterFactor`. Until the per-character factor is identified, UAlbion's placeholder stays — but the constants `{25, 35, 30, 25, 25, 20, 40, 0, 25, 35}` are now known and could be used as a scaling input once the second factor is found.

### Status-condition system — primitives + rest semantics

**`fcn.000363c2(sheet, condId)` — `SetCondition`** (152 B, 25 xrefs). Sets bit `1 << condId` at `sheet[+0x1E]`. For party members (sheet[+0] == 0), if the bit was newly set, plays sound `762 + condId` (so sounds 762..773 are the per-condition "got status X" cues). Bounds-checks condId < 12.

**`fcn.0003645a(sheet, condId)` — `ClearCondition`** (86 B, 31 xrefs). Plain `sheet[+0x1E] &= ~(1 << condId)`. Bounds-checks condId < 12.

**`fcn.0003822d` — `RestRecoveryClearConditions`** (in the prtlogic.c range). Iterates `dword[0x153a20]` (current party member ptr) and explicitly clears **exactly 6 conditions** in this order:

| Order | condId | UAlbion enum |
|---|---|---|
| 1 | 0 | Unconscious |
| 2 | 4 | Paralysed |
| 3 | 10 (0xA) | Insane |
| 4 | 9 | Asleep |
| 5 | 8 | Panicking |
| 6 | 5 | Fleeing |

The conditions NOT cleared by rest: **Poisoned, Ill, Exhausted, Intoxicated, Blind, Irritated**. These persist until explicitly cured by spells or items — matches Albion's design where you need Antidote / HealParalysis / HealBlindness / HealPoisoning / etc. as discrete cures.

**Implication for UAlbion's `StatusConditionTicker`:** the original engine has no per-condition timer — conditions are *binary*, applied by SetCondition and cleared in batch by rest events. My current ticker is **more lenient** than the original (auto-clears Asleep / Intoxicated / Panicking / Insane / Fleeing on a multi-hour timer). Two options:
1. Keep current ticker — playability-friendly, doesn't match original perfectly.
2. Replace with a "rest event" handler that clears the 6 conditions in one shot when the party sleeps.

Going with option 1 for now because the timer approach is forgiving for non-RPG-genre players and doesn't break any game logic. PLACEHOLDER comment in `StatusConditionTicker` updated to reflect that the original is binary-on-rest, not timer-based.

Sound IDs **762..773** map to the 12 conditions (per-condition "you got X" notification cue when SetCondition fires on a party member newly receiving the status).

### Defend / Retreat action (action_kind = 4)

`fcn.0004f5d3` (207 B) — corrected from the earlier "Defend" label. Actually **`Retreat`**: gated by `self->row == 0` (mob back row) or `self->row == 4` (party back row). Plays sound 445 and calls `SetCondition(sheet, 5 = Fleeing)`. So only back-row combatants can attempt to flee, which becomes the Fleeing condition.

### Close Range Combat skill (PRTCHAR +0x7A)

Read at 5 sites: `0x446f2` (fcn.000446f2, 642 B — large), `0x44774` (same function), `0x37a65` (fcn.00037958), `0x3601f` (fcn.00035fd5), `0x361d9` (fcn.00036193). The latter three are in the stat-getter range (`fcn.00035c77` family) — display/stat queries. `fcn.000446f2` is at 0x44xxx — outside combat module proper but worth a deeper look as it may be the "weapon-class effective rating" computation that feeds combat indirectly. Marked for next-session investigation.

## Combat event queue + damage roll — animation queue (Phase 2.4 confirmation)

The 800-entry × 44-byte queue at `0x15f144` is the **combat event queue** — populated during the planning pass, drained by the animator pass. `0x176d26` (u16) holds the active entry count (max 800).

**Enqueuer:** `fcn.00053ba3` (258 B, **101 xrefs** — used everywhere in combat). Scans for first free slot, increments the active count, returns the slot pointer.

```c
// Roughly:
EventQueueEntry* EnqueueEvent(int eventArg0, int eventArg1, int eventArg2) {
    if (!ValidateEventInputs(eventArg0)) return NULL;
    if (word[0x176d26] >= 800) {                    // assert at comobs.c:590
        Error(ERR_QUEUE_FULL); return NULL;
    }
    for (int i = 0; i < 800; i++) {
        EventQueueEntry* slot = (EventQueueEntry*)(0x15f144 + i * 44);
        if (slot->occupied) continue;
        word[0x176d26]++;
        // ... init slot fields ...
        return slot;
    }
    return NULL;
}
```

**Inside `fcn.000526e1` (the melee animation handler at sub_idx 0, 350 B) — calls the enqueuer 5 times** to schedule sound/visual effects, each enqueue followed by the same field-write pattern:

```c
EventQueueEntry* slot = EnqueueEvent(...);
if (slot != NULL) {
    slot[+0x0c] = 1;            // event type — 1, 2, 3, ... at each of the 5 call sites
    slot[+0x0e] = (u16)var_8h;  // damage class (item[+0x14], 0..9)
    slot[+0x10] = (u16)var_4h;  // animation/sound id from 0x13e37x table
    slot[+0x12] = (u32)var_ch;  // pointer or handle
}
```

After each enqueue, two `rand()` calls roll the **damage value** for the next slot:

```c
int roll1 = rand() % 40 + 60;     // uniform [60, 99]
int roll2 = rand() % roll1;       // uniform [0, roll1)
```

**This is NOT damage variance after all** — corrected against the wiki, these rolls feed *sound/animation* parameters, not damage. `var_8h` and `var_4h` get written into the queued event's `+0xe` / `+0x10` ushort fields and `var_ch` (the `[400, 599]` roll) goes to `+0x12`. The combination looks like *(pitch, pan, duration)* for a sound effect. The actual physical damage value is computed elsewhere — likely just before MeleeResolve queues these effects, using the `item[+0x0d]` (damage) field × stat modifiers.

**My `DamageCalculator.VaryDamage` change from ±10 % to 60..99 % was based on this misread** — it's a wider band than the original probably uses for melee damage variance. I'll keep the wider band as a "feels right for combat" default but flag it as still-unconfirmed for actual damage.

**Watcom `rand()` confirmed at `fcn.00094326`:**

```c
int rand(void) {
    seed = seed * 0x41C64E6D + 0x3039;
    return (seed >> 16) & 0x7FFF;     // 0..32767
}
```

LCG multiplier `0x41C64E6D`, increment `0x3039` (12345), 15-bit output. Standard Watcom runtime — usable as the reference RNG when comparing UAlbion's outputs against the original.

## Slot pool allocator — `fcn.00053871` (238 B, 52 xrefs)

Separate from the event queue, this is the **animation slot pool** used by `fcn.00051b51` etc.:

- **Master pool**: 1000 × 58 bytes at `0x168a84` (~58 kB)
- **Active list**: 1000 × 4 bytes at `0x167ac4` (4 kB) — pointers to currently-active slots
- **Active count**: `word[0x176d24]`

`fcn.00053871(size)` scans the master pool for a slot with `(flags & 1) == 0`, marks it as used (`flags |= 1`, `flags &= ~8`), appends to active list, returns the pointer.

The complementary frame-tick maintainer `fcn.00053fd6` (527 B, in `comobs.c:865`) iterates this list each frame, calling `fcn.00055e23` validator and clearing flag bits as animations complete. `fcn.0005395f` (326 B, also `comobs.c`) cleans up an individual slot — clears flag bits, frees subobjects at `+0x2c` and `+0x32`, scrubs from the queue.

## Combat-state init — `fcn.00053554` (446 B)

The "new battle" reset routine. Zeroes:
- 1000 × 4 B at `0x167ac4` (active-list)
- 3 × 4 B at `0x176d18` (counters / cursors)
- 8 × 4 B at `0x168a64` (per-team? per-school?)
- 0xE290 (57,984) B at `0x168a84` — master slot pool, confirming **1000 × 58** = 58 kB
- 0x8980 (35,200) B at `0x15f144` — event queue, confirming **800 × 44** = 35.2 kB

Plus initialises `0x176d28 = 0`, `0x176d24 = 0`, `0x176d26 = 0` (the three active-count words for the three pools).

## `prtlogic.c` bit-storage scheme — fully decoded

`fcn.000350fc` (SetBit) + `fcn.0003529d` (GetBit) form the game-flag accessor pair. Each takes `(category, bitIndex)` and routes to one of 9 bitsets via switch. Switch tables at `0x3511c` and `0x352c0`. Category 0 → `0x153d9a` (1024 bits) and Category 1 → `0x1594ba` (999 bits) confirmed from disassembly. Likely uses:

- Cat 0 = chain-visited bits per map
- Cat 1 = NPC-disabled flags
- Cat 2..8 = chests opened, doors opened, signal flags, etc. (full table = dump `0x3511c` switch).

`fcn.00034f81` is the bulk-zero variant — initialises bitsets at game-start via `fcn.000350a5` (the bitset-zero primitive: clears `(bitCount + 7) / 8` bytes).
| 2 | `0x0004e9f5` (204 B) | **Cast school 5**: AP-loop retry pattern. Cast-info globals at `0x13e1be / c0 / c2` |
| 3 | `0x0004ef8b` (204 B) | **Cast school 6**: identical AP-loop. Cast-info globals at `0x13e1d6 / d8 / da` (24-byte stride per school) |
| 4 | `0x0004f5d3` (207 B) | **Apply condition** (probably Defend/Wait): plays sound 445, calls `fcn.000363c2(sheet, 5)` — likely `SetCondition(Paralysed)` |
| 5 | `0x0004f6a2` (391 B) | **Summon** — confirmed. (1) Scans monster table at `0x15e944` for the first slot with `kind == 2`; (2) `memcpy`s 28 bytes from caster +0x52 (combat-position block) into the slot; (3) Sets the slot's `+0x66` to `1 << (row*6 + col)` — placing it on the grid at the caster's tile; (4) Links the new instance's `+0x5c` to the caster's sheet handle and `+0x60` to itself (parent-of-summon ref); (5) Dispatches `fcn.00051ab5(target, 7, ...)` — animation phase sub_idx=7. Gated by `word_13e7dc != 0` (likely a "summon enabled in this battle" flag). |
| 6 | `0x0004f829` | (not yet decoded) |
| 7 | `0x0004eac1` | (not yet decoded) |

**Spell-school globals — per-school 24-byte block** at `0x13e1a4..` (calculated from school 5 base):

```
0x13e1be  Spell-school-5  cast-info-area (size unknown — at least 18 bytes)
0x13e1c0      .. caster team (1 / 2)
0x13e1c2      .. caster sub-kind
0x13e1d6  Spell-school-6  cast-info-area
0x13e1d8      .. caster team
0x13e1da      .. caster sub-kind
```

Each school has its own dedicated cast-info struct. There are 7 schools (0..6), so 7 × 24 = 168 bytes total starting around `0x13e150` (which is also the battle-config lookup base — they may overlap).

## Decoded: `fcn.0004ef8b` — Action 3 (School-6 Cast) — and the AP COST PATTERN

```c
void Action3_CastSchool6(Combatant *self) {
    // Set globals describing the caster, used by the deferred cast dispatcher (fcn.0002fce1)
    *(uint16_t*)0x13e1d8 = (self->kind == 1) ? 1 : 2;  // caster team
    *(uint16_t*)0x13e1da = self->[+0x02];               // caster sub-kind (race/class?)

    // Get raw ActionPoints from the underlying CharacterSheet (offset 0x11 — matches PRTCHAR wiki)
    char *sheet = LockHandle(self->sheet);
    int ap = sheet[0x11];                              // byte: action points this turn
    UnlockHandle(self->sheet);

    // Some flag at self->[+0x04] bit 0 doubles AP — likely a "powered" / berserk state
    if (self->flags_04 & 1)
        ap <<= 1;

    // Try to cast `ap` times — each attempt either succeeds (sets 0x15f140 ≠ 0) and consumes
    // the action, or fails (decrement remaining AP, try again).
    for (int i = 0; i < ap; i++) {
        DispatchDeferredAction(0x13e1d6);              // fcn.0002fce1
        if (*(uint16_t*)0x15f140 != 0) {
            self->action_kind = 0;  // consumed — stop loop
            break;
        }
    }
}
```

**Key discoveries:**

| Insight | Evidence |
|---|---|
| **AP is at `sheet + 0x11`** | Matches PRTCHAR wiki offset 0x11 = "Combat action points" |
| **Cast attempts loop AP times** | Each AP = one cast attempt; cast can fail (resist) and AP gets retried |
| **Self flag bit 0 doubles AP** | Probably a power state (Battle Frenzy? Berserk?) |
| **`0x15f140`** = "cast succeeded" flag | Set non-zero by `DispatchDeferredAction` when a spell lands |
| **`0x13e1d6` block** | Caster-info struct (4-6 bytes: team + sub-kind + spell id + ?) read by the dispatcher |

The function `fcn.0002fce1` (136 B) is called from 10+ sites across the binary — it's a **deferred-action / event-dispatch utility**, not spell-specific. Combat uses it as a generic "execute the pending action stored in this buffer" call.

## Action dispatch architecture (TWO vtables)

Combat uses a **2-level dispatch**:

```
RoundController (fcn.0004b032)
   |
   |  for each combatant with action_kind != 0:
   |  call vtable_1[action_kind * 6]  // 0x13e196 — 6-byte entries
   v
ActionResolver  (e.g. MeleeResolve at 0x0004e6d1)
   |
   |  validates target tile, plays setup sound, etc.
   |  calls fcn.00051ab5(combatant, sub_index, ...)
   v
fcn.00051ab5 (universal action executor, 156 B)
   |
   |  reads combatant->kind at [+0x00] (1=party, 2=monster),
   |  caches as team index at [+0x10] (0=party, 1=monster),
   |  dispatches via vtable_2[team*48 + sub_idx*4]
   v
TeamSpecificHandler  (12 × 2 entries at 0x13e280)
   |
   |  Party version of action       OR      Monster version of action
   |  (different animations, UI prompts, AI-vs-player-input)
```

### vtable_1 at `0x13e196` — per-action-kind resolvers (6-byte entries)

The round controller (`fcn.0004b032`) dispatches via `call dword [edx + 0x13e196]` where `edx = action_kind * 6` (6-byte entries: 4-byte function pointer + 2 bytes metadata).

| Index | Action handler | Hypothesis |
|---|---|---|
| 1 | `fcn.0004e6d1` (804 B) | **MeleeResolve** — does the hit-check + damage roll on the target tile |
| 2 | `fcn.0004e9f5` | School-5 spell resolver |
| 3 | `fcn.0004ef8b` | School-6 spell resolver |
| 4 | `fcn.0004f5d3` | (unknown — possibly defend/parry) |
| 5 | `fcn.0004f6a2` | (unknown — possibly use item) |
| 6 | `fcn.0004f829` | (unknown — possibly flee) |
| 7 | `fcn.0004eac1` | (unknown) |

These are the **per-action gameplay logic functions**. Decoding them = decoding combat math.

### vtable_2 at `0x13e280` — per-team × per-sub-action handlers (sparse!)

Decoded contents (party team = team_idx 0, addresses found in slots 8, 12, 24, 32, 40, 44):

| Team | sub_idx | offset | Handler address | Notes |
|---|---|---|---|---|
| 0 (party) | 2 | +0x08 | `0x00051b51` | |
| 0 (party) | 3 | +0x0c | `0x00051c91` | |
| 0 (party) | 6 | +0x18 | `0x00051cd8` | |
| 0 (party) | 8 | +0x20 | `0x00051dd1` | |
| 0 (party) | 10 | +0x28 | `0x00051e0d` | ← Also identified as comshow.c (assertion citing comshow) — shared display function |
| 0 (party) | 11 | +0x2c | `0x00051fde` | |
| 1 (mob) | 0 | +0x30 | `0x00051e0d` | (shared with party sub-10) |
| 1 (mob) | 1 | +0x34 | `0x00051fde` | (shared with party sub-11) |
| ... | | | | (further slots not yet dumped) |

So the dispatcher pattern: **some sub-actions are team-specific, others are shared**. That justifies the 2D vtable shape — when both teams use the same function the entry is just duplicated.

### Universal action executor: `fcn.00051ab5` (156 B)

```c
void UniversalActionExecutor(Combatant *self, uint subIdx, void *arg) {
    if (subIdx >= 12) return;            // sub-action bound check

    int team = self->team;                // cached at [+0x10]
    if (team >= 5) {                       // un-initialised marker (5 = "unset")
        team = (self->kind == 1) ? 0 : 1;  // party kind=1 → team 0; else team 1
        self->team = team;                 // cache for next call
    }

    void (*fn)(Combatant*, void*) = (void(*)(Combatant*, void*))
        vtable_2[team * 48 + subIdx * 4]; // 0x13e280
    if (fn == NULL) return;
    fn(self, arg);
}
```

This is a 2-level vtable dispatcher with team-detection cache. The interesting bit is **how the team gets set lazily** — `[+0x10]` is checked against an "unset" sentinel value `>=5`, and if so, computed from `[+0x00]` (kind) and cached.

### vtable_1 at `0x13e196` — per-action-kind resolvers (6-byte entries)

Layout: 2 rows (team) × 12 entries (sub-action) × 4-byte function pointers = 96 bytes total.

Index formula: `vtable[team * 48 + sub_idx * 4]` — so a `dword*` 2D table.

Sub-action indices 0..11 — 12 distinct sub-actions per team. Total 24 distinct gameplay handlers behind this table.

Some callers of `fcn.00051ab5` (which dispatches via this vtable):
```
0x0004cce7, 0x0004cd8c, 0x0004d097  — from comobs.c functions (combat-object behaviour)
0x0004df48, 0x0004e0db                — combat.c functions
0x0004e908                            — MELEE resolver
0x0004ec3a                            — (intermediate handler in MeleeResolve)
0x0004f1d0, 0x0004f68c, 0x0004f813   — action vtable_1 entries 4, 5, 6
0x000602ed                            — outside combat module (some 0x60... function)
```

Each caller passes a different `sub_idx` value — telling us each action_kind has multiple internal phases (e.g. melee = "approach", "swing", "resolve hit", "play impact sound", "apply damage").

## Combat grid at `0x176d74`

- 30 cells (5 rows × 6 cols, matches `CombatRows × CombatColumns`).
- 14 bytes per cell, 84 bytes per row stride.
- Access pattern: `tile = (Tile*)(0x176d74 + row * 84 + col * 14)` where `row = pos / 6`, `col = pos % 6`.
- The `MeleeResolve` reads `[grid + row*84 + col*14]` and checks "non-zero" to determine if a target is present on the tile.
- 5-row layout: monster rows 0-2, party rows 3-4 (matches UAlbion's `CombatRowsForMobs=3`, `CombatRowsForParty=2`).

The 14-byte cell layout is still unknown — likely contains a pointer/handle to the occupying combatant plus metadata.

## Memory layout — combat arena globals

```
0x15e6ac  PARTY table (6 slots × 110 B = 660 B)
0x15e940  4-byte battle-context pointer (allocated to size 39, via fcn.000233b8)
0x15e944  MONSTER table (18 slots × 110 B = 1980 B)
0x15f108  end-of-monster-table region
0x15f10c  battle config index (word, used as lookup into 0x13e150 table)
0x15f118  mob count (word) — number of active monsters in current battle
0x15f122  combat marker (dword, set to 0xc7660000)
```

**Two combatant tables** — original uses separate arrays for party vs. mobs, unlike UAlbion's single flat 30-slot grid. The per-round iteration in `fcn.0004be7f` walks both:

```c
for (int i = 0; i < 6; i++) {                       // Party — fixed slot count
    Combatant *c = (Combatant*)(0x15e6ac + i * 110);
    if (c->kind == 1 && (c->flags & 4))             // 1 = party, 4 = "needs action"
        PerCombatantTurnGate(c);                    // fcn.0004bf77
}
for (int i = 0; i < *(uint16_t*)0x15f118; i++) {     // Monsters — variable count
    Combatant *c = (Combatant*)(0x15e944 + i * 110);
    if (c->kind == 2 && (c->flags & 4))             // 2 = monster, 4 = "needs action"
        PerCombatantTurnGate(c);
}
```

## Combatant struct partial layout (decoded so far)

```c
struct Combatant {  // 110 bytes total
    uint16_t kind;            // [+0x00]   1 = party member, 2 = monster, 0 = empty/inactive
                              //           (also used as "render mode" — temporarily set to 3 during targeting)
    uint16_t unknown_02;      // [+0x02]
    uint16_t flags;           // [+0x04]   bit 4 = "needs to act this round"
    uint16_t unknown_06;      // [+0x06]
    void    *sheet;           // [+0x08]   pointer to monster/character sheet
    ...                       // [+0x0C..0x4D]  ~66 bytes still unknown
    uint16_t action_kind;     // [+0x4E]   0 = no action, 1 = melee, 2 = school-5 spell, 3 = school-6 spell
    uint16_t unknown_50;      // [+0x50]
    uint16_t action_kind_2;   // [+0x52]   duplicate of action_kind (or "result kind")
    uint16_t target_position; // [+0x54]   chosen target grid index (0..29)
    ...                       // [+0x56..0x6D] remaining ~24 bytes — unknown
};
```

## Decoded: `fcn.0004c256` — `AI_MeleeTarget(self)` — Action A

```c
void AI_MeleeTarget(Combatant *self) {
    int reachable = GetMeleeReachableMask(self);   // fcn.0004db61 — bit per grid position
    int excluded  = GetExcludedMask(self);          // fcn.0004d85b
    int valid     = reachable & ~excluded;          // can-hit-now mask
    if (valid == 0) return;
    int picked = PickRandomTargetFromMask(valid);   // fcn.000512e7
    if (picked == 0xffff) return;
    self->action_kind = 1;       // [+0x4E]
    self->action_kind_2 = 1;     // [+0x52]
    self->target_position = picked; // [+0x54]
}
```

## Decoded: `fcn.0004c2cd` — `AI_TryCastSpell(self)` — Action B

```c
void AI_TryCastSpell(Combatant *self) {
    int kind = 0;
    int target = 0xffff;
    int targetMask = 0;

    // First try school 6 (combat magic):
    int school6Avail = HasSpellInSchool(self->sheet, 6);  // fcn.00049cc1
    if (school6Avail != 0xffff) {
        int savedMode = self->mode;
        self->mode = 3;  // signal "targeting for school 6"
        int mask = GetSchool6TargetMask(self);            // fcn.0004d68c
        self->mode = savedMode;
        if (mask != 0) {
            target = PickRandomTargetFromMask(mask);
            kind = 3;
        }
    } else {
        // School 6 not ready — try school 5:
        int school5Avail = HasSpellInSchool(self->sheet, 5);
        int savedMode = self->mode;
        self->mode = 3;
        int mask = GetSchool5TargetMask(self);            // fcn.0004d512
        self->mode = savedMode;
        if (mask != 0) {
            target = PickRandomTargetFromMask(mask);
            kind = 2;
        }
    }

    if (kind != 0 && target != 0xffff) {
        self->action_kind = kind;       // 2 or 3
        self->action_kind_2 = kind;
        self->target_position = target;
    }
}
```

**Spell school numbers in combat AI:**
- **School 6** preferred — combat magic (the most-effective school for the AI)
- **School 5** fallback — alternate combat magic
- Order matters: school 6 first → only if unavailable, try school 5

## Pointer-bounds-check helper: `fcn.00055e23`

118 bytes, 19 callers in combat code. Function-call equivalent of an `assert(ptr >= 0x168a84 && ptr < 0x176d14)` macro:

```c
int CombatPtrCheck(void *ptr, const char *file, int line) {
    if (0x168a84 > (uintptr_t)ptr) {            // upper-bounds violated
        AssertFailed(file, line, 13);
        return 0;
    }
    if ((uintptr_t)ptr >= 0x176d14) {           // lower-bounds violated
        AssertFailed(file, line, 13);
        return 0;
    }
    return 1;
}
```

The arena `[0x168a84, 0x176d14)` is 0xE290 (57,984) bytes — combat / game-state heap.

## Decoded: `fcn.0004cdb0` — Monster spawn / stat randomisation

Called once from `fcn.0004c937` (at `0x4cbb7`, inside the combat-setup function). Size 632 B. Watcom-style "lock the heap handle, work on the inner pointer, unlock" pattern.

```c
void RandomiseMonster(Combatant *src) {
    void *cloneHandle = Allocate(1214);                // 1214 = MonsterSheet size (per freealbion wiki)
    char *srcSheet = LockHandle(src);
    char *dstSheet = LockHandle(cloneHandle);
    Memcpy(dstSheet, srcSheet, 1214);                  // fcn.00092bcd
    UnlockHandle(cloneHandle);                         // fcn.0008b808
    UnlockHandle(src);

    for (int i = 0; i < 8; i++) {                      // 8 attributes
        int val = GetAttributeI(cloneHandle, i);       // fcn.00035da6
        GetAttributeMaxI(cloneHandle, i);              // fcn.00035f2f (side-effect read?)
        int rolled = ((rand() % 11) + 95) * val / 100; // ±5 % variance
        if (rolled < 0) rolled = 0;
        int capped = (val < rolled) ? rolled : val;    // clamp
        SetAttributeI(cloneHandle, i, capped);         // fcn.00035e23
    }

    for (int i = 0; i < 4; i++) {                      // 4 skills (CloseCombat/Ranged/Critical/LockPick)
        // similar pattern via fcn.00036193 / fcn.00036... etc
        ...
    }
}
```

**Match against UAlbion's `MonsterFactory.RandomiseStat`:**
```csharp
value = (AlbionRandom.Next() % modulus + offset) * value / 100;
// statOffset = 100 - (percentage / 2) = 95
// statModulus = percentage + 1 = 11
```
**Exact match** — original Watcom formula = `(rand()%11 + 95) * value / 100`. UAlbion's MonsterFactory is correct as-is. No change needed.

### Discovered helpers (decoded by usage)

| Function | Purpose |
|---|---|
| `fcn.000820ca` | `Allocate(size)` — heap allocator returning a handle |
| `fcn.0008b739` | `LockHandle(handle)` — returns raw inner pointer for the handle |
| `fcn.0008b808` | `UnlockHandle(handle)` — releases the inner-pointer lock |
| `fcn.00092bcd` | `Memcpy(dst, src, n)` — standard memcpy |
| `fcn.00035da6` | `GetAttributeI(sheet_handle, idx)` — returns 16-bit attribute value |
| `fcn.00035e23` | `SetAttributeI(sheet_handle, idx, value)` — sets 16-bit attribute |
| `fcn.00035f2f` | `GetAttributeMaxI(sheet_handle, idx)` — getter for `Max` field |
| `fcn.00036193` | Similar getter pattern, for skill data |

The 8 attributes (Strength / Intelligence / Dexterity / Speed / Stamina / Luck / MagicResistance / MagicTalent) and 4 skills (CloseCombat / Ranged / Critical / LockPicking) align exactly with UAlbion's `CharacterAttributes` and `CharacterSkills` enums.

## Global addresses

| Addr | Size/type | Purpose |
|---|---|---|
| `0x15e944` | 18 × 110 B | Combatant table (party+mobs) |
| `0x15f118` | word | Mob count (live monsters in current battle) |
| `0x15e6ac` | (referenced near 0x4c793 with `word ... = 1`) | Possibly "combat-active" flag |
| `0x15e6ae` | (referenced near 0x4c7a9 with `word ... = dx` indexed by slot) | Possibly per-slot something |

## Tooling recipes

```sh
# function info
echo 'afi @ fcn.XXXXXXXX' | radare2 -q -p albion_aaa MAIN.EXE

# disassemble
echo 'pdf @ fcn.XXXXXXXX' | radare2 -q -e scr.color=0 -p albion_aaa MAIN.EXE

# pseudo-C decompile (r2 native, limited)
echo 'pdc @ fcn.XXXXXXXX' | radare2 -q -e scr.color=0 -p albion_aaa MAIN.EXE

# find what calls a function
echo 'axt @ fcn.XXXXXXXX' | radare2 -q -p albion_aaa MAIN.EXE

# find what a function calls
echo 'axff @ fcn.XXXXXXXX' | radare2 -q -p albion_aaa MAIN.EXE
```


## Punch-list RE (2026-06-12)

> Items: spell magnitudes, combat Move rules, XP award, monster action masks, 3D collision
> radius, 3D Y-Z load transform. All radare2 against `albion_aaa`. CONFIRMED = read from
> disassembly with consistent cross-references; INFERRED = strong pattern, one detail unverified.

### MAJOR CORRECTION - action kinds 2/3 are WEAPON attacks, not spell schools (CONFIRMED)

`fcn.00049cc1` ("HasSpellInSchool") is actually **`FindEquippedItemOfType(sheet, itemType)`**:
it scans the 9 equipment slots at `sheet+0x2E6` and returns the slot index whose
`item[+1] (typeid)` matches the argument, else 0xFFFF. The "schools" 5/6 are
`ItemType.CloseRangeWeapon` (5) and `ItemType.LongRangeWeapon` (6).

Consequently in vtable_1 (base `0x13e196`, 6-byte entries = u32 fn + u16 meta; ends 0x13e1c0):

| kind | fn | Real meaning |
|---|---|---|
| 0 | NULL | no action |
| 1 | `0x0004e6d1` | **Move** (grid reposition; the "MeleeResolve" label was wrong) |
| 2 | `0x0004e9f5` | **Close-range (melee) weapon attack** - ctx template `0x13e1be`, completion cb `fcn.0004eac1` |
| 3 | `0x0004ef8b` | **Long-range weapon attack** - ctx template `0x13e1d6`, completion cb `fcn.0004f057`, consumes ammo via `fcn.0004f3e2` |
| 4 | `0x0004f5d3` | Flee/Retreat (back row only) |
| 5 | `0x0004f6a2` | Summon |
| 6 | `0x0004f829` | Use item |

The punch-list "per-school caster-info blocks at 0x13e1a4/0x13e1be/0x13e1d6" are actually:
0x13e1a4 = middle of vtable_1; 0x13e1be / 0x13e1d6 = the 24-byte **deferred-action context
templates** for melee/ranged attack resolution (`+2` team, `+4` combatant index 1-based,
`+0xC` completion callback, `+0x14` script IP - overwritten by `fcn.0002fce1` with one of the
generic scripts 0x13d70c/0x13d73c/0x13d768). Both attack callbacks re-derive the combatant,
read the weapon (slot 4 = right hand), play sound 446 (or 455 bare-handed), and resolve damage
via `fcn.0004ee3b` (the already-decoded RollDamageVsDefense) then `fcn.0004dec9` (ApplyDamage).
Misses play sound 451. Target sheet flag `+0x0E & 0x80` = "immune to normal weapons"
(needs magic weapon, checked via `fcn.00035bdc(sheet,2)`).

### Item 1 - Magic system: full cast pipeline (CONFIRMED)

Real combat/menu magic does NOT go through vtable_1 - it has its own dispatcher:

```
fcn.0005ecda  SpellDispatch(ctx):
    school = ctx[+6].hi16, number = ctx[+8].hi16     (1-based within school)
    table  = dword[0x13e930 + school*4]              // per-school fn-pointer tables
    fn     = dword[table + number*4 - 4]             // NULL => spell does nothing (no SP cost!)
    mult   = fcn.0005fdf7()                          // cast core, see below
    if (mult != 0) fn(ax = mult)
```

**Cast core `fcn.0005fdf7`** (globals: `0x1775a0` school, `0x1775a2` number,
`0x1775aa` caster sheet handle, `0x1775ae` caster combatant ptr):

1. LP surcharge: `fcn.0006042c` - if SP cost > current SP, the shortfall is paid in
   **Life Points** (scaled by Stamina, stat 4); applied via `fcn.00036799` SetLifePoints.
2. SP deduction: `SP = max(0, SP - cost)`; cost = `fcn.00060a1e(school, number)` =
   byte at `spelldat[ (school*30 + number - 1)*5 + 1 ]` - SPELLDAT.DAT buffer handle is at
   `0x177594`, 5-byte records (matches UAlbion SpellData). SP cur/max at sheet+0xD0/+0xD2
   (getter `fcn.00036a2a`, setter `fcn.00036a77`, max `fcn.00036b17`).
3. Returns **multiplier M = max(1, (mastery+50)/100)** where mastery =
   `u16 at sheet + 0x140 + school*60 + (number-1)*2` (`fcn.000372f4`), range 0..10000
   (i.e. M ~ mastery% 1..100; magic-cheat global 0x147130 forces 10000).
4. Post-cast `fcn.000603ae`: **mastery += MagicTalent (stat 7)** - spells improve with use.

**Effect magnitude formula (all damage/heal spells): `value = max(1, M * K / 100)`** -
K is the per-spell constant = value at 100% mastery.

Targeting: `fcn.0005fb21` iterates the 30-tile grid against bitmask `0x1775b4`
(mode word `0x1775b8` 1..4: variants for occupied/enemy-only/all), calling the per-spell
continuation `(tilePtr, col=dx, row=bx, M=cx)`; party-slot-target spells use `fcn.0005f9a3`.
If no target was affected: sound 698 (fizzle) and the action is NOT consumed (`+0x4E` stays).

**Timed combat effects `fcn.0004b8a1(target, kind, M, base)`** - duration =
`max(1, M*base/100) + 1` rounds, stored in 4 slots of 8 bytes at combatant `+0x22`
(re-applying while active is rejected):

| kind | Effect |
|---|---|
| 0 | sets combatant flag `+4 |= 1` => **AP doubled** (this is Hurry; matches the AP<<=1 check in the attack handlers) |
| 1 | SetCondition(4 = Paralysed) - used by the Frost line ("frozen") |
| 2 | SetCondition(7 = Blind) - Blinding line |
| 3 | Berserk: target loses 25% current LP, Strength x1.5, CloseCombat skill x1.5 |

**Per-school function tables** (`0x13e930[]`: S0=0x13e83c Dji-Kas, S1=0x13e890 Dji-Kantos /
Enlightened, S2=0x13e8bc Druid, S3=0x13e8e4 Oqulo-Kamulos, S4=NULL, S5=0x13e920 Zombie, S6=NULL):

| Spell (global id) | fn | Decoded effect (M = mastery multiplier 1..100) |
|---|---|---|
| 1 ThornSnare | 0x9b95e | inflict Paralysed(4); chance scaled M*100/100 (INFERRED roll) |
| 4 Hurry | 0x9bd5f | timed kind 0 (AP x2), duration max(1,M*10/100)+1 rounds |
| 5 ViewOfLife | 0x9bddf | UI: shows target LP (no combat math) |
| 6 FrostSplinter | 0x9c109 | dmg max(1,**M*27**/100) + freeze (kind 1, base 3 => 2..4 rounds) |
| 7 FrostCrystal | 0x9c7ba | dmg max(1,**M*18**/100) + freeze (kind 1) - area per SPELLDAT targets |
| 8 FrostAvalanche | 0x9cf43 | dmg max(1,**M*27**/100) + freeze (kind 1) |
| 9 LightHealing | 0x640c5 | heal max(1, MaxLP*25*M/10000) => **25% of MaxLP** at 100% |
| 10/11/12 BlindingSpark/Ray/Storm | 0x9d6ca/0x9dab3/0x9e292 | timed Blind (kind 2, base 10 => up to 11 rounds); no direct damage |
| 13 SleepSpores | 0x9ea71 | SetCondition(9 = Asleep) |
| 14 ThornTrap | 0x9f123 | places trap; trigger dmg max(1,**M*24**/100) |
| 15 RemoveTrapDK | 0x9fa2d | removes trap |
| **16 HealParalysis** | **NULL** | **no implementation in MAIN.EXE** - casting does nothing (and costs nothing: dispatcher bails before SP deduction) |
| 17 HealIntoxication | 0x641af | ClearCondition(6) on chosen party member |
| 18 HealBlindness | 0x6423d | ClearCondition(7) |
| 19 HealPoisoning | 0x642cb | ClearCondition(1) |
| 20 Fungification | 0x9fd5c | LP-based: ~half of target's current LP as damage, magic-resist gated (INFERRED - x100/x120 factors + sar 1 on GetLifePoints) |
| 21 Light | 0x64359 | ambient light active-spell via fcn.0006085d |
| 31 Regeneration / 33 Lifebringer | 0x643aa / 0x645d1 | shared cont 0x643fa: clears 9 conditions + heal (MaxLP-based) |
| 32 MapView | 0x64571 | utility |
| 34 Teleporter | 0xa068b | map transfer |
| 35 HealingDC | 0x64621 | heal max(1, MaxLP*40*M/10000) => **40% MaxLP** |
| 36 QuickWithdrawal | 0xa1021 | SetCondition(5 = Fleeing) on caster |
| 39 GoddessWrath | 0xa1134 | damage via queued anim callback 0x99778 - **magnitude NOT yet extracted (open)** |
| 40 Irritation | 0xa1799 | SetCondition(11 = Irritated) |
| 41 Recuperation | 0x6470b | full restore via fcn.00068d2f(sheet, 100) |
| 61 Berserk | 0xa1a69 | timed kind 3 (see above), base 10 |
| 62/63/64 BanishDemon(s)/DemonExodus | 0xa1aec (shared cont 0xa1b20) | demon-kill, chance from demon LP (x1000/x250 factors; INFERRED) - area grows per spell |
| 65 SmallFireball | 0xa2055 | dmg max(1,**M*16**/100) (shl 4) |
| 66 MagicShield | 0xa2510 | active-spell type 1, strength max(1,M*10/100) via fcn.000607da |
| 67 HealingD | 0x647bd | heal 40% MaxLP * M/100 (same as 35) |
| 68/69/70 Boasting/Shock/Panic | 0xa2612 (shared cont 0xa2646) | SetCondition(8 = Panicking), chance M*80/100 (INFERRED); area differs per SPELLDAT |
| 91 Fireball | 0xa2f17 | dmg max(1,**M*22**/100) |
| 92 LightningStrike | 0xa33d2 | dmg max(1,**M*33**/100) |
| 93 FireRain | 0xa43c6 | dmg max(1,**M*22**/100) |
| 94 Thunderbolt | 0xa477a | dmg max(1,**M*36**/100) |
| 95 FireHail | 0xa50ef | dmg max(1,**M*20**/100) |
| 96 Thunderstorm | 0xa5aad | dmg max(1,**M*40**/100) |
| 97/98 LightningTrap/Big | 0xa6567 (shared cont 0xa659b) | trap dmg max(1,**M*30**/100) |
| 99/100 LightningMine/Big | 0xa7004 (shared cont 0xa7038) | mine dmg max(1,**M*42**/100) |
| 101 StealLife | 0xa77b3 | dmg max(1, targetMaxLP*30*M/10000) (=30% MaxLP), caster heals same amount |
| 102 StealMagic | 0xa80e3 | drains 30%*M/100 of target Max SP to caster |
| 103 PersonalProtection | 0xa8a3f | active-spell type 2, strength max(1,M*10/100) |
| 104 KamulosGaze | 0xa8b12 | instant-kill via fcn.0004e247 (SetCondition + LP wipe + awards XP) |
| 105 RemoveTrapKK | 0xa92ae | removes trap |
| 151..154 (Zombie magic) | 0xa95a4/0xa96fc/0xa9854/0xa99ac | SetCondition: 151->Panicking(8), 152->Poisoned(1), 153->Irritated(11), 154->Ill(2) |
| 37 Levitation | NULL | no spell-effect fn (handled by 3D engine elsewhere) |

Remake divergence: `src/Game/Combat/Spells/*.cs` placeholders use flat `baseDamage +
strengthScale` - the original is **purely mastery-scaled**: `max(1, K * masteryPct / 100)`,
no caster-attribute scaling at cast time (attributes matter indirectly: MagicTalent drives
mastery growth). The Frost line also paralyses; the Blinding line does NO damage.

### Item 2 - Combat Move rules (CONFIRMED)

`fcn.0004d85b` = `GetValidMoveMask(self)` (774 B; used by both AI and player UI callers
0x508d0/0x50b6f/0x50c6f/0x56c49):

- **Range** = `fcn.0004d773` = `clamp(Speed / 30, 1, 3)` tiles (Speed = effective stat 3).
- **Metric**: 8-directional flood relaxation (direction table `0x134bf4`, (dx,dy) pairs incl.
  diagonals) => **Chebyshev distance**, diagonals cost 1.
- **Team area masks**: party may only stand on rows 3-4 (`0x3FFC0000` over bits row*6+col);
  monsters on rows 0-3 (`0x00FFFFFF`). Monsters may advance into row 3; party never leaves
  its two rows.
- **Blocking**: propagation ignores occupancy (you may path *through* occupied tiles);
  only the **destination** must be empty (`grid[0x176d74 + r*84 + c*14] == NULL`).
  Traps do NOT block movement (they trigger on entry).
- **Tie-breaking / reservation**: `fcn.0004db61` returns the OR of the *last queued move
  destination* of every same-team combatant with a queued Move (`+0x4E == 1`,
  destination = `u16 at c+0x52+2*c[+0x52]`, i.e. last entry of the position list at +0x54
  with count at +0x52). Candidates = `validMask & ~claimedMask` => **first combatant to queue
  a move claims the tile**; later combatants cannot pick it.
- AI move chooser `fcn.0004c256`: picks a uniformly random bit of the candidate mask
  (`fcn.000512e7`), sets action 1 / count 1 / dest.

NOTE: `fcn.00051b51` (punch-list pointer) is only the **move animation** (40-byte anim slot,
frame ticker fcn.00075e71) - no rules in it. Remake divergence: `Battle.MoveCombatant` is a
teleport with no range/area/reservation checks.

### Item 3 - XP award (CONFIRMED)

- Each monster sheet carries a **u16 XP reward at sheet+0x20** (UAlbion `CharacterSheet.Unknown20`!).
- On kill, the death paths (`0x4e2a6` inside fcn.0004e247, and `0x4f671`) do
  `dword[0x15f104] += u16 monsterSheet[+0x20]` (battle total; zeroed at combat init 0x4abda).
- On victory `fcn.000648a7` -> `fcn.00064f8a(total)`:
  `share = max(1, total / livingPartyMembers)`; each **living** member gets
  `SetExperience(sheet, GetExperience + share)` (`fcn.00036d54`), which immediately runs
  `LevelUpCheck (fcn.00037aaa)`. UI message id 200 announces the per-member share.
- No class/level factor on the award itself - flat per-monster field, split evenly.

### Item 4 - Monster action availability mask (CONFIRMED)

The mask is **combatant field +0x06** (NOT a monster-sheet byte):

- Set in combat setup `fcn.0004c937`: every monster gets `mask |= 6`
  (bit1 = melee attempt allowed, bit2 = ranged attempt allowed), and `mask |= 1`
  (bit0 = magic) iff `sheet[+4] != 0` (the spell-class byte, `fcn.000359c2`).
- Per-turn AI `fcn.0004fc99`:
  ```
  while (mask && action not chosen):
      pick = u16[0x4facb + (rand() & 15)*2]      // table: 1,1,1,1,1,1,2,2,4,4,4,4,4,4,2,2
      if (pick not in mask) continue             // => weights: magic 6/16, melee 4/16, ranged 6/16
      mask &= ~pick                              // a failed attempt removes the option
      pick==1 -> fcn.0004fd56 (magic AI; perma-clears bit 0 when SP==0 or conds & 0xF31)
      pick==2 -> fcn.00050651 (melee-preferring: close-range weapon OR natural base damage
                fcn.00037455 != 0; else re-equip from backpack via fcn.00050f52)
      pick==4 -> fcn.00050488 (ranged-preferring: long-range weapon + usability check
                fcn.000513bf (ammo/line); falls back to backpack re-equip)
  ```
- `fcn.00050f52`: monsters **auto-equip the best weapon from their backpack** (24 slots at
  sheet+0x31C) into the right hand (slot 4 / sheet+0x2F8) and return its item type (5/6).
- Conditions `0x331` (Unconscious|Paralysed|Fleeing|Panicking|Asleep) block the normal AI;
  Panicking -> flee handler fcn.0004bff7 (action 4 if in back row, else move away);
  Insane -> fcn.0004c1e4 (50/50 random Move-vs-Attack - previously documented).
- **Flee is not part of the mask** - it only arises from the Panicking condition / Retreat.

### Item 5 - 3D collision radius (CONFIRMED)

3D movement: `fcn.0001e52d` (TryMove) -> `fcn.0001e650` (full move, then X-only, then Z-only
axis slide) -> `fcn.0001e832` (CanMove test):

- Tile size `T` = `word[0x14a4a0]`, set per labyrinth at 3D-engine init (clamped 128..1024
  at 0x943cb; standard maps use 512).
- **Wall margin (collision radius) = `min(T/4, 50)` world units** (`fcn.0001ede1`):
  the sub-tile position `(worldX & (T-1), worldZ & (T-1))` is classified into a 9-zone grid
  (near-edge if within `margin` of a tile edge). A move is rejected when the new sub-tile
  zone touches a blocked neighbour tile (8-direction blocked-mask via table `0x1d640`,
  occupancy test `fcn.0001eeb8` - the `<100 object / >=100 wall` map-contents check).
  Escape clause: if you are *already* in that zone of the same tile, movement is allowed
  (can't get stuck).
- For T=512 => radius = **50/512 ~ 0.098 tile**. Remake divergence:
  `Movement3D.CollisionRadiusTiles = 0.25f` is ~2.5x too large; should be
  `min(tileSize/4, 50)/tileSize` => 0.0977 for standard 512-unit labyrinths.

### Item 6 - 3D tile <-> world transform (CONFIRMED)

Party map position globals: map id `0x153b32`, tile X `0x153b34`, tile Y `0x153b36`,
direction `0x153b38` (0..3). Map width `0x14799a`, height `0x147994` (tiles).

`fcn.0001f1ce` TileToWorld (used by camera init `fcn.0001d670`):
```
worldX = T*(tileX - 1) + T/2
worldZ = T*(mapHeight - tileY) + T/2          // Z axis FLIPPED vs tile Y
```
Inverse `fcn.0001f238`: `tileX = worldX/T + 1; tileY = mapHeight - worldZ/T`.

So the load transform is: **X is 1-based (minus one then center), Z = (height - Y) tiles,
both centered with +T/2**. Camera yaw init: `((-dir*4096) & 0x3FFF) << 16` - 4096 units per
90 degrees, 16384 = full turn, stored 16.16 at `0x14a48c`; camera X/Z are 16.16 at
`0x14a480/0x14a482` and `0x14a486/0x14a488` (fraction word zeroed on load).

### New key globals / functions (this session)

| Addr | Meaning |
|---|---|
| `0x13e930` | per-school spell-fn table pointers (7 dwords) |
| `0x177594` | SPELLDAT.DAT buffer handle |
| `0x1775a0/a2/aa/ae` | cast: school / number / caster sheet / caster combatant |
| `0x1775b4/b8` | cast: target tile bitmask / target mode |
| `0x15f104` | battle XP accumulator (u32) |
| `0x14a4a0` | 3D tile size T (u16) |
| `0x147994/0x14799a` | 3D map height / width in tiles |
| `0x153b32..38` | map id / tile X / tile Y / facing |
| `fcn.0005fdf7` | cast core (SP/LP cost + mastery multiplier) |
| `fcn.000372f4/00037396` | get/set spell mastery (sheet+0x140+school*60+(n-1)*2) |
| `fcn.00060a1e` | SP cost lookup (SPELLDAT rec +1) |
| `fcn.000603ae` | post-cast mastery += MagicTalent |
| `fcn.0005fb21/0005f9a3` | for-each-target-tile / party-slot target wrappers |
| `fcn.0004b8a1` | timed combat effect applier (AP x2 / Paralyse / Blind / Berserk) |
| `fcn.0004d85b/0004d773/0004db61` | move mask / move range / claimed-destination mask |
| `fcn.00036d54/00064f8a/000648a7` | SetExperience / award-XP-split / victory handler |
| `fcn.0004fc99/00050488/00050651/0004fd56` | monster AI: dispatcher / ranged / melee / magic |
| `fcn.00050f52` | monster auto-equip best backpack weapon |
| `fcn.0001e832/0001ede1/0001eeb8` | 3D CanMove / sub-tile 9-zone classify / tile-contents test |
| `fcn.0001f1ce/0001f238` | tile to world / world to tile |
| `fcn.00036a2a/00036a77/00036b17` | SP get/set/max (sheet+0xD0/+0xD2) |

### Open items

- GoddessWrath (39) damage constant - applied inside queued animation callback `0x99778`
  chain; not yet extracted.
- Exact success-roll for condition spells (ThornSnare / Boasting family): chance value
  `M*100/100` resp. `M*80/100` is computed, the comparing rand-roll site not yet pinned.
- Banish-demon family (62-64) chance math (x1000 / x250 factors against demon LP).
- Initial mastery value when a spell is first learned (suspected = LevelRequirement-based
  or fixed; growth per cast = MagicTalent is confirmed).

## Battle view rendering (RE'd 2026-06-12)

> How MAIN.EXE draws the combat scene: grid->screen projection, sprite slots, animation
> selection/timing, projectiles, shadows, and party (non-)rendering. All from radare2
> against `albion_aaa`. CONFIRMED unless marked INFERRED.

### 1. The projection — tile(col,row) -> (screenX, screenY, scale) (CONFIRMED)

The combat scene is a tiny 3D world rendered with a single perspective divide.
World units are stored **x100** (fixed point) in animation slots; "world units" below are
the /100 values.

**Constants:**

| Addr | Value | Meaning |
|---|---|---|
| `0x13e3fe` | u16 = **148** | focal length `f` |
| `0x13e400` | u16 = **83** | camera height `camY` (the old "83<<16" note was wrong — the dword is 83; `sar 16` of it yields 0, so the camZ term is 0) |
| `0x13e146` | u32 = 3200 | half tile width x100 -> tile width = **64** world units |
| `0x13e14a` | u32 = 6400 | tile depth x100 -> tile depth = **64** world units |

Axes: X right, Y up (ground = 0), Z away from the camera. Viewport: the 360x192 combat
backdrop at the top of the 360x240 screen; screen centre x = **180**, horizon y = **96**.

```
denom   = worldZ + 148            // if 0 -> 1; sprite culled entirely when worldZ < -110
screenX = 180 + 148 * worldX / denom
screenY =  96 - 148 * (worldY - 83) / denom      // ground (y=0) -> 96 + 12284/denom
scale   = 148 / denom              // multiplied on top of the sprite's own W%/H%
```

Seen identically in `fcn.000542c9` (sprite renderer), `fcn.00055c10` (grid overlay),
`fcn.0005304a` (projected-X comparator used for projectile facing).

**TileToWorld — `fcn.00053382(col, row, &outX, &outZ)`** (161 B, 15 callers):

```c
if (row == -1) {                       // virtual "party row" (see section 6)
    *outX = (2*col - 5) * 800;         // -> (2c-5)*8  world units
    *outZ = -11000;                    // -> -110 = exactly the cull plane
} else {
    *outX = (2*col - 5) * 3200;        // -> (2c-5)*32 = 64*col - 160
    *outZ = (row == 3) ? -6400/3       // -> -21.33 (row 3 special-cased!)
                       : -(row-2) * 6400;  // -> 128 - 64*row
}
```

**The key table** (tile centre, feet position y=0):

| Row | worldZ | scale = 148/(z+148) | screenY (feet) | screenX(col) |
|---|---|---|---|---|
| 0 (mob back) | +128 | 0.536 | 140.5 | 180 + 0.536*(64*col-160) -> 94..266 |
| 1 | +64 | 0.698 | 153.9 | 180 + 0.698*(64*col-160) -> 68..292 |
| 2 | 0 | 1.000 | 179.0 | 180 + (64*col-160) -> 20..340 |
| 3 (front/contact) | **-21.33** | 1.168 | 193.0 (~viewport bottom edge) | 180 + 1.168*(64*col-160) |
| 4 (party back) | -128 (unused for sprites) | — | — | — |
| -1 (party virtual) | -110 | 3.89 | 107.7 | 180 + 3.89*(16*col-40) -> 24..336 step ~62 |

Row 3 is pulled in to -21.33 instead of -64 so monsters advancing into the contact row
stay (barely) on screen at ~1.17x. Rows 3-4 party combatants are never drawn at all
(section 6). Column spacing: 64 world units; the X centres are symmetric about 180.

**Dotted grid overlay** (`fcn.00055c10`, called from the scene renderer only when
`word[0x13e404] != 0` — the move/target selection mode flag): clip rect set to
(0,96,360,208); colour 194 single-pixel dots. Verticals: 7 lines at x = (2c-6)*32
(c=0..6, i.e. -192..+192 step 64), dotted along z from 15 down to -105 step -10.
Horizontals: 6 lines at z = 32*(5-2r) (r=0..5 -> +160..-160 step 64), dotted along
x -192..+192 step 10. These line positions confirm the 64-unit tile pitch and that row
centres sit halfway between (z = 128 - 64*row).

### 2. Scene renderer & animation-slot system

**`fcn.00053ce9` = RenderCombatScene** (562 B; called from combat init `fcn.0004ac00`,
round controller `fcn.0004b032`, and every frame by the combat screen callback `0x4af0b`):

1. Blit 360x192 combat background (handle at `0x15e940`) at (0,0) via `fcn.0006ffff`.
2. If `word[0x13e404]` -> dotted grid overlay (`fcn.00055c10`).
3. Sort the active slot list `0x167ac4` (count `word[0x176d28]`) by **worldZ descending**
   (qsort `fcn.00070fa6`, cmp `0x53f1b` compares `slot[+0x10]`) — painter's algorithm.
4. Pass 1: draw every slot with flags bit2 set (**shadows / ground decals**);
   Pass 2: draw the remaining slots. Both via `fcn.000542c9`.
5. Debug text "COMOBs : %u" / "Behaviours : %u" when `word[0x147136] > 0`.

**Anim slot (58 B each, 1000-slot pool at `0x168a84`, alloc `fcn.00053871`, free `fcn.0005395f`):**

| Offset | Type | Field |
|---|---|---|
| +0x00 | u16 | flags: bit0 in-use, bit2 hidden (renderer skips), bit3 **has-velocity (auto-move + frame-cycle)**, bit5 moved-this-tick |
| +0x02 | u16 | flags2: bit0 = frame-cycle variant select (fcn.00054ce7 vs fcn.00054c6d), bit2 = draw in pass 1 (shadow layer) |
| +0x04 | u16 | render kind: 0 plain scaled blit, 1 +colour arg, 2/3 LUT-remap blits (slot[+0x32] handle + slot[+0x36] offset; 3 = shadow), 4/7/8 global-LUT blits (`[0x176d14]` +0 / +0x10000 / +0x20000 — translucency tiers), 5 single pixel, 6 2x2 dot (colour byte +0x30) |
| +0x08/+0x0C/+0x10 | i32 | worldX / worldY / worldZ, x100 |
| +0x14/+0x18/+0x1C | i32 | velocity per logic tick, x100 (projectiles) |
| +0x06 | u16 | TTL in logic ticks for velocity slots (slot freed when it hits 0; 0 = no expiry) |
| +0x20/+0x22 | u16 | anchorX% / anchorY% — fraction of the projected size subtracted from screen pos. (50,100) = bottom-centre (standing sprites), (50,50) = centred (effects) |
| +0x24/+0x26 | u16 | width% / height% scale (monster `WidthPercentage`/`HeightPercentage` go here) |
| +0x28 | u16 | current gfx frame index |
| +0x2A | u16 | cached frame count (projectiles — enables auto frame-cycling) |
| +0x2C | u32 | gfx resource handle |
| +0x30 | u8 | colour (kinds 5/6/1) |
| +0x32/+0x36 | u32 | LUT handle / LUT pointer-offset (kind 2/3; shadow uses LUT table `0x17d25c`) |

**`fcn.000542c9` = DrawAnimSlot** (1463 B): does the projection above, then
`projW = gfxFrameW * wScale%/100 * 148/denom` (same for H), anchors
`screenX -= anchorX% * projW / 100`, and dispatches on render kind to the blitters
(`0x9b3f9/0x9b4f1/0x9b6e8/0x9b5f6/0x9b7b1`). Asserts `frame < gfx[+5]` (frame count
byte in the gfx header) at comobs.c:1124. Frame data: 6-byte header {u16 w, u16 h, ...}
+ w*h pixels, frames sequentially packed.

### 3. Monster sprites, shadows, and the x2 frame interleave

**`fcn.00051e0d` = ShowCombatant(c)** — vtable_2 row>=1 sub 0 (monsters only):

```c
sheet = Lock(c->sheet);
gfxId = sheet[0x3AC];                          // MonsterData.CombatGfx
c[+0x0C] = LoadAsset(file 0x26, gfxId);        // monster combat gfx (MONGFX)
c[+0x46] = LoadAsset(file 0x29, gfxId + 12);   // tactical-grid icon — matches UAlbion
                                               // TacticalGfx = CombatGfx.Id + 12 !
slot = AllocSlot(); c[+0x1E] = slot;
TileToWorld(c->col[+0x42], c->row[+0x44], &slot.x, &slot.z);
slot.y      = -(i16)sheet[0x4B8] * 100;        // MonsterData.Unk152 = VERTICAL OFFSET
                                               // (negative value -> hovers above ground)
slot.wScale = sheet[0x4BA];                    // WidthPercentage
slot.hScale = sheet[0x4BC];                    // HeightPercentage
base = HasAnim(c,6) ? sheet[0x470]             // Initial[0] if Initial anim exists
                    : sheet[0x3B0];            // else Move[0]
slot.frame  = base * 2;                        // x2 — see below
c[+0x1C]    = base;                            // idle base frame
slot.anchor = (50, 100);                       // feet at the tile point
slot.kind   = 0; slot.gfx = c[+0x0C];
StartShadow(slot);                             // fcn.000558d2
```

**The x2 multiplier / shadow (`fcn.000558d2` + behaviour callback `0x55265`)**: every
logical animation frame occupies **two physical frames in the combat gfx: even = body,
odd = ground shadow**. The shadow is a second slot: same gfx, `frame = body.frame + 1`,
y = 0 (flat on the ground), anchor (50,50), flags2 bit2 (drawn in pass 1, under everyone),
render kind 3 with shadow colour LUT at `0x17d25c`. A behaviour re-syncs x/z/frame/scales
from the parent every logic tick. (UAlbion's MonsterGfx loader must treat odd frames as
shadow masks, not animation frames.)

**Monster sheet tail (MONCHAR, sheet len 1214 = 0x376 char data + 0x148 MonsterData)** —
all offsets confirmed against UAlbion `MonsterData.Serdes`:

| Sheet offset | MonsterData field | Engine use |
|---|---|---|
| 0x3AC | CombatGfx (u8) | file 0x26 index; +12 -> file 0x29 tactical icon |
| 0x3AD | **Unk37 = per-animation PING-PONG flag bitfield** | bit n set & len>2 -> anim n plays 0..len-1 then back down to 1 (see stepper). NOT an AI mask — UAlbion's placeholder comment is wrong. Values 2 = Melee bounces, 6 = Melee+Ranged, 0x10 = Hit, 0x12 = Melee+Hit. |
| 0x3B0+kind*0x20 | Animations[kind][0..31] | frame lists (logical frames; x2 for physical) |
| 0x4B0+kind | AnimLengths[kind] | 0 = animation absent (`fcn.0004d135(c,kind)` = HasAnim) |
| 0x4B8 | Unk152 | vertical offset: slot.y = -value (flying monsters have negative values) |
| 0x4BA/0x4BC | Width/HeightPercentage | slot scale |

Animation kinds (= UAlbion `CombatAnimationId`): 0 Move, 1 Melee, 2 Ranged, 3 Magic,
4 Hit, 5 Die, 6 Initial, 7 Retreat.

### 4. Animation playback machinery

**Combatant fields:** +0x18 current anim kind (0xFFFF = none), +0x1A frame cursor,
+0x1C idle base frame, +0x1E anim slot ptr, +0x0C combat gfx handle, +0x46 tactical
gfx handle, +4 bit1 = ping-pong reversing, +4 bit3 = "self-animating, skip global stepper".

- **`fcn.0004d0d3(c, kind)` = StartAnim** — if monster & `sheet[0x4B0+kind] != 0`:
  `c[+0x18] = kind; c[+0x1A] = 0`, clear reverse flag.
- **`fcn.0004d36f(c)` = StepAnim** — one frame advance:
  cursor++; frame = `sheet[0x3B0 + kind*0x20 + cursor]`; at end: if ping-pong bit set
  (sheet[0x3AD] & (1<<kind), len>2) bounce back down (reverse until cursor 1), else stop.
  On stop: `c[+0x18]=0xFFFF`, frame = idle base `c[+0x1C]`. Always writes
  `slot.frame = frame*2`.
- **`fcn.0004d2e2` = StepAllMonsterAnims** — steps every monster (kind==2, not flag-bit3).
- **`fcn.0004d268` = WaitAnimEnd** — blocks (running engine frames `fcn.00075e71`)
  until `c[+0x18] == 0xFFFF`. This is how attack handlers synchronise.
- **Idle is a STATIC frame** (Move[0], or Initial[0] before the Initial anim has played).
  No idle frame-cycling. Hovering/ghost monsters get motion from sine behaviours instead.

**`fcn.00075e71` = engine frame**: timing bookkeeping (`fcn.000757d7` — stores elapsed
timer ticks, calls the active screen's callback from the 30-byte screen table at
`0x179164[+0x1A]`), input (`fcn.0007382a`), present (`fcn.00075e9f`). All combat
animation code "waits" by calling this in loops — the whole battle presentation is
synchronous coroutine-style.

**Combat screen per-frame callback (`0x4af0b`)** — the heartbeat:

```c
word[0x15f120] = 0;                       // logic ticks executed this frame
RenderCombatScene();
delta = timer - last;                     // timer = dword[0x1835f0], 60 Hz PIT
accum(word[0x13e14e]) += delta;
while (accum > 2) {                       // ONE LOGIC TICK PER 3 TIMER TICKS = 20 Hz
    fcn.00053fd6();                       //   move velocity-slots, run behaviours, TTLs
    if (++word[0x13e150] == 3) {          //   EVERY 3rd LOGIC TICK = 6.67 Hz:
        fcn.0004d2e2();                   //     step all monster sheet-animations
        fcn.00054bbc();                   //     cycle projectile frames
        word[0x13e150] = 0;
    }
    if (word[0x15f120] < 15) word[0x15f120]++;
    accum -= 3;
}
```

**TIMING (CONFIRMED):** PIT reprogrammed at init (`fcn.0008973c`: out 0x43,0x36;
divisor 0x4DAE = 19886 -> 1193182/19886 = **60.00 Hz**). So: combat logic ticks at
**20 Hz** (50 ms); monster animation frames & projectile sprite frames at **6.67 fps**
(150 ms/frame). A typical 4-frame Melee anim ~ 0.6 s; damage is applied after
WaitAnimEnd returns (the swing fully plays before HP changes). Blocking effect loops
play 1 effect frame per *engine frame*; hit-splash, soul-rise and similar call
`fcn.00075e71` **twice** per frame (half rate). Engine frame rate itself is
machine/vsync-bound (not PIT-throttled) — INFERRED ~60-70 Hz on period hardware.

**Behaviours (the 800x44 B queue at `0x15f144` — these are "Behaviours", not damage
events):** record = {+0 slot ptr, +4 optional 2nd slot, +8 callback, +0xC.. params}.
Run each logic tick by `fcn.00053fd6` (enqueue `fcn.00053ba3`). Known callbacks:
- `0x55265` — shadow sync (copy parent x/z/frame+1/scales).
- `0x54eeb` — **sine oscillator**: `value = sin(2*pi*phase/period)*amplitude`, applied as
  a delta to one of {x, y, z, wScale, hScale, both scales} per `param[+0xC]` (0..5);
  period = rand%40+60 ticks (3-5 s), amplitude = rand%200+400 (4-6 world units).

### 5. vtable_2 — the display vtable is 5 rows x 12 subs (`0x13e280`, CONFIRMED dump)

Row = combatant render class `c[+0x10]` (0 = party; 1 = ground monster; 2 = ghostly;
3 = flying; 4 = variant w/ extra sway). The earlier vtable_2 decode in this file had the
offsets wrong — authoritative dump:

| sub | Row 0 (party) | Rows 1-4 (monsters) | Purpose |
|---|---|---|---|
| 0 | — | `0x51e0d` / `0x526e1` (ghost: kind-8 translucent + 2 sine sways) / `0x5283f` (flying: +y-bob) / `0x52a23` (variant) | **Show** (create sprite + shadow) |
| 1 | — | `0x51fde` | Hide (free gfx handles) |
| 3 | — | `0x52019` | **WalkPath** — see below |
| 4 | `0x51b51` | `0x52189` | party: move-marker effect at dest tile (gfx 0x28/#48, scale 150%, anchor 50/50, 1 frame/engine-frame). monster: z -= 1 nudge, StartAnim(Melee), WaitAnimEnd |
| 5 | `0x51c91` | `0x521d5` | Ranged: party -> LaunchProjectile only; monster -> StartAnim(Ranged) + LaunchProjectile + WaitAnimEnd |
| 6 | — | `0x52243` | **Flee**: StartAnim(Move); 20 ticks of z += 50/tick (runs into the distance); sprite freed |
| 7 | — | `0x522ba` | Magic: StartAnim(Magic), WaitAnimEnd |
| 8 | `0x51cd8` | `0x522f4` | **Hit**: party-row variant = cast-burst gfx 0x28/#47 at aim point (scale 200+4*param %). Monster: slot.z += 20 (pop forward), StartAnim(Hit) + **hit-splash gfx 0x28/#46** over the victim at scale (200+4*param)% — param is the queued event word (damage-scaled), 2 engine-frames per splash frame |
| 9 | — | `0x5241f` (rows 1,2) / `0x528fc` (row 3 flying) | **Die**: z += 20; StartAnim(Die)+wait; demonic targets (`fcn.00036701(sheet) & 0x44`) additionally spawn a **soul-rise**: gfx 0x28/#43, kind 7 (translucent), 2x scale, y driven by word-table at `0x51a97` (scaled by hScale/86), 2 engine-frames/frame. Non-demonic: sprite set to LAST Die frame (corpse). Flying variant `0x528fc`: corpse **falls with gravity** (vy -= 0.16/tick^2, until y <= -40) onto/below the ground, then last-Die-frame corpse |
| 10 | `0x51dd1` | `0x52619` | wrapper -> `fcn.00099233(c, word[arg])` (spell-anim module; big-spell visual on a combatant) |
| 11 | — | `0x52655` | **PlayInitialThenIdle**: if Initial anim exists play it once (battle intro "unfold"), then idle base = Move[0]. Combat init `fcn.0004d028` calls this for every monster, then waits 20 engine frames |

Row selector (`c[+0x10]` = 2/3/4) write site **not located** — INFERRED to come from
monster data during spawn (sheet bytes 0x3AE/0x3AF are never read by code, so it's
probably derived elsewhere; open item).

**WalkPath (`0x52019`)** — monster movement is smooth, not teleport:
takes a path list {count, (col,row)...}. Per tile step: target = TileToWorld(col,row);
then for j = 0..N-1 (N = `fcn.0004d1ac(c,0)` ~ Move anim length): slot position is
linearly interpolated start->target, `c[+0x18]=0` (Move anim) re-asserted and StepAnim
called **every engine frame** (so the walk cycle runs at frame rate, faster than the
6.67 fps idle stepper), one engine frame per interpolation step. Combatant flag bit3 set
during the walk so the global stepper leaves it alone.

### 6. Party members are NOT drawn (CONFIRMED)

- `fcn.0004d028` (ShowAllCombatants, called from combat init) iterates **only the monster
  table** `0x15e944`; no party slot is ever created. vtable_2 row 0 has NULL Show/Hide.
- ShowCombatant reads sheet fields >= 0x3AC which only exist in 1214-byte monster sheets
  (party PRTCHAR is 940 = 0x3AC bytes — the monster block starts exactly where the party
  sheet ends).
- For effect/projectile endpoints, party members get a **virtual position** from
  `fcn.00053208` (GetAimPoint): kind==1 -> TileToWorld(col, row=-1) =>
  world (16*col-40, 80, -109.99) => screen ~ (24 + 62.4*col, 108) — i.e. party melee
  swings, casts and incoming/outgoing projectiles originate/terminate at six points
  spread across the screen just below the horizon, as if the party stood just behind
  the camera. For monsters GetAimPoint returns the sprite's 3/4-height chest point
  (x centred, y = feet + 3/4 * scaledH).

### 7. Projectiles (CONFIRMED)

`fcn.00052b71` = LaunchProjectile(attacker, weaponSlotIdx, targetCol, targetRow)
(both party sub-5 and monster sub-5 use it):

1. **Anim class** (0..9): from the AMMO item's `+0x14` (`ammoAnim`) when the weapon's
   `+0x0E` ammoType != 0 and matching typeid-7 ammo is in the ammo slot (sheet+0x304);
   otherwise from the weapon's own `+0x14`. Asserted < 10 (comshow.c:1608).
2. **Start point** = attacker AimPoint (party virtual point / monster chest).
   **End point** = target occupant's AimPoint, or tile ground point if empty.
3. **Velocity** (`fcn.00052ee8`): `speed = strikeTable[class].word[+0xC] * 100`
   (units x100 per logic tick; data: classes 0-3 -> 10, 4-5 -> 15, 6-7 -> 7, 8-9 -> 12
   world units/tick = 200-300 units/s); `dist = sqrt(dx^2+dy^2+dz^2)`;
   `v = d*speed/dist`; **flightTicks = max(1, dist/speed)** (returned).
4. **Sprite** — the strike table `0x13e370` (10 x 14 B) fields +0/+2/+4/+6 are the
   **four flight-direction variants** of the projectile gfx in file 0x28
   (correcting the earlier "direction/hit variants" guess):
   - target right of shooter on screen (`fcn.0005304a` compares projected Xs) & vz >= 0 (flying away) -> +0
   - left & away -> +2; right & toward camera -> +4; left & toward -> +6
   Fields +8/+10 = width%/height% (150/150 in data — these are scales, not coords);
   +0xC = speed (the old "Frames" column label was wrong).
   Slot: kind 0, anchor (50,50), frame count cached at +0x2A.
5. **Flight**: the slot auto-moves each logic tick (`fcn.00053fd6` adds the velocity;
   slot flag bit3) and its frames cycle at 6.67 fps (`fcn.00054bbc` -> `fcn.00054c6d`);
   the builder blocks `while (flightTicks > 0) { EngineFrame(); flightTicks -= word[0x15f120]; }`
   (0x15f120 = logic ticks executed that frame) then frees the slot. Straight line,
   no arc, no rotation beyond the 4 pre-baked direction sprites.

### 8. Combat-gfx file 0x28 — known effect indices

| Index | Used by | Effect |
|---|---|---|
| 43 (0x2B) | Die handler | soul/wraith rising from demonic corpses (translucent, 2x scale) |
| 46 (0x2E) | Hit handler | impact splash over victim, scale 200%+4*damage-param |
| 47 (0x2F) | party sub-8 | cast-burst at party aim point, scale 200%+4*param |
| 48 (0x30) | party sub-4 | tile move-marker at Move destination, 150% |
| 49..85 | strike table | projectile sprites, 4 direction variants per ammoAnim class |

Monster body gfx come from **file 0x26** (index = `MonsterData.CombatGfx`), tactical-grid
icons from **file 0x29** (index +12). Combat background bitmap handle lives at `0x15e940`.

### 9. Corrections to earlier sections of this file

- `fcn.00051b51` is **not** a "melee animation" — it is the party Move **destination tile
  marker** (gfx 0x28/#48). Its "(50,150)" are anchor (50%,50%) + scale (150%,150%), not
  baseline coordinates.
- `fcn.000526e1` is **not** "the melee animation handler enqueuing 5 sound events" — it
  is the ghostly-monster Show variant; the rand(60..99)/rand(400..599) rolls are
  **sine-sway behaviour periods/amplitudes**, not sound parameters.
- The 800x44 queue at `0x15f144` is the **behaviour queue** (per-tick slot callbacks:
  shadow-sync, sine bobs), not the combat damage-event queue.
- Strike table `0x13e370` fields: 4 x direction-variant gfx ids, w%/h% = 150/150,
  +0xC = projectile speed (10/15/7/12).
- vtable_2 layout corrected: **5 rows** (render classes) x 12 subs; party row only has
  subs 4/5/8/10.
- `0x13e400` holds 83 (camera height), not 83<<16.
- `MonsterData.Unk37` = per-animation ping-pong bitfield; `MonsterData.Unk152` = vertical
  draw offset (negated, world units; flying monsters hover).

### 10. Open items

- Combatant render-class (`c[+0x10]` rows 2-4) selection — where 2/3/4 get written.
- Exact semantics of `fcn.00099233` (sub-10 "big spell visual") and `fcn.00054ce7`
  (alternate projectile frame-cycler, flags2 bit0).
- `0x13e146/0x13e14a` are writable globals (default 3200/6400) — nothing in combat seems
  to change them, but a zoom/config writer may exist (INFERRED constant).
