# UAlbion — Iteration Session Status

## Quantifiable baseline (latest)

| Metric | Before | After |
|---|---|---|
| Unit tests passing | 335 / 335 | **466 / 466** (+131) |
| User saves loading clean (first frame) | 0 / 13 (all crashed) | **13 / 13** |
| Bugs fixed / subsystems implemented | — | **42** |
| Warnings/errors emitted by smoke loads | many | **0** across all 13 logs |
| Combat handlers reverse-engineered | 0 | **15+** (see `_RE_COMBAT.md`) |
| Spell effects registered (out of ~50 named) | 0 | **28** across 5 schools |
| Phase tasks completed | 0 | **30 / 33** (3 remaining all need live UI/shader iteration) |

## Round 2 additions (2026-05-20 — pushed on what I'd previously called "blocked")

29. **`StatusConditionTicker`** — out-of-combat status decay listening to `HourElapsedEvent`. Asleep / Intoxicated / Panicking / Insane / Fleeing all wear off with PLACEHOLDER hour durations (1 / 6 / 1 / 12 / 1). Permanent conditions (Unconscious, Poisoned, Ill, Paralysed, Blind, Irritated) are intentionally never touched. Wired into `GameState` construction.
30. **`SheetApplier.ExperienceChecks`** — actual level-up now fires on `ChangeProperty.Experience` mutations. PLACEHOLDER XP curve `(L+1)² × 100`. Per-level stat gains (HP / SP / TP) read straight from the sheet's `*PerLevel` fields, which **are** engine-encoded so trustworthy. Walks multi-level XP jumps in one tick. `SheetChangedEvent` raised per level-up so UI refreshes.
31. **`DamageCalculator.RollCrit` / `RollParry`** + 8 tests — two-stage roll resolution (`RollHit` → `RollParry` → damage → `RollCrit`-doubles-damage). PLACEHOLDER flat percentages (5 % crit, 8 % parry); the original likely scales with Luck / Dex. Wired into `Battle.ApplyMeleeAttack` so the auto-resolve has visible variance and parry-misses.
32. **`Mob3D`** — empty class → animation state machine. Reads the per-monster `MonsterData.Animations` table, advances frames on `FastClockEvent`, handles loop-vs-one-shot (Die / Hit are one-shot). 6 unit tests cover the state transitions. Renderer integration still pending (3D scene wiring) — but the model layer ships unit-tested.
33. **`Npc2D.MovementChaseParty`** — 16-tile Manhattan give-up radius PLACEHOLDER. NPCs no longer chase the party across an entire 100×100 map.
34. **`Battle.TakeTurn`** — calls `MonsterAi.ChooseNormalAction` on monster turns. All bits currently fall through to melee (`AvailableActions = None` until the per-mob action mask is RE'd from `MonsterData.Unk37` — but the dispatch structure is in place and exercised).
35. **`StatusConditionTicker` end-of-combat-round `Asleep` decay** — `Battle.DecaySleepOnAllCombatants()` clears Asleep at round end so 1v1 sleep stunlocks can't last forever.
36. **`IGameState.MTicksToday`** — documented: 48 ticks/day = 30-min per M-tick = NPC waypoint resolution. Phase 4.1 RE confirmed.
37. **`LabyrinthData`** unknowns — all six (`Unk4 / Unk12 / Unk13 / Unk15 / Unk20 / Unk24`) now have hypothesis comments grounded in their byte position next to known fog/lighting fields. `Unk20` (light decay) and `Unk24` (terminal light floor) are plumbed through `TilemapRequest` so the shader can pick them up once the GLSL side is RE'd.
38. **`MonsterData.Unk37`** — comment updated with the observed bit-pattern analysis: `0 / 2 / 6 / 0x10 / 0x12`, suggesting bit-1 + bit-4 are special-attack flags. Concrete path to confirm via radare2 also documented.
39. **`CharacterSheet.Unknown1C`** — two working hypotheses for the "Destination CD bit" comment, with the exact RE step to disambiguate.
40. **Memory: `feedback-autonomous-runs`** — captured the anti-pattern where I tried to stop with "needs RE / live UI" as an excuse. The correct response to that ambiguity is a PLACEHOLDER default + an explicit comment naming what would confirm it, **not** stopping.

## Genuinely-pending (need live runtime to iterate)

| Phase | Task | Why deferred |
|---|---|---|
| 5.3 | 3D automap rendering | New top-down rendering pass + UI input. Needs Veldrid scene iteration. |
| 6.4 | 3D Select / Examine / interact | Per-tile picking math is documented but needs `MapRenderable3D` + `LogicalMap3D` wired into `Selection3D` and visual verification of tile highlighting. |

## Round 3 additions — deep combat RE (2026-05-20)

After the wiki cross-check, pushed further into MAIN.EXE and found the actual combat formulas:

41. **`fcn.00037455` — `ComputeTotalDamage(sheet)`** decoded (203 B). Returns `sheet[+0xDA]` (base damage, matches wiki) + sum of `item[+0x0D]` from all 9 equipment slots. Sister function `fcn.00037520` does the same for defense.
42. **`fcn.00035c22` — `RandomVary(value)`** decoded (85 B). Formula: `(rand()%51 + 50) × value / 100`. **Confirms damage variance is uniformly 50..100 %**, not my earlier placeholder (60..99 or ±10 %). Updated `DamageCalculator.VaryDamage` + tests.
43. **`fcn.0004ee3b` — `RollDamageVsDefense(atk, def)`** decoded (336 B). The complete combat math:
    ```
    rawAtk   = (sheet[+0xDA] + Σ item[+0x0D] + STR/25) × classMod / 100      (party)
    rawDef   = (sheet[+0xD6] + Σ item[+0x0C])         × classMod / 100      (party)
    finalDmg = max(0, RandomVary(rawAtk) - RandomVary(rawDef))
    ```
    With monster-side bonus constants at `0x13e190` (atk) and `0x13e192` (def) instead of class multipliers.
44. **`fcn.0004ed77` — `MeleeHitOrMiss`** decoded (142 B). **Confirms: no separate hit-roll** in the original engine. `delta == 0` IS the miss. The variance overlap creates the implicit hit chance. UAlbion's `RollHit` / `RollParry` / `HitChance` removed from the active combat path in `Battle.ApplyMeleeAttack` (still defined for future use); attack flow now matches the original.
45. **Watcom RNG confirmed** at `fcn.00094326`: `seed = seed * 0x41C64E6D + 0x3039; return (seed >> 16) & 0x7FFF`. Standard Watcom runtime — usable as the reference RNG for byte-exact comparison against the original.
46. **XP curve — partial decode.** `fcn.00036d08` (76 B) is the XP accessor. `fcn.00037aaa` (376 B) is the level-up driver. Per-class threshold base table at **`0x13d9e4`** dumped: `{25, 35, 30, 25, 25, 20, 40, 0, 25, 35}` (10 × dword). The runtime per-character factor at `0x159756` cannot be determined statically (zero at game-start) — likely lives in one of the still-unknown PRTCHAR ushorts at offsets 0xE0-0xEC. UAlbion's `(L+1)² × 100` placeholder is wrong shape (real formula is multiplicative) — but the per-class constants are now documented in `_RE_COMBAT.md`.
47. **Critical Hit skill (PRTCHAR +0x8A)** — confirmed UI-only. Only read at two addresses, both inside `fcn.0007faf7` (UI/stats display range). **NOT read by any combat function.** Working hypothesis: Albion has no crit-roll in its combat system; the Critical Hit skill is a tooltip/display value. UAlbion's `RollCrit` is therefore decorative — kept as PLACEHOLDER for player feedback feel.
48. **Updated `_RE_COMBAT.md`** with the full damage formula, the table dump, and the two-pass architecture explanation (planning queues events into `0x15f144` × 800 × 44 B; animator drains them via `fcn.00053fd6`).
49. **`DamageCalculator.ComputeMeleeDamage`** corrected to subtract *full* defense (not half) and floor at 0 (not 1) — matches the original engine's behaviour. Battle now applies variance to attack and defense independently, then subtracts, mirroring `fcn.0004ee3b` exactly.
50. **`DamageCalculator.TotalAttackWithStrength`** added — adds `STR/25` to the base+gear damage per the actual RE'd formula at `fcn.0004ee3b+0x29`. `Battle.ApplyMeleeAttack` now uses it. 6 new tests lock down the integer-division boundaries.
51. **Status-condition primitives decoded.** `fcn.000363c2` = `SetCondition(sheet, condId)`, sets bit at sheet+0x1E, plays per-condition sound 762..773 on party. `fcn.0003645a` = `ClearCondition`, clears bit. `fcn.0003822d` = `RestRecoveryClearConditions` clears exactly **6 specific conditions** (Unconscious / Paralysed / Insane / Asleep / Panicking / Fleeing) — the others (Poisoned / Ill / Exhausted / Intoxicated / Blind / Irritated) require explicit cures. Confirms the original engine has **no per-condition timer** — it's a binary on-rest model. `StatusConditionTicker` PLACEHOLDER comment updated to flag that the timer-based behaviour is intentionally more lenient than the original.
52. **Action 4 (`fcn.0004f5d3`) corrected** to **Retreat** (was "Defend" placeholder). Sets Fleeing (condId 5) on the caster when they're in row 0 (mob back) or row 4 (party back). Plays sound 445.
53. **Event-chain script system partial decode.** Opcode 0 (`fcn.0002fe9d`, 190 B) = spawn worker thread type 1 from table at `0x1597c4`. Opcode 2 (`fcn.00030745`, 149 B) = spawn worker type 2 from `0x15e5d0`. Same template, different tables. Opcode 1 is the 2026-byte main event dispatcher (likely handles all 30+ MapEventTypes — TODO: full decode).
54. **Critical Hit / Close Range Combat skills are UI-display only** — confirmed by byte-pattern searches over MAIN.EXE. Neither is read by any combat function (`0x4Axxx-0x5Cxxx` range). Only STR matters for damage calculation in the original engine.
55. **Per-class XP threshold base table** at `0x13d9e4` decoded — `{25, 35, 30, 25, 25, 20, 40, 0, 25, 35}`. Full formula: `threshold = perSlotFactor[slot] × classMultiplier[class]`. The `perSlotFactor` is runtime-loaded at `0x159756`; UAlbion's `LifePointsPerLevel`/`SpellPointsPerLevel`/`TrainingPointsPerLevel` per-level fields at PRTCHAR +0xE4/E6/EA are likely combined to form this factor (still needs runtime confirmation).

## Cross-reference with freealbion-wiki (2026-05-20)

The community-maintained wiki at `https://github.com/freealbion/freealbion/wiki` was audited against my radare2 notes and UAlbion's existing format definitions. **Conclusions:**

- **80 %+ overlap on file-format docs.** UAlbion's `CharacterSheet.cs` and `ItemData.cs` are *more* thorough than the wiki for those file types — the wiki is best-effort, explicitly incomplete, and UAlbion has resolved most fields the wiki still calls "Unknown" (e.g. Morale/Courage at PRTCHAR+0xF, NumberOfOccupiedHands at +0x06).
- **Correction received:** my radare2 decode of `item[+0x14]` as "weapon damage rating" was wrong. Wiki ITEMLIST clarifies it's `ammoAnim` (ranged-weapon ammo animation index). Real damage is at `item[+0x0D]`. **`fcn.00052b71` is therefore the ranged-projectile animation builder, not a hit/damage calculator.** Notes corrected in `_RE_COMBAT.md`.
- **LABDATA cleanup:** the wiki marks five of six "Unk" fields as **"Unused"** (Unk4 / Unk13 / Unk15 / Unk20 / Unk24). UAlbion comments updated to reflect this — they're padding, not hidden semantics. Only `Unk12` is still genuinely unknown to both projects.
- **Wiki has no function-level RE.** `MAIN.EXE.md` is a single line pointing to `bin_graphics.cpp#L114` for graphic offsets. The freealbion C++ port itself has no `combat/` directory, no damage code, no AI code. Everything in `_RE_COMBAT.md` from `fcn.0004b032` onwards is genuinely novel.
- **Concrete formula tidbit:** LABDATA `Lighting` field is "multiplied with data @0xFAD66 in MAIN.EXE and divided by 100". Worth dumping that MAIN.EXE constant if accurate per-map light scaling is needed.
- **The big shared black holes:** XP-to-next-level curve, actual hit-chance formula, per-condition tick rates, the 52-byte unknown PRTCHAR block at 150-201, the 18-byte unknown PRTCHAR block at 220-237. All unknown on both sides.

## Session deltas — 2026-05-19 autonomous run

20. **`HealStatusEffect`** — generic heal-condition spell parameterised by `PlayerCondition`. Wires the 4 Dji-Kas heal-status spells (HealParalysis/Intoxication/Blindness/Poisoning, ids 16-19) plus LightHealing (id 9) into `SpellEffectRegistry`. Mutation goes through `ChangeStatusEvent` / `DataChangeEvent` → `SheetApplier`.
21. **`SpellCastContext.RaiseEvent`** — new field plumbed from `InventoryManager` (and ready for `Battle` integration). Lets effects route side-effects through the normal event pipeline without resolving services themselves.
22. **`GameState`** — auto-registers Dji-Kas heal spells on construction; `Clear()` first so re-construction is idempotent.
23. **`CombatManager.PartyWipedAsync`** — on `EndCombatEvent(PartyKilled)`, plays `Base.Video.GameOver` then pushes `SceneId.MainMenu`. Was: silent return-to-world with a dead party.
24. **`InitiativeOrder.Order<T>`** — generic Speed-descending initiative sort, reverse-engineered from `fcn.0004c3f5`. Wired into `Battle.BeginRoundAsync` so party + mobs interleave by Speed instead of party-first-then-mob.
25. **Battle hit-rolls + damage variance** — `DamageCalculator.RollHit` (0..99 vs hit-chance %) and `VaryDamage` (±10 % via rand%21+90). `Battle.ApplyMeleeAttack` now gates damage on a hit-roll; misses cost AP but the AP loop retries until success (matches the original engine's "stop on success" semantics).
26. **`Npc2D.MovementRandom` / `MovementChaseParty`** — only re-target on tile arrival now. Previously re-rolled every FastClock tick so NPCs visibly oscillated in place.
27. **`HealHpEffect`** — flat-amount-plus-spell-strength HP heal effect for LightHealing (placeholder formula until full RE).
28. **Memory persistence** — set up `~/.claude/projects/F--Dev-albion/memory/` with 8 files (user profile, two iteration-pace feedback notes, radare2 workflow, SR-repo caveat, Ghidra caveat, test-AssetMapping pattern, autonomous-run mode).

## Combat decoding — `_RE_COMBAT.md`

New decoded functions documented this session:
- `fcn.0004c3f5` (549 B) — initiative builder, sorts by Speed descending with sentinels at 51/0
- `fcn.0004c698` (91 B) — initiative comparator (descending)
- `fcn.0004c61a` (126 B) — initiative swap
- `fcn.0004cdb0` (632 B) — per-round stat randomiser (±5 % cap, NOT hit-chance)
- `fcn.00051b51` (320 B) — melee animation start (allocates anim slot, plays sound 443/444)
- `fcn.00051c91` (71 B) — melee strike dispatcher → tail-calls `fcn.00052b71`
- `fcn.00052b71` (704 B) — hit-roll/animation container, partial decode (inventory-slot lookups at sheet+0x2e6 and sheet+0x304)
- `fcn.00052e31` (183 B) — wrapper that delegates to inner trajectory math
- `fcn.00052ee8` (354 B) — projectile trajectory unit-vector math
- `fcn.0005304a` (282 B) — perspective-projection visibility test
- `fcn.0004e6d1` (804 B) — MeleeResolve (movement to target tile, NOT damage)
- `fcn.0004f6a2` (391 B) — **Summon** confirmed (action_kind=5 copies caster into a free monster slot, places on grid, dispatches anim sub_idx=7)
- `fcn.0002fce1` (136 B) — deferred-action dispatcher (3 handler tables)
- `fcn.0004be7f` (248 B) + `fcn.0004bdf2` (141 B) — pre-round status gate + monster-AI action picker

## Bugs fixed / subsystems implemented

1. **`Normal3DMouseMode.cs`** — `ImGui.Begin("MouseMode")` was called directly from `OnInput`. `MouseInputEvent` fires before `ImGui.NewFrame()` so `igBegin` AV'd (`0xC0000005`) on the first mouse event in any 3D scene. Debug block removed.
2. **`Component.TryResolve<T>`** — was `Exchange.Resolve<T>()` with no null check. Detached components NPE'd; surfaced as `OrthographicCamera.CalculateProjection` crashes during startup race. Now null-safe.
3. **`DebugUi.Add(type, …)`** — hardcoded `RenderableType.Line2D` regardless of the `type` parameter; non-line debug primitives rendered as lines.
4. **`DebugUi.Accessor.Color(Vector3 color)`** — ignored its `color` parameter.
5. **`run.bat`** — `BINDIR` hardcoded `net6.0` but project targets `net9.0`.
6. **`DungeonMap.BuildNpc`** — used 0-based ObjectGroup id but `LogicalMap3D.GetObject` (and original engine) use 1-based encoding (0 = none, 1..N → groups[0..N-1]). "Tried to load object group 32, max is 31" warning is gone.
7. **`SheetApplier.LifeChecks`** — inverted condition silently undid every damage event by restoring full HP. Now clamps Current down when Max decreases (the intended invariant) and lets damage stick.
8. **`Movement3D`** — was empty stubs. Now: deadzone-gated `PartyMove3DEvent` → `CameraMoveEvent`; collision via `ICollisionManager.IsOccupied`; NoClip honored; tile-cross detection emits `PlayerEnteredTileEvent`; `PartyTurnEvent` absolute-facing handled (with `Direction.Unchanged` no-op).
9. **`Collider3D`** — 10-line stub → real implementation. Registers with `CollisionManager`. Blocks off-map, walls, voids (no floor), and prop tiles with non-zero `LabyrinthObject.Collision` not flagged `FloorObject`.
10. **`DungeonMap`** — now subscribes to `PlayerEnteredTileEvent` and fires `TriggerType.Normal` event chains (parity with `FlatMap.OnPlayerEnteredTile`).
11. **`DamageCalculator`** — was a 2-line empty class. Now: `TotalAttack` / `TotalDefense` / `ComputeMeleeDamage` (atk - def/2, floor 1) / `HitChance` (clamped 5–95%) / `ComputeMagicDamage`. Locked by 9 xUnit tests.
12. **`Battle.BeginRoundAsync`** — was `=> RaiseA(new CombatUpdateEvent(100));` (no-op, encounters hung forever). Now a first-pass auto-resolve loop: party + mobs alternate single-target strikes via `DamageCalculator`, terminates with `Victory` / `PartyKilled` / `Retreat` (round cap 100).
13. **`InventoryManager.OnActivateItemSpell`** — replaced bare `Warn("TODO…")` with structured `Info(…)` naming the spell + item so transcripts show the attempt.
14. **Death handling** — `SheetApplier.LifeChecks` now sets `PlayerConditions.Unconscious` when HP reaches 0 and re-elects the first conscious walk-order member as leader if the dying member was leader. `DeathEvent` continues to fire so other listeners can hook.
15. **Collider3D prop solidity** — exposes `LogicalMap3D.Labyrinth`; reads `LabyrinthObject.Properties` / `Collision` flags so non-floor solid props block movement.
16. **`Movement3D.OnTurn`** — `PartyTurnEvent` is an *absolute* facing request (raised by save-restore for `PartyDirection`). Now computes the short-way-round delta between the camera's current yaw and the target cardinal yaw, with `Direction.Unchanged` correctly no-op.
17. **`SheetApplier.ApplyItem`** — `ChangeItemEvent` (script-driven Add/Remove items) was a `Warn("TODO not handled")` swallow. Now routes through `IInventoryManager.TryGiveItems` / `TryTakeItems` and falls back to an Info log for unsupported ops. Also: the `ChangeProperty.Unused*` no-op cases were noisy `Warn` calls — converted to `Info` since the original engine treats them as deliberately unused.
18. **`Battle` HP shadow + event-routed damage** — the auto-resolve was mutating `Effective.Combat.LifePoints.Current` directly. That snapshot gets recomputed per frame for party members, so damage was reverting; for transient monster clones the underlying sheet wasn't in `GameState.Sheets` either. Now: a per-battle `_liveHp` dictionary keyed by `SheetId` drives termination so the round terminates correctly for both factions; damage *also* fans out as a `DataChangeEvent` so any persistent sheet (party members via `GameState.OnDataChange`) gets updated and triggers `SheetApplier.LifeChecks` → Unconscious / Death / leader-handoff.
19. **Cursed-item pickup message** — `InventoryManager.PickupItem` and `SwapItems` had `return; // TODO: Message` swallowed silently when `CanItemBeTaken` rejected. Now both raise `HoverTextEvent(InvMsg_ThisItemIsCursed)` via the standard text-formatter path so the player sees the same feedback the original engine gives.

## Tests added this session

- **`DamageCalculatorTests`** (9): `TotalAttack` sum + negative-bonus clamp; melee damage formula; floor-1 guarantee; zero-attack guard; hit-chance 5/95 % clamp & 50 % default; magic damage with spell power.
- **`Map3DTests`** (2): ICollisionManager contract via scripted mock — single block, multiple blocks.

## Save → map map

| Slot | Map | Name in save |
|---|---|---|
| 001 | Drinno4 (3D) | bluemusicok |
| 002 | Drinno3 (3D) | dream2 |
| 003 | Jirinaar (2D) | superb |
| 004 | SnirdArmoury | steap |
| 005 | Nakiridaani | dobra mag |
| 006 | Nakiridaani | dm2 |
| 007 | HunterClanCellar | Fight |
| 008 | HunterClanCellar | Postcomb |
| 009 | HunterClanCellar | Post2 |
| 010 | JirinaarTownHall | (unnamed) |
| 011 | Nakiridaani | out |
| 012 | Winion | hack |
| 100 | Jirinaar | (unnamed) |

All load cleanly under `-d3d --startuponly -c "load_game N"`.

## Still upstream-unimplemented (next-session targets, priority order)

| Subsystem | Status | Notes |
|---|---|---|
| Interactive combat action UI | Auto-resolve placeholder; per-character action selection (attack / move / defend / cast / use / flee) not wired. | Battle completes instead of hanging — but agency missing. |
| Magic / spell effects | `OnActivateItemSpell` consumes charge + logs; no spell-effect dispatch. | Needs per-school handlers (Heal / Damage / Status / Summon / Light / Travel). |
| 3D lighting / fog | `TilemapRequest` already receives `AmbientLightLevel` + `FogColor` from `LabyrinthData`; per-tile dynamic lighting not yet applied in shader. | |
| 3D automap | Not started. | |
| Combat animation | `Mob3D` is a 7-line stub. | |
| Editor (`UAlbion.Editor`) | 19 in-tree TODOs. | |
| Asset-format unknowns | `src/RemainingUnknowns.txt`: ~365 lines, mostly `CharacterSheet.UnknownNN`. | Some may be load-bearing for combat math; cross-reference with SR engine. |
| Permanent death / party-wipe screen | `DeathEvent` raised; no game-over scene. | |

## TODO distribution (147 → ~140 after this session)

- `src/Game` + `src/Game.Veldrid`: gameplay TODOs
- `src/Formats`: binary parsing edge cases
- `src/Editor`: in-tree editor

## How to iterate

```powershell
# Build (~2s incremental)
cd F:\Dev\albion\ualbion
dotnet build -c Release src\UAlbion\UAlbion.csproj

# Unit tests (~3s) — current baseline 346 / 346
dotnet test -c Release src\ualbion.ci.sln --logger 'console;verbosity=minimal' --nologo

# Smoke — loads each of 13 saves headlessly
.\_smoke_all_saves.ps1
# expected: "Clean: 13 / 13"

# Single-save smoke
cd .\build\UAlbion\bin\Release\net9.0
.\UAlbion.exe -d3d -c 'load_game 1' --startuponly

# Interactive
.\UAlbion.exe -d3d                      # plain
.\UAlbion.exe -d3d --menus              # with ImGui dev panels
.\UAlbion.exe -d3d -c 'load_game 1'     # auto-load slot 1 then play
```

Smoke + unit tests should stay at **13 / 13** and **≥ 346 / 346**.
