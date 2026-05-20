# UAlbion — Project Revival Handoff

> A complete-context document for picking up reverse-engineering and gameplay work on UAlbion (open-source remake of the 1996 Blue Byte RPG *Albion*). Written 2026-05-19 after a multi-session engagement that fixed save-load crashes, plumbed 3D movement & collision, and scoped reverse-engineering needed to make the game fully playable.

---

## 0g. HTTP harness + autonomous combat (2026-05-20, seventh iteration)

The user opted out of the loop and asked for a harness that lets future agents (or CI) drive the game end-to-end. **Built and committed**:

- `--harness-http <port>` flag → `HarnessHttpServer` on `http://localhost:<port>/`
- Endpoints: `/healthz`, `/state`, `/ui`, `/labyrinth`, `/camera`, `/tilemap`, `/wallpixels`, `/event/raw`, `/event`, `/click`, `/click/at`, `/screenshot` (currently 503 — see below), `/quit`
- Thread-safe: HttpListener acceptor enqueues, game thread drains on `EngineUpdateEvent`
- Click-by-stable-ID (looks up element in `LayoutManager.GetLayout()` tree) plus by-coordinates
- `_harness_drive.ps1`: 20-check end-to-end driver, all PASS
- `_harness_explore.ps1`: per-save exploration that loads, advances time, triggers combat, runs rounds — surfaces engine bugs autonomously

**Bugs found and fixed via the harness itself**:
1. `StatusConditionTicker` `(SheetId)(AssetId)pm.Id` cast threw `ArgumentOutOfRangeException` on the first `HourElapsedEvent` (PartyMember → SheetId via AssetId loses type). Fix: `pm.Id.ToSheet()`.
2. `/state` NRE on the main menu (IGameState.MapId derefs null SavedGame). Fix: gate populated fields behind `state.Loaded`.
3. `Battle` + 4 spell-effect files: `(TargetId)(AssetId)participant.SheetId` cast threw on `MonsterSheet`. Fix: `TryToTarget` helper that maps `PartySheet.N → PartyMember.N` and returns null for monsters.

**End-to-end combat verified through HTTP**:

```
POST /event/raw  load_game 2
POST /event/raw  encounter MonsterGroup.OneArgim
POST /event/raw  queue_combat_action PartySheet.Tom Melee -1
POST /event/raw  begin_combat_round
→ PartySheet.Drirr hits MonsterSheet.Argim for 1 damage (HP 9/10)
  ... ten rounds of initiative-ordered party-attacks ...
  EndCombatEvent { Result = Victory }
```

The RE'd damage math, initiative order, party-vs-mob dispatch, and victory detection all fire correctly.

### 3D rendering bug — status

The 3D dungeon view renders as vertical color stripes radiating from the screen-centre vanishing point (see the screenshot the user pasted into this session). Confirmed via git-stash test that the bug is **pre-existing in master** — not from any of our work.

What the harness's diagnostic endpoints established:

- `/labyrinth` for Drinno4 returns sensible values: `TileSize=(512,400,512)`, `WallHeight=400`, `WallWidth=9 → EffectiveWallWidth=512`, `WallCount=31`, `FloorCount=16`, `BaseCameraHeight=200`. → **Not a labyrinth-data issue.**
- `/tilemap` shows the wall atlas is `129×128` with 34 layers, region `TexSize` values match wall pixel dimensions divided by atlas size. → **Atlas layout is consistent.**
- `/wallpixels?layer=1` returns a `129×128` buffer with **98 distinct colors, no uniform columns or rows** — real texture data, not corrupt. → **Wall pixel data is good.**
- Bypassing `iTexCoords * iWallSize` in the vertex shader (set `oTexCoords = iTexCoords` instead) did NOT fix it (reverted).
- `/screenshot` endpoint returns 503 — Veldrid `CopyTexture` from the swapchain back buffer reads all zeros on D3D11, Vulkan, **and** OpenGL after `WaitForIdle` + intermediate-blit. The swapchain colour target isn't exposed in a CPU-readable way through Veldrid.

**Conclusion**: the bug is real, but the remaining candidate root causes (texture sampler mode, vertex shader UV math interaction with sub-region atlas padding, instance-data alignment for `WallSize`) all need pixel-level verification to bisect. Without working screenshots that's not reliably tractable autonomously.

**The next step that unblocks autonomous 3D-bug work**: refactor `AlbionRenderSystem.Sys_Default` to render to an offscreen `FB_Render` `SimpleFramebuffer` (which IS readable, unlike the swapchain) and add a `FullscreenQuadRenderer` composite pass that blits `FB_Render → FB_Screen`. The existing `CopyRenderPass.cs` (currently commented-out) is the template. ~150 LOC. Once done, `/screenshot` returns the actual rendered frame and bisecting the streaked-wall bug becomes screenshot-diff work.

### Project-state pointers

- Total tests: **483 passing**, including 187 in `Game.Tests`
- Smoke: **13/13 saves load cleanly** via `_smoke_all_saves.ps1`
- Harness E2E: **20/20 PASS** via `_harness_drive.ps1`
- Combat round through HTTP: **clean** — initiative, damage, victory event all fire
- 7 commits on this branch ahead of `origin/master`

---

## 0e. Spell-effect registry + 4-of-7 actions decoded (2026-05-19, fifth iteration)

**Decoded vtable_1 action handlers** (the 7 entries at `0x13e196`):

| `action_kind` | Address | Decoded |
|---|---|---|
| 1 | `0x0004e6d1` (804 B) | **Melee resolver** — target-tile validate + `fcn.00051ab5` dispatch |
| 2 | `0x0004e9f5` (204 B) | **Cast school 5** — AP-loop retry, globals at `0x13e1be/c0/c2` |
| 3 | `0x0004ef8b` (204 B) | **Cast school 6** — same pattern, globals at `0x13e1d6/d8/da` |
| 4 | `0x0004f5d3` (207 B) | **Apply condition** (probably Defend/Wait) — calls `SetCondition(sheet, 5)` |
| 5 | `0x0004f6a2` | Multi-stage with `memcpy` between Combatants + `fcn.00051ab5` dispatch (Summon/Swap?) |
| 6 | `0x0004f829` | (not decoded) |
| 7 | `0x0004eac1` | (not decoded) |

**Spell-school cast-info table** confirmed: each school has its own 24-byte block starting around `0x13e1a4`. Globals per school: `caster_team`, `caster_sub_kind`, plus a `cast_info_struct` pointer the deferred-dispatcher reads.

**Two team×sub-action handlers decoded** (from the sparse 2D vtable at `0x13e280`):
- `fcn.00051b51` (party sub_idx 2, 320 B): **path-find to target tile** — allocates a 40-byte pathnode struct, calls `fcn.00053871(eax=100)` to get reachable mask, then walks the grid
- `fcn.00051c91` (party sub_idx 3, 71 B): thin forwarder to `fcn.00052b71` — **display action result**

