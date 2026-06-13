using System;
using UAlbion.Api.Eventing;
using UAlbion.Config;
using UAlbion.Formats.Assets.Sheets;
using UAlbion.Formats.Ids;
using UAlbion.Formats.MapEvents;
using UAlbion.Game.Events;

namespace UAlbion.Game.State;

/// <summary>
/// Hourly condition effects, per the RE'd original semantics (_RE_COMBAT.md
/// "Placeholder formulas" item 7): there is NO timed decay of conditions — they are
/// cure-only (spells, healers, rest for Exhausted), and the combat-scoped ones
/// (Irritated/Asleep/Panicking/Fleeing/Paralysed) batch-clear when combat ends.
/// The only over-time effects (hour tick fcn.00043acc + odd-hour fatigue fcn.00039362):
///   - Poisoned: drains 1-5 LP per game hour.
///   - Awake &gt; 24 h: "Everyone is getting tired" message (odd hours).
///   - Awake &gt; 48 h: Exhausted set (stat penalties applied by SheetApplier — STR×¾,
///     other attributes/skills ×½ with backups); members ALREADY Exhausted lose 10 % of
///     current LP per odd-hour check.
/// </summary>
public class StatusConditionTicker : GameComponent
{
    int _poisonSeed;
    bool _oddHour;

    public StatusConditionTicker()
    {
        On<HourElapsedEvent>(_ => TickAll());
    }

    void TickAll()
    {
        var party = TryResolve<IParty>();
        var state = TryResolve<IGameState>();
        if (party == null || state == null)
            return;

        // STATE-04: the original gates the fatigue block on the game-clock hour-of-day parity
        // (fcn.00043acc, 0x153b2e % 2), NOT a per-event toggle — a load or odd-count bulk advance
        // permanently flipped the phase relative to the real clock. Read it off Time.Hour.
        _oddHour = (state.Time.Hour & 1) != 0;

        if (_oddHour && state.HoursSinceResting > 24 && state.HoursSinceResting <= 48)
        {
            var tf = TryResolve<Text.ITextFormatter>();
            if (tf != null)
                Raise(new DescriptionTextEvent(tf.Format(Base.SystemText.Misc_EveryoneIsGettingTired)));
        }

        foreach (var pm in party.StatusBarOrder)
        {
            var combat = pm?.Apparent?.Combat;
            if (combat == null)
                continue;

            var target = new TargetId(AssetType.PartyMember, pm.Id.Id);

            // Poison drains 1-5 LP per game hour until cured.
            if ((combat.Conditions & PlayerConditions.Poisoned) != 0 && combat.LifePoints.Current > 0)
            {
                var rng = TryResolve<IRandom>();
                int drain = 1 + (rng?.Generate(5) ?? (_poisonSeed++ % 5));
                Raise(new DataChangeEvent(target, ChangeProperty.Health, NumericOperation.SubtractAmount, (ushort)drain));
            }

            if (!_oddHour)
                continue;

            bool exhausted = (combat.Conditions & PlayerConditions.Exhausted) != 0;
            if (state.HoursSinceResting > 48 && !exhausted)
            {
                // Sets the condition; SheetApplier backs up + reduces the stats.
                Raise(new ChangeStatusEvent(target, PlayerCondition.Exhausted, NumericOperation.SetToMaximum));
            }
            else if (exhausted && combat.LifePoints.Current > 1)
            {
                // Already exhausted: 10 % of current LP per 2-hour check.
                int drain = Math.Max(1, combat.LifePoints.Current / 10);
                Raise(new DataChangeEvent(target, ChangeProperty.Health, NumericOperation.SubtractAmount, (ushort)drain));
            }
        }
    }
}
