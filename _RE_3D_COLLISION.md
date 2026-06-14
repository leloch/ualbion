# RE: Albion 3D (first-person dungeon) tile passability / collision

Reverse-engineered from `MAIN.EXE` via radare2 project `albion_aaa` (DOS LE).
Purpose: explain the remake's two-way 3D collision bug (party walks OVER water; gets STUCK on
open tiles) and give the exact original rule for "can the party enter / pass through 3D tile (x,y)?".

All instruction addresses are file/virtual offsets as reported by radare2 (`pdf @ <addr>`).

---

## KNOWN FACTS (addresses)

| Symbol | Addr | Role | Confidence |
|---|---|---|---|
| `fcn.0001e650` | 0x1e650 | **Top-level 3D mover.** Takes a move-request struct, decomposes the move into axis sub-steps, calls `fcn.0001e832` per sub-step. Called from `fcn.0001e52d` (party) and `fcn.0004166c` (3D NPC chase). | CONFIRMED |
| `fcn.0001e832` | 0x1e832 | **Collision arbiter for one (sub)step.** Quantises start+end world position to tiles, runs the 9-zone sub-tile test, the 8-neighbour wall-hug test, and the object/NPC overlap tests. Returns 1 = move blocked, 0 = move allowed. | CONFIRMED |
| `fcn.0001ede1` | 0x1ede1 | **9-zone sub-tile classifier.** Given (subX, subY) inside a tile, returns a zone code 0..8 (3×3 grid). Margin = `MAX(tileSize/4, 50)`. | CONFIRMED |
| `fcn.0001eeb8` | 0x1eeb8 | **Tile-solidity predicate.** Given a tile (x,y) + a direction bit, returns 1 = tile is *passable in that direction*, 0 = solid/blocked. Tests the WALL, the FLOOR, and the CEILING collision fields. | CONFIRMED |
| `fcn.0001f07e` | 0x1f07e | **Object-group overlap test.** Loops the 8 sub-objects of an object-group, tests each sub-object's collision bit + a bounding-box overlap against the party's swept position. | CONFIRMED |
| `fcn.0001f238` | 0x1f238 | World→tile quantiser (returns tileX, tileY for a world coord; uses tileSize `[0x14a4a0]`). | INFERRED (helper) |
| `[0x14a4a0]` | data | **tileSize** (world units per tile = `EffectiveWallWidth = 1<<WallWidth`). word. | CONFIRMED |
| `[0x14799a]` | data | **map width** (tiles). word. | CONFIRMED |
| `[0x147994]` | data | **map height** (tiles). word. | CONFIRMED |
| `[0x14ff50]` | data | per-content-value table of offsets to wall collision records (indexed `content*4`). | CONFIRMED |
| `0x1d640` | data | **8×6-byte neighbour table** (dx,dy,zone-mask) used by the wall-hug test. | CONFIRMED |

---

## 1. THE EXACT PASSABILITY PREDICATE — `fcn.0001eeb8`

This is the heart of the answer. Signature (register args, Watcom-ish):

```
fcn.0001eeb8(ax = tileX, dx = tileY, ebx = ?, cx = dirBit) -> 1 = passable, 0 = blocked
```

Decompiled logic:

```c
int tilePassable(short tileX, short tileY, short dirBit) {
    int passable = 1;                              // 0x1eed9: default = passable

    // --- (a) bounds check: off-map is SOLID ---
    if (tileX < 1 || tileX > mapWidth  ||           // 0x1eee0..0x1eef5  ([0x14799a])
        tileY < 1 || tileY > mapHeight)             // 0x1eefb..0x1ef12  ([0x147994])
        return 0;                                    // 0x1ef16 (blocked)

    byte* tile = tileBase + 3*(tileX-1) + 3*mapWidth*(tileY-1);  // 3 bytes per tile
    byte content = tile[0];   // 0x1ef85  WALL / object-group byte
    byte floor   = tile[1];   // 0x1efd9  FLOOR index
    byte ceiling = tile[2];   // 0x1f020  CEILING index

    // --- (b) WALL test ---
    if (content >= 0x65) {                           // 0x1ef93  (>= 101 => it is a wall)
        uint coll = *(uint*)(labBase + wallOffsetTable[content]); // 0x1ef9e..0x1efac
        if (coll & (1u << (dirBit + 11)))            // 0x1efb7..0x1efc1
            passable = 0;                            // 0x1efc6
    }

    // --- (c) FLOOR test ---
    if (passable && floor != 0) {                    // 0x1efd2 / 0x1efe4
        uint coll = *(uint*)(fcArray + 0xA*(floor-1));   // 0x1efed (stride 0xA = FloorAndCeiling rec)
        if (coll & (1u << (dirBit + 11)))            // 0x1effe..0x1f008
            passable = 0;                            // 0x1f00d
    }

    // --- (d) CEILING test ---
    if (passable && ceiling != 0) {                  // 0x1f019 / 0x1f02b
        uint coll = *(uint*)(fcArray + 0xA*(ceiling-1));  // 0x1f034
        if (coll & (1u << (dirBit + 11)))            // 0x1f045..0x1f04f
            passable = 0;                            // 0x1f054
    }

    return passable;                                 // 0x1f06f
}
```

### What this means, point by point

1. **Off-map = solid.** A tile outside `[1..width] × [1..height]` blocks. (The remake already does this in `Collider3D.IsOccupied`.) Note the original is **1-based** with an inclusive upper bound; the playable tile range is `1..width`/`1..height`.

2. **A WALL blocks** when `content >= 101` AND the wall's `Collision` field has the
   relevant direction bit set. **It is NOT "any non-zero wall blocks".** The block decision is
   per-direction, read from the wall record's 24-bit `Collision` field (the remake's
   `Wall.Collision`). See §3 for the bit layout.
   - Content `== 100` is the "empty wall slot" marker (passable); `>= 101` is a real wall
     (`wallIndex = content - 100`, i.e. remake `Walls[content-100-1]`). The `>= 0x65` threshold
     matches the remake's `WallOffset = 100` exactly.
   - Content `< 100` is an **object-group** index — it is NOT tested here at all. Object-group
     collision is a separate, geometric test (`fcn.0001f07e`, §4).

3. **The FLOOR blocks** by the SAME collision-bit mechanism, read from the tile's
   `FloorAndCeiling` record. **This is where WATER fits.** Water is a *floor type*; the
   water floor record has the collision direction bits set, so stepping onto a water tile is
   blocked by the floor test exactly like a wall is blocked by the wall test. There is no
   separate "is-water" check — water blocks because its floor record is flagged solid.

4. **The CEILING blocks** by the same mechanism (rarely used for movement, but a low/solid
   ceiling can block — same field, same bits).

5. **The collision field is read as a 32-bit dword** from the first 4 bytes of the record:
   - Wall: a 66-byte (`0x42`) in-memory record; the disk field is the 3-byte `Collision`
     at wall-record offset 1.
   - FloorAndCeiling: the **first dword of the 10-byte (`0xA`) record**, i.e. bytes
     `Properties(0) | Unk1(1) | Unk2(2) | Unk3(3)`. The tested bits 11..14 therefore fall in
     **`Unk1`** (byte 1 = bits 8..15). See §3.

CONFIDENCE: CONFIRMED (full disassembly of `fcn.0001eeb8`).

---

## 2. THE COLLISION BIT — direction-keyed, bit `(dir + 11)`

Both `fcn.0001eeb8` (walls/floors/ceilings) and `fcn.0001f07e` (objects) test:

```
blocked_in_this_direction = collisionField & (1 << (dir + 11))
```

- `dir` is the move-request "direction/mode" word (struct field `+0x12`, threaded as `cx` →
  `var_1ch`). For the 4 cardinal moves it is 0..3, so the tested bits are **11, 12, 13, 14**.
