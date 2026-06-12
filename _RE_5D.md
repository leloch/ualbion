# RE cluster 5D — NPC behaviour constants & small unknowns (RE'd 2026-06-12)

> radare2 against `albion_aaa` (MAIN.EXE, DOS LE). Status tags as in `_RE_NOTES.md`:
> **CONFIRMED** = read directly from disasm, **INFERRED** = consistent interpretation, not byte-traced.
> Runtime NPC state slot = `0x159c58 + idx*0x80` (96 slots — note the array base is 0x159c58,
> not 0x159c5c; 0x159c5c is the Type byte at +4). Offsets below match the C# `NpcState` save layout 1:1.

Shared context discovered while working this cluster (used by several items):

| Global | Meaning | Status |
|---|---|---|
| `0x153b28` | runtime base of the savegame header block (header offset 0 ↔ 0x153b28). MapId=0x153b32 (+0xA), PartyX=0x153b34 (+0xC), PartyY=0x153b36 (+0xE), **PartyDirection=0x153b38** (+0x10) | CONFIRMED |
| `0x153cbc` | **current party leader (member id, word)** = save offset 0x194 — currently inside the remake's `SavedGame.UnkB1` blob (its last 2 bytes). `0x153cbe` (+0x196) = ActiveMembers[6×2], `0x153cce` (+0x1A6) = ActiveItems, `0x153cd2` (+0x1AA) = HoursSinceResting | CONFIRMED |
| `0x153a1c` | per-member-id array of CharacterSheet handles; `0x1597c0` = leader's locked sheet ptr | CONFIRMED |
| `0x15cc5a` | "monster alert" flag: zeroed at top of each NPC tick, set to 1 when any ChaseParty NPC of Type 2/3 currently *detects* the party (see items 1, 3). Also the rest-blocking flag ("too dangerous") | CONFIRMED |
| `0x15cc66` / `0x15cc60` | game-clock movement-slot index / sub-slot fraction 0..1600 (fcn.000437c5; saved at 0x153bf0/0x153bf2). 2D NPCs and 3D waypoint NPCs move at most 1 tile per slot, pixel pos = lerp(prev, next, fraction/1600) | CONFIRMED |
| `0x13eebc` | **frame delta time Δt** in system-timer ticks (fcn.000757d7: `Δt = curTick − prevTick + 1`, fcn.0008ab40 = read timer; initialised to 3). Scales party *and* NPC 3D movement per rendered frame | CONFIRMED |
| `0x14a4a0` | 3D tile size in world units = `1 << labyrinthData[0x1A]` (= remake `LabyrinthData.WallWidth`, EffectiveWallWidth 128..1024), written at 0x1a0d2 (3D map load) | CONFIRMED |
| `0x14a482` / `0x14a488` / `0x14a48c` | party world X / world Y / yaw (16.16, full circle 0x4000) on 3D maps | CONFIRMED |
| `0x178100` | "freeze NPC AI" flag, set/cleared by script.c code at 0x6a24b / 0x6a506 (cutscene/script gate); aborts detection, contact checks and the fatigue check | CONFIRMED behaviour, script-opcode identity INFERRED |
| `0x13da80` | **map-event TYPE handler table** (13 dwords, index = MapEventType−1): 1 MapExit→0x39e7d, … 0xA Encounter→0x3ae03, 0xB PlaceAction→0x3af09 (calls fcn.000666e9), 0xC Query→0x3ca34 (subtype switch @0x3cb5d, 0x2C cases), 0xD Modify→0x3da9a (compare-chain switch on byte+1) | CONFIRMED |

NPC tick architecture (basis for items 1–3):

