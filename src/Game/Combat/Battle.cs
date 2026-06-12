using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using UAlbion.Api.Eventing;
using UAlbion.Api.Visual;
using UAlbion.Config;
using UAlbion.Core.Visual;
using UAlbion.Formats.Assets.Save;
using UAlbion.Formats.Ids;
using UAlbion.Formats.MapEvents;
using UAlbion.Game.Events;
using UAlbion.Game.Gui.Combat;
using UAlbion.Game.Gui.Dialogs;
using UAlbion.Game.State;

namespace UAlbion.Game.Combat;

/// <summary>
/// Contains the logical state of a battle
/// The top-level combat UI is handled by <see cref="CombatDialog"/>
/// </summary>
public class Battle : GameComponent, IReadOnlyBattle
{
    readonly MonsterGroupId _groupId;
    readonly List<ICombatParticipant> _mobs = [];
    readonly List<ICombatParticipant> _corpses = [];
    readonly ICombatParticipant[] _tiles = new ICombatParticipant[SavedGame.CombatRows * SavedGame.CombatColumns];
    // Battle-scoped HP shadow keyed by SheetId. Avoids the Effective-vs-underlying ambiguity:
    // party members have their Effective snapshot recomputed per frame, while monsters are
    // transient clones that aren't in GameState.Sheets so DataChangeEvent can't reach them.
    readonly Dictionary<SheetId, int> _liveHp = [];
    // Queued per-character actions (chosen by player via context menu before the round
    // begins). Members not in this map fall through to default Melee — matches Albion's
    // "leave defaults" behaviour when the player doesn't explicitly set actions.
    readonly Dictionary<SheetId, QueueCombatActionEvent> _pendingActions = [];
    // Damage traps placed by trap/mine spells, keyed by tile index. Triggered when a
    // combatant moves onto the tile (MoveCombatant); single-use like the original's.
    readonly Dictionary<int, int> _traps = [];
    // SP shadow for MONSTERS (transient clones DataChangeEvent can't reach) — party
    // members read/write their persistent sheets via Mana events instead. Without this,
    // monster casts never drained SP and repeated casts were free.
    readonly Dictionary<SheetId, int> _liveSp = [];

    // XP pool: each kill adds the monster sheet's ExperienceReward (offset 0x20); on
    // victory every LIVING party member receives max(1, total/livingCount) — RE'd from
    // MAIN.EXE (pool at 0x15f104), see _RE_COMBAT.md "Punch-list RE" item 3. No factor.
    int _xpPool;

    public IReadOnlyList<ICombatParticipant> Mobs { get; }
    public event Action Complete;

    public Battle(MonsterGroupId groupId, SpriteId backgroundId)
    {
        // Handles EXTERNAL end_combat raises (debug menu, scripts). Battle's own
        // CheckBattleOver calls HandleCombatEnd directly because Raise() skips the
        // sender's handlers — without the direct call, natural victories leave the
        // battle subscribed and the combat scene pushed forever.
        On<EndCombatEvent>(e => HandleCombatEnd(e.Result));
        OnAsync<BeginCombatRoundEvent>(BeginRoundAsync);
        OnAsync<ObserveCombatEvent>(Observe);
        On<QueueCombatActionEvent>(OnQueueAction);

        _groupId = groupId;
        Mobs = _mobs;
        AttachChild(new CombatActionPicker());
        AttachChild(new CombatAudio());
        AttachChild(new BattleView(this)); // the animated battle scene (backdrop + monster sprites)

        // The painted combat backdrop is the original's 360x192 blit at (0,0) (the status
        // bar covers the bottom 48 UI pixels). It renders through the UI sprite path
        // (NoDepthTest) — the world keeps drawing in its own passes underneath, and a
        // world-space Sprite at a high layer corrupts the 3D billboard batches, so the UI
        // element is the safe mechanism. It must draw at BattleView.BackdropLayer: BELOW
        // the battle-view monster sprites (which sit just under the UI band) — at
        // DrawLayer.Interface it would cover them, leaving only speckles through the
        // backdrop's index-0 holes. Stretching to the full 360x240 would also be wrong:
        // the projection's horizon is y=96 of the 192-high image.
        // ZeroOpaque: the original's background blit is unmasked — its palette-index-0
        // pixels are solid black, not holes (without this the map scene shows through
        // the backdrop's dark areas as colour speckles).
        AttachChild(new UAlbion.Game.Gui.Controls.UiFixedPositionElement(
            backgroundId,
            new UAlbion.Core.Rectangle(0, 0, 360, 192),
            BattleView.BackdropLayer,
            SpriteKeyFlags.ZeroOpaque));
    }

    AlbionTask Observe(ObserveCombatEvent _) =>
        WithFrozenClock(this, async x =>
        {
            Raise(new CombatDialog.ShowCombatDialogEvent(false));
            var dlg = x.AttachChild(new InvisibleWaitForClickDialog());
            await dlg.Task;
            Raise(new CombatDialog.ShowCombatDialogEvent(true));
        });

    void OnQueueAction(QueueCombatActionEvent e)
    {
        _pendingActions[e.Actor] = e;
    }

    static readonly QueueCombatActionEvent DefaultAction = new(default, CombatAction.Melee, -1);

    /// <summary>
    /// Look up the action queued for this combatant, or the default (Melee, no explicit
    /// target). Clears the entry so each queued choice applies exactly once — matches
    /// Albion's per-round action selection.
    /// </summary>
    QueueCombatActionEvent ConsumePendingAction(ICombatParticipant p)
    {
        if (p?.SheetId == null) return DefaultAction;
        if (_pendingActions.TryGetValue(p.SheetId, out var choice))
        {
            _pendingActions.Remove(p.SheetId);
            return choice;
        }
        return DefaultAction;
    }

    // Playback pacing: how long the active-combatant highlight shows before the action
    // resolves, and how long the result (damage flash / number) stays before the next turn.
    const float TurnDelaySeconds = 0.3f;
    const float TurnResultDelaySeconds = 0.5f;

    bool _roundInProgress;

    async AlbionTask BeginRoundAsync(BeginCombatRoundEvent _)
    {
        // Resolve exactly ONE round per invocation — the original runs a single round each
        // time the player confirms, then returns to the planning grid. Combatants act in
        // initiative order (highest Speed first) — matches the original engine's
        // fcn.0004c3f5 builder which sorts the combatant table descending by sheet
        // attribute #3 (Speed).
        //
        // Re-entrancy guard: playback awaits wall-clock timers, so a second
        // begin_combat_round arriving mid-round would interleave two resolutions (and
        // crash with a null Exchange if the first ends the battle while the second is
        // suspended). One round at a time.
        if (_roundInProgress || _combatEnded)
            return;
        _roundInProgress = true;
        try
        {
            await RunRound();
        }
        finally
        {
            _roundInProgress = false;
        }
    }

