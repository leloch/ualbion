# UAlbion → 1:1 TODO — the single source of truth

> Everything still standing between the current build and a 1:1 match with the original
> MAIN.EXE implementation. Compiled 2026-06-12 from a ground-truth sweep of the source
> (every `PLACEHOLDER` / `TODO` marker), the RE docs, and the session logs. Update this
> file as items land; nothing in it is tracked anywhere else.
>
> Verification gates for every change: `dotnet test src/ualbion.ci.sln` (493 green),
> `_smoke_all_saves.ps1` (13/13), and a live harness check where behaviour is visible.

## 0. Current state in one line

All 11 original goal-tiers and the 19-item placeholder list are done except the
RE-blocked remainder below. Combat (strike pipeline, crits, XP, spells incl. margin
scaling, services, rest/fatigue, battle view, loot window, automap glyph rendering)
matches the RE'd formulas; the open items are mostly *small mechanics* and *constants*.

---

## 1. RE clusters — status

### Cluster A (combat executors) — ✅ DECODED (`_RE_5A.md`) and ✅ APPLIED (`f0015333`)
Melee Chebyshev reach + approach-move conversion; multi-strike (ActionPoints ×2 Hurry,
stop on target death/ammo/battle end — NOT on hit); ranged usability + per-strike ammo
(backpack first, snd 454); panic/insane autopilot (greedy flee stepper, Retreat at the
edge, 50/50 insane move-vs-attack over BOTH sides); fleeing leaves the battle same round
(monsters still pay XP; "party escaped" outcome); monster morale flight
((deadPct+lostPct)/2 ≥ Morale + behaviour variants); hover bob (sine ±4-6 units,
3.0-4.95 s, classes 2/3/4). "Summon" was dead code — no action exists.
*Still open from A:* battle-view WALK animation for multi-tile monster moves (lerp at
Move-anim length per tile — state side done, view-side pending); class-2 ghost
translucency; party Move UI single-tile picker claim-mask (minor).

### Cluster B (SPELLDAT & cast core) — ✅ DECODED (`_RE_5B.md`) and ✅ APPLIED (`9212f855`)
Row/all target areas, Big traps = whole row, wrath picker (rejection sampling + grid
order), item casts M=50 + charge flags, school 4/6 = dead data (premise wrong),
LightningStrike ungated, shields = hour-duration entries in SavedGame.ActiveSpells
(persist + decay hourly + no refresh).
*Still open from B:* fizzle shouldn't charge SP (we still charge on attempt — verify
original's SP timing vs zero-continuation casts); monster-AI spell pick should be
uniform-random over candidates matching preferred target bits (ours = first affordable).

### Cluster C (world tick & state) — ✅ DECODED (`_RE_5C.md`) and ✅ APPLIED (`fe822d22`)
Real Light system (ambient entry accumulates, dungeon formula min(100, max(spell,
items)) + Iskai bonus, hourly decay, recompute on cast/hour/inventory); lockpicking
formula (auto ≥ difficulty, else skill·(100−diff)/100 roll); the four query opcodes
(facing / leader-language / schedule-tick / is-it-light); rest heals before the clock
advance, no mid-rest interruption exists. Levitation (37) confirmed DEAD in the
original (NULL handler + env 0) — our implementation stays dormant.

### Cluster D (NPC & misc constants) — ✅ DECODED (`_RE_5D.md`) and ✅ APPLIED (`c9fd77a9`)
Collision margin = MAX(tile/4, 50) (bug fixed — quarter tile); detection 10 Euclidean
2D / Bresenham LOS 3D / unlimited cities; binary MonsterEye; 3D NPC walk 0.7 tiles/s;
PlaceAction Unk2 = intro text (shown); SetPartyLeader unk2/3 ignored; sheet+0x1C =
removed-NPC index.

## 3. Remaining implementation leftovers from the clusters
- Battle-view WALK lerp for multi-tile monster moves (state side done; view needs a
  CombatMoveEvent → per-tile lerp at Move-anim-length engine frames).
- ~~Fizzle SP timing~~ VERIFIED CORRECT `57bc62e5` (the fizzle return precedes the
  SP charge — zero-target casts cost nothing).
- 5D extras not yet wired: MapNpc V2 byte1 = ambient sound-set index (positional loop
  samples, table 13×0x28 @0x13db10); NpcState ActiveSfx0-3 = the sample handles;
  MapNpc flag 0x40 = collision class 1; NoClip is really a collision-class selector.

## 4. Implementation possible NOW (no RE needed)

