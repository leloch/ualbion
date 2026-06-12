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

    public IReadOnlyList<ICombatParticipant> Mobs { get; }
    public event Action Complete;

    public Battle(MonsterGroupId groupId, SpriteId backgroundId)
    {
        On<EndCombatEvent>(_ => Complete?.Invoke());
        OnAsync<BeginCombatRoundEvent>(BeginRoundAsync);
        OnAsync<ObserveCombatEvent>(Observe);
        On<QueueCombatActionEvent>(OnQueueAction);

        _groupId = groupId;
        Mobs = _mobs;
        AttachChild(new CombatActionPicker());

        // AttachChild(new UiFixedPositionElement(backgroundId, UiConstants.UiExtents));
        AttachChild(new Sprite(
            backgroundId,
            DrawLayer.Background,
            SpriteKeyFlags.NoTransform,
            SpriteFlags.LeftAligned)
        {
            Position = new Vector3(-1.0f, 1.0f, 1.0f),
            Size = new Vector2(2.0f, -2.0f)
        });
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

    async AlbionTask BeginRoundAsync(BeginCombatRoundEvent _)
    {
        // Resolve exactly ONE round per invocation — the original runs a single round each
        // time the player confirms, then returns to the planning grid. Combatants act in
        // initiative order (highest Speed first) — matches the original engine's
        // fcn.0004c3f5 builder which sorts the combatant table descending by sheet
        // attribute #3 (Speed).
        var partyAlive = LiveParticipants(forParty: true).ToList();
        var mobsAlive  = LiveParticipants(forParty: false).ToList();
        if (CheckBattleOver(partyAlive.Count, mobsAlive.Count))
            return;

        foreach (var (attacker, isParty) in OrderByInitiative(partyAlive, mobsAlive))
        {
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
            TakeTurn(attacker, forParty: isParty);
            await RaiseA(new WallClockTimerEvent(TurnResultDelaySeconds));

            if (!LiveParticipants(forParty: true).Any() || !LiveParticipants(forParty: false).Any())
                break;
        }

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
            return true;
        }
        if (mobsAlive == 0)
        {
            Raise(new EndCombatEvent(CombatResult.Victory));
            return true;
        }
        return false;
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
    void RemoveMonsterCorpse(ICombatParticipant p)
    {
        if (p == null || p.SheetId.Type == AssetType.PartySheet)
            return;
        int tile = TileOf(p);
        if (tile >= 0)
            _tiles[tile] = null;
        _corpses.Add(p);
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
        // Monster turns route through MonsterAi.ChooseNormalAction to pick a weighted-random
        // action bit (Summon / Action2 / Action4). All three currently fall through to melee
        // because the per-bit handlers aren't implemented yet — but exercising the AI keeps
        // the structure honest and ready for the per-action wire-up later.
        if (!forParty)
        {
            var available = MonsterAi.AvailableActions.None;       // PLACEHOLDER: read from
            // mob sheet's action mask when that field is decoded. For now every mob defaults
            // to plain melee, so available stays None and ChooseNormalAction returns None.
            if (available != MonsterAi.AvailableActions.None)
            {
                var rng = Resolve<IRandom>();
                var picked = MonsterAi.ChooseNormalAction(available, () => rng.Generate(16), _ => true);
                _ = picked; // TODO: dispatch per-bit (Summon→spawn, etc.) once decoded
            }
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
    /// Move a combatant to an empty tile. First pass: teleport-style reposition with no
    /// path-find or movement-range limit (the original engine path-finds via
    /// fcn.00051b51 / fcn.00053871 and limits distance by Speed; that grid walk isn't
    /// fully decoded yet — PLACEHOLDER until it is).
    /// </summary>
    void MoveCombatant(ICombatParticipant mover, int targetTile)
    {
        if (mover == null || targetTile < 0 || targetTile >= _tiles.Length)
            return;

        if (_tiles[targetTile] != null && LifePoints(_tiles[targetTile]) > 0)
        {
            Info($"[Combat] {mover.SheetId} can't move to occupied tile {targetTile}");
            ShowCombatMessage(Base.SystemText.CombatMsg_MoveWasBlocked); // SYSTEXTS 443
            return;
        }

        int oldTile = System.Array.IndexOf(_tiles, mover);
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
    /// Resolve a queued spell cast through the SpellEffectRegistry: consume SP, apply the
    /// effect to the occupant of the target tile (or the caster for self-target picks).
    /// </summary>
    void CastQueuedSpell(ICombatParticipant caster, SpellId spellId, int targetTile)
    {
        if (caster == null || spellId.IsNone)
            return;

        var spell = Assets.LoadSpell(spellId);
        int cost = spell?.Cost ?? 0;
        int sp = caster.Effective?.Magic?.SpellPoints?.Current ?? 0;
        if (cost > 0 && sp < cost)
        {
            Info($"[Combat] {caster.SheetId} lacks SP for {spellId} ({sp}/{cost})");
            return;
        }

        var target = targetTile >= 0 && targetTile < _tiles.Length ? _tiles[targetTile] : null;
        if (target == null || LifePoints(target) <= 0)
        {
            // Empty / dead tile picked: fall back by the spell's declared target side.
            // Monster-targeting spells retarget the nearest live enemy (like melee);
            // party-targeting ones apply to the caster. Without this an offensive spell
            // at an empty tile would hit the caster.
            var targets = spell?.Targets ?? default;
            bool offensive = (targets & (UAlbion.Formats.Assets.SpellTargets.OneMonster
                                        | UAlbion.Formats.Assets.SpellTargets.RowOfMonsters
                                        | UAlbion.Formats.Assets.SpellTargets.AllMonsters)) != 0;
            target = offensive
                ? LiveParticipants(forParty: !IsParty(caster)).FirstOrDefault() ?? caster
                : caster;
        }

        var rng = Resolve<IRandom>();
        var context = new SpellCastContext
        {
            Caster = caster,
            Target = target,
            CombatTargetPosition = targetTile,
            SpellStrength = (byte)(caster.Effective?.Level ?? 1),
            Random = max => rng.Generate(max),
            RaiseEvent = Raise,
            ApplyDamage = ApplyDirectDamage,
            ApplyHeal = ApplyDirectHeal,
            PlaceTrap = (tile, damage) => _traps[tile] = damage,
            RemoveTrap = tile => _traps.Remove(tile)
        };

        var outcome = SpellEffectRegistry.Cast(spellId, context);
        Info($"[Combat] {caster.SheetId} casts {spellId} at tile {targetTile}: {outcome}");
        TraceLog.Emit("combat_cast", ("actor", caster.SheetId), ("spell", spellId), ("tile", targetTile), ("outcome", outcome));

        // SP is consumed when the cast is attempted, regardless of resist — matches the
        // original engine's charge/SP handling for unfulfilled casts.
        if (cost > 0)
        {
            var casterTarget = TryToTarget(caster);
            if (casterTarget != null)
                Raise(new DataChangeEvent(casterTarget.Value, ChangeProperty.Mana, NumericOperation.SubtractAmount, (ushort)cost));
        }
    }

    /// <summary>
    /// Resolve a queued magic-item use: cast the item's spell with no SP cost.
    /// PLACEHOLDER: charge consumption needs the inventory slot plumbing
    /// (InventoryManager.OnActivateItemSpell has it for the out-of-combat path).
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
            SpellStrength = 1, // Item casts use the item's fixed strength, not caster level
            Random = max => rng.Generate(max),
            RaiseEvent = Raise,
            ApplyDamage = ApplyDirectDamage,
            ApplyHeal = ApplyDirectHeal,
            PlaceTrap = (tile, damage) => _traps[tile] = damage,
            RemoveTrap = tile => _traps.Remove(tile)
        };

        var outcome = SpellEffectRegistry.Cast(pending.Spell, context);
        Info($"[Combat] {user.SheetId} uses item {pending.Item} ({pending.Spell}): {outcome}");
        TraceLog.Emit("combat_use_item", ("actor", user.SheetId), ("item", pending.Item), ("spell", pending.Spell), ("outcome", outcome));
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

    void ApplyMeleeAttack(ICombatParticipant attacker, ICombatParticipant defender)
    {
        var a = attacker?.Effective?.Combat;
        var d = defender?.Effective?.Combat;
        if (a == null || d == null || d.LifePoints == null)
            return;

        // RE'd from MAIN.EXE fcn.0004ed77 → fcn.0004ee3b: the original engine has NO
        // separate "did it hit?" roll. Both attacker damage and defender protection get
        // varied independently by 50..100 %, then subtracted. delta == 0 IS the miss.
        // This produces a natural hit-chance distribution from the variance overlap
        // (heavy armour = damage often falls to 0; powerful attacker = damage rarely 0).
        //
        // Attacker damage includes Strength/25 per fcn.0004ee3b at +0x29.
        var rng = Resolve<IRandom>();
        int atkRoll = rng.Generate(51);
        int defRoll = rng.Generate(51);
        int strength = attacker.Effective?.Attributes?.Strength?.Current ?? 0;
        int rawAtk = DamageCalculator.TotalAttackWithStrength(a, strength)
                     + CombatBuffs.Bonus(attacker.SheetId, CombatBuffs.BuffKind.Attack);
        int rawDef = DamageCalculator.TotalDefense(d)
                     + CombatBuffs.Bonus(defender.SheetId, CombatBuffs.BuffKind.Defense);
        int variedAtk = DamageCalculator.VaryDamage(rawAtk, atkRoll);
        int variedDef = DamageCalculator.VaryDamage(rawDef, defRoll);
        int adjusted = Math.Max(0, variedAtk - variedDef);

        if (adjusted <= 0)
        {
            TraceLog.Emit("attack_miss",
                ("attacker", attacker.SheetId),
                ("defender", defender.SheetId),
                ("varied_atk", variedAtk),
                ("varied_def", variedDef));
            Raise(new CombatHitEvent(TileOf(defender), 0, false, false)); // miss — shows "0"
            return;
        }

        // Crit roll — doubles damage on success. PLACEHOLDER 5 %: a Critical-Hit skill
        // value lives on the sheet (PRTCHAR +0x8A) but the original engine's crit formula
        // isn't fully decoded yet. The Crit Hit skill is *probably* the crit-chance %.
        int critRoll = rng.Generate(100);
        bool crit = DamageCalculator.RollCrit(critRoll);
        if (crit) adjusted *= DamageCalculator.CritDamageMultiplier;

        // For trace continuity with the old "baseDamage" / "variance" keys.
        int baseDamage = adjusted;
        int varianceRoll = atkRoll;

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
            ("variance", varianceRoll),
            ("crit",     crit ? 1 : 0),
            ("damage",   amount),
            ("hp",       next),
            ("max",      d.LifePoints.Max));
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
}