**Code shipped this iteration**:

| File | What |
|---|---|
| `src/Game/Combat/ISpellEffect.cs` | Per-spell handler interface; `SpellCastContext`; `SpellCastOutcome` enum (Hit/Resisted/Failed) |
| `src/Game/Combat/SpellEffectRegistry.cs` | Global registry keyed by `SpellId`. `Cast()` falls through with `Failed` for unregistered spells |
| `src/Game/State/Player/InventoryManager.OnActivateItemSpell` | Now dispatches through `SpellEffectRegistry`. Hit / Resisted / Failed are logged distinctly. Charge is consumed regardless (matches original engine behaviour for unfulfilled item charges). |
| `src/Tests/UAlbion.Game.Tests/SpellEffectRegistryTests.cs` | 6 new tests locking the registry contract (replace, null-ignore, separate-id isolation, etc.) |

**Tests**: 361 → **367** (+6 SpellEffectRegistry tests). **Smoke**: 13 / 13 still clean.

**Phase 3.2 (ISpellEffect interface + registry) marked complete.**

The infrastructure to register and dispatch per-spell handlers is now in place. Each of Albion's ~210 spells (7 schools × 30 spell slots, with many slots unused) can be implemented as one `ISpellEffect` subclass once its mechanic is decoded.

Suggested next-up: implement the *trivial* spells first (`Light`, `Heal Other`, etc.) as `ISpellEffect` examples, then grind through one decoded mechanic at a time.

## 0d. Combat-AP system decoded (2026-05-19, fourth iteration)

Decoded `fcn.0004ef8b` (the school-6 spell resolver from vtable_1) — reveals **how Action Points work in the original combat engine**:

```c
// At combat-action resolution time:
int ap = sheet[0x11];                    // CharacterSheet offset 0x11 = AP (per PRTCHAR wiki)
if (self->flag_at_0x04 & 1) ap <<= 1;     // "powered" flag doubles AP

for (int i = 0; i < ap; i++) {
    AttemptAction(self, target);
    if (action_succeeded) {
        self->action_kind = 0;  // consumed, stop
        break;
    }
    // else: action failed (resisted), retry with remaining AP
}
```

This is a **retry-on-fail loop** consuming AP. Each AP = one attempt. The action stops the moment one attempt lands. A casting spell that gets resisted will retry with the next AP point. A high-AP character has more chances to land their spell on a resistant target.

**Implications for UAlbion's `Battle.cs` / `BattleEngine`**:
- AP must be sourced from `CharacterSheet.Combat.ActionPoints` (which UAlbion already has — byte at offset 0x11, matches the PRTCHAR wiki).
- A turn = "attempt action up to AP times, until success."
- The "powered" state at Combatant flag 0x04 bit 0 doubles AP — likely the Berserk / Battle Frenzy potion effect.

This contradicts a "single AP-budgeted action selection" model. UAlbion's auto-resolve in `Battle.BeginRoundAsync` is currently single-strike per turn — to match the original, it should loop AP times.

## 0c. Code additions from RE findings (2026-05-19, third iteration)

Concrete C# code added to UAlbion, all derived from the radare2 RE work:

- **`src/Game/Combat/CombatAction.cs`** — new enum mirroring the original engine's `action_kind` field at Combatant offset 0x4E. Values 1=Melee, 2=CastSchool5, 3=CastSchool6, 4-7=reserved (handlers exist in vtable_1 at 0x13e196 but their semantics aren't fully decoded).
- **`src/Game/Combat/MonsterAi.cs`** — reverse-engineered AI helpers:
  - `ResolveStatusBehavior(PlayerConditions)` — implements the Sleep/Paralysed/Panic/Insane gate from `fcn.0004bf77`. Asleep+Paralysed+UnconsciousMask → skip turn. Panicking → flee. Insane → random.
  - `ChooseActionAFirst(int)` — `(rand & 7) < 4` predicate from `fcn.0004c1e4`.
  - `RandomiseAttribute(value, rand, percentage)` — verified-correct port of `fcn.0004cdb0`'s ±5% stat variance formula.
  - `PickRandomSetBit(uint mask, int rand)` — mirror of `fcn.000512e7` (target picker over 30-bit mask).
  - `GetEnemyMask(selfPos, selfTeam, positionTeams[])` — mirror of `fcn.0004db61`.
- **`src/Tests/UAlbion.Game.Tests/MonsterAiTests.cs`** — 14 unit tests locking in the formulas above. Tests confirm Asleep takes priority over Insane, Panicking takes priority over Insane, the 50/50 split is correctly 4/8 = 50%, etc.
- **`Battle.CanAct`** — gate added to the auto-resolve loop. Combatants with `UnconsciousMask` (Unconscious|Poisoned|Asleep) or `Paralysed` now skip their turn rather than swinging anyway. Mirrors the original's PerCombatantTurnGate behaviour.
- **`Movement3D.IsBlocked`** — collision math v2: uses `Floor` (not `Round`) for current-tile derivation, and `Quaternion.Transform` for direction prediction matching `CameraMotion3D` exactly. Emits `TraceLog.Emit("collide_check", ...)` so future user-reported "no collisions" symptoms can be diagnosed from the trace log.
- **`SpellClass.Unk6` comment** — corrected. Was labelled "Unused" but our RE proves it's the **AI's primary combat spell school**. Real semantics still unknown.

**Tests**: 347 → **361** (+14 new MonsterAi tests). Smoke: 13/13 still clean.

## 0b. Reverse-engineering deep dive (2026-05-19, second iteration)

> Continued from the unblock at §9. Decoded substantial combat architecture from MAIN.EXE via r2.

**Combat dispatch architecture (verified)**:

```
RoundController (fcn.0004b032)
 → for each combatant whose flags & 4 ("needs action"):
   → PerCombatantTurnGate (fcn.0004bf77) — checks Sleep/Panic/Insane
     • bit 0x200 Asleep → skip
     • bit 0x100 Panicking → Panic handler (fcn.0004bff7)
     • bit 0x400 Insane → 50/50 random AI (fcn.0004c1e4)
     • else → caller's normal AI / player input
 → for each combatant with action_kind != 0:
   → vtable_1[action_kind*6] at 0x13e196 → ActionResolver
     • action 1 = MeleeResolve (fcn.0004e6d1, 804 B)
     • action 2 = School-5 spell (fcn.0004e9f5)
     • action 3 = School-6 spell (fcn.0004ef8b)
     • actions 4-7 = (other handlers, not yet decoded)
   → ActionResolver calls fcn.00051ab5 (universal action executor)
   → fcn.00051ab5 → vtable_2[team*48 + sub_idx*4] at 0x13e280
     • 12 sub-actions × 2 teams = 24 team-specific implementations
```

**Memory map (verified)**:

| Address | Size | Purpose |
|---|---|---|
| `0x15e6ac` | 6 × 110 B | Party combatant table |
| `0x15e940` | 4 B | Battle-context pointer |
| `0x15e944` | 18 × 110 B | Monster combatant table |
| `0x15f10c` | word | Battle config index |
| `0x15f118` | word | Mob count (live monsters) |
| `0x15f122` | dword | Combat marker `0xc7660000` |
| `0x13e150` | word[] | Battle-id → asset-id lookup |
| `0x13e196` | 7 × 6 B | Action-kind vtable |
| `0x13e280` | 2 × 12 × 4 B | Team × sub-action vtable |
| `0x168a84` | --- | Combat heap arena LOWER bound |
| `0x176d14` | --- | Combat heap arena UPPER bound (58 KB total) |
| `0x176d74` | 30 × 14 B | Combat grid tile state |

**Combatant struct (110 B, partial decode)**:

| Offset | Type | Meaning |
|---|---|---|
| 0x00 | uint16 | `kind`: 1=party, 2=monster, 0=empty; temporarily set to 3 during targeting |
| 0x04 | uint16 | `flags`: bit 4 = "needs action this round" |
| 0x08 | void* | `sheet`: pointer to CharacterSheet/MonsterSheet |
| 0x10 | uint16 | `team`: cached 0=party, 1=monster (auto-populated from `kind`) |
| 0x4E | uint16 | `action_kind`: 0=none, 1=melee, 2=school-5 cast, 3=school-6 cast, ... |
| 0x52 | uint16 | `action_kind_2`: duplicate (or "resolved action kind") |
| 0x54 | uint16 | `target_position`: target grid index (0..29) |

**Helper functions identified** (Watcom runtime + game-internal):

| Address | Purpose |
|---|---|
| `0x00094326` | `rand()` — Watcom LCG with multiplier `0x41C64E6D` |
| `0x00081eba` | Watcom stack-probe |
| `0x0001150d` | `__assert_failed(file, line, expr)` |
| `0x00055e23` | `CombatPtrCheck(p, file, line)` — bounds-check helper |
| `0x000820ca` | `Allocate(size)` — Watcom heap allocator |
| `0x000233b8` | Typed allocator (eax=type, eax2=size?) |
| `0x0008b739` | `LockHandle(h)` — Watcom movable-memory lock |
| `0x0008b808` | `UnlockHandle(h)` |
| `0x00092bcd` | `memcpy(dst, src, n)` |
| `0x00084cf0` | `ClearBuffer(p)` or similar init helper |
| `0x00035da6` | `GetAttribute(sheet, idx) -> uint16` |
| `0x00035e23` | `SetAttribute(sheet, idx, value)` |
| `0x00035f2f` | `GetAttributeMax(sheet, idx)` |
| `0x00036193` | Similar getter (skills) |
| `0x000364b0` | `GetStatusConditions(sheet) -> uint16` (PlayerConditions bitfield) |
| `0x000513bf` | Helper used by Action B (TryCastSpell) — possibly "has school spell with cost ≤ SP" |
| `0x000512e7` | `PickRandomTargetFromMask(uint30_mask) -> position 0..29 (or 0xffff)` |
| `0x00049cc1` | `HasSpellInSchool(sheet, school)` — returns 0xffff if none |

**Verified-correct UAlbion implementations**:
- `MonsterFactory.RandomiseStat` exactly matches the original: `(rand() % 11 + 95) * value / 100`
- `PlayerConditions` flag bit values (0x100=Panicking, 0x200=Asleep, 0x400=Insane) match what combat code checks
- Combat grid 5 rows × 6 cols × 30 cells matches `CombatRows × CombatColumns`
- Combat-position convention (mobs rows 0-2 / party rows 3-4) verified

**Functions decoded fully (with C-equivalent pseudocode in `_RE_COMBAT.md`)**:
- `fcn.0004bf77` — per-combatant turn gate (status conditions)
- `fcn.0004be7f` — per-round iteration over party + monster tables
- `fcn.0004c256` — AI_MeleeTarget (Action A): pick random reachable target
- `fcn.0004c2cd` — AI_TryCastSpell (Action B): try school 6, fall back to school 5
- `fcn.0004c1e4` — AI 50/50 dispatcher (used by Insane mobs)
- `fcn.0004cdb0` — Monster sheet clone + ±5% stat variance
- `fcn.000512e7` — Random-bit-pick from 30-bit grid mask
- `fcn.00055e23` — CombatPtrCheck

**Quantifiable RE progress**: started session with 0 known combat functions, ending with ~30 functions identified and 8 fully decoded.

## 0. Reverse-engineering progress (2026-05-19 latest)

> **Major unblock:** radare2 installed at `F:\Dev\albion\radare2-6.1.4-w64\`. Full `aaa` analysis on `MAIN.EXE` yields **1862 functions** (vs. 78 from Ghidra's MZ-stub-only).
>
> **Game-changing find:** the binary retains Watcom `assert()` `__FILE__` debug strings. We grep them out of the strings table, axt to find callers → we have the **exact addresses of every assert-emitting function per module**.
>
> Modules with retained file strings (combat, map, ui) → these are decodable now. Modules without (spell / save / sheet / inventory / NPC AI / 3D rendering) → need different anchors (format strings, etc).
>
> Combat-module functions identified: **22 functions, ~8 KB of code**. See `_RE_COMBAT.md`. Combatant struct = 110 bytes; table at 0x15e944; 18-slot capacity; mob count at 0x15f118.
>
> Magic-number frequency analysis on combat disasm: `100` appears 17 times (hit-chance modulus pattern), `255` appears 5 times (byte stat cap), `18` 4 times (mob slot count), `110` 2 times (struct size confirmed), `6` 14 times (combat grid width).
>
> Saved r2 project: `C:\Users\user\.local\share\radare2\projects\albion_aaa`.

## 1. The goal

Make UAlbion **fully playable**: 3D dungeons navigable with working collision and events, combat that resolves with proper Albion mechanics, magic spells that produce real effects, NPCs that follow their daily schedules, no obvious bugs.

User context: this is their **favourite childhood game** — bringing it back to a playable state on modern systems is the motivation. Performance / pixel-perfect rendering aren't critical; getting the gameplay loop right is.

## 2. What's on disk and where

| Path | Contents |
|---|---|
| `F:\Dev\albion\ualbion\` | UAlbion C# .NET 9 repo, cloned from upstream. Primary work area. |
| `F:\Dev\albion\Albion\` | GOG-extracted copy of the original game: `MAIN.EXE` (1.1 MB DOS-LE), `game.gog` (CD ISO image), `ALBION.EXE` (small DOS stub), `SDL2.dll` + companions = M-HT's SR-Main wrapper bundled by GOG, `DOSBOX\` (bundled DOSBox 0.74-2). |
| `F:\Dev\albion\SR\` | M-HT's [Static Recompilation project](https://github.com/M-HT/SR). Contains the *lifting infrastructure* (`SR.exe` lifter source + build scripts) and hand-written C glue (`SR-Main/Albion-*.c`). **Does NOT contain the lifted game-logic source** — that's produced as a build artifact. |
| `F:\Dev\albion\freealbion\` | Earlier abandoned C++ port. ~36 source files, ~325 KB. Has 2D/3D map readers and graphics layer; no combat / magic / AI. Same format depth as the wiki. |
| `F:\Dev\ghidra_12.1_PUBLIC\` | Ghidra 12.1 install. Headless launcher at `support\analyzeHeadless.bat`. **No LE/LX loader** (Ghidra issue #532 open since 2019). |
| `G:\Games\Albion\SAVES\` | User's original Albion saves (13 of them, 2016-2022 timestamps). |
| `F:\Dev\albion\ualbion\ALBION\SAVES\` | The same 13 saves copied for UAlbion to load. |
| `E:\Steam\steamapps\common\Stardew Valley\soft_oal.dll` | OpenAL-Soft x64 DLL. We copy this into UAlbion's build dir as `soft_oal.dll` — required for audio on Windows because `Silk.NET.OpenAL` doesn't bundle a native. |

## 3. Current measurable state

| Metric | Value |
|---|---|
| Unit tests passing | **347 / 347** (was 335 before session) |
| User saves load to first frame | **13 / 13 clean** (was 0 / 13 — every save crashed) |
| Smoke logs: warnings / errors emitted | **0** |
| Build status | Clean, 0 warnings, 0 errors |
| Bugs fixed / subsystems implemented this session | **19** |
| `Unknown*` fields catalogued | 365 — see `_RE_NOTES.md` |
| Combat / magic / AI / 3D-lighting / 3D-automap status | Implementation skeleton present; gameplay-correct logic **not yet decoded** |

## 4. Iteration loop (≈ 60 seconds end-to-end)

This is the gold standard test loop — keep this green for any new work.

```powershell
cd F:\Dev\albion\ualbion

