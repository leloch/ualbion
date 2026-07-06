# RE: Albion 3D party movement + collision — IMPLEMENTABLE precision

> Re-disassembled 2026-07-03 from MAIN.EXE (radare2 project `albion_aaa`, DOS LE). Every claim below is
> address-stamped. This doc SUPERSEDES the conflicting readings in `_RE_3D_COLLISION.md` ("bit dir+11,
> dir=direction") and `_RE_COLLISION_DATA.md` ("mask 0x78 union") and the `_RE_5D.md` §4 sketch.
> Purpose: rewrite the remake's 3D collision (`Collider3D` / `Movement3D`) faithfully.

---

## 0. KNOWN FACTS (all CONFIRMED by disassembly unless marked)

| Item | Value / Address | Evidence |
|---|---|---|
| Party translate executor | `fcn.0001e52d(eax=dx16.16, edx=dy16.16)` | pdf |
| Party move struct (static) | `0x13504e`: +0 x(int32), +4 y(int32), +8 dx(int32), +0xC dy(int32), +0x10 **class**(word), +0x12 **id**(word) | 0x1e561..0x1e5b2 |
| Party id | 0xFFFF, statically initialised in the data segment (bytes `ff ff` at 0x135060; no code writer) | `px 24 @ 0x13504e` |
| **Party collision class** | `word[0x153ccc]`, copied into struct+0x10 **every move** | `0x1e5a1 mov ax,[0x153ccc]` / `0x1e5a7 mov [0x13505e],ax` |
| class writers | `= 0` at new-game init `fcn.00034b66 @0x34be6`, `= 0` twice in debug map-warp menu `fcn.00025746 @0x258be/0x25a0b` ("Map: %u" / "X: %u, Y: %u" strings nearby); **loaded from savegame** (block of 0x5BB8 bytes read to `0x153b28`, so class = save-header offset **+0x1A4** = remake `SavedGame.Unk1A2` high word). **No writer ever sets it nonzero → party class is 0 in practice.** | axt @0x153ccc; load: `fcn.00024842 @0x24b1d..0x24b2b` (`mov ebx,0x5bb8; mov edx,0x153b28; call read`) — "g:\albion\src\saveload.c" |
| Sanity anchor | party tile words `0x153b34/36` = save +0xC/+0xE = remake `SavedGame.PartyX/PartyY` (block base 0x153b28 + 0xC) | SavedGame.cs:283 |
| Axis-fallback wrapper | `fcn.0001e650(eax=&struct)` — try (dx,dy), then one axis, then the other; commits pos on success | pdf |
| One-step arbiter | `fcn.0001e832(eax=x,edx=y,ebx=dx,ecx=dy, arg_10h=id, arg_14h=class)` returns **1=ALLOWED, 0=BLOCKED** (note: older doc had this inverted) | pdf; commit path in e650 0x1e7e0..0x1e807 runs on nonzero |
| Tile passability predicate | `fcn.0001eeb8(eax=tileX, edx=tileY, ebx=id(unused), cx=class)` returns 1=passable | pdf |
| 9-zone classifier | `fcn.0001ede1(eax=subX, edx=subY)` → 0..8; margin = `MAX(tileSize/4, 50)` | 0x1ee0f..0x1ee3e |
| Object/NPC overlap test | `fcn.0001f07e(eax=px, edx=py, ebx=relX, ecx=relY, arg_10h=class, arg_14h=groupPtr, arg_18h=objInfoBase)` returns 1=hit | pdf |
| World→tile quantiser | `fcn.0001f238`: `tileX = worldX/tileSize + 1` (1-based); **`tileY = mapHeight − worldY/tileSize`** (Y-FLIPPED, 1-based) | 0x1f269..0x1f28b |
| Camera integrator | `fcn.0001d9d7(dx16,dy16)`: pos = int dword `[0x14a482]/[0x14a488]` + frac word `[0x14a480]/[0x14a486]`; frac normalised into [0,0x10000) with ±1 carry into int | pdf |
| tileSize | `word[0x14a4a0]` = **`1 << labdataHeader[+0x1A]`** (= 1<<WallWidth = remake EffectiveWallWidth), set at labdata load | `0x1a0c7 mov cx,[eax+0x1a]; shl eax,cl; 0x1a0d2 mov [0x14a4a0],ax` |
| Map dims | width `word[0x14799a]`, height `word[0x147994]` (tiles, 1-based valid range 1..W / 1..H) | 0x1eeef/0x1ef0c |
| Map tile bytes | 3 bytes/tile: `[0]`=content (0=empty, 1..100=object group, ≥101=wall), `[1]`=floor idx, `[2]`=ceiling idx; tile addr = mapBase + 3·((tileY−1)·W + tileX−1) | 0x1ef4f..0x1ef88 |
| Wall record | 0x42 bytes; found via offset table `[0x14ff50 + content*4]` added to labBase, OR as `labBase+0x28 + (content−1)·0x42` (same array serves object groups 1..100 and walls 101+) | 0x1ef9e..0x1efac; 0x1ebc2..0x1ebd1 |
| FloorAndCeiling array | `fcBase = labBase + 0x28 + numWalls(word[0x1511c8])·0x42 + 2`; 0xA-byte records, fetched **[idx−1]** | 0x1ef32..0x1ef4c; `0x1efec dec eax; imul 0xa` |
| ObjectInfo (LabyrinthObject) array | after FC array: `+ numFC(word[0x1511c4])·0xA + 2`; 0x10-byte records, fetched [n−1] | 0x1eb58..0x1eb66; 0x1f0e9 |
| Neighbour table | `0x1d640`, 8 rows × 6 bytes: `word dxTile, word dyWorld, word zoneMask` (dump below) | px; used 0x1e997..0x1e9d9 |
| 9-case slide switch | jump table @ `0x1ea38`, dispatch @ `0x1ea7d`, case 4 (centre) = default = no restriction | pd |
| Party noclip flag | `word[0x147130]` ≠ 0 → e832 returns ALLOWED immediately for id 0xFFFF (toggled 0/1 in `fcn.000254fc @0x2565d/0x2569d` and 0x2f1b8/0x2f1d0 — debug/cheat toggle) | 0x1e8f2..0x1e912 |
| "party moved" flag | `word[0x14798a]` = 0 at entry, 1 after successful move | 0x1e54a / 0x1e5c5 |
| NPC 3D mover | `fcn.0004166c(eax=npcIndex)`; NPC state records 0x80 bytes @ `0x159c58`, 96 slots (savegame block 0x3000 bytes, loaded 0x24b68..0x24b76) | pdf |
| NPC state fields used | +0x02 word objectGroupNumber (0x159c5a), **+0x05 word collision class** (0x159c5d), +0x17 word active flag (0x159c6f), +0x2A/+0x2C words tileX/Y, +0x32/+0x36 dwords worldX/Y int (0x159c8a/8e), +0x3A/+0x3E dwords desired dx/dy, +0x42 word speed (0x159c9a) | fcn.0004166c |
| **NPC class source** | map NPC record (10 bytes) byte+6 (= flags low byte): `class = (flags & 0x40) ? 1 : 0` — this is remake `MapNpcFlags.NoClip = 0x40` | `fcn.0003ec73 @0x3ef81 mov al,[eax+6]; and al,0x40;` → `0x3ef98 mov word[eax+0x159c5d],1` / `0x3efac ...,0` (mirror @0x3f39b/0x3f3af) |
| Delta-time scalar | `dword[0x13eebc]` (ticks elapsed + 1); NPC speed = word[+0x42]·S/5 per frame | 0x4169f..0x416af |
| Other eeb8 caller | `fcn.0001f294` (tile-ray walk, Bresenham-style; wrapped by `fcn.0001f9f2` which passes party tile + target `[0x151246/48]`, id 0xFFFF, class `[0x153ccc]`) — line-of-walk/teleport check; same predicate, same class | 0x1fa0c..0x1fa3c |

---

## 1. THE DEFINITIVE COLLISION-BIT RULE (settles the 0x78 vs class conflict)

All four record types are tested identically: **load the dword at record offset 0, test bit `(11 + class)`**:

```
fcn.0001eeb8 wall:    0x1efb3 mov cx,[var_1ch]   ; var_1ch = the cx argument
              0x1efb7 add ecx, 0xb               ; 11 + cx
              0x1efbf shl eax, cl ; 0x1efc1 test [wallDword], eax
fcn.0001eeb8 floor:   0x1effe add ecx, 0xb  (same pattern, FC[floor−1] dword)
fcn.0001eeb8 ceiling: 0x1f045 add ecx, 0xb  (same pattern, FC[ceil−1] dword)
fcn.0001f07e object:  0x1f0fd add ecx, 0xb  (same pattern, objInfo[n−1] dword)
```

**What is `cx`? It is a per-ENTITY collision CLASS, not a per-move direction.** Proof:

- Party: `fcn.0001e52d @0x1e5a1-0x1e5a7` loads struct+0x10 **directly from `word[0x153ccc]`** — a
  per-party global that is only ever written 0 (init/debug) or loaded from the save (+0x1A4). It is
  not recomputed from the move vector. There is NO direction computation anywhere on the path
  e52d → e650 → e832 → eeb8; e650 passes struct+0x10 verbatim (0x1e680/0x1e689), e832 threads it
  as arg_14h into every eeb8/f07e call (0x1e93d, 0x1e987, 0x1ebde, 0x1ec9d, 0x1ed99).
- NPC: `fcn.0004166c @0x41786` loads struct+0x10 from NPC state +0x05, which map-NPC init sets to
  **1 if mapNpc.flags & 0x40 (remake `MapNpcFlags.NoClip`) else 0** (`fcn.0003ec73 @0x3ef81..0x3efac`).

Dword byte 1 = disk `Collision` low byte (walls/objects, Collision @ record offset 1) or `Unk1`
(FloorAndCeiling). So bit (11+class) of the dword = **raw field bit (3+class)**:

```
blocks(entity) == (rawByte & (0x08 << class)) != 0
  party (class 0):         rawByte & 0x08
  NoClip NPC (class 1):    rawByte & 0x10
```

### Why this reconciles ALL the dumped data (from _RE_COLLISION_DATA.md §2)

| Record | raw value | & 0x08 (party) | & 0x10 (class-1 NPC) | verdict |
|---|---|---|---|---|
| Water floor `Unk1=0x08` | 0x08 | **blocks party** | passes | ✓ party can't walk on water; NoClip NPCs can |
| Open arch/gate wall `0x00` | 0 | passes | passes | ✓ |
| Solid masonry `0x08` | 0x08 | blocks | **passes** | ✓ NoClip("ghost") NPCs walk through walls |
| `0x18` walls (334) | 0x18 | blocks | blocks | ✓ barriers that stop even NoClip NPCs |
| **`0x10`-only walls (84)** | 0x10 | **PASSES** | blocks | fences for NoClip NPCs only — the remake's `&0x78` union wrongly blocks these for the party |
| **Plates/jewels/mud floors `Unk1=0x10/0xF0`** | 0x10/0xF0 | **PASSES** | blocks | resolves the _RE_COLLISION_DATA §5 caveat exactly — `&0x78` wrongly blocks these |

**⇒ The remake bug:** `Collider3D` currently uses the union mask `0x78`. That over-blocks every
wall/floor whose raw byte is `0x10`, `0x20`, `0x40`, `0xF0`, … (party-passable in the original).
This is the "impassable archways / pillar bridges" report. **The correct party test is `& 0x08`
(exactly bit 3), not `& 0x78`.** Per-direction blocking does not exist at all; one-way barriers
are not a thing in this engine — asymmetry is by *entity class*.

`fcn.0001eeb8` exact pseudo-code (return 1 = passable):

```c
int tilePassable(int tileX, int tileY, int class) {
    if (tileX < 1 || tileX > mapW || tileY < 1 || tileY > mapH) return 0;   // 0x1eee0..0x1ef16
    byte* t = mapBase + 3*((tileY-1)*mapW + (tileX-1));                     // 0x1ef4f..0x1ef80
    uint bit = 1u << (11 + class);
    if (t[0] >= 101 && (*(uint*)(labBase + wallOfsTable[t[0]]) & bit)) return 0;  // 0x1ef93..0x1efc6
    if (t[1] != 0   && (*(uint*)(fcBase + 10*(t[1]-1)) & bit))         return 0;  // 0x1efd2..0x1f00d
    if (t[2] != 0   && (*(uint*)(fcBase + 10*(t[2]-1)) & bit))         return 0;  // 0x1f014..0x1f054
    return 1;
    // NOTE: object groups (t[0] in 1..100) are NOT tested here — objects never make a TILE solid
    // and never contribute to the wall-hug margin; they are only overlap-tested (fcn.0001f07e).
    // NOTE: floor==0 is NOT a block (it just skips the floor test). No "pit" rule exists.
}
```

---

## 2. COORDINATE SYSTEM (needed to read everything else)

- World position = integer world units (dword) + 1/0x10000 fraction (word). **1 world unit = 1/0x10000
  of nothing-special; 1 TILE = `tileSize` world units**, tileSize = `1 << WallWidth` from the labdata
  header (+0x1A) — e.g. WallWidth 9 → 512. (The old note "1 tile = 0x10000 sub-units" conflated the
  two; the fraction is per WORLD UNIT.)
- Collision runs entirely on **integer world units**; the fraction is ignored by collision and only
  integrated by `fcn.0001d9d7` afterwards.
- Tile indices are 1-based; `tileX = x/tileSize + 1`, `tileY = mapHeight − y/tileSize` — **the tile Y
  axis is flipped vs world Y** (0x1f27f..0x1f286). Sub-tile coords `sub = coord & (tileSize−1)` are in
  WORLD orientation. All zone/table logic is intuitive in world space (the f238 flip and the table's
  Y subtraction cancel — see §4). An implementation in the remake's own tile frame just needs
  "neighbour across the edge the point is near" to be consistent with its floor/wall lookups.

`fcn.0001ede1` zone classifier (CONFIRMED, 0x1edfe..0x1eea7):

```c
int zone(uint subX, uint subY) {           // sub coords in world units within tile
    int m = tileSize/4; if (m <= 50) m = 50;      // margin = MAX(tileSize/4, 50)
    int zx = subX < m ? 0 : (subX >= tileSize-m ? 2 : 1);   // unsigned compare (jb) on the low bound
    int zy = subY < m ? 0 : (subY >= tileSize-m ? 6 : 3);
    return zx + zy;    // 0..8; zone 4 = centre band both axes
}
```
Zone grid, world orientation (world X→ right, world Y→ up in tile terms is irrelevant — just: 0/3/6
rows are the LOW-subY / mid / HIGH-subY bands):
```
        subX<m   mid   subX>=ts-m
subY<m     0      1        2
mid        3      4        5
subY>=ts-m 6      7        8
```

---

## 3. THE MOVER STEP — `fcn.0001e52d` (party) / `fcn.0004166c` (NPC)

```c
// dx16/dy16: 16.16 fixed-point world-unit deltas, ALREADY delta-time scaled by the input layer
// (fcn.0001e28a keyboard / fcn.0001db96 mouse: v = const · S/D, dx16 = v·cos etc., see _RE_SMOOTH_MOVE.md)
void PartyTranslate(int dx16, int dy16) {                    // fcn.0001e52d
    partyMovedFlag[0x14798a] = 0;
    if (!fcn.000127ce()) return;                             // map-resource gate (lock); 0x1e553
    s.x  = camXint;  s.y = camYint;                          // integer world units       0x1e561..0x1e570
    s.dx = dx16 / 0x10000;  s.dy = dy16 / 0x10000;           // TRUNCATED TOWARD ZERO     0x1e575..0x1e59c
    s.class = word[0x153ccc];                                // party collision class     0x1e5a1
    if (!fcn.0001e650(&s)) return;                           // fully blocked: NOTHING moves (fraction included)
    partyMovedFlag = 1;
    if (s.dx == 0) dx16 = 0;                                 // axis killed by collision (or sub-unit) 0x1e5ce..0x1e5e7
    if (s.dy == 0) dy16 = 0;
    fcn.0001d9d7(dx16, dy16);                                // integrate camera int+frac  0x1e5f4
    (tx,ty) = fcn.0001f238(camXint, camYint);                // re-derive party tile       0x1e60a
    if (tx != word[0x153b34] || ty != word[0x153b36]) {
        word[0x153b34]=tx; word[0x153b36]=ty;
        fcn.0005c40e(10); fcn.00012875();                    // tile-changed notifications  0x1e639..0x1e643
    }
}
```

Notes (all CONFIRMED):
- **No clamping, no subdivision.** The proposed delta is tested as ONE step against the endpoint tile
  only. Tunnelling through a whole solid tile is theoretically possible if a frame delta exceeds
  ~a tile; in practice speeds are ≈10–20 world units/frame (≈3% of a 512 tile) so it never happens.
  A faithful port may safely sub-step deltas > margin for robustness.
- **Sub-unit axis deltas are DROPPED**: if |dx16| < 0x10000 the integer delta truncates to 0, e650
  commits 0 for that axis, and e52d zeroes the fractional part too (0x1e5d7). So there is an
  effective minimum speed of 1 world unit/frame per axis; below it that axis simply doesn't move.
- The struct's committed x/y are scratch — the real position is the camera int+frac pair.
- NPC variant `fcn.0004166c`: desired delta (dwords +0x3A/+0x3E) is magnitude-clamped to
  `speed·S/5` (sqrt length, 0x416d6..0x4173a), then the same e650 call with class from +0x05 and
  id = npc index; on success writes back position and achieved delta, sets "walked" flag bit 4
  (byte +0x19) if any axis moved, and re-derives its tile via f238 into +0x2A/+0x2C.

---

## 4. THE AXIS FALLBACK (WALL SLIDE) — `fcn.0001e650` (CONFIRMED, whole function)

```c
int TryMove(Mover* m) {                       // returns 0 = blocked, 1 = moved (possibly one axis)
    int dx = m->dx, dy = m->dy;
    int ux = dx, uy = dy;
    int r = e832(m->x, m->y, dx, dy, m->id, m->class);            // 0x1e6a8
    if (!r) {
        if (m->dx > m->dy) { uy = 0; r = e832(m->x,m->y, dx, 0, ...); }   // 0x1e6c7: SIGNED compare!
        else               { ux = 0; r = e832(m->x,m->y, 0, dy, ...); }
        if (!r) {
            if (m->dx > m->dy) { ux = 0; uy = m->dy; r = e832(m->x,m->y, 0, dy, ...); }  // 0x1e756
            else               { ux = m->dx; uy = 0; r = e832(m->x,m->y, dx, 0, ...); }
        }
    }
    if (r) { m->x += ux; m->y += uy; m->dx = ux; m->dy = uy; }    // 0x1e7e7..0x1e807: commit achieved delta
    else   { m->dx = 0; m->dy = 0; }                              // 0x1e80c..0x1e819
    return r;
}
```

- Order: full (dx,dy) → single axis → other axis. Which axis is tried first is decided by the
  **raw SIGNED comparison `dx > dy`** (not magnitudes) — a Watcom-era quirk; e.g. moving (+3,−3)
  tries X-only first, (−3,+3) tries Y-only first. Faithful port should replicate or accept the
  cosmetic difference (only matters when both single axes would succeed, which is rare).
- This fallback is the entire "slide along the wall" behaviour; e832 itself never modifies the delta.

---

## 5. THE ONE-STEP ARBITER — `fcn.0001e832` (CONFIRMED, whole function)

Inputs: current int position (x,y), proposed int delta (dx,dy), entity id (0xFFFF = party), class.
Returns 1 = allowed, 0 = blocked.

```c
int StepAllowed(int x, int y, int dx, int dy, ushort id, ushort class) {
    int ts = tileSize;                                   // 0x1e853
    (srcTX, srcTY) = f238(x, y);                         // 0x1e868   current tile
    (dstTX, dstTY) = f238(x+dx, y+dy);                   // 0x1e87f   DESTINATION TILE = quantised
                                                         //           PROPOSED POINT (no corner, no margin)
    srcZone = ede1(x & (ts-1), y & (ts-1));              // 0x1e88b..0x1e8a8  zone of CURRENT pos
    if (dstTX<1 || dstTX>mapW || dstTY<1 || dstTY>mapH) return 0;   // 0x1e8b0..0x1e8e6
    if (id == 0xFFFF && word[0x147130] != 0) return 1;   // 0x1e8f2..0x1e90b  party noclip toggle

    x += dx; y += dy;                                    // 0x1e917..0x1e920  (locals now = proposed pos)
    subX = x & (ts-1); subY = y & (ts-1);                // 0x1e923..0x1e938  DEST sub-tile coords

    // (1) destination tile must be passable for this class
    if (!tilePassable(dstTX, dstTY, class)) return 0;    // 0x1e94f..0x1e95c   (fcn.0001eeb8)

    // (2) 8-neighbour forbidden-zone mask (wall-hug margin)
    ushort forbidden = 0;
    for (i = 0; i < 8; i++) {                            // 0x1e970..0x1e9dc
        // table row: {dxTile(word), dyWORLD(word), zoneMask(word)} @ 0x1d640 + 6*i
        int nx = dstTX + tbl[i].dxTile;                  // 0x1e9b5 add
        int ny = dstTY - tbl[i].dyWorld;                 // 0x1e99d sub  (tileY axis is world-Y-flipped,
                                                         //  so this equals world offset +dyWorld)
        if (!tilePassable(nx, ny, class))                // 0x1e9bf  (off-map neighbours count as solid)
            forbidden |= tbl[i].zoneMask;                // 0x1e9d2..0x1e9d9
    }
    dstZone = ede1(subX, subY);                          // 0x1e9ea
    bool ok = true;
    if (forbidden & (1 << dstZone)) {                    // 0x1e9f2..0x1ea06
        // moving INTO a forbidden zone: only tolerated if we are ALREADY in that exact tile+zone
        if (srcTX != dstTX || srcTY != dstTY || srcZone != dstZone) return 0;  // 0x1ea0c..0x1ea30
        // already hugging: 9-case switch — may only move AWAY from / PARALLEL to the solid edge
        switch (dstZone) {                               // dispatch 0x1ea7d, table @0x1ea38
            case 0: if (dx < 0 || dy < 0) ok = false; break;   // 0x1ea84  x-低/y-low corner
            case 1: if (dy < 0)           ok = false; break;   // 0x1ea9c  y-low edge band
            case 2: if (dx > 0 || dy < 0) ok = false; break;   // 0x1eaae
            case 3: if (dx < 0)           ok = false; break;   // 0x1eac3  x-low edge band
            case 4: /* centre: no restriction (== default) */  break;
            case 5: if (dx > 0)           ok = false; break;   // 0x1ead2  x-high edge band
            case 6: if (dx < 0 || dy > 0) ok = false; break;   // 0x1eae1
            case 7: if (dy > 0)           ok = false; break;   // 0x1eaf6  y-high edge band
            case 8: if (dx > 0 || dy > 0) ok = false; break;   // 0x1eb05
        }
        if (!ok) return 0;                               // 0x1eb18..0x1eb26
    }

    // (3) object-group overlap on the DEST tile (props/furniture)
    content = mapTile(dstTX, dstTY)[0];                  // 0x1eb6f..0x1ebab
    if (content != 0 && content < 101) {                 // 0x1ebae..0x1ebbe
        group = labBase+0x28 + (content-1)*0x42;         // 0x1ebc2..0x1ebd1 (same array as walls)
        if (f07e(subX, subY, 0, 0, class, group, objInfoBase)) ok = false;   // 0x1ebef..0x1ebf9
    }
    if (!ok) return 0;                                   // 0x1ec0a

    // (4) NPC moving: test the PARTY body (party group looked up via mover's own... see note)
    if (id != 0xFFFF) {                                  // 0x1ec15..0x1ec20
        relX = camXint - x; relY = camYint - y;          // 0x1ec26..0x1ec39
        if (-ts < relX && relX < ts && -ts < relY && relY < ts) {          // 0x1ec3c..0x1ec6e
            grp = labBase+0x28 + (word[0x159c5a + id*0x80] - 1)*0x42;      // 0x1ec72..0x1ec90
            //  ^ NOTE: uses the MOVER's own object-group as the party's stand-in footprint
            if (f07e(subX, subY, relX, relY, class, grp, objInfoBase)) return 0;   // 0x1ecb2..0x1ecbc
        }
    }

    // (5) all live NPC bodies
    for (i = 0; i < 96; i++) {                           // 0x1ecce..0x1edc1
        if (word[0x159c6f + i*0x80] == 0) continue;      // inactive          0x1ecf6
        if (i == id) continue;                           // self              0x1ed00
        relX = dword[0x159c8a + i*0x80] - x;             // NPC worldX int    0x1ed17
        relY = dword[0x159c8e + i*0x80] - y;             //                   0x1ed2c
        if (relX <= -ts || relX >= ts || relY <= -ts || relY >= ts) continue;  // 0x1ed38..0x1ed6c
        grp = labBase+0x28 + (word[0x159c5a + i*0x80] - 1)*0x42;               // 0x1ed70..0x1ed8c
        if (f07e(subX, subY, relX, relY, class, grp, objInfoBase)) return 0;   // 0x1edae..0x1edb8
    }
    return 1;
}
```

### The neighbour table @0x1d640 (dumped bytes `px 96 @ 0x1d640`)

```
ffff ffff 0100 | 0000 ffff 0700 | 0100 ffff 0400 | ffff 0000 4900
0100 0000 2401 | ffff 0100 4000 | 0000 0100 c001 | 0100 0100 0001
```

| i | dxTile | dyWorld | zoneMask | world-space neighbour | forbids zones |
|---|---|---|---|---|---|
| 0 | −1 | −1 | 0x001 | (x−, y−) corner | 0 |
| 1 |  0 | −1 | 0x007 | y− side | 0,1,2 (subY<margin band) |
| 2 | +1 | −1 | 0x004 | (x+, y−) corner | 2 |
| 3 | −1 |  0 | 0x049 | x− side | 0,3,6 (subX<margin band) |
| 4 | +1 |  0 | 0x124 | x+ side | 2,5,8 |
| 5 | −1 | +1 | 0x040 | (x−, y+) corner | 6 |
| 6 |  0 | +1 | 0x1c0 | y+ side | 6,7,8 |
| 7 | +1 | +1 | 0x100 | (x+, y+) corner | 8 |

In WORLD space the table is exactly intuitive: **if the tile across an edge/corner is solid (per
tilePassable for this class), the sub-tile band(s) of the destination tile adjacent to that
edge/corner become forbidden.** (In tile-index space Y appears negated because of the f238 flip —
the code's `sub` cancels it; an implementer should just use the world-space reading above.)

### Semantics summary of the zone stage

- You can never MOVE INTO a forbidden zone (from another zone or another tile). This is the wall
  standoff: the party's point stays ≥ margin = MAX(tileSize/4, 50) world units away from any solid
  tile face, and corners are cut off likewise (no corner clipping).
- If you already ARE in a forbidden zone of that same tile (e.g. a door closed next to you, or you
  approached before the neighbour was solid), you may still move, but only with components that do
  not push toward the forbidden edge(s) — the 9-case switch. Combined with e650's axis fallback,
  a diagonal push against a wall degrades into the parallel component = smooth wall slide.
- Sliding along a wall happens in the centre band (outside the margin), so crossing tile boundaries
  while sliding is unproblematic (the next tile's matching zone is only forbidden if you are inside
  the margin band, which the standoff prevents).

---

## 6. OBJECT / BODY OVERLAP — `fcn.0001f07e` (CONFIRMED, whole function)

```c
// px,py: proposed position's DEST sub-tile coords (world units within tile)
// relX,relY: 0,0 for map objects; (otherEntityWorldInt - proposedWorldInt) for bodies
int GroupOverlap(int px, int py, int relX, int relY, ushort class, byte* group, byte* objInfoBase) {
    for (i = 0; i < 8; i++) {                                    // 8 sub-objects   0x1f0ad
        // group record: word AutoGraphicsId @+0, then 8 sub-objects of 8 bytes @+2
        // sub-object fields (disk == remake ObjectGroup.SubObject): +0 X, +2 Z, +4 Y(height), +6 objInfoNo
        ushort n = *(ushort*)(group + 8*i + 8);                  // subobj +6: objectInfoNumber  0x1f0d1
        if (n == 0) continue;                                    // empty slot                    0x1f0dd
        byte* o = objInfoBase + 16*(n-1);                        // LabyrinthObject record        0x1f0e9
        if ((*(uint*)o & (1u << (11 + class))) == 0) continue;   // class gate (same rule!)       0x1f0fd..0x1f10e
        int objX = *(short*)(group + 8*i + 2) + relX;            // subobj +0 (X in tile)         0x1f120
        int objY = *(short*)(group + 8*i + 4) + relY;            // subobj +2 (Z in tile)         0x1f136
        int half = *(ushort*)(o + 0xC) / 2;                      // MapWidth/2 — BOTH axes        0x1f143..0x1f156
        if (px >= objX - half && py >= objY - half &&            // 0x1f159..0x1f17f
            px <  objX + half && py <  objY + half)              // 0x1f183..0x1f1ab
            return 1;                                            // 0x1f1af — first hit wins, whole loop stops
    }
    return 0;
}
```

Facts:
- The footprint is a **SQUARE**: half-extent = `LabyrinthObject.MapWidth / 2` (record offset 0xC) on
  **both X and Y**. `MapWidth` here is in world units. **MapHeight (+0xE) is never read by collision**
  (it is the vertical/billboard size).
- The mover is a **point**; no margin is added around objects (the 3×3 zone margin applies only to
  solid TILES, not to objects). That is why you can brush right up against furniture but stand a
  margin away from walls.
- Gate: same `(0x08 << class)` rule on the object's raw Collision byte. Party class 0: objects with
  Collision & 0x08 are solid (MetalColumn, Robot, Chest…); `0x00` and `0x10/0x380000`-style records
  pass. (Matches the 1887-record dump in _RE_COLLISION_DATA.md.)
- On hit the WHOLE step attempt is rejected (e832 returns 0). Per-axis recovery happens only via
  e650's axis fallback re-running the whole test with a reduced delta — so you CAN slide along an
  object's box edge, one axis at a time.
- Sub-object numbering: disk stores objectInfoNumber where 0 = empty; the asm uses `objArray[n-1]`.
  The remake's `SubObject.ObjectInfoNumber` is already decremented (and slot nulled when 0), so
  `objects[sub.ObjectInfoNumber]` is the correct record, and `sub != null` ⇔ the asm's `n != 0`.
- NPC bodies (and the party body when an NPC moves) are collided as the NPC's own ObjectGroup
  translated by `rel = otherPos − proposedPos`, prefiltered by |rel| < tileSize on both axes.
  CAVEAT (INFERRED): this mixes a sub-tile coordinate (px) with a world-relative offset (relX); it is
  exact only when both entities are at the same sub-tile offset and degrades gracefully otherwise —
  an original approximation ("blocks within roughly a tile, fuzzily"). A faithful port can replicate
  the formula verbatim; a *corrected* port would use
  `hit = |proposedWorld − (npcTileOrigin + subobj.xz)| < half` per axis. Replicate-verbatim is the
  safe default for 1:1 behaviour.

---

## 7. MARGIN / MOVER-SHAPE ANSWERS (deliverable 6)

- **The mover is a POINT** (the camera/NPC anchor integer world position). No mover box exists.
- The `MAX(tileSize/4, 50)` margin participates **only in fcn.0001ede1's zone classification**, i.e.
  in the forbidden-zone (wall-hug) stage. It is not applied to the destination-tile pick (that is a
  plain point quantisation) and not applied to object AABBs.
- Effective behaviour: point-vs-tile with a margin-wide keep-out band around solid tiles
  (= wall standoff of tileSize/4, min 50 world units), plus point-vs-square for objects/bodies.

---

## 8. COMPLETE IMPLEMENTABLE ALGORITHM (remake-ready pseudo-code)

```csharp
// Per frame, input layer produces worldDelta (dx,dy) in world units (float ok), already dt-scaled.
// tileSize = labyrinth.EffectiveWallWidth; margin = Max(tileSize/4, 50);
// partyClass = 0 (save word @+0x1A4, never nonzero in practice); npcClass = NoClip flag ? 1 : 0.
// classBit(raw) => (raw & (0x08 << class)) != 0     // raw = Wall/Object Collision low byte, or FC Unk1

bool TilePassable(int tx, int ty, int cls) {
    if (outOfBounds(tx, ty)) return false;                        // off-map = solid
    var wall = GetWall(tx, ty);                                   // content >= 101
    if (wall != null && ((wall.Collision >> (3+cls)) & 1) != 0) return false;
    var floor = GetFloor(tx, ty);                                 // FC[floorIdx-1]! (off-by-one already fixed)
    if (floor != null && ((floor.Unk1 >> (3+cls)) & 1) != 0) return false;
    var ceil = GetCeiling(tx, ty);
    if (ceil != null && ((ceil.Unk1 >> (3+cls)) & 1) != 0) return false;
    return true;                                                  // floorIdx==0 is NOT a block
}

int Zone(int subX, int subY) {
    int zx = subX < margin ? 0 : subX >= tileSize-margin ? 2 : 1;
    int zy = subY < margin ? 0 : subY >= tileSize-margin ? 6 : 3;
    return zx + zy;
}

// rows: worldNbr offset -> zoneMask   (world space; adapt sign to your tile-Y convention)
static (int wx, int wy, int mask)[] Nbr = {
    (-1,-1,0x001),(0,-1,0x007),(1,-1,0x004),(-1,0,0x049),(1,0,0x124),(-1,1,0x040),(0,1,0x1c0),(1,1,0x100) };

bool StepAllowed(pos, delta, id, cls) {
    var dst = pos + delta;
    var dstTile = TileOf(dst); var srcTile = TileOf(pos);
    if (outOfBounds(dstTile)) return false;
    if (id == PARTY && noclip) return true;
    if (!TilePassable(dstTile, cls)) return false;

    int forbidden = 0;
    foreach (r in Nbr)
        if (!TilePassable(dstTile + worldToTile(r.wx, r.wy), cls)) forbidden |= r.mask;
    int sub = dst mod tileSize;  int dz = Zone(sub);
    if ((forbidden & (1 << dz)) != 0) {
        if (dstTile != srcTile || dz != Zone(pos mod tileSize)) return false;
        // already hugging: block components pushing toward the forbidden edges
        switch (dz) { // world-space: zones 0/3/6 are the low-X band, 0/1/2 the low-Y band, etc.
            case 0: if (delta.x < 0 || delta.y < 0) return false; break;
            case 1: if (delta.y < 0) return false; break;
            case 2: if (delta.x > 0 || delta.y < 0) return false; break;
            case 3: if (delta.x < 0) return false; break;
            case 5: if (delta.x > 0) return false; break;
            case 6: if (delta.x < 0 || delta.y > 0) return false; break;
            case 7: if (delta.y > 0) return false; break;
            case 8: if (delta.x > 0 || delta.y > 0) return false; break;
        }
    }
    // objects on dest tile: point vs square, class-gated
    var group = GetObjectGroup(dstTile);      // content 1..100
    if (group != null)
        foreach (var so in group.SubObjects) {
            if (so == null) continue;
            var o = objects[so.ObjectInfoNumber];
            if (((o.Collision >> (3+cls)) & 1) == 0) continue;
            int half = o.MapWidth / 2;                       // SQUARE: same half for both axes
            if (sub.x >= so.X - half && sub.x < so.X + half &&
                sub.y >= so.Z - half && sub.y < so.Z + half) return false;
        }
    // NPC bodies (and party body for NPC movers): same square test with rel = other - dst,
    // prefilter |rel| < tileSize; original compares (sub - rel) against the sub-object slots (§6 caveat).
    foreach (var npc in liveNpcs) { ... verbatim per §6 ... }
    return true;
}

bool TryMove(ref pos, ref delta, id, cls) {                       // fcn.0001e650
    if (StepAllowed(pos, delta, id, cls)) { pos += delta; return true; }
    var first  = (delta.x > delta.y) ? (delta.x, 0) : (0, delta.y);   // signed compare quirk
    var second = (delta.x > delta.y) ? (0, delta.y) : (delta.x, 0);
    if (StepAllowed(pos, first,  id, cls)) { pos += first;  delta = first;  return true; }
    if (StepAllowed(pos, second, id, cls)) { pos += second; delta = second; return true; }
    delta = (0,0); return false;
}
```

Remake-specific fix list implied by this doc:
1. **Change the wall/floor/ceiling/object mask from `0x78` to `0x08` (party)** — i.e. bit 3 only —
   and thread the class (0 party, 1 NoClip NPCs) if NPC fidelity is wanted. This un-blocks the
   `0x10`/`0xF0` records (pillar bridges, plates, NoClip-fences) that cause "impassable archways /
   pillar bridges".
2. Implement the 9-zone forbidden-band stage for the *standoff* distance = MAX(tileSize/4, 50)
   world units (scaled into remake units), including the "already hugging → away/parallel only"
   switch. This replaces any ad-hoc collision radius; the mover is a point.
3. Keep axis-fallback order (full → one axis → other) at the TryMove level, not inside the test.
4. Object collision: point-in-square per sub-object, half-extent MapWidth/2 both axes, class-gated;
   no margin around objects; whole-step reject on hit (slide emerges from the axis fallback).
5. No `floorIndex == 0` block; off-map (and out-of-1..W/1..H) is solid; wall content ≥ 101 only.
6. Party noclip flag (script/debug) bypasses EVERYTHING including bounds pt.2-5 (only the map-bounds
   check precedes it).
7. **[DONE 2026-07-06]** e832 steps 4-5 (entity bodies): `NpcBodyCollider3D` collides movers with
   every live NPC's object-group AABBs (the "corrected port" frame of §6: box centre = NPC
   continuous position × tileSize + subobj.xz, half = MapWidth/2, class-gated, 1-tile pre-cull);
   `Npc3D` blocks NPC steps AND the schedule glide from moving into the party's body (step 4,
   0.75-tile approach gate, closer-only so an overlapped NPC can leave). Live-verified on Jirinaar:
   party stops at the stationary gate-guard's box edge (z=55.393 vs edge 55.42) and can walk around
   it; probe endpoint `GET /collide?x=&y=[&cls=]` samples the exact point-collision the movers use.

