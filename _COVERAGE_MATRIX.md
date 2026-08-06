# UAlbion — Mechanics & Interface Coverage Matrix

> Created 2026-07-10. The ledger for the **full-coverage push**: every game mechanic and
> UI surface, what automated exercise it has, and what still needs driving. Status values:
> **LIVE** = exercised by a repeatable script through the harness; **UNIT** = formula/logic
> unit-tested only (live surface undriven); **SHOT** = screenshot-only (no assertion);
> **NONE** = no automated exercise; **N/A** = intentionally out of scope (noted why).
>
> Existing scripts: smoke (`_smoke_all_saves.ps1`), drive (`_harness_drive.ps1`), explore
> (`_harness_explore.ps1`), narrative (`_narrative_shakeout.ps1`), conversation
> (`_conversation_shakeout.ps1`), collision (`_collision_sweep.ps1` /
> `_allmaps_collision_sweep.ps1`), saves (`_save_behavior_sweep.ps1`), fidelity shots
> (`_ui_fidelity_shot.ps1`), **mechanics (`_mechanics_shakeout.ps1` — this push)**.
>
> **STATUS 2026-07-10:** nearly every "→ target" row below has since been driven LIVE by
> the two same-day passes appended at the bottom (the UI-drive pass on 7878 + the
> event-driven mechanics shakeout on 8123). The tables keep the original gap audit for
> history; the appendices are the current truth.

## A. World / movement / time

| Surface | Status | Covered by | Notes |
|---|---|---|---|
| Save deserialization (13 saves) | LIVE | smoke | 13/13 |
| 2D movement + collision bound | LIVE | saves sweep | party_move + bounded |
| 3D movement + collision bound | LIVE | saves sweep, collision sweeps | /collisionscan all maps |
| 3D collision data integrity (all maps) | LIVE | allmaps collision sweep | blockMismatch==0 |
| Clock liveness / stuck-world | LIVE | saves sweep | |
| Map event chains (all 166 maps) | LIVE | narrative shakeout | fire-only, not outcome |
| party_goto pathing (2D A* / 3D BFS) | LIVE | B6 story driving | |
| Map transitions (walk exits) | NONE → target | mechanics | teleport zones fired by narrative, organic walk untested |
| Time advance / hourly events | LIVE | explore | modify_hours 24 |
| Rest / camp (dungeon 8h) | NONE → target | mechanics | rest event |
| Wait (city hour prompt) | NONE → target | mechanics | party_wait |
| Fatigue / exhaustion penalties | UNIT → target | mechanics | >24h/>48h paths |
| Day/night palette blending | NONE → target | mechanics | screenshot day vs night |
| Torch burnout over time | NONE → target | mechanics | charges decrement + light recompute |
| NPC schedules / detection / chase | LIVE(partial) | saves sweep, B6 | contact combat verified |

## B. Inventory / items

| Surface | Status | Covered by | Notes |
|---|---|---|---|
| Inventory open + page render | SHOT → target | fidelity shots | add assertions |
| Char screen page III content | NONE → target | mechanics | conditions/languages/spells |
| Item pickup / swap / move | NONE → target | mechanics | inv:pickup/swap |
| Equip / unequip | NONE → target | mechanics | equipment slots |
| Give between members (weight cap) | UNIT → target | mechanics | inv:give |
| Examine item | NONE → target | mechanics | inv:examine |
| Discard (+ plot-item guard) | UNIT → target | mechanics | inv:discard |
| Item "Use" verb | NONE → target | mechanics | chain-gated use |
| Cursed item equip trap | NONE → target | mechanics | + destroy_cursed_equipment |
| Chest open / loot / take_all | NONE → target | mechanics | chest event |
| Chest/door locks + lockpicking | UNIT → target | mechanics | inv:pick_lock live |
| Chest traps | NONE → target | mechanics | trapped chest fire |
| Doors (locked, key, open text) | NONE → target | mechanics | door event |
| Gold/rations partial-stack ops | UNIT | InventoryTests | |

## C. Economy / services

| Surface | Status | Covered by | Notes |
|---|---|---|---|
| Merchant buy (gold −, item +) | UNIT(pricing) → target | mechanics | live inv:merchant |
| Merchant sell (inv:sell_to_merchant) | NONE → target | mechanics | |
| svc_heal / svc_food / svc_train | NONE → target | mechanics | PlaceAction paths |
| svc_repair / recharge / identify | NONE → target | mechanics | |
| svc_learn / svc_spells | NONE → target | mechanics | |
| Inn rest (heals, poison ticks) | LIVE(once) | 2026-06-13 manual verify | encode in mechanics |
| Teleporter service | NONE → target | mechanics | teleporter_menu |

## D. Magic

| Surface | Status | Covered by | Notes |
|---|---|---|---|
| Combat spell cast + FX | LIVE(manual) → target | T1.3 verify | encode |
| Field cast (magic_menu/cast_spell) | NONE → target | mechanics | heal + light |
| Item casts (wands, charges) | LIVE(once) | CMB-03 verify | encode |
| Spell learning (learn_spell) | NONE → target | mechanics | |
| Active spell persistence (shields) | UNIT | | hour decay |
| Light system recompute | UNIT → target | mechanics | torch + spell |

## E. Combat

