# RE batch 5C (2026-06-12): Light/ambient spell, Levitation, Lockpicking, query opcodes 0xC/0x19/0x1E/0x21, rest interruption

> All radare2 against `albion_aaa`. Companion to docs/re/RE_COMBAT.md and docs/re/RE_5B.md.
> CONFIRMED = read from disassembly with consistent cross-references; INFERRED = strong
> pattern, one detail unverified. Written incrementally; items appear as decoded.

## 1. Light spell, the ambient active-spell entry, and the dungeon light formula (CONFIRMED)

### 1.1 School-0 (Dji-Kas) fn table located: `0x13e83c`

Per-school fn-table pointer array `0x13e930` (7 dwords): S0=`0x13e83c`, S1=`0x13e890`,
S2=`0x13e8bc`, S3=`0x13e8e4` (known), S4=NULL, S5=`0x13e920`, S6=NULL.

School-0 table (21 entries): 1=0x9b95e, 2/3=NULL, 4=0x9bd5f, 5=0x9bddf, 6=0x9c109 (FrostSplinter
region), 7=0x9c7ba, 8=0x9cf43, 9=0x640c5, 10=0x9d6ca, 11=0x9dab3, 12=0x9e292, 13=0x9ea71,
14=0x9f123, 15=0x9fa2d, 16=NULL, 17=0x641af, 18=0x6423d, 19=0x642cb, 20=0x9fd5c (Fungification,
matches docs/re/RE_COMBAT), **21 Light = `0x64359`**.

### 1.2 Light handler `0x64359` — writes {hours=M, pct=M} to the ambient pair; ACCUMULATES on re-cast

```c
void CastLight(int M) {                      // eax = M (spellbook: max(1,(mastery+50)/100); item cast: 50)
    fcn.00062a41(38, 80, 100, 50, 0);        // AV cue (74-xref generic effect/sound helper, gated
                                             // on config byte 0x177f2c bits 2/8/0x10) — INFERRED sound
    fcn.0006085d(strength = M, pct = M);     // merge into the AMBIENT entry 0x153b3a/0x153b3c
}
```

`fcn.0006085d(eax=strength, edx=pct)` is a thin wrapper: `fcn.00060898(entry=0x153b3a, strength, pct)`.
**`fcn.00060898` full decode (127 bytes):**

```c
if (entry->strength != 0) {                  // ACTIVE: accumulate
    entry->strength += strength;             // add word [entry], ax  @ 0x608c6
    entry->pct = max(entry->pct, pct);       // sar-16 high-word read @ 0x608ce, cmp/jle
} else {                                     // EMPTY: plain write
    entry->strength = strength;  entry->pct = pct;
}
```

So a Light cast adds **M game hours** of duration and raises (never lowers) the light percent
to **M**. Re-casting while active EXTENDS the duration (unlike the per-member type-0/1/2 entries,
whose adder `fcn.000607da` drops the call when the entry is non-empty — see docs/re/RE_5B §5b).
Decay: the hour tick `fcn.00043acc -> fcn.000605ed -> fcn.00060670` does
`hours--; if (hours==0) pct=0;` (known). Combat freezes the clock, so Light persists through fights.

### 1.3 How the percent maps to the dungeon light level — **MAX with item light, NOT add/override**

Map **light mode** `word[0x14798c]` is set at map load (`fcn.0001183b` @ 0x11a22):
**`mapHeader[0] & 3`** (bottom 2 bits of the map-flags word; the RestMode known at 0x119ea is
bits 2-3 of the same word). Debug override: when `word[0x14712c] != 0`, mode/`0x147998` come
from `0x153bec/0x153bee` instead.

`fcn.0001473c` = **GetCurrentLightLevel()** (returns 0..100; same logic duplicated in the
3D renderer `fcn.0001aa22` @ 0x1aa41 and the automap-region copy at 0x3d503):

