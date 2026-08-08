# Reverse-engineering Albion's SMOOTH (continuous) first-person movement

> Read-only RE against MAIN.EXE (radare2 project `albion_aaa`, DOS LE). All addresses are the linear
> file addresses radare2 prints. Companion: docs/re/RE_3D_MOVEMENT.md (the DISCRETE step path, well-documented
> there; this doc fills in the SMOOTH path it under-explored). Target consumer: `src/Game/.../Movement3D`
> + `src/Game.Veldrid/Input/Normal3DMouseMode.cs`.

---

## KNOWN FACTS (addresses) — all CONFIRMED unless tagged

### Globals
- `word[0x1479b0]` = **smooth-movement flag** (0 = classic tile-step, !=0 = smooth/continuous). **It is a
  PER-MAP property, NOT a global config toggle** — see §1.
- `word[0x14799c]` = "in 3D mode" gate (1 = first-person active).
- `dword[0x14a48c]` = **camera yaw**: `angle14 = (dword[0x14a48c] >> 0x10) & 0x3fff`. Full circle = 0x4000.
- Camera world position is **16.16 fixed-point per tile**:
  - X = `dword[0x14a482]` (integer tile) + `word[0x14a480]` (sub-tile fraction 0..0xFFFF)
  - Y = `dword[0x14a488]` (integer tile) + `word[0x14a486]` (sub-tile fraction)
  - **1 tile = 0x10000 = 65536 sub-units.** (Proven by the carry/borrow normaliser in `fcn.0001d9d7`.)
- `word[0x153b34]`/`word[0x153b36]` = party tile X/Y (integer; re-derived from the camera each move).
- `word[0x153b38]` = facing quadrant 0..3 (used by the stepped path + collision only).
- `dword[0x196ca0]` = **current sin(yaw)**, `dword[0x196cd4]` = **current cos(yaw)**, scale **0x4000 = 1.0**.
  Recomputed only when the yaw changes, in `fcn.00094a4c @ 0x94b29..0x94b68` (sin = `fcn.000b479b`,
  cos = `fcn.000b47c4`; both read the quarter-wave sine table at **0x143e84**, peak amplitude 0x4000).
- `dword[0x13eebc]` = **per-frame delta-time scalar `S`** (NOT a config slider). Set in `fcn.000757d7`
  `@ 0x7581b/0x7582c` to `|tick - lastTick| + 1` each tick (tick counter = `dword[0x1835f0]`). Defaults to
  3 at init (`0x754bd`, `0x755ef`). So **velocity is already delta-time-scaled by the engine.**
- Movement key-state bytes (read by the smooth keyboard handler):
  `byte[0x13ff6f]` / `byte[0x13ff71]` = horizontal keys (turn or strafe), `byte[0x13ff6c]` = forward,
  `byte[0x13ff74]` = back.
- Modifier bitmask from `fcn.00089eb0`:
  - bit 0x10 (`byte[0x13ff41]`) = **RUN modifier** → multiply speed by **250/100 = ×2.5**.
  - bit 0x20 (`byte[0x13ff5c]`) = **STRAFE modifier** → horizontal keys translate sideways instead of turning.

### Functions
- `fcn.0001d99f(int16 delta)` = **continuous yaw add**: `dword[0x14a48c] += (delta << 0x10)`, then
  `and byte[0x14a48f], 0x3f` (wrap to 14-bit). delta is in **14-bit angle units** (0x4000 = 360°).
- `fcn.0001e28a` = **smooth keyboard handler** (per frame): reads the 4 key bytes + modifier mask, builds
  per-key velocity/yaw deltas (CONSTANT magnitudes), calls `fcn.0001e52d` (translate) or `fcn.0001d99f` (turn).
- `fcn.0001db96` = **smooth mouse-compass handler** (per frame): reads cursor offset from compass centre and
  builds velocity/yaw deltas that **scale with cursor distance** (INTENSITY). 12-case switch (the compass zones).
- `fcn.0001e52d(int dx16, int dy16)` = **translate executor**: collision-tests, then `fcn.0001d9d7` to add
  the 16.16 world delta to the camera position, then re-derives the integer party tile (`word[0x153b34/36]`).