- So the `Collision` field carries **4 directional "blocks entry from side N/E/S/W" bits**
  in bits 11–14, plus other bits used elsewhere (rendering / sight / auto-graphics).
- A fully-solid wall/floor has all four of bits 11–14 set (blocks from every side).
- This per-direction encoding is why "transparent-but-solid" and one-way / partial barriers
  are possible in the data: a wall can block some approach directions and not others.

CONFIDENCE: CONFIRMED for the bit index `(dir+11)` and that the same predicate is used for
walls, floors, ceilings and objects. INFERRED that `dir` is the cardinal index 0..3 (it is the
quantised facing/move axis from the mover; exact 0..3 ↔ N/E/S/W mapping not byte-traced — mark
as "direction selector", value range 0..3).

---

## 3. WHERE THE BITS LIVE (disk format / remake fields)

### Wall (`Wall.cs`, disk record, `Wall.Collision` 24-bit @ offset 1)
The tested dword is the wall's collision field; bits 11–14 = the 4 directional blockers.
The remake already deserialises `Wall.Collision` as the full 24-bit value, so
`(Collision & (1 << (dir+11))) != 0` is directly computable. The remake's current
`Collider3D` ignores `Wall.Collision` entirely and instead treats *any non-null wall* as
blocking — that is the simplification that causes "stuck on open tiles": a wall whose
`Collision` does NOT set bits 11–14 (decorative / passable-archway walls) is genuinely
walkable in the original but blocked in the remake.

### FloorAndCeiling (`FloorAndCeiling.cs`, 10-byte record)
The tested dword = first 4 bytes = `Properties(byte0) | Unk1(byte1) | Unk2(byte2) | Unk3(byte3)`.
Bits 11–14 of that dword are **bits 3–6 of `Unk1`**.
- i.e. `Unk1 & 0x08` = block from dir 0, `Unk1 & 0x10` = dir 1, `Unk1 & 0x20` = dir 2,
  `Unk1 & 0x40` = dir 3.
- The remake's `FloorAndCeiling.FcFlags` enum (`NonWalkable = 1<<2`, `Walkable = 1<<5`, etc.)
  is the **`Properties` byte (byte 0)** and is NOT what the original collision code reads.
  The original ignores `Properties` for movement and reads the directional bits out of `Unk1`.
- **WATER**: a water floor record has these `Unk1` bits set, so `floor != 0` + bit test ⇒
  blocked. (The remake never looks at the floor record's collision bits at all — it only checks
  `floorIndex == 0` (= "no floor / pit"). Water has a *non-zero* floor index, so the remake's
  check passes and the party walks over water. This is the "walk over water" half of the bug.)

CONFIDENCE: CONFIRMED that the FC collision dword = first 4 bytes of the record and the tested
bits are 11–14 (= `Unk1` bits 3–6). INFERRED (could not load a decoded water LABDATA record to
read the literal `Unk1` value) that the specific water floor type sets these bits — but the
*mechanism* by which water blocks is certain: it is the floor collision-bit test, not a pit
test.

### LabyrinthObject (`LabyrinthObject.cs`, 16-byte / `0x10` record, `Collision` 24-bit @ offset 1)
Same 24-bit `Collision` field; bits 11–14 are the directional blockers (§4).

---

## 4. OBJECT-GROUP COLLISION — `fcn.0001f07e`

Object-groups (tile content `< 100`) do NOT block the whole tile. They are tested
geometrically against the party's swept position:

```c
// args: dx0=ax(curOffX), dy0=edx(curOffY), arg_14h = objectGroup ptr,
//       arg_18h = LabyrinthObject array base, arg_10h = dirBit, + sweep deltas in ebx/ecx
for (i = 0; i < 8; i++) {                              // 0x1f0ad  (8 sub-objects per group)
    short objInfoNo = group[i].objectInfoNumber;       // +8 in the 8-byte SubObject  (0x1f0d1)
    if (objInfoNo == 0) continue;                       // 0x1f0dd

    LabyrinthObject* o = &objArray[objInfoNo - 1];      // stride 0x10 = 16  (0x1f0e9)
    if ((o->Collision & (1 << (dirBit + 11))) == 0)     // 0x1f0fd..0x1f10c
        continue;                                        // not solid in this direction

    // bounding-box overlap of the object footprint vs the party sweep
    int objX = group[i].x + var_2ch;                    // 0x1f120  (SubObject +2)
    int objY = group[i].y + var_28h;                    // 0x1f136  (SubObject +4)
    int half = o->MapWidth / 2;                          // 0x1f143  (LabyrinthObject +0xC), /2
    // if party sweep box overlaps [objX-half, objX+half] x [objY-half, objY+half] -> blocked
    if (overlap(...)) { result = 1; break; }            // 0x1f1af
}
return result;  // 1 = an object blocks the move
```

So an object blocks only if (a) its `Collision` bit for the move direction is set AND (b) the
party's swept circle/box actually overlaps the object's footprint (`MapWidth`×`MapHeight` at
`+0xC`/`+0xE`, halved). Decorative / floor-marking objects (footprint 0 or collision bit clear)
do not block. The remake's current `Collider3D` heuristic (any sub-object with non-zero 8-bit
`Collision` and not `FloorObject` blocks the whole tile) is a coarse approximation: it ignores
the directional bit and the footprint geometry, so it can both over-block (props you should be
able to brush past) and mis-handle direction.

CONFIDENCE: CONFIRMED (full disassembly). The exact overlap inequalities are present at
0x1f159–0x1f1ab; summarised above as an AABB overlap with half-extent `MapWidth/2`.

---

## 5. THE SUB-TILE 9-ZONE MARGIN TEST — `fcn.0001ede1`  (CONFIRMS the remake comment)

```c
// fcn.0001ede1(ax = subX, dx = subY) -> zone 0..8
int zone(short subX, short subY) {
    int margin = tileSize / 4;          // 0x1edfe..0x1ee17  ([0x14a4a0] / 4, arithmetic)
    if (margin <= 50) margin = 50;      // 0x1ee1a  cmp 0x32 ; -> MAX(tileSize/4, 50)

    int zx;                              // 0x1ee4e..0x1ee75
    if      (subX <  margin)            zx = 0;   // near low edge
    else if (subX >= tileSize-margin)   zx = 2;   // near high edge
    else                                zx = 1;   // centre band

    int zy;                             // 0x1ee7f..0x1eea3
    if      (subY <  margin)            zy = 0;   // +0
    else if (subY >= tileSize-margin)   zy = 6;   // +6
    else                                zy = 3;   // +3

    return zx + zy;                     // 0x1eea7  -> 0..8
}
```

Resulting 3×3 zone grid (subX increases →, subY increases ↓):

```
   0 1 2      (top edge band)
   3 4 5      (middle band)
   6 7 8      (bottom edge band)
```

The wall-margin is therefore **`MAX(tileSize/4, 50)` world units** — CONFIRMING the remake's
note verbatim. Zone 4 = "well inside the tile, clear of all edges".

### How the zone drives the 8-neighbour wall-hug test (in `fcn.0001e832`)

`fcn.0001e832` loops the **8 neighbour tiles** using the table at `0x1d640`
(8 rows × 6 bytes = `dx[word], dy[word], zoneMask[word]`):

| i | dx | dy | zoneMask | covers zones |
|---|----|----|----------|--------------|
| 0 | -1 | -1 | 0x001 | 0 (NW corner) |
| 1 |  0 | -1 | 0x007 | 0,1,2 (N edge) |
| 2 | +1 | -1 | 0x004 | 2 (NE corner) |
| 3 | -1 |  0 | 0x049 | 0,3,6 (W edge) |
| 4 | +1 |  0 | 0x124 | 2,5,8 (E edge) |
| 5 | -1 | +1 | 0x040 | 6 (SW corner) |
| 6 |  0 | +1 | 0x1c0 | 6,7,8 (S edge) |
| 7 | +1 | +1 | 0x100 | 8 (SE corner) |

