# UAlbion — Iteration Session Status

> Rolling status. Updated 2026-06-12 (second pass) after the **gameplay-completeness session**.

## 2026-06-12 second pass — five gameplay systems landed

1. **Sub-tile 3D collision with wall sliding** — axis-separated margin checks (0.25-tile
   radius PLACEHOLDER), diagonal input slides along walls; CameraMove3DWorldEvent carries
   collision-filtered world-axis velocity. Verified: party stops at wall + 0.25, slides on
   diagonal input.
2. **Combat action pickers** — Move / Use magic / Use magic item fully wired:
   CombatActionPicker drives spell/item submenus + target-tile clicks into
   QueueCombatActionEvent (now carries SpellId/ItemId); Battle resolves Move (teleport
   PLACEHOLDER, original path-finds via fcn.00051b51), CastSpell (SP gating + consumption)
   and UseItem. Fixed: GameState.GetTarget crashed on PartyMember targets; ChangeStatus/
   ChangeItem/ChangeAttribute/etc script events were never routed to SheetApplier (map
   event chains silently did nothing!).
3. **NPC schedules** — 2D waypoint-following verified (Jiris + herd migrate with game
   time); 3D maps get live Npc3D entities (wander + waypoint walk; previously static
   props). GET /npcs endpoint for empirical verification. MTicksToday = 48/hour (1152
   slots/day) matches the waypoint arrays.
4. **All 39 named spells have effect handlers** — CombatBuffs (Berserk = the RE'd
   "powered" AP-doubling flag; Hurry/Boasting/MagicShield/PersonalProtection), combat
   traps/mines, Steal Life/Magic, Quick Withdrawal, anti-demon damage line, utility
   placeholders that trace their missing subsystem. Spells finally damage MONSTERS (battle
   HP-shadow callbacks); empty-tile casts retarget by SpellData.Targets (was self-nuking).
5. **3D automap (Phase 5.3)** — AutomapDialog composes the dungeon top-down from
   AutomapTiles gfx via MapData3D.AutomapGraphics (the original's wall→tile table),
   discovery radius marked on tile entry, persisted via SavedGame.Automaps (original-save
   blobs restore; their stride differs slightly so reads are bounds-safe). Toggle:
   show_automap event / "Map" in the 3D context menu.

Also: --dump --formats png fixed end-to-end (8268 PNGs, 21 categories, 0 failures) for
the user's upscaling work; GlobalResourceSetUpdater startup race fixed (dummy palette
seeding — was a reliable crash with -c load_game at boot).

### Verification (all green)
- **503 unit tests** (207 in Game.Tests), smoke **13/13**, explore gauntlet
  (load + 24h + encounter + 3 combat rounds × 13 saves) **0 failures**.

### Known PLACEHOLDERs for byte-exact follow-up (all marked in code)
| Area | What needs RE |
|---|---|
| Collision radius 0.25 | measure original under DOSBox |
| Combat Move | path-find + range via fcn.00051b51/fcn.00053871 |
| Buff magnitudes/durations | per-spell handlers in the deferred-action dispatcher |
| Spell damage/heal numbers | school cast-info blocks at 0x13e1a4+ |
| Automap tile selection | automap.c (debug strings retained at 0x13168c!) |
| 3D NPC walk speed / collision | original step rate not decoded |
| Light/MapView/Teleporter/Levitation/ViewOfLife | need light-override / automap-reveal / travel-UI subsystems |

## Still-pending gameplay work (next session targets)

| Item | Notes |
|---|---|
| ~~3D Select / Examine (Phase 6.4)~~ | DONE 2026-06-12: SelectionHandler3D ray-marches to the targeted wall/object tile, environment context menu fires zone chains. Also fixed inverted 3D forward movement (commit f6c56d38). |
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