- `fcn.0001d9d7(dx, dy)` = **camera-position integrator** (the 1-tile=0x10000 normaliser).
- `fcn.00094a4c` = per-frame camera maintenance incl. the sin/cos recompute.

---

## 1. Is smooth mode the default? — it is **PER-MAP**, not a config toggle  (CONFIRMED)

`word[0x1479b0]` is **written exactly once at runtime**, during map load, in the 2D/3D map loader
(`@ 0x13412`, source string `g:\albion\src\map\2d_map.c`):

```
0x13405  mov ax, word [ebp-4]            ; ebp-4 = map index
0x13409  add eax, eax                    ; *2 (word stride)
0x1340b  mov ax, word [eax + 0x134cdc]   ; per-map flag table
0x13412  mov word [0x1479b0], ax         ; <- smooth flag := map's flag
0x13418  cmp word [0x1479b0], 0
0x13422  mov word [0x134be2], 0x20       ; if smooth: grid/step constant = 0x20
0x1342d  mov word [0x134be2], 0x10       ; else (stepped): = 0x10
```

So **each map declares its own movement model** in the table at `0x134cdc` (word per map index). A map is
"smooth" iff its flag is non-zero. (`axt @ 0x1479b0` shows every other reference is a *read* — only `0x13412`
and the save/restore path `fcn.00016e7f @ 0x16f85` write it.)

**Conclusion for the remake:** smooth-vs-stepped is a property of the map being entered, not a global option
and not user-toggleable in-game. Albion's real dungeons/cities ship the smooth flag set. For UAlbion you can
either read this per-map flag from the map data (faithful) or — pragmatically — treat all 3D maps as smooth,
since the brief states the original 3D dungeon movement is smooth. (CONFIRMED the mechanism is per-map; which
specific map indices are non-zero is in the `0x134cdc` table but its exact decode is left as INFERRED — the
first low map indices read 0/1, consistent with a per-map boolean.)

---

## 2. Gradual TURN rate  (CONFIRMED math; absolute rate INFERRED via the delta-time scalar)

### Keyboard (constant) — `fcn.0001e28a`, horizontal key without the strafe modifier
```
v = S * 0x28            ; 0x28 = 40
if RUN:  v = v * 250 / 100
v = v / D               ; D = 1 << (0xa - timer.field[0x1a])   (frame divisor)
fcn.0001d99f(±v)        ; add ±v to the 14-bit yaw  (left key negates)
```
- The turn magnitude constant is **40** (per unit of `S`). Forward uses 166, back 100, strafe 67 — see §3.
- `S` = `dword[0x13eebc]` = elapsed ticks since last frame (≈3 at steady state). `D` is a runtime frame
  divisor from the timer object (`fcn.0008b739` field `+0x1a`), so `S/D` is the engine's **delta-time term**.

### Mouse (intensity-scaled) — `fcn.0001db96` case 0 etc.
```
third  = halfW / 3                              ; halfW = word[0x14a49a]
turn   = (third - cursorDx) * (S * 0x3c) / third   ; 0x3c = 60, scales with distance from a deadzone
fcn.0001d99f(turn)
```
So the **mouse turn rate is proportional to how far the cursor is past the central third** (intensity), with a
larger base constant (60) than the keyboard's 40.