```c
mode = word[0x14798c];                       // mapFlags & 3
switch (mode) {
case 0:  light = 100; break;                 // always lit (cities/2D)
case 1:                                      // DUNGEON
    light = max(fcn.00060796(),              //   ambient ACTIVE-SPELL pct (gated: 0 when hours==0)
                fcn.00038f24());             //   party light-ITEM total (below)
    if (light >= 100) light = 100;
    else if (Race(leaderSheet[0x1597c0]) == 1)   // fcn.0003666b = byte sheet+2 = Race; 1 = ISKAI
        light = min(100, light + 25);        //   Iskai night-sight: +25
    break;
case 2:  light = 100; break;                 // day/night maps: fcn.00014875 routes mode 2 to the
                                             // time-of-day palette path BEFORE calling this fn
default: light = 0;                          // mode 3: pitch black (no map uses it?)
}
if (word[0x147130] != 0) light = 100;        // global "full light" override flag
return light;
```

- **The Light spell does NOT add to torches: the effective level is `max(spellPct, itemLight)`**
  (computed twice in the codegen — once for the compare, once for the value).
- **`fcn.00038f24` = PartyLightItemTotal()**: for every present party member (`fcn.00039941`,
  sheets via `0x153a20[]`), scan all 9 equipment slots (`sheet+0x2E6`) AND all 24 backpack
  slots (`sheet+0x31C`); for each non-empty slot whose ITEMLIST **type byte (+1) == 0x16 (22 =
  UAlbion `ItemType.LightSource`)** read the item's **light value at ITEMLIST+0x13** and
  **SUM** them; return `count ? min(sum, 100) : 0`. (A max is also tracked at var_18h but the
  return uses the sum — torches stack across the party.) Note: light items work from the
  BACKPACK, not just equipped.
- The 0..100 result drives the palette-fade LUT builder `fcn.000148ce` (256-entry table at
  `0x147560`, the 3D distance-fade) and the same three call regions (0x147xx, 0x1aaxx, 0x3d5xx).
- `0x1597c0` = current leader sheet handle (written by fcn.00034b66 @ 0x34cae, read all over
  the 0x21dxx-0x225xx map-menu region).

### 1.4 Levitation (spell 37, school 1 slot 7) — NOT an active spell; it is DEAD (CONFIRMED)

School-1 (Dji-Kantos) fn table `0x13e890` (11 entries): 31=0x643aa, 32=0x64571, 33=0x645d1,
34=0xa068b (Teleporter, matches docs/re/RE_5B), 35=0x64621, 36=0xa1021 (QuickWithdrawal),
**37 Levitation = NULL**, **38 Unused38 = NULL**, 39=0xa1134 (GoddessWrath, matches docs/re/RE_5B),
40=0xa1799 (Irritation), 41=0x6470b (Recuperation).

SPELLDAT record for global 37 (direct file read, `ualbion/ALBION/CD/XLDLIBS/SPELLDAT.DAT`
offset 180): `{env=0x00, cost=8, lvl=4, targets=0x00, unused=0}` — **environment mask 0x00
means castable in NO environment**, and the handler pointer is NULL, so `fcn.00061b96`
(IsSpellImplemented) is false: the spell never appears in the spell-list UI and the dispatcher
would no-op anyway. Record 38 is all-zero. (Sanity anchors: 39 GoddessWrath =
{env=0x20, cost=160, lvl=22, targets=0x20}, 40 Irritation = {env=0x20, cost=18, lvl=8,
targets=0x08} — both consistent with the docs/re/RE_5B target-mask decode.)

**There is no levitation state anywhere**: it writes nothing, no active-spell entry, no flag.
The spell exists only as a name + SPELLDAT row. Holes/shafts in dungeons are traversed by map
events, not by a spell. Remake: keep `Levitation`/`Unused38` unimplemented.

## 2. Lockpicking (CONFIRMED — door.c, "g:\albion\src\door.c" @ 0x131674)

Two completely separate paths, both operating on the lock record `rec` (looked up by
`fcn.00074bda`; fields: **+0x1A = PickDifficulty (u16 0..100), +0x1C = KeyItemId (u16),
+0x1E = trap-armed flag (u16), +0x20 = ptr to result word**). The result word drives the
caller: **1 = lock opened, 2 = unlocked with key, 3 = trap triggered** (the caller then runs
the trap event chain). All messages via `fcn.0002f85d(SystemText id)` — ids match UAlbion's
`SystemText` enum exactly (522 LeaderPickedTheLock ... 538 LeaderCannotPickThisLock).

### 2.1 "Pick the lock" button (UI control record @ 0x13e724, callback `0x5ab33`) — SKILL path

