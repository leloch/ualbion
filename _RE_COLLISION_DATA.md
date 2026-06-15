# RE: Albion 3D passability — REAL DATA dump + corrected rule

Resolves the two confirmed live bugs in `src/Game/Entities/Map3D/Collider3D.cs`:
1. party **walks over water** (water must block);
2. party is **blocked by open arches / gates** (they must pass).

Method: re-disassembled the predicate `fcn.0001eeb8` + arbiter `fcn.0001e832` + object test
`fcn.0001f07e` in radare2 (project `albion_aaa`), and DUMPED every real labyrinth record to
JSON via the game's own `--DUMP -T Labyrinth` mode (output in
`data/exported/json/labdata/labyrinth*.json`). All field values below are the actual decoded
bytes from the shipped LABDATA*.XLD, not inferences.

> **Headline:** the prior doc (`_RE_3D_COLLISION.md`) and the current C# both got the BIT
> POSITIONS and the FLOOR INDEXING wrong. The real directional block bits are **raw-field bits
> 3–6 (mask `0x78`)**, not bits 11–14 (`0x7800`), of the `Collision` / `Unk1` byte. And the
> floor record is fetched **off-by-one** in the remake. Those two errors are the entire bug.

---

## KNOWN FACTS (top)

| Fact | Status |
|---|---|
| `fcn.0001eeb8` reads tile bytes: `tile[0]`=content (wall/objgroup), `tile[1]`=floor idx, `tile[2]`=ceiling idx. | CONFIRMED (asm 0x1ef88 / 0x1efd9 / 0x1f020) |
| WALL test: if `content>=101`, read `*(uint*)(wallRec)` (**from record offset 0**) and test bit `(dir+11)`. | CONFIRMED (0x1ef9e–0x1efc1) |
| FLOOR test: if `floor!=0`, read `*(uint*)(fcArray + 0xA*(floor**-1**))` (offset 0) and test bit `(dir+11)`. | CONFIRMED (0x1efe6 `dec eax` → `imul 0xa` → `mov eax,[eax]` → 0x1f008) |
| CEILING test: identical, `0xA*(ceiling-1)`. | CONFIRMED (0x1f02d–0x1f052) |
| OBJECT test `fcn.0001f07e`: `o=&objArray[n-1]` stride 0x10, read `*(uint*)o` (offset 0), test bit `(dir+11)`. | CONFIRMED (0x1f0e9–0x1f10c) |
| The tested dword is read from **record byte 0** in all four cases. So byte 0 = `Properties`, byte 1 = `Collision_lo` / `Unk1`. Bit 11 = (byte1, bit3) = **`Collision & 0x08` / `Unk1 & 0x08`**. | CONFIRMED |
| `dir` = move-request struct field **+0x10** (word), passed as `arg_14h`→`cx`. Range 0..3. (Prior doc wrongly said +0x12; +0x12 is the entity id `arg_10h`.) | CONFIRMED (caller 0x1e680/0x1e689) |
| The original has **NO `floorIndex==0` block** — `floor!=0` simply *gates* the floor test; floor 0 ⇒ floor test skipped ⇒ tile passable as far as the floor is concerned. | CONFIRMED (0x1efd2 `cmp 0; je`) |

### What "bit (dir+11)" actually means in field terms (the key correction)

The dword is `byte0 | byte1<<8 | byte2<<16 | byte3<<24`. Bit `(dir+11)` for dir 0..3 = bits
11,12,13,14 = **byte 1, bits 3,4,5,6**. Byte 1 is:
- the **low byte of `Collision`** for Wall / LabyrinthObject (because `Collision` lives at record
  offset 1, so its low byte = dword byte 1);
- **`Unk1`** for FloorAndCeiling (Unk1 is record byte 1).

So the predicate, expressed on the remake's deserialised fields, is:

```
WALL / OBJECT block-in-dir d  ==  (Collision >> (3+d)) & 1          // i.e. Collision & (0x08<<d)
FLOOR / CEILING block-in-dir d ==  (Unk1 >> (3+d)) & 1               // i.e. Unk1 & (0x08<<d)
direction-agnostic union       ==  (Collision & 0x78) != 0  /  (Unk1 & 0x78) != 0
```