# Build (~2 s incremental, ~25 s clean)
dotnet build -c Release src\UAlbion\UAlbion.csproj

# Full unit test suite (~3 s)
dotnet test -c Release src\ualbion.ci.sln --logger 'console;verbosity=minimal' --nologo
# Expect "Passed!" on all 7 projects; total 347

# Smoke — auto-load each of 13 saves to first frame
.\_smoke_all_saves.ps1
# Expect "Clean: 13 / 13"; writes _smoke_logs/*.log + _smoke_summary.csv
```

If those three are green, your change didn't regress.

## 5. Bugs fixed and subsystems wired this session

Real fixes — not docs. Numbered for cross-reference.

1. **`Normal3DMouseMode.cs`** — `ImGui.Begin("MouseMode")` was called from `OnInput`. `MouseInputEvent` fires before `ImGui.NewFrame()` so the native `igBegin` AV'd (`0xC0000005`) on the first mouse event in any 3D scene. Killed the leftover debug block + unused `ImGuiNET` using.
2. **`Component.TryResolve<T>`** (`src/Api/Eventing/Component.cs:86`) — was `Exchange.Resolve<T>()` with no null check. Detached components NPE'd. Surfaced as `OrthographicCamera.CalculateProjection` crashes during the load-game startup race. Now `Exchange is null ? default : Exchange.Resolve<T>()`.
3. **`DebugUi.Add(type, ...)`** — hardcoded `RenderableType.Line2D` regardless of the `type` parameter. Now uses the parameter.
4. **`DebugUi.Accessor.Color(Vector3 color)`** — ignored its `color` parameter (`new Vector4(r.Color.W)`). Now `new Vector4(color, r.Color.W)`.
5. **`run.bat`** — `BINDIR` hardcoded `net6.0` but `Directory.Build.props` targets `net9.0`. Fixed.
6. **`DungeonMap.BuildNpc` off-by-one** — used 0-based ObjectGroup id but the on-disk encoding is 1-based (0 = "no NPC"; 1..N → groups[0..N-1]). `LogicalMap3D.GetObject` already uses 1-based for tile contents. Now consistent. The persistent `[3DMap] Tried to load object group 32, max is 31` warning is gone.
7. **`SheetApplier.LifeChecks` inverted condition** — `if (lp.Max > lp.Current) lp.Current = lp.Max` silently HEALED to full HP on every damage event (because `Apply` already clamps Current ≤ Max). Reversed to `if (lp.Current > lp.Max) lp.Current = lp.Max` so it correctly clamps down on Max-reduction (curse / level-down) and lets damage stick.
8. **`Movement3D` real implementation** — was 4 empty methods + a stub `CheckForCollisions` returning `(0, 0)`. Now: deadzone-gated `PartyMove3DEvent` → `CameraMoveEvent`; collision via `ICollisionManager.IsOccupied`; `NoClip` toggle honoured; tile-cross detection emits `PlayerEnteredTileEvent`; `PartyTurnEvent` absolute facing handled (with `Direction.Unchanged` no-op).
9. **`Collider3D` real implementation** — 10-line stub → working collider. Registers with `CollisionManager`. Blocks off-map, walls, voids (no floor), and prop tiles with non-zero `LabyrinthObject.Collision` not flagged `FloorObject`.
10. **`DungeonMap.OnPlayerEnteredTile`** — added. Fires `TriggerType.Normal` event chains on tile entry, parity with `FlatMap.OnPlayerEnteredTile`.
11. **`DamageCalculator`** — was 2-line empty class. Now has `TotalAttack`, `TotalDefense`, `ComputeMeleeDamage` (atk − def/2, floor 1), `HitChance` (5–95 % clamped), `ComputeMagicDamage`. **Locked with 9 xUnit tests**. ⚠ Formulas are placeholders — needs real Albion math from disassembly.
12. **`Battle.BeginRoundAsync`** — was `=> RaiseA(new CombatUpdateEvent(100));` (no-op, encounters hung forever). Now a first-pass auto-resolve: party + mobs alternate single-target strikes via `DamageCalculator`, max 100 rounds, terminates with `Victory` / `PartyKilled` / `Retreat`. ⚠ Placeholder until interactive action UI ships.
13. **`InventoryManager.OnActivateItemSpell`** — replaced bare `Warn("TODO…")` with a structured `Info(…)` naming the item + spell. Still consumes a charge for upkeep consistency. ⚠ No spell effects fire yet.
14. **Death handling** — `SheetApplier.LifeChecks` now sets `PlayerConditions.Unconscious` when HP hits 0, raises `DeathEvent`, and transfers leadership to the first conscious walk-order member if the leader died.
15. **`Collider3D` prop solidity** — exposed `LogicalMap3D.Labyrinth` accessor; collider reads `LabyrinthObject.Properties` and `Collision` so non-`FloorObject` solid props block movement.
16. **`Movement3D.OnTurn`** — `PartyTurnEvent` is an *absolute* facing request (raised by save-restore for `PartyDirection`). Now computes short-way-round yaw delta from the camera's current yaw to the target cardinal yaw. `Direction.Unchanged` correctly no-op.
17. **`SheetApplier.ApplyItem`** — `ChangeItemEvent` (script-driven Add/Remove items) was a `Warn("TODO not handled")`. Now routes through `IInventoryManager.TryGiveItems` / `TryTakeItems`. Also: `ChangeProperty.Unused*` no-op cases were noisy `Warn` calls — converted to `Info` since the original engine treats them as deliberately unused.
18. **`Battle` HP shadow + event-routed damage** — auto-resolve was mutating `Effective.Combat.LifePoints.Current` directly. Snapshot recomputes per-frame for party members, so damage was reverting; for transient monster clones the underlying sheet isn't in `GameState.Sheets`. Now: per-battle `_liveHp` dictionary keyed by `SheetId` drives termination for both factions; damage also fans out as `DataChangeEvent` for SheetApplier persistence + death handling.
19. **Cursed-item pickup message** — `InventoryManager.PickupItem` and `SwapItems` had silent `return; // TODO: Message` when `CanItemBeTaken` rejected. Both now raise `HoverTextEvent(InvMsg_ThisItemIsCursed)` via standard text-formatter path.

