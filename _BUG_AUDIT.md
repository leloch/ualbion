# UAlbion Game-Logic Bug Audit

> Produced 2026-06-13 by a multi-agent audit (14 subsystem finders → per-finding adversarial
> verification → synthesis). 25 candidates raised, 24 survived verification, merged to 19 distinct
> bugs. Severities below are the **verifier-confirmed** levels, not the finder's initial guess.

## Fix status (updated 2026-06-25 after a re-verification pass against the evolved code)

Re-checked every finding against current `master` (the repo moved ~12 days). Result:

- **Already fixed upstream (5):** MERCH-01 (unlimited-stock guard added), MOV2D-01 (NPC null-guard),
  LOCK-01 (unlock-text flow), LOCK-02 (chest text-byte order), LOCK-03 (one-use keys) — landed in
  commits `624a3fcf`, `d174906f`, etc.
- **Fixed this pass (15):** CMB-CRIT-01, STATE-01, INV-01, INV-02, INV-03, QRY-01, QRY-02, MOV3D-01,
  MOV2D-02, MOV3D-02, MERCH-02, CMB-01, CMB-02, CMB-03, DLG-01, DLG-02. Verified: full CI suite
  **623/623 green**, save smoke **13/13 clean**.
- **Deferred (1):** DATA-01 — the percentage-op base. The shared `NumericOperation` formula clamps to
  `max`, so a targeted fix is ambiguous (could alter the common current-stat heal path) and needs an
  RE check of the original's percentage semantics. Left untouched pending that confirmation.

> Note: CMB-03 fixed the condition-hook half (item debuffs now land on monsters); the area-enumeration
> half (item area-spells still resolve on a single tile) is a larger follow-up, noted inline.
> Build note: the Release build is currently red at HEAD due to **pre-existing** CA analyzer errors in
> unrelated menu files (`NativeMenuDialog.cs`, `ExtrasMenu.cs`, `TextRasterizer.cs`) — not from this work.

## 1. Executive Summary

**Counts by confirmed severity:**

| Severity | Count |
|---|---|
| Critical | 1 |
| High | 4 |
| Medium | 8 |
| Low | 6 |
| **Total** | **19** |

**Headline themes:**

- **Combat state collides across duplicate monsters.** The single critical bug: all per-instance combat state (HP, conditions, buffs) is keyed by a shared `SheetId`, so same-type monsters in a group are treated as one entity — spells, status, and damage leak across the whole rank.
- **Event-routing duplication and misrouting** corrupt inventory operations: `change_item` applies twice; spell scrolls teach the wrong character; the take-from-party success result is inverted (breaking quest-gate scripts).
- **Ordering / sequencing defects** break the rest mechanic (fatigue counter zeroed after the clock advance, so resting re-exhausts the party).
- **Unguarded array/dictionary access** produces latent crashes in the query dispatcher, NPC toggling, word lookup, and conversation panes.
- **Off-by-one and wrong-formula** divergences from the byte-exact RE reference appear repeatedly (wall index, place-block bounds, damage variance floor, percentage-on-max base).

---

## 2. Critical / High

### 2.1 Combat (core / spells)

#### CMB-CRIT-01 — Combat HP/status/buff shadows collide across same-type monsters
- **Severity:** Critical
- **File:** `src/Game/Combat/Monster.cs:45` (with `Battle.cs:32-47, 1107, 1411, 1431-1432`; `CombatBuffs.cs:51, 65-99`)
- **What it does vs. should do:** Each monster instance in a battle should own its HP, conditions, and combat buffs. Instead `Monster.SheetId` returns `_sheet.Id`, and `MonsterFactory.BuildMonster` clones the sheet via `DeepClone`, which preserves the original type `Id`. All same-type instances therefore share one `SheetId`, and every per-instance container keys solely on it: `Battle._liveHp`, `Battle._liveConditions`, and `CombatBuffs`.
- **Player-visible impact:** FrostSplinter/Crystal/Avalanche freeze every monster of the targeted type; Sleep/Paralyse/Blind/Panic/Poison/Irritate debuffs land on the whole same-type group from one cast (and the "already present" no-op gate then blocks re-targeting); a direct-damage spell shares one HP pool across clones, so killing one duplicate marks all of them dead. HP bookkeeping for duplicate monsters is corrupt and fights are inconsistently trivialized or griefed.
- **Evidence:** `public SheetId SheetId => _sheet.Id;` ; DeepClone `Serdes(Id, this, ...)` keeps same Id ; `_liveConditions[p.SheetId] = cur | flag;` ; `CombatBuffs.Add(target.SheetId, BuffKind.Freeze, 0, rounds);`
- **Verifier note:** Confirmed critical. Most monster groups contain duplicate types (the `Monster.cs` comment block itself lists Krondir1 at two grid positions). `_ENGINE_GOTCHAS.md` describes the SheetId-keyed shadow as intended design but never acknowledges the duplicate-monster collision; not tracked anywhere. Worst case (one kill marks all clones dead) trivializes fights. Needs an instance/tile key, not `SheetId`.