```c
void PickLockButton() {                                // leader sheet = [0x1597c0]
    rec = GetLockRecord(...);                          // fcn.00074bda, assert line 1047
    if (word[0x147130]) { Msg(522); *rec->result = 1; return; }   // debug/cheat: always open
    if (rec->difficulty >= 100) { Msg(523); return; }  // "cannot be picked" — NO trap, retry moot

    skill = GetEffectiveSkill(leader, 3 /*Lockpicking*/);   // fcn.00035fd5 @ 0x5abb4
    if (skill >= rec->difficulty)
        ok = 1;                                        // AUTO-SUCCESS, no roll @ 0x5abc6
    else
        ok = PercentRoll(skill * (100 - rec->difficulty) / 100, 100);  // fcn.00035b15 @ 0x5ac02

    if (ok) { Msg(522); *rec->result = 1; return; }    // opened

    // FAILURE:
    if (rec->trapArmed) {                              // word rec+0x1E
        if (RollStat(leader, 2 /*Dexterity*/))         // fcn.00035b96 = PercentRoll(EffStat,100)
            Msg(524);                                  // trap evaded
        else {
            fcn.00062a41(108, 100, 100, 0, 0);         // trap AV effect
            Msg(525);  *rec->result = 3;               // trap fires (caller runs trap chain)
        }
        rec->trapArmed = 0;                            // @ 0x5ac83 — trap is SPENT either way
    } else
        Msg(538);                                      // "cannot pick" — nothing changes
}
```

- **Success formula:** auto-succeed when `effLockpicking >= difficulty`; otherwise the chance
  is `PercentRoll(skill*(100-difficulty)/100, 100)` — e.g. skill 40 vs difficulty 70 ->
  roll value 12 -> 13 % per attempt (PercentRoll is `rand()%100 <= value`).
  It is NOT RollSkill-vs-100 and there is no difficulty-vs-rand comparison.
- **Trap:** only on a FAILED skill pick, only if `rec+0x1E` is set; the leader gets a
  **Dexterity roll** (vs 100) to evade; **the trap disarms after one trigger/evade** (the
  flag is zeroed in both sub-branches), so at most one trap event per lock.
- **Retries: unlimited.** Failure with no trap (or after the trap is spent) changes no state;
  the player can click Pick again immediately. No time cost, no item cost in this path.
- The picker is always the **party leader** (`0x1597c0`), not a chosen member.

### 2.2 Using an ITEM on the lock (handler `0x5a8f0` region, same lock record) — KEY/LOCKPICK path

In-hand item slot = global `0x153078` (6-byte slot; id at `0x15307c`):

```c
fcn.00062a41(104, 100, 100, 0, 0);                     // "try item on lock" AV cue
item = LockItemlist(slot 0x153078);                    // fcn.0004a521
if (slot.itemId == rec->keyItemId) {                   // +0x1C @ 0x5a92a
    Msg(526);                                          // opened with the key
    if (item->flags[+0x1B] & 0x10) ConsumeOne(slot);   // one-use keys only (vanish flag)
    *rec->result = 2;
} else switch (item->type /*+1*/) {
    case 0x10 /*16 = Key*/:        Msg(528); break;    // "not the right key"
    case 0x15 /*21 = Lockpick*/:
        if (rec->difficulty >= 100) Msg(523);          // unpickable
        else { Msg(527); *rec->result = 1; }           // OPENED — UNCONDITIONAL, no skill roll
        ConsumeOne(slot 0x153078);                     // @ 0x5a9e3 — lockpick ALWAYS consumed,
        break;                                         //   success or not (unpickable lock eats it too)
    default:                       Msg(529); break;    // "cannot open with this item"
}
```

- **The Lockpick item is a guaranteed pick** for any difficulty < 100, with **no skill roll,
  no trap check** (rec+0x1E untouched — the item path can never trigger the trap), and the
  lockpick is **destroyed on every use** (count-1 at 0x153078, cursor cleared if empty).
- A matching key is only consumed if it carries ITEMLIST flag 0x10 ("vanish when used up" —
  same bit the spell-item charge code uses, docs/re/RE_5B §3).
- So: "does a failed pick consume the lockpick?" — the SKILL path never involves the item;
  the ITEM path consumes it always (and only "fails" against difficulty-100 locks).