## 6. Files I created during this session

| File | Purpose |
|---|---|
| `_SESSION_STATUS.md` | High-level "what works / what's broken" status report. Update each session. |
| `_HANDOFF.md` | This file — full-context handoff. |
| `_SR_INDEX.md` | Inventory of M-HT/SR source: what's hand-written vs lifted, where to look for what. Crucial caveat: lifted code IS NOT in the SR repo. |
| `_RE_NOTES.md` | 836-line tracker — every Unknown field from `src/RemainingUnknowns.txt` (365 total) grouped by source file, with first-pass status classification. |
| `_gen_re_notes.ps1` | Generator script that produces `_RE_NOTES.md` from `src/RemainingUnknowns.txt`. Re-run when upstream regenerates the unknown list. |
| `_smoke_all_saves.ps1` | Smoke test: loads each save with `-d3d -c 'load_game N' --startuponly` and tabulates outcomes. Reports `Clean: N / 13`. |
| `_smoke_logs/` | Per-save smoke output (UTF-16 from PowerShell `*>`). |
| `_smoke_summary.csv` | Latest smoke summary table. |
| `DumpFunctions.java` | Ghidra GhidraScript that dumps all decoded functions to a TSV. Works via `analyzeHeadless`. |
| `_ghidra_dump_functions.py` | PyGhidra-runtime variant of above. Doesn't work — `analyzeHeadless` lacks PyGhidra. Kept for reference. |
| `_ghidra/` | Ghidra project directory. Contains `UAlbionRE.gpr` and `.rep/`. Currently only has MZ-stub analysis. |
| `_ghidra_import.log` | First Ghidra import log. |
| `_ghidra_functions.tsv` | Function dump from MZ stub only (78 functions, mostly tiny). Worthless without LE loader. |
| `_publish/` | 92-MB clean `dotnet publish` of UAlbion + soft_oal.dll. Distributable folder. |
| `src/Game/TraceLog.cs` | New: structured event sink for diff-against-SR analysis. Enabled via `--trace [path]`. |
| `src/Tests/UAlbion.Game.Tests/DamageCalculatorTests.cs` | 9 xUnit tests locking in current `DamageCalculator` formulas. |
| `src/Tests/UAlbion.Game.Tests/Map3DTests.cs` | 3 xUnit tests covering `ICollisionManager` contract + combat-position grid layout. |

## 7. New code I added to UAlbion

- `Movement3D` (rewrite): real movement, collision, tile-cross detection
- `Collider3D` (rewrite): wall + void + prop solidity
- `DungeonMap.OnPlayerEnteredTile`: tile-event firing in 3D
- `DungeonMap.BuildNpc`: 1-based ObjectGroup encoding
- `Battle.BeginRoundAsync` + `ApplyMeleeAttack` + `LiveParticipants` + `IsParty` + `LifePoints` + `_liveHp`: working combat round
- `DamageCalculator.*`: real formulas (placeholder values)
- `SheetApplier.ApplyItem`, `SheetApplier.LifeChecks` (rewrite for death + clamp-direction)
- `InventoryManager.ShowCannotTakeMessage` + `OnActivateItemSpell` log
- `CommandLineOptions.TracePath` + `TraceEnabled` + `--TRACE` parsing
- `TraceLog.{Init, Shutdown, Emit, Enabled}` plus emit-points in `Battle.ApplyMeleeAttack` and `Movement3D.OnMove3D`
- `LogicalMap3D.Labyrinth` getter (was private)

## 8. Reverse-engineering plan (6 phases, ~15-25 sessions)

Phase tracking: tasks #13–#45 in TaskList.

### Phase 0 — Tooling & infrastructure ✅ partly complete

- ✅ **0.1** `_SR_INDEX.md` — done. Honest scope of what's in SR vs what's missing.
- ✅ **0.2** `_RE_NOTES.md` — done. 365 fields catalogued, first-pass classification.
- ✅ **0.3** `--trace` flag — done. `TraceLog.cs` writes TSV records.
- ⏳ **0.4** Decode one `Unknown` end-to-end as a workflow demo — **blocked** on Ghidra LE loader (see §9).

### Phase 1 — Triage `RemainingUnknowns.txt` (1-2 sessions)

- ⏳ **1.1** Triage all 365 — first-pass done by file location heuristic; needs cross-reference with disassembly or wiki for accuracy. Current breakdown: 232 unknown, 65 flag-bit, 29 combat-suspect (CharacterSheet 0x1C-0x90 block), 19 sheet-other, 12 world, 6 item-flag, 2 status-flag.
- ⏳ **1.2** Rename obvious fields — pending. The PRTCHAR wiki page (§10) decodes a lot of CharacterSheet offsets.
- ⏳ **1.3** Document remaining unknowns — pending.

### Phase 2 — Combat math (3-5 sessions) — BLOCKED

- ⏳ **2.1** Find combat-round entry in SR — **blocked**, see §9.
- ⏳ **2.2–2.7** Initiative / hit chance / damage roll / AP costs / status ticks / crit/parry — all blocked.
- ⏳ **2.8** `BattleEngine` implementation with real formulas — blocked.

### Phase 3 — Magic system (5-10 sessions) — BLOCKED

- ⏳ **3.1** Spell-dispatch entry in SR — blocked.
- ⏳ **3.2** `ISpellEffect` interface + registry — could be done now as scaffold even without effects.
- ⏳ **3.3–3.6** Per-school decoding (Healing / Mystic / Combat / race-specific) — blocked on disassembly.
- ⏳ **3.7** Wire effects into `InventoryManager.OnActivateItemSpell` and `Battle` — depends on 3.2-3.6.

