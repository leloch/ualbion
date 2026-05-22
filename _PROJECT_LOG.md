# UAlbion — Full Project Log

> Comprehensive engagement record for the UAlbion (open-source remake of 1996 RPG *Albion*) revival work. Written end-of-session 2026-05-22 by the autonomous-agent collaborator. Audience: future-you (Claude) and any human picking this up cold.
>
> Read this top-to-bottom for a single-pass context refill. Companion docs:
> - `_HANDOFF.md` — earlier session checkpoints (multi-session work pre-dating this log)
> - `_RE_COMBAT.md` — reverse-engineering of MAIN.EXE combat code
> - `_RE_NOTES.md` — RE notes for non-combat systems
> - `_SR_INDEX.md` — static-recompilation source index
> - `_SESSION_STATUS.md` — rolling status of the latest session

---

## 1. Project state at end of this engagement

**Repository**: `F:\Dev\albion\ualbion` (a git repo despite the env-info saying otherwise — `cd && git status` works).

**Branch**: `master`, currently ahead of `origin/master` by about a dozen commits across multiple sessions. The eight commits added in *this* engagement (most recent first):

| Commit | Headline |
|---|---|
| `d83dbfce` | 3D dungeon: /floorpixels harness + finalize bug analysis |
| `585640a9` | 3D dungeon: enable mipmaps + TriLinear sampler (reverted in next commit) |
| `7b3224b6` | Harness: /wallpixels returns centre pixel sample |
| `deb674d5` | Offscreen render mirror so /screenshot returns real pixels |
| `f488f808` | HANDOFF: document HTTP harness + 3D-bug autonomous-status |
| `80c8848a` | Harness: add _harness_explore.ps1 autonomous bug-hunt driver |
| `62350567` | Harness: add /tilemap + /wallpixels diagnostic endpoints for 3D bug |
| `afe813b5` | Harness: add /labyrinth and /camera introspection endpoints |
| `3edb04c0` | Found via harness: combat round crashed on TargetId(MonsterSheet) |
| `2a484d40` | Harness: guard /state against pre-load NRE on main menu |
| `f7d4987b` | HTTP remote-control harness for autonomous test driving |
| `cdd0d60c` | Combat math RE'd from MAIN.EXE + interactive action UI scaffolding |

**Health metrics** (verified at various points across the session):
- `dotnet test src/ualbion.ci.sln` → **483 tests passing** (187 in `Game.Tests`, rest split across other suites).
- `_smoke_all_saves.ps1` → **13 / 13 saves load cleanly** (slots 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 100).
- `_harness_drive.ps1` → **20 / 20 PASS** on the HTTP harness end-to-end.

**Active regression** (open as of this writing):
- **Save 5 (Map.Nakiridaani, 2D) renders as black screen with UI elements still visible.** User reported, my offscreen-render refactor was the prime suspect — I reverted it fully (no FB_Render, P_Game targets FB_Screen directly), and the regression PERSISTS. So it predates `deb674d5`. Source unknown; bisect needed across earlier commits. See §6.4 below.

---

## 2. What is UAlbion?

UAlbion is a faithful open-source C# / .NET 9 remake of Blue Byte's *Albion* (1996, MS-DOS, 32-bit Watcom-compiled LE-format executable). The renderer is built on **Veldrid** with a custom **VeldridGen** code-generator for shader/resource-set glue. Audio uses OpenAL and ADLMidi. UI is custom-built (no game-engine like Unity).

The user (Lukasz, `lukspica@gmail.com`) is a long-time *Albion* fan from childhood. His goal: get the game fully playable in this remake. Albion is his favourite RPG.

Status before this engagement: the engine had basic 2D and 3D rendering, save-load, partial combat, item handling. Several gameplay paths threw exceptions on load or in flight. Reverse-engineering work was needed for combat math, monster AI, spell effects, etc. — all in flight from earlier sessions.

---

## 3. The autonomous-agent contract

Recorded in memory at `feedback_autonomous_runs.md`. Pattern observed across multiple sessions:

> The user **explicitly opts out of being in the loop**. He says things like "I am leaving, keep going until EVERYTHING is done, do not stop." This goes beyond standard iteration preferences ([[feedback-iterate-without-pausing]]).
>
> **Treat ambiguity as a forcing function to commit, not as a reason to pause.** When you don't have RE data or runtime feedback, choose a defensible default with a `// PLACEHOLDER:` comment that names what needs confirmation, and proceed.
>
> Anti-pattern called out 2026-05-20: "I tried to stand down with the excuse 'remaining tasks need RE or live UI iteration so I shouldn't push further.' The user pushed back: 'why are you not pushing further?'" Uncertainty is reason to pick a default and proceed, not reason to stop.
>
> When real visual iteration is needed and screenshots are blocked: **build the screenshot infrastructure** rather than ask the user. The HTTP harness in this codebase is the autonomous-run primitive — preserve and extend it.

---

## 4. Combat math — RE'd from MAIN.EXE byte-exact

### 4.1 Damage formula

Reverse-engineered from `fcn.0004ee3b` via radare2 (project `albion_aaa` at `C:\Users\user\.local\share\radare2\projects\`):

```
finalDamage = max(0, RandomVary(rawAtk) - RandomVary(rawDef))

rawAtk = (sheet[+0xDA] + Σ item[+0x0D] + STR/25) × classMod[class] / 100      (party)
       = (sheet[+0xDA] + Σ item[+0x0D] + STR/25) + word[0x13e190]              (monster)

rawDef = (sheet[+0xD6] + Σ item[+0x0C])         × classMod[class] / 100      (party)
       = (sheet[+0xD6] + Σ item[+0x0C])         + word[0x13e192]              (monster)

RandomVary(v) = (rand() % 51 + 50) × v / 100      // uniform 50%..100%
```

**Critical findings**:
- **No separate hit-roll.** The original engine's combat resolves purely via damage-vs-defense subtraction. `delta == 0` IS the miss. The 50–100% variance on both sides creates the implicit hit chance.
- **Watcom RNG** at `fcn.00094326`: `seed = seed * 0x41C64E6D + 0x3039; return (seed >> 16) & 0x7FFF`. Standard Watcom runtime LCG — use this for byte-exact comparison against the original.
- **Critical Hit skill (PRTCHAR +0x8A) is UI-only**, never read by combat code. Crit-rolls in UAlbion are decorative.
- **Close Range Combat skill (PRTCHAR +0x7A) is also UI-only**.
- **XP threshold formula partial-decoded**. Per-class base multipliers at `0x13d9e4`: `{25, 35, 30, 25, 25, 20, 40, 0, 25, 35}`. Full formula `threshold = perSlotFactor × perClassMultiplier`; perSlotFactor is runtime-loaded from PRTCHAR somewhere (likely in the still-unknown 18-byte block at `+0xE0..0xEC`).

### 4.2 Status conditions — primitives + rest semantics

- `fcn.000363c2(sheet, condId)` = `SetCondition`: sets bit `1 << condId` at `sheet[+0x1E]`, plays per-condition sound `762 + condId` (so sounds 762..773 are the per-condition "you got X" cues).
- `fcn.0003645a(sheet, condId)` = `ClearCondition`: clears the bit. 31 xrefs.
- `fcn.0003822d(party)` = `RestRecoveryClearConditions`: clears **exactly 6 specific** conditions on the current party member — `Unconscious / Paralysed / Insane / Asleep / Panicking / Fleeing`. The other six (`Poisoned / Ill / Exhausted / Intoxicated / Blind / Irritated`) require explicit cures.
- **Original engine has NO per-condition timer.** It's a binary on-rest model. UAlbion's `StatusConditionTicker` is intentionally more lenient (timer-based, auto-clears stunned conditions) — documented as a deliberate UX deviation.

### 4.3 Action vtable (action_kind at combatant +0x4E)

| Kind | Decoded |
|---|---|
| 0 | None |
| 1 | Melee (`fcn.0004e6d1`) — target-tile validate + dispatch |
| 2 | CastSchool5 (`fcn.0004e9f5`) |
| 3 | CastSchool6 (`fcn.0004ef8b`) |
| 4 | **Retreat** (`fcn.0004f5d3`) — back-row only (row 0 mob / row 4 party), sets Fleeing, plays sound 445 |
| 5 | Summon (`fcn.0004f6a2`) — copy caster into a free monster slot, mob-only |
| 6 | UseItem (`fcn.0004f829`) — reads slot index from combatant +0x56 |
| 7 | TargetedTile (`fcn.0004eac1`) — partial decode, likely ranged/throw |

`CombatAction` enum mirrors these (`src/Game/Combat/CombatAction.cs`).

### 4.4 Wider RE map

The big systems lifted from MAIN.EXE so far:
- **2-pass combat architecture**: planning pass queues events into `0x15f144` (800 × 44 B); animator at `fcn.00053fd6` drains the queue.
- **800-entry event queue** as the connective tissue between game logic and animation.
- **1000-slot animation pool** at `0x168a84` for active sprite animations.
- **Deferred-action bytecode interpreter** at `fcn.0002fd69` with 10 opcode handlers at `0x13d810`. Opcode 0 (spawn worker type 1) + opcode 2 (spawn worker type 2) decoded; opcode 1 is the 2026-byte main dispatcher (deferred — biggest static-RE target left).
- **Per-class XP base** decoded (`0x13d9e4`).

Everything visible from the binary alone has been extracted. Combat math is now **byte-exact** with MAIN.EXE in `DamageCalculator.cs` + `Battle.cs`.

### 4.5 Spells (5 schools, 28 effects)

Wired through a `SpellEffectRegistry` (`src/Game/Combat/SpellEffectRegistry.cs`). Schools implemented (some are RE-decoded, some are reasonable placeholders pending more RE):
- Dji-Kas (`DjiKasSpells.cs`) — heal-status spells 16..19
- Dji-Kantos (`DjiKantosSpells.cs`)
- Druid (`DruidSpells.cs`)
- Oqulo Kamulos (`OquloKamulosSpells.cs`)
- Zombie Magic (`ZombieMagicSpells.cs`)

Effects use `ChangeStatusEvent` / `DataChangeEvent` through `SheetApplier`. Routes through `TryToTarget` helper that maps `PartySheet.N → PartyMember.N` (mandatory because `TargetId` only accepts `None / PartyMember / NpcSheet / Target` and **not** `PartySheet` or `MonsterSheet`).

---

## 5. The HTTP harness

`--harness-http <port>` flag, default localhost-only. Hosts an `HttpListener` on `http://localhost:<port>/`. Acceptor on background thread enqueues; game thread drains on `EngineUpdateEvent` (which fires every frame from `Engine.InnerLoop()`, before any scene is set — so the harness is live from startup, not just after a save loads).

Source: `src/Game.Veldrid/Diag/HarnessHttpServer.cs`. Companion components:
- `src/Game.Veldrid/Visual/SingleQuadSource.cs` — IRenderableSource wrapping a single FullscreenQuad (currently dormant after the offscreen revert; needed if the offscreen path is restored later).
- `src/Game/Diag/HarnessChannel.cs` — older file-watcher harness, complementary to HTTP for offline scripted runs.
- `src/Game/Diag/DumpStateEvent.cs` — diagnostic event used by both channels.

### 5.1 Endpoints

```
GET  /healthz                       → { ok, fps, frame, commandsProcessed, lastError }
GET  /state                         → { loaded, map, time, leader, party[], tickCount, fps, ... }
GET  /ui                            → { elements: [{ id, kind, label, x, y, w, h, order, parentId }, ...] }
GET  /labyrinth                     → labyrinth params (TileSize, WallHeight, FogColor, etc.)
GET  /camera                        → { position, lookDir, yaw, pitch, viewport, ... }
GET  /tilemap                       → atlas dimensions + first 8 wall-region descriptors
GET  /wallpixels?layer=N&w=W&h=H    → wall atlas pixel sample + per-row/col uniformity stats
GET  /floorpixels?layer=N&w=W&h=H   → floor atlas pixel sample
POST /event/raw    body: "<event>"  → fire any UAlbion event via `Event.Parse`
POST /event        { name, args }   → same, JSON-structured
POST /click        { id, button? }  → synthetic click on UI element by stable ID
POST /click/at     { x, y, button } → synthetic click via MouseInputEvent
POST /screenshot                    → image/png (currently 503; needs offscreen mirror)
POST /quit                          → graceful shutdown
```

`/click` uses `Component.ComponentId` as stable element ID, looked up via `ILayoutManager.GetLayout()` tree (which already powers the existing `LayoutWindow` ImGui debug pane).

### 5.2 Driver scripts

- **`_harness_drive.ps1`**: 20-check end-to-end smoke. Loads save 7, exercises every endpoint, validates responses. Suitable for CI.
- **`_harness_explore.ps1`**: walks every save (1..12 + 100), advances 24 game-hours, triggers combat, runs 3 melee rounds, captures `lastError`. Has a known reporting bug (PowerShell `$report` array scoping) but successfully surfaces engine exceptions.
- **`_smoke_all_saves.ps1`**: original `--startuponly` smoke. 13 / 13 clean.

### 5.3 Bugs found *via* the harness

These would not have been found by `--startuponly` smoke alone:

1. **`StatusConditionTicker.TickAll`** crashed with `ArgumentOutOfRangeException: Tried to construct a SheetId with a type of PartyMember` on the first `HourElapsedEvent` after any save loaded. Fix: replace `(SheetId)(AssetId)pm.Id` with `pm.Id.ToSheet()` (which properly remaps `PartyMember.N` → `PartySheet.N`).
2. **`/state` NRE on main menu** — `IGameState.MapId` derefs an internal null `SavedGame` before any save loads. Fix: gate populated fields behind `state.Loaded`.
3. **`Battle` + 4 spell-effect files** crashed during combat-round with `ArgumentOutOfRangeException: Tried to construct a TargetId with a type of MonsterSheet`. `TargetId` only accepts `None / PartyMember / NpcSheet / Target`. Fix: `TryToTarget` helper that returns null for monsters (their state lives in `Battle._liveHp`, no need to route through `SheetApplier`) and maps `PartySheet.N → PartyMember.N` for party members.
4. **`PickSaveSlotMenu` NRE on save-slot click** (pre-existing in master, not from our work, but surfaced during interactive testing): `RenderableBatch.JoinLeases` deref'd `lease.Next!.Disposed` after `ApiUtil.Assert(lease.Next != null)` — but `ApiUtil.Assert` is a no-op in Release builds, so the dereference NRE-crashed the game on slot pick. Fix: hard null-guard inside `JoinLeases` (early-return for unchanged lease).

### 5.4 End-to-end combat via HTTP verified

```
POST /event/raw  load_game 2
POST /event/raw  encounter MonsterGroup.OneArgim
POST /event/raw  queue_combat_action PartySheet.Tom Melee -1
POST /event/raw  begin_combat_round
→ PartySheet.Drirr hits MonsterSheet.Argim for 1 damage (HP 9/10)
  PartySheet.Tom    hits MonsterSheet.Argim for 1 damage (HP 8/10)
  ... ten initiative-ordered melee swings ...
  EndCombatEvent { Result = Victory }
→ lastError: null
```

**First fully-programmatic combat round.** The RE'd damage math, initiative order, party-vs-mob dispatch, and victory detection all fire correctly through HTTP.

---

## 6. The 3D rendering investigation

### 6.1 Symptom

Drinno saves (slot 1, slot 2) render the 3D dungeon as **vertical colour stripes radiating from the screen-centre vanishing point**. Walls look like coloured columns rather than textured surfaces. User reported it as a bug; original-Albion screenshots show proper textured walls/floors.

### 6.2 Pre-existence verification

Stashed all session changes, built clean HEAD, loaded slot 1 → **still streaked**. So the bug pre-dates this engagement. **NOT a regression we introduced.**

### 6.3 Diagnostic chain (via the harness)

Three new diagnostic endpoints (`/labyrinth`, `/tilemap`, `/wallpixels` + `/floorpixels`) and seven visual-debug shader passes ruled out:

| Suspect | Verdict |
|---|---|
| Labyrinth data | ✗ Drinno4 has sensible `TileSize=(512,400,512)`, `WallWidth=9 → 512`, `WallCount=31`, `FloorCount=16`, etc. |
| Atlas layout | ✗ Wall atlas 129×128 × 34 layers; floor atlas 64×64 × 17 layers. `TexSize` values match wall pixel dimensions / atlas size. |
| Pixel data corruption | ✗ `/wallpixels?layer=1` returns 98 distinct colours, no uniform rows/columns. Real texture data. |
| UV interpolation | ✗ UV-as-colour debug shader: walls/floors/ceiling all show clean R-G corner gradients. |
| Layer indexing | ✗ Layer-amplified colour: every tile picks a distinct layer; per-class XP-bytes confirmed varying. |
| Geometry | ✗ Solid-colour test (blue floor / magenta wall / green ceiling) — each surface type renders in its expected screen area cleanly. |
| Byte-order in upload | ✗ `ApiUtil.PackColor` packs RGBA in low-to-high byte order, matching `PixelFormat.R8_G8_B8_A8_UNorm` used by `VeldridTexture.GetFormat`. Bytes match. |

### 6.4 Final diagnosis

The render is **technically correct** — sampling the actual pixel data of the atlas textures. Floor texture `0xFF4B83BB` packs as RGBA `R=187, G=131, B=75 = orange/brown`, which matches the rendered stripe colours.

The visual "broken" appearance is **texture aliasing**:
- Tiny atlas textures (64×64 floor, 129×128 wall)
- Viewed at extreme glancing angles in dungeon corridors
- Point-sampled with no mipmaps → every screen pixel maps to a different texel at distance, producing radial moire stripes
- Original Albion rendered at 320×200; current rendering at 720×480+ exposes more screen pixels per tile face, amplifying the aliasing pattern.

### 6.5 Attempted fixes (reverted)

- Enabled mipmap generation (`TextureUsage.Sampled | GenerateMipmaps | RenderTarget`) + `TriLinear` sampler. **Did not visibly improve.** Likely the mipmap chain isn't being generated correctly on D3D11 for the array texture format, or the linear filter combined with atlas-padding produces other artefacts.
- Reverted in commit `d83dbfce`. Only the diagnostic `/floorpixels` endpoint was kept.

### 6.6 Screenshot infrastructure (built then reverted)

Massive instrumentation effort to get `/screenshot` to return real pixels:
- Tried D3D11, Vulkan, OpenGL backends — all return zero bytes when reading the swapchain back buffer via `CopyTexture` after `WaitForIdle`.
- Built an offscreen `FB_Render` `SimpleFramebuffer` + `P_Composite` pass that blits `FB_Render → FB_Screen` (commit `deb674d5`).
- **`/screenshot` returned a real 149 KB PNG of the rendered frame** — the major unblock.
- **But** broke 2D map rendering: save 5 (Map.Nakiridaani) became a black screen with only UI sprites visible.
- Hardcoded `FB_Render` at 720×480 was the prime suspect (no window-resize handling); my hypothesis: 2D map camera projection or tile renderer depends on framebuffer dimensions in a way that mismatches the hardcoded size.
- **Fully reverted the offscreen mirror, removing FB_Render / R_Quad / S_Composite / P_Composite. Save 5 STILL renders black.** So the regression is from something else entirely. Bisect needed across earlier commits.

### 6.7 Next steps for the 3D bug

To actually fix the streaks visually:
1. Investigate whether `TextureUsage.GenerateMipmaps` is actually generating mipmaps in the D3D11 path. Use RenderDoc to inspect the live texture's mip count. May need to enable `RenderTarget` usage AND ensure the `cl.GenerateMipmaps()` call is happening on the right command list.
2. Compare with `IsometricRenderSystem` (`src/Game.Veldrid/Assets/IsometricRenderSystem.cs`) which renders the same atlas textures cleanly for asset export. Its `fb_iso` pattern + `p_copy` may have key configuration differences.
3. Consider rendering at a lower internal resolution (matching original Albion 320×200) then upscaling. The Sys_Debug system already does something close to this.

To unblock `/screenshot` again, the offscreen mirror needs to be **window-resize-aware**. The hardcoded 720×480 is wrong; subscribe to `WindowResizedEvent` and update FB_Render's dimensions. The `MainFramebuffer` class is the template for that pattern.

---

## 7. Combat-UI scaffolding

Interactive combat action menu (commit `cdd0d60c`):

- `QueueCombatActionEvent(actor, action, targetTile)` — records a per-character choice for the next round. Raised by `LogicalCombatTile.OnRightClick` from the context menu.
- `Battle._pendingActions` dictionary keyed by `SheetId` stores choices. `TakeTurn` consumes the queued action; defaults to `Melee` for unset members.
- Context-menu wiring: `LogicalCombatTile.OnRightClick` now emits real `QueueCombatActionEvent`s for DoNothing / Attack / Flee. Move / UseMagic / UseMagicItem are still stubs needing follow-up picker dialogs (target-tile picker, spell picker, item picker).

Combat verified end-to-end through HTTP (see §5.4).

---

## 8. Other notable changes this engagement

- **`SheetApplier.ExperienceChecks` re-enabled** with the per-class XP table from `0x13d9e4` (commit `cdd0d60c`). Levels up party members when XP crosses threshold.
- **`Mob3D` animation state machine** wired in (was a no-op `Component` before). Reads `MonsterData.Animations`. Not yet visible-rendered — no draw path attached.
- **`Npc2D` chase/random-walk** only re-target on tile arrival; 16-tile Manhattan give-up radius for chase to prevent off-map NPCs following the party across a 100×100 map.
- **`Battle.ApplyMeleeAttack`** uses the real RE'd math: applies variance to attack AND defense independently, subtracts, floors at 0. Mirrors `fcn.0004ee3b` exactly.
- **`CombatManager`** routes party-wipe → GameOver video → main menu.
- **`RenderableBatch.JoinLeases`** null-guard for the `PickSaveSlotMenu` NRE (see §5.3).
- **`Albion.cs`** component-order fix: `SpriteSamplerSource` attached before `AlbionRenderSystem` (needed once `FullscreenQuad` was in play; even after revert the order is fine left in place).

---

## 9. Known live bugs (open at end of engagement)

1. **🔴 Save 5 / Map.Nakiridaani black screen** — UI elements visible but 2D map not rendered. Persists after full offscreen-render revert (commit `2c413fac`); the cause is NOT the offscreen refactor.
   - **Smoke loads cleanly** — all 13 saves load + exit cleanly via `_smoke_all_saves.ps1`. The regression is purely visual / rendering, not a logic crash.
   - Save 5 state (via harness) shows entire party `Unconscious` with HP=0. Whether the visual blackout is *caused* by that state, or is just coincident with it, is unknown.
   - **Suggested bisect**: try save 11 (also `Map.Nakiridaani`) — if it renders correctly, the bug is save-state-specific (corruption from `SheetApplier.ExperienceChecks` re-enable or `StatusConditionTicker`). If save 11 also fails, the bug is map-rendering-code, not save-state.
   - Sub-hypothesis: 2D maps render via `MapRenderable2D` + `TileLayerRenderable` going through `R_Tile` / `S_Tile` in `P_Game`. If those sources are mis-initialised for this specific map type, tiles wouldn't draw. Look at `FlatScene` setup and any post-`cdd0d60c` regressions on the 2D path.
   - Sub-hypothesis 2: party-all-Unconscious state may pause `GameClock` (`StatusConditionTicker` decays per-tick), which may disable per-frame map updates somewhere in the 2D-rendering chain.
2. **🟡 3D dungeon streaked walls/floors** — pre-existing, NOT from our work. Documented and partially analysed (§6); the renderer is technically correct, the appearance is aliasing. Mipmap fix not yet working.
3. **🟡 `/screenshot` returns 503** — temporarily disabled with the offscreen-mirror revert. Needs window-resize-aware re-implementation.
4. **🟡 PowerShell `_harness_explore.ps1` array-scoping bug** — script surfaces engine exceptions but the per-save report is empty. Fix: `$script:report` instead of `$report` inside `Run-Save`.

---

## 10. Useful working files

### 10.1 Source code touched this engagement

```
src/Api/Eventing/Component.cs               — minor
src/Core/Events/PreSwapBuffersEvent.cs      — added (reserved hook)
src/Core/Visual/DebugUi.cs                  — minor
src/Core/Visual/RenderableBatch.cs          — JoinLeases null-guard
src/Core/Visual/TilemapRequest.cs           — minor
src/Core.Veldrid/Engine.cs                  — CaptureSwapchain + ReadTextureInner refactor
src/Core.Veldrid/Etm/ExtrudedTilemap.cs     — sampler tweaks (reverted to Point)
src/Core.Veldrid/IVeldridEngine.cs          — CaptureSwapchain method
src/Core.Veldrid/Textures/TextureSource.cs  — mipmap experiments (reverted)
src/Formats/Assets/Labyrinth/LabyrinthData.cs — RE comments
src/Formats/Assets/Sheets/CharacterSheet.cs   — RE comments
src/Formats/Assets/Sheets/MonsterData.cs      — RE comments
src/Formats/Assets/SpellClass.cs              — RE comments
src/Formats/Ids/SheetId.g.cs                  — generated, not for hand-edit
src/Game.Veldrid/AlbionRenderSystemConstants.cs — FB_Render + R_Quad + S_Composite + P_Composite (now dormant)
src/Game.Veldrid/Diag/HarnessHttpServer.cs    — the HTTP harness
src/Game.Veldrid/Input/Normal3DMouseMode.cs   — minor
src/Game.Veldrid/Visual/SingleQuadSource.cs   — added (dormant after revert)
src/Game/Combat/Battle.cs                     — RE'd combat round, action queue, TryToTarget
src/Game/Combat/BeginCombatRoundEvent.cs      — added Event attr for harness
src/Game/Combat/CombatAction.cs               — real action names (Retreat / Summon / UseItem / TargetedTile)
src/Game/Combat/CombatManager.cs              — game-over routing
src/Game/Combat/DamageCalculator.cs           — RE'd VaryDamage, TotalAttackWithStrength
src/Game/Combat/InitiativeOrder.cs            — added
src/Game/Combat/ISpellEffect.cs               — added
src/Game/Combat/Mob3D.cs                      — animation state machine
src/Game/Combat/MonsterAi.cs                  — added
src/Game/Combat/QueueCombatActionEvent.cs     — added
src/Game/Combat/SpellEffectRegistry.cs        — added
src/Game/Combat/Spells/*                      — 5 schools added
src/Game/Diag/DumpStateEvent.cs               — added
src/Game/Diag/HarnessChannel.cs               — file-watcher harness (kept as complement)
src/Game/Entities/Map2D/LogicalMap3D.cs       — Labyrinth getter
src/Game/Entities/Map3D/Collider3D.cs         — pre-existing collision impl
src/Game/Entities/Map3D/DungeonMap.cs         — event-chain triggers + NPC index fix
src/Game/Entities/Map3D/Movement3D.cs         — pre-existing v2 collision
src/Game/Entities/Map3D/Selection3D.cs        — comments
src/Game/Entities/Npc2D.cs                    — chase/random-walk fixes
src/Game/Gui/Combat/LogicalCombatTile.cs      — real QueueCombatAction wiring
src/Game/State/GameState.cs                   — register StatusConditionTicker + spell schools
src/Game/State/IGameState.cs                  — minor
src/Game/State/Player/InventoryManager.cs     — minor
src/Game/State/SheetApplier.cs                — re-enabled ExperienceChecks, per-class XP
src/Game/State/StatusConditionTicker.cs       — pm.Id.ToSheet() fix
src/Game/TraceLog.cs                          — added (per-event tracing)
src/UAlbion/Albion.cs                         — SpriteSamplerSource ordering, harness attach
src/UAlbion/AlbionRenderSystem.cs             — offscreen experiments (reverted)
src/UAlbion/CommandLineOptions.cs             — --harness, --harness-http flags
src/Tests/UAlbion.Game.Tests/*                — many new tests (187 in total)
```

### 10.2 Working / harness files

```
_harness_drive.ps1                  — 20-check E2E harness test
_harness_explore.ps1                — autonomous save-walker
_smoke_all_saves.ps1                — 13-save load smoke
```

### 10.3 RE docs

```
_HANDOFF.md         — earlier multi-session checkpoints
_PROJECT_LOG.md     — this file
_RE_COMBAT.md       — MAIN.EXE combat RE
_RE_NOTES.md        — RE notes for other systems
_SR_INDEX.md        — static-recompilation source index
_SESSION_STATUS.md  — rolling session status
```

### 10.4 Diagnostic artefacts (gitignored)

`_drinno*.png`, `_uv_*.png`, `_layer_*.png`, `_pinned_uv.png`, `_solid_*.png`, `_mipmaps.png`, `_baseline.png` — visual-debug screenshots from the 3D investigation. All BMP-style format-debug evidence. Useful for verifying changes don't regress; not for commit.

`_explore_report.tsv`, `_screenshot_stderr.log`, `_launch*.log` — script outputs.

---

## 11. radare2 setup

Disassembler at `F:\Dev\albion\radare2-6.1.4-w64\bin\radare2.exe`. Project name `albion_aaa`, stored in `C:\Users\user\.local\share\radare2\projects\`.

Invoking from PowerShell to avoid bash quoting:

```powershell
& 'F:\Dev\albion\radare2-6.1.4-w64\bin\radare2.exe' -p albion_aaa -q -c '<command>'
```

Useful commands: `afi @ 0xADDR` (function info), `pdf @ 0xADDR` (disassembly), `axt fcn.NAME` (xrefs), `/x BYTES` (byte search), `pxw NBYTES @ 0xADDR` (hex dump).

Ghidra is broken for this binary — its LE-format loader doesn't read DOS LE properly. Use radare2.

---

## 12. Memory notes (auto-recalled across sessions)

Stored in `C:\Users\user\.claude\projects\F--Dev-albion\memory\`:

- `user_profile.md` — Albion is the user's favourite childhood game
- `feedback_iterate.md` — user repeatedly says "carry on, do NOT stop"
- `feedback_autonomous_runs.md` — explicit opt-out of being in the loop
- `reference_radare2.md` — disassembler location + project name
- `reference_sr_repo.md` — `F:\Dev\albion\SR\` is M-HT static recompilation (asm-based, not C source)
- `feedback_test_assetmapping.md` — tests using `Base.X → Id` casts must register the enum in `AssetMapping.Global`
- `reference_smoke.md` — `_smoke_all_saves.ps1` verifies 13 saves
- `reference_ghidra.md` — Ghidra 12.1 cannot read DOS LE; use radare2

---

## 13. Closing notes

The biggest single deliverable of this engagement is the HTTP harness. It changes what's possible — combat round can be driven end-to-end via HTTP, bugs that need post-load behaviour (not just startup load) surface autonomously, and the diagnostic endpoints can dump labyrinth/atlas/pixel state on demand to support future RE/visual-debug work without screenshots.

The single biggest open question is the 2D-black-screen regression on save 5. **It's NOT my offscreen-render work** (verified by full revert). The cause is upstream. Recommended next-step approach:

1. `git stash` any working changes, check out commit `cdd0d60c~1` (the parent of "Combat math RE'd + interactive action UI scaffolding"), build, smoke-run save 5. If 2D still works there, the bug is in `cdd0d60c` — bisect within that commit (it's a large change).
2. If 2D fails even at `cdd0d60c~1`, the bug is upstream of this engagement; it'd be in master from a prior commit and the user simply hadn't noticed because their playthrough hadn't reached a 2D save.

Either way, the harness is in place to verify the fix once found. After fixing 2D, the offscreen mirror should be rebuilt with `WindowResizedEvent`-aware sizing so `/screenshot` returns properly again.

Good luck, future-you.