    async AlbionTask RunRound()
    {
        var partyAlive = LiveParticipants(forParty: true).ToList();
        var mobsAlive  = LiveParticipants(forParty: false).ToList();
        if (CheckBattleOver(partyAlive.Count, mobsAlive.Count))
            return;

        foreach (var (attacker, isParty) in OrderByInitiative(partyAlive, mobsAlive))
        {
            if (Exchange == null || _combatEnded) return; // detached mid-playback
            if (LifePoints(attacker) <= 0) continue; // killed earlier this round
            if (!CanAct(attacker))
            {
                // The original announces why the turn is lost (SYSTEXTS 762..773 via
                // fcn.000363c2) — show the line and give the player a beat to read it.
                var conds = attacker?.Effective?.Combat?.Conditions ?? UAlbion.Formats.Assets.Sheets.PlayerConditions.None;
                var message = ConditionMessage(conds);
                if (message != null)
                {
                    ShowCombatMessage(message.Value, attacker);
                    await RaiseA(new WallClockTimerEvent(TurnDelaySeconds));
                }
                continue;
            }

            // Playback: highlight whose turn it is, give the player a beat to register it,
            // resolve the action (which raises CombatHitEvents), then pause on the result.
            Raise(new CombatTurnHighlightEvent(TileOf(attacker)));
            await RaiseA(new WallClockTimerEvent(TurnDelaySeconds));
            if (Exchange == null || _combatEnded) return;
            TakeTurn(attacker, forParty: isParty);
            await RaiseA(new WallClockTimerEvent(TurnResultDelaySeconds));
            if (Exchange == null || _combatEnded) return;

            if (!LiveParticipants(forParty: true).Any() || !LiveParticipants(forParty: false).Any())
                break;
        }

        if (Exchange == null || _combatEnded)
            return;

        Raise(new CombatTurnHighlightEvent(-1));

        // End-of-round status decay. Only handles transient combat conditions whose
        // duration semantics are unambiguous: Asleep wakes up after a round of taking
        // hits (we always clear here rather than gating on damage, because nothing
        // wakes a sleeping target if no-one attacks them in a 1v1 stunlock). Permanent
        // conditions (Unconscious, Poisoned, Paralysed, etc.) are deliberately left
        // alone — clearing them speculatively would corrupt the original engine's
        // pacing. See _RE_COMBAT.md Phase 2.6 for the full per-condition tick table.
        DecaySleepOnAllCombatants();
        CombatBuffs.TickRound();

        CheckBattleOver(
            LiveParticipants(forParty: true).Count(),
            LiveParticipants(forParty: false).Count());
    }

    bool CheckBattleOver(int partyAlive, int mobsAlive)
    {
        if (partyAlive == 0)
        {
            Raise(new EndCombatEvent(CombatResult.PartyKilled));
            HandleCombatEnd(CombatResult.PartyKilled); // Raise() skips own handlers
            return true;
        }
        if (mobsAlive == 0)
        {
            Raise(new EndCombatEvent(CombatResult.Victory));
            HandleCombatEnd(CombatResult.Victory); // Raise() skips own handlers
            return true;
        }
        return false;
    }

    bool _combatEnded;

    void HandleCombatEnd(CombatResult result)
    {
        if (_combatEnded) // Guard against double-handling (external raise + direct call)
            return;
        _combatEnded = true;

        if (result == CombatResult.Victory)
        {
            AwardExperience();
            if (_loot.Count > 0 || _lootGold > 0 || _lootRations > 0)
            {
                var items = _loot.Select(kvp => (kvp.Key, (ushort)Math.Min(ushort.MaxValue, kvp.Value))).ToList();
                Raise(new ShowBattleLootEvent(items, _lootGold, _lootRations));
            }
        }
        ClearCombatScopedConditions();
        Complete?.Invoke();
    }

    /// <summary>
    /// The five combat-scoped conditions are batch-cleared for every party member when a
    /// battle ends, regardless of outcome — RE'd from MAIN.EXE fcn.000650ad (called from
    /// both the battle-exit and victory paths). Everything else (Poisoned, Ill, Blind,
    /// Insane, Unconscious, ...) persists until an explicit cure.
    /// </summary>
    void ClearCombatScopedConditions()
    {
        ReadOnlySpan<UAlbion.Formats.Assets.Sheets.PlayerCondition> combatScoped =
        [
            UAlbion.Formats.Assets.Sheets.PlayerCondition.Irritated,
            UAlbion.Formats.Assets.Sheets.PlayerCondition.Asleep,
            UAlbion.Formats.Assets.Sheets.PlayerCondition.Panicking,
            UAlbion.Formats.Assets.Sheets.PlayerCondition.Fleeing,
            UAlbion.Formats.Assets.Sheets.PlayerCondition.Paralysed
        ];

        foreach (var p in _mobs)
        {
            if (p.SheetId.Type != AssetType.PartySheet)
                continue;
            var target = TryToTarget(p);
            if (target == null)
                continue;
            foreach (var condition in combatScoped)
                Raise(new ChangeStatusEvent(target.Value, condition, NumericOperation.SubtractAmount, 1));
        }
    }

    int TileOf(ICombatParticipant p) => p == null ? -1 : Array.IndexOf(_tiles, p);

    /// <summary>
    /// Show a combat system message in the status/description area. The original engine's
    /// ShowSystemMessage (fcn.0002f85d) prints SYSTEXTS entries during combat — 443/444/445
    /// for movement, 762..773 for per-condition turn messages (fcn.000363c2 emits
    /// 762 + conditionIndex). "{NAME}" texts take the combatant's display name.
    /// </summary>
    void ShowCombatMessage(Base.SystemText text, ICombatParticipant subject = null)
    {
        var tf = TryResolve<Text.ITextFormatter>();
        if (tf == null)
            return;
        var name = subject?.Effective?.GetName(ReadVar(V.User.Gameplay.Language));
        var source = name == null ? tf.Format(text) : tf.Format(text, name);
        Raise(new DescriptionTextEvent(source));
    }

    /// <summary>
    /// The condition message for a combatant whose turn is affected, in the original's
    /// priority order (SYSTEXTS 762..773). Null when no relevant condition is present.
    /// </summary>
    static Base.SystemText? ConditionMessage(UAlbion.Formats.Assets.Sheets.PlayerConditions conds)
    {
        if ((conds & UAlbion.Formats.Assets.Sheets.PlayerConditions.Unconscious) != 0) return Base.SystemText.Condition_XIsUnconscious;
        if ((conds & UAlbion.Formats.Assets.Sheets.PlayerConditions.Paralysed) != 0)   return Base.SystemText.Condition_XIsUnableToMove;
        if ((conds & UAlbion.Formats.Assets.Sheets.PlayerConditions.Asleep) != 0)      return Base.SystemText.Condition_XIsAsleep;
        if ((conds & UAlbion.Formats.Assets.Sheets.PlayerConditions.Panicking) != 0)   return Base.SystemText.Condition_XIsPanicking;
        if ((conds & UAlbion.Formats.Assets.Sheets.PlayerConditions.Insane) != 0)      return Base.SystemText.Condition_XHasGoneInsane;
        return null;
    }