- **2D tick** `fcn.0003fafe` (from 2d_map.c fcn.000136f1): per active NPC (`+0x17` word != 0), switch on movement type `+0x15`: 0→`fcn.0004022a` (waypoints, only on slot change), 1→`fcn.0004052a` (random wander), 2→`fcn.00040840` (stationary), 3→`fcn.000408fb` (**ChaseParty**), 4→`fcn.0004022a` (waypoints2). Then pixel-lerp loop, then per-NPC contact/event check `fcn.00042237`, then sound update `fcn.000433ef`. CONFIRMED.
- **3D tick** `fcn.000411a3` (from 3D map tick fcn.00019bf6): same switch: 0→`fcn.000414cf`+`fcn.000418b4`, 1→`fcn.00041a80`, 2→`fcn.00041c18` (stationary), 3→`fcn.00041c3c` (**ChaseParty 3D**), 4→`fcn.000414cf`. Then `fcn.00042237`, `fcn.000433ef`. CONFIRMED.
- Movement types: 0=Waypoints, 1=RandomWander, 2=Stationary, 3=ChaseParty, 4=Waypoints2 — matches remake `NpcMovement` exactly (the remake's comment "movement is 2-bit" is only true for V1 maps; V2 maps store 0..4 in MapNpc byte 7). The ChangeIcon dispatcher (`fcn.00020fb9`, case 5 @0x21325) refuses to set values 0 and 4 at runtime. CONFIRMED.

---

## 1. Chase give-up radius — `fcn.00041fbb` (CanSeeParty) — CONFIRMED

One function decides both *acquisition and give-up* (it is re-evaluated every tick of the chase
handler; there is no separate hysteresis). Callers: 2D chase fcn.000408fb @0x4096c, 3D chase
fcn.00041c3c @0x41c7b.

```
CanSeeParty(npcIdx):                              # fcn.00041fbb
    if word[0x178100] != 0: return 0              # script freeze
    dx = partyX(0x153b34) - npc.X(+0x2A)          # TILE coordinates
    dy = partyY(0x153b36) - npc.Y(+0x2C)
    if dx==0 && dy==0: return 1
    if word[0x14799c] != 0:                       # map-has-walls flag (3D maps)
        # Bresenham line walk NPC→party along the major axis, |major|-1 steps,
        # MAP_BlocksSight(fcn.00012f4f, wall flag 0x04) per intermediate tile:
        return 0 if any tile blocks sight else 1  # NO distance cap at all
    else:                                         # 2D maps
        if RestMode(0x147990) == 0 or == 3:       # city / interior maps
            return 1                              # always detected, any distance
        dist = round(sqrt(dx*dx + dy*dy))         # FPU, fcn.00094e60 = sqrt @0x421f3
        return 0 if dist > 10 else 1              # 0xA @ 0x42218
```

- **Constant: 10 tiles. Metric: Euclidean (rounded `sqrt(dx²+dy²)`), not Manhattan.**
- **2D and 3D differ**: 2D outdoor/dungeon = 10-tile Euclidean radius (no LOS check);
  2D city/interior (RestMode 0 or 3) = unlimited; 3D = pure line-of-sight occlusion test
  with **no range limit** (the same wall flag 0x04 the automap uses).
- On detection loss the NPC does not "return": chase sub-state `+0x29` → 2 and it runs the
  random-wander handler each tick until it sees the party again (0x40bf3 / 0x41f45). CONFIRMED.
- Chase movement detail (both maps): dx/dy toward party, sub-state machine at `+0x29`
  {1=direct, 3=rotate 90° CW, 4=90° CCW, 5=180°} with deflect counter `+0x23`
  (2D: 0x10/0x10/0x20 steps; 3D: 0x4B for each), used when the direct step is blocked. CONFIRMED.
- While a ChaseParty NPC of Type 2 or 3 *currently* sees the party, `word[0x15cc5a] = 1`
  (2D @0x4099e, 3D @0x41cad) — feeds rest-gating and the MonsterEye.

**Remake implications:** replace the 16-tile-Manhattan placeholder with: 2D wilderness/dungeon
`round(sqrt(dx²+dy²)) <= 10`; 2D city/interior always-chase; 3D LOS-only (reuse the automap's
BlocksSight = `WallFlags 0x04`), no distance term. Lost-sight behaviour = wander, not return-to-spawn.

## 2. 3D NPC walk speed — `fcn.0004166c` — CONFIRMED

3D RandomWander/ChaseParty NPCs move in continuous world units (waypoint NPCs instead lerp
tile-to-tile on the game clock, fcn.000418b4):

```
step = npc.Speed(+0x42) * Δt(0x13eebc) / 5        # world units this frame (integer div)
(dx,dy) = npc.MoveDelta(+0x3A/+0x3E)              # remaining move vector
dist = round(sqrt(dx² + dy²))
if dist > step: dx = dx*step/dist; dy = dy*step/dist     # clamp vector length to step
try move via fcn.0001e650 (same collision pipeline as the party, see item 4);
on success commit world pos +0x32/+0x36 and refresh tile coords (fcn.0001f238)
```

- **`+0x42` (remake `NpcState.Unk42`) = movement speed.** Map load sets **30** for every NPC
  (fcn.0003fa10 @0x3fabe, called from 3D map enter 0x19a07); the chase handler re-asserts 30
  (fcn.00041c3c @0x41d03 and @0x41f94). No other value is ever written ⇒ effective speed is
  always 30.
- **Per engine tick: 30/5 = 6 world units; per rendered frame: 6×Δt (nominally Δt=3 → 18 units/frame).**
  In tile terms: 6/tileSize tiles per tick — tileSize = `1<<WallWidth` (512 on typical maps ⇒
  ~85 ticks to cross one tile). NPC speed does NOT scale with tile size.
- Party comparison (fcn.0001e28a, keyboard move): forward/back = `67×Δt / 2^(10−WallWidth)`
  world units (×2.5 when the run modifier bit 0x10 is held), turn = 40×Δt yaw units
  (×2.5 run); the two strafe paths use 166×Δt and 100×Δt before the same divisor.
  So on a WallWidth-9 map the walking party does 33.5 units/tick vs the monster's 6 —
  monsters are ~5.6× slower than a walking party. CONFIRMED.
- 3D random wander (fcn.00041a80): every 0xC8=200 ticks (counter `+0x23`) turn the heading
  `+0x25` by ±(70 + rand()%40) degrees (sign 50/50), set MoveDelta = (cos,sin)(heading)×100,
  then walk it at the speed above. CONFIRMED (100.0 multiplier @0x130fb8, 360 divisor @0x130fb0).

**Remake implications:** 3D NPC speed = 6 world units per engine tick (= 30×Δt/5), i.e.
`6/EffectiveWallWidth` tiles/tick; do not scale NPC speed with map tile size; wander = 200-tick
legs with 70–109° turns.

## 3. MonsterEye proximity — `fcn.0001fdb1` / `fcn.0001ff4e` (specitem.c) — CONFIRMED

**There are no distance thresholds and no multi-stage eye.** The status-bar special items are
gated by `ActiveItems` (`word[0x153cce]`, save +0x1A6; 0x147130 show-all forces 0xFFFF):
bit0=Compass (fcn.000200cc), **bit1=MonsterEye**, bit3=Clock (fcn.000201a4) — exactly the
remake's `ActiveItems` enum.

- The eye is **binary**: graphic ptr = `0x13a3b0` (open) when `word[0x15cc5a] != 0`, else
  `0x13a050` (closed). 3D HUD draw @0x1fdf6-0x1fe5c (fcn.0001fdb1); 2D control variant
  @0x2000e-0x2002e (fcn.0001ff4e, control descriptor 0x1350aa, gfx ptr injected at 0x1350c0).
- `0x15cc5a` is set by the chase handlers when a **ChaseParty NPC with Type byte (+4) ∈ {2,3}**
  currently detects the party — i.e. the eye uses the *exact same metric as item 1*:
  2D dungeon/wilderness = Euclidean distance ≤ 10 tiles, 2D city/interior = always,
  3D = unobstructed line of sight. It is recomputed every NPC tick (flag zeroed at tick start).
- So "the eye opens as monsters approach" is just open/closed at the 10-tile detection edge
  (any animation between the two states would be cosmetic, not data-driven). The same flag is
  what blocks Rest ("It's too dangerous here", SYSTEXTS 601).

**Remake implications:** drop the separate 16-tile MonsterEye threshold; the eye should mirror
"is any Type-2/3 ChaseParty NPC currently chasing-capable of seeing the party" (share the
item-1 predicate and the rest-blocking flag — one source of truth, two consumers).

## 4. 3D party collision margin — `fcn.0001ede1` (zone classifier) — CONFIRMED

The party (fcn.0001e52d, struct @0x13504e, slot id 0xFFFF) and 3D NPCs (fcn.0004166c) share
the same collision pipeline: `fcn.0001e650` (try (dx,dy), then dy-only / dx-only axis fallback —
wall sliding) → `fcn.0001e832` (the real test).

Sub-tile zone classifier `fcn.0001ede1(subX, subY)` (subX/Y = worldPos & (tileSize−1)):

```
margin = tileSize/4 > 50 ? tileSize/4 : 50        # i.e. margin = MAX(tileSize/4, 50)
xz = subX < margin ? 0 : subX < tileSize-margin ? 1 : 2
yz = subY < margin ? 0 : subY < tileSize-margin ? 1 : 2
return xz + 3*yz                                  # 0..8, 3x3 grid within the tile
```

- **The remake's `min(tileSize/4, 50)` has the comparison inverted — the original is
  `max(tileSize/4, 50)`** (0x1ee1a: `cmp eax,0x32; jle → 50`). For WallWidth 9 (512-unit
  tiles) the margin is **128** world units, not 50; WallWidth 7 (128) → 50 (over a third of
  the tile!); WallWidth 10 (1024) → 256.
- How it's used (fcn.0001e832): destination tile passability is checked via
  `fcn.0001eeb8` — wall/floor/ceiling records each carry a collision dword tested with bit
  `11 + collisionClass` (party class = `word[0x153ccc]`, save +0x1A4; NPC class = NpcState+0x05).
  If the destination tile is blocked, the engine builds a mask of which of the 8 neighbouring
  tiles are passable (offset/bit table @0x1d640, 6-byte rows) and only allows the move if the
  current sub-tile **zone**'s bit is set and the move direction is compatible (9-case switch
  @0x1ea7d on the zone, checking sign(dx)/sign(dy)) — that is the wall-slide/clamp.
  `0x147130` (the show-all/cheat global) doubles as **no-clip for the party** (0x1e8ff).