### 2.2 State / Sheets (rest & fatigue)

#### STATE-01 — Rest zeroes the fatigue counter AFTER advancing the clock, re-exhausting the party during the rest
- **Severity:** High
- **File:** `src/Game/State/GameState.cs:585-586`
- **What it does vs. should do:** Per the RE'd executor (`fcn.00068b05`, `_RE_COMBAT.md:1974`), the original zeroes `HoursSinceResting` **before** advancing the clock. `OnRest` instead cures Exhausted and heals, then calls `OnModifyHours(AddAmount, hours)` (line 585) — which runs per-hour `ProcessHourElapsed` (incrementing `HoursSinceResting`) and raises `HourElapsedEvent` → `StatusConditionTicker.TickAll` — all while `HoursSinceResting` still holds its stale pre-rest value (>48). Only afterward (line 586) is it set to 0.
- **Player-visible impact:** Resting precisely when fatigued (the situation players rest in) re-applies the Exhausted condition (re-incurring the STR×¾ / other-stats×½ penalties the rest just removed), fires the >24h "everyone is getting tired" message, and drains ~10% LP per crossed odd hour — the opposite of curing and healing. The corrupted condition and counter persist (saved).
- **Evidence:** `OnModifyHours(new ModifyHoursEvent(NumericOperation.AddAmount, (ushort)hours));` then `_game.HoursSinceResting = 0;`
- **Verifier note:** Two independent findings merged (`state-sheets` and `time-world-tick`, same file:line). **Fix: swap lines 585/586.** `_TODO_1TO1.md:26` wrongly claims rest/fatigue "matches the RE'd formulas," which this contradicts.

### 2.3 Inventory / Items

#### INV-01 — `change_item` map events are applied twice (targeted member AND whole party)
- **Severity:** High
- **File:** `src/Game/State/PartyInventory.cs:31-35` (and `src/Game/State/GameState.cs:165`)
- **What it does vs. should do:** A single `change_item` event should add/remove the item exactly once against the resolved target. Instead two always-live handlers both subscribe to `ChangeItemEvent` on the same `EventExchange`: `GameState.On<ChangeItemEvent>(OnDataChange)` → `SheetApplier.ApplyItem` (targeted member), and `PartyInventory.On<ChangeItemEvent>` → `ChangePartyItemAmount` (party-wide). The "Raise skips own handlers" rule does not help — the event is raised by the script source, not by either component.
- **Player-visible impact:** Item-grant/-removal events double their effect — players get twice the reward, or lose twice the intended quantity (possible soft-lock if a unique quest item is over-consumed). With `Target.Everyone` the duplication compounds.
- **Verifier note:** High confirmed. Not a tracked deliberate deviation. **Fix: remove the `ChangeItemEvent` subscription from `PartyInventory` lines 31-35**, leaving `ModifyItemCountEvent`/`ModifyGold`/`ModifyRations` as the party-wide paths.