    /// <summary>
    /// Monsters leave the grid when killed (the original plays a death animation then
    /// clears the cell). Party members stay — unconscious bodies remain visible.
    /// </summary>
    // Post-combat loot, RE'd from MAIN.EXE fcn.0004e124 (monster-death dump): a killed
    // monster's equipped items + backpack + gold (sheet+0x18) + rations (sheet+0x1A) go
    // into the loot list; broken party gear is added here too (fcn.000665ce moves it to
    // the same list). Presented at victory in the battle-loot window.
    readonly Dictionary<ItemId, int> _loot = [];
    int _lootGold;
    int _lootRations;

    void AddLoot(ItemId item, int amount)
    {
        if (item.IsNone || amount <= 0)
            return;
        _loot.TryGetValue(item, out int existing);
        _loot[item] = existing + amount;
    }

    void RemoveMonsterCorpse(ICombatParticipant p)
    {
        if (p == null || p.SheetId.Type == AssetType.PartySheet)
            return;
        int tile = TileOf(p);
        if (tile >= 0)
            _tiles[tile] = null;
        _corpses.Add(p);
        _xpPool += p.Effective?.ExperienceReward ?? 0;
        CollectMonsterLoot(p);
    }

    void CollectMonsterLoot(ICombatParticipant p)
    {
        var inv = p.Effective?.Inventory;
        if (inv == null)
            return;

        foreach (var slot in inv.EnumerateAll())
            if (slot != null && slot.Item.Type == AssetType.Item)
                AddLoot(slot.Item, Math.Max(1, (int)slot.Amount));

        _lootGold += inv.Gold?.Amount ?? 0;
        _lootRations += inv.Rations?.Amount ?? 0;
    }

    void AwardExperience()
    {
        if (_xpPool <= 0)
            return;

        var living = LiveParticipants(forParty: true).ToList();
        if (living.Count == 0)
            return;

        int each = Math.Max(1, _xpPool / living.Count);
        foreach (var member in living)
        {
            var target = TryToTarget(member);
            if (target == null)
                continue;
            Raise(new DataChangeEvent(target.Value, ChangeProperty.Experience, NumericOperation.AddAmount, (ushort)Math.Min(ushort.MaxValue, each)));
        }

        Info($"[Combat] Victory: {_xpPool} XP pooled, {each} each to {living.Count} living members");
        TraceLog.Emit("combat_xp", ("total", _xpPool), ("each", each), ("members", living.Count));
        _xpPool = 0;
    }

    /// <summary>
    /// Order combatants descending by Speed (the original engine's initiative formula).
    /// Stable order within equal initiative — first party then monsters — for determinism in tests.
    /// </summary>
    static IEnumerable<(ICombatParticipant Attacker, bool IsParty)> OrderByInitiative(
        List<ICombatParticipant> party,
        List<ICombatParticipant> mobs)
        => InitiativeOrder.Order(party, mobs, EffectiveSpeed);

    static int EffectiveSpeed(ICombatParticipant p)
        => (p?.Effective?.Attributes?.Speed?.Current ?? 0)
           + (p?.SheetId != null ? CombatBuffs.Bonus(p.SheetId, CombatBuffs.BuffKind.Speed) : 0);

    void DecaySleepOnAllCombatants()
    {
        foreach (var p in _mobs)
        {
            if (LifePoints(p) <= 0) continue;
            var combat = p?.Effective?.Combat;
            if (combat == null) continue;
            if ((combat.Conditions & UAlbion.Formats.Assets.Sheets.PlayerConditions.Asleep) == 0) continue;
            var target = TryToTarget(p);
            if (target == null) continue; // monster — sleep state lives on the Effective clone, not in GameState.Sheets
            Raise(new ChangeStatusEvent(target.Value, UAlbion.Formats.Assets.Sheets.PlayerCondition.Asleep, NumericOperation.SubtractAmount, 1));
        }
    }

    IEnumerable<ICombatParticipant> LiveParticipants(bool forParty)
    {
        foreach (var p in _mobs)
        {
            if (IsParty(p) != forParty) continue;
            if (LifePoints(p) <= 0) continue;
            yield return p;
        }
    }

    static bool IsParty(ICombatParticipant p)
        => p.CombatPosition / SavedGame.CombatColumns >= SavedGame.CombatRowsForMobs;

    /// <summary>
    /// Safely convert a combat participant's SheetId to a TargetId for events that flow
    /// through SheetApplier (ChangeStatusEvent, DataChangeEvent, etc.). TargetId only
    /// accepts PartyMember / NpcSheet / Target / None — passing a raw PartySheet or
    /// MonsterSheet throws ArgumentOutOfRangeException at construction time. For party
    /// members we map PartySheet.N → PartyMember.N (same numeric id, different enum
    /// namespace). For monsters there is no equivalent target — their state lives in
    /// Battle._liveHp + ICombatParticipant.Effective, so the caller should skip the
    /// event-raise instead.
    /// </summary>
    static TargetId? TryToTarget(ICombatParticipant p)
    {
        if (p?.SheetId == null) return null;
        var sheetId = p.SheetId;
        if (sheetId.Type == UAlbion.Config.AssetType.PartySheet)
            return new TargetId(UAlbion.Config.AssetType.PartyMember, sheetId.Id);
        // Monsters / other types: no compatible TargetId mapping.
        return null;
    }

