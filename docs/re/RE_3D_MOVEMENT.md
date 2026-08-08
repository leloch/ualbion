# Reverse-engineering Albion's 3D (first-person dungeon) MOVEMENT & TURNING model

> Live document. Read-only RE against MAIN.EXE (radare2 project `albion_aaa`). All addresses are
> file/linear addresses as shown by radare2 (`pdf @ <addr>`). Companion: docs/re/RE_OPCODES_WORLD.md
> (Spinner = the same 14-bit angle model + turn animation), docs/re/RE_INDEX.md, docs/re/RE_NOTES.md.

---

## KNOWN FACTS (running list — addresses)

### Globals (CONFIRMED)
- `word[0x153b34]` = party tile **X**; `word[0x153b36]` = party tile **Y** (the dword at 0x153b34/0x153b32 holds a 16.16-style packed value; `sar 0x10` extracts the integer tile, see `0x1d68e`).
- `word[0x153b38]` = party **facing quadrant** 0..3 (N/E/S/W).
- `dword[0x14a48c]` = **camera angle** in its **high word**: `angle = dword[0x14a48c] >> 0x10`, 14-bit space (full circle = 0x4000, one quadrant = 0x1000, one of 8 dirs = 0x800).
- `word[0x14799c]` = **"in 3D mode" gate** (1 = first-person 3D map active).
- `word[0x1479b0]` = **smooth-movement mode flag** (0 = classic step/tile movement, !=0 = smooth/free continuous movement). Selected at `0x14c91`.
- Object-relative camera origin used by the renderer: `dword[0x14a482]` = camera world X, `dword[0x14a488]` = camera world Y (set in `fcn.0001d670`).
- Movement key-state flags (1 byte each, set elsewhere from the keymap):
  - `byte[0x13ff6f]` = "X-axis key A" → contributes **horizontal = -1**
  - `byte[0x13ff71]` = "X-axis key B" → contributes **horizontal = +1**
  - `byte[0x13ff6c]` = "Y-axis key A" → contributes **vertical = -1** (forward)
  - `byte[0x13ff74]` = "Y-axis key B" → contributes **vertical = +1** (back)

### Functions (CONFIRMED)
- `fcn.0001d670` = **enter-3D facing→camera-angle sync**: `camAngle = (-(facing<<0xc)) & 0x3fff` (`0x1d6c8..0x1d6db`). Identical to the Spinner formula.
- `fcn.0001318e` = sets facing (`word[0x153b38]=ax`, `0x131c2`) then, if 3D, calls `fcn.0001d670` to resync the camera angle.
- `fcn.000149a8(eax=actionCode)` = **the party-movement command** (builds a request struct at `0x134d08`, calls the resolver `fcn.00014afc`, writes results back to X/Y/facing). Called with 8 distinct action codes 0..7.
- `fcn.00014afc(req)` = **movement/turn resolver** (interprets actionCode + facing → new facing + tile delta, with collision).
- `fcn.0001573c(req)` = **classic step-mode executor** (collision-probes, fills `req[+8]=dx, req[+0xa]=dy`, may TURN). Used when `word[0x1479b0]==0`.
- `fcn.00014d47(req)` = smooth-mode executor (used when `word[0x1479b0]!=0`).
- `fcn.00015c22(req, dir)` = **TURN executor** (set facing to `dir`; if blocked, try `dir+2`).
- `fcn.00015d39(req, dir)` = **move-with-turn** (reads delta from the 4-quadrant table at `0x134be4`, sets `req[+8/+0xa]`, sets turn target `req[+0xe]=dir`).
- `fcn.00013bf5` = **on-screen movement-compass (mouse) dispatcher**: maps a 3×3 click zone (0..8 from `fcn.000141df(mx,my)`) to an action code and calls `fcn.000149a8`.
- `fcn.00013da4` = **keyboard movement handler**: reads the 4 key-state bytes into (horizontal,vertical) ∈ {-1,0,1}², converts via table `0x134c26` to an action code, calls `fcn.000149a8`.
- `fcn.00042bbd(x1,y1,x2,y2; arg=oldFacing)` = **"face toward a target tile"** helper (delta→facing), used by `fcn.00042872` (camera/object sync).
- `fcn.0001d99f(delta)` = **continuous turn**: `dword[0x14a48c] += delta<<0x10; wrap to 14 bits` (smooth-mode + scripted camera).