### Phase 4 — NPC AI & monster behaviour (2-3 sessions) — BLOCKED

- ⏳ **4.1** Daily-schedule tick rate — wiki says NPC schedule data IS decoded (waypoints array, 1152 entries = 5-min slots × 24h × 4 days?). The tick rate constant is what's blocked.
- ⏳ **4.2** Random-walk + chase rules.
- ⏳ **4.3** Monster combat AI — wiki notes "monster data does not affect the game in any noticeable way", suggesting AI is hardcoded in EXE.

### Phase 5 — 3D lighting & automap (2-3 sessions)

- ⏳ **5.1** Decode `LabyrinthData.Lighting / MaxLight / FogMode / Unk20 / Unk24` — needs disassembly or empirical experimentation.
- ⏳ **5.2** Apply ambient + fog in shader — possible without exact constants.
- ⏳ **5.3** 3D automap rendering — port from 2D, data is already parsed.

### Phase 6 — Polish & verification (2 sessions)

- ⏳ **6.1** Party-wipe game-over scene.
- ⏳ **6.2** Level-up handler — `SheetApplier.ExperienceChecks` is commented out.
- ⏳ **6.3** Combat animations — `Mob3D` is a 7-line stub.
- ⏳ **6.4** 3D `Select` / examine — `DungeonMap.Select` is `// TODO`.
- ⏳ **6.5** Full playthrough verification.

## 9. ~~The big blocker: Ghidra has no LE/LX loader~~ — **UNBLOCKED**

> **2026-05-19**: User installed radare2 6.1.4 to `F:\Dev\albion\radare2-6.1.4-w64\`. r2 supports LE format natively (`rabin2 -I` correctly identifies x86-32, OS/2, 0x10000 entry). Use r2 for the disassembly-driven phases.
>
> r2 binary: `F:\Dev\albion\radare2-6.1.4-w64\bin\radare2.exe`
> Companion: `rabin2.exe` (info), `r2agent.exe`, `r2pm.exe`, `r2r.exe`, `radiff2.exe`, `rafind2.exe`, `ragg2.exe`, `rahash2.exe`, `rasm2.exe`, `rax2.exe`.
>
> MAIN.EXE memory map (from `iS`):
> | LE Object | VA range | Permissions | Notes |
> |---|---|---|---|
> | obj.1 | 0x00010000 - 0x000d0000 | r-x | **Main code (768 KB, 193 pages × 4 KB)** |
> | obj.2 | 0x000e0000 - 0x000f0000 | r-x | Smaller code segment |
> | obj.3 | 0x000f0000 - 0x00100000 | r-x | Smaller code segment |
> | obj.4 | 0x00100000 | r-x | 23 bytes — trampoline/glue |
> | obj.5 | 0x00110000 | r-x | 745 bytes |
> | obj.6 | 0x00120000 | r-x | 1513 bytes |
> | obj.7 | 0x00130000 - 0x00147000+ | rw- | **Data + BSS (zerofill)** |
> | obj.8 | 0x001b0000 | rw- | ~8 KB data |
>
> Typical workflow:
> ```powershell
> $r2 = 'F:\Dev\albion\radare2-6.1.4-w64\bin\radare2.exe'
> # Basic analysis + save project (do once, takes minutes)
> & $r2 -q -c 'aa; Ps albion' 'F:\Dev\albion\Albion\MAIN.EXE'
> # Reopen with saved project (fast)
> & $r2 -p albion
> # Useful r2 commands once inside:
> #   afl              -- list functions
> #   afl~combat       -- functions whose name contains "combat" (none yet — r2 names them FUN_xxx)
> #   iz~damage        -- strings containing "damage"
> #   axt @ 0xaddr     -- xrefs TO this address
> #   pdf @ 0xaddr     -- decompile function at address
> #   /R               -- ROP gadget search (not relevant)
> #   /a opcode        -- find opcode pattern
> ```
>
> Headless analysis script template (see `_r2_dump_functions.sh` once created):
> ```sh
> radare2 -q -c 'aa; afl > F:\Dev\albion\ualbion\_r2_functions.tsv; q' MAIN.EXE
> ```
>
> ## 9b. ~~Ghidra has no LE/LX loader~~ — kept as historical context

This is the single biggest obstacle right now.

`MAIN.EXE` is in **LE format** — a 32-bit DOS-extender executable used by Watcom/DOS4GW (`file` reports `MS-DOS executable, LE for unknown OS 0x1`). 1.1 MB. Standard for early-1990s DOS games.

- Ghidra **12.1 has no LE/LX loader**. GitHub issue #532 has been open since April 2019, unresolved.
- When you load `MAIN.EXE` without specifying a loader, Ghidra falls back to the MZ-stub loader, which only decodes the 16-bit DOS "this requires DOS-4G" stub at the start of the file (~78 functions, all tiny). The real 32-bit code is invisible.
- `winget` has **neither Ghidra nor radare2 nor IDA** in its standard repos.
- Tried `-loader LxLoader` to `analyzeHeadless` — that loader doesn't exist; the call hung waiting for stdin input.

### Paths forward (pick one)

| Option | Effort | Notes |
|---|---|---|
| **a. Write an LE → flat-binary extractor** | Small-medium | Parse the LE header, extract the 32-bit text section as a raw blob. Load into Ghidra as "raw binary, x86-32, base 0x10000". LE format is well-documented. ~200 lines of C# or Python. |
| **b. Find a third-party Ghidra LE extension** | Unknown | Issue #532 has no merged PR but somebody outside the official repo may have published one. Need to search GitHub thoroughly. |
| **c. Install radare2** | Medium | r2 supports LE format natively. Not in winget — would need manual download from `https://github.com/radareorg/radare2/releases`. |
| **d. Install IDA Free** | Medium | Hex-Rays publishes a free version. Supports LE. |
| **e. Build M-HT's SR.exe** | Medium-large | The lifter would produce the actual disassembled .asm. M-HT publishes source at `https://github.com/M-HT/SR/tree/master/SR`. Build with SCons on Linux. Once we have `SR.exe`, run it on `MAIN.EXE` → `Albion-main.asm`. |
| **f. Set up PyGhidra** | Medium | The `pyghidraRun.bat` exists but `analyzeHeadless` doesn't activate PyGhidra mode. User has `micromamba` available, suggested for setting up a Python env. PyGhidra is `pip install pyghidra`; then `pyghidra-analyze` works headlessly. *But this doesn't solve the LE-loader gap by itself.* |

**Recommendation:** option (a) — write the extractor. Smallest scope, fully self-contained, doesn't require installing anything else. The LE header format (Section 9.6 below) is small enough to parse in ~50 lines.