    /// <summary>
    /// Take a single combatant's turn — AP-loop attack pattern reverse-engineered from
    /// MAIN.EXE fcn.0004ef8b (school-6 spell resolver). The original engine spends each
    /// AP point as one attack/cast attempt, stopping once the attempt lands.
    /// </summary>
    void TakeTurn(ICombatParticipant attacker, bool forParty)
    {
        // Monster turns: weighted-random action pick, RE'd from MAIN.EXE (_RE_COMBAT.md
        // "Punch-list RE" item 4). Setup grants every monster Melee|Ranged and adds Magic
        // when the sheet has spells; Magic is disabled at 0 SP. The 16-entry weight table
        // favours Magic/Ranged 6/16 each over Melee 4/16; failed commits clear their bit.
        if (!forParty)
        {
            var available = MonsterAi.AvailableActions.Melee | MonsterAi.AvailableActions.Ranged;
            int sp = attacker?.Effective?.Magic?.SpellPoints?.Current ?? 0;
            if (sp > 0 && (attacker?.Effective?.Magic?.KnownSpells?.Count ?? 0) > 0)
                available |= MonsterAi.AvailableActions.Magic;

            var aiRng = Resolve<IRandom>();
            var picked = MonsterAi.ChooseNormalAction(available, () => aiRng.Generate(16), bit => bit switch
            {
                MonsterAi.AvailableActions.Magic => TryMonsterCast(attacker),
                MonsterAi.AvailableActions.Melee => true, // resolved by the AP loop below
                // Ranged commits when the mob actually holds a long-range weapon — the
                // strike pipeline then rolls LongRangeCombat for it. PLACEHOLDER:
                // ammunition rules + the original's mid-fight auto-equip pending RE 5A.
                MonsterAi.AvailableActions.Ranged => HasRangedWeapon(attacker),
                _ => false,
            });

            if (picked == MonsterAi.AvailableActions.Magic)
                return; // the cast already resolved this turn
        }

        // Honour the per-combatant status gate (Sleep/Panicking/Insane) — RE'd from
        // MAIN.EXE fcn.0004bf77 (see _RE_COMBAT.md). Insane mobs still act but pick a
        // random opponent regardless of which side they're on; Panicking ones lose
        // their turn (would flee if we had a flee mechanic).
        var conds = attacker?.Effective?.Combat?.Conditions ?? UAlbion.Formats.Assets.Sheets.PlayerConditions.None;
        var outcome = MonsterAi.ResolveStatusBehavior(conds);
        if (outcome == MonsterAi.StatusOutcome.SkipTurn || outcome == MonsterAi.StatusOutcome.Flee)
            return;

        // Pull the player's queued choice (if any). Defaults to Melee for unset members
        // and for all monsters — matches the original engine's behaviour where unconfigured
        // combatants fall back to attack-nearest.
        var pending = forParty ? ConsumePendingAction(attacker) : DefaultAction;
        var chosenAction = pending.Action;
        var chosenTile = pending.TargetTile;

        // Skip-turn actions (DoNothing / Retreat with the Fleeing status applied) exit early.
        if (chosenAction == CombatAction.None)
            return;

        if (chosenAction == CombatAction.Retreat)
        {
            // Only back-row members can retreat (party row 4) per fcn.0004f5d3.
            int row = attacker.CombatPosition / SavedGame.CombatColumns;
            if (row == SavedGame.CombatRows - 1)
            {
                ShowCombatMessage(Base.SystemText.CombatMsg_XIsFleeing, attacker); // SYSTEXTS 445
                var targetId = TryToTarget(attacker);
                if (targetId != null)
                    Raise(new ChangeStatusEvent(targetId.Value, UAlbion.Formats.Assets.Sheets.PlayerCondition.Fleeing, NumericOperation.AddAmount, 1));
            }
            return;
        }

        if (chosenAction == CombatAction.Move)
        {
            MoveCombatant(attacker, chosenTile);
            return;
        }

        if (chosenAction is CombatAction.CastSpell or CombatAction.CastSchool5 or CombatAction.CastSchool6)
        {
            CastQueuedSpell(attacker, pending.Spell, chosenTile);
            return;
        }

        if (chosenAction == CombatAction.UseItem)
        {
            UseQueuedItem(attacker, pending);
            return;
        }

        int ap = attacker?.Effective?.Combat?.ActionPoints ?? 1;
        if (ap < 1) ap = 1;
        // Berserk doubles the AP attempt count — the original's "powered" flag at
        // Combatant+0x04 bit 0 (byte-exact mechanic from MAIN.EXE fcn.0004ef8b).
        if (CombatBuffs.IsBerserk(attacker.SheetId))
            ap <<= 1;

        for (int attempt = 0; attempt < ap; attempt++)
        {
            ICombatParticipant target;
            if (outcome == MonsterAi.StatusOutcome.InsaneRandomAct)
            {
                // Insane: pick a random LIVE combatant of any side (50/50 in the original
                // engine for "ally vs enemy" but we just pick any live target — close enough
                // until we have proper friendly-fire mechanics).
                var allLive = new System.Collections.Generic.List<ICombatParticipant>();
                foreach (var p in _mobs)
                    if (LifePoints(p) > 0 && p.SheetId != attacker.SheetId)
                        allLive.Add(p);
                if (allLive.Count == 0) return;
                target = allLive[attempt % allLive.Count];   // deterministic-cycle for now
            }
            else if (chosenTile >= 0 && chosenTile < _tiles.Length)
            {
                // Explicit target tile from the player's menu choice. Falls back to
                // nearest-enemy if the chosen tile is empty or the occupant is dead.
                target = _tiles[chosenTile];
                if (target == null || LifePoints(target) <= 0)
                    target = LiveParticipants(forParty: !forParty).FirstOrDefault();
            }
            else
            {
                target = LiveParticipants(forParty: !forParty).FirstOrDefault();
            }
            if (target == null) return;

            int hpBefore = LifePoints(target);
            ApplyMeleeAttack(attacker, target);
            int hpAfter  = LifePoints(target);

            // Stop the AP loop on a successful hit — matches the original's "stop on success"
            // semantics from fcn.0004ef8b. ApplyMeleeAttack rolls hit/miss internally; a miss
            // leaves hpAfter == hpBefore so the loop retries on the next AP point.
            if (hpAfter < hpBefore) return;
        }
    }

    /// <summary>
    /// Move a combatant to an empty tile. Rules RE'd from MAIN.EXE fcn.0004d85b
    /// (_RE_COMBAT.md "Punch-list RE" item 2): range = clamp(Speed/30, 1, 3) tiles,
    /// Chebyshev distance (8-directional); only the DESTINATION tile must be empty
    /// (intermediate occupancy is ignored); party members may only stand in the bottom
    /// two rows, monsters in rows 0..CombatRowsForMobs.
    /// </summary>
    void MoveCombatant(ICombatParticipant mover, int targetTile)
    {
        if (mover == null || targetTile < 0 || targetTile >= _tiles.Length)
            return;

        int oldTile = System.Array.IndexOf(_tiles, mover);
        int speed = mover.Effective?.Attributes?.Speed?.Current ?? 0;
        int range = Math.Clamp(speed / 30, 1, 3);
        int dx = Math.Abs(targetTile % SavedGame.CombatColumns - oldTile % SavedGame.CombatColumns);
        int dy = Math.Abs(targetTile / SavedGame.CombatColumns - oldTile / SavedGame.CombatColumns);
        int targetRow = targetTile / SavedGame.CombatColumns;
        bool rowAllowed = IsParty(mover)
            ? targetRow >= SavedGame.CombatRowsForMobs                       // party: bottom rows only
            : targetRow <= SavedGame.CombatRowsForMobs;                      // monsters: rows 0..3 (may advance one row into the party zone)

        if (Math.Max(dx, dy) > range || !rowAllowed)
        {
            Info($"[Combat] {mover.SheetId} move to {targetTile} refused (range {range}, row ok {rowAllowed})");
            ShowCombatMessage(Base.SystemText.CombatMsg_MoveWasBlocked); // SYSTEXTS 443
            return;
        }

        if (_tiles[targetTile] != null && LifePoints(_tiles[targetTile]) > 0)
        {
            Info($"[Combat] {mover.SheetId} can't move to occupied tile {targetTile}");
            ShowCombatMessage(Base.SystemText.CombatMsg_MoveWasBlocked); // SYSTEXTS 443
            return;
        }

        if (oldTile >= 0)
            _tiles[oldTile] = null;
        _tiles[targetTile] = mover;
        ShowCombatMessage(Base.SystemText.CombatMsg_XIsMoving, mover); // SYSTEXTS 444
        Info($"[Combat] {mover.SheetId} moves from tile {oldTile} to {targetTile}");
        TraceLog.Emit("combat_move", ("actor", mover.SheetId), ("from", oldTile), ("to", targetTile));

        if (_traps.TryGetValue(targetTile, out var trapDamage))
        {
            _traps.Remove(targetTile);
            Info($"[Combat] {mover.SheetId} triggers a trap on tile {targetTile} for {trapDamage} damage");
            ApplyDirectDamage(mover, trapDamage);
        }
    }