#### INV-02 — Reading a spell scroll teaches the spell to the wrong character
- **Severity:** Medium *(downgraded from High)*
- **File:** `src/Game/State/Player/InventoryManager.cs:1032-1057`
- **What it does vs. should do:** After validating that some party member `target` (found via `SpellClasses`) has the right class/level and doesn't already know the spell, the spell should be taught to **that** member: `Raise(new LearnSpellEvent(target.SheetId, item.Spell))`. Instead the event is raised against `e.SlotId.Id.ToSheetId()` — the inventory **owner** of the scroll, not `target`. The scroll is consumed (`slot.Amount--`) and a success message shown regardless.
- **Player-visible impact:** When the scroll sits in member X's pack but only member Y can validly learn it, the validated member never learns it; the owner is force-taught the spell (bypassing the class/level checks), the scroll vanishes, and a misleading "X learned the spell" message appears.
- **Verifier note:** Downgraded High→Medium: in normal play the caster reads their own scroll (owner==target, correct path). **Correction:** the owner *does* learn it (improperly, with no class/level re-check); the validated `target` is the one who never does.

#### INV-03 — Inverted success result for party item/gold removal (`TakeFromParty`)
- **Severity:** High
- **File:** `src/Game/State/PartyInventory.cs:134-151`
- **What it does vs. should do:** After removing items/gold, the previous-action result should be TRUE when the full amount was taken. Instead the code reports `SetLastResult(slot.Amount == 0)`, but `slot` is the **acceptor** that *accumulates* the removed items, so its `Amount` grows as items are taken. The result is therefore TRUE only when **nothing** was taken — fully inverted. (`GiveToParty` uses the same line correctly because there `slot` is the donor being drained.)
- **Player-visible impact:** `ModifyGoldEvent`/`ModifyItemCountEvent`/`ChangeItemEvent` Subtract/SetToMinimum ops feed `LastEventResult`, read by `QueryPreviousActionResultEvent`. Scripts that branch on "did the player pay / hand over the item" (tolls, fees, give-item quest gates) take the wrong branch — the transaction succeeds but the script behaves as if it failed, causing stuck/soft-locked quest progression.
- **Verifier note:** High confirmed but bounded. The correct test is `amount == 0`.

### 2.4 Map Events / Query

#### QRY-01 — `QueryNpcX`/`QueryNpcY` (0x29/0x2A) index the NPC array unguarded
- **Severity:** Medium *(downgraded from High)*
- **File:** `src/Game/Querier.cs:109-110`
- **What it does vs. should do:** Should bounds- and null-check the slot before reading X/Y, exactly as the sibling facing query (0x0C) does. Instead both handlers do `Resolve<IGameState>().Npcs[q.Immediate].X` (and `.Y`) with no guard. `Npcs` is a fixed `NpcState[96]` whose entries can be null, and `q.Immediate` is a byte (0..255): any value ≥96 throws `IndexOutOfRangeException`, any null slot <96 throws `NullReferenceException`.
- **Player-visible impact:** A map script running an `is_npc_x`/`is_npc_y` branch against an empty or out-of-range NPC index throws an unhandled exception in the event chain, aborting the chain and soft-locking the interaction.
- **Verifier note:** Downgraded High→Medium: latent, data-dependent crash. Every sibling consumer guards (the 0x0C handler, `DungeonMap`, `NpcManager2D`), proving the guard is required.

### 2.5 Movement (3D)

#### MOV3D-01 — `GetWall` off-by-one drops the highest wall index
- **Severity:** High
- **File:** `src/Game/Entities/Map2D/LogicalMap3D.cs:97-100`
- **What it does vs. should do:** Wall tile indices are 1-based (`Walls[tileIndex-1]`, valid range 1..`Walls.Count`). The guard should accept `tileIndex == Walls.Count` (`tileIndex <= _labyrinth.Walls.Count`), mirroring the sibling `GetObject` which correctly uses `<= ObjectGroups.Count`. Instead the guard is `tileIndex < _labyrinth.Walls.Count`, so the highest-numbered wall falls to the null branch and returns `(Count, null)`.
- **Player-visible impact:** The highest-indexed wall still **renders** solid, but `Collider3D.IsOccupied` sees `wall == null` and reports the tile walkable, and `AutomapDialog.BlocksSight`/`PickTileRegion` treat it as floor. The player walks straight through a visibly-solid wall; it never appears on the automap and never blocks line-of-sight — potential soft-lock / out-of-bounds where that wall gates an area.
- **Verifier note:** High confirmed. `_HANDOFF.md:316` documents tile-content ids as 1-based and `GetObject` as the correct precedent. Data-dependent (only maps using the max wall index), but the walk-through-wall desync justifies High.

