# UAlbion — Iteration Session Status

> Rolling status. Updated 2026-06-12 (third pass) after the **presentation + RE-fidelity session**.

## 2026-06-12 third pass — combat presentation, RE-confirmed formulas, QoL

Commits b4af91d1..bc21f8b1. All green throughout: 503 tests, smoke 13/13.

1. **T2.7 utility spells** — Light raises the ETM ambient level (`ambient_light` event →
   MapRenderable3D → uAmbient in ExtrudedTileMapSF.frag, regenerate headers via
   `dotnet run --project src/Tools/ShaderWriter -- <shader dir>`); MapView = full automap
   reveal + open (`reveal_automap` + `show_automap`). Verified live: ambient 40 dims the
   scene to exactly 0.4× measured brightness.
2. **T2.5 animated tactical combat** — one round per Start Round click (was: whole battle
   auto-resolved); per-turn playback with WallClockTimerEvent pacing, active-combatant
   highlight, red/green damage flashes + overlaid damage numbers (LayerStacker+SimpleText
   in VisualCombatTile), monsters leave the grid when killed. The painted combat backdrop
   finally displays (UiFixedPositionElement through the UI sprite path — a world Sprite
   at any layer is covered by the map render passes). At 720×480 the combat screen matches
   the original layout (NB window heights <480 drop UI to 1× integer scale).
3. **T2.6 RE punch-list — all decoded and applied** (see _RE_COMBAT.md "Punch-list RE"
   and _RE_NOTES.md "automap.c"/"Sound id space"):
   - Automap discovery = facing wedge (depth 10) + flood fill with corner occlusion;
     sight-block = wall flags bit 0x04 (WriteOverlay). Implemented in AutomapDialog.
   - Spell magnitudes = max(1, M·K/100), M = max(1,(mastery+50)/100) from per-spell
     mastery 0..10000. Confirmed K table applied; heals are % of target max LP.
     Blinding line = Blind only; Shock/Boasting inflict Panicking; Hurry = AP×2 flag;
     HealParalysis is a NULL handler in the original (kept 1:1 as a no-op).
   - Combat Move: range clamp(Speed/30,1,3) Chebyshev, destination-only occupancy,
     row restrictions. XP: pool monsterSheet.ExperienceReward (offset 0x20), victory
     splits max(1,total/living). Monster AI mask Magic/Melee/Ranged with the verbatim
     16-entry weight table; monsters now cast spells. 3D collision margin = min(T/4,50)
     world units (≈0.098 tile). "Sounds 443/444/445" are actually SYSTEXTS combat
     messages — now shown in the status bar during playback (+condition lines 762-773).
4. **Monster contact combat** — chasing MonsterGroup NPCs trigger EncounterEvent on
   reaching the party (2D + new 3D chase movement); victory raises npc_off. Save 2 now
   auto-starts the Warniak fight it was saved during.
5. **T3.10 QoL** — map-entry autosave (slot 98), Ctrl+F5/Ctrl+F9 quicksave/load (slot 99),
   Vulkan window-close crash fixed (bail out of InnerLoop when _done set during PumpEvents),
   `e:window_size <w> <h>` event.
6. **T3.9 partial** — CombatAudio plays hit/miss/kill/heal samples (PLACEHOLDER named
   samples; real combat SFX are per-monster WAVELIB entries via AIL, not yet RE'd).

### Fourth pass (same day) — RPG service layer + playthrough verification

Commits 767c05f9..d3e272ed. Explore gauntlet 65/65 (first-ever full pass), tests 503/503.

7. **Gauntlet crash fixes** — level-up NRE (null SpellPoints), round playback re-entrancy
   (battle detached mid-await), Raise()-skips-sender combat-end leak (natural victories
   never ran Battle's own cleanup), PartyMember→SheetId cast crash on leader death.
   AlbionTaskBuilder.SetException now preserves stack traces.
8. **Faithful combat SFX** — melee is SILENT in the original; death = sample 268 pitch-
   varied per side; per-spell hardcoded sample sequences wired (CombatAudio.CastSamples).
9. **Conversation item-query** (was a literal TODO): ConversationItemWindow picker +
   AskAboutItem chains (block = item class / 255 wildcard). Verified: Garris responds to
   the Tharnoss permit with his real sea-voyage quest line.
10. **Out-of-combat casting**: PartyMagicMenu (portrait → Use magic was a null stub) —
   environment-filtered spell list, member targeting, mastery-scaled resolution, SP cost,
   cast SFX. Events magic_menu / cast_spell.
11. **NPC services** (PlaceActionEvent had NO handler — trainers/healers/taverns were
   silent no-ops): Heal (gold/LP), Cure, SleepInRoom (RestEvent 8h), OrderFood,
   LearnCloseCombat trainer (1 TP + gold/point). Real prices from event-set data
   (Rejira 2 g/LP, Zirr room 12/food 5, Ferina 40 g/point). dump_eventset diagnostic.
12. **Contact dialogue**: human ChaseParty NPCs start their conversation on touch
   (Anne Dorbeck fetch beat in the intro); monster give-up radius no longer applies to
   scripted human chasers. New game verified through the cabin monologue + Anne contact.

### Open items
- Toronto intro playthrough beyond Anne contact (shuttle launch → crash → Nakiridaani):
  agent driving it, report lands in _PLAYTHROUGH.md.
- PlaceAction types LearnSpells / RepairItem / RestoreItemEnergy / RemoveCurse /
  ScrollMerchant / AskOpinion are logged placeholders.
- Combat battle-view animations (Mob3D state machine not rendered) — biggest remaining
  "feels like Albion" gap.
- World-interaction verbs (examine/use/take on map objects), readable items, ammo,
  torch lighting, ActiveSpells durations, world-map pathfinding.
- Automap RENDERING still uses fixed glyphs (original: connection-mask wall glyphs at
  0x230+mask, floor-texture minis, gated markers — documented in _RE_NOTES.md).
- DumpJson.cs:46 NREs when dumping event sets via --dump.

---

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


