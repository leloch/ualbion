# CLAUDE.md — UAlbion working guide

Operational index for working on **UAlbion**, an open-source C# / .NET 9 remake of the 1996 Blue Byte RPG *Albion* (MS-DOS). Read this first; it points to the deeper docs.

> **Companion docs (read when relevant):**
> - `_PROJECT_LOG.md` — full narrative engagement record (combat RE, harness, 3D-bug analysis, known regressions, next steps). **Read this for deep context.**
> - `_HANDOFF.md` — earlier multi-session checkpoints.
> - `_RE_COMBAT.md` — reverse-engineering of MAIN.EXE combat code.
> - `_RE_NOTES.md` — RE notes for non-combat systems.
> - `_SR_INDEX.md` — index of the M-HT static-recompilation source (`F:\Dev\albion\SR\`, asm-based, not C).
> - `_SESSION_STATUS.md` — rolling status of the latest session.

---

## What this is

A faithful remake of *Albion*. Renderer is **Veldrid** (D3D11/Vulkan/OpenGL) with a custom **VeldridGen** source generator for shader/resource glue. Audio: OpenAL + ADLMidi. UI is hand-built (no Unity/engine). Needs original game data (from GOG install) layered through the `mods/` asset system.

The maintainer is reviving the game to be fully playable. Active work areas: combat (reverse-engineered from MAIN.EXE), spell system, 3D dungeon rendering, an HTTP test harness for autonomous driving.

## Build / test / run

Repo root: `F:\Dev\albion\ualbion`. **It is a git repo** (despite some tooling reporting otherwise — `cd F:\Dev\albion\ualbion && git status` works). Default branch `master`.

```bash
# Build the game (Release)
dotnet build src/UAlbion/UAlbion.csproj -c Release      # run from F:\Dev\albion\ualbion

# Run full test suite (~483 tests)
dotnet test src/ualbion.ci.sln

# Run just the game tests (fast, 187 tests)
dotnet test src/Tests/UAlbion.Game.Tests/UAlbion.Game.Tests.csproj

# Run the game (launch as a real Windows process, not via Git Bash — focus issues otherwise)
# PowerShell:
Start-Process -FilePath 'F:\Dev\albion\ualbion\build\UAlbion\bin\Release\net9.0\UAlbion.exe' `
  -ArgumentList '-d3d','--harness-http','7878' -WorkingDirectory 'F:\Dev\albion\ualbion\build\UAlbion\bin\Release\net9.0'
```

**Build gotcha:** the running `UAlbion.exe` locks its DLLs. If a build fails with `MSB3021 ... file is locked by UAlbion`, kill the process first: `Get-Process UAlbion* | Stop-Process -Force`.

### CLI flags worth knowing
- `-d3d` / `-vk` / `-gl` — graphics backend (Direct3D11 / Vulkan / OpenGL).
- `--mute` — no audio.
- `-c "<event>;<event>"` — fire script events at startup, e.g. `-c "load_game 7"`. Same syntax as the harness `/event/raw`.
- `--harness-http <port>` — HTTP remote-control harness (see below).
- `--trace <path>` — structured event trace log.
- `--startuponly` / `-s` — exit after first frame (used by smoke).

### Verification scripts (repo root, PowerShell)
- `_smoke_all_saves.ps1` — loads all 13 saves with `--startuponly`, asserts clean exit. **Baseline: 13/13.**
- `_harness_drive.ps1` — 20-check end-to-end harness smoke (launch → load → state/ui/click → quit). **Baseline: 20/20.**
- `_harness_explore.ps1` — walks every save, advances time, triggers combat, captures errors (autonomous bug hunt).

## Architecture

### Project layering (dependencies point downward)
```
UAlbion            (entry point: Albion.cs wires every component; AlbionRenderSystem.cs builds the render graph)
  └─ Game.Veldrid  (Veldrid-specific game code: renderers, HarnessHttpServer, asset loaders)
       └─ Game     (game logic: Combat, State, Entities, Gui, Scenes — backend-agnostic)
            ├─ Core / Core.Veldrid  (engine: eventing, rendering primitives, textures, framebuffers)
            └─ Formats (asset formats, MapEvents, Ids, Sheets)
                 ├─ Base    (generated enum IDs from game data)
                 ├─ Config
                 └─ Scripting
       Api          (foundation: eventing, visual primitives — no deps)
```
Tests live in `src/Tests/UAlbion.<Project>.Tests/`. The main game test project is `UAlbion.Game.Tests`.

### The eventing system (the central pattern — understand this first)
Everything is a `Component` (`src/Api/Eventing/Component.cs`) attached to a tree under a global `EventExchange`. Components communicate **only** via events, not direct references.

- **Subscribe**: `On<TEvent>(handler)` in the constructor.
- **Fire (synchronous)**: `Raise(new SomeEvent(...))` — runs handlers immediately on the current thread.
- **Fire (deferred, thread-safe)**: `Enqueue(new SomeEvent(...))` — queued, flushed on the game thread next frame. **Use this from background threads** (`Raise` is not thread-safe).
- **Resolve a service**: `Resolve<IFoo>()` (throws if missing) or `TryResolve<IFoo>()` (null if missing).
- Events are plain records/classes tagged `[Event("name", "description", "alias")]`; the attribute makes them parseable from text (the `-c` flag, the console, and the HTTP harness all use `Event.Parse`).

`EngineUpdateEvent` fires every frame from `Engine.InnerLoop()` — before any scene is set up, so per-frame work is live from startup. `FastClockEvent` only fires once `GameClock` runs (i.e. after a save loads).

### Rendering
`AlbionRenderSystem` (`src/UAlbion/AlbionRenderSystem.cs`) builds two render systems: `Sys_Default` (fullscreen) and `Sys_Debug` (ImGui dev UI, renders the game to an offscreen `FB_Game` then composites). `P_Game` collects renderable sources (`S_Sprite`, `S_Etm` for 3D tilemaps, etc.) and draws to `FB_Screen` (the swapchain). 2D maps go through `R_Tile`/`S_Tile`; 3D dungeons through `R_Etm`/`S_Etm` (extruded tilemap).

### Assets / mods
`mods/` is a layered asset system. `Base` = raw game data conversion; `Unpacked`/`Repacked` = working formats; `Shaders` = GLSL (`.vert`/`.frag`, with VeldridGen `.h.*` generated headers); `UATest` = test fixtures + saves. Original game data must be present.

## The HTTP harness (key tool for driving the game)

`--harness-http <port>` hosts a localhost HTTP server (`src/Game.Veldrid/Diag/HarnessHttpServer.cs`). Acceptor thread enqueues requests; game thread drains them on `EngineUpdateEvent`. Lets you (or CI) drive the game without a human at the keyboard.

```
GET  /healthz                       → { ok, fps, frame, lastError }
GET  /state                         → { loaded, map, time, leader, party[], ... }
GET  /ui                            → visible UI elements [{ id, kind, label, x,y,w,h }]
GET  /labyrinth /camera /tilemap    → 3D-render diagnostics
GET  /wallpixels?layer=N /floorpixels?layer=N → atlas pixel sampling
POST /event/raw   body "load_game 7"→ fire any UAlbion event (text form)
POST /event       { name, args }    → same, JSON form
POST /click       { id, button? }   → click a UI element by stable id (from /ui)
POST /click/at    { x, y, button }  → synthetic mouse click at coords
POST /screenshot                    → PNG (currently 503 — see Known issues)
POST /quit                          → graceful shutdown
```
Example (PowerShell): `Invoke-RestMethod -Method POST http://localhost:7878/event/raw -Body "load_game 7" -ContentType text/plain`

Combat can be driven fully through HTTP: `load_game 2` → `encounter MonsterGroup.OneArgim` → `queue_combat_action PartySheet.Tom Melee -1` → `begin_combat_round`.

## Conventions & gotchas

- **`SheetId` / `TargetId` casts are type-restricted.** `TargetId` only accepts `None / PartyMember / NpcSheet / Target` — **not** `PartySheet` or `MonsterSheet`. To route a combatant through `SheetApplier`, map `PartySheet.N → PartyMember.N` (see `Battle.TryToTarget`) and skip monsters (their state lives in `Battle._liveHp`). The naive `(TargetId)(AssetId)x.SheetId` cast throws at runtime.
- **`ApiUtil.Assert` is a no-op in Release builds.** Never rely on it to guard a dereference — add an explicit null-check too.
- **Reverse-engineering**: disassembler is radare2 at `F:\Dev\albion\radare2-6.1.4-w64\bin\radare2.exe`, project `albion_aaa`. Invoke from PowerShell to dodge bash quoting: `& '...\radare2.exe' -p albion_aaa -q -c '<cmd>'`. **Ghidra cannot read this DOS LE binary — use radare2.**
- **Placeholders**: when RE data or runtime feedback is unavailable, pick a defensible default and mark it `// PLACEHOLDER:` naming what needs confirmation. Don't block on uncertainty.
- **Tests using `Base.X → Id` casts** must register the enum in `AssetMapping.Global` or they throw.
- **Commit messages**: end with the Co-Authored-By trailer. Don't push or commit unless asked.

## Combat (reverse-engineered from MAIN.EXE — byte-exact)

`DamageCalculator.cs` + `Battle.cs` implement the real formula:
```
finalDamage = max(0, RandomVary(rawAtk) - RandomVary(rawDef))
RandomVary(v) = (rand()%51 + 50) * v / 100      // uniform 50%..100%
```
**No separate hit-roll** — `delta == 0` is the miss. Watcom RNG: `seed = seed*0x41C64E6D + 0x3039; (seed>>16)&0x7FFF`. Crit/Close-Range skills are UI-only. Full detail + the action vtable, status-condition primitives, spell schools, and XP curve are in `_RE_COMBAT.md` / `_PROJECT_LOG.md §4`.

## Known issues (as of latest session)

All three legacy rendering issues were **FIXED 2026-06-12** (commit `0dc83ac3`) — single root cause: the optimizing GLSL→SPIR-V compile in Release builds. The optimizer stripped unused resource declarations (shifting every later D3D11 register off the slots Veldrid binds — the 3D dungeon was rendering the *palette texture* as walls) and broke D3D11 structured-buffer reads (2D `Map[]` read as zeros → black 2D maps). `ShaderCache` now always compiles with debug options. **Never re-enable SPIR-V optimization**; if shaders ever regress to rainbow stripes / black maps, compare `%LOCALAPPDATA%\ualbion\ShaderCache\*.hlsl` registers against the C# resource-set layouts and clear that cache (its hash covers GLSL content only, not compile options).

`/screenshot` works again via the resize-aware `FB_Render` offscreen mirror. New diagnostics: `/wallpixels?format=png` (CPU atlas layer as PNG), `/gpuwallpixels?layer=N` (GPU texture readback). Note the original 2D-black-screen suspicion of the offscreen mirror was wrong — it was the shader bug all along.

Combat presentation gotchas (2026-06-12): the combat backdrop MUST render through the UI sprite path (`UiFixedPositionElement`) — a world-space `Sprite` at any DrawLayer is covered by the map render passes, and `ShowMapEvent(false→true)` does not restore the map cleanly. UI integer scale is `floor(min(w/360, h/240))`: a 720×**479** window renders the UI at 1× — use `e:window_size 720 480`. Chasing MonsterGroup NPCs trigger contact combat on reaching the party (2D + 3D); save 2 auto-starts its Warniak fight on load — don't mistake point-blank monster billboards for texture corruption (they look like giant pixel blobs).

Map-type correction: Jirinaar (110) and HunterClanCellar (123) are **3D** maps (Albion cities/dungeons are first-person); only Nakiridaani (200), Winion (132), JirinaarTownHall (113), SnirdArmoury (118) of the user's saves are true 2D.

## Where things live

| Area | Path |
|---|---|
| Entry point / DI wiring | `src/UAlbion/Albion.cs` |
| Render graph | `src/UAlbion/AlbionRenderSystem.cs` |
| CLI flags | `src/UAlbion/CommandLineOptions.cs` |
| Combat logic | `src/Game/Combat/` (`Battle.cs`, `DamageCalculator.cs`, `Spells/`) |
| Game state / save data | `src/Game/State/` (`GameState.cs`, `SheetApplier.cs`) |
| 2D / 3D map entities | `src/Game/Entities/Map2D/`, `Map3D/` |
| GUI | `src/Game/Gui/` |
| HTTP harness | `src/Game.Veldrid/Diag/HarnessHttpServer.cs` |
| Eventing core | `src/Api/Eventing/` (`Component.cs`, `EventExchange.cs`) |
| Asset formats / events | `src/Formats/` |
| Generated enum IDs | `src/Base/` |
| Shaders | `mods/Shaders/Shaders/*.vert` `*.frag` |
