# UAlbion → 100% Playthrough roadmap

> Companion to `_TODO_1TO1.md`. That ledger tracks **1:1 mechanic fidelity** vs MAIN.EXE.
> This doc tracks a different axis: **content-pipeline completeness** — can the engine
> actually drive the whole scripted game start-to-finish (crash of the *Toronto* → Seed
> ending)? Compiled 2026-06-13 from a 13-dimension research+audit sweep (real-game
> mechanics vs codebase). Every item below cites code evidence; verify before trusting.
>
> **The one rule:** declare nothing done on code-complete — declare it done on a green
> harness run against the relevant save. The project has repeatedly had ledger drift.

## Bottom line

Albion is **NOT yet completable** in ualbion. Engine + systems are strong and largely
1:1, but the game is **intro-complete and finale-impossible**. The only recorded run
(`_PLAYTHROUGH.md`) stops at the Hunter Clan handoff (~chapter a/b of ~11), and the
canonical ending cannot fire (no `ask_surrender` handler). Two compounding problems:
(a) a cluster of un-dispatched conversation/world-verb/ending events gating main-quest
beats; (b) no empirical run past the intro, which is hiding further blockers.

## Blockers — a playthrough is impossible/broken without these

| # | Item | Evidence | Effort |
|---|---|---|---|
| B1 | **Conversation dispatch half-wired.** `Conversation.cs` dispatches only action types 0x0/0x1/0x6/0x7; `BlockClicked` understands only block ids 0–3. `DialogueLine`(0x8) branches, `AskToJoin`/`AskToLeave`(0x4/0x5), special scenes (`PartySleeps`0x3D, Sira 0x17/0x2D, Tom 0xE, pay 0x2) never fire. **Gates the entire recruitable roster + quest scenes.** | `Conversation.cs:246-285`, `ActionType.cs`, grep: TriggerAction never gets 0x4/0x5/0x8 | L |
| B2 | **Canonical ending can't trigger.** `AskSurrenderEvent` parses to `Unk1..Unk8`, no handler. Final boss (~4500 LP) is intentionally unkillable; win is surrender/Seed branch only. End scripts exist in data, can't run. | `AskSurrenderEvent.cs`; grep AskSurrender → only serdes, no `src/Game` | M |
| B3 | **No terminal state.** `MapExitType.EndSequence`/`Shuttle` exist but `MapManager.Teleport` ignores `Unk4`; no credits/outro/win consumer in `src/Game`. Finale (Endgame1-4, Seed deploy, credits) unorchestrated. | `MapManager.cs:107`, `TeleportEvent.cs:15`, `Base/Video.cs` | M + **XL** finale |
| B4 | **UseItem world-verb never dispatched.** `SelectionHandler2D/3D` build only Examine/Manipulate/Take/TalkTo. Kills tool-on-obstacle puzzles (pick-axe/screwdriver/staff) — gates **Toronto endgame** + Gratogel library. | `SelectionHandler2D.cs:120-150,218`; grep: no live `TriggerType.UseItem` dispatch | L |
| B5 | **Merchant economy non-functional.** Buy = free `Pickup` (no gold debit); `InventorySellEvent` has zero subscribers; no pricing/transaction layer. Breaks the core gold loop. | `InventoryManager.cs:85-88`, `LogicalInventorySlot.cs:226`, `Inventory.cs:100` | L |
| B6 | **No empirical run past intro.** 179 maps, region gating, every recruit/departure/split, all endgame data unverified end-to-end. Blocker-by-uncertainty: expect 2–4 more unhandled `Unk*` opcodes. | `_PLAYTHROUGH.md` stops at Hunter Clan; `_TODO_1TO1.md` §7 | L |

## Major gaps — playable but materially incomplete/wrong

**Presentation bookends:** no intro cinematic (`MainMenu.NewGame` → Tom's cabin, **Tom
alone** `GameState.NewGame`); shuttle cockpit overlays render over black (beat 4a); Fade/
Wipe/FillScreen parse, no handler (abrupt cuts); dead View Intro/Credits buttons
(`MainMenu.cs:54-55`, no `.OnClick`).