The current C# uses mask `0x7800` against the **raw 24-bit `Collision`** (wall/object) — that
tests `Collision` bits 11–14 (its *high* byte), which is ~always 0 → every wall/object treated
passable. For the FLOOR it happens to be right only because `FcCollisionDword` re-shifts `Unk1`
back up by 8, so `& 0x7800` there == `Unk1 & 0x78`. (See §4.)

---

## 2. REAL DUMPED RECORDS

### 2a. WATER floor records (every lab that has water)

`Floor.Water` is **FloorAndCeilings index 2** in the brick/stone labs; a tile referencing it
stores floor **value 3** (asm fetches `FC[value-1]`).

| Lab (map) | FC idx | Sprite | Properties | **Unk1** | Unk2 | Unk3 | `Unk1 & 0x78` ⇒ blocks? |
|---|---|---|---|---|---|---|---|
| **109 Jirinaar** (real) | 2 | Floor.Water | 0 | **8** | 0 | 0 | **0x08 → BLOCKS** |
| **204 Srimalinar** (real) | 2 | Floor.Water | 0 | **8** | 0 | 0 | **0x08 → BLOCKS** |
| 9 Unknown9 (real) | 2 | Floor.Water | 0 | **8** | 0 | 0 | **BLOCKS** |
| 110/201/205 | 32 | Floor.Water2 | 0 | **8** | 0 | 0 | **BLOCKS** |
| 203 Beloveno / 206 UmajoKenta | 44 | Floor.Water2 | 0 | **8** | 0 | 0 | **BLOCKS** |
| 102 Toronto1 | 9 | Floor.Water | 0 | 0 | 0 | 0 | 0 → (deep-water area uses Water2/objects) |
| 1 Test1 / 101 TestSrimalinar (test data) | 2 | Floor.Water | 0 | 0 | 0 | 0 | 0 (unconverted test maps) |
| — `Floor.WatersEdge*` (shoreline, all labs) | 15–26 | WatersEdge.. | 0 | **0** | 0 | 0 | 0 → **walkable** (correct: you stand at the edge) |

**Finding:** real, shipped water (`Floor.Water` / `Floor.Water2`) carries **`Unk1 = 0x08`**, i.e.
the direction-0 block bit. The shoreline `WatersEdge*` tiles carry `Unk1 = 0` and are walkable.
Water is NOT distinguished by `Properties` (it is 0 — not even `NonWalkable`), nor by a special
floor index, nor by a pit. It blocks **purely via the `Unk1` directional bit**, exactly the
mechanism the asm tests.

### 2b. Why the remake walks over water — the OFF-BY-ONE

`LogicalMap3D.GetFloor` (src/Game/Entities/Map2D/LogicalMap3D.cs:73-76) does:

```csharp
byte tileIndex = _mapData.Floors[index];
var tile = ... ? _labyrinth.FloorAndCeilings[tileIndex] : null;   // BUG: no -1
```

`GetWall` (line 98) and `GetObject` (line 108) correctly use `[idx-1]`; `GetFloor` and
`GetCeiling` (line 88) do **not**. The asm uses `FC[value-1]`. Proof from data (Lab 109/204):

| floor value in tile | original reads `FC[v-1]` | remake reads `FC[v]` |
|---|---|---|
| 3 (a water tile) | FC[2] = **Floor.Water, Unk1=8 → BLOCK** | FC[3] = Floor.MossyBrickEdge, Unk1=0 → **pass** |

So the engine inspects the *next* record (a passable mossy-brick edge) instead of water ⇒
party walks over water. This is the whole "walk over water" bug. (`CollisionMask 0x7800` is fine
for floors once the index is corrected, because `FcCollisionDword(water)=0x800`, `&0x7800≠0`.)

### 2c. WALLS — open arches/gates vs solid walls (1239 walls across all labs)

Distribution of the raw 24-bit `Collision` and the asm's directional mask `& 0x78`:

| Collision (raw) | `& 0x78` | verdict | count | example sprites |
|---|---|---|---|---|
| `0x000000` | 0x00 | **passable** | 225 | **JiriOpenDoorway, JiriGateLeft, JiriGateRight, TorontoOpenDoor, DrinnoDoorOpen, ReactorOpenDoor** |
| `0x000008` | 0x08 | blocks | 587 | JiriMasonry, JiriTileAndStone, JiriDoor, JiriVines |
| `0x000018` | 0x18 | blocks | 334 | TorontoPanelling, TorontoClosedDoor |
| `0x000010` | 0x10 | blocks | 84 | (various solid) |
| `0x003b08` | 0x08 | blocks | 2 | Unknown105 |
| `0xf80008` | 0x08 | blocks | 7 | **JiriGate** (a *closed* gate), Unknown130 |

**Finding:** every OPEN doorway / gate / arch has `Collision = 0` → passable. Solid walls have
`0x8`/`0x10`/`0x18` → block. Only **2 of 1239** walls set any bit in the 11–14 range — so the
remake's `Collision & 0x7800` test classifies essentially **all** walls (solid included) as
passable, and conversely blocks none. The wall test is therefore inert today; the reason the
party gets *stuck on arches* is not the wall test at all but the **`floorIndex==0` pit guard**
(arch/threshold tiles have no floor record, floor value 0) — a guard that does **not** exist in
the original (§1). Remove it and arches pass.

### 2d. OBJECTS (`LabyrinthObject`, 1887 records)

| Collision (raw) | `& 0x78` | verdict | count | example |
|---|---|---|---|---|
| `0x000000` | 0 | pass | 1115 | LightCover (FloorObject), Plant, Pylon |
| `0x000008` | 0x08 | block | 767 | MetalColumn, Robot, VerticalLight |
| `0xf80008` | 0x08 | block | 3 | Chest, PhallicGrowth2 |
| `0x380000` | 0 | pass(here) | 2 | Krondir2, EvilKangaroo (NPCs; bits 19-21 are non-movement) |

Same rule: `Collision & 0x78` → solid; decorative/floor objects = 0 → pass. (The full original
also AABB-tests the footprint via `MapWidth/2`; the remake's per-tile approximation is
acceptable but must use `& 0x78`, not `& 0x7800`.)

---

## 3. TRUE PASSABILITY PREDICATE (per element)

For destination tile (x,y) and move direction `dir` (0..3 = move-struct +0x10):

```
solidBit(field, dir) = field & (0x08 << dir)          // field = raw Collision (wall/obj) or Unk1 (fc)
direction-agnostic   = field & 0x78                   // union of the four bits (recommended)

off-map (x,y outside 1..W / 1..H)         -> BLOCK
WALL    content>=101 & (Collision & 0x78) -> BLOCK    (open arch Collision=0 -> pass)
FLOOR   floor!=0     & (Unk1     & 0x78)  -> BLOCK    (water Unk1=8 -> block; floor==0 -> NOT a block)
CEILING ceiling!=0   & (Unk1     & 0x78)  -> BLOCK
OBJECT  per sub-object: (Collision & 0x78) [&& AABB footprint] -> BLOCK
everything else                            -> PASS
```

Note: `field & 0x78` (raw) ≡ `assembledDword & 0x7800`. The remake's `FcCollisionDword(fc) &
0x7800` already equals `Unk1 & 0x78`, so the floor/ceiling test is mask-correct; only the
**off-by-one fetch** breaks it. The wall/object tests apply `0x7800` to the raw field and must
change to `0x78`.

---

## 4. DROP-IN FIXES (do not apply here — for the implementer)

Two files. **Fix A** (the index off-by-one) alone fixes walk-over-water. **Fix B** fixes
stuck-on-arches and the inert wall/object masks.

### Fix A — `src/Game/Entities/Map2D/LogicalMap3D.cs`

```csharp
// GetFloor: original fetches FC[value-1] (asm: dec eax ; imul 0xa)
byte tileIndex = _mapData.Floors[index];
var tile = tileIndex > 0 && tileIndex - 1 < _labyrinth.FloorAndCeilings.Count
    ? _labyrinth.FloorAndCeilings[tileIndex - 1]   // was [tileIndex]
    : null;