### Data tables (CONFIRMED — read out of the binary)
- `0x134be4` = **4-quadrant facing→tile-delta table** (stride 4 = `{dx:word, dy:word}`). This is THE crux table.
- `0x134bf4` = **8-direction→tile-delta table** (stride 4). (Sits 16 bytes after 0x134be4; the 8-dir collision/step path uses this.)
- `0x134c26` = **keyboard (vert,horiz)→action-code** table (3×3, 9 words, 6-byte rows indexed by `(signY+1)`, word at `signX*2+2`).
- `0x1494a` = relative-action→facing helper table (8 words).
- `0x3ec4f` / `0x3ec61` = delta→facing tables used by `fcn.00042bbd`.

---

## 1. THE FACING → TILE-DELTA TABLE (the crux) — CONFIRMED

Table at **`0x134be4`**, indexed by the facing quadrant `word[0x153b38]` (0..3), 4-byte rows `{dx:i16, dy:i16}`
(read via `pv2`; `fcn.00015d39 @ 0x15d5f/0x15d72`):

| facing | name  | dx | dy |
|:------:|:-----:|:--:|:--:|
| 0      | North | 0  | **-1** |
| 1      | East  | **+1** | 0  |
| 2      | South | 0  | **+1** |
| 3      | West  | **-1** | 0  |

So **"forward one tile" = `(X,Y) += table[facing]`**. Positive Y is SOUTH; North decreases Y.
This matches the engine's other movement code (`MapNpc` / 2D party movement) where +Y is downward/south.

The 8-direction table at **`0x134bf4`** (used by the step-collision path `fcn.0001573c`, for the 8 click-zones /
diagonal keys) is the natural superset, clockwise from North:

| dir | 0 N | 1 NE | 2 E | 3 SE | 4 S | 5 SW | 6 W | 7 NW |
|-----|:---:|:----:|:---:|:----:|:---:|:----:|:---:|:----:|
| dx  | 0   | +1   | +1  | +1   | 0   | -1   | -1  | -1   |
| dy  | -1  | -1   | 0   | +1   | +1  | +1   | 0   | -1   |

### Facing ↔ camera-angle (CONFIRMED, `fcn.0001d670 @ 0x1d6c8`, and Spinner `0x3a92f`)
```
camAngle14 = (-(facing << 0xc)) & 0x3fff     ; stored in HIGH word of dword[0x14a48c]
```
| facing | quadrant | camAngle (14-bit) |
|:------:|:--------:|:-----------------:|
| 0 N    | 0        | 0x0000 |
| 1 E    | 1        | 0x3000 |
| 2 S    | 2        | 0x2000 |
| 3 W    | 3        | 0x1000 |

i.e. the *facing index* increments **clockwise** (N→E→S→W), but the *14-bit angle* runs the other way
(angle goes 0 → 0x3000 → 0x2000 → 0x1000 because of the unary negate). The renderer consumes the angle;
the gameplay/collision consumes the facing index and the `0x134be4` delta table. **For a remake you only
need the facing→delta table above plus the angle formula; the negate is purely the renderer's handedness.**

---

## 2. MOVEMENT MODEL: step-based, one tile per command — CONFIRMED

- Classic mode (`word[0x1479b0]==0`) is **discrete, one tile per movement command** (`fcn.0001573c`
  fills `req[+8]=dx, req[+0xa]=dy` from the per-direction delta table, then `fcn.000149a8` adds them once to
  `word[0x153b34]/[0x153b36]` at `0x14aa3/0x14ab0`). A single key/zone press = one tile (or one turn).