---

## 3. Medium / Low

### 3.1 Merchant / Economy

#### MERCH-01 — Buying from a merchant decrements an "unlimited supply" stock slot
- **Severity:** Medium
- **File:** `src/Game/State/Player/InventoryManager.cs:289-291`
- **What it does vs. should do:** Merchant slots flagged `ItemSlot.Unlimited` (0xFFFF, shown with `*`) represent infinite stock and must not be decremented — the protection `ItemSlot.TransferInner` applies. Instead `OnBuyFromMerchant` does `slot.Amount -= given` directly, so an Unlimited slot (65535) becomes 65534 after one purchase.
- **Player-visible impact:** Items meant to be infinitely purchasable stop being infinite, show a giant numeric count instead of the `*` marker, and can eventually be exhausted.
- **Verifier note:** Medium confirmed (silent degradation). **Correction:** no crash — `ApiUtil.Assert` is a no-op in Release, so this is a silent state-leak.

#### MERCH-02 — Buying from a merchant never refreshes the buyer's inventory
- **Severity:** Medium
- **File:** `src/Game/State/Player/InventoryManager.cs:282-292`
- **What it does vs. should do:** After handing the bought item to the leader, an `InventoryChangedEvent` should be raised for `leaderInv` so the leader's pane redraws and carry weight recomputes — mirroring `OnSellToMerchant`. Instead only `Update(e.Id)` for the **merchant** inventory runs; the buyer's change broadcasts only incidentally if the leader was debited by `SpendPartyGold`.
- **Player-visible impact:** When a non-leader funds the buy, the purchased item can fail to appear and carry weight stays stale until the next unrelated refresh — purchases look like they did nothing despite gold being spent.
- **Verifier note:** Medium confirmed. Self-corrects on the next inventory interaction.

### 3.2 Map Events / Query

#### QRY-02 — Gender/Class/Race queries (0x16/0x17/0x18) only check the party leader
- **Severity:** Medium
- **File:** `src/Game/Querier.cs:53-55`
- **What it does vs. should do:** Per `_RE_5C.md §3` (dispatcher 0x3ca53), 0x16/0x17/0x18 should loop members 1..6 and return true if **any** member matches. Instead all three handlers read only `Party.Leader.Effective.Gender/PlayerClass/Race`.
- **Player-visible impact:** Dialogue/quest branches gated on the party containing a particular gender/class/race take the wrong branch whenever the qualifying member is not the current leader — wrong NPC lines or skipped content.
- **Verifier note:** Medium confirmed. The inline comment shows this was a throw-avoidance stopgap, not a documented deviation.

### 3.3 Map Events / DataChange

#### DATA-01 — Percentage ops on MaxHealth/MaxMana (and Gold/Food) use 65535 as the percentage base
- **Severity:** Low
- **File:** `src/Game/State/SheetApplier.cs:57-64`
- **What it does vs. should do:** A percentage op on a max stat should compute the percentage of a sensible base. Instead `ApplyToMax` calls `Apply16(Max, amount)` with the default `max=ushort.MaxValue`, so `AddPercentage` evaluates `Max + amount*(65535-0)/100`; for `amount=10` it adds 6553 (clamped to 65535). `Gold`/`Food` share the same default-max issue.
- **Player-visible impact:** Any map script using a percentage op on MaxHealth/MaxMana (or Gold/Food via DataChange) produces absurd values instead of a proportional change.
- **Verifier note:** Low confirmed. The sibling `CharacterAttribute.Apply` correctly passes `Max` as the base. Uncommon in shipped scripts.

### 3.4 Combat (core / spells)

