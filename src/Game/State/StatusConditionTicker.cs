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
/// The only over-time effects are:
///   - Poisoned: drains 1-5 LP per game hour.
///   - Exhaustion: set after 48 hours awake (stat penalties); 10 % LP per 2 h beyond
///     (PLACEHOLDER: the LP drain is implemented; the stat-penalty application —
///     STR×¾, other stats/skills ×½ — needs the effective-sheet hook).
/// </summary>
public class StatusConditionTicker : GameComponent
{
    int _poisonSeed;

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

            // Exhaustion sets in after 48 hours awake.
            if (state.HoursSinceResting >= 48 && (combat.Conditions & PlayerConditions.Exhausted) == 0)
                Raise(new ChangeStatusEvent(target, PlayerCondition.Exhausted, NumericOperation.AddAmount, 1));
        }
    }
}
