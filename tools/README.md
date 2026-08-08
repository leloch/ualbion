# Diagnostic and test-driving scripts

PowerShell drivers built on the HTTP harness (`UAlbion.exe --harness-http <port>`).
Each one launches the game, drives it over HTTP and asserts on observable state, so
behavioural changes can be checked against the running game rather than by inspection.

Run them from anywhere — paths are resolved relative to the script location, and the
game is expected at `build/UAlbion/bin/Release/net9.0/UAlbion.exe`.

| Script | Purpose |
|---|---|
| `smoke_all_saves.ps1` | Loads every save with `--startuponly` and asserts a clean exit. Baseline: 13/13. |
| `regression_e2e.ps1` | 25 end-to-end assertions, one per previously-fixed defect (progression, poison, combat, conversation, saves). |
| `mechanics_shakeout.ps1` | Broad sweep over inventory, economy, magic, combat, UI and persistence. |
| `harness_drive.ps1` | Basic harness smoke: launch, load, query state, click, quit. |
| `harness_explore.ps1` | Autonomous bug hunt: walks saves, advances time, triggers combat, captures errors. |
| `story_drive_lib.ps1` | Helper library (dot-source it) wrapping the harness for beat-by-beat story driving. |
| `narrative_shakeout.ps1` | Fires every map's event chains to crash-test story content. |
| `conversation_shakeout.ps1` | Drives NPC conversations and checks the dialogue flow. |
| `collision_sweep.ps1`, `allmaps_collision_sweep.ps1` | Probe 3D collision against the map data. |
| `save_behavior_sweep.ps1` | Per-save clock/movement liveness regression. |
| `fog_shot.ps1`, `minimap_shot.ps1`, `skybox_probe.ps1`, `skybox_turn.ps1`, `ui_fidelity_shot.ps1` | Screenshot comparisons for individual rendering features. |

Screenshots and logs are written to `_shots/`, `_smoke_logs/` and similar, all gitignored.
