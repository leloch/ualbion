# B6 Story Playthrough — organic drive + bug fixes (2026-07-06)

Driving the real story with the HTTP harness (organic: `party_goto`, context-menu
verbs, `npc_talk`, conversation clicks — no cheat teleports on the critical path except
to skip already-verified stretches). Goal: prove the game is actually completable, and
fix whatever blocks it. **Two recruitment/progression-blocking bugs found and fixed.**

## Bugs found & fixed this pass

1. **Intro-flight cutscene DEADLOCK (blocker — game could not progress past the flight).**
   `Script.ShuttleFlight_165` interleaves `play N` pacing ops with `play_anim`. `play N`
   waited on `Video.CycleCompleted`, which only fired when the FLIC frame wrapped to 0 —
   but **ring-frame FLICs never revisit frame 0** after the first pass, so the cycle event
   never fired and the chain blocked forever at `play 5`. The intro could not reach Albion
   under organic driving. Fix (`45a9991a`): `FlicPlayer` exposes a real `Cycle` counter;
   `Video` raises `CycleCompleted` on cycle change and a new `Ended` event on removal/load
   failure; `VideoManager.PlayCycles` also completes on `Ended`. **Verified: intro plays
   end-to-end → LandingOnAlbion → HunterClan.**

2. **Party-companion recruitment BROKEN (blocker — Drirr/Sira/Mellthas could not join).**
   Party-companion map NPCs store the party-member index on disk; their on-talk behaviour
   is the recruitment EventSet at `980 + index` (`EventSet.Drirr = 983 = 980 + 3`). The
   `+980` offset was a never-done TODO in `MapNpc.AssetTypeForNpcType` (and the save-side
   `NpcState.Serdes`), so Drirr's id resolved to `EventSet.SpellsUnused (3)` and talking to
   him did nothing. Fix (`406d4377`): apply the offset (read + write, round-trip safe; id 0
   = Tom/empty stays unoffset); `Npc2D`/`SelectionHandler3D` now trigger an EventSet-typed
   NPC's set from entry 0. **Verified end-to-end: `npc_talk 12` → EventSet.Drirr → Yes →
   Drirr joins (party 2 → 3).** Regression test in `AssetLoadTests` + data.

3. **Screenshot hang when the game window is hidden/minimised** — `/screenshot` deferred to
   `PreSwapBuffersEvent`, which stops firing while `Engine._active == false`, hanging the
   client forever. Now fails fast after ~2s (`45a9991a`).

## Harness additions (for reliable driving)
- `GET /chains` — active event-chain contexts + the exact node each is stuck on. This is
  what pinpointed the `play 5` deadlock instantly.
- `GET /npctalk?n=N` — talk to NPC slot N (fires the real TalkTo interaction). `/click/at`
  is overridden by the real mouse and can't be aimed, so this is the only reliable NPC-talk.
- `GET /collide?x=&y=[&cls=]` — point-collision probe (walls + object/NPC-body AABBs).
- `GET /zones` now reports each door/exit zone's teleport destination (`goesTo`).
- Weighted body-aware 3D `party_goto` (Dijkstra, NPC bodies passable-at-high-cost) so a
  chaser parked on the party's tile is routed around instead of wedging the glide.

## Verified working (organic, this pass)
- **Ch.0 intro**, full: new game → cabin monologue → crew (Christine etc.) → PA launch
  prompt → hangar briefing → flight to Albion (the fixed cutscene) → crash → Rainer joins
  → dream → **Hunter Clan** wake-up.
- **Hunter Clan**: Sebai-Li Wrinn conversation (greeting, topic menu, word topics like
  Trii, farewell), lock/key puzzles (guest-room + exit door, HunterClanKey), map
  transitions HunterClan ↔ Downstairs ↔ Cellar, exit to **Jirinaar**.
- **Jirinaar** (3D city): traversal (weighted pathfinding across the map past a chasing
  city NPC), door/exit discovery via `/zones goesTo`.
- **House of the Winds**: **Drirr recruited** (the fixed path).
- **Late game**: `finale` scenario loads the full 6-member party (Tom, Drirr, Sira,
  Mellthas, Khunag, Siobhan); AI-boss encounter starts combat. (Surrender→endgame FLICs
  were verified in a prior session, `_PLAYTHROUGH.md`.)

## Known friction (not blockers)
- **NPC body collision + chasers**: a chasing NPC that body-blocks can slow the auto-`goto`
  glide (weighted pathfinding + re-path mitigate it; a human on the keyboard just sidesteps).
- **Exhaustion**: the party accumulates fatigue if never rested (correct mechanic) — a real
  player rests at the guest room; long unrested harness drives hit Exhausted + LP drain.
  Use `reset_fatigue` when driving.
- `/click/at` unreliable (documented) → use `/npctalk`, `/click` (by id), verb `trigger_tile`.

## Not yet organically walked (engine proven crash-safe by the 166-map shakeout)
Ch.2 Jirinaar quest chain, Ch.3 Drinno + Mellthas, Ch.4-5 Maini cities + Khunag/Siobhan,
Ch.6-8 Dji-Cantos/Kenget. The recruitment mechanism (the one real progression blocker) is
now fixed & generalised; remaining verification is content-walking, not engine-blocking.