- The optional **smooth-movement mode** (`word[0x1479b0]!=0`, `fcn.00014d47` + `fcn.0001e28a`) does continuous
  sub-tile translation/rotation (calls `fcn.0001d99f` to add to the camera angle each frame, scales by
  sin/cos at `0x196ca0/0x196cd4`). This is the original's "smooth 3D scrolling" config option; the default and the
  faithful step model is the discrete one. **Recommendation: implement the discrete step model.**
- Collision: before committing, `fcn.0001573c` probes the destination tile via `fcn.00016340`/`fcn.000166bf`
  (wall/blocked checks). If blocked, the move is rejected (dx/dy left 0). Diagonal moves require both component
  tiles to be free (the bit-mask loop at `0x157dd..0x158ab`).

---

## 3. THE CONTROL MAP: 8 actions on a clockwise ring — CONFIRMED

`fcn.000149a8` is invoked with an **action code 0..7** that is a **direction RELATIVE to the screen / compass**,
laid out as a clockwise ring with **0 = forward (ahead)**:

```
        action 0 (forward / ahead)
   7  \      |      /  1
       \     |     /
  6 ---- (no move) ---- 2
       /     |     \
   5  /      |      \  3
        action 4 (back / behind)
```

### On-screen movement compass (mouse) — `fcn.00013bf5`
Zone (3×3, from `fcn.000141df(mouseX,mouseY)`) → action code (switch at `0x13c8c`):
```
zone:  0 1 2      action:  7 0 1
       3 4 5  -->          6 . 2     (zone 4 = center = no-op)
       6 7 8               5 4 3
```

### Keyboard — `fcn.00013da4` + table `0x134c26`
Reads the 4 key flags into `horiz` (`0x13ff6f`=-1, `0x13ff71`=+1) and `vert` (`0x13ff6c`=-1, `0x13ff74`=+1),
then `action = table0x134c26[(vert+1) row][horiz col]`:

| vert\horiz | -1 | 0 | +1 |
|:----------:|:--:|:-:|:--:|
| **-1 (up/fwd)**   | 7 | **0** | 1 |
| **0**             | 6 | (none=0xFFFF) | 2 |
| **+1 (down/back)**| 5 | **4** | 3 |

So with the original key bindings: **Up = action 0 (forward), Down = action 4 (back), Left-only = action 6,
Right-only = action 2**, and the corners give the diagonal actions 1/3/5/7.

(The exact scancodes bound to the 4 flag bytes are set by the engine's keymap elsewhere; the flags themselves
are the movement inputs and are read identically by both the step handler `fcn.00013da4` and the smooth
handler `fcn.0001e28a`. See COULD-NOT-DETERMINE.)

---

## 4. TURNING: discrete 90° quadrant turns — CONFIRMED

There is **no free-turn key in the classic model**; turning is produced by the *side* actions, and the engine
turns the party by **discrete 90° quadrants** (`facing = (facing±1)&3`), animating the camera angle along the
shorter arc (same loop as the Spinner, see docs/re/RE_OPCODES_WORLD.md).

In `fcn.0001573c`, after classifying the requested absolute direction relative to the current facing
(switch at `0x158b2`), the **pure-sideways actions (2 = right, 6 = left) call the TURN executor
`fcn.00015c22`**, NOT a strafe:
- case "turn right" → `fcn.00015c22(req, facing+1 & 3)` (e.g. `0x15c00 mov edx,3` paths / `0x15bee mov edx,1`).
- case "turn left"  → `fcn.00015c22(req, facing+3 & 3)` i.e. `facing-1`.
`fcn.00015c22` sets the new facing into `req[+0x10]`; if the tile directly ahead of the new facing is blocked
it falls through to `dir+2` (`0x15c6e add eax,2; and eax,3`). `fcn.000149a8` then writes `req`'s facing back to
`word[0x153b38]` and (in 3D) the camera angle is re-driven toward `(-(facing<<0xc))&0x3fff`.

The diagonal actions (1,3,5,7) call `fcn.00015d39` = **turn *and* step** (turn toward the diagonal's nearest
quadrant and move one tile).

