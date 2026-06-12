# UAlbion — Iteration Session Status

> Rolling status. Updated 2026-06-12 after the **rendering-root-cause session**.

## 2026-06-12 session — BOTH headline rendering bugs fixed (one root cause)

### The big fix: SPIR-V optimization broke D3D11 (commit `0dc83ac3`)

The "3D striped walls" (Drinno) and "2D black map" (Nakiridaani/Winion) bugs shared one
root cause: `ShaderCache` compiled GLSL→SPIR-V **with optimization in Release builds**:

1. The optimizer strips never-referenced resource declarations. Veldrid.SPIRV assigns
   D3D11 registers from surviving declarations only, but Veldrid binds by the full C#
   resource-set layout. `ExtrudedTileMapSF`'s palette declarations are unused (dead
   `#ifdef USE_PALETTE`), so `DayFloors`/`DayWalls` shifted to t0/t1 — the palette
   texture slots. **The 3D dungeon was rendering the palette texture** (vivid 1-px
   columns = palette entries). `BlendedSpriteSF` (2D day/night map layers) and `MeshSF`
   had the same shift → 2D maps drew the palette (rainbow) or discarded to black.
2. Even with correct registers (TilesSF references everything), optimized SPIR-V made
   D3D11 structured-buffer reads return zeros (`Map[]` empty → tile ids all 0 → black).

Debug builds compiled without optimization — which is why developers never saw it.
Vulkan was always correct. Diagnosis chain that cracked it: working `/screenshot` →
UV-probe shaders → CPU atlas PNG dumps (perfect) → GPU texture readback (perfect after
cache poke) → cached `.hlsl` register inspection (wrong) → Debug-vs-Release A/B.

**Fixes**: ShaderCache always compiles with debug options; keep-alive blocks in the three
shaders guard against re-enabling optimization. **Never re-enable SPIR-V optimization.**
Shader cache (`%LOCALAPPDATA%\ualbion\ShaderCache`) hash covers GLSL content only — clear
it manually if compile options ever change.

### Other fixes this session

- **EventExchange detached-handler guard** — components detached mid-broadcast by an
  earlier handler no longer receive the event (NRE crash in `SkyboxRenderable` when
  `load_game` tore down the old map during `EngineUpdateEvent`). `IComponent.IsSubscribed`
  added.
- **LogicalMap2D out-of-range tile ids** — `GetUnderlay`/`GetOverlay` now bounds-check
  against `TileData.Tiles.Count`; corrupt cells (e.g. `ChangeUnderlay 0` → 0xFFFF) no
  longer crash NPC pathing (`CollisionManager.IsOccupied` AOORE on save 4).
- **Offscreen render mirror re-applied, resize-aware** — `P_Game → FB_Render`,
  `P_Composite → FB_Screen`; `C_WindowUpdater` resizes `FB_Render` with the window.
  `/screenshot` works on all backends. (The old "mirror broke 2D" suspicion was wrong —
  that was the shader bug.)
- **`party_move_3d` text-parseable** — harness/console can drive 3D movement; verified
  forward/backward/strafe + collision blocking + noclip via HTTP.
- **Harness upgrades**: `/wallpixels|/floorpixels?format=png` (CPU atlas layer as PNG),
  `/gpuwallpixels|/gpufloorpixels?layer=N` (live GPU texture readback), `/camera` works
  on 2D scenes (resolves via `ICameraProvider`).
- **`_harness_explore.ps1` report fixed** (`$script:report`) — full gauntlet now reports.

### Verification state (all green)

| Check | Result |
|---|---|
| Unit tests | **483 / 483** |
| Smoke (13 saves, `--startuponly`) | **13 / 13 clean** |
| Harness E2E (`_harness_drive.ps1`) | **20 / 20 PASS** |
| Explore gauntlet (load + 24h + encounter + 3 combat rounds × 13 saves) | **0 failures** |
| Visual sweep | **All 13 saves screenshot-verified rendering correctly on D3D11 Release** |

### Map-type correction (save catalog)

Jirinaar (110), HunterClanCellar (123), Drinno (146/147) are **3D**; Nakiridaani (200),
Winion (132), JirinaarTownHall (113), SnirdArmoury (118) are true 2D. (Earlier docs
mislabelled Jirinaar/HunterClanCellar as 2D.)

## Still-pending gameplay work (next session targets)

| Item | Notes |
|---|---|
| 3D Select / Examine (Phase 6.4) | `DungeonMap.Select` still TODO |
| 3D automap (Phase 5.3) | not started |
| Combat UI pickers | Move / UseMagic / UseMagicItem stubs in `LogicalCombatTile` |
| More spell effects | 28 registered; grind through remaining schools via `_RE_COMBAT.md` |
| NPC schedules (Phase 4) | waypoint data parsed, tick application unverified |
| Movement polish | 3D collision is tile-granular (blocks ~0.5 tile early vs original's margin) |
| Vulkan close exception | cosmetic `Swapchain surface lost` on window close |

## How to iterate

```powershell
cd F:\Dev\albion\ualbion
dotnet build -c Release src\UAlbion\UAlbion.csproj   # ~2-6s
dotnet test -c Release src\ualbion.ci.sln --logger 'console;verbosity=minimal' --nologo  # 483 tests
.\_smoke_all_saves.ps1                                # 13/13
.\_harness_drive.ps1                                  # 20/20
powershell -File _harness_explore.ps1 -LaunchWaitSeconds 14   # full gauntlet + _explore_report.tsv

# Visual verification (the workflow that cracked the rendering bugs):
Start-Process -FilePath 'build\UAlbion\bin\Release\net9.0\UAlbion.exe' -ArgumentList '-d3d','--mute','--harness-http','7878' -WorkingDirectory 'build\UAlbion\bin\Release\net9.0'
Invoke-RestMethod -Method POST http://localhost:7878/event/raw -Body "load_game 1" -ContentType text/plain
Invoke-WebRequest -Method POST http://localhost:7878/screenshot -OutFile _shot.png
```