### LE format reference (for option a)

```
DOS MZ header at offset 0:
  +0x3C: e_lfanew (uint32) → offset of LE header

LE/LX header (at e_lfanew):
  +0x00: "LE" or "LX" magic
  +0x02: byte order, word order
  +0x04: format level
  +0x08: cpu type (0x02 = 80286, 0x03 = 80386, 0x04 = 80486)
  +0x0A: OS type
  +0x10: module flags
  +0x14: number of pages
  +0x18: EIP object number
  +0x1C: EIP offset (entry point)
  +0x20: ESP object number
  +0x24: ESP offset
  +0x28: page size (usually 4096)
  +0x40: object table offset (relative to LE header)
  +0x44: number of objects
  +0x48: object page table offset
  ...

Object table entry (24 bytes each):
  +0x00: virtual segment size
  +0x04: base virtual address (where to map this segment in memory)
  +0x08: object flags (READ/WRITE/EXEC)
  +0x0C: page table index (1-based)
  +0x10: number of pages in segment
  ...

Page table → maps to data pages
Data pages: the actual code + data
```

For Albion, we mostly need: code segment, mapped at its base virtual address, with the entry point set. That gives Ghidra a raw image it can disassemble.

## 10. Resources discovered

### freealbion wiki (high-value)

`https://github.com/freealbion/freealbion/wiki` documents file formats deeply but **gameplay mechanics are missing**. The TACTICO page (tactical combat) is literally "To be completed."

| Page | Content level | Notable |
|---|---|---|
| **PRTCHAR** | Comprehensive byte-by-byte | Decodes a lot of UAlbion's `Unknown*` fields. Key blocks still labelled `Unknown` in wiki: 0x06-0x08, 0x0C-0x10, 0x1C, 0x20-0x27, per-attribute 4-byte trailer (which UAlbion already maps to `Boost`/`Backup`), 0x6C-0x7D, 0x82-0x91, 0x96-0xC9, 0xCE-0xCF, 0xD8-0xD9, 0xDC-0xEF. |
| **MONCHAR** | Comprehensive | Monster char format. Wiki note: *"monster data does not affect the game in any noticeable way"* — monster behaviour is hardcoded, not data-driven. |
| **NPCCHAR** | Format only | |
| **SPELLDAT** | Just record format (5 bytes per spell, 7 schools × 30) | No per-spell effects documented. 210 spells total. |
| **MAPDATA (3D)** | Format | |
| **3DBCKGR / 3DFLOOR / 3DOBJEC / 3DOVERL / 3DWALLS** | Format | |
| **SAVEGAME** | Should cross-reference with UAlbion's serdes | |
| **ITEMLIST** | Item DB | |
| **MONGRP** | Monster groups | |
| **TACTICO** | "To be completed" | The page everyone wants. |

### freealbion source (`F:\Dev\albion\freealbion\src`)

36 C++ files, ~325 KB. Same depth as wiki — format readers, rendering, no combat / magic / AI logic. Useful files:

- `map/3d/map3d.cpp` (45 KB) — 3D map rendering with palette + lighting hooks
- `map/3d/labdata.cpp` (28 KB) — labyrinth data parser (might have decoded some `Unknown*` lighting fields)
- `map/2d/map2d.cpp` (19 KB) — 2D rendering
- `script/albion_script.cpp` (19 KB) — script event interpreter
- `ui/albion_ui.cpp` (17 KB)

### SR (`F:\Dev\albion\SR`)

M-HT's static recompilation infrastructure. **Lifted code is NOT here** — generated by running `SR.exe MAIN.EXE Albion-main.asm` from `SR-games/Albion/SR/build-x64.sh`.

What IS here:
- `games/Albion/SR-Main/` — 81 hand-written C files. Glue: DOS APIs, BBOPM graphics, AIL audio, music, BBDOS file I/O, 3D engine raster. Largest: `Albion-3dengine.c` (4910 LOC). Not gameplay logic.
- `games/Albion/SR/` — lifter configuration (.sci files, build scripts, architecture-specific .asm stubs).
- `SR/SR/` — the lifter itself (`SR_full.c`, `SR_basic.c`, etc.). Could be built on Linux.

See `_SR_INDEX.md` for the full file-by-file index.

### Ghidra at `F:\Dev\ghidra_12.1_PUBLIC\`

Works for analysis but **doesn't load LE-format binaries**. Headless invocation that DID work:

```
F:\Dev\ghidra_12.1_PUBLIC\support\analyzeHeadless.bat F:\Dev\albion\ualbion\_ghidra UAlbionRE \
  -import F:\Dev\albion\Albion\MAIN.EXE -overwrite \
  -scriptPath F:\Dev\albion\ualbion -postScript DumpFunctions.java
```

…but it only analyzed the 16-bit MZ stub. The real 32-bit LE code is invisible until we get the LE loader working.

### Other tools available

