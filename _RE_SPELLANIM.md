# RE: Combat spell-cast animations

> How MAIN.EXE visualises spell casts in combat, RE'd for a 1:1 remake implementation.
> All radare2 against `albion_aaa` (2026-07-05). CONFIRMED unless marked INFERRED.
> Conventions: anim-slot pool/projection/timing per `_RE_COMBAT.md` "battle view" section;
> Watcom regcall (args eax, edx, ebx, ecx, then stack).

## 0. Executive summary

- **There is no per-spell animation data table and no "anim script" format.** Every spell's
  cast visual is HARD-CODED in its effect function (the K-table fns of `_RE_COMBAT.md`
  §"Magic system"), built from a small helper vocabulary over the generic 58-byte anim-slot
  pool. `0x15db4c` (the suspected "anim table") is the spell-NAME pointer table and
  `fcn.00081f42` is `sprintf` — the old _RE_5B.md:363 label "anim script builder" is wrong.
- Caster-side: monsters play their **Magic sheet-anim** (vtable_2 sub 7). Party casters have
  no sprite; their casts visually originate at the party's virtual aim point
  (`TileToWorld(col,-1)` ⇒ screen ≈ (24+62·col, 108)).
- Effect-side: each spell fn spawns effect sprites from **COMBATGFX (file 0x28)** at world
  positions, moves/scales them with blocking engine-frame loops, plays its own samples, and
  only then applies damage/conditions. **The battle is fully paused** (same synchronous
  coroutine style as WalkPath): every wait is a `fcn.00075e71` engine-frame loop.
- Area spells first COLLECT all valid recipients, play one shared intro, then run the
  per-victim hit visual **sequentially per victim** (single-target spells are the same
  code with one victim). Fireball-style spells run the entire projectile flight once per
  recipient (their entry iterates `ForEachTargetTile`).
