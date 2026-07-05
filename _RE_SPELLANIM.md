# RE: Combat spell-cast animations (gap T1.3, _TODO_1TO1.md §9)

> Goal: how MAIN.EXE visualises spell casts in combat, precisely enough to implement 1:1.
> All radare2 against `albion_aaa`. Status: IN PROGRESS — incremental notes below.

## Known facts going in (from _RE_5A/_RE_5B/_RE_COMBAT)

- Cast executor (action kind 5) = `fcn.0004f6a2`: prints "X casts Y", plays vtable_2
  **sub 7** (StartAnim(Magic) + WaitAnimEnd = the CASTER's own Magic sheet-anim), then
  queues deferred ctx `0x13e9da` → cast core `fcn.0005fdf7` → dispatcher `0x5ecda`.
- `0x15db4c` dword[210]: _RE_5A says it is the **spell-name pointer table**
  (`fcn.00060b30` returns `0x15db4c[(school*30+num)*4]`), fed to formatter `fcn.00081f42`
  with format handle `0x15d994`. _RE_5B:363's "anim script builder" label is SUSPECT —
  to be re-verified here.
- vtable_2 (display vtable, 5 rows × 12 subs @ 0x13e280) **sub 10** =
  wrapper `0x51dd1` (party) / `0x52619` (monster) → `fcn.00099233(c, word[arg])` —
  labelled "spell-anim module; big-spell visual on a combatant", semantics OPEN.
- GoddessWrath (39) damage is applied "inside queued animation callback `0x99778`" —
  i.e. the 0x99xxx region is the spell-visual module.
- Anim-slot pool: 1000×58 B @ 0x168a84, alloc `fcn.00053871`, draw `fcn.000542c9`,
  projection f=148, effects gfx live in file 0x28 (COMBATGFX): #43 soul, #46 hit splash,
  #47 party cast-burst, #48 move marker, #49..85 projectiles.
- Timing: logic ticks 20 Hz; sheet anims 6.67 fps; blocking loops call engine frame
  `fcn.00075e71`; hit-splash style effects step 1 frame per 2 engine frames.
- Party members are not drawn; party aim point = TileToWorld(col, row=-1) ⇒ screen
  ≈ (24 + 62.4·col, 108).

## Findings

### F1. `0x15db4c` is NOT an anim table — it is the spell-NAME string-pointer table (CONFIRMED)

- `fcn.00060b30(school, num)` @0x60b6a..0x60b7e: `if (IsSpellImplemented) return
  dword[0x15db4c + (school*30 + num)*4]` — a per-global-spell POINTER, dword[210]
  (7 schools × 30).
- `fcn.00081f42` is a **39-byte varargs trampoline with 93 call sites** that marshals
  (fmt, args...) into `fcn.00081f19(dest, fmt, va_list)` — i.e. `sprintf`. NOT an anim
  script builder. The "X casts Y" print in the cast executor `fcn.0004f6a2` (calls at
  0x4f7d9/0x4f8e0) passes the 0x15db4c pointer as a %s argument.
- Only two code refs to 0x15db4c exist in the whole binary (`/x 4cdb1500`): the reader
  0x60b7e and `0x55f46` (combat spell-menu UI: copies entry [0] to 0x176d64 for a widget).
  The table is filled at spell-list build time with SYSTEXTS name pointers.
- **Correction to _RE_5B.md:363**: "anim script builder 0x81f42" → sprintf; the table is
  the cast-message name table. Cast animations come from the per-spell effect functions
  (below), not from any per-spell data table.

### F2. Fireball (91, fn 0xa2f17) — the archetypal damage-spell presentation (CONFIRMED)

Entry @0xa2f17: `fcn.0005fb21(spellCtx, cont=0xa2f4b)` = ForEachTargetTile — the whole
animation below runs **once per recipient**, sequentially (area spells = N full anims).

Per-target continuation 0xa2f4b (target c = eax, M = ecx):

1. Load COMBATGFX **#20 (0x14)** → gfxBall and **#21 (0x15)** → gfxFly
   (`fcn.000233b8(file 0x28, id)`), alloc two anim slots (fcn.00053871).
2. `GetAimPoint(dword[0x1775ae])` = **CASTER** world point (party ⇒ the virtual row -1
   point below the horizon); `GetAimPoint(target)` = destination.
3. `fcn.00097c54(casterPt, targetPt, &vel, speed=500)` → velocity x100/tick + flightTicks
   (speed 500 = **5 world units per logic tick**; cf. arrows 7-15).
4. Slot A: pos = caster point, render kind **8** (max translucency tier), anchor (50,50),
   scale **40%**, gfx #21 (frame count cached → auto-cycle at 6.67 fps).
   Slot B: same pos/kind/anchor, scale **400%**, gfx #20.
5. `PlaySample(233)` (fcn.00062a41, vol 100/100).
6. **Charge-up loop** @0xa316b (blocking, engine frames fcn.00075e71): per frame
   A.scale += 2·ticksThisFrame (clamp ≤100), B.scale −= 10·ticksThisFrame (clamp ≥100);
   exits when A==100 && B==100 ⇒ exactly **30 logic ticks = 1.5 s** forming at the caster.
7. Hide slot B (`fcn.00053b21`), give slot A the velocity (+0x14/18/1C) — the auto-move
   flag makes it **fly caster → target**; `PlaySample(234)`;
   `fcn.00097e63(flightTicks)` = blocking wait N logic ticks.
8. **Magic-resist gate `fcn.000601a6(target, M, 0, 0xFFFF)` runs AFTER the flight** —
   margin==0 (resisted) ⇒ skip impact entirely (no explosion, no damage), clean up.
9. Hit: `fcn.00053b62(slotB)` (unhide), `PlaySample(235)`, read slot A's arrival (x,y,z),
   free both slots, `fcn.000982dc(x, y, z, 75; 100, 100, 40, 2)` = **impact effect**
   (decoded below — gfx COMBATGFX #40, the hit-splash family), damage
   `max(1, margin*22/100)` via `fcn.0004dec9(target, dmg)` (Hit-anim + splash pipeline),
   then `fcn.00097e63(0x20)` = 32-tick (1.6 s) post-impact wait.
10. Free gfx handles (fcn.0008bc7d).

Sound ids: **233 charge / 234 flight / 235 impact**. Everything is blocking/synchronous —
the battle presentation pauses exactly like WalkPath.

### F3. Where the visuals actually live

- Caster-side: vtable_2 sub 7 (`0x522ba`) = StartAnim(Magic sheet-anim) + WaitAnimEnd —
  monsters only; party casters show nothing at this stage (row 0 sub 7 is NULL).
- Effect-side: each spell's effect fn (K-table in _RE_COMBAT.md "Magic system") builds its
  own visual inline using a small helper module at 0x97xxx-0x99xxx (commagic), on top of
  the generic anim-slot pool (alloc fcn.00053871 via wrappers, behaviours via
  fcn.00053ba3, blocking engine-frame loops).
- `fcn.00099233(c, &param)` (vtable_2 sub 10 wrapper target) = generic "magic burst on a
  combatant": loads COMBATGFX #30 (0x1e) via fcn.000233b8(file 0x28, 30), spawns it at
  the combatant's aim point (GetAimPoint fcn.00053208; z biased -1 for monsters / +1 for
  party so it sorts in front), scale 200%, render kind 2, behaviour cb 0x995dd with
  params {2000,250,320,210,1000,500,2,2}; then 5 particle emitters (fcn.0009811f, gfx
  handle dword[0x168a6c], 15 particles each) in a plus pattern (±10 world units around
  the aim point) and a 64-tick particle wait fcn.00097e63(0x40). Param word only gates
  execution (non-zero) in this fn.