- **micromamba** at `C:\Users\user\AppData\Local\Microsoft\WinGet\Packages\Mamba.Micromamba_Microsoft.Winget.Source_8wekyb3d8bbwe` — for setting up Python environments. Useful if we go the PyGhidra route.
- **DOSBox 0.74-2** bundled at `F:\Dev\albion\Albion\DOSBOX\DOSBox.exe` — used once to extract the GOG `game.gog` ISO into `ALBION/CD/XLDLIBS/` via the `src/Tools/GOG_EXTR.bat` recipe.
- **7-Zip** at `C:\Program Files\7-Zip\7z.exe`. Cannot read MODE2/2352 CD images (that's why we needed DOSBox for `game.gog`).
- **dotnet SDK 9.0.314 + 8.0.416** installed.
- **Git Bash** + standard Unix tools (`grep`, `awk`, `find`, `head`, `tail`, `wc`, `iconv`).
- **PowerShell 5.1** + `winget v1.28.240`.

## 11. Save catalog (user's playthroughs)

| Slot | Map (ID) | 2D/3D | Save name |
|---|---|---|---|
| 001 | Drinno4 (147) | 3D | bluemusicok |
| 002 | Drinno3 (146) | 3D | dream2 |
| 003 | Jirinaar (110) | 2D | superb |
| 004 | SnirdArmoury (118) | ? | steap |
| 005 | Nakiridaani (200) | ? | dobra mag |
| 006 | Nakiridaani (200) | ? | dm2 |
| 007 | HunterClanCellar (123) | ? | Fight |
| 008 | HunterClanCellar (123) | ? | Postcomb |
| 009 | HunterClanCellar (123) | ? | Post2 |
| 010 | JirinaarTownHall (113) | ? | (unnamed) |
| 011 | Nakiridaani (200) | ? | out |
| 012 | Winion (132) | ? | hack |
| 100 | Jirinaar (110) | 2D | (unnamed) |

The "Fight"/"Postcomb"/"Post2" names suggest the user was testing combat scenarios — once combat is real, these are great regression fixtures.

## 12. Quick start for someone picking this up

1. Verify the baseline still works:
   ```powershell
   cd F:\Dev\albion\ualbion
   dotnet build -c Release src\UAlbion\UAlbion.csproj
   dotnet test  -c Release src\ualbion.ci.sln --logger 'console;verbosity=minimal' --nologo
   .\_smoke_all_saves.ps1
   ```
   Expect `347 / 347` + `Clean: 13 / 13`.

2. Read `_SESSION_STATUS.md` for the gameplay-feature status.

3. Read `_SR_INDEX.md` to understand the M-HT/SR infrastructure (and its limits).

4. Read `_RE_NOTES.md` to see the 365 Unknown fields catalogued.

5. **Pick the LE-loader path** (§9). Without it, the disassembly-driven phases (2, 3, 4, parts of 5) can't proceed. Recommendation: option (a), write an LE-extractor in ~200 lines.

6. Once disassembly is unblocked, work through tasks #16–#45 in `TaskList` in order. They're in dependency order within each phase, and the phases are mostly independent of each other (Phase 5/6 can start before Phases 2-3 are done).

## 13. Known-good interactive launch commands

```powershell
cd F:\Dev\albion\ualbion\build\UAlbion\bin\Release\net9.0
.\UAlbion.exe -d3d                      # default play
.\UAlbion.exe -d3d --menus              # with ImGui dev panels (asset/inspector/positions/etc.)
.\UAlbion.exe -d3d -c 'load_game 1'     # auto-load slot 1 then play
.\UAlbion.exe -d3d -c 'load_game 1' --trace 'F:\Dev\albion\ualbion\_trace.log'   # plus structured trace
.\UAlbion.exe -d3d --no-audio           # disable OpenAL if it complains
```

Caveats:
- `-d3d` is recommended over `-vk` (default). Vulkan throws a cosmetic `Swapchain's underlying surface has been lost` exception on window close.
- `soft_oal.dll` must be alongside `UAlbion.exe`. We copy it from `E:\Steam\steamapps\common\Stardew Valley\soft_oal.dll`.

## 14. Architectural quirks worth knowing

- **Camera position IS the party position in 3D** (`DungeonMap.cs:132`: `player.SetPositionFunc(() => _camera.Position / TileSize)`). Move the camera, the party "moves." This is why `Movement3D` raises `CameraMoveEvent` — it's how you move the party in 3D mode.
- **`PlayerEnteredTileEvent` is the 3D event-trigger pivot.** 2D fires it from `PartyCaterpillar.OnEnteredTile`; 3D fires it from `Movement3D.OnTick` when the rounded camera position crosses a tile.
- **Combat positions are absolute in the 5-row grid.** Mobs occupy rows 0-2 (positions 0-17), party occupies rows 3-4 (positions 18-29). `GameState.GetCombatPositionForPlayer` adds `CombatRowsForMobs * CombatColumns = 18` to translate the party-relative slot to absolute.
- **Object groups in tile content bytes are 1-based.** `contents == 0` → no NPC; `contents 1..N` → `ObjectGroups[contents - 1]`. The same convention applies to `MapNpc.SpriteOrGroup` (this was the bug fixed in §5 item 6).
- **`Effective` is a per-frame snapshot for party members, persistent for monsters.** Mutating `Effective.Combat.LifePoints.Current` directly is unreliable. Issue `DataChangeEvent` instead to update the underlying sheet via `SheetApplier`.
- **DOS-LE format is unusual.** Watcom/DOS4GW compiled, runs in 32-bit protected mode. `file` reports "MS-DOS executable, LE for unknown OS 0x1". Ghidra 12.1 needs help to load it.

## 15. Things I noticed but didn't fix

- `MapObject.cs:175` z-fighting workaround `subObject.ObjectInfoNumber * 0.0001f`. Cosmetic, not high priority.
- `MapObject.cs:184-185` division by `WallHeight` — could produce NaN if WallHeight=0; theoretically possible from corrupted data, never observed in practice.
- `OrthographicCamera.CalculateProjection` still uses `TryResolve<IEngine>` after my null-safe fix. Works now but the entire codepath assumes the engine eventually resolves; if it never does, the projection matrix never accounts for clip-space Y inversion. Subtle.
- `Movement3D.IsBlocked` — the user reported "no collisions" interactively after the fix. The direction math (`forwardVec = (sin(yaw), cos(yaw))`) matches `CameraMotion3D`'s `Vector3.Transform((eX, 0, eY), Q(yaw))`. But position rounding uses `Round` which flips to the next tile at the half-tile mark. **`Floor` may be more correct.** Worth investigating with a movement-emitting smoke test.
- The original Albion `Direction.North = 0`. My `Movement3D.OnTurn` maps it to yaw=0. Per Quaternion math `Transform((0,0,1), Q(0)) = (0, 0, 1)` = +Z. If world Y axis in tiles increases southward (`VerticalSpacing = TileSize * Vector3.UnitZ` in `DungeonMap.Setup`), then `yaw=0` actually faces *south*, not north. **Possible 180° error.** Cross-reference with the save dump where `PartyDirection=3` ("West" per Direction enum) and party_turn log says "party_turn West" — but the mapping is consistent because the convention is consistent.

## 16. Concrete next actions in priority order

1. **Unblock disassembly** — write an LE-format extractor (~200 lines C# or Python). Output: a raw 32-bit binary that Ghidra loads as "x86-32 raw, base 0x10000". Then re-run `DumpFunctions.java` to get the real function count (expect thousands).

2. **Once disassembled, locate combat round entry** by string cross-reference: the original game has visible strings like "%d points of damage", "missed", "critical hit", "AP" — find references to those, the calling functions are the combat-resolution routines.

3. **Decode hit / damage formulas** by tracing from those call sites. Document in `_RE_NOTES.md` per-formula.

4. **Lock down with golden traces:** run the original game (via `F:\Dev\albion\Albion\SR-Main.exe`) in a controlled scenario, observe combat outcomes. Then replicate in UAlbion + compare `_trace.log` outputs.

5. **Continue through Phase 2-3 task list** as documented.

6. **Parallel track (Phase 5):** 3D lighting + automap can proceed without disassembly using empirical experimentation against the running original.

## 17. Open questions

- Is the user willing to install **IDA Free** or **radare2** as an alternative to writing the LE extractor? Either would be faster than rolling our own.
- Should we attempt to **build M-HT's SR lifter** on Windows (via WSL maybe)? That'd give us machine-translated C as a *third* reference alongside Ghidra + wiki.
- Are there **community Albion modders** (GOG forums, Watcom-game communities) whose existing notes we should grab before doing our own RE work?
- Original game **observation harness**: do we want a screen-capture + state-extraction pipeline against the running DOSBox / SR-Main copy, so combat outcomes can be empirically logged?

---

End of handoff.