- Resisted targets: the magic-resist gate `fcn.000601a6` itself prints "resists" (msg 731 via
  `fcn.0004de64` @0x602ce) and plays the **generic fizzle-burst** (sub 10 →
  `fcn.00099233`: COMBATGFX **#30** at 200% + spark emitters) on the resisting combatant,
  then the spell fn skips its own hit visual and damage for that victim.
- Heals and pure buffs show **no battle-view animation at all** — sound only (heals:
  sample 38 + portrait-area callback via `fcn.0005f9a3`; Hurry/Berserk/MagicShield/
  PersonalProtection: nothing visual in the cast path).

## 1. Corrections to earlier docs

- `_RE_5B.md:363`: `0x15db4c` = dword[210] **spell-name pointers** (7 schools × 30),
  read by `fcn.00060b30(school,num)` @0x60b7e (`IsSpellImplemented` first). `fcn.00081f42`
  = 39-byte varargs trampoline into `fcn.00081f19(dest,fmt,va)` with 93 call sites =
  `sprintf`. The cast executor's calls (0x4f7d9/0x4f8e0) format "X casts Y".
  Only other code ref: 0x55f46 (spell-menu UI widget reads entry [0]).
- `_RE_COMBAT.md` §vtable_2 "sub 10 = big-spell visual, semantics open" → resolved: it is
  the **resist fizzle-burst**, invoked from inside the gate `fcn.000601a6`, not from spell
  code (no spell fn calls `fcn.00051ab5` directly).

## 2. Helper vocabulary (the "commagic" module, 0x974xx-0x994xx)

| Fn | Signature (regcall) | Meaning |
|---|---|---|
| `fcn.000233b8(file, id)` | returns gfx handle | LoadAsset; spells always use file 0x28 = COMBATGFX |
| `fcn.0009796b(x,y,z, ttlTicks; scale%, gfxHandle, kindFlag)` | returns slot | **SpawnEffectSlot**: anchor (50,50), w%=h%=scale, frame 0, frame count cached (+0x2A). kindFlag 1→render kind 0 (plain), 2→kind 4, 4→kind 7, 8→kind 8 (translucency tiers). ttl 0 = no expiry. Callers then `or byte[slot+2],8` (flags2 bit3) to enable auto frame-cycling at the 6.67 fps projectile rate |
| `fcn.00097c54(fromXYZ, toXYZ, &velXYZ, speed)` | returns flightTicks | ComputeFlight: vel = delta·speed/dist (x100/logic tick), ticks = max(1,dist/speed). Spells use speed **500** (5 wu/tick) |
| `fcn.00097e63(n)` | — | **WaitTicks**: blocking engine-frame loop consuming n logic ticks (20 Hz) |
| `fcn.00097bdf(M, lo, hi)` | int | **MasteryLerp** = lo + (hi−lo)·M/100 (works descending too) |
| `fcn.00097d9a(lo, hi)` | int | RandRange inclusive |
| `fcn.00097de5(lo, step, ?, hi)` | int | RandRange with granularity (particle velocities) |
| `fcn.000982dc(x,y,z, count; velRange, ttlBase, scale%, gfxClass)` | — | **SpawnParticleBurst**: count slots, kind 8, anchor (50,50), scale = rand[scale,1.5·scale], TTL = rand[ttl,1.5·ttl] ticks, vel xz = ±velRange, y upward-biased; random start frame; per-slot behaviour cb `0x54da2` (gravity/expiry, param rand%5+10, 60). gfxClass: 1 → particle gfx idx 0-2, 2 → idx 6-7, else 0-7 |
| particle gfx table `0x168a64[8]` | — | pre-loaded at combat init `fcn.00053554` @0x53684: **COMBATGFX #2,#3,#4,#6,#7,#9,#10,#11** (fcn.00023605 batch-load, id list `0x13e40c`). Companion trio `0x176d18[3]` = **#1,#5,#8** (id list `0x13e406`) |
| `fcn.0009811f(x,y,z, gfxHandle; count)` | — | directed spark emitter (variant of burst; used by fizzle/lightning with gfx from `0x168a64`) |
| `fcn.00053aa5(slot)` | clone | **CloneSlot** (copies an anim slot; basis of overlay/echo effects) |
| `fcn.00097eaf(slot, count≤16, growPct, ttl)` | — | **EchoRipple**: spawns `count` clones at progressively larger scale (+scale·growPct/100 per step), z+1 apart, kind 8 translucent, given TTL — the concentric "pulse rings" |
| `fcn.00053b21` / `fcn.00053b62` | — | HideSlot (flags bit2 set) / ShowSlot |
| `fcn.00053ba3(slot, slot2, cb)` | behaviour | attach per-logic-tick behaviour (params at +0xC..) |
| behaviour `0x995dd` | — | "swell" envelope on a slot (params: e.g. Fireball-fizzle {2000,250,320,210,1000,500,2,2}, frost {2500,2500,100,100,500,500,2,2}) INFERRED grow/shrink keyframes |
| behaviour `0x54eeb` | — | known sine oscillator (bolt wobble etc.) |
| `fcn.000987f3` | slot | spawns an invisible "driver" object whose behaviour (`0x99d45`) sweeps a wind/rain field (FrostAvalanche, Thunderstorm, FireHail, KamulosGaze) |
| `fcn.00098520(x,y,z,…)` | — | spawn **COMBATGFX #37** strike flash (Thunderbolt impact) |
| `fcn.00098665(…)` | — | spawn **COMBATGFX #54** falling drop (FireRain) |
| `fcn.00098d5b(victimSlot, x, y)` | slot | spawn a small effect pinned to the victim's sprite (frost flakes, spores, fungus) |
| `fcn.000988c6(…)` | — | multi-slot ray/beam cluster between two points (GoddessWrath, KamulosGaze) INFERRED detail |
| `fcn.0009900f(…)` | — | orbiting swirl around a combatant (Irritation) INFERRED detail |
| `fcn.0009b827(M, destX, destY, destZ)` | — | **School-intro orb**: gfx **#18**, spawned at CASTER aim point at 100%, sample **201**, flies to dest at speed 500 while `EchoRipple`-scaling to MasteryLerp(M,20,80)%, blocking wait for the flight. Used by every Dji-Kas spell (callers 0x9ba4a, 0x9c227, 0x9c8a5, 0x9d030, 0x9d761, 0x9db54, 0x9e333, 0x9eb36, 0x9f1e6, 0x9fe01) |
| `fcn.00099233(c, &param)` | — | **Resist fizzle** (vtable_2 sub 10 target): gfx **#30** at target aim point (z∓1 to sort in front), 200%, kind 2, swell behaviour `0x995dd`, then 5× `fcn.0009811f` emitters (gfx `0x168a64[2]`=#4, 15 sparks each) in a plus-pattern ±10 wu, wait 64 ticks |
| `fcn.00062a41(id, vol, pan, ?, flag)` | — | PlaySample |
| `fcn.0004dec9(victim, dmg)` | — | ApplyDamage — funnels into the standard hit presentation (calls sub 8 Hit / sub 9 Die via `fcn.00051ab5` at 0x4df48/0x4e0db: Hit sheet-anim + splash #46, or death anim) |

Magic-resist gate `fcn.000601a6(victim, M, inclMask, exclMask)`: on RESIST prints msg 731
and plays the fizzle (sub 10 @0x602ed) itself, returns 0 ⇒ caller skips its hit visual.

## 3. The cast pipeline, presentation order (CONFIRMED)

1. Cast executor `fcn.0004f6a2` (action kind 5): print "X casts Y"; **monster caster** plays
   Magic sheet-anim + WaitAnimEnd (vtable_2 sub 7 = `0x522ba`); party caster: nothing here.
2. Deferred ctx `0x13e9da` → cast core `fcn.0005fdf7` (SP/charges) → dispatcher `0x5ecda` →
   per-spell effect fn.
3. Effect fn entry: `fcn.0005fb21(ctx, cont)` ForEachTargetTile:
   - **Projectile-per-target spells** (Fireball 91, SmallFireball 65, zombie breezes
     151-154, LightningStrike 92, ThornSnare 1, FrostSplinter 6, Blinding-Spark 10,
     SleepSpores 13, Fungification 20, Banish 62-64, Panic 68-70, StealLife 101,
     StealMagic 102, KamulosGaze 104, Irritation 40): cont = the full per-target animation
     (repeats per recipient for the area variants of these).
   - **Collector spells** (FrostCrystal 7, FrostAvalanche 8, BlindingRay 11,
     BlindingStorm 12, FireRain 93, Thunderbolt 94, FireHail 95, Thunderstorm 96):
     cont = `0x97928` **CollectTarget** (append to `0x196fdc`, count `0x19703c`); the body
     then plays ONE shared intro and iterates victims sequentially.
4. Per victim, always: gate `fcn.000601a6` → resisted = fizzle #30 only; else the spell's
   hit visual → `fcn.0004dec9` damage (standard Hit anim + splash #46) or SetCondition.
5. Everything blocking; battle input/turns resume when the effect fn returns.

## 4. Per-spell visuals (fn addresses = K-table; gfx ids = COMBATGFX file 0x28)

School intro: ALL Dji-Kas (school 0) spells open with the **orb #18** flight (§2,
`fcn.0009b827`) from the caster to the primary target (or to a sky/ground point), then a
particle burst `fcn.000982dc` with class-specific particles. Sound 201.

| Spell | fn | Visual sequence (samples in parens) |
|---|---|---|
| 1 ThornSnare | 0x9b95e | orb→target; burst cls 3; gate; **#34 + #35** vine slots at target (203); echo; wait ~12 |
| 4 Hurry | 0x9bd5f | **nothing** (condition only) |
| 5 ViewOfLife | 0x9bddf | **#56** overlay on target (208), EchoRipple, wait 4 (then LP display UI) |
| 6 FrostSplinter | 0x9c109 | orb; burst cls 1; gate; (209) snow flakes **#26** rain over sprite rect (`fcn.00053164` bounds, `fcn.00098d5b` pinned on monsters); (210) **#33** frost overlay kind 7 + swell; **ice-blue LUT clone** of the victim sprite (see §5); flake fall-out ±150 vel; wait 64; damage |
| 7 FrostCrystal | 0x9c7ba | collector; same as 8 with smaller params |
| 8 FrostAvalanche | 0x9cf43 | collect; orb→(0,0,160) deep-background; 100-particle sky burst cls 1 at (0,5,160); driver `987f3` (rolling cloud, behaviour 0x99d45, param MasteryLerp(M,25,75)); wait 16+32; per victim: gate(inclMask 0x80) → (209) flake curtain #26 (scale MasteryLerp(M,20,30)%, up to 400 slots array `0x197690`) → (210) #33 overlay (scale 10, swell 0x995dd {2500,2500,100,100,500,500,2,2}) → ice-LUT clone flash → flakes get fall velocities (vx,vz ±150, vy 150..250) → wait → damage max(1,margin·27/100) |
| 9/35/67 heals, 17/18/19 cures, 31/33 regen, 21 Light, 32 MapView, 41 Recuperation | 0x64xxx | **no combat visual**; heal sample 38, `fcn.0005f9a3` per-targeted-party-member callback (LP text/portrait), conditions cleared silently |
| 10 BlindingSpark | 0x9d6ca | orb; burst cls 1; gate FIRST; **#40** flash at victim (213); two big bursts (rand 1000-2000 param); (214); wait 8. No damage |
| 11 BlindingRay | 0x9dab3 | collector version of 10 (same #40 + bursts per victim) |
| 12 BlindingStorm | 0x9e292 | collector version of 10 |
| 13 SleepSpores | 0x9ea71 | orb; burst cls 1; gate; wait 16; spore cloud slots (particle gfx) drift onto victim (215), pinned spores `98d5b`, EchoRipple; waits 4/48/24; Asleep |
| 14 ThornTrap | 0x9f123 | orb→tile; burst cls 3; **#29** trap marker persists on tile (216); wait 50. TRIGGER (0x9f39f..): **#44** thorn burst + `98e51`×2 + victim clone flash (218); gate; damage; (219); waits 10/16 |
| 15 RemoveTrapDK | 0x9fa2d | (221) two slots around tile ±16 wu using the **trap's own gfx handle** (lift-and-fade, behaviours, RandRange(25,75)); (206); wait 48 |
| 20 Fungification | 0x9fd5c | orb; burst cls 2; gate; mushrooms: gfx id **rand 12..15** ×2 loads, scale rand(200,300), pinned via 98d5b; (223) wait 24; damage; spores fly out ±150; more mushrooms scale 5..80; wait 64; (223) wait 8; clone flash; wait 40; (225) wait 48; second damage tick |
| 34 Teleporter / 36 QuickWithdrawal / 37 Levitation | — | no cast visual (map/flee mechanics) |
| 39 GoddessWrath | 0xa1134 | **#57 goddess figure** rises at a point (SpawnEffectSlot + EchoRipple grow-in) (266); behaviour; per victim (list picked per _RE_5B §2): gate @0xa1340 → EchoRipple, wait 12, (267), ray cluster `988c6` goddess→victim, hide/show flicker, jitter rand(-25,25)³, flight; damage inside behaviour cb `0x99778` |
| 40 Irritation | 0xa1799 | gate; (215) swirl `fcn.0009900f` around victim (rand pos 150-300), behaviours; wait |
| 61 Berserk | 0xa1a69 | **nothing** |
| 62/63/64 Banish demons | 0xa1aec | **#40** flash on victim; gate; rising slots (rand vel −300..300, scale 80-160), behaviours rand(20,40); wait 16; second slot (scale rand(300,400)); dissolution rand(100,150) |
| 65 SmallFireball | 0xa2055 | = Fireball (same #20/#21, 233/234/235, hide/show, burst cls 2, damage, wait 32) minus the charge-up loop |
| 66 MagicShield / 103 PersonalProtection | — | **nothing** |
| 68/69/70 Boasting/Shock/Panic | 0xa2612 | gate; **#28** fear icon over victim (230), EchoRipple + behaviour rise; (231); wait 8 |
| 91 Fireball | 0xa2f17 | per target: **charge-up at caster** — #21 at 40% grows +2/tick AND #20 at 400% shrinks −10/tick, both to 100% (= 30 ticks, 1.5 s), kind 8, (233) → #20 hidden, #21 flies caster→target at 5 wu/tick (234) → gate → (235) + **impact burst `982dc`(count 75, vel 100, ttl 100, scale 40, cls 2 = fire particles #10/#11)** + damage + wait 32. Resist = fizzle only, no explosion |
| 92 LightningStrike | 0xa33d2 | per target: **#36 bolt** ×MasteryLerp(M,3,5) slots at 75%, spread across sprite width, y staggered by ⅓ sprite height, sine-wobble `0x54eeb`; flicker hide/show (2-tick), wait 24+3; (234) sparks `9811f` ×5 ±5 wu; EchoRipple; (235); damage (UNGATED, _RE_5B) |
| 93 FireRain | 0xa43c6 | collect; (260); MasteryLerp×3; flight; wait 2; **#54 drops** (`98665`) spawn at rand(−3000..3000, −1500..3000) sky points falling per victim; behaviour; gate; damage; wait 64 per victim |
| 94 Thunderbolt | 0xa477a | collect; loads **#55 cloud, #36 bolt, #33 flash**; victim clone flash; (239); bolt flight `97c54` down from cloud; **#37 strike** (`98520`) + sparks at rand offsets; gate; behaviours; (235); damage; wait 12 |
| 95 FireHail | 0xa50ef | collect; (261); loads **#43 comet, #21 trail, #36**; driver; comets spawn across sky rand(±19200, −16000..16000); per victim: rand ±150 impacts; gate; (235); damage; wait 12; (241) |
| 96 Thunderstorm | 0xa5aad | collect; MasteryLerp×2; driver; (239); **#55 clouds** ×2 (rand 25-75 / 15-75 params) gather; per victim: **#36 bolt** + clone flash + **#33 flash**; gate; (235); damage; wait 2; rand(5,40); wait 45 |
| 97/98 LightningTrap/Big | 0xa6567 | (242) **#29 marker** on tile(s) (243), behaviours, waits 24/50. TRIGGER: #36+#33 (265), sparks ±200 gran 10, gate, damage, wait 24 |
| 99/100 LightningMine/Big | 0xa7004 | **#29 marker** (242), behaviours, waits. TRIGGER: gate; EchoRipple; flight ±200; slot rand(25,75); (206); damage; wait 32 |
| 101 StealLife | 0xa77b3 | gate first; **#32** essence gfx; (206); victim clone; particles stream victim→caster (flight `97c54`, vel rand ±1500, y −250..250) ×2 waves; behaviours rand(10,20); wait 4; damage (caster heals) |
| 102 StealMagic | 0xa80e3 | same skeleton with **#31** (206) |
| 104 KamulosGaze | 0xa8b12 | **#24** gaze; MasteryLerp; victim clone; 3× `97de5` rand vel ±4500; flight; (263); wait 40; gate; (264); ray cluster `988c6` + driver `987f3`; shake rand(−30,30); rand(96,180); wait **180 ticks (9 s)**; instant-kill `fcn.0004e247` |
| 105 RemoveTrapKK | 0xa92ae | = RemoveTrapDK skeleton (trap gfx lift ±16 wu, rand(25,75), wait 48) without samples 221/206? (INFERRED same) |
| 151-154 zombie breezes | 0xa95a4.. | simple projectile: **#68/#66/#67/#65** (Panic/Poison/Irritation/Ill) flies caster→victim at speed 500, wait flight, gate → SetCondition. No impact gfx |

Notes:
- All effect sprites use anchor (50,50) and the standard projection (f=148); kind 8/7/4 =
  translucency tiers via the global LUT `0x176d14`.
- "clone flash" = `fcn.00053aa5` copy of the **victim's own sprite**, render kind 3 with a
  freshly-built 256-byte LUT (`fcn.000820ca`/`fcn.00080e7d(ptr,0,0x100,0xa0; 0xa0,0xff,50)`
  = remap toward a colour ramp — ice-blue for frost, white for lightning), z−1 (in front),
  flags2 bit4, synced to the parent by behaviour `0x9a7b4` (param 7 = TTL/fade); the parent
  sprite stays visible underneath. FrostAvalanche @0x9d3e6, Thunderbolt @0x4aba(0xa4aba),
  Thunderstorm @0xa619b, StealLife @0xa7a6c, KamulosGaze @0xa8c5c, FrostSplinter @0x9c540.
- Party members have no sprite, so victim-side visuals for party targets land at the
  virtual aim point; monster-pinned effects (`98d5b`, clone flash) are monster-only
  (the code branches on kind==2; party branch spawns free-floating slots instead,
  e.g. FrostAvalanche @0x9d214 rolls rand(1,12)>3 for flake placement).

## 5. Sound ids observed (fcn.00062a41)

201 school-orb launch; 203 thorns; 206 drain/mine hit/remove-trap end; 208 ViewOfLife;
209/210 frost flakes/overlay; 213/214 blinding flash; 215 spores + irritation; 216/218/219
thorn trap place/trigger/end; 221 remove-trap start; 223/225 fungification stages;
230/231 panic; 233/234/235 fire charge/fly/impact (also lightning strike reuses 234/235,
thunder impacts 235); 239 thunder clouds; 241 fire-hail end; 242/243 lightning trap place;
260 fire-rain start; 261 fire-hail start; 263/264 KamulosGaze; 265 lightning-trap trigger;
266/267 GoddessWrath; 38 heals (vol 80). There is **no generic cast sample** — the remake's
single cast-sample placeholder diverges.

## 6. Timing summary

- Logic ticks 20 Hz; effect frame-cycling at 6.67 fps (every 3rd tick); waits are blocking.
- Typical durations: Fireball ≈ 30 ticks charge + flight (distance/5 wu per tick;
  caster→row-0 monster ≈ 240 wu ⇒ ~48 ticks) + 32 ticks post-impact ⇒ **~5.5 s per target**.
  LightningStrike ≈ 30 ticks per target. KamulosGaze ≈ 9 s. Area spells sum per victim.
- Sequence per round: caster Magic anim (monsters, ~4 frames @6.67 fps) → spell fn visuals →
  Hit anim/splash via ApplyDamage → next queued action.

## 7. Remake implementation recipe (data → BattleView)

1. **No new data files needed** — everything is code + COMBATGFX. Add a
   `CombatSpellFx` table in code: per spell id → {intro, projectile gfx, impact gfx,
   overlay gfx, samples, waits} mirroring §4's table. SPELLDAT contributes nothing visual.
2. **Primitive layer** (BattleView): effect sprite = existing anim-slot analogue
   {worldPos x100, vel/tick, ttl, anchor 50/50, scale%, gfx+frame cycling 6.67 fps,
   translucency tier, optional behaviours: swell, sine, gravity-particle, echo-ripple,
   parent-sync}. Reuse the projection already in BattleView; party aim point =
   TileToWorld(col, −1) as for melee/arrows.
3. **Sequencer**: a blocking coroutine per cast (like the WalkPath lerp), one full visual
   per recipient for projectile spells; collector spells: shared intro then per-victim hits.
   Gate first for condition spells (10-13, 40, 68-70, 101-104), gate after flight for
   projectiles, gate mid-sequence for frost (after flakes, before overlay).
4. **Resist feedback**: on gate failure show COMBATGFX #30 at 200% + sparks on the victim,
   suppress the spell's own impact/damage visuals (message 731 already exists in remake?).
5. **Monster caster**: play CombatAnimationId.Magic before the effect fn (already have the
   anim machinery); party caster: skip straight to the effect.
6. **Clone-flash**: tint a duplicate of the monster sprite (blue for frost, white for
   lightning/steal/gaze) for ~0.5 s in front of the original — palette-remap or shader tint.
7. Wire the per-spell samples of §5 instead of the single generic cast sample.
8. Traps/mines: persist gfx #29 marker slot on the tile until trigger; trigger plays the
   trap's strike visual (#44 thorns / #36+#33 lightning).

## 8. Open items (minor)

- Exact keyframe semantics of behaviours `0x995dd` (swell), `0x99d45` (cloud/rain driver),
  `0x9a7b4` (clone-sync fade), `0x54da2` (particle gravity) — parameter sets recorded above;
  eyeball-match is sufficient for 1:1 feel.
- `fcn.000988c6` (ray cluster) and `fcn.0009900f` (irritation swirl) internals sketched
  only (INFERRED labels).
- Sample-id → WAVLIB mapping should be confirmed against the remake's sample enum.
- The trio table `0x176d18` (COMBATGFX #1,#5,#8) users: 0x5549a/0x53821 (combat UI/marker
  paths, not spell casts).
