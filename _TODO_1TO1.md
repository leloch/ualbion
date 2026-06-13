# UAlbion → 1:1 TODO — the single source of truth

> Everything still standing between the current build and a 1:1 match with the original
> MAIN.EXE implementation. Compiled 2026-06-12 from a ground-truth sweep of the source
> (every `PLACEHOLDER` / `TODO` marker), the RE docs, and the session logs. Update this
> file as items land; nothing in it is tracked anywhere else.
>
> Verification gates for every change: `dotnet test src/ualbion.ci.sln` (587 green),
> `_smoke_all_saves.ps1` (13/13), and a live harness check where behaviour is visible.
>
> **⮕ Expanded goal scope (2026-06-13): two axes, both in scope.** This file tracks
> **axis 1 — 1:1 mechanic fidelity** vs MAIN.EXE. Its companion **`_TODO_100PCT.md`** tracks
> **axis 2 — content-pipeline completeness**: can the engine drive the whole scripted game
> start-to-finish (Toronto crash → Seed ending)? The project is intro-complete but
> **finale-impossible** today. DONE now means *both*: every 1:1 item below resolved AND the
> 6 playthrough blockers (B1–B6) in `_TODO_100PCT.md` cleared, each verified on a green
> harness run (not code-complete). Order: finish the 1:1 remainder here, then execute the
> `_TODO_100PCT.md` phase plan (Phase 0 harness-trace → P1 conversation/recruit dispatch →
> P2 world-verb + economy → P3 mid/late run → P4 ending → P5 progression fidelity →
> P6 bookends/day-night → P7 polish).

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
- ~~Battle-view WALK lerp~~ DONE `6927f129`.
- ~~Fizzle SP timing~~ VERIFIED CORRECT `57bc62e5`.
- ~~Soul-rise banish VFX~~ DONE `12ff1433` (RE 6 worker 0xa1b20 — body lift/shrink/fade
  + 3 wisps; pre-gate descending orb omitted as a lead-in flourish).
- ~~Ambient NPC sound-set~~ DONE `a5f7ff22` (RE 6 table @0x13db10 + keyed audio API +
  cadence scheduler). **Audible output UNVERIFIED here** (no audio device in tests/
  harness) — needs a listening pass on a real machine.
- ~~Animated 3D meshes~~ N/A (RE 6: original has no mesh path) — closed.
- 5D leftovers (low/cosmetic): MapNpc flag 0x40 = collision class 1 / NoClip is a
  collision-class selector (RE says non-behavioral for the remaining bits).

## 4. Implementation possible NOW (no RE needed)

| Item | Where | Notes |
|---|---|---|
| ~~3D `change_npc_*` dispatch~~ | `DungeonMap.cs` | DONE `021a3481` — live + persisted + replay, group rebuild |
| ~~AI ranged commit~~ | `Battle.cs` | DONE `f0015333` — real usability (typeid 6 + ammo) |
| ~~Dead `TacticalSpriteId`~~ | — | DONE `021a3481` — removed from interface + impls |
| ~~VideoManager positioned pics~~ | `VideoManager.cs` | DONE `9212f855` — non-zero x/y draws at native size at UI coords |
| ~~Selection3D per-tile picking~~ | `Selection3D.cs` | RESOLVED — per-tile 3D selection is done by SelectionHandler3D (zones/NPCs/context menu); Selection3D does scene-geometry ray hits. The commented ground-plane alternative was redundant dead code, removed. |
| ~~Conversation default block~~ | `Conversation.cs` | RESOLVED `57bc62e5` — all four real BlockIds handled; default now warns on malformed data |
| ~~Goddess' amulet / vital items~~ | `InventoryManager.cs` | DONE `78c13390` — PlotItem-flagged items can't be discarded (InvMsg 193) |
| ~~TextFormatter Damage token~~ | `TextFormatter.cs` | ACCEPTED — defensive arg-order fallback, never crashes, no live text mis-renders; exact token-arg binding undecoded but harmless (documented). |
| ~~Animated 3D meshes~~ | `MapObject.cs` | N/A (RE 6): the original has no mesh path — all dungeon objects are billboards with sprite-frame animation, already handled. TODO closed. |
| Z-fighting hack | `MapObject.cs:178` | "still happens sometimes" — cosmetic, harmless |
| ~~Weight limit on give~~ | `InventoryManager.cs` | DONE `755a37f1` — TryGiveItems enforces MaxWeight |
| ~~ChangeNpcMovement "other flags"~~ | `NpcManager2D.cs` | RESOLVED (documented) — RE 5D: remaining MapNpc flag bits are non-behavioral metadata except 0x40 (collision class, via NoClip). No mapping needed. |
| ~~Battle-view walk lerp~~ | `BattleView.cs` | DONE `6927f129` (also listed at section 3) — WalkPath lerp at Move-anim-length frames/tile |
| ~~Ghost translucency (class 2)~~ | `BattleView.cs` | DONE `973b57a5` — renderClass==2 draws at 0.6 opacity |
| SheetApplier ChangeItem subtract | `SheetApplier.cs:142` | Subtract/SubtractPercentage silently no-op (unreachable in base data; mod/script safety) |
| ~~Banish 62/63/64 area targeting~~ | `InstantKillSpellEffects.cs` | ALREADY DONE — Battle.CastQueuedSpell enumerates row/all by the Targets byte and calls BanishDemonEffect per enemy (verified `dc3014fe`-era; comment was stale). |
| Unknown1C HomeNpcIndex wiring | `Party.cs:90/126` | DEFERRED (scoped): RE 5D §6 — join sets sheet+0x1C=(mapId−1)*96+slot + RemovedNpcs bit, leave clears it. AddMember/RemoveMember currently don't; faithful fix needs AddPartyMemberEvent to carry the source map+NPC-slot (not present) and mutates save-state. Recruit/dismiss is rare and recruitment scripts already npc_off the source. M [needs-design], save-compat risk — do deliberately, not rushed. |