- After the wall test, object collision: map-cell contents 1..100 → ObjectGroup (0x42-byte
  record, 8 sub-objects ×8 bytes) → 16-byte object info; `fcn.0001f07e` does an AABB test of
  half-width `word[objInfo+0xC]/2` against the mover, gated by the same `11+class` bit.
  Then **NPC↔party / NPC↔NPC**: only when |Δ| < tileSize on both axes (Chebyshev pre-cull),
  again via fcn.0001f07e using the *other* entity's SpriteOrGroup object record. The party
  itself does not collide with NPCs in this path (arg 0xFFFF skips that block); NPC movers do.

**Remake implications:** change `min(tileSize/4, 50)` to `max(tileSize/4, 50)`; implement the
margin as a 3×3 sub-tile zone + neighbour-passability mask rather than a plain position clamp
if faithful sliding is wanted; `NpcState+5` ("NoClip") is really a **collision class selector**
(wall/floor collision dword bit 11+class), set from MapNpc flag 0x40 on V2 maps.

## 5. SetPartyLeader event Unk2/Unk3 — handler @0x3dde2 — CONFIRMED: unused

Modify-event handler = `0x3da9a` (map-event type 0xD via table 0x13da80). Its prologue loads
byte+2 → var_4, byte+3 → var_8, word+6 → var_C for *all* subtypes, then a compare-chain switch
on subtype byte+1; case **0x1A (Leader)** @0x3dde2:

```
fcn.00031699();                                   # event-UI bookkeeping
memberId = fcn.00035783(word[event+6]);           # resolve party slot/word arg → member id (0xFFFF if absent)
if (memberId != 0xFFFF && fcn.000355b8(memberId)) # TrySetLeader
    fcn.0003165c();
```

**Neither var_4 (Unk2) nor var_8 (Unk3) is ever read in the Leader case, and
`fcn.000355b8(eax=memberId)` takes no other input — bytes 2 and 3 are dead for this event.**
The (3,0) seen in map data is just the residue the map editor left in the generic Modify-event
"amount/flag" slots (other subtypes do use them).

`fcn.000355b8` = TrySetLeader, for reference: fails if the member is not in the party
(fcn.00039941), or has conditions & **0x731** (Unconscious|Paralysed|Fleeing|Panicking|Asleep|
Insane), or (in the 0x15e5ba context) SYSTEXTS 735/543 refusals (one is gated by a
monster-class-bits test fcn.000366b6 vs [0x15e5a4] — INFERRED: the demon-possession/celestial
plot gate); success path writes leader id `[0x153cbc]`, leader sheet ptr `[0x1597c0]`
(via the popup callback 0x35719 / direct @0x356de-0x356ff) and, on 2D maps, swaps the
walking-party sprite (fcn.00017329). The Query event (type 0xC) subtype 0x1A @0x3d317 is
"is X the leader" against the same `[0x153cbc]`.