---

## 9. CONFIDENCE / OPEN ITEMS

| Claim | Status |
|---|---|
| Bit = (11+class) of record offset-0 dword, class per-entity; party class = word[0x153ccc] = 0 | **CONFIRMED** (writers exhaustively enumerated) |
| MapNpcFlags 0x40 (NoClip) → NPC class 1 | **CONFIRMED** (0x3ef81..0x3efac) |
| No per-direction collision anywhere in the movement path | **CONFIRMED** (no direction ever computed on the path) |
| e832 return 1=allowed (older doc inverted) | **CONFIRMED** (e650 commit path) |
| Zone/margin/table/switch semantics as §5 | **CONFIRMED** (full disassembly + table dump) |
| f07e square AABB, MapWidth/2 both axes, MapHeight unused | **CONFIRMED** |
| NPC-body test frame-mixing (px vs world-rel) | CONFIRMED the formula; *intent* INFERRED (original approximation) |
| Party class could in principle be nonzero via crafted save (+0x1A4) | noted; no in-game writer sets it |
| fcn.0001f294 = tile-ray walk (line of walk/teleport validation) using same predicate+class | PARTIAL (function identified, ray-walk structure seen; full decode not completed — not needed for movement) |
| fcn.000127ce / fcn.00012875 gate pair around party move | INFERRED (map resource lock/unlock; opaque) |
| fcn.0005c40e(10) on tile change | INFERRED (step-trigger notification; outside scope) |
