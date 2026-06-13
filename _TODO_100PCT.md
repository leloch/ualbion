# UAlbion → 100% Playthrough roadmap

> Companion to `_TODO_1TO1.md`. That ledger tracks **1:1 mechanic fidelity** vs MAIN.EXE.
> This doc tracks a different axis: **content-pipeline completeness** — can the engine
> actually drive the whole scripted game start-to-finish (crash of the *Toronto* → Seed
> ending)?
>
> **Rewritten 2026-06-13** after a multi-agent gap audit (`_TODO_GAP_AUDIT.md`) **and a
> hand-verification pass that re-opened every blocker against the current code.** The audit
> was useful for breadth but had a **~25 % false-positive rate on its headline blockers**:
> it flagged B4 (UseItem) and B8 (HomeNpcIndex off-by-one) as broken when both are actually
> implemented, and called WordKnown / keyword-sharing "entirely unimplemented" when it works
> at runtime. Its auditors repeatedly read the **stale line numbers from the old version of
> this doc** and concluded "missing" without opening the file. So:
>
> **The one rule (now doubly earned):** declare nothing done on code-complete *and trust no
> "it's missing" claim that you haven't reproduced by opening the file yourself*. Every item
> below carries a verification tag:
> - **[VERIFIED✓]** — I opened the cited code on 2026-06-13 and confirmed the state.
> - **[CORRECTED]** — the audit/old-doc called this a gap; it is actually done (don't work it).
> - **[UNVERIFIED]** — audit-sourced, plausible, but not re-opened. **Open the file before
>   starting work.** Treat as a hypothesis, not a fact.
> - **[RISK]** — a latent crash/soft-lock that depends on real map data we haven't extracted.

## Bottom line

Albion is **NOT yet completable** in ualbion. The engine + 1:1 systems are strong, and the
audit's panic was overblown — several "blockers" are already done. But after hand-verifying,
**five real blockers remain**, and they are concentrated in the **finale** and the
**economy**, plus one **3D-dungeon hard-stop**, and the whole back half of the quest has
**never been run empirically**. The only recorded run (`_PLAYTHROUGH.md`) stops at the
Hunter Clan handoff (~beat 5 of ~11).

The corrected blocker spine:
1. **The ending cannot fire** — `ask_surrender` (the *only* win condition; the final AI is
   unkillable by design) is a parse-only no-op with no handler, and there is no finale
   sequencer or terminal win-state at all. **[VERIFIED✓]**
2. **The economy is dead** — buying is a free pickup (no gold debit); selling raises an event
   with **zero subscribers**. The linear quest gates regions on gold. **[VERIFIED✓]**
3. **3D lever/pressure-plate wall openings silently fail** — `ChangeIconEvent` is subscribed
   in 2D (`FlatMap`) but **not** in 3D (`DungeonMap`), so portcullis/wall-dissolve chains in
   Drinno/Kounos/Kenget-Kamulos/Toronto never take effect. **[VERIFIED✓]**
4. **A cluster of event-triggered scenes never dispatch** — `PartySleeps` (rest), Sira spell
   scenes, the Tom-endgame action — gating mid/late story beats. **[VERIFIED✓]**
5. **No empirical run past the Hunter Clan** — every chapter Jirinaar→finale is unverified;
   expect 2–4 more unhandled opcodes to surface, plus the `QueryType` map-load crash risk.

---

## Blockers — a playthrough is impossible/broken without these

| # | Item | Status | Evidence (verified) | Effort |
|---|---|---|---|---|
| **B2** | **Canonical ending can't trigger.** `AskSurrenderEvent` parses to `Unk1..Unk8`, no handler. Final AI (~4500 LP, weapon-immune) is intentionally unkillable; win is the scripted surrender→member-falls→Tom-deploys-Seed branch only. | **[VERIFIED✓] BLOCKER** | grep `AskSurrender` in `src/` → only `Formats/MapEvents/AskSurrenderEvent.cs`, `MapEvent.cs`, `MapEventType.cs`, `RemainingUnknowns.txt`. **Zero** `src/Game` handler. | M |
| **B3** | **No finale orchestration / no terminal win-state.** After the AI fight: member falls → Tom throws Seed → CpuGoBoom/NedsDead → Endgame1-4 → credits → win. None of this is wired. `CombatResult` has only Victory/Retreat/PartyKilled/ExitGame. | **[VERIFIED✓] BLOCKER** | grep `Seed\|ThrowingSeed\|Endgame\|Surrender\|GameComplete\|EndSequence` in `src/Game` → **no** finale consumer (only unrelated: topic-seeding, `ThrowMagicSeed` SFX, `_poisonSeed`). FLIC *playback* primitive works (`VideoManager.Play`) — nothing routes to it. | XL |
| **B5** | **Merchant economy non-functional.** Buy = free `Pickup` (no gold debit); `InventorySellEvent` has **zero subscribers**. No pricing/pool/stock/transaction layer; no price/total UI. Breaks the core gold loop that gates ship passage, guide Ohl, trainers. | **[VERIFIED✓] BLOCKER** | `InventoryManager.GetInventoryAction:85-106` — merchant slot hits `(None,_) ⇒ Pickup`, no `InventoryType.Merchant`/gold branch. `InventorySellEvent` raised at `LogicalInventorySlot.cs:226`; **no** `On<InventorySellEvent>` anywhere. No `InventoryBuyEvent` type exists. | L |
| **B7** | **`ChangeIcon` (wall/floor/ceiling) in 3D dungeons.** Levers/pressure plates open portcullises and dissolve walls in Drinno/Kounos/Kenget-Kamulos/Toronto. | ✅ **DONE `556f297b`** | `DungeonMap` now subscribes `On<ChangeIconEvent>(ChangeIcon)` mirroring `FlatMap` (relative/absolute coords from source, temp/perm scope) → `LogicalMap3D.Modify`. Build/tests/smoke green. | ~~M~~ |
| **B1′** | **Event-triggered conversation actions.** Menu actions all fire (verified live). Engine-fired chains: **PartySleeps `0x3D`** (rest) ✅ **DONE `17299982`** — `GameState.OnRest` scans each member's set for a `PartySleeps` chain and runs it. **Remaining:** **Sira `0x17`/`0x2D`** (seed scenes), **Tom `0xE`** (endgame), **Riko/Gerwad `0x9`** — engine-fired in story contexts whose triggers still need RE. | **[PARTLY DONE]** | PartySleeps wired in `GameState.OnRest` via `FindActionChainIndex`→`TriggerChainEvent`. Sira/Tom/Riko dispatch points undecoded. | M (remainder) |
| **B6** | **No empirical run past the Hunter Clan handoff.** 179 maps, region gating, every recruit/departure/split, the whole finale data — unverified end-to-end. Blocker-by-uncertainty: expect 2–4 more unhandled `Unk*` opcodes and the `QueryType` crash risk to surface here. | **BLOCKER (meta)** | `_PLAYTHROUGH.md` stops at beat 5 (Sebai-Li Wrinn); Drirr/Sira join "not attempted." | L |

### Corrected — the audit/old-doc called these blockers; they are DONE. Do **not** re-implement.

| Was | Reality (verified 2026-06-13) | Evidence |
|---|---|---|
| **B4 — "UseItem world-verb never offered, no item flow"** | **[CORRECTED] Fully wired.** The verb is offered in **both** menus when the player holds an item and the zone accepts it; the held item is carried into the trigger's `EventSource`; the puzzle chain's `query used_item == X` resolves. The audit read the *old* line numbers (`:120-150`) and missed the branch. | `SelectionHandler2D.cs:157-164`; `SelectionHandler3D.cs:219-226`; `FlatMap.cs:211-214`; `DungeonMap.cs:322-324`; `Querier.cs:33`. **Residual:** `ChangeUsedItemEvent` (`change_used_item`) has no handler → the item isn't consumed/flagged *after* use (grep: 0 hits in `src/Game`). Treat as **S** wiring + a live puzzle check, not a blocker. |
| **B8 — "HomeNpcIndex leave-side off-by-one recruit soft-lock"** | **[CORRECTED] Fixed.** `TryDecode` adds `+1` to convert the script literal `(mapId−1)*96+slot` (26888) into the remake's `MapId`-keyed bit (26984); the leave handler re-keys correctly. The docstring describes the bug in **past tense**. Round-trip is correct. The audit re-stated a stale `SavedGame.cs:155` TODO comment as a live bug. | `HomeNpcIndex.cs:32-44` (the `+1`); `Party.cs:130-136` (`TryDecode`→`ModifyNpcOffEvent(Clear, slot, map)`). |
| **WordKnown — "entirely unimplemented; topics never saved/shared"** | **[CORRECTED] Works at runtime.** Words discovered in conversation are pushed to a global set and re-seeded into every later conversation (cross-NPC gossip loop functions); the script `WordKnownEvent` is handled. | `GameState.cs:77` `DiscoverWord`; `:118` `On<WordKnownEvent>`; `Conversation.cs:62` (seed from `DiscoveredWords`), `:285` (share back). **Residual (real):** `_discoveredWords` is **not** in `SavedGame` → discovered words are lost on save/reload. **S** persistence task — see Major gaps. |

---

## Major gaps — playable but materially wrong/incomplete (all VERIFIED✓ unless tagged)

**Map-event opcodes that parse but have no `src/Game` handler** (each confirmed by grep — no
`On<…Event>` subscription anywhere in `src/Game`):
- **`ExecuteEvent` / `SignalEvent`** — cross-zone signal flags & sub-chain execution; silent
  no-ops, can drop story side-effects. **M.**
- **`TrapEvent` (world)** — 3D trap tiles (Kenget Kamulos / Toronto / Jirinaar dungeon) apply
  damage + conditions with Luck/Dexterity evasion; currently walking a trap tile does nothing,
  so the trap-evasion attributes are dead too. **M.**
- **`SpinnerEvent` (MapEventType 5)** — rotates party facing on disorientation tiles; `Unk1`
  meaning still unknown. Navigation-fidelity gap, not a hard stop. `Movement3D` has reusable
  absolute-facing machinery a handler can call. **S** (after RE'ing `Unk1`).
- **`CreateTransportEvent` (0x13)** — spawns a rideable ship/raft/flying transport; no handler,
  no player-vehicle state machine. **[RISK]** whether any *mandatory* crossing is
  script-spawned vs teleport-driven is unverified — the Gratogel→Maini sail is *likely*
  `SailToGratogel_*` teleport (non-blocking), but the script bodies weren't opened. **M.**
- **`ChangeUsedItemEvent` (`change_used_item`)** — marks a tool consumed after a UseItem
  puzzle; no handler. Puzzles that *query* the used item work (B4 corrected); puzzles that
  rely on *consuming/flagging* it afterward don't complete. **S–M.**
- **`FadeToBlack/FromBlack/ToWhite/FromWhite` + `FillScreen`** — scene-transition blends;
  empty `[Event]` records, no subscriber → hard cuts / wrong-frame flashes around
  cutscenes & the finale. **M.**

**Status spells do nothing to monsters.** `InflictStatusEffect.Apply` only writes a condition
when `target.SheetId.Type == PartySheet`; against a monster it runs the success gate then
returns `Hit` **without applying anything** (`InflictStatusEffect.cs:43-48`). Sleep / ThornSnare-
paralyse / Blind / Panic — the debuff backbone of every hard fight from Drinno on, and the
documented brute-force AI trap — are inert on enemies (Frost works because it routes through
`CombatBuffs`). There is no monster-condition shadow state. **M, combat-completability.**

**Per-map ambient bed never plays.** `MapManager` fires `SongEvent` on load but never
`AmbientEvent`; `AmbientSongId` is deserialized then dropped. The `On<AmbientEvent>` handler
exists but nothing raises it. Music half works; the ambient half is a genuine missing wiring
(grep: no `On<AmbientEvent>` in `src/Game`). **S.**

**Discovered words not persisted to saves.** `_discoveredWords` is a runtime `HashSet` in
`GameState` (grep: absent from `SavedGame`). Cross-NPC sharing works in a session but resets on
reload — breaks the "Umajo Danu" / learned-word gates across a save. **S.**

**SLP (spell-learning-point) economy unimplemented [UNVERIFIED].** Original tracks a separate
SLP pool (sheet+0xEA) granted on level-up and spent to learn spells; ualbion folds it into
TrainingPoints and charges gold only. Completable but non-faithful. (`SheetApplier.cs:413-418`,
`PlaceActionManager.cs:334-372` per audit — re-open before working.) **M.**

**Merchant pricing/pool/stock/UI [VERIFIED✓, part of B5].** Even once buy/sell debit gold,
the original applies per-shop 38–130 % multipliers off item `Value`, models a merchant purse
that caps sells, and shows prices/totals. None present (`ItemData.Value` unused for trade;
`Inventory` has no purse; `InventoryMerchantPane` is a bare slot grid). **L** (do with B5).

**Join-side home-NPC stamp is script-driven, not engine [VERIFIED✓ — downgraded].** `AddMember`
(`Party.cs:90-124`) only adds to the roster; it never writes `sheet+0x1C` or the RemovedNpcs
bit — the original (fcn.000384eb) auto-stamps both. **Not a functional blocker:** base recruit
chains do it via `modify_npc_off Set`, and the leave-side decode (corrected B8) matches that
bit. Pure 1:1-fidelity / mod-safety debt. **M, low priority.**

---

## Latent crash / soft-lock risks (verify against extracted map data)

**[RISK, reduced] `QueryType` map-load crash.** `QueryEvent.Serdes` threw `FormatException`
on unhandled query types *at deserialize time* (would crash **map load**). **`81e531db`
added Gender/Class/Race/Day** handlers (the audit's named character/time gates), so those no
longer crash. **Still unhandled (would throw):** `IsCurrentMap2D`, `DoorUnlocked` (0x2),
`ChestUnlocked` (0x3), `Unk8`, and assorted `Unk*`. Whether any real map uses one is still
unverified (XLDs not extracted) — diff the used `QueryType` set vs the serdes switch before
the Phase-4 run; add the remaining handlers if hit. **S to check, S–M to finish.**

**[RISK] Forced party-swap with a full party (Joe at the Toronto).** The 6-cap is *enforced*
(`AddMember` returns false when full) but there is **no dismiss-to-make-room prompt**. If Joe's
join is a plain `add_party_member` that branches on success, a full party means Joe never
boards — and Joe is mechanically required for the Toronto electrical puzzles. Verify the recruit
chain + add a swap prompt if it hard-fails. **M.**

---

## Minor / polish (mostly UNVERIFIED audit-sourced — open the file before working)

- **`MapExitType` (Unk4) ignored by Teleport [VERIFIED✓ benign].** `MapManager.Teleport` reads
  only MapId/X/Y/Direction, but the serdes asserts Unk4 ∈ {0,1,2,3,6,106,255} ("always 255") —
  value 4/EndSequence never appears; finale/shuttle handoffs are script-driven. Dead-enum
  cleanup, **not** the finale mechanism. **S.**
- **MainMenu ViewIntro/Credits buttons dead (no `.OnClick`)** — FLIC playback exists to fulfill
  them. **S.** [UNVERIFIED]
- **No menu-launched intro cinematic on New Game** — `NewGame` jumps straight to `TorontoBegin`;
  the in-engine cockpit FLIC beats DO run. **L.** [UNVERIFIED]
- **Automap `M` key dead** — `input.json` binds `M`→`open_map` (no such event; real is
  `show_automap`). Automap **is** reachable via the 3D context-menu "Map" and the DjiKantos
  spell. Rename the keybind. **S.** [UNVERIFIED]
- **No 2D drag-to-walk** — 2D is keyboard-only. 3D mouse nav **is** implemented
  (`Normal3DMouseMode`). 2D click-to-move is the only true control gap. **XL.** [UNVERIFIED]
- **Trapped-chest mechanic absent** — `triggeredTrap` hardcoded false; no trap field on
  `ChestEvent`; Thief's Amulet unreferenced; unlimited free retries. **M.** [UNVERIFIED]
- **SP-shortfall refuses cast** instead of paying the shortfall in Stamina-scaled LP. **S.** [UNVERIFIED]
- **`SpellEnvironments` bit order wrong** (City/Dungeon/Wilderness/Interior) — risks hiding
  dungeon-only utility spells; combat-bit path unaffected. **S.** [UNVERIFIED]
- **Condition spells ignore per-family success multiplier** (Boasting/Shock/Panic M×80/100 vs
  ThornSnare M×100/100, INFERRED). **S.** [UNVERIFIED]
- **Banish-demon uses M>resist gate** vs the demon-LP chance math (RE flags it INFERRED, so
  byte-exact impossible now; functionally works vs demon class). **M.** [UNVERIFIED]
- **Night-palette table hardcodes 8 pairs**; `OutdoorsNight` fallback already darkens unlisted
  2D DayNightCycle maps, so the worst symptom is mitigated. **S.** [UNVERIFIED]
- **New-game date 2200 vs lore Sept 8 2230** — one-line Epoch change; no UI/content depends on
  it. **S.** [UNVERIFIED]
- **Test stubs / coverage debt** — `ConversationTests` empty; no Party join/leave/cap behavior
  tests (the isolated `HomeNpcIndexTests` only check decode, not the round-trip). **M.** [VERIFIED✓]
- **Large undecoded `ActionType` span (0x9–0x3C minus the named)** — latent RE backlog beyond
  the four event-actions in B1′. **L.** [VERIFIED✓ — many `Unk*` remain]

---

## Live-run control / render bugs (user-reported 2026-06-13, UNTRIAGED)

Reported from an actual play session. These are movement/camera/UI defects, not content gaps —
several are likely small fixes but each needs reproduction + a root-cause pass. Iterate later.

1. **3D movement W/S reversed / WASD dead** — ✅ **FIXED** (`input.json` World3D). Root: WASD was
   bound to the collision-free free camera with `Y=-1` for W, but both move handlers treat
   *positive Y = forward*. **First fix mistakenly used `party_move` — a 2D-only event with no 3D
   handler (Movement3D only handles `party_move_3d`), so WASD did nothing.** Corrected to
   `+party_move_3d` (positive Y = forward, A/D strafe); arrows Up/Down now walk, Left/Right turn.
   Verified: `party_move_3d` steps -Z and reverses; continuous `+` bindings re-raise each frame.
2. **3D mouse rotation dead** — ✅ **FIXED (wired)** (`Movement3D.OnMove3D`). The mouse turn/look
   zones set `PartyMove3DEvent.Yaw/Pitch` but `OnMove3D` only read `Velocity` and early-returned
   when velocity was zero, dropping all rotation. Now raises `CameraRotateEvent` for yaw/pitch
   before the translation early-return. (Live mouse-drag confirmation still pending — harness can't
   send the Yaw part.)
3. **3D mouse movement goes the wrong way** — likely resolved by #1/#2 (the Forward/Back/Strafe
   zones already send the correct signs; the disorientation came from the dead rotation + reversed
   keyboard). **Confirm live.**
4. **3D no wall collision** — ✅ **FIXED** (same rebind as #1). Plain WASD drove `camera_move` (free
   cam, no collision); now drives `party_move` → `Movement3D.FilterCollision`. Verified live: 500
   forward steps stop at a wall (~z 21.3) instead of clipping through.
5. **2D mouse movement impossible** — 2D maps are keyboard-only; clicking the map does not move
   the party. (Matches the doc's "No 2D drag-to-walk" gap — but confirm even single click-to-step
   is absent.) **XL (full path-to-click) or M (single-step).** STILL OPEN.
6. **Party-leader switch has no effect** — ✅ **FIXED** (`StatusBarPortrait.OnTimer`). Root: the
   single-left-click leader path guarded on `inventoryManager?.ItemInHand.Item != null`, but
   `.Item` is a non-nullable `ItemId` struct so the test was ALWAYS true — every click diverted to
   "give item" and `SetPartyLeaderEvent` never fired. Now guards on `!…Item.IsNone`. Verified live
   (`set_party_leader` Tom→Rainer takes; the leader portrait is raised 3px via `Leader.Id`). Note:
   the indicator is a 3px raise, not an enlargement — if the original enlarges the head, that's a
   separate render tweak.
7. **World-map NPCs are giant / wrong** — ✅ **FIXED** (`ecc5c9bc`). Root cause (diagnosed live via
   the harness `/sprites` + the new `/state` mapType/tileset): `SavedGame.cs:322` hardcodes
   `MapType.TwoD` when (de)serializing saved `NpcState`, so every NPC restored from a save loads as
   `NpcLargeGfx` regardless of map. On a `TwoDOutdoors` map (overworld) the NPCs must be
   `NpcSmallGfx` → they came back 32×48 (vs 16×32) AND the wrong sprite. Teleporting masked it
   (re-inits from map data). Fix: `NpcManager2D` re-keys each NPC sprite to the live map's gfx set
   (`UseSmallSprites`; small/large enums are parallel, same numeric id). Deeper follow-up: fix the
   `SavedGame.cs:322` hardcode itself (needs a MapId→MapType resolver in the save layer). User-confirmed.
   --- (original note below, superseded) --- Asset-type resolution is
   correct (`MapNpc.Serdes`: TwoDOutdoors→`NpcSmallGfx`, TwoD→`NpcLargeGfx`); both player
   (`SmallPlayer`/`LargePlayer`) and `Npc2D` build a `MapSprite` with no explicit `.Size`, so both
   fall back to native texture frame size (`Sprite.UpdateSprite` `_size ??= subImage.Size`). Needs
   a **live visual pass**. NEW LEAD (via the new `/sprites` harness endpoint): on a 2D map
   (Nakiridaani) `/sprites` returns **0** sprites, while a 3D map returns 2400 — i.e. 2D map
   entities (player + NPCs) are NOT individual `Sprite` components under the scene/map tree the way
   3D billboards are. So 2D NPCs render through a different (batched/tile-renderer) path; the size
   bug likely lives there, not in `MapSprite`. Next: trace the 2D NPC render path
   (`NpcManager2D`/`Npc2D` → which renderer actually draws it on the overworld). **M.** STILL OPEN.
8. **Combat: combatant names show literal "NAME"** — ✅ **FIXED** (`TextFormatter` + `Battle`).
   Combat SYSTEXTS use the `{NAME}` token, which resolves the formatter's `active` entity (set by a
   `{COMBATANT}`/`{SUBJECT}` context token reading `GameState`), NOT the string arg `ShowCombatMessage`
   passed. With no active set it fell back to literal "{NAME}". Fix: context tokens now accept a
   directly-provided entity (`p ?? GameState.X`), and `ShowCombatMessage` passes the combatant as an
   implicit `Combatant` token. (Other `{NAME}`/`{SUBJECT}` placeholders may exist — watch for more.)
9. **Combat: no target-selection highlight** — ✅ **FIXED (wired; visual to confirm)**. Queuing
   an action now broadcasts `CombatTargetHighlightEvent` for the tile it will hit (melee
   auto-target resolved via `AdjacentEnemy`/first-enemy, mirroring execution); `VisualCombatTile`
   tints that tile (`BlueTint`, distinct from the active-turn `Highlight`). Cleared when the round
   runs. Combat verified end-to-end via harness (queue→round, no errors); the on-grid tint colour
   is a user-visual confirm (a proper border sprite would be a nicer indicator later).

---

## Additional completion requirements to VERIFY in the live run (not yet findings)

These are real-game mechanics the brief mandates that no audit finding squarely covers. Each is
a hypothesis to confirm during Phase 3/4, not an asserted gap:

1. **Language barrier** — early game you can't understand the Iskai until you learn the
   language (`ChangeLanguageEvent` is wired). Confirm the scrambled-text-until-learned
   *presentation* and that `TalkTo` is gated by shared language (the `MapPopup_…SameLanguage`
   text exists).
2. **Nakiridaani recovery time-skip (~2 months, → Oct 2230)** — a scripted multi-week jump is
   exactly the bulk-advance landmine; confirm it fires per-hour chains correctly. Gateway out
   of chapter 2.
3. **Time-gated content × NPC schedule × clock** — the Kounos Critical-Hit trainer (the only
   one in the game) is 8–9 AM only; shops open/close by time. Confirm the schedule-tick system
   end-to-end with day/night and the (verified-working) trainers.
4. **Quest-item gating** — `QueryHasItem` is in the serdes (`QueryEvent.cs:24`), so plot-item
   checks likely work; confirm the screwdriver/Ohl's-jewel/key gates actually branch.
5. **Torch / light consumption in dungeons** and the **can't-rest-unless-tired** rule (rest
   *does* consume rations — verified earlier; the light + fatigue halves are unconfirmed).
