# UAlbion — Reverse-Engineering Index

> One-stop map: each RE'd game mechanic → the authoritative doc/section and the MAIN.EXE
> function(s) that encode it. Use this to find "how does X actually work" fast, and to
> avoid re-deriving something already decoded. Docs: `_RE_COMBAT.md` (RC), `_RE_NOTES.md`
> (RN), `_RE_5A.md`..`_RE_5D.md`, `_RE_6.md`.

## Skill / roll primitives
- **PercentRoll** `fcn.00035b15` — success iff `rand()%range <= value` (note `<=`; auto-fail
  ≤0). RC "Placeholder formulas §skill".
- **RollSkill** `fcn.00035bdc` = PercentRoll(GetEffectiveSkill, 100).
- **GetEffectiveSkill** `fcn.00035fd5` — sheet skill at `+0x7A+idx*8` (0 CloseRange / 1
  LongRange / 2 CriticalHit / 3 Lockpicking) + equipment, halved for weapon skills when Blind.
- **Attribute getter** `fcn.00035c77` — `sheet+0x2A+stat*8`; +6 word = pre-exhaustion backup.
- Watcom RNG: `seed = seed*0x41C64E6D + 0x3039; (seed>>16)&0x7FFF`.

## Combat strike pipeline (RC §1, RE 5A)
- Melee callback `fcn.0004eac1`, ranged `fcn.0004f057`. **To-hit** = RollSkill(weapon skill
  vs 100); NO defender dodge (the defender check is dead code). **Crit** = RollSkill(CriticalHit)
  → INSTANT KILL (damage = target current LP), blocked by crit-immunity flag `sheet+0x0E & 0x80`.
  **Damage** `fcn.0004ee3b` = max(0, vary(rawAtk) − vary(rawDef)), vary = `(rand()%51+50)*v/100`;
  delta 0 = absorbed (≠ miss). Strikes per action = `sheet+0x11` (×2 Hurry).
- **Melee reach** (RE 5A) `fcn.0004d512/0004d55e` — 8 Chebyshev-adjacent tiles only; no
  adjacent enemy → convert to a greedy approach move `fcn.00050887`. Ranged = whole grid, no LoS.
- **Equipment break** `fcn.0004f920` — PercentRoll(item breakRate, 1000) per connecting swing
  on attacker weapon + defender chest/head; sets Broken flag (NO item morph — `fcn.000665ce` is
  AppendSlotToLootList). RC "RE batch 4 §1".
- **Move** range = clamp(Speed/30,1,3) Chebyshev, destination-only occupancy (`fcn.0004d85b`);
  no pathfinding — greedy single-pass stepper, partial move on block. RE 5A §1.
- **Ammo** (RE 5A §2) `fcn.000513bf` usable / `fcn.0004f3e2` consume — one round per strike
  before the to-hit roll (misses burn it), backpack stack first.
- **Initiative** = descending Speed. **Action vtable** 0x13e196 (kinds 0–7; 5=CastSpell not
  Summon). Cancel masks vtable_1 0x13e194 (Irritated blocks casting).

## Combat status / morale / flee (RE 5A §4–5)
- Status gate `fcn.0004bf77`: Asleep skips; Panicking → forced flee to own back edge;
  Insane → 50/50 (rand%8≥4) move-vs-attack, target uniform over BOTH sides.
- **Morale** `sheet+0x0F`, check `fcn.00051506`: flee when `(deadMonsterPct+ownLostLpPct)/2 ≥
  Morale`. Behaviour-table 0x13e1f0 indexed by MONCHAR `sheet+0x0C` (variants: flee-on-first-
  death / outnumbered+LP% / caster-stay-back). Class bit 0x80 never flees.
- **Retreat** `fcn.0004f5d3` → RemoveFromBattle `fcn.0004b49e` (leaves the round it fires;
  fled monsters still credit XP). Outcomes `fcn.0004b256`/0x15f112: 1 victory / 2 party-fled /
  3 defeat.
- **Combat-end condition clear** `fcn.000650ad`: Irritated/Asleep/Panicking/Fleeing/Paralysed.

## Spells (RC §3.x, "RE batch 4", RE 5B)
- Magnitude = `max(1, M*K/100)`, M = `max(1,(mastery+50)/100)`, mastery 0..10000 at
  `sheet+0x140+school*60+(n-1)*2`. Cast core `fcn.0005fdf7`.
- **Success gate** `fcn.000601a6` — returns **margin = M − MagicResist** (incl/excl creature
  masks vs `sheet+0x0E`; demons mask 0x44; deterministic, no roll). **Gated damage scales on the
  MARGIN, not raw M** (frost line, fire/lightning except LightningStrike). **LightningStrike (92)
  is the only UNGATED damage spell** (raw M*33/100). RE 5B §5a.
- **SPELLDAT** = 5 bytes {env, cost, levelReq, targets, dead}. Target areas (RE 5B §1):
  RowOfMonsters (0x10) = whole 6-tile grid row; AllMonsters (0x20) = every living enemy, one
  gate each; DeadParty (0x04) = whole LIVING party; Big traps (0x80, `fcn.0x5f60b`) = whole row.
- **GoddessWrath** picker `fcn.0005f7ec` — kills max(1,living*M/100), uniform without
  replacement (rejection re-roll), grid-order, each gated. **Banish 62/63/64** = demon instant
  kill (mask 0x44, M>resist). **KamulosGaze** = instant kill, class 0x80 immune.