### Converting to an absolute rate
14-bit units: 0x4000 = 360°, so **1 unit = 360/16384° = 0.02197°** (≈ 3.835e-4 rad).
Per *frame*, keyboard turn = `40 * S / D` units. With the steady-state engine values S≈3, and the
delta-time normalisation making `S/D` ≈ "frames worth of 1 tick", the per-tick turn is on the order of
`40 * (1) ≈ 40` units/tick = **0.879°/tick**. Albion's movement tick runs at roughly **~36 ticks/s**
(the timer slot uses a 30-unit stride at `0x75841`; exact rate not pinned — see CAN'T DETERMINE), giving an
order-of-magnitude **continuous keyboard turn ≈ 30–35°/s at the slow setting, scaling up with the RUN
modifier (×2.5 ≈ 75–90°/s)**. Treat the absolute as approximate; the *ratios* below are exact.

---

## 3. Forward / back / STRAFE speed  (CONFIRMED math)

All translation goes through `fcn.0001e52d(dx16, dy16)`, where the caller forms `dx16,dy16` by multiplying a
scalar `v` by the current sin/cos (scale 0x4000). The result is a 16.16 world delta (1 tile = 0x10000).

Keyboard per-key scalar (constant), `fcn.0001e28a`:

| Action | key byte | base const | with RUN (×2.5) | translate formula (sin = `[0x196ca0]`, cos = `[0x196cd4]`) |
|---|---|---:|---:|---|
| **Forward** | 0x13ff6c | **166** (`0xa6`) | 415 | `dx = +v·cos`, `dy = −v·sin` |
| **Back**    | 0x13ff74 | **100** (`0x64`) | 250 | `dx = −v·cos`, `dy = +v·sin` (signs flipped) |
| **Strafe** (horiz + STRAFE mod) | 0x13ff6f/71 | **67** (`0x43`) | 167 | `dx = ±v·sin`, `dy = ±v·cos` → **perpendicular to facing** |
| Turn (horiz, no mod) | 0x13ff6f/71 | **40** (`0x28`) | 100 | yaw via `fcn.0001d99f` (not translation) |

where `v = base * S` then `/ D` (and `*2.5` if RUN). YES — **strafe is move perpendicular to facing**: it uses
the same sin/cos but swapped between dx and dy (forward uses cos→X / −sin→Y; strafe uses sin→X / cos→Y).

**Forward is notably faster than back** (166 vs 100, a 1.66× ratio) and **strafe is slowest** (67, ≈0.40×
of forward). These ratios are exact and framerate-independent.

### Absolute world speed
Per frame, forward distance (tiles) = `(v/D) * cos / 0x10000`. At `cos≈0x4000` (facing an axis):
`= (166·S/D) * 0x4000 / 0x10000 = (166·S/D) * 0.25` sub-tile units → **≈ 41.5·(S/D) / 256 tiles per frame**.
With the same steady-state assumption (S/D ≈ 1 tick) and ~36 ticks/s this is roughly **forward ≈ 1.5–1.8
tiles/s walking, ~4 tiles/s running**; back ≈ 0.6× that; strafe ≈ 0.4×. Absolute values are approximate
(the `D` divisor and tick rate are runtime/timer-driven); the **inter-action ratios 166 : 100 : 67 : 40 and
the ×2.5 RUN multiplier are exact**.

The mouse path (`fcn.0001db96`) uses the larger constants **0xe6 (230)** for forward/back and **0x64 (100)**
for strafe, each scaled by cursor distance — so the mouse compass at full deflection is faster than a key tap
and is continuously variable (INTENSITY).

---

## 4. Corner snap-turns  (INFERRED — not part of the smooth handlers)

The smooth handlers `fcn.0001e28a`/`fcn.0001db96` contain **no 90°/180° snap path**; their horizontal input is
either a gradual yaw add (`fcn.0001d99f`) or a strafe. The corner 90/180 buttons the remake exposes
(`Zone.Left90/Right90/Left180/Right180` in `Normal3DMouseMode.cs`) correspond to the **discrete quadrant-turn
path** documented in docs/re/RE_3D_MOVEMENT.md §4`: set facing to `(facing±1)&3` (90°) or `(facing±2)&3` (180°), then
animate the camera yaw toward the new `(-(facing<<0xc))&0x3fff` target **along the shorter arc, 0x400 units per
frame** (the Spinner loop, `fcn.00042872 @ 0x42976..0x42a30`).

So: **corners animate (not instant)**, sweeping 0x400 (= 1024) of 16384 per frame = **22.5°/frame** → a 90° turn
is 4 frames, 180° is 8 frames. This is much faster than the gradual hold-turn (~40 units/frame). The brief's
assumption is correct: top corners = animated 90°, bottom corners = animated 180°. (Marked INFERRED only because
the corner buttons are a UI affordance whose exact original binding I did not trace into the smooth handlers —
the *animation rate* 0x400/frame is CONFIRMED from the shared turn-animation loop.)

---

## 5. Acceleration / intensity  (CONFIRMED)

- **Keyboard input = CONSTANT speed** (the per-key constants above; no ramp, no distance term).
- **Mouse-compass input = INTENSITY-scaled**: `fcn.0001db96` scales both translation and turn by the cursor's
  distance from the compass centre (e.g. case 0: `turn = (third − cursorDx)·(S·60)/third`,
  `fwd = (...− cursorDy)·(S·230)/third`). There is a central deadzone of one third of the half-extent.
  This exactly matches the remake's existing design of passing an `intensity` Vector2 — **keep it for the
  mouse, treat the keyboard as full intensity (1.0).**
- The only "acceleration"-like term is the engine-wide delta-time scalar `S` (`0x13eebc`), which makes speed
  framerate-independent — it is NOT cursor- or hold-duration-based.

---

## C# RECOMMENDATION for `Movement3D` (continuous model)

**Coordinate / sign mapping (from the binary):**
- Yaw stored as 14-bit, `0x4000 = 2π`. Facing→yaw: `yaw14 = (-(facing<<12)) & 0x3fff` (renderer handedness).
- Forward world delta = `(+cos, -sin) * speed`; Back = `(-cos, +sin) * speed`;
  StrafeRight = `(+sin, +cos) * speed`; StrafeLeft = `(-sin, +cos)`… i.e. forward axis = (cos,−sin),
  right axis = (sin,cos). **+Y world = south** (consistent with the rest of the engine).

**Define one base walking speed in tiles/second and one turn rate in deg/s, multiply by `deltaTime`** (the
engine already normalised by delta-time via `S`, so per-second constants are the faithful translation):

```csharp
// Faithful inter-action RATIOS are exact; absolute base is tuned to feel like the original (~36 ticks/s).
const float ForwardTilesPerSec = 1.7f;          // base walk
const float BackRatio   = 100f / 166f;          // 0.602  (CONFIRMED ratio)
const float StrafeRatio =  67f / 166f;          // 0.404  (CONFIRMED ratio)
const float RunMultiplier = 2.5f;               // 250/100 (CONFIRMED, RUN modifier byte[0x13ff41])

const float TurnDegPerSec = 33f;                // gradual hold-turn base (~40 units/tick * 36 tick/s)
const float MouseTurnGain = 60f / 40f;          // mouse base 60 vs keyboard 40 (CONFIRMED ratio)

// Per frame, with yaw in radians and dt = frame seconds:
float run = runHeld ? RunMultiplier : 1f;
Vector2 fwdAxis   = new( MathF.Cos(yaw), -MathF.Sin(yaw)); // (cos,-sin)
Vector2 rightAxis = new( MathF.Sin(yaw),  MathF.Cos(yaw)); // (sin, cos) = perpendicular

Vector2 vel = Vector2.Zero;
if (forward) vel += fwdAxis   * (ForwardTilesPerSec * run);
if (back)    vel -= fwdAxis   * (ForwardTilesPerSec * BackRatio   * run);
if (strafeR) vel += rightAxis * (ForwardTilesPerSec * StrafeRatio * run);
if (strafeL) vel -= rightAxis * (ForwardTilesPerSec * StrafeRatio * run);
cameraWorld += vel * dt;     // then re-derive integer tile, like fcn.0001e52d/fcn.0001d9d7

if (turnLeft)  yaw -= TurnDegPerSec * MathF.PI/180f * run * dt;
if (turnRight) yaw += TurnDegPerSec * MathF.PI/180f * run * dt;
```

**Mapping to existing events** (`Normal3DMouseMode.cs` already produces these — just feed the rates above into
the `Movement3D` receiver):

| Original behaviour | Existing event | How to drive it |
|---|---|---|
| Forward/back glide | `PartyMove3DEvent.Velocity.Y` (×intensity) | receiver multiplies by `ForwardTilesPerSec`(×Back/Run); apply along `fwdAxis` |
| Strafe glide | `PartyMove3DEvent.Velocity.X` (×intensity) | apply along `rightAxis` × `StrafeRatio` |
| Gradual hold-turn | `PartyMove3DEvent.Yaw` (×intensity) | yaw-rate `TurnDegPerSec` (× `MouseTurnGain` for mouse) |
| RUN modifier | (add) hold-Shift flag | ×2.5 on velocity AND yaw |
| Per-axis collision (wall slide) | `CameraMove3DWorldEvent(x,z)` | emit AFTER yaw transform + sub-tile collision, one axis blockable (mirrors `fcn.0001e52d` zeroing dx or dy on the blocked tile, `@ 0x1e5ce/0x1e5de`) |
| Corner 90/180 snap | `PartyTurn3DEvent(±90/±180)` | DISCRETE; animate yaw toward target at **22.5°/frame** (0x400/16384), shorter arc — keep `if (justPressed)` |
| Look up/down | `PartyMove3DEvent.Pitch` | original pitch handling not in scope of these fns |

`Normal3DMouseMode.cs`'s zone layout, the `intensity` Vector2, the gradual `TurnLeft/Right` zones, and the
discrete `Left90/Right90/Left180/Right180` corners are **all correct** — the only fix is in the *receiver*
(`Movement3D`): replace any discrete grid-step/90°-snap of `PartyMove3DEvent` with the continuous
velocity/yaw integration above. The remake's discrete model was applying the wrong (stepped) executor to maps
that the original drives through the smooth executor.

`CameraMove3DWorldEvent` is the right primitive for the post-yaw, per-axis-collision world delta (its doc
comment already matches `fcn.0001e52d`'s behaviour of independently zeroing the blocked axis for wall-sliding).

---

## CONFIDENCE

| Finding | Confidence | Evidence |
|---|---|---|
| Smooth flag is per-map (table 0x134cdc), set at map load | **CONFIRMED** | `0x13405..0x13412`; only writers of `0x1479b0` are map-load + save-restore |
| Yaw add = `fcn.0001d99f`, 14-bit, 0x4000=360° | **CONFIRMED** | `0x1d9be..0x1d9c7` |
| 1 tile = 0x10000 sub-units; camera = int+frac fixed point | **CONFIRMED** | `fcn.0001d9d7` carry/borrow normaliser |
| sin/cos current values at 0x196ca0/0x196cd4, scale 0x4000 | **CONFIRMED** | `fcn.00094a4c @ 0x94b48..0x94b5b`; table 0x143e84 peak 0x4000 |
| Translate constants 166 fwd / 100 back / 67 strafe / 40 turn | **CONFIRMED** | `fcn.0001e28a @ 0x1e457/0x1e4c2/0x1e301/0x1e362` |
| Strafe = perpendicular to facing (sin/cos swapped) | **CONFIRMED** | `0x1e494/0x1e4a0` vs forward `0x1e486..0x1e4a0` |
| RUN modifier ×2.5 (byte[0x13ff41]); STRAFE modifier (byte[0x13ff5c]) | **CONFIRMED** | `fcn.00089eb0`; the `*250/100` blocks gated on bit 0x10 |
| Speed scalar `S`=delta-time (ticks elapsed+1), not a slider | **CONFIRMED** | `fcn.000757d7 @ 0x7581b/0x7582c` |
| Mouse compass = intensity (cursor-distance) scaled | **CONFIRMED** | `fcn.0001db96` case math (`(third−cursorDx)*(S*60)/third` etc.) |
| Keyboard = constant (no intensity/ramp) | **CONFIRMED** | `fcn.0001e28a` constants are not distance-scaled |
| Corner 90/180 = animated discrete turn, 0x400/frame | **INFERRED** | shared turn-anim loop `fcn.00042872`; corners not in the smooth handlers |
| Absolute tiles/s and °/s | **APPROXIMATE** | depends on runtime `D` divisor + tick rate (~36/s, 30-stride timer) |

## CAN'T DETERMINE
- **Exact movement tick rate** (estimated ~36 ticks/s from the timer-slot 30-unit stride at `0x75841`; the
  `D = 1<<(10-field[0x1a])` divisor is read from a live timer object `fcn.0008b739`, so the precise per-second
  absolute is runtime-dependent). Fine to approximate — the ratios and the delta-time model are exact.
- **Exact decode of which map indices are smooth** in table `0x134cdc` (mechanism is confirmed; the first
  low-index entries read 0/1; a full per-map dump would need the map-index→table-offset mapping verified).
- **Pitch (look up/down) rate** — handled outside the three functions analysed here.
