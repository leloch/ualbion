# RE batch 6 (2026-06-13): soul-rise / ambient NPC sound-set table / animated 3D meshes

> radare2 against `albion_aaa` (MAIN.EXE, DOS LE). Conventions per `_RE_5A.md` / `_RE_5D.md`.
> CONFIRMED = read from disasm with consistent xrefs; INFERRED = strong pattern, one detail unverified.
> KNOWN going in: battle-view render class = MONCHAR byte sheet+0x0D → combatant+0x10 → vtable_2 row;
> sine hover oscillator cb 0x54eeb; flying-class Die handler 0x528fc (gravity fall vy −= 0.16/tick², floor y=−40);
> ambient sound-set table 13×0x28 @0x13db10, NpcState ActiveSfx0-3 = sample handles, positional looped via fcn.0004356a.

## Item 1 — Soul-rise animation for DEMONIC corpses — CONFIRMED (it is the BANISH SPELL, not a death-class hook)

### Trigger path — the three Banish spells only
The soul-rise is **NOT** a render-class / Die-handler variant in the battle-view vtable_2, and it is
**NOT** keyed on a demon class-bit (0x44). It is the *bespoke visual of the three Banish-demon spells*
(spell numbers 62/63/64). All three spell-effect handlers are thin wrappers that tail-call the SAME
worker `0xa1b20` via the spell-dispatch trampoline `fcn.0005fb21(spellNo, worker=0xa1b20)`:

```
0xa1aec  Banish v1: push 0x20; … ; mov ax,word[spellNo]; edx=0xa1b20; call fcn.0005fb21
0xa1fed  Banish v2: …                edx=0xa1b20; call fcn.0005fb21      (0xa2008)
0xa2021  Banish v3: …                edx=0xa1b20; call fcn.0005fb21      (0xa203c)
```
So a *normal* demon dying from a sword/other spell uses the ordinary corpse freeze (vtable_2 class 1–4
Die handlers, e.g. flying-class 0x528fc gravity fall). The "soul rising" only plays when the demon is
killed **by a Banish spell**. (Confirmed: `0xa1b20` is referenced only by the three Banish wrappers;
no death/Die handler and no class-bit test reaches it.)

### The worker `0xa1b20` (CONFIRMED, full trace) — three phases
Local frame: caster combatant ptr in `[ebp-0x54]`. Two helpers grab geometry:
- `fcn.00053208(combatant,&X,&Z,&Y)` = world position of a combatant's render slot
  (reads render-obj `+0x1e`: +8 worldX, +0xc worldZ, +0x10 worldY, anchor +0x24/+0x26).
  → caster X `[ebp-0x2c]`, caster Z `[ebp-0x50]`, caster Y `[ebp-0x4c]`.
- `fcn.00053164(combatant,&X0,&Y0,&X1,&Y1)` = the target sprite's screen/world bounding rect.
  → target X0 `[ebp-0x28]`, **Y0(top) `[ebp-0x18]`, Y1(bottom) `[ebp-0x14]`**, X1 `[ebp-0x24]`.