- The chest UI shares this code: chest.c (string 0x13165c, fns 0x58b00-0x59b00) handles the
  container contents (item stacks capped at 99 @ 0x58c2f); its lock sub-object routes through
  the same door.c lock handlers (no second GetEffectiveSkill(3) call site exists in MAIN.EXE
  besides 0x5abb4; the other two are the stat-sheet UI 0x29277 and the QUERY dispatcher 0x3d7a1).

## 3. Query opcodes 0x0C, 0x19, 0x1E, 0x21 (CONFIRMED — quermod.c dispatcher)

**Dispatcher located** (quermod.c, string 0x130f90): entry ~0x3cb0a. Event record =
`0x153160 + u16[0x13d70a]*50 + 0x18` (same record scheme as the PlaceAction dispatcher);
fields: **+1 = query TYPE, +2 = operation, +3 = immediate byte, +6 = u16 argument**.
`if (type > 0x2B) bail; jmp [0x3ca53 + type*4]` — **44-entry jump table at `0x3ca53`**.
Results merge at 0x3d965. Comparisons go through **`fcn.0003d983(value, op, arg)`** (switch on
op 0..4 + default, semantics = UAlbion's `QueryOperation`: 0 NonZero, 1 <=, 2 <, 3 ==, 4 >=, 5 >).

Jump-table sanity anchors (all match UAlbion's `QueryType` enum): 0x00 Switch ->
`fcn.000350fc(block 0, arg)`; 0x01 ChainActive -> block 4, bit `(mapId-1)*250+imm`
(arg 0 -> current map `word[0x153b32]`); 0x02 Door / 0x03 Chest -> blocks 2/1; 0x04
NpcActiveOnMap -> block 3, `(mapId-1)*96+imm`; 0x0F Gold -> `fcn.00038804` (pooled total);
0x16/0x17/0x18 Gender/Class/Race -> loop members 1..6 comparing sheet byte +1/+3/+2;
0x1A Leader -> `fcn.00035783(arg)` vs `word[0x153cbc]`; 0x23 DemoVersion -> `word[0x134582]`;
0x29/0x2A NpcX/NpcY -> NPC runtime array (below); 0x20/0x25 -> direct fallthrough (no-op).
Bonus: **0x26 (Unk26) = Compare(GetEffectiveSkill(leader, imm), op, arg)** — leader-skill
query (imm 3 = Lockpicking) @ 0x3d796; 0x27 is the same shape with another sheet accessor
(leader attribute, INFERRED).

### 0x0C (UAlbion `UnkC`) = **NPC / party FACING DIRECTION** @ 0x3cddc

```c
if (imm == 0xFF)                                  // PARTY
    return Compare(word[0x153b38], op, arg);      // party facing 0..3
// NPC #imm:
if (word[0x14799c] == 0) return 0;                // only on 3D maps (mapHeader byte2 == 1)
if (word[0x159c6f + imm*128] == 0) return 0;      // NPC slot not active
return Compare(word[0x159ccc + imm*128], op, arg);// NPC facing word (+0x5D in the record)
```

- `0x153b38` is the word straight after partyX `0x153b34` / partyY `0x153b36` (all three are
  copied into the save context in fcn.000149a8). In 3D it is derived from the camera yaw:
  `dir = ((yaw & 0x3FFF) >> 12 + 1) & 3` @ 0x19c97 — i.e. **compass quadrant 0..3**.
- The 128-byte NPC runtime records: active flag +0 (base `0x159c6f`), X +0x13 (`0x159c82`,
  used by query 0x29), Y +0x15 (`0x159c84`, query 0x2A), **facing +0x5D (`0x159ccc`)** —
  confirmed as direction by the NPC sprite-frame math `imul ax, [..0x159ccc], 3` @ 0x40144
  (3 anim frames per facing) and the movement-AI writers (0x3f602 writes constant 2, etc.).
- Note the X/Y queries (0x29/0x2A) do NOT have the 3D-map gate; 0x0C does.

### 0x19 (UAlbion `Unk19`) = **party LEADER knows language #arg** @ 0x3d2d2

```c
sheet  = Lock(leaderHandle [0x1597c0]);
langs  = byte sheet[+8];                          // languages bitmask (Terran 1/Iskai 2/Celtic 4)
Unlock;
result = langs & (1 << argument);                 // arg = language INDEX 0..2, not a mask
```
Operation and immediate are IGNORED; result is the raw bit (nonzero = true).

### 0x1E (UAlbion `Unk1E` "clock tick related") = **time-of-day SCHEDULE TICK vs arg** @ 0x3d402

```c
value = word[0x15cc66] % word[0x15cc64];
return Compare(value, op, argument);
```

- `0x15cc64` = **ticks per day**, set once at game init (`fcn.000437c5`, called from the
  new-game/setup fcn.00034b66): `clockCfg[0x13dd22]->w4 * clockCfg->w8` (= 48 ticks/hour x 24 h
  = **1152**, the well-known Albion NPC-schedule resolution).
- `0x15cc66` = the running **daily tick counter** (initialised from SavedGame word `0x153bf0`,
  advanced by the clock, consumed by all the NPC waypoint-walking code at 0x3ecxx-0x411xx;
  a map-event setter at 0x3e046 stores `arg % cfg->w8`).
- So opcode 0x1E compares the current tick-of-day (0..1151, 48 per game hour) against the
  argument with the usual operation — a finer-grained version of the Hour query (0x12).

### 0x21 (UAlbion `Unk21`) = **"is it light enough to see?"** @ 0x3d4fa

```c
result = 1;                                       // default TRUE
if (word[0x14798c] == 1) {                        // dungeon-light maps only (mapFlags&3 == 1)
    light = min(100, max(AmbientLightSpellPct(),  // fcn.00060796
                         PartyLightItemTotal())); // fcn.00038f24 (see item 1.3)
    if (light < 25) result = 0;                   // 0x19 threshold @ 0x3d5a3
}
return result;                                    // op / imm / arg ALL IGNORED
```
Exactly the item-1 lighting formula minus the Iskai +25 — an Iskai leader does NOT help this
query. True on every non-dungeon-lit map; false only when the party is effectively in the
dark (light < 25, i.e. no Light spell and no/weak light items).

## 4. Rest interruption (CONFIRMED): the ONLY mid-rest abort is a MAP CHANGE

**`fcn.000439b3(hours)` = AdvanceClock** (callers: rest executor 0x68ce1, the map-menu Wait
option 0x2272b, and two time-modify map events 0x3ded8/0x3dfb6):

```c
void AdvanceClock(int hours) {
    word[0x15cc68] = 1;                          // "fast-forward in progress" flag
    fcn.000759ba(0x137cb0);                      // push UI/render context
    startMap = word[0x153b32];                   // CURRENT MAP ID snapshot
    for (h = 0; h < hours; h++) {
        for (t = 0; t < cfg->w8; t++) {          // cfg [0x13dd22]: w8 ticks per hour (48)
            word[0x15cc66]++;                    // daily tick counter (see query 0x1E)
            word[0x153b30] = (tick % 48) * 60 / 48;     // minutes-past-hour for the clock UI
            if (tick % 48 == 0) fcn.00043acc();  // HOUR TICK (spell decay, hunger, events...)
            if (word[0x153b32] != startMap) break;      // <-- ABORT check, after EVERY tick
        }
        if (word[0x153b32] != startMap) break;   // outer loop too
    }
    fcn.00075a0f(); word[0x15cc68] = 0; word[0x15cc58] = tick;
}
```

- **There is no hostile-spawn check, no combat check, no generic event-chain interruption.**
  The single early-out is `currentMapId != startMapId` — i.e. an hour-tick side effect that
  teleports the party to another map (a timed map event / map switch) stops the remaining
  fast-forward. Anything else the hour tick does (monster respawns, NPC schedule movement,
  hunger, active-spell expiry) happens silently and the rest continues.
- **Rest executor `0x68b05`** (the popup's confirmed-rest callback) order of operations:
  palette fade-out (`fcn.0009251d`), **per-member rest recovery loop FIRST**
  (`fcn.00037958` per present member @ 0x68ba8), duration = 8 h if RestMode
  (`word[0x147990]`) == 1, else till-dawn from the hour `word[0x153b2e]` (before 4 h:
  `4 - hour`; from 19 h: `28 - hour`; the "you rest until..." texts via `fcn.0007dc07`),
  per-member wake-condition messages (`fcn.00068d2f`), then `word[0x153cd2] = 0;
  AdvanceClock(duration); word[0x153cd2] = 0;` and fade-in/refresh.
- Because recovery is applied BEFORE the clock advance, even a map-change-aborted rest
  grants the full LP/SP recovery — only the remaining clock hours are skipped.
- `0x153cd2` = **hours-awake counter**: incremented by the hour tick (0x43b19), zeroed on
  rest (both sides of the AdvanceClock call) and by the time-modify events; read by the
  rest-gating popup (0x22098), the exhaustion logic `fcn.00039362`, and the Recuperation
  spell handler (0x64728).

**Remake implication:** rest needs no interruption system — model it as: apply recovery,
then advance the clock hour-by-hour firing hourly logic, stopping only if a map transition
occurs; reset the awake counter.

## New key globals / functions (this batch)

| Addr | Meaning |
|---|---|
| `0x13e83c` / `0x13e890` | school-0 (Dji-Kas) / school-1 (Dji-Kantos) spell fn tables |
| `0x64359` | Light spell handler (ambient {hours+=M, pct=max(pct,M)}) |
| `fcn.00062a41(id,a,b,c,f)` | generic AV cue helper (74 xrefs; config byte 0x177f2c) — Light 38, lock-trap 108, item-on-lock 104 |
| `word[0x14798c]` | map LIGHT MODE = mapHeader.flags & 3 (0 lit, 1 dungeon, 2 day/night, 3 dark) |
| `word[0x14799c]` | "map is 3D" (mapHeader byte 2 == 1) |
| `word[0x147990/96/98/9a]` | RestMode / hdr+8 / hdr+3 or 0x153bee / hdr+4 (map-load copies) |
| `word[0x147130]` | master cheat/debug flag: full light + auto-pick locks + query 0x2B |
| `0x14712c` / `0x153bec..` | map-load debug override switch / override light-mode pair |
| `fcn.0001473c` / `fcn.00014875` / `fcn.000148ce` | GetCurrentLightLevel / wrapper (routes mode-2 day/night) / palette-fade LUT builder (0x147560) |
| `fcn.00038f24` | PartyLightItemTotal: sum ITEMLIST+0x13 over type-22 items, equip+backpack, all members, cap 100 |
| `fcn.0003666b` / `fcn.0003658a` / `fcn.000365d5` | sheet byte accessors: Race(+2) / (query 0x16/17 gender-class family) / (query 0x13) |
| `0x1597c0` | current leader sheet handle (writer fcn.00034b66 @ 0x34cae) |
| `0x5ab33` / `0x5a8f0` | door.c PickLock button callback / use-item-on-lock handler |
| lock record +0x1A/+0x1C/+0x1E/+0x20 | PickDifficulty / KeyItemId / trap-armed / result ptr (1 open, 2 key, 3 trap) |
| `0x153078` (+id `0x15307c`) | in-hand ("mouse cursor") item slot |
| `fcn.00035b96(sheet, stat)` | RollStat = PercentRoll(EffStat, 100) — trap evasion uses stat 2 DEX |
| `0x3ca53` | QUERY dispatcher jump table (44 entries; record +1 type +2 op +3 imm +6 arg) |
| `fcn.0003d983(value, op, arg)` | query comparator (QueryOperation semantics) |
| `fcn.000350fc(block, bit)` | switch-block bit test (0 switches, 1 chests, 2 doors, 3 npc-active, 4 chains) |
| `0x159c6f + npc*128` | NPC runtime record: +0 active, +0x13 X, +0x15 Y, +0x5D facing |
| `0x153b2e/30/32/34/36/38` | hour / minute / current MAP ID / party X / party Y / party facing 0..3 |
| `0x15cc64` / `0x15cc66` / `0x15cc68` / `0x15cc58` | ticks-per-day (48x24=1152) / daily tick counter / fast-forward flag / last-tick mirror |
| `0x13dd22` | clock-config ptr (w4 x w8 = ticks/day; w8 = 48 ticks/hour) |
| `fcn.000439b3(hours)` | AdvanceClock — fires hour tick per 48 ticks; aborts ONLY on map change |
| `0x153cd2` | hours-awake counter (hour tick ++, rest/time events zero it; exhaustion + rest gating read it) |