### Which way is "turn right"?
`facing` increments **clockwise** (N=0 → E=1 → S=2 → W=3). **Turn right = `facing = (facing+1)&3`**
(N→E→S→W). **Turn left = `facing = (facing+3)&3`** (N→W→S→E). Verified by the table `0x134be4` ordering
(N,E,S,W clockwise) and the `+1`/`+3` arguments passed to `fcn.00015c22`.

### Camera-angle animation (CONFIRMED — same as Spinner, `fcn.00042872 @ 0x42976..0x42a30`)
```
target = (-(facing<<0xc)) & 0x3fff
cur    = dword[0x14a48c] >> 0x10
cw  = (cur - target) & 0x3fff ;  ccw = (target - cur) & 0x3fff
step = (ccw <= cw) ? +1 : -1                       ; shorter arc
while (|cur - target| != 0) { cur += step*min(0x400,d); cur&=0x3fff;
                              dword[0x14a48c]=cur<<0x10; redraw } ; 0x400 per frame
```
Smooth mode instead feeds a per-frame delta to `fcn.0001d99f` (continuous `angle += delta<<0x10`).

---

## 5. STRAFE: does it exist? — CONFIRMED: NO (in the classic step model)

The classic 3×3 control map has **no strafe**. The side cells (actions 2/6, the mid-left/mid-right compass
zones and the Left/Right-only keys) **TURN** the party (`fcn.00015c22`), and the corner cells move diagonally
*with a turn* (`fcn.00015d39`). There is no code path that translates the party sideways without changing
facing in `fcn.0001573c`. (The smooth-mode handler `fcn.0001e28a` similarly maps the X-axis keys to rotation
via sin/cos, not lateral translation.) **So the original Albion 3D dungeon has forward/back + turn only — no
sidestep.**

---

## 6. THE "FACE-TOWARD-TILE" HELPER `fcn.00042bbd` (delta → facing) — CONFIRMED

Used by the object/camera-sync function `fcn.00042872` to point the camera at a target tile (not party input,
but documented because it inverts the delta table). Given `dx = x2-x1`, `dy = y2-y1`, it picks the dominant axis
and looks up tables at `0x3ec4f`/`0x3ec61` to return the facing quadrant whose `0x134be4` delta best matches
`(dx,dy)`. Net effect (consistent with §1):
`dy<0 → N(0)`, `dx>0 → E(1)`, `dy>0 → S(2)`, `dx<0 → W(3)`; diagonals snap to the dominant axis.

---

## IMPLEMENTATION RECOMMENDATION (for the remake — fixes the reversed forward/back)

Use the **facing→delta table from §1** directly. Bind **W/Up = forward, S/Down = back, A/Left = turn left,
D/Right = turn right** (the user's requested scheme — this is *exactly* the original keyboard semantics: Up=fwd,
Down=back, and the Left/Right inputs are turns).

```csharp
// Facing: 0=North, 1=East, 2=South, 3=West   (matches word[0x153b38])
static readonly (int dx,int dy)[] Forward = {
    ( 0,-1),  // 0 North  : -Y
    ( 1, 0),  // 1 East   : +X
    ( 0, 1),  // 2 South  : +Y
    (-1, 0),  // 3 West   : -X
};

void MoveForward() { var d = Forward[facing];          TryStep(x + d.dx, y + d.dy); }
void MoveBack()    { var d = Forward[facing];          TryStep(x - d.dx, y - d.dy); } // exact negation — never reverses
void TurnRight()   { facing = (facing + 1) & 3; ResyncCameraAngle(); }               // N->E->S->W (clockwise)
void TurnLeft()    { facing = (facing + 3) & 3; ResyncCameraAngle(); }               // N->W->S->E

// Camera angle (only if you mirror the original renderer's handedness):
int CameraAngle14() => (-(facing << 12)) & 0x3fff;   // full circle 0x4000, quadrant 0x1000
```