    /// <summary>
    /// Apply non-melee damage (spells, traps) through the same HP-shadow + DataChangeEvent
    /// plumbing as melee hits, so deaths/unconsciousness resolve identically.
    /// </summary>
    void ApplyDirectDamage(ICombatParticipant target, int amount)
    {
        if (target == null || amount <= 0)
            return;

        var clamped = (ushort)Math.Min(ushort.MaxValue, amount);
        var current = LifePoints(target);
        var next = Math.Max(0, current - clamped);
        _liveHp[target.SheetId] = next;

        var targetId = TryToTarget(target);
        if (targetId != null)
            Raise(new DataChangeEvent(targetId.Value, ChangeProperty.Health, NumericOperation.SubtractAmount, clamped));

        Info($"[Combat] {target.SheetId} takes {clamped} damage (HP {next})");
        Raise(new CombatHitEvent(TileOf(target), clamped, next == 0, false));
        if (next == 0)
            RemoveMonsterCorpse(target);
    }

    void ApplyDirectHeal(ICombatParticipant target, int amount)
    {
        if (target == null || amount <= 0)
            return;

        var clamped = (ushort)Math.Min(ushort.MaxValue, amount);
        var max = target.Effective?.Combat?.LifePoints?.Max ?? int.MaxValue;
        var next = Math.Min(max, LifePoints(target) + clamped);
        _liveHp[target.SheetId] = next;

        var targetId = TryToTarget(target);
        if (targetId != null)
            Raise(new DataChangeEvent(targetId.Value, ChangeProperty.Health, NumericOperation.AddAmount, clamped));

        Raise(new CombatHitEvent(TileOf(target), clamped, false, true));
    }

    /// <summary>
    /// Monster AI magic commit: cast the first affordable known spell at the nearest live
    /// enemy. Returns false (clearing the Magic bit for this turn) when nothing is castable.
    /// SP comes from the battle shadow, so repeated casts drain the monster's pool.
    /// </summary>
    bool TryMonsterCast(ICombatParticipant caster)
    {
        var spells = caster?.Effective?.Magic?.KnownSpells;
        if (spells == null || spells.Count == 0)
            return false;

        int sp = SpellPoints(caster);
        foreach (var spellId in spells)
        {
            var spell = Assets.LoadSpell(spellId);
            if (spell == null || (spell.Cost > 0 && spell.Cost > sp))
                continue;

            var target = LiveParticipants(forParty: !IsParty(caster)).FirstOrDefault();
            if (target == null)
                return false;

            CastQueuedSpell(caster, spellId, TileOf(target));
            return true;
        }
        return false;
    }

    /// <summary>
    /// Resolve a queued spell cast through the SpellEffectRegistry: consume SP, apply the
    /// effect to the occupant of the target tile (or the caster for self-target picks).
    /// </summary>
    void CastQueuedSpell(ICombatParticipant caster, SpellId spellId, int targetTile)
    {
        if (caster == null || spellId.IsNone)
            return;

        var spell = Assets.LoadSpell(spellId);
        int cost = spell?.Cost ?? 0;
        int sp = SpellPoints(caster);
        if (cost > 0 && sp < cost)
        {
            Info($"[Combat] {caster.SheetId} lacks SP for {spellId} ({sp}/{cost})");
            return;
        }

        // RE'd mastery multiplier M = max(1, (mastery+50)/100); mastery is the per-spell
        // 0..10000 value grown by MagicTalent on each cast (_RE_COMBAT.md "Punch-list RE").
        ushort mastery = 0;
        caster.Effective?.Magic?.SpellStrengths?.TryGetValue(spellId, out mastery);
        int m = Math.Max(1, (mastery + 50) / 100);

        var rng = Resolve<IRandom>();

        // Target-AREA enumeration (RE 5B fcn.0005ef24/fcn.0005fb21): RowOfMonsters hits
        // every enemy in the picked tile's 6-tile grid row; AllMonsters hits every
        // living enemy; DeadParty (0x04) is actually the WHOLE LIVING PARTY; the rest
        // are single-target. Each recipient runs the effect (and its own gate)
        // separately. GoddessWrath manages its own victims (the random picker).
        var targets = spell?.Targets ?? default;
        bool selfManagedArea = SpellEffectRegistry.TryGet(spellId, out var registered)
                               && registered is Spells.GoddessWrathEffect;

        var recipients = new List<ICombatParticipant>();
        if (!selfManagedArea && (targets & UAlbion.Formats.Assets.SpellTargets.AllMonsters) != 0)
        {
            recipients.AddRange(EnumerateTilesRowMajor(enemyOf: caster));
        }
        else if (!selfManagedArea && (targets & UAlbion.Formats.Assets.SpellTargets.RowOfMonsters) != 0)
        {
            int row = targetTile >= 0
                ? targetTile / SavedGame.CombatColumns
                : TileOf(LiveParticipants(forParty: !IsParty(caster)).FirstOrDefault());
            if (row >= 0)
                foreach (var p in EnumerateTilesRowMajor(enemyOf: caster))
                    if (TileOf(p) / SavedGame.CombatColumns == row)
                        recipients.Add(p);
        }
        else if (!selfManagedArea && (targets & UAlbion.Formats.Assets.SpellTargets.DeadParty) != 0)
        {
            // 0x04 = whole living party (the "DeadParty" name predates the RE).
            recipients.AddRange(LiveParticipants(forParty: IsParty(caster)));
        }
        else
        {
            var single = targetTile >= 0 && targetTile < _tiles.Length ? _tiles[targetTile] : null;
            if (single == null || LifePoints(single) <= 0)
            {
                // Empty / dead tile picked: monster-targeting spells retarget the
                // nearest live enemy (like melee); party-targeting ones self-cast.
                bool offensive = (targets & (UAlbion.Formats.Assets.SpellTargets.OneMonster
                                            | UAlbion.Formats.Assets.SpellTargets.RowOfMonsters
                                            | UAlbion.Formats.Assets.SpellTargets.AllMonsters)) != 0;
                single = offensive
                    ? LiveParticipants(forParty: !IsParty(caster)).FirstOrDefault() ?? caster
                    : caster;
            }
            recipients.Add(single);
        }

        if (recipients.Count == 0 && !selfManagedArea)
        {
            // Nothing in the area: fizzle (sound 698) and the action is not consumed —
            // RE 5B: zero continuations leave the combatant's action slot intact.
            Raise(new SoundEffectEvent(new SampleId(698), 100, 0, 0, 0, SoundMode.GlobalOneShot));
            Info($"[Combat] {caster.SheetId} casts {spellId} but nothing is in the area (fizzle)");
            return;
        }

        SpellCastContext BuildContext(ICombatParticipant target) => new()
        {
            Caster = caster,
            Target = target,
            CombatTargetPosition = targetTile,
            SpellStrength = (byte)(caster.Effective?.Level ?? 1),
            MasteryMultiplier = m,
            Random = max => rng.Generate(max),
            RaiseEvent = Raise,
            ApplyDamage = ApplyDirectDamage,
            ApplyHeal = ApplyDirectHeal,
            PlaceTrap = (tile, damage) => _traps[tile] = damage,
            RemoveTrap = tile => _traps.Remove(tile),
            GetLiveEnemies = () => EnumerateTilesRowMajor(enemyOf: caster).ToList(),
            GetAllies = () => LiveParticipants(forParty: IsParty(caster)).ToList(),
            HoursAwake = () => TryResolve<IGameState>()?.HoursSinceResting ?? 0,
            ModifySp = ModifySpellPoints,
            GetActiveSpellPct = LookupActiveSpellPct,
            // Instant kill = LP wipe through the normal damage path so death / corpse /
            // XP-pool handling resolve identically (the original's fcn.0004e247).
            InstantKill = p => ApplyDirectDamage(p, Math.Max(1, LifePoints(p)))
        };

        var outcome = SpellCastOutcome.Resisted;
        if (selfManagedArea || recipients.Count == 0)
        {
            outcome = SpellEffectRegistry.Cast(spellId, BuildContext(recipients.FirstOrDefault() ?? caster));
        }
        else
        {
            foreach (var recipient in recipients)
            {
                var result = SpellEffectRegistry.Cast(spellId, BuildContext(recipient));
                if (result == SpellCastOutcome.Hit || (result == SpellCastOutcome.Failed && outcome != SpellCastOutcome.Hit))
                    outcome = result;
                if (Exchange == null || _combatEnded)
                    return; // an area kill may have ended the battle mid-loop
            }
        }

        Info($"[Combat] {caster.SheetId} casts {spellId} at tile {targetTile} ({recipients.Count} target(s)): {outcome}");
        TraceLog.Emit("combat_cast", ("actor", caster.SheetId), ("spell", spellId), ("tile", targetTile), ("targets", recipients.Count), ("outcome", outcome));

        if (outcome != SpellCastOutcome.Failed)
            Raise(new CombatCastEvent(spellId)); // cast SFX (CombatAudio)

        // SP is consumed when the cast is attempted, regardless of resist — matches the
        // original engine's charge/SP handling for unfulfilled casts.
        if (cost > 0)
        {
            var casterTarget = TryToTarget(caster);
            if (casterTarget != null)
                Raise(new DataChangeEvent(casterTarget.Value, ChangeProperty.Mana, NumericOperation.SubtractAmount, (ushort)cost));
            else
                _liveSp[caster.SheetId] = Math.Max(0, SpellPoints(caster) - cost); // monster SP shadow
        }
    }