| Surface | Status | Covered by | Notes |
|---|---|---|---|
| Melee rounds | LIVE | explore | 3 rounds/save |
| Ranged + ammo consumption | UNIT → target | mechanics | live strike |
| Combat spell (row/all targets) | UNIT → target | mechanics | |
| Flee / retreat outcome | UNIT → target | mechanics | party escaped |
| Advance party | NONE → target | mechanics | T1.4 landed, undriven |
| Use item in combat | LIVE(once) | CMB-03 | encode |
| Monster morale flight | UNIT | CombatFormulasTests | live = stochastic, skip |
| Loot window + Take All | LIVE(once) | 2026-06-13 verify | encode |
| Combat end condition clears | UNIT | | |
| Game over path (party wipe) | NONE → target | mechanics | game_over track/screen |

## F. Conversation / story

| Surface | Status | Covered by | Notes |
|---|---|---|---|
| Dialogue open/greeting/options | LIVE | conversation shakeout | 12 chapters × all NPCs |
| Word entry (enter_word) | LIVE(partial) | B6 driving | |
| Recruit / leave party | LIVE(once) | B6 | +980 offset fix |
| Switch/ticker/quest state | LIVE | /quest, B6 | |
| Full story beats | IN PROGRESS | _PLAYTHROUGH_B6.md | separate track |
| Intro FLIC / credits clip | NONE → target | mechanics | fire + healthz |

## G. UI shell / system

| Surface | Status | Covered by | Notes |
|---|---|---|---|
| Main menu (classic + modern) | SHOT → target | menu screenshots | click-through |
| New game flow | LIVE(event) | allmaps sweep uses new_game | menu-driven start → target |
| Options menu + sliders | NONE → target | mechanics | volume persist |
| In-game save UI + name entry | NONE → target | mechanics | save_game 90 + reload round-trip |
| Classic save picker paging | LIVE(once) | 2026-07-02 | |
| Automap dialog (show_automap) | UNIT → target | mechanics | open + glyphs + goto labels |
| Teleporter goto markers | LIVE(once) | verification debt §7.4 | encode |
| Extras menu / cockpit | LIVE(manual) | cockpit sessions | debug-only |
| Conversation keyboard (1-9/0/Esc) | LIVE(once) | 2026-07-02 | |
| Window resize / UI scale | NONE | | low value headless; skip |
| Graphics toggles (#34/#65/#43) | LIVE(once) | LOW batch | |
| Audio (audible) | N/A headless | | needs human ears (documented) |

## H. Persistence

| Surface | Status | Covered by | Notes |
|---|---|---|---|
| save_game/load_game round trip | LIVE(partial) | NPC-morph verify | broaden: mid-quest state |
| Save round-trip of new mechanics | → target | mechanics | chest looted, torch charges, active spells, automap discovery |
| CloneAutomap (0x10) | UNIT | T2.c | |

---


## Verified fixes — 2026-07-10 empirical coverage campaign

Every row below was found by driving the real game through the HTTP harness and is guarded
by an automated check in `_regression_e2e.ps1` (run it alongside `dotnet test` and
`_smoke_all_saves.ps1`).

| Area | Defect found | Fix |
|---|---|---|
| Combat | Melee never connected after any move — `IsAdjacent` compared the frozen `CombatPosition`, not the live grid | `Battle.IsAdjacent` uses `TileOf` |
| Combat | Ranged attackers silently downgraded to melee — ammo in the backpack wasn't counted | `RangedUsable` scans `EnumerateAll` |
| Combat | Fleeing a contact fight re-triggered it on the next tick (inescapable loop) | `Npc2D`/`Npc3D` latch the contact trigger on non-victory combat end |
| Progression | Non-casters gained SP on level-up | Gate on the spell-class byte (`_RE_COMBAT.md:1926`) |
| Progression | Spell mastery growth was invisible to later casts (effective sheet never refreshed) | Raise `InventoryChangedEvent` after `GrowMastery` |
| State | Whole party incapacitated out of combat = permanent dead state | `LeaderIncapacityWatcher` (fcn.0003822d): Tom revives at 1 LP |
| State | Event chains survived `load_game` and fired into the new game | `EventChainManager` aborts all contexts on load/new-game/warp |
| State | A corrupt/truncated save killed the process | `GameState.LoadGame` guards deserialization, keeps current state |
| Items | Event-granted spell items arrived with 0 charges | Seed template charges in `SheetApplier.ApplyItem` |
| Time | Rest-till-dawn kept a minute offset (07:MM) | Snap to exactly 07:00 |
| Conversation | Language barrier existed only as an unused string | `ConversationManager` refuses talk with no shared language (SYSTEXTS 540) |
| Services | `0xFF` "use default" text sentinel rendered as `!MISSING STRING …:255!` | `PlaceActionManager` treats 0xFF as the per-service default |
| UI | Party status bar rendered over the main menu after quit-to-menu | Removed `StatusBar` from `MenuScene` |
| UI | Portrait effect cycled three *distinct* status sprites as if one animation | Static dead face when down; no effect for conscious afflictions |

### Verified-faithful (no change needed)

- **Chest traps** — the RE'd mechanism works (proved via `debug_trapped_chest`); no shipped
  chest arms one, so there is no base-game data to exercise it.
- **`create_transport`** — fired by no shipped map; island travel uses teleport-cave chains.
- **Combat auto-approach** — the greedy pathing matches `fcn.00050887`; kept 1:1.
- **RNG** — the original seeds from the BIOS timer at boot, not from saves; no seed
  persistence is required for fidelity.
- **German/French** — this data set ships English only; both options menus already filter
  languages through `Assets.IsStringDefined`.