## 4b. Test hardening (goal §4) — ✅ LANDED

Extracted the previously-untestable RE'd decision logic into pure statics and covered them
(587 tests green, was 502; smoke 13/13):
- `CombatFormulas.cs` (NEW) — monster morale flight (strategy 2/7/default + class-bit-0x80
  never-flee) and lock-picking (unpickable ≥100 / auto-success / `skill·(100−diff)/100`
  percent). `Battle.MoraleBroken` + `InventoryLockPane.CanPick` now delegate. **Fixed a
  lockpick off-by-one**: the chance roll was `(ushort)(chance+1)` → now `(ushort)chance`
  (`IRandom.Generate(100)` is 0..99, so `roll < chance` is exactly `chance%`). `CombatFormulasTests`.
- `AutomapGlyphs.cs` (NEW) — connection-mask packing, wall-glyph selection (type 1 = 560+mask,
  2..19 markers, degrade past region count), party-marker/goto glyph indices; `AutomapDialog`
  delegates. Filled the empty `AutomapTests`.
- `DungeonLighting.EffectiveLight`/`IsLightEnough` — added pure `(mode, spellPct, itemTotal,
  isIskai)` overloads; `DungeonLightingTests`.
- `SpellTargeting.cs` (NEW) — `Classify(Targets, selfManaged)` (All>Row>WholeParty>Single
  priority) + `IsOffensive`; `Battle.CastQueuedSpell` switches on it. `SpellTargetingTests`.
- Query-opcode comparator (`FormatUtil.Compare`) — `QueryComparisonTests`.
- Ammo consumption (backpack-first, one round/strike) — `InventoryTests` ConsumeAmmo cases.
- crit/instant-kill + equipment-break primitives already covered by `DamageCalculatorTests`
  (`PercentRoll`, range-agnostic) — documented, no new extraction needed.

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

1. **Loot window**: ✅ PARTIAL 2026-06-13 — Warniak victory resolves cleanly through
   the new flee/morale/multi-strike pipeline (no errors, scene pops, MonsterEye live);
   Warniaks carry no inventory so the no-drop case (no empty window) is verified.
   STILL TODO: a drop-carrying group (e.g. humanoid bandits) → window lists items →
   Take All lands in party inventory.
2. **NPC morph**: ✅ VERIFIED 2026-06-13 — change_npc_movement fired live on
   Nakiridaani (2D) flipped NPC 0 Waypoints→Stationary, lastError null (dispatch path
   works). Cross-reload replay is structurally exercised by every map load in smoke
   (NpcManager2D.Subscribed replays the recorded changes); the explicit leave-and-return
   walk-through remains a nice-to-have manual check.
3. **Hourly events during rest**: ✅ VERIFIED 2026-06-13 — poisoned Mellthas is healed
   by the inn stay then drained back to 0 by the 8 hourly poison ticks (pre-fix he
   ended at 12; the bulk-advance now fires per-hour events).
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