    /// <summary>
    /// Resolve a queued magic-item use: cast the item's spell with no SP cost; a charge
    /// (or one consumable) is consumed on success via ConsumeItemChargeEvent below.
    /// </summary>
    void UseQueuedItem(ICombatParticipant user, QueueCombatActionEvent pending)
    {
        if (user == null || pending.Spell.IsNone)
            return;

        var target = pending.TargetTile >= 0 && pending.TargetTile < _tiles.Length ? _tiles[pending.TargetTile] : null;
        target ??= user;

        var rng = Resolve<IRandom>();
        var context = new SpellCastContext
        {
            Caster = user,
            Target = target,
            CombatTargetPosition = pending.TargetTile,
            SpellStrength = 1,
            // Item casts run at the FLAT multiplier 50 (RE 5B: cast core fcn.0005fdf7
            // @0x5fe34 returns 0x32 for any item slot) — no SP cost, no mastery growth.
            MasteryMultiplier = 50,
            Random = max => rng.Generate(max),
            RaiseEvent = Raise,
            ApplyDamage = ApplyDirectDamage,
            ApplyHeal = ApplyDirectHeal,
            PlaceTrap = (tile, damage) => _traps[tile] = damage,
            RemoveTrap = tile => _traps.Remove(tile),
            GetLiveEnemies = () => LiveParticipants(forParty: !IsParty(user)).ToList(),
            GetAllies = () => LiveParticipants(forParty: IsParty(user)).ToList(),
            HoursAwake = () => TryResolve<IGameState>()?.HoursSinceResting ?? 0,
            ModifySp = ModifySpellPoints,
            GetActiveSpellPct = LookupActiveSpellPct,
            InstantKill = p => ApplyDirectDamage(p, Math.Max(1, LifePoints(p)))
        };

        var outcome = SpellEffectRegistry.Cast(pending.Spell, context);
        Info($"[Combat] {user.SheetId} uses item {pending.Item} ({pending.Spell}): {outcome}");
        TraceLog.Emit("combat_use_item", ("actor", user.SheetId), ("item", pending.Item), ("spell", pending.Spell), ("outcome", outcome));

        if (outcome != SpellCastOutcome.Failed)
        {
            Raise(new CombatCastEvent(pending.Spell)); // cast SFX (CombatAudio)

            // Consume a charge from the item that was used (party members only —
            // monster items aren't tracked in inventories).
            if (user.SheetId.Type == AssetType.PartySheet && !pending.Item.IsNone)
                Raise(new ConsumeItemChargeEvent(new PartyMemberId(AssetType.PartyMember, user.SheetId.Id), pending.Item));
        }
    }

    /// <summary>
    /// Per-combatant turn gate — mirror of the original combat.c PerCombatantTurnGate
    /// (fcn.0004bf77 in MAIN.EXE). Asleep / Paralysed combatants skip their turn.
    /// Insane combatants still act but with random behaviour (which our auto-resolve
    /// already approximates with a deterministic melee swing).
    /// </summary>
    static bool CanAct(ICombatParticipant p)
    {
        var conds = p?.Effective?.Combat?.Conditions ?? UAlbion.Formats.Assets.Sheets.PlayerConditions.None;
        // UnconsciousMask = Unconscious | Poisoned | Asleep — none of these can act.
        if ((conds & UAlbion.Formats.Assets.Sheets.PlayerConditions.UnconsciousMask) != 0)
            return false;
        if ((conds & UAlbion.Formats.Assets.Sheets.PlayerConditions.Paralysed) != 0)
            return false;
        // Frost-line freeze (the original's kind-1 round-timed buff sets Paralysed and
        // clears it at expiry; we model it directly as a timed turn-skip).
        if (p != null && CombatBuffs.IsFrozen(p.SheetId))
            return false;
        return true;
    }

    int LifePoints(ICombatParticipant p)
    {
        if (p == null) return 0;
        if (_liveHp.TryGetValue(p.SheetId, out var hp))
            return hp;
        // Lazy seed from Effective on first read; subsequent damage updates the shadow.
        var initial = p.Effective?.Combat?.LifePoints?.Current ?? 0;
        _liveHp[p.SheetId] = initial;
        return initial;
    }

    /// <summary>
    /// Add (+) or drain (−) SP on a combatant: party members via Mana events (persistent
    /// sheet), monsters via the battle SP shadow. Used by Steal Magic.
    /// </summary>
    void ModifySpellPoints(ICombatParticipant p, int delta)
    {
        if (p == null || delta == 0)
            return;

        if (p.SheetId.Type == AssetType.PartySheet)
        {
            var target = new TargetId(AssetType.PartyMember, p.SheetId.Id);
            var op = delta > 0 ? NumericOperation.AddAmount : NumericOperation.SubtractAmount;
            Raise(new DataChangeEvent(target, ChangeProperty.Mana, op, (ushort)Math.Min(ushort.MaxValue, Math.Abs(delta))));
        }
        else
        {
            int max = p.Effective?.Magic?.SpellPoints?.Max ?? int.MaxValue;
            _liveSp[p.SheetId] = Math.Clamp(SpellPoints(p) + delta, 0, max);
        }
    }