6. **Encumbrance / over-weight penalty** — Strength-based carry weight + gold-has-weight is a
   core economy constraint; confirm a penalty exists (placeholder constant suspected).
7. **Save round-trip for every NEW subsystem** — words, economy gold flow, transports: each
   feature added below MUST be added to `SavedGame` and round-trip-tested. Cross-cutting.
8. **Per-chapter scripted gate, individually** — Drinno (Bero rescue, Strength amulet,
   water-bucket/blue-staff force fields), Kounos↔Srimalinar Mahino gate, Umajo guide-Ohl,
   the "Umajo Danu" ritual, Kenget Kamulos / Beastmaster. The "179 maps unverified" lumps
   these; track each as its own checkpoint.

---

## RE backlog — original logic still to disassemble from MAIN.EXE

> Distinct from the wiring work above: these are places where we **don't know the original's
> logic** and must read it out of `MAIN.EXE` (radare2 project `albion_aaa`,
> `F:\Dev\albion\radare2-6.1.4-w64\bin\radare2.exe`). The big systems (combat strike pipeline,
> spells, world-tick, NPC AI, lighting, lockpicking — RE clusters A–D in `_TODO_1TO1.md`) are
> **decoded and applied**; what remains is below. Enums verified against current
> `ActionType.cs` / `QueryType.cs` 2026-06-13 (the `src/RemainingUnknowns.txt` dump is from a
> different checkout `C:\Depot\bb\ualbion` and is **stale** — e.g. it still shows `Unk3D` for
> the now-named `PartySleeps`; don't cite it as current).
>
> **The complete RE list can only come from the Phase-4 run.** Static analysis gives the
> *candidate* set; running the back half tells you which `Unk` opcodes/queries actually appear
> in real Jirinaar→finale maps (must-RE) vs which are dead enum entries (ignore).

### Tier 1 — RE that gates COMPLETION (decode, or the game can't finish / crashes on load)

| Item | Why it blocks | Where to look |
|---|---|---|
| **`AskSurrenderEvent` Unk1–Unk8** — *the single highest-value RE task.* The only win path; need the surrender threshold, which member falls, and how it chains to the Seed deploy. | B2 — ending cannot fire. | `AskSurrenderEvent.cs:24-30`; find the surrender handler in MAIN.EXE (combat end / map-event dispatch). |
| **Story `ActionType` values used by real NPCs** — `0xE` (981_Tom, endgame), `0x17`+`0x2D` (Sira spell scenes), `0x9` (234 Riko / 242 Gerwad), `0x2` (Garris pay/charter). | B1′ + finale dialogue beats; each is `Unk*` with the NPC named but no decoded effect. | `ActionType.cs:9,16,21,30,52`; trace the action-dispatch switch in MAIN.EXE. ~40 of 62 ActionType values are still `Unk*`, but only these 5 are known-used on the story path. |
| **`QueryType` values that THROW on parse — [RISK] crashes map load.** Diff of the enum vs `QueryEvent.Serdes` (29 cases, default `throw FormatException`). **Semantics known from the name (light RE — mostly serdes layout + handler):** `DoorUnlocked`(0x2), `ChestUnlocked`(0x3), `Gender`(0x16), `Class`(0x17), `Race`(0x18), `Day`(0x1B), `IsCurrentMap2D`(0x28). **Genuinely undecoded (needs RE):** `Unk8`, `UnkB`, `UnkD`, `Unk24`, `Unk25`, `Unk26`, `Unk27`. | Any one used in a real map = unloadable map. **Extract the map event tables in Phase 0 and diff the used set against the serdes switch.** | `QueryType.cs`; `QueryEvent.cs:18-50`; `Querier.cs`. |
| **World map-event opcodes parse-only with unknown fields** — `TrapEvent` (Unk1–6: damage / condition / Luck-Dex evasion; Unk6 tagged "Damage?"), `SpinnerEvent` (Unk1: rotation amount), `CreateTransportEvent` (Unk1–8: type / position / piloting). | Traps inert, spinners don't disorient, transports don't spawn (possible water-crossing gate). | `TrapEvent.cs:36-40`; `SpinnerEvent.cs:28`; `CreateTransportEvent.cs:22-28`. |
| **Placeholder-*named* events — we don't even know what they DO** — `ChangeUnk0Event`, `ChangeUnkBEvent`, `ChangeUnkCEvent`, `ModifyUnk2Event`, `ExecuteEvent`/`SignalTarget`. | Silent no-ops that may drop story side-effects. | the `*Unk*Event.cs` files in `src/Formats/MapEvents`; identify the opcode handler in MAIN.EXE. |

### Tier 2 — RE for FIDELITY only (game completes; just not byte-exact)

The spells/mechanics **work**; these `INFERRED` tags (from `_RE_COMBAT.md`) mean one factor in
the probability/damage math is a strong guess, not byte-traced:
- Condition-spell roll factors: ThornSnare (M×100/100), Boasting/Shock/Panic (M×80/100). (`_RE_COMBAT.md:1295,1325`)
- Fungification & BanishDemon LP-chance factors (×1000/×250, ×120). (`_RE_COMBAT.md:1310,1321,2205`)
- GoddessWrath living-monster count. (`_RE_COMBAT.md:2004`)
- The remainder of the damage **K-table**. (`_RE_COMBAT.md:2205`)
- Interior of the 14-byte combat-cell / ~66-byte combatant struct (gameplay fields decoded;
  the rest is unknown padding). (`_RE_COMBAT.md:999,1038`)
- Assorted `INFERRED` audio-sample and automap-helper attributions. (`_RE_NOTES.md`)

### Tier 3 — the large `Unk*` data-field surface (NOT logic-blocking)

Most of `RemainingUnknowns.txt` is this: dozens of `Unknown*` fields in `CharacterSheet`
(0x06–0xFC), `NpcState` (Unk4–0x7E), `MiscState`, `SavedGame` blobs, `LabyrinthData`, and
unused flag-enum bits. They are **read and written back faithfully** — not knowing their
meaning doesn't break logic as long as the round-trip holds (smoke 13/13 confirms it does).
Each becomes an RE task **only if the Phase-4 run shows one gates behavior.** Do not pre-RE
these speculatively.

### Confirmed NOT needed (RE already proved the original has nothing here)

Animated 3D meshes (RE 6: no mesh path — all billboards), Levitation (NULL handler in the
original), Smacker decoder (all video is FLIC per `alb_assets.json`).

---

## Suggested attack order

Strategy: **reconcile the ledger → unblock economy + 3D walls → drive an empirical run that
verifies fixes AND discovers unknown blockers → close the finale → fidelity.** Each phase ends
on a **green harness run**, not code-complete.

- **Phase 0 (S) — Ledger reconciliation.** This doc now marks B4/B8/WordKnown as DONE.
  `_TODO_1TO1.md` §4d#3 already correctly records HomeNpcIndex as done — **the audit was wrong
  to call it a live bug; no edit needed there.** Strike the stale combat-RE contradictions noted
  in `_TODO_GAP_AUDIT.md §5` (13-15). **Extract the map event tables and diff the used `QueryType`
  set vs the serdes switch — resolve the [RISK] crash before any run.** *Verify:* docs match
  code; build clean; `dotnet test` green; smoke 13/13.
- **Phase 1 (L) — Economy transaction layer [B5].** `InventoryType.Merchant` buy branch with
  gold debit + affordability/weight gate; subscribe `InventorySellEvent` to credit gold;
  per-merchant multiplier from item `Value`; prices/totals in `InventoryMerchantPane`.
  *Verify:* harness a shop — buy debits, sell credits, insufficient-gold refuses.
- **Phase 2 (M) — 3D world-state + puzzle finishers [B7 + B4 residual].** Subscribe
  `On<ChangeIconEvent>` in `DungeonMap` → `LogicalMap.Modify`; wire `ChangeUsedItemEvent`,
  `TrapEvent`, `signal`/`execute`. *Verify:* harness a 3D dungeon — a lever opens a portcullis;
  pick-axe-on-cracked-wall opens a passage and the chain branches tool-correct/wrong and
  consumes the tool.
- **Phase 3 (M) — Story-scene dispatch [B1′] + word persistence.** Dispatch `PartySleeps` on
  rest and Sira `0x17/0x2D`; persist `_discoveredWords` in `SavedGame`. Add a forced-swap
  prompt for Joe. *Verify:* rest fires the Sira2 chain; a learned word survives save/reload and
  gates a second NPC.
- **Phase 4 (L) — Mid/late live run, opcode shakeout [B6].** Drive the harness chapter-by-chapter
  Jirinaar→Gratogel→Maini→Dji-Cantos→Umajo, save-scumming each transition. Fix the `QueryType`
  parse-throws as they hit, decode/handle `CreateTransport` if any crossing is script-spawned,
  burn down the surfacing `Unk*`. Verify the time-skip and the 8–9 AM trainer window.
  *Verify:* an empirical run reaches the Dji-Cantos Seed-plan intro with no soft-locks.
- **Phase 5 (XL) — The ending [B2 + B3].** Decode `ask_surrender`; add `CombatResult.Surrender`
  + a per-member-KO hook in `Battle`; route Seed-deploy → ThrowingSeed/CpuGoBoom/NedsDead →
  Endgame1-4 FLICs → credits → a terminal **GameComplete** state. Wire Fade/FillScreen for clean
  cutscene transitions; wire the per-map ambient bed. *Verify:* harness the Toronto finale —
  endure the AI fight, a member falls, the Seed deploys, Endgame plays, the game reaches a win
  state. **The literal 100 % checkpoint.**
- **Phase 6 (M) — Combat-difficulty fidelity.** Monster-side condition state so Sleep/Paralyse/
  Blind/Panic land on enemies (ThornSnare traps the AI). Required for scripted-difficulty late
  fights to be winnable.
- **Phase 7 — remaining polish + 1:1 leftovers** (open each [UNVERIFIED] item first): intro
  cinematic, dead menu buttons, `M` keybind, 2D drag-to-walk, trapped chests, SP-shortfall LP,
  SpellEnvironments bit order, night palettes, Epoch date, spinner reorientation, test coverage.
  *Verify:* full regression + a complete start-to-Seed playthrough with screenshots at each
  chapter gate.

**Dependencies:** Phase 0's `QueryType` check guards Phase 4 from a hard crash. B5 (economy)
and B7 (3D walls) are independent and parallelizable. Phase 5 (ending) is unreachable until
Phase 4 proves the path to the Toronto. The corrected B4 means the Toronto tool puzzles need
only the Phase-2 `change_used_item` finisher, not a from-scratch world-verb.

---

## Provenance

- `_TODO_GAP_AUDIT.md` — the raw multi-agent audit (98 findings, 67 "confirmed", 31 overturned).
  **Read it for breadth, but it over-reported blockers** (B4/B8/WordKnown were false). This
  roadmap supersedes it for the blocker spine.
- Hand-verification pass 2026-06-13 re-opened all blockers + the high-stakes major gaps against
  current code; those carry **[VERIFIED✓]**. Items still tagged **[UNVERIFIED]** were not
  re-opened — do so before scheduling them.