Why the remake currently reverses forward/back after moving: almost certainly the camera angle and the
tile-delta were derived from each other with the **negate sign inconsistent** between turn and move (the
original keeps the *facing index* canonical and only negates when producing the renderer angle). Anchor BOTH
forward/back AND the camera on the single `Forward[facing]` table and the `(-(facing<<12))&0x3fff` angle, and
they can never disagree: "back" is the literal arithmetic negation of "forward", independent of the angle.

If you want byte-faithful animation, turn by snapping the camera angle toward `CameraAngle14()` along the
shorter arc in 0x400 steps (the loop in §4 / Spinner). Functionally a snap is fine.

NO STRAFE in the faithful model. If you choose to add strafe as a remake convenience, the deltas are
`left = Forward[(facing+3)&3]`, `right = Forward[(facing+1)&3]` — but mark it a deliberate deviation.

---

## CONFIDENCE per finding

| Finding | Confidence | Key evidence |
|---|---|---|
| Facing→delta table `0x134be4` = {N(0,-1),E(+1,0),S(0,+1),W(-1,0)} | **CONFIRMED** | `fcn.00015d39 @ 0x15d5f/0x15d72` reads `word[dir*4 + 0x134be4 / +0x134be6]`; bytes dumped |
| 8-dir delta table `0x134bf4` (clockwise from N) | **CONFIRMED** | `fcn.0001573c @ 0x15781/0x1579a`; bytes dumped |
| `camAngle = (-(facing<<0xc))&0x3fff` | **CONFIRMED** | `fcn.0001d670 @ 0x1d6c8..0x1d6db`; matches Spinner `0x3a92f` |
| Movement is discrete one-tile-per-command (classic) | **CONFIRMED** | `fcn.000149a8 @ 0x14aa3/0x14ab0` adds delta once; smooth path gated by `word[0x1479b0]` |
| Action ring 0=fwd,4=back, clockwise | **CONFIRMED** | mouse switch `fcn.00013bf5 @ 0x13c8c`; keyboard table `0x134c26` |
| Side actions (2/6) TURN (no strafe) | **CONFIRMED** | `fcn.0001573c` switch calls `fcn.00015c22` (turn) for the sideways cases; corners call `fcn.00015d39` (turn+step) |
| Turn is discrete 90° quadrant, right=(f+1)&3, left=(f+3)&3 | **CONFIRMED** | `fcn.00015c22` args (`mov edx,1`/`mov edx,3`), `0x134be4` clockwise order |
| Turn animates camera angle along shorter arc, 0x400/frame | **CONFIRMED** | `fcn.00042872 @ 0x42976..0x42a30`; Spinner `0x3a979` |
| Smooth-movement mode exists (continuous), gated by `word[0x1479b0]` | **CONFIRMED** | `fcn.00014afc @ 0x14c91`; `fcn.0001e28a`; `fcn.0001d99f` |
| Keyboard inputs: vert keys = fwd/back, horiz keys = turn | **CONFIRMED (semantics)** | `fcn.00013da4` reads `0x13ff6c/6f/71/74` → `0x134c26` table |

## COULD NOT DETERMINE
- **The exact scancodes** bound to the 4 movement key-flag bytes (`0x13ff6c/6f/71/74`). They are set by the
  engine's keymap/config layer (no direct `mov byte[…],imm` writes — they are populated indirectly), and Albion
  let the player remap keys. The *semantics* are nailed (vertical pair = forward/back, horizontal pair = turn);
  the literal default keys (arrow keys vs WASD-equivalent) were not traced to a scancode constant here. For the
  remake this does not matter — bind whatever keys you like to "forward/back/turn-left/turn-right".
- **Whether the on-screen compass corner zones (diagonal actions 1/3/5/7) were exposed to the player in the 3D
  dungeon UI** or only reachable via diagonal key combos. The code supports all 8; the 3×3 mouse compass exposes
  all 8 non-center zones. Cosmetic for the remake (which only needs fwd/back/turn).
- **The precise per-frame step size / timing of smooth mode** (sin/cos magnitudes at `0x196ca0`/`0x196cd4`) —
  not decoded, since the faithful default is the discrete step model.