    /// <summary>
    /// Current SP: party members read their live sheet (Mana events keep it current);
    /// monsters use the battle-scoped shadow so casts actually drain their pool.
    /// </summary>
    int SpellPoints(ICombatParticipant p)
    {
        if (p == null) return 0;
        if (p.SheetId.Type == AssetType.PartySheet)
            return p.Effective?.Magic?.SpellPoints?.Current ?? 0;
        if (_liveSp.TryGetValue(p.SheetId, out var sp))
            return sp;
        var initial = p.Effective?.Magic?.SpellPoints?.Current ?? 0;
        _liveSp[p.SheetId] = initial;
        return initial;
    }

    /// <summary>
    /// Effective weapon/crit skill for combat rolls: Effective sheet skill (equipment
    /// bonuses already merged) + Berserk's battle buff, halved for weapon skills when
    /// Blind (fcn.00035fd5).
    /// </summary>
    static int EffectiveSkillFor(ICombatParticipant p, CombatBuffs.BuffKind buffKind, bool isWeaponSkill)
    {
        int skill = buffKind switch
        {
            CombatBuffs.BuffKind.CloseCombatSkill  => p?.Effective?.Skills?.CloseCombat?.Current ?? 0,
            CombatBuffs.BuffKind.RangedCombatSkill => p?.Effective?.Skills?.RangedCombat?.Current ?? 0,
            CombatBuffs.BuffKind.CritSkill         => p?.Effective?.Skills?.CriticalChance?.Current ?? 0,
            _ => 0
        };
        skill += CombatBuffs.Bonus(p.SheetId, buffKind);
        bool blind = ((p?.Effective?.Combat?.Conditions ?? 0) & UAlbion.Formats.Assets.Sheets.PlayerConditions.Blind) != 0;
        return DamageCalculator.EffectiveSkill(skill, isWeaponSkill, blind);
    }

    /// <summary>
    /// Live enemies of the given combatant in GRID ORDER (row-major over the tile array)
    /// — the original's ForEachTargetTile iteration order (RE 5B fcn.0005fb21).
    /// </summary>
    IEnumerable<ICombatParticipant> EnumerateTilesRowMajor(ICombatParticipant enemyOf)
    {
        bool casterIsParty = IsParty(enemyOf);
        for (int i = 0; i < _tiles.Length; i++)
        {
            var p = _tiles[i];
            if (p == null || LifePoints(p) <= 0)
                continue;
            if (IsParty(p) == casterIsParty)
                continue;
            yield return p;
        }
    }

    /// <summary>Active-spell percent for party combatants (0 for monsters — the table is party-only).</summary>
    int LookupActiveSpellPct(ICombatParticipant p, int type)
        => p?.SheetId.Type == AssetType.PartySheet
            ? TryResolve<IGameState>()?.GetActiveSpellPct(new PartyMemberId(AssetType.PartyMember, p.SheetId.Id), type) ?? 0
            : 0;

    /// <summary>True when the combatant's weapon hand holds a LongRangeWeapon (ItemType 6).</summary>
    bool HasRangedWeapon(ICombatParticipant p)
    {
        var slot = p?.Effective?.Inventory?.RightHand;
        if (slot == null || slot.Item.Type != AssetType.Item)
            return false;
        return Assets.LoadItem(slot.Item)?.TypeId == UAlbion.Formats.Assets.Inv.ItemType.LongRangeWeapon;
    }

    void ApplyMeleeAttack(ICombatParticipant attacker, ICombatParticipant defender)
    {
        var a = attacker?.Effective?.Combat;
        var d = defender?.Effective?.Combat;
        if (a == null || d == null || d.LifePoints == null)
            return;

        var rng = Resolve<IRandom>();

        // Weapon-type gate: a LongRangeWeapon (ItemType 6) in the weapon hand routes the
        // strike through the RANGED callback semantics (fcn.0004f057) — the to-hit roll
        // uses LongRangeCombat instead of CloseRangeCombat. PLACEHOLDER: ammunition
        // (AmmoType matching + consumption per shot) pending RE.
        bool ranged = HasRangedWeapon(attacker);

        // 1. TO-HIT: the attacker's weapon skill vs 100 (RE'd from the attack completion
        // callbacks fcn.0004eac1/fcn.0004f057 — RollSkill fcn.00035bdc). There is NO
        // defender-side evasion: the only defender check in the original is dead code.
        int weaponSkill = EffectiveSkillFor(
            attacker,
            ranged ? CombatBuffs.BuffKind.RangedCombatSkill : CombatBuffs.BuffKind.CloseCombatSkill,
            isWeaponSkill: true);
        if (!DamageCalculator.PercentRoll(weaponSkill, rng.Generate(100)))
        {
            TraceLog.Emit("attack_miss",
                ("attacker", attacker.SheetId),
                ("defender", defender.SheetId),
                ("skill", weaponSkill));
            Raise(new CombatHitEvent(TileOf(defender), 0, false, false)); // miss — shows "0"
            return;
        }

        // 2. Equipment wear (fcn.0004f920): every swing that passes the to-hit roll wears
        // the attacker's weapon and the defender's chest + head pieces (break-rate vs 1000).
        RollEquipmentBreak(attacker, UAlbion.Formats.Assets.Inv.ItemSlotId.RightHand, rng);
        RollEquipmentBreak(defender, UAlbion.Formats.Assets.Inv.ItemSlotId.Chest, rng);
        RollEquipmentBreak(defender, UAlbion.Formats.Assets.Inv.ItemSlotId.Head, rng);

        // 3. CRITICAL HIT: CriticalHit skill vs 100 → a LETHAL hit (damage = the target's
        // current LP), not a multiplier; blocked entirely by the target's crit-immunity
        // flag (sheet+0x0E & 0x80 — Ai's bodies, the named bosses and Kamulos).
        int adjusted;
        bool crit = false;
        bool critImmune = ((defender.Effective?.UnknownE ?? 0) & 0x80) != 0;
        int critSkill = EffectiveSkillFor(attacker, CombatBuffs.BuffKind.CritSkill, isWeaponSkill: false);
        if (!critImmune && DamageCalculator.PercentRoll(critSkill, rng.Generate(100)))
        {
            crit = true;
            adjusted = Math.Max(1, LifePoints(defender));
        }
        else
        {
            // 4. Damage roll (fcn.0004ee3b): attacker damage and defender protection varied
            // independently by 50..100 %, then subtracted. delta == 0 is "hit but fully
            // absorbed" (the original plays sound 451 — distinct from the to-hit miss 452).
            // Attacker damage includes Strength/25 per fcn.0004ee3b at +0x29.
            int atkRoll = rng.Generate(51);
            int defRoll = rng.Generate(51);
            int strength = (attacker.Effective?.Attributes?.Strength?.Current ?? 0)
                           + CombatBuffs.Bonus(attacker.SheetId, CombatBuffs.BuffKind.Strength);
            int rawAtk = DamageCalculator.TotalAttackWithStrength(a, strength)
                         + CombatBuffs.Bonus(attacker.SheetId, CombatBuffs.BuffKind.Attack);
            int rawDef = DamageCalculator.TotalDefense(d)
                         + CombatBuffs.Bonus(defender.SheetId, CombatBuffs.BuffKind.Defense);
            // MagicShield/PersonalProtection: the active-spell type-1 percentage
            // MULTIPLIES defense (fcn.0004ee3b: rawDef += rawDef·pct/100). The table is
            // party-only and persists across battles, decaying hourly (RE 5B).
            if (defender.SheetId.Type == AssetType.PartySheet)
            {
                int shieldPct = TryResolve<IGameState>()?.GetActiveSpellPct(
                    new PartyMemberId(AssetType.PartyMember, defender.SheetId.Id), 1) ?? 0;
                rawDef += rawDef * shieldPct / 100;
            }
            int variedAtk = DamageCalculator.VaryDamage(rawAtk, atkRoll);
            int variedDef = DamageCalculator.VaryDamage(rawDef, defRoll);
            adjusted = Math.Max(0, variedAtk - variedDef);

            if (adjusted <= 0)
            {
                TraceLog.Emit("attack_absorbed",
                    ("attacker", attacker.SheetId),
                    ("defender", defender.SheetId),
                    ("varied_atk", variedAtk),
                    ("varied_def", variedDef));
                Raise(new CombatHitEvent(TileOf(defender), 0, false, false)); // absorbed — shows "0"
                return;
            }
        }

        int baseDamage = adjusted;

        var amount = (ushort)Math.Min(ushort.MaxValue, adjusted);
        var current = LifePoints(defender);
        var next = Math.Max(0, current - amount);
        _liveHp[defender.SheetId] = next;

        // Also route through the data-change pipeline so any persistent sheet (party members
        // resolved via GameState.Sheets) updates and SheetApplier.LifeChecks fires
        // Unconscious / DeathEvent / leader-handoff. For transient monster clones the
        // TargetId mapping is null and the _liveHp shadow above already records the damage.
        var target = TryToTarget(defender);
        if (target != null)
            Raise(new DataChangeEvent(target.Value, ChangeProperty.Health, NumericOperation.SubtractAmount, amount));

        Info($"{attacker.SheetId} hits {defender.SheetId} for {amount} damage (HP {next}/{d.LifePoints.Max})");
        Raise(new CombatHitEvent(TileOf(defender), amount, next == 0, false));
        if (next == 0)
            RemoveMonsterCorpse(defender);

        TraceLog.Emit("attack_hit",
            ("attacker", attacker.SheetId),
            ("defender", defender.SheetId),
            ("attack",   DamageCalculator.TotalAttack(a)),
            ("defense",  DamageCalculator.TotalDefense(d)),
            ("base",     baseDamage),
            ("crit",     crit ? 1 : 0),
            ("damage",   amount),
            ("hp",       next),
            ("max",      d.LifePoints.Max));
    }