**Character progression (under-delivers over a long run):**
- **AP never granted on level-up** — `ApplyPerLevelGains` ignores `LevelsPerActionPoint`; AP multiplies strikes/round (`Battle.cs:595`). **S, high-leverage.**
- **Spell mastery never grows with use** — only learn-time seed `4×MagicTalent` (`SheetApplier.cs:115`); both cast paths read-only; `Battle.cs:1112` comment falsely asserts growth. **S.**
- Training-point grant flat `+=` vs RE'd `clamp(level/divisor,1,4)` (`SheetApplier.cs:399`).
- **Non-melee trainers unreachable** — only `LearnCloseCombat` dispatched; Critical-Hit (Maini@Kounos, the only one in game), Ranged, Lock-Picking unwired (`PlaceActionManager.cs:108`).
- MaxSP omits INT/30 term; SLP economy unimplemented (spells learned for gold only).

**Day/night bug (not polish):** `NightPalettes.cs:24-34` hardcodes 8 base-game pairs with
`TODO: load from asset`; later regions silently never go dark. Gate blend on
`MapLightingMode (mapFlags&3 == DayNightCycle)` instead of palette-table membership.

**Keyword persistence:** `_topics` is per-Conversation, discarded on close, never saved
(`Conversation.cs:28`, `SavedGame.cs:308` TODO). Words don't carry between NPCs — breaks
the gossip/trade-topics loop.

**Traps unapplied:** decoded in `_RE_5C.md` but `triggeredTrap` hardcoded `false`
(`InventoryScreenManager.cs:118`); world `TrapEvent` parse-only, no handler. Unlimited free
chest retries, no damage. Pressure-plate/lever→wall-passability chains unverified.

**Economy depth:** no merchant gold pool / stock depletion / restock (`Inventory.cs:100`).

**Controls:** no mouse navigation (drag-to-walk 2D, click-to-move 3D) — original is
mouse-primary; ualbion keyboard-only. Completable but a fundamental deviation.

**Save-state:** `HomeNpcIndex` (sheet+0x1C) + `RemovedNpcs` not written on join/leave
(deferred — needs `AddPartyMemberEvent` to carry source map+slot).

## Minor / polish

Advance-Party action = no-op (`NopEvent`); SP-shortfall refuses cast vs pay-in-LP; Iskai
tail 2nd attack missing; encumbrance constant placeholder, no over-weight penalty; some
utility spells fall through to `Failed`; Levitation expires on map-change vs hourly decay;
trainer spell-list hides vs greys options; automap `TAB`/`M` dead-wired (reachable via
right-click/Map-View spell); no monster respawn; no mid-rest ambush; no "really rest?"
confirm; options missing 3D-window-size; Smacker FMV decoder absent (FLIC only); empty
test stubs (`ConversationTests`, `InventoryTests` gold/ration, no Party behavior tests).

## Suggested attack order

Strategy: **unblock the dispatch layer → drive an empirical run that verifies the fixes
AND discovers unknown blockers → close the finale → fidelity.** Each phase ends on a green
harness run, not code-complete.

- **Phase 0 (S):** scripted 13-save harness run w/ event-chain traces to detect silent
  dispatch failures; add behavioral Party + conversation-dispatch tests.
- **Phase 1 (L) — Conversation + recruit dispatch [B1]:** keystone. DialogueLine branching
  → AskToJoin/Leave → special handlers. Verify: recruit Drirr/Sira, `Party=N` increments.
- **Phase 2 (L) — World-verb + economy [B4, B5]:** independent of P1, parallelizable.
  UseItem dispatch + merchant transaction engine. Verify gold changes on buy/sell.
- **Phase 3 (L) — Full mid/late playthrough [B6]:** drive chapters c–k via harness,
  save-scumming each transition. **Unknown blockers surface here.**
- **Phase 4 (M + XL) — The ending [B2, B3]:** `ask_surrender` handler + `EndSequence`
  consumption + orchestrated finale/credits. The literal 100% checkpoint.
- **Phase 5 (S, anytime after P0) — combat/progression fidelity:** level-up AP, per-cast
  mastery growth, training-point clamp, non-melee trainers.
- **Phase 6 — presentation bookends + day/night palette fix** (a real bug, not polish).
- **Phase 7 — remaining polish:** keyword persistence + save round-trip, traps, mouse nav,
  automap binding, HomeNpcIndex wiring.

**Dependencies:** P1 gates party assembly + most quest scenes; P4 unreachable until P3
proves the path to Toronto; land B4 (UseItem) in P2 *before* the run reaches the Toronto
endgame.