#### CMB-01 — `VaryDamage` forces a minimum of 1, diverging from the byte-exact `RandomVary` (no floor)
- **Severity:** Low
- **File:** `src/Game/Combat/DamageCalculator.cs:81-87`
- **What it does vs. should do:** Per `fcn.00035c22`, `RandomVary(v) = (rand()%51 + 50) * v / 100` with **no** lower floor; for small `v` it legitimately returns 0 (the method's own doc-comment says "Original game floors negative damage at zero (not 1)"). Instead `VaryDamage` returns `Math.Max(1, …)`. `Battle.cs:1627-1629` applies it to **both** the attack roll and the defender protection roll before `Math.Max(0, variedAtk - variedDef)`.
- **Player-visible impact:** A raw atk/def value that should round to 0 is bumped to 1 — an off-by-one on low-armour/low-damage exchanges on both sides.
- **Verifier note:** Two near-duplicate findings merged. Low confirmed. A unit test (`DamageCalculatorTests.cs:101-102`) enshrines the wrong behavior, asserting `VaryDamage(1,0)==1` — fix the test too.

#### CMB-02 — Area-spell outcome aggregation lets a Failed recipient downgrade a Resisted attempt, refunding SP
- **Severity:** Low
- **File:** `src/Game/Combat/Battle.cs:1289-1325`
- **What it does vs. should do:** If at least one recipient resisted (a genuine attempt), SP should be consumed; only an all-no-op cast (every recipient Failed) refunds SP and skips mastery growth. Instead `outcome` starts Resisted and the merge overwrites a prior Resisted with a later Failed, so a no-Hit mix containing any Failed ends as Failed and skips SP deduction and mastery growth.
- **Player-visible impact:** A multi-target offensive spell that resists some targets but no-ops on another (e.g. one already has the condition) costs the caster nothing — free area debuff spam in that mixed case.
- **Verifier note:** Low confirmed. **Correction:** not order-dependent — the merge collapses **any** Resisted+Failed mixture (no Hit) to Failed.

#### CMB-03 — Magic-item spell casts skip `ApplyCondition` and area enumeration
- **Severity:** Low
- **File:** `src/Game/Combat/Battle.cs:1353-1386`
- **What it does vs. should do:** A magic item casting a status-inflict or area spell should behave like the player cast — status routes through `ApplyCondition`, area spells enumerate every recipient. Instead `UseQueuedItem` builds its own `SpellCastContext` that omits `ApplyCondition` and casts once on a single target with no area enumeration. `InflictStatusEffect` then falls back to the `RaiseEvent` path which only works for `PartySheet` targets.
- **Player-visible impact:** Item-based debuff scrolls/charges silently fail against monsters (return Hit but apply nothing), and area item-spells affect only one tile.
- **Verifier note:** Low confirmed. `_RE_5B.md:186-218` confirms the original funnels spellbook and item casts through the same cast core.

### 3.5 Dialogue / Conversation

#### DLG-01 — `WordLookup.GetHomonyms` throws `KeyNotFoundException` after an in-game language change
- **Severity:** Medium *(downgraded from High)*
- **File:** `src/Game/Text/WordLookup.cs:14, 29-32`
- **What it does vs. should do:** After `LanguageChangedEvent`, the next `GetHomonyms`/`GetText` should rebuild both `_lookup` and `_reverse` together. Instead `On<LanguageChangedEvent>(_ => _lookup.Clear())` clears only `_lookup`, leaving `_reverse` populated. `GetHomonyms` does `_reverse.TryGetValue(word, out var text) ? _lookup[text] : …`, so `TryGetValue` succeeds but `_lookup[text]` throws.
- **Player-visible impact:** Player changes language mid-game, then clicks "What do you know about <word>?" before any other word parse runs → `KeyNotFoundException` → conversation/script crash.
- **Verifier note:** Downgraded High→Medium. **Correction:** largely self-healing — the next-frame `UiText` re-render calls `Parse`→`Rebuild`, so the throwing window is narrow.

#### DLG-02 — Profession option dereferences a null string set when the NPC has no event set
- **Severity:** Low
- **File:** `src/Game/Gui/Dialogs/Conversation.cs:78, 127-144`
- **What it does vs. should do:** The "What's your profession?" option should be suppressed for word-set-only NPCs (`EventSetId.IsNone`), or `BlockClicked.Profession` should null-check the loaded string set. Instead `BuildStandardOptions` adds it unconditionally, and `Profession` does `LoadAssetCached(setId)` then immediately `for (… i < strings.Count …)` — null-deref when the asset is missing.
- **Player-visible impact:** Talking to a word-set-only NPC and clicking Profession can `NullReferenceException` instead of showing a brush-off.
- **Verifier note:** Low confirmed. All 13 verified crew NPCs carry event sets; latent edge-case crash.

### 3.6 Movement (2D)

#### MOV2D-01 — `NpcOn`/`NpcOffEvent` for an unused/skipped NPC slot null-derefs `_npcs[npcNum]`
- **Severity:** Medium *(downgraded from High)*
- **File:** `src/Game/Entities/Map2D/NpcManager2D.cs:76-84`
- **What it does vs. should do:** `UpdateNpcStatus` should null-check `_npcs[npcNum]` before dereferencing, like `DispatchNpcEvent` does. Instead it only guards `npcNum >= _npcs.Length`, then does `_npcs[npcNum].IsActive = active`. Unused NPCs are skipped in `Subscribed`, so `_npcs[index]` stays null for those slots.
- **Player-visible impact:** A `NullReferenceException` when a map script toggles an NPC occupying an unused/skipped slot — soft-locks the player on that map.
- **Verifier note:** Downgraded High→Medium. **Correction:** crash site is `_npcs[npcNum]` (null), not `game.Npcs[npcNum]`. Latent robustness gap, not a guaranteed soft-lock in base data.

#### MOV2D-02 — `PlaceBlock` bounds check uses `>` instead of `>=`
- **Severity:** Low
- **File:** `src/Game/Entities/Map2D/LogicalMap2D.cs:117`
- **What it does vs. should do:** The guard should be `targetIndex >= _mapData.Tiles.Length`. Instead `targetIndex > _mapData.Tiles.Length` lets `targetIndex == Length` pass, then `_mapData.Tiles[targetIndex]` indexes out of bounds.
- **Player-visible impact:** `IndexOutOfRangeException` when a script `BlockHard`/`BlockSoft` places a block whose footprint reaches exactly one tile past the array end (e.g. map's bottom-right edge).
- **Verifier note:** Low confirmed. Sibling `GetUnderlay`/`GetOverlay` correctly use `>= Length`.

### 3.7 Movement (3D)

#### MOV3D-02 — 3D tile-cross event uses `Round()` while collision/automap use `Floor()`
- **Severity:** Medium
- **File:** `src/Game/Entities/Map3D/Movement3D.cs:60-68`
- **What it does vs. should do:** The occupied tile is `Floor(pos)` per the "tile N owns [N, N+1)" convention (used by `FilterCollision` and `AutomapDialog.Show`). `PlayerEnteredTileEvent` should report that same Floor-based tile. Instead `OnTick` uses `MathF.Round`, so the reported tile flips at the half-tile boundary and can be one greater than `Floor(pos)`.
- **Player-visible impact:** Normal-trigger event chains and automap discovery are keyed to a tile offset by up to one from the party's actual collision tile — map events fire half a tile early/late or on the neighbouring tile.
- **Verifier note:** Known/suspected: `_HANDOFF.md:651` flags this ("Floor may be more correct. Worth investigating"). Medium confirmed — consistent half-tile phase offset; each crossing still fires once.

### 3.8 Map Transitions / Interactables

#### LOCK-01 — Locked chest/door shows `OpenedText` before the lock is touched, and `UnlockedText` is never shown
- **Severity:** Medium
- **File:** `src/Game/Gui/Inventory/InventoryScreenManager.cs:75-76`
- **What it does vs. should do:** For a locked container the original shows no "opened" text up front; `OpenedText` is the already-open message and `UnlockedText` the post-unlock confirmation. Instead `SetMode` unconditionally raises `OpenedText` the instant the event fires (no lock-state guard), and `UnlockedText` — though deserialized and stored — has **zero read sites** and is never raised.
- **Player-visible impact:** A locked chest/door flashes its "opened" message while the player is still at the lock pane, and the intended "you unlocked it" message never appears.
- **Verifier note:** Medium confirmed. For an unlocked chest raising `OpenedText` immediately is correct — the defect is the missing lock-state guard plus the never-raised `UnlockedText`.

#### LOCK-02 — `DoorEvent` and `ChestEvent` assign the two text bytes in opposite order
- **Severity:** Medium
- **File:** `src/Formats/MapEvents/DoorEvent.cs:18-19` (vs `ChestEvent.cs:28-29`)
- **What it does vs. should do:** The two consecutive text bytes (record offsets 4 and 5) must map to the same fields for both event types. Instead `DoorEvent` reads `OpenedText` then `UnlockedText`, while `ChestEvent` reads `UnlockedText` then `OpenedText` from identical byte positions — at most one matches the original.
- **Player-visible impact:** Either chests or doors display the wrong message id when opened (`OpenedText` is the only text field actually displayed). Round-trip re-serialization preserves the swap, masking it from tests.
- **Verifier note:** Medium confirmed (inconsistency is certain). **Caveat:** which serializer is faithful needs an original map-event byte dump to settle. Related to LOCK-01, distinct defect.

#### LOCK-03 — Right key is never consumed even when it is a one-use (vanish-flag) key
- **Severity:** Low
- **File:** `src/Game/Gui/Inventory/InventoryLockPane.cs:64-69`
- **What it does vs. should do:** Per `door.c` (`_RE_5C.md §2.2 @0x5a92a`), when the in-hand item matches the lock's key the lock opens and the key is consumed only if it carries ITEMLIST flag 0x10 ("vanish when used up"). Instead, on a key match the handler always raises `InventoryReturnItemInHandEvent`, returning the key unconditionally; vanish-flagged single-use keys are never destroyed.
- **Player-visible impact:** Any quest key the original consumes on use stays in inventory forever and could be reused.
- **Verifier note:** Low confirmed. UAlbion models the exact bit (`ItemFlags.Unk4 = 0x10`) but `LockClicked` never checks it; the sibling lockpick branch correctly uses `InventoryDestroyItemInHandEvent`.

---

## 4. Cross-cutting Patterns

1. **Multiple always-live handlers on the same event (Raise-skips-own-handlers does NOT protect cross-component duplication).** `change_item` is handled by both `GameState` and `PartyInventory` (**INV-01**). The "Raise skips the sender's own subscriptions" rule gives no protection here because the event originates from the script source, not from either handler.

2. **Copy-paste of a success/state test across two call sites with flipped slot semantics.** `TakeFromParty` copied `SetLastResult(slot.Amount == 0)` from `GiveToParty` without accounting for `slot` being the accumulating acceptor (**INV-03**). Same "sibling did it right, this one didn't" shape recurs in **LOCK-02** and the off-by-one pattern.

3. **Off-by-one in 1-based vs. 0-based index bounds checks.** `GetWall` uses `< Count` where 1-based requires `<= Count`, sibling `GetObject` correct (**MOV3D-01**). `PlaceBlock` uses `>` where it needs `>=`, siblings `GetUnderlay`/`GetOverlay` correct (**MOV2D-02**).

4. **Unguarded array/dictionary access producing latent crashes.** `QueryNpcX/Y` (**QRY-01**), `UpdateNpcStatus` (**MOV2D-01**), `GetHomonyms` (**DLG-01**), `Profession` (**DLG-02**). Recurring fix shape: mirror the guard an adjacent method already performs.

5. **Numeric-operation formula divergence from the byte-exact RE reference.** `VaryDamage` floor (**CMB-01**); `ApplyToMax`/Gold/Food wrong percentage base (**DATA-01**). Both have a correct sibling in the same family.

6. **Sequencing / ordering relative to a clock or pipeline step.** Rest zeroes fatigue *after* advancing the clock (**STATE-01**); the 3D tile-cross event samples position with a different rounding *phase* than collision/automap (**MOV3D-02**).

7. **Routing a delegated operation to the wrong target / a degraded context.** Scroll teaches the owner not the validated caster (**INV-02**); item-cast spells run through a stripped context (**CMB-03**); buying refreshes only the merchant inventory (**MERCH-02**).