For each neighbour, the loop (0x1e970..0x1e9dc) calls `fcn.0001eeb8` on that neighbour tile;
if the neighbour is **solid**, it `OR`s the neighbour's `zoneMask` into a "forbidden zones"
bitset (`var_18h`). After the loop it computes the party's *destination* zone via
`fcn.0001ede1` and (0x1e9f2..0x1ea06) checks `forbiddenZones & (1 << destZone)`. If the party's
sub-tile position would land in a zone that hugs a solid neighbour, the move is rejected — this
is the wall-hugging margin that stops you from clipping into the corner/edge of an adjacent
solid tile even when your own tile is free. The switch at 0x1ea7d further allows sliding
*along* a wall (per-zone, it permits the component of motion that moves away from the blocking
neighbour) — that is why you slide along walls instead of dead-stopping.

CONFIDENCE: CONFIRMED (table bytes dumped from 0x1d640; loop + mask logic disassembled).

---

## 6. CONTROL FLOW OF ONE STEP — `fcn.0001e832` (summary)

1. Quantise current pos → `(tileX0, tileY0)`; quantise current+delta → `(tileX1, tileY1)`
   (two `fcn.0001f238` calls, 0x1e868 / 0x1e87f).
2. Compute sub-tile offsets within the destination tile (`AND (tileSize-1)`).
3. `fcn.0001ede1` → destination zone (`var_ch`).
4. If destination tile == source tile **and** same zone (no edge crossed) → allow (0x1e8e6,
   the cheap "still inside the same safe cell" path).
5. Else: run the **8-neighbour wall-hug test** (§5). If destination zone is forbidden → reject
   (`fcn.0001eeb8` per neighbour + the slide switch). 
6. Then run **object/NPC overlap** tests (`fcn.0001f07e`) for the destination tile and the
   monster list at `0x159c5a` (per-NPC records, stride 0x80) — blocks if a solid object or an
   NPC body overlaps the swept position.
7. Return 0 = allowed, 1 = blocked. The result is consumed by `fcn.0001e650`, which retries
   axis-decomposed (X-only, then Y-only) to permit sliding.

CONFIDENCE: CONFIRMED for the structure; the monster-overlap detail (NPC table `0x159c5a`,
stride 0x80) is INFERRED from the shifts (`shl eax,7`) and matches the 128-byte runtime NPC
record used elsewhere.

---

## 7. WHY THE REMAKE IS WRONG (both directions)

The remake `Collider3D.IsOccupied` (src/Game/Entities/Map3D/Collider3D.cs):

```
wall != null                  -> blocked   // ANY wall blocks
floorIndex == 0               -> blocked   // "no floor" pit
sub-object Collision!=0 etc.  -> blocked
```

Versus the original:

| Original rule | Remake | Effect of the mismatch |
|---|---|---|
| Wall blocks **iff** `Wall.Collision & (1<<(dir+11))` | "any wall blocks" | **STUCK on open tiles**: decorative / archway walls whose Collision bits are clear are walkable in the original but blocked here. |
| Floor blocks **iff** `FloorAndCeiling.Unk1 & (0x08<<dir)` (this is how **water** blocks) | only `floorIndex==0` blocks | **WALK OVER WATER**: water has a non-zero floor index, so the remake lets you onto it; the original blocks it via the floor collision bits. Conversely a genuinely-empty `floorIndex==0` tile that the original treats as solid-by-floor-record-absence is handled differently. |
| Ceiling can block via same bits | not checked | minor; low ceilings not modelled. |
| Object blocks iff `Collision` direction bit set **and** AABB footprint overlaps | any non-floor sub-object with `Collision!=0` blocks whole tile | over/under-blocks props; ignores direction + footprint. |
| Off-map blocks; tiles are **1..width / 1..height** (1-based, inclusive) | 0-based `< Width`/`< Height` | edge-row indexing differs — check the boundary tiles. |

---

## 8. IMPLEMENTATION RECOMMENDATION FOR THE REMAKE