**Remake implications:** keep serialising Unk2/Unk3 for round-trip but document them as
engine-ignored; the remake's `(3,0)` default is safe. Leader change should be refused for the
0x731 condition mask.

## 6. CharacterSheet offset 0x1C — "home NPC index" for party members — CONFIRMED

Not CD audio, not a zone bitfield. `sheet+0x1C` (u16, remake `Unknown1C`) is the **encoded
map-NPC identity of the party member's walking-NPC**, used with permanent-switch type 3 — the
savegame **RemovedNpcs** bitfield:

- **Join** (AddPartyMember, fcn.000384eb @0x386cf): if the member was recruited from a live
  map NPC (slot != 0xFFFF): if `sheet[0x1C] == 0xFFFF` then
  `sheet[0x1C] = (mapId(0x153b32) − 1) * 96 + npcSlot` (96 = max NPCs/map — same stride as the
  remake's RemovedNpcs bitfield); then `fcn.0003529d(3, sheet[0x1C], 1)` **sets** the bit →
  the NPC disappears from the map.
- **Leave** (RemovePartyMember, fcn.0003822d @0x38274): if `sheet[0x1C] != 0xFFFF` →
  `fcn.0003529d(3, sheet[0x1C], 0)` **clears** the bit → the NPC re-appears on its home map
  at its scripted position. (This is the "read at party-leave time".)
- PRTCHAR data ships 0xFFFF ("not yet placed"); the field is therefore save-mutable state, not
  static config.

**Remake implications:** rename `CharacterSheet.Unknown1C` → e.g. `RemovedNpcIndex` /
`HomeNpcKey`; implement join/leave as set/clear of `RemovedNpcs[(mapId−1)*96 + npcIndex]`;
keep 0xFFFF = none. (fcn.0003529d is the generic permanent-switch writer: type 3 =
RemovedNpcs, type 4 = DisabledChains, type 7 = automap goto-points — stride confirmed 96/map
for type 3, 250/map for type 4.)

## 7. PlaceAction Unk2 — intro/greeting map-text id — CONFIRMED use, role INFERRED

Dispatcher `fcn.000666e9` copies the event into globals before calling the per-type handler
from the 13-entry table @0x13eaf0:

| event byte | global | meaning (per existing notes + this pass) |
|---|---|---|
| +1 Type | 0x17800a | PlaceActionType 0..0xC |
| **+2 Unk2** | **0x178014** | **map-text id, 0xFF = none** |
| +3 Unk3 | 0x178016 | confirm text |
| +4 Unk4 | 0x178012 | success text |
| +5 Unk5 | 0x17800e | param2 (heal %, spell class, …) |
| +6 Unk6 | 0x17800c | price |
| +8 Unk8 | 0x178010 | merchant/inventory id |

Every reader of `0x178014` (0x668d5, 0x66a28, 0x66c50, 0x67032, 0x67219, 0x67583, 0x679cf,
0x67dde — i.e. most of the handlers) does the identical sequence:

```
if (word[0x178014] != 0xFF) {
    txt = fcn.00043d3a(Lock(mapTextHandle 0x178000), unk2)   # fetch map-text string by id
    fcn.0007c646(1, 0, ..., txt)                             # open text/dialog window
}
```

So **Unk2 is a third text id in the map's text table** alongside Unk3/Unk4. It is displayed at
the *start* of the handler in several types (e.g. @0x66a28 before any UI), and inside the
opening branch in others — INFERRED: it is the **greeting / flavour text shown when the place
action begins** (before confirmation), 0xFF meaning "no text". The exact text id semantics per
PlaceActionType were not exhaustively traced (skim level).

**Remake implications:** treat `PlaceActionEvent.Unk2` as `TextId` (map-text) and display it on
activation when != 0xFF; rename to e.g. `IntroText`.

## 8. MapNpc / NpcState remaining unknowns (skim) — mixed

**MapNpc record (10 bytes)** — loader `fcn.0003ec73` (called from map enter fcn.0001183b),
two formats selected by map flag **0x2000 = V2NpcData** (remake name correct); map flag
0x4000 = 96-vs-32 NPCs (remake `ExtraNpcs` correct):

| byte | V2 maps (flag 0x2000) | V1 maps | status |
|---|---|---|---|
| 0 | id → state+0 | same | known |
| 1 "Sound" | → state+0x07: **ambient sound-set index** | → state+0x05 (raw; no looped-sound use found on V1 — state+0x07 forced 0) | CONFIRMED V2 / V1 use INFERRED-dead |
| 2-3 | event index → state+0x13 | same | known |
| 4-5 | SpriteOrGroup → state+0x02 | same | known |
| 6 | bits0-2 = NpcType (3 bits!) → state+4; bit3 → state+0x19 \|=0x02; bit4 → \|=0x20; bit5 → \|=0x10; **bit6 (0x40) → state+0x05 = collision class 1** | bits0-1 = type; bits2-3 = movement; bit4 → state+0x19 \|=0x20 | CONFIRMED |
| 7 | movement type 0..4 → state+0x15 | (part of word 6 flags) | CONFIRMED |
| 8-9 "Unk8/Unk9" | **Triggers word → state+0x11** | same | CONFIRMED (remake already parses word 8 as `Triggers` — the notes-table "MapNpc Unk8/Unk9 bytes" entries are stale and can be closed) |

**NpcState fields decoded this pass** (slot base 0x159c58; names = remake `NpcState`):

| ofs | remake name | decoded meaning | status |
|---|---|---|---|
| +0x05 | NoClip | **collision-class selector** (wall/floor passability bit `11+class`), 0/1 from MapNpc flag 0x40 on V2; on V1 holds MapNpc byte1 | CONFIRMED |
| +0x07 | Sound | ambient **sound-set row 0..12** of the 13×0x28 table @0x13db10; `fcn.0004356a(idx,slot)` plays row's 10-byte sub-entry {sampleId,prio,vol,loop,rate} as a positional looped SAMPLE (fcn.00062f60), slot 2 = chase-alert (played when a chase starts, @0x409d6/0x41ce5), other slots driven by fcn.000433ef | CONFIRMED |
| +0x09/0B/0D/0F | ActiveSfx0-3 | the 4 looped-sample slot handles (0xFFFF = silent) — explains "Unk9 long always −1" | CONFIRMED |
| +0x11 | Triggers | interaction trigger mask; 3D look/interact range check fcn.0001f70a reads it: bit1 (&2) → range 0x1000, bit3 (&8) → 0xC00, bits 2/4/12 (&0x1014) → 0x800 world units + LOS fcn.0001f9f2 (NPC fallback mask 8 when event==0xFFFF and type not 2/3) | CONFIRMED mechanism |
| +0x17 | WasActive | nonzero = slot in use (checked by every per-NPC loop) | CONFIRMED |
| +0x19 | Flags | runtime: bit0 = "scripted/disabled" (skips AI → fcn.00040e25), bit1←MapNpc flag8, bit2 (4) = **is-moving this tick**, bit4/bit5 ← MapNpc flags 0x20/0x10, bit6 cleared each tick | CONFIRMED bits, bit0 semantics INFERRED |
| +0x1F | WaypointDataOffset | offset of this NPC's waypoint/position data in the map blob (0x900 bytes per waypoint NPC = 1152×2, 2 bytes for types 1-3) | CONFIRMED |
| +0x23 | Unk23 | **chase-deflect / wander step counter** (2D deflect 0x10/0x20; 3D deflect 0x4B; 3D wander leg 0xC8) | CONFIRMED |
| +0x25 | Angle | 3D wander heading in degrees 0..359 | CONFIRMED |
| +0x27 | WaypointIndex | current waypoint (movement type 4; type-0 NPCs index by clock slot 0x15cc66) | CONFIRMED |
| +0x29 | Unk29 | **chase sub-state**: 0 idle, 1 chasing, 2 lost, 3/4/5 deflecting (90°CW/90°CCW/180°) | CONFIRMED |
| +0x42 | Unk42 | **3D movement speed** (world units ×Δt/5 per frame; always 30) — see item 2 | CONFIRMED |
| +0x44..4A | OldX/Y, MoveToX/Y | lerp source/target tile for the pixel interpolator | CONFIRMED (names already right) |
| +0x74 | (NpcMoveState) | sprite facing/animation index from the direction tables 0x134c14 / 0x3ec17 (6-byte rows, indexed sign(dx)/sign(dy)); saved/swapped by the dialogue "face each other" helper fcn.00042872 | CONFIRMED |
| +0x7E | (NpcMoveState end) | current tile's **SitMode nibble** (tile-flags bits 23-26 via fcn.000129c6) — chair/bench direction for sit animations | CONFIRMED extraction, SitMode naming INFERRED |
| +0x1B/+0x1D, +0x4C..0x66 | Unk1B/1D, Unk4C+ | not traced this pass (gfx cache block 0x58-0x63 already partially named in remake; 0x4C-0x53 only touched by render-side code) | not found (out of skim budget) |

Bonus (same dispatcher): `fcn.00020fb9` = **ChangeIcon executor**; cases = ChangeType
0 overlay-nibble?, 1 underlay, 2 wall(3D byte0), 3 floor, 4 ceiling, **5 = set NPC movement
(1-3 only)**, **6 = set NPC SpriteOrGroup** (with 2D remove/re-add fcn.0003f9d8/fcn.0003f8ca),
7 = re-point zone event chain (6-byte zone records), 8/9 = block-copy variants
(fcn.000215e2/fcn.00021886), 10 = zone trigger word. CONFIRMED structure.

The dialogue face-each-other helper fcn.00042872 also revealed: on 3D maps NPC "turning" is
done by stepping the **party camera yaw** toward the NPC (0x400/step of 0x4000), i.e. the
*party* turns, NPCs have no facing in 3D. CONFIRMED.

---

### Cross-checks / corrections to other docs (do not edit them — for their owners)

1. `_RE_NOTES.md` automap section says NPC state base "~0x159c5c" — the slot stride base is
   **0x159c58**; 0x159c5c is the Type byte (+4) and 0x159c5a the SpriteOrGroup (+2), which is
   why the automap NPC-marker text reads "ObjectGroup of word(+0x159c5a)".
2. Combat notes' "0x153cbc[] maps member → slot" is imprecise: `0x153cbc` is the **leader id
   word**; the member→slot data is the adjacent ActiveMembers array 0x153cbe.
3. SavedGame.UnkB1's final word (save offset 0x194) = current party leader id.
4. `word[0x153b38]` = party facing (save +0x10), used by TeleportEvent byte3 (0xFF = keep).