- **Item casts** = flat M=50 (`fcn.0005fdf7 @0x5fe34`); charge flags `item+0x1B` (0x04 single-use,
  0x10 vanish-at-0). RE 5B §3.
- **Shields** (MagicShield/PersonalProtection) write `ActiveSpells` types 1+2, duration
  `max(1,M*10/100)` GAME HOURS, pct=M, no refresh; type 1 multiplies defense, type 2 boosts
  MagicResist. **Light** (RE 5C) merges {hours=M,pct=M} into the ambient entry (ACCUMULATES on
  recast). Hourly decay `fcn.000605ed/00060670`. RE 5B §5b, RE 5C §1.
- Schools 4 & 6 are DEAD data (NULL handler tables); Levitation (37) DEAD (NULL + env 0);
  Summon does not exist. RE 5B §4, RE 5A §3.

## Progression / rest / fatigue (RC "RE batch 4" §2–3, RE 5C §4)
- **XP to reach level N+1** = `max(1, ⌊1.25N²⌋+N−14) × classMul {25,35,30,25,25,20,40,0,25,35}`,
  cap 50. Table built at 0x159758; LevelUpCheck `fcn.00037aaa`; ApplyLevelUp `fcn.00037c22`
  (PRTCHAR +0xE2/E4/E6/EA).
- **Rest** executor 0x68b05 / RestoreSlot `fcn.00068d2f`: one-shot 50% maxLP + Stamina/15,
  50% maxSP + MagicTalent/15, **2 rations/member**, cures Exhausted, gate hoursAwake≥3.
  Recovery applies BEFORE the clock advance. Gating (city Wait / dungeon 8h / wilderness till
  dawn / interior none) + "too dangerous" (0x15cc5a) + "nobody tired" live in the map-menu popup
  builder, not the executor.
- **Fatigue** (hour tick `fcn.00043acc`): hoursAwake++; Poisoned drains 1–5 LP/h
  (`fcn.000395ec`); >24h "tired" msg; >48h Exhausted (`fcn.00039362`: STR×¾, other attrs/skills
  ×½, backed up in the +6 word). **No timed condition decay exists** otherwise.

## NPC services (PlaceAction, "RE batch 4", RE 5D)
Dispatcher `fcn.000666e9`, table 0x13eaf0; prices gold-tenths from POOLED party gold
(`fcn.00067b9c`). Unk2=intro text, Unk3=confirm, Unk4=success. Types: Heal/Cure/Sleep/Food/
LearnCloseCombat/LearnSpells (price=Unk6×levelReq, mastery seed 4×MagicTalent)/RepairItem/
RestoreItemEnergy/RemoveCurse (DESTROYS cursed equipped)/AskOpinion (identify)/Merchant≡ScrollMerchant.

## World / map / NPC constants (RE 5C, RE 5D, RN automap.c)
- **Query opcodes** (RE 5C §3, dispatcher 0x3ca53): 0x0C facing, 0x19 leader-language
  (`leaderSheet+8 & (1<<arg)`), 0x1E schedule tick (MTicksToday vs arg, 48/hour), 0x21 light-check
  (≥25). Plus 0x26 leader skill.
- **Lockpicking** (RE 5C §2): skill≥difficulty auto-success, else PercentRoll(skill*(100−diff)/100,
  100); diff≥100 unpickable; untrapped = free retries; Lockpick item opens any diff<100, always
  consumed. Lock record +0x1A diff / +0x1C key / +0x1E trap.
- **Dungeon light** (RE 5C §1) `fcn.0001473c`: mode = mapFlags&3; dungeon = `min(100,
  max(spellPct, partyLightItems)) +25 if leader Iskai`; light-item total = Σ Activate over type-22
  items (`fcn.00038f24`).
- **Chase detection** (RE 5D §1) `fcn.00041fbb`: 2D dungeon/wilderness lose party >10 tiles
  EUCLIDEAN; cities unlimited; 3D = Bresenham LoS, no cap. **3D NPC speed** `fcn.0004166c` =
  Speed×Δt/5 units/frame. **3D collision margin** `fcn.0001ede1` = **MAX(tileSize/4, 50)**.
  **MonsterEye** = binary (any chaser detects → 0x15cc5a). RE 5D §1–4.
- **sheet+0x1C** = removed-NPC index `(mapId−1)*96+slot`, set on join, cleared on leave (RE 5D §6).
- **Automap** (RN automap.c): discovery = facing cone depth 10 + flood w/ corner occlusion;
  wall glyph 0x230+connection-mask; floor = 8×8 texture mini; goto glyph 18 gated by switch type 7
  (set by stepping on the tile, `fcn.0005c10a`). Sight-block = wall flag 0x04.

## Audio (RN "Sound id space", "Combat SFX")
- 0x15d824 = SYSTEXTS string table; ShowSystemMessage `fcn.0002f85d`. Melee is SILENT; death =
  sample 268 (party 11000Hz/vol100, monster 15000Hz/vol60); per-spell hardcoded sample sequences
  (CombatAudio.CastSamples). Ambient NPC sound table 13×0x28 @0x13db10 (pending RE 6).

## MONCHAR / sheet bytes worth knowing
`+0x0C` AI behaviour-strategy id (→ 0x13e1f0) · `+0x0D` battle-view render class (1 ground /
2 ghost+translucent / 3 flying / 4 sway — bob via osc 0x54eeb) · `+0x0E` crit-immunity (0x80) +
creature-class mask · `+0x0F` Morale · `+0x11` strikes/action · `+0x1C` removed-NPC index ·
`+0x4B8` (Unk152) battle hover offset.