Replace the predicate in `Collider3D.IsOccupied` (and mirror in `LogicalMap3D`) with the
original's bit-driven rule. Pseudocode (per destination tile + move direction `dir` 0..3):

```csharp
static bool BlocksDir(uint collision24, int dir) => (collision24 & (1u << (dir + 11))) != 0;

bool TileBlocks(int x, int y, int dir)
{
    if (x < 0 || y < 0 || x >= Width || y >= Height) return true;        // off-map solid

    // WALL  (content >= 100 -> wall; Wall.Collision is the 24-bit field)
    var (wallIdx, wall) = GetWall(x, y);
    if (wall != null && BlocksDir(wall.Collision, dir)) return true;

    // FLOOR  (THIS is how water blocks). The collision bits are the first dword of the
    // 10-byte FloorAndCeiling record = Properties|Unk1|Unk2|Unk3.  Bits 11..14 == Unk1 bits 3..6.
    var (floorIdx, floor) = GetFloor(x, y);
    if (floor != null && BlocksDir(FcCollisionDword(floor), dir)) return true;

    // CEILING  (same mechanism)
    var (ceilIdx, ceil) = GetCeiling(x, y);
    if (ceil != null && BlocksDir(FcCollisionDword(ceil), dir)) return true;

    // OBJECTS  (geometric: per sub-object Collision dir-bit + AABB footprint overlap)
    // keep current loop but gate on the direction bit and the MapWidth/MapHeight footprint
    // rather than treating any Collision!=0 sub-object as a full-tile block.
    ...
    return false;
}

// FloorAndCeiling needs its collision dword exposed. The remake currently splits the first
// 4 bytes into Properties + Unk1..Unk3; reconstruct:
static uint FcCollisionDword(FloorAndCeiling fc) =>
    (uint)fc.Properties | ((uint)fc.Unk1 << 8) | ((uint)fc.Unk2 << 16) | ((uint)fc.Unk3 << 24);
```

Minimal-change variant if direction threading is too invasive right now:
- Treat a wall/floor/ceiling as solid if **any** of bits 11–14 are set
  (`(collision24 & 0x7800) != 0`). This makes water block and decorative walls pass, fixing
  both halves of the bug, at the cost of not modelling one-way/partial barriers. This is a
  strict improvement over the current "any wall / floorIndex==0" rule and is safe to ship first.
- Then add the per-direction `dir` and the 9-zone / 8-neighbour wall-hug for pixel-faithful
  sliding (port `fcn.0001ede1` + the `0x1d640` table).

The crucial single fix for the reported bug: **stop using `wall != null` / `floorIndex == 0`
as the block test; instead test bits 11–14 of the wall's `Collision` and of the floor record's
collision dword (`Unk1` bits 3–6).** Water blocks because its floor record sets those bits;
open archways pass because their wall record does not.

---

## 9. COULD NOT DETERMINE (open items)

- **Exact `dir` ↔ cardinal mapping (0..3 = N/E/S/W?)** and whether diagonal moves use a 5th
  value. The bit base `(dir+11)` and range 0..3 are confirmed; the literal compass assignment
  is not byte-traced. (Low risk: the symmetric "any of bits 11–14" fallback sidesteps it.)
- **Literal `Unk1` value of the water floor record.** Mechanism confirmed (floor collision-bit
  test); could not load a decoded LABDATA water floor to print the exact byte (binary asset, no
  extracted JSON with `Collision` values in `mods/`). Verify by dumping one dungeon's
  FloorAndCeiling list and checking the water entry's `Unk1`.
- **Meaning of the other Collision bits (0–10, 15–23).** Used outside movement (rendering /
  sight / auto-graphics — cf. sight-block = wall flag 0x04 noted in `_RE_INDEX.md`). Not needed
  for passability.
- **`fcn.0008b739` / `fcn.0008b808`** are segment/handle lock-unlock helpers around the
  labyrinth data block; treated as opaque (they resolve `labBase` / `fcArray` / `objArray`).