    /// <summary>
    /// Equipment wear roll, RE'd from MAIN.EXE fcn.0004f920 (corrected by RE batch 4 —
    /// there is NO item morph): PercentRoll(item break-rate, 1000) per connecting swing;
    /// on success the slot is flagged broken (item id unchanged) and the message shown.
    /// The original then moves the broken item to the post-combat LOOT LIST
    /// (fcn.000665ce = AppendSlotToLootList) and empties the slot; we keep it equipped
    /// with the Broken flag instead — a DELIBERATE equivalent-outcome deviation (the
    /// player keeps the item either way; RepairItem clears the flag). Monster drops DO
    /// go through the loot window. Party members only: monster kit isn't persistent.
    /// </summary>
    void RollEquipmentBreak(ICombatParticipant p, UAlbion.Formats.Assets.Inv.ItemSlotId slotId, IRandom rng)
    {
        if (p?.SheetId.Type != AssetType.PartySheet)
            return;

        var inv = p.Effective?.Inventory;
        var slot = slotId switch
        {
            UAlbion.Formats.Assets.Inv.ItemSlotId.RightHand => inv?.RightHand,
            UAlbion.Formats.Assets.Inv.ItemSlotId.Chest     => inv?.Chest,
            UAlbion.Formats.Assets.Inv.ItemSlotId.Head      => inv?.Head,
            _ => null
        };
        if (slot == null || slot.Item.IsNone || slot.Item.Type != AssetType.Item)
            return;
        if ((slot.Flags & UAlbion.Formats.Assets.Inv.ItemSlotFlags.Broken) != 0)
            return;

        var item = Assets.LoadItem(slot.Item);
        if (item == null || !DamageCalculator.PercentRoll(item.BreakRate, rng.Generate(1000)))
            return;

        Info($"[Combat] {p.SheetId}'s {item.Id} broke (rate {item.BreakRate}/1000)");
        TraceLog.Emit("item_broke", ("owner", p.SheetId), ("item", item.Id), ("slot", slotId));
        Raise(new BreakInventorySlotEvent(new PartyMemberId(AssetType.PartyMember, p.SheetId.Id), slotId));
        // The original moves the broken item to the post-combat loot list (it leaves the
        // body slot); we keep it equipped+Broken for repairability, so don't add to loot.
    }

    protected override void Subscribed()
    {
        if (_mobs.Count > 0)
            return;

        CombatBuffs.Clear(); // Buffs are battle-scoped

        foreach (var partyMember in Resolve<IParty>().StatusBarOrder)
        {
            _mobs.Add(partyMember);
            _tiles[partyMember.CombatPosition] = partyMember;
        }

        var group = Assets.LoadMonsterGroup(_groupId);
        if (group == null)
        {
            Error($"Tried to start battle with group {_groupId}, but no such group was found.");
            Enqueue(new EndCombatEvent(CombatResult.Victory));
            return;
        }

        for (int row = 0; row < SavedGame.CombatRowsForMobs; row++)
        {
            for (int column = 0; column < SavedGame.CombatColumns; column++)
            {
                var index = row * SavedGame.CombatColumns + column;
                MonsterId mobId = group.Grid[index];
                if (mobId.IsNone)
                    continue;

                // CombatPosition convention: mobs occupy the top CombatRowsForMobs rows
                // (positions 0..17 with 5x6 grid); party members occupy the bottom
                // CombatRowsForParty rows (positions 18..29) — see
                // GameState.GetCombatPositionForPlayer which adds CombatRowsForMobs*Columns
                // to translate the stored party-relative slot into the absolute grid.
                var monster = AttachChild(Resolve<IMonsterFactory>().BuildMonster(mobId, index));

                _mobs.Add(monster);
                _tiles[monster.CombatPosition] = monster;
            }
        }
    }

    public ICombatParticipant GetTile(int x, int y)
    {
        int tileIndex = x + y * SavedGame.CombatColumns;
        return GetTile(tileIndex);
    }

    public ICombatParticipant GetTile(int tileIndex)
        => tileIndex < 0 || tileIndex >= _tiles.Length ? null : _tiles[tileIndex];

    public int GetLifePoints(ICombatParticipant participant) => LifePoints(participant);
}