- `comgfx = fcn.000233b8(0x28, 0x28)` = **load+lock combat-graphic id 0x28 (#40) = the soul/spirit sprite**.
- anim slots are made with `fcn.0009796b(worldX, worldY, worldZ, gfxFrame, [push anchor=0x64, push comgfxPtr, push renderKindFlags])`
  → returns a pool slot (1000×58 B pool 0x168a84). renderKindFlags: 1→kind0 opaque, 2→kind4, 4→kind7,
  **8→kind 8 = TRANSLUCENT**. Slot fields: +6 gfxFrame(word), +8 X, +0xc Y, +0x10 Z, +0x20/+0x22 anchor(50,50 def),
  +0x24/+0x26 scaleX/scaleY(%), +0x2c comgfxPtr.
- The animation is paced by the global **spell-frame counter `word[0x15f120]`** (shared by ALL spell
  visuals 0x9b000-0xa9000; reset to 0 here at 0xa1c1d, incremented once per logic frame by the engine
  pump at 0x4afef). Each `fcn.00075e71` call = **WaitFrame** (advances one ~20Hz logic tick).

**PHASE A — descending banish-orb onto the demon (0xa1b56-0xa1c5b)** ("x1000 factor"):
- Allocate slot `[ebp-0x44]` (the orb): X = caster-tile X `[ebp-0x2c]`, **Y = 0x3e80 = 16000 (high above)**,
  Z = casterZ+5, gfxFrame ecx=0, anchor 0x64, comgfx #40, renderKind flag 8 → translucent.
- Static: slot+0x22=0x64; slot+0x24 = `(targetY1-targetY0)*100 / 0xc80(3200)` (scaleX from sprite height);
  slot+0x26 = 0xc350 = 50000 (scaleY).
- Set descent start `[ebp-0x48] = 0x6590 = 26000`, and `[ebp-0x10] = 0x2af8 = 11000` (used by phase C).
- Loop: `posY = 26000 − counter·1000` (clamped DOWN to target top `[ebp-0x24]`); slot+0xc = posY; WaitFrame;
  repeat until posY reaches the target. **Orb falls from Y 26000 → demon at 1000 world-units/frame.**

**PHASE B — resist gate + begin the lift (0xa1c5b-0xa1c9e)**:
- `fcn.000601a6` = the known deterministic magic-resist gate (lands iff caster mastery% > target MagicResist).
  If resisted → jump 0xa1fdf: free orb, demon survives, NO soul rise.
- If it lands AND target is a monster (kind==2): set target render-obj+4 = 4 (switch its render kind),
  WaitFrame, then run phases that LIFT both the demon body and spawn soul particles.

**PHASE C — soul rise + dissolve (0xa1ca3-0xa1fcc)** ("x250 factor" = rise velocity):
- Spawns **3 translucent soul particles** (`[ebp-0x1c]` 0..2). Each particle slot `[ebp-0x40]`:
  X = targetX + rand[-100..100], spread over the target's Y band, comgfx #40, renderKind 8 (translucent),
  +0x18 = rand[300..400], +0x22 = 0x64; behaviour cb `0x99480` attached via fcn.00053ba3, with
  per-frame velocity +0xc = −rand[100..150] (UP), +0xe = −rand[100..150], +0x10/+0x12 = 2,2.
- Main body lift loop (0xa1e17-0xa1fc6): `posY += counter·250` clamped UP to `[ebp-0x48]=26000`;
  writes the body render-obj+0xc = posY each WaitFrame; **the body rises at 250 world-units/frame**.
  Simultaneously the body's anchor/scale `render-obj+0x24` is reduced by `counter·3` (min 10) — the
  sprite **shrinks toward a point** as it ascends (the same `−counter·3, floor 10` is applied at 0xa1f59
  to the spawned slot and at 0xa1f94 to the demon's own render-obj). The shadow does not participate.
- When the body reaches Y 26000: `fcn.0005395f(render-obj)` frees the demon's battle sprite, then
  `fcn.0004e247(combatant)` = the **death finalizer** (applies death status fcn.000363c2/00036799; for a
  monster adds its XP reward `u16 sheet[+0x20]` to pool 0x15f104; calls after-turn cleanup 0x4b2ed).
- Finally frees the orb slot `[ebp-0x44]` (0xa1fdf) and returns.

### Velocities / constants summary (per logic frame ≈ 1/20 s)
| thing | value | meaning |
|---|---|---|
| orb start Y | 0x6590 = 26000 | top of the descent / top of the rise |
| descent speed | **1000 units/frame** (counter·1000) | banish orb falling onto demon |
| rise speed | **250 units/frame** (counter·250) | demon body + soul rising back to 26000 |
| sky cap | 0x3e80 = 16000 (orb spawn), 0x2af8 = 11000 ([ebp-0x10]) | spawn/limit heights |
| shrink rate | render-obj+0x24 −= counter·3, floor 10 | sprite shrinks to a dot as it ascends |
| soul sprite | comgfx **#0x28 (40)**, render **kind 8 = translucent** | the spirit graphic |
| particles | 3, vy = −rand[100..150]/frame, X spread ±100 | wisps drifting up, cb 0x99480 |
| tile = 64 world units (per _RE_5A) | so 1000/frame ≈ 15.6 tiles/frame (orb is fast), 250/frame ≈ 3.9 tiles/frame | |

### How it differs from the normal corpse freeze
- Normal demon death: vtable_2 Die handler for its render class (ground 1 = freeze in place;
  flying 3 = clone slot + gravity fall vy−=0.16/tick², floor y=−40). The sprite stays as a corpse.
- Banish death: the body is **never left as a corpse** — it is lifted off the floor (rise 250/frame),
  shrunk to nothing, drawn translucent, accompanied by 3 rising wisps, then the sprite is freed and the
  combatant killed. There is a preceding "banish orb" that drops from the sky (1000/frame) onto the demon
  to start it.

### Reimplementation note for BattleView.cs
Treat Banish (spells 62/63/64) as a dedicated death VFX, independent of the per-class Die handler:
1. On cast: spawn a translucent sprite (combat gfx #40) at screenX of the target, Y far above; animate it
   down at ~1000 u/frame to the target head over a few frames.
2. Roll the deterministic resist gate (mastery% > MagicResist). On fail, abort (demon lives).
3. On success: spawn 3 translucent wisps rising at 100-150 u/frame with ±100 X jitter; lift the demon
   sprite up at 250 u/frame, shrinking it (scale −3%/frame, min 10%) and drawing it translucent, until it
   reaches the sky height (~26000 in the original's world units); then remove the combatant (death + XP),
   skipping the normal corpse freeze entirely.
(World units here are the battle-view's internal 16.16-ish space; scale to BattleView's coordinate system —
the ratio that matters is descent:rise = 4:1, and the rise spans from floor to ~26000 over ~100 frames.)

---

## Item 2 — Ambient NPC sound-set table @0x13db10 — CONFIRMED (full contents + player + attenuation)

### Record layout (CONFIRMED from fcn.0004356a + fcn.00062f60)
Table = **13 rows × 0x28 (40) bytes**. **Each row is 4 sub-entries of 10 bytes** — one sub-entry per
ActiveSfx slot (sfx0..sfx3). Sub-entry = 5 words:
```
+0  word  sampleId   (0 = empty slot, skip)                       → fcn.00062f60 eax (sample resource id)
+2  word  unk1       percent (50/75/100) — secondary scale (w1)   → edx (stored voice+0x15, scaled like vol)
+4  word  volume     percent 0..100 (default 100 if 0)            → ebx (base volume, voice+0x17)
+6  word  loop/flag  if !=0 the live handle is KEPT in ActiveSfx  → ecx (==100 also sets voice flag bit1)
+8  word  radius     per-sample max range override (default 0x2af8=11000 if 0) → arg5 (voice+9 / arg_10h)
```
`record = 0x13db10 + soundSet*0x28 + sfxSlot*0xa`.

### Full table contents (13 sound-sets, sfx0..sfx3 sub-entries; empty = sampleId 0)
| set | sfx0 (ambient loop) | sfx1 (movement) | sfx2 (chase one-shot) | sfx3 (active-chase loop) |
|---|---|---|---|---|
| 0  | — | — | — | — (silent) |
| 1  | — | sid=151 unk1=50 vol=50 loop=100 | sid=56 loop=**0** (one-shot) | — |
| 2  | sid=11 unk1=75 vol=75 loop=100 | — | — | — |
| 3  | — | sid=154 unk1=75 vol=75 loop=**25** | sid=56 loop=0 (one-shot) | — |
| 4  | sid=151 unk1=100 vol=50 loop=100 **rad=20000** | — | — | — |
| 5  | sid=11 vol=100 loop=100 | — | — | — |
| 6  | — | sid=150 unk1=50 vol=50 loop=100 | — | — |
| 7  | — | — | — | — (silent) |
| 8  | — | — | — | — (silent) |
| 9  | — | sid=23 unk1=100 vol=50 loop=100 | — | — |
| 10 | — | — | — | — (silent) |
| 11 | — | sid=159 vol=100 loop=100 | — | — |
| 12 | sid=160 vol=100 loop=**50** | — | — | — |

(unk1/vol/loop default 100 in empty cells; only non-zero sampleId rows matter. SampleIds 11/23/56/150/151/154/159/160
are indices into the sample/SECTODO bank loaded by fcn.000233b8(sid, 30).)

### How a sound-set is selected & ActiveSfx held (CONFIRMED)
- MapNpc V2 byte1 → **NpcState+0x07 = Sound = the sound-set row 0..12** (per _RE_5D §8).
- NpcState slot base `0x159c58 + idx*0x80`. **ActiveSfx0..3 = words at 0x159c61 + idx*0x80 + slot*2**
  (= state+9/+0x0B/+0x0D/+0x0F). 0xFFFF = silent/not-playing.
- `fcn.0004356a(eax=npcIdx, edx=sfxSlot)` = START-IF-NOT-PLAYING for one slot:
  1. if ActiveSfx[slot] != 0xFFFF → return (already looping, do not restart).
  2. soundSet = state+7; if >= 13 → return.
  3. record = 0x13db10 + soundSet*0x28 + slot*0xa; if record.sampleId == 0 → return.
  4. (posX,posY) = fcn.00043714(npcIdx): 3D map → state+0x32/+0x36 world coords; 2D → state+0x2A/+0x2C
     tile coords << 4 (16 units/tile).
  5. handle = fcn.00062f60(sampleId, unk1, vol, loop, radius, posY, posX) — allocates a voice (64-voice
     pool, stride 0x23 @ 0x1775bc; gated by `byte[0x177f2c]&2` sound-enabled).
  6. **only if record.loop(+6) != 0** store handle into ActiveSfx[slot] (so a loop=0 sub-entry plays
     once and is never tracked — that's why sfx2 sid=56 is a fire-and-forget alert).
- `fcn.00043685(eax=npcIdx, edx=slot)` = STOP: if ActiveSfx[slot] != 0xFFFF → fcn.00063140(handle) stop
  the voice, set ActiveSfx[slot]=0xFFFF.

### Trigger cadence — fcn.000433ef (CONFIRMED), called once per NPC tick (2D 0x3ff26 / 3D 0x41397)
Loops all 96 NPC slots; for each WasActive (state+0x17) NPC:
- **sfx0 (ambient)**: `fcn.0004356a(idx, 0)` every tick → starts the looped ambient the first tick the NPC
  is live, then no-ops while it keeps playing (the loop self-perpetuates; it is NOT re-triggered on a
  period — the "cadence" is just "ensure-running each tick").
- **sfx1 (movement)**: if state+0x19 Flags bit2(0x04 = is-moving-this-tick) OR bit4(0x10) set →
  `fcn.0004356a(idx,1)` (start); else `fcn.00043685(idx,1)` (stop). So footstep/move loop runs only while
  the NPC is actually moving this tick.
- **sfx3 (active-chase loop)**: only if movement type (state+0x15) == 3 (ChaseParty): if Unk29 (state+0x29
  chase sub-state) is 0(idle) or 2(lost) → stop sfx3; otherwise (1/3/4/5 = actively chasing/deflecting) →
  start sfx3.
- **sfx2 (chase ALERT, one-shot)**: NOT driven here — it is fired by the chase handlers the moment a chase
  begins: 2D `fcn.000408fb @0x409d6` and 3D `fcn.00041c3c @0x41ce5` call `fcn.0004356a(idx, 2)`. Because
  its table loop field is 0 it plays once and isn't held.
- After the slot updates: (posX,posY)=fcn.00043714(idx); for each live ActiveSfx handle (!=0xFFFF) call
  `fcn.0006326b(handle, posX, posY)` → write the voice's current world position (voice+0x1b/+0x1f) so the
  mixer re-pans/attenuates it as the NPC moves. **Positions are refreshed every NPC tick.**

### Distance attenuation & pan — fcn.000638d5 (CONFIRMED, the audio mixer, called per audio frame)
For each of the 64 voices (base 0x1775bc, stride 0x23):
```
listenerX = dword[0x177f24]; listenerY = dword[0x177f28]; listenerFacing = word[0x177fa8]
maxRange  = dword[0x177fa4]     # global audio max distance (also per-voice radius caps via voice+9)
dx = voicePosX(+0x1b) - listenerX;  dy = voicePosY(+0x1f) - listenerY
dist2 = dx*dx + dy*dy
if dist2 >= maxRange*maxRange:  cull the voice (fcn.00063e96)   # silent beyond range
else:
   dist  = round(sqrt(dist2))                                   # FPU sqrt
   atten = (100 - dist*100/maxRange)                            # LINEAR falloff, 100% at 0 → 0% at maxRange
   volume(+0x07) = baseVol(voice+0x17) * atten / 100
   (w1   (+0x0b... pan) = unk1(voice+0x15) * atten / 100  similarly)
   # PAN: ang = listenerFacing * 2 * PI / 360;  balance = (cos·dx − sin·dy)/maxRange*128 + 64, clamp 0..127
   pan(+0x0b) = clamp(balance, 0, 127)
```
- **Falloff is linear** from the listener to maxRange (`0x177fa4`), with a per-sample radius cap (record+8,
  default 11000) feeding voice+9. Pan is true directional (uses listener facing 0x177fa8 + dx,dy).
- The 3D map type-3 voices have extra streaming logic (fcn.00063f27) and a "ducking when out of the active
  window" check (voice+0x19 == 100 gate, every 20th frame via counter 0x177faa).

### Remake wiring (MapNpc.Sound → NpcState → looped positional sample)
1. Parse the 13×4 table above into a `SoundSet[13] { SfxEntry[4] {SampleId, Unk1, Volume%, Loop, Radius} }`.
2. NpcState gets `ushort[4] ActiveSfx` (init 0xFFFF) + `byte Sound` (the set index from MapNpc V2 byte1).
3. Per NPC tick: ensure sfx0 running (looped, positional, vol from table); start/stop sfx1 by is-moving;
   for ChaseParty NPCs start/stop sfx3 by chase-active; fire sfx2 one-shot when a chase starts.
4. Each tick push the NPC's world position (tileX<<4,tileY<<4 on 2D; world XY on 3D) into the live samples.
5. Attenuate linearly: `vol = tableVol * (1 - dist/maxRange)`, cull past maxRange; per-sample radius cap;
   pan by direction relative to the listener (party) facing.

---

## Item 3 — Animated 3D meshes (dungeon objects) — CONFIRMED: there are NO meshes; objects are billboards

### Headline finding
**Albion's original 3D dungeon renderer has no polygonal-mesh path at all.** It is a column/span
ray-style raster (3d_disp.c). Every placed "object" in a dungeon cell — pillars, fountains, chests,
torches, etc. — is drawn as a **screen-aligned textured billboard sprite**, exactly like the NPC
billboards, by the single object pass `fcn.000bf4d4` → `fcn.000bed08`. There is **no** separate mesh
rasterizer; UAlbion's `IMesh`/`MeshInstance` path (and the `// TODO: Animated meshes` branch in
`MapObject.AdvanceFrame`) is a UAlbion-only embellishment with no 1:1 original counterpart. So "animating
a mesh object" in the original = **cycling the billboard's sprite frame** — the same mechanism the remake
already runs for `_sprite` via `AnimUtil.GetFrame`.

### The 3D render pipeline (CONFIRMED)
- Per-frame world render `fcn.000c10d0` runs ordered passes: `fcn.000be448` (sky/init), `fcn.000bf02c`,
  `fcn.000bf05c` (floor/ceiling spans), **`fcn.000bf4d4` (placed objects)**, `fcn.000bf9bc`,
  `fcn.000c0ccc`, `fcn.000c0f2c`, `fcn.000c0254` (walls, via `fcn.000bfe40`). All output is textured
  2D spans/billboards — no triangles.
- The labyrinth blob is carved at map-enter by `fcn.00019e46` into:
  | global | points to | record size |
  |---|---|---|
  | `0x14a4a2` | **ObjectGroup table** (cell contents 1..100) | **0x42 (66) bytes** = 2-byte hdr + **8 sub-objects × 8 bytes** |
  | `0x14a4aa` / `0x14a4b2` | **ObjectInfo table** (the per-object-type defs) | **0x10 (16) bytes**, 1-based: record = base + (index<<4) − 0x10 |
  | `0x14a4ae` | FloorAndCeiling table | 0xA bytes |
  | `0x14a4b8` | wall table | — |

### ObjectInfo (16 bytes) field offsets — CONFIRMED (matches remake `LabyrinthObject`)
| ofs | field | use in original |
|---|---|---|
| +0  | **Properties / Collision** (byte0 flags; bytes 1-3 collision dword) | bit-tests in `fcn.0001f07e` (collision bit 11+class), `fcn.000bed08` byte0 &2 (translucent), &0x80 (extra flag) |
| +4  | **Id** (u16) graphic/sprite asset id | the billboard sprite sheet drawn |
| **+6**  | **FrameCount** (byte) | **number of animation frames in the sprite** — read at `fcn.0001caba` 0x1cb1e/0x1cba2/0x1cba7 etc. as `byte[objInfo+6]` |
| +7  | Unk7 (byte) | unconfirmed |
| +8  | Width (u16) world half-extent | collision AABB / draw width (read at `fcn.000c0097`) |
| +0xA| Height (u16) | draw height (read at `fcn.000c00a1`) |
| +0xC| MapWidth (u16) | on-screen sprite width |
| +0xE| MapHeight (u16) | on-screen sprite height |

### How the per-frame advance works (CONFIRMED)
The animation frame is selected from `FrameCount (objInfo+6)` — there is no separate "frame rate" or
"frames table"; the sprite simply has N frames and the engine cycles them. The frame chooser is
`fcn.0001caba` (the 3D billboard frame selector, shared by NPCs and objects). Its three cases on the
entity's motion/animation flags (NpcState+0x19 bits, or static for objects):
```
objInfo = 0x14a4aa + (objNumber<<4) - 0x10
frameCount = byte[objInfo + 6]
if (flags & 0x10) ...  : use the "moving" frame set     (fcn.00022bc1(spriteId, frameCount))
elif (flags & 0x04)... : use the alt frame set          (fcn.00022bc1(spriteId, frameCount))
else (static object)   : frame = frameCount / 2;        # the "rest" frame is the MIDDLE one
                         if (frame >= frameCount) frame = 0
```
`fcn.00022bc1(spriteId, frameCount)` resolves the actual frame index within the loaded sprite. The
default (static object) branch picks frame = `FrameCount/2` as the idle pose, and otherwise the frame is
driven by the global animation phase (the same clock that animates wall textures, `fcn.000bfe40` reads
phase `0x13ffaa`/`0x196d0a`). **Animation cadence = the slow clock; cycling = modulo FrameCount.**
- The remake's `LabyrinthObjectFlags.Unk0` (Properties bit0) is the **"back-and-forth / bouncy"** flag
  the remake already passes as `_isBouncy` to `AnimUtil.GetFrame` — i.e. ping-pong vs wrap. This is the
  only per-object animation parameter beyond FrameCount.

### Remake guidance for `MapObject.AdvanceFrame` (the `_mesh != null` branch)
Because the original has no meshes, there is **nothing to port for a true animated mesh**. Two faithful options:
1. **Recommended (1:1):** render dungeon objects as billboards (the `ITexture` path) and animate them with
   the existing `_sprite` code — `FrameCount` from `LabyrinthObject.FrameCount (+6)`, ping-pong per
   `Properties.Unk0`. Then the `_mesh` branch is simply unreachable for real game data and the TODO can be
   closed as "N/A — Albion 3D objects are billboards".
2. **If keeping the UAlbion mesh embellishment:** drive `_mesh` animation the same way the sprite path does
   — advance `_frame` on `SlowClockEvent`, compute `AnimUtil.GetFrame(_frame, FrameCount, _isBouncy)`, and
   swap the mesh's texture/sub-mesh per frame (the mesh would need per-frame texture sets). There is no
   original "frame rate" field; use the slow-clock tick (same rate as billboards) and modulo `FrameCount`.

(No `// TODO: Animated meshes`-specific data exists in MAIN.EXE — the field that governs object animation
is the single byte `ObjectInfo+6 = FrameCount`, plus the `Properties` bit0 ping-pong flag.)