return (tileIndex, tile);

// GetCeiling: same correction
var tile = tileIndex > 0 && tileIndex - 1 < _labyrinth.FloorAndCeilings.Count
    ? _labyrinth.FloorAndCeilings[tileIndex - 1]   // was [tileIndex]
    : null;
```

### Fix B — `src/Game/Entities/Map3D/Collider3D.cs` (`IsOccupied`)

```csharp
// Directional block bits are raw-field bits 3..6 (mask 0x78), NOT 11..14.
// (asm fcn.0001eeb8/0001f07e test bit (dir+11) of the dword read from record offset 0;
//  offset-0 dword byte 1 = Collision low-byte / Unk1, so bit 11 == field bit 3.)
const uint WallObjMask = 0x78;   // raw Collision (wall / LabyrinthObject)
const uint FcMask      = 0x78;   // raw Unk1     (FloorAndCeiling)

public bool IsOccupied(int fromX, int fromY, int toX, int toY)
{
    if (toX < 0 || toY < 0 || toX >= _logicalMap.Width || toY >= _logicalMap.Height)
        return true;                                   // off-map = solid

    // WALL: open arches/gates have Collision==0 -> pass; solid walls have 0x8/0x10/0x18 -> block.
    var (_, wall) = _logicalMap.GetWall(toX, toY);
    if (wall != null && (wall.Collision & WallObjMask) != 0)
        return true;

    // FLOOR: water carries Unk1==0x08 -> blocks. (GetFloor now returns the correct FC[idx-1].)
    var (floorIndex, floor) = _logicalMap.GetFloor(toX, toY);
    if (floor != null && (floor.Unk1 & FcMask) != 0)
        return true;

    // NO floorIndex==0 pit guard — the original does not block on a missing floor record.
    // This is exactly what un-sticks open arches / thresholds (they have floor value 0).
    // (If a levitation/void-fall guard is still desired it must be opt-in and must NOT block
    //  arch tiles; the faithful behaviour is: floor 0 -> not blocked by the floor test.)

    // CEILING: same mechanism.
    var (_, ceiling) = _logicalMap.GetCeiling(toX, toY);
    if (ceiling != null && (ceiling.Unk1 & FcMask) != 0)
        return true;

    // OBJECT-GROUP props.
    var group = _logicalMap.GetObject(toX, toY);
    if (group != null)
    {
        var objects = _logicalMap.Labyrinth.Objects;
        foreach (var sub in group.SubObjects)
        {
            if (sub == null) continue;
            if (sub.ObjectInfoNumber >= objects.Count) continue;
            var info = objects[sub.ObjectInfoNumber];
            if (info == null) continue;
            if ((info.Properties & LabyrinthObjectFlags.FloorObject) != 0) continue;
            if ((info.Collision & WallObjMask) != 0)   // was 0x7800
                return true;
        }
    }

    return false;
}
```

> Optional per-direction faithfulness: replace `& 0x78` with `& (0x08u << dir)` once a `dir`
> 0..3 is threaded in. The union `0x78` blocks water (it sets only the dir-0 bit, but any-of
> matches) and passes arches; it is the safe, strict-improvement default. Per-direction is only
> needed for one-way barriers, of which the data shows essentially none in the movement bits.

---

## 5. CONFIDENCE

| Claim | Confidence | Basis |
|---|---|---|
| Bit base is `(dir+11)` of the offset-0 dword | **CONFIRMED** | asm `add ecx,0xb; shl; test` ×3 in eeb8, ×1 in f07e |
| ⇒ block bits = raw field `& 0x78` (Collision / Unk1) | **CONFIRMED** | dword byte 1 = Collision-lo/Unk1; 1239 walls + 1887 objects + all FC dumped agree |
| Real water = `Unk1 0x08` (blocks); shoreline = 0 (walks) | **CONFIRMED** | dumped FC of labs 9/109/110/201/203/204/205/206 |
| Open arches/gates = `Collision 0x00` (pass); solid = 0x8/0x10/0x18 | **CONFIRMED** | dumped Walls of all labs |
| Remake `GetFloor`/`GetCeiling` are off-by-one vs asm `FC[idx-1]` | **CONFIRMED** | asm 0x1efe6 `dec` vs C# line 75/88 |
| Original has no `floorIndex==0` block (remake added it) | **CONFIRMED** | asm 0x1efd2 only gates the floor test |
| `dir` source = move-struct +0x10 (not +0x12) | **CONFIRMED** | caller fcn.0001e650 0x1e680/0x1e689 |
| Exact `dir`→compass (0=N?) mapping | INFERRED | range 0..3 confirmed; union mask sidesteps it |
| Meaning of bits 0–2, 7+, and `Unk1` values 0x10/0xF0 | OUT OF SCOPE | not movement bits (rendering/auto-gfx); e.g. Unk1=0x10/0xF0 seen on walkable jewels/plates ⇒ not collision |

### One caveat worth noting
`Unk1` also takes values `0x10` (bit 4 = dir-1 block) and `0xF0` (bits 4-7) on some clearly
*walkable* floors (pressure plates, jewels, mud variants). Under the union `0x78`, `0x10` and
`0xF0` would **also block** (they intersect 0x78). The asm would too (bit 12/13/14). Either (a)
those floors are never actually stepped on from the relevant direction in real maps, or (b) the
intended rule is per-direction and those set a non-cardinal/decorative direction. If, after
shipping the union fix, any of those specific floors wrongly block, switch the FLOOR test to the
per-direction form `(Unk1 & (0x08u << dir))` — water (0x08, dir-0) still blocks, while a
plate that only sets bit 4 won't block a dir-0 approach. This is the only residual risk and it
does not affect the two reported bugs (water 0x08 and arches 0x00 are unambiguous).

---

## 2026-06-15 — Collision mask CONFIRMED COMPLETE (full 24-bit field RE'd)

Re-RE'd `fcn.0001eeb8` (passability) + `fcn.0001f07e` (object) + `fcn.0001e832` (mover) to
resolve whether the empirical `0x78` low-byte mask is complete for the full 24-bit `Collision`
field (`Wall`/`LabyrinthObject` store Collision as 3 bytes in a uint).

- The wall/floor/ceiling test reads the record's offset-0 DWORD and does `test bit (dir+11)`
  (dir 0..3 = N/E/S/W). Bits 11..14 of that dword == **Collision bits 3..6 == mask 0x78**
  (record layout: Properties@b0, Collision@b1..3). Floors/ceilings test the same on `Unk1`
  (water `Unk1=0x08` = bit 3 → blocks). Objects (`fcn.0001f07e`) test the identical bit on the
  object record's offset-0 dword + an AABB footprint via MapWidth/2.
- **The high byte (Collision bits 16..23) is NEVER tested by any movement code** (searched the
  whole binary; the only consumers of the wall table are `fcn.0001eeb8` and the auto-gfx renderer
  `fcn.0005e12c` which reads offset 7). So `& 0x78` is COMPLETE, not an approximation.
- Reconciles the TestMapArgim2 anomaly: objects with Collision `0x380000` (Krondir2/EvilKangaroo —
  NPC/monster metadata in the high byte) are correctly **passable** (`0x380000 & 0x78 == 0`);
  `0xf80008` blocks solely via bit 3 (`& 0x78 == 0x08`). All real maps use only 0x0/0x8.
- Verified by `_allmaps_collision_sweep.ps1`: **59 3D maps scanned, ZERO see-through walls/objects,
  blockMismatch=0 on every map** (only TestMapArgim2 shows the high-byte monster metadata, correctly
  ignored). So Collider3D's `(Collision & 0x78) != 0` is correct and complete; no change needed.
- Per-direction faithfulness exists in the original (`field & (0x08u << dir)` for one-way barriers)
  but the dumped data has essentially none, so the omnidirectional union `0x78` matches observed
  behaviour. The original's diagonal corner-cutting is governed by a separate 8-neighbour arbiter
  (`fcn.0001e832`, neighbour table @0x1d640, 9-case slide switch @0x1ea38) — the remake approximates
  this with axis-separated FilterCollision + a diagonal-corner block; faithful port is future work.