| Item | Where | Notes |
|---|---|---|
| ~~3D `change_npc_*` dispatch~~ | `DungeonMap.cs` | DONE `021a3481` — live + persisted + replay, group rebuild |
| ~~AI ranged commit~~ | `Battle.cs` | DONE `f0015333` — real usability (typeid 6 + ammo) |
| ~~Dead `TacticalSpriteId`~~ | — | DONE `021a3481` — removed from interface + impls |
| ~~VideoManager positioned pics~~ | `VideoManager.cs` | DONE `9212f855` — non-zero x/y draws at native size at UI coords |
| Selection3D per-tile picking | `Selection3D.cs:22` | Ground-plane intersection only; wall-face ray refinement |
| ~~Conversation default block~~ | `Conversation.cs` | RESOLVED `57bc62e5` — all four real BlockIds handled; default now warns on malformed data |
| Goddess' amulet activation | `InventoryManager.cs:223` | Special-item activation chain ("TODO: Goddess' amulet etc"); story-critical late-game item |
| TextFormatter Damage token | `TextFormatter.cs:40` | Guessed semantics; scan game texts for actual usage to confirm |
| Animated 3D meshes | `MapObject.cs:102` | 3D map objects don't animate |
| Z-fighting hack | `MapObject.cs:178` | "still happens sometimes" |
| Weight limit on give | `InventoryManager.cs:514` | Party members can exceed carry weight |
| ChangeNpcMovement "other flags" | `NpcManager2D.cs` InitialiseState | Only SimpleMsg flag is mapped from MapNpcFlags |
| Battle-view walk lerp | `BattleView.cs` | Multi-tile monster moves should WalkPath-lerp (N engine frames per tile, N = Move anim length) — state side done |
| Ghost translucency (class 2) | `BattleView.cs` | Render kind 8 (translucent) for ghostly monsters |

## 5. Doc drift / comment cleanups (5 minutes each, do with next touch)

- `Battle.cs:1037` + `InventoryManager.cs:705` — say "PLACEHOLDER until the battle-loot
  window exists"; **the window exists** (batch A). Reword: broken gear staying equipped
  is a deliberate equivalent-outcome deviation.
- `PlaceActionManager.cs:34` — "Unk3/Unk4 ... aren't shown" is FALSE since batch A.
- `CombatBuffs.cs:13` — "magnitudes/durations PLACEHOLDER pending RE" is stale
  (durations RE'd; shield pct landed). Rewrite.
- `BattleView.cs:26` — placeholder list shrinks once A6 lands.
- `README.md` "Current Status" — still lists combat/magic/3D events/3D automap as
  UNIMPLEMENTED; all exist. Rewrite the section (good first impression for visitors).
- `_PROJECT_LOG.md`/`_SESSION_STATUS.md` — add an eighth-pass section when the RE
  sweep lands.

## 6. QoL / maintainer wishlist (not 1:1 fidelity, but wanted)

- 2D mouse-click path-finding movement (README wishlist).
- Key rebinding UI (`InputBinder` exists, no UI).
- Save-slot picker: scrollbar + 99 slots (`PickSaveSlotMenu.cs:23`); dedupe the save
  path logic (`GameState.cs` + `PickSaveSlotMenu.cs`).
- Keyboard support in conversation topic window (`ConversationTopicWindow.cs:20`).
- LoadMapPromptDialog → textbox (`LoadMapPromptDialog.cs:9`).
- i18n: price formatting (`TextFormatter.cs:164`).
- `DumpJson.cs:46` NRE when dumping event sets.
- New-game start coords → config (`MainMenu.cs:76`).
- NightPalettes → config/asset (`NightPalettes.cs:7`).

## 7. Verification debt (live checks to run after the next batches)

1. **Loot window**: kill a drop-carrying monster group via harness → window lists
   items/gold → Take All lands in party inventory.
2. **NPC morph persistence**: change_npc_sprite on a 2D map, leave, return → sprite
   still changed.
3. **Hourly events during rest**: poison drains and EveryHour chains fire across an
   inn stay (clock-advance path).
4. **Goto markers**: walk onto a Jirinaar marker tile → automap shows glyph 18 →
   Teleporter lists it; save/load → bit persists (offset 0x5972).
5. **Shield pct**: cast MagicShield in combat → trace shows defense multiplied, spell
   gate resist boosted.
6. **Full intro replay** after the RE sweep lands (regression vs `_PLAYTHROUGH.md`).

## 8. Known deliberate deviations (documented, NOT bugs — do not "fix")

- Broken equipment stays equipped+Broken instead of moving to the loot list
  (equivalent outcome; repairable via the RepairItem service).
- Ground shadows: 50%-black blend instead of the original's palette-remap LUT
  (0x17d25c) — the LUT is a dest-pixel remap the sprite pipeline can't express;
  visually identical minus palette quantisation.
- Berserk expressed additively (+half values) instead of in-place ×1.5 — identical at
  application, *more* accurate at expiry (no rounding loss).
- The remake draws a party marker on the automap (original uses a UI cursor).
- CombatBackground.Dungeon fallback for maps without one (original fatally asserts).
- Audio fail-soft: missing/busy audio device runs muted (original requires a device).
