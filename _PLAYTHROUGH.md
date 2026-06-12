# Intro Playthrough Report — New Game → Crash Site → Hunter Clan (2026-06-12)

Driven end-to-end via the HTTP harness (`--harness-http 7878`), D3D11, Release build.
Screenshots: `_pt_*.png` (repo root). Game stdout: `_pt_stdout.log` / `_pt_stdout2.log`.

## Beat-by-beat status

| # | Beat | Status | Evidence / Notes |
|---|------|--------|------------------|
| 1 | `new_game Map.TorontoBegin 31 76` → Tom's cabin monologue | **WORKS** | `_pt_01_intro_monologue.png` — "That crazy dream again…". Chain `Map.TorontoBegin:2` fires, autosave 98 written. |
| 2a | 2D movement, cabin door | **WORKS** | Door at (38-39,70) opens via `verb Examine` zone (`change_icon` + `Sample.HiTechHiss2`). Movement needs running clock. |
| 2b | Crew conversations (13 NPCs: Christine, Rainer, Shaw, Akira, Michelle, Anne, Alice, Ashley, Brandt, Ned, Joe, Priver) | **WORKS (after 2 fixes)** | All open, show unique greetings, answer Profession ("I'm a physicist and xenobiologist…" `_pt_11`), word queries (Word.Flight `_pt_13`), and exit cleanly via Farewell. Two bugs found & fixed (below). |
| 2c | Topic-discovery / "What do you know about…?" | **WORKS** | Topics window lists discovered keyword "flight" + Enter-word box; clicking topic fires `action Word 0 Word.Flight` chain. `_pt_12/13`. |
| 3a | Tom-meets-Christine scripted scene (zone 38-40,68 → `Script.TomMeetsChristine_300`) | **WORKS** | Full 11-line scene incl. Snoopy-exploded exposition, NPC pathing (npc_jump/npc_move), Christine walks away. `_pt_35..37`. |
| 3b | PA announcements (EveryStep `Ticker.Ticker142` counter) | **WORKS** | "Shuttle prepared for launch!" then "Please report for launch of the exploratory flight." `_pt_43`. |
| 3c | Environment interactions (Examine/Take context menu) | **WORKS** | Right-click tile → Environment menu → Take removes item + `change_icon`. `_pt_41/42`. |
| 3d | Launch prompt → `Script.ShuttleDeparture_300` | **WORKS** | "Report for launching the shuttle?" Yes/No (`prompt_player 168`, `_pt_44`); hangar scene with Capt. Brandt, Hofstedt, android-Ned; full briefing dialogue; map → `Map.FlightToAlbion`. |
| 4a | Flight + radio chatter + systems failure + crash dialogue | **WORKS (text/audio), PARTIAL (visuals)** | All ~25 dialogue beats play, incl. garbled Toronto radio. `Video.ApproachToAlbion` FLIC plays (72 frames). **But** the cockpit/background visuals driven by `play N` script events are missing — see Broken #1. Map renders black with Tom's sprite floating (`_pt_48`). |
| 4b | Crash arrival on Albion (`Map.LandingOnAlbion`), Rainer joins | **WORKS** | Party=2 (Tom + Rainer lvl 4) on map load; crashed shuttle in jungle + "MY GOD! So this is the desert world!" `_pt_50`. |
| 4c | Shuttle explosion + dream sequence | **WORKS** | `Video.ShipExplosion` (10 frames) and `Video.DreamShort` (102 frames) both play — `_pt_51_dreamvideo.png`. `start_anim 2` is a silent no-op (Broken #2). |
| 4d | Wake-up in Hunter Clan guest room (`Map.HunterClan`), first Iskai speech | **WORKS** | "Srrniak? Rriiba nes anrji…" + Tom's panic + Rainer's "Welcome back among the living!". Autosave 98 updated. `_pt_52/53`. |
| 4e | Camera follow after wake-up | **BROKEN → FIXED** | `Script.ShuttleCrashed_166` issues `camera_lock` and never unlocks; camera stayed frozen in the guest room while the party walked away (`_pt_55`). Fixed: unlock on MapInit (below). Verified following again (`_pt_56`). |
| 5 | Iskai meeting beats (Sebai-Li Wrinn) | **WORKS** | `talk NpcSheet.SebaiLiWrinn` → "Dsarii-ma, Tom and Rainiir! I'm glad to see, Tom, that you have recovered…" `_pt_59`. (Drirr/Sira joining not attempted per scope.) |

## Broken / missing (still open)

1. **`play N` script events are dropped — flight sequence visuals missing.** (TOP BLOCKER for presentation, not progression)
   - `Script.ShuttleFlight_165` interleaves `play 5 / play 1 / play 8 / play 12 / play 13` with `play_anim` and sounds. `PlayEvent` (`src/Formats/ScriptEvents/PlayEvent.cs`, "Unknown" int arg) has **no handler anywhere**, so whatever the original `PLAY` opcode did (likely controlling pre-started map animation loops — cockpit window frames during the flight) never happens. Net effect: the whole flight plays over a black screen with Tom's sprite floating in it (`_pt_48_crash.png`).
   - Related oddity: the party sprite probably shouldn't be visible on FlightToAlbion at all.
2. **`start_anim` is a no-op.** `VideoManager.Start` (`src/Game/VideoManager.cs:25`) is an empty method. Used by `ShuttleCrashed_166` ("Giria kommt!", `start_anim 2 0 0 2`) — the animated overlay never shows. Same root family as #1.
3. **`/npcs` lists some NPCs with `MapTextIndex.*` ids** (e.g. service droids on Toronto, several HunterClan NPCs at 0,0 / 255,255). Cosmetic id-mapping issue in the harness NPC dump (or genuinely unmapped sheet ids in map data).
4. **Pistol gate untested.** Chains 40/60 (`has_item >= 1 Item.Pistol` at (148-150,6-12) and (209,7)) are bypassed because the PA-announcement chain offers the launch prompt first. Did not verify the no-pistol refusal branch; Tom's starting inventory presumably contains the pistol.
5. **Pre-existing, unrelated:** Windows Event Log shows UAlbion.exe NullReferenceException crashes at 15:48–16:46 — all from the `DumpText` CLI path (`src/UAlbion/DumpText.cs:46`), i.e. earlier asset-dump tool runs, **not** this playthrough. Zero crashes/hangs in ~2h of harness driving; `lastError` stayed null throughout.

## Bugs found and FIXED during this run (small, verified)

1. **Greeting text never shown / first menu click swallowed** — `Conversation.OnText` case `TextLocation.StandardOptions` blocked the event chain on an options menu and discarded the clicked option. The chain (which shows the actual greeting, e.g. "Ah, hello, Driscoll!") only continued after a wasted click, and the click result went nowhere.
   *Fix:* `src/Game/Gui/Dialogs/Conversation.cs` — StandardOptions is now a non-blocking no-op; the chain proceeds to show the greeting and `Run()`'s loop presents the standard menu, routing clicks through `BlockClicked`. Verified: greeting first, then menu; Profession answer correct (`_pt_10/11`).
2. **Nested conversations wedge the UI** — a `ChaseParty` NPC (Anne Dorbeck) initiating contact-talk while another `start_dialogue` arrives stacked two Conversations + input modes; conversation became unclosable and every later `talk` piled more on (log: duplicate `start_dialogue NpcSheet.AnneDorbeck`).
   *Fix:* `src/Game/Gui/Text/ConversationManager.cs` — `StartDialogueCommon` ignores `start_dialogue` while a conversation is active (logged). Verified: Anne's contact-talk opens once, closes cleanly.
3. **Camera permanently locked after crash cutscene** — `camera_lock` from `Script.ShuttleCrashed_166` persisted across the map change to HunterClan (original engine resumes follow on map load).
   *Fix:* `src/Game/Entities/CameraMotion2D.cs` — clear `_locked` on `MapInitEvent`. Verified camera follows party post-wake-up (`_pt_56`).
4. **New diagnostic:** `dump_map_zones` event (`src/Game/MapManager.cs`) — logs every event zone (coords, trigger, chain, first event) of the current map. Used to locate the Christine zone, door pads, pistol gates and launch trigger.

Regression checks after all fixes: `UAlbion.Game.Tests` 207/207 pass; `_smoke_all_saves.ps1` 13/13 clean.

## Ranked top blockers

1. `play` / `start_anim` script opcodes unimplemented → intro flight/crash presentation plays on a black screen (story still progresses).
2. (was) StandardOptions conversation bug — fixed this session; without it every NPC dialogue felt broken (no greeting, dead first click).
3. (was) camera_lock leak — fixed this session; soft-locked the presentation after the crash.
4. (was) nested-conversation wedge — fixed this session; could soft-lock near any ChaseParty NPC.
5. Harness/NPC id mapping cosmetics + untested pistol-gate branch — low priority.
