using System;
using UAlbion.Config;
using UAlbion.Formats.Assets.Sheets;
using UAlbion.Formats.Ids;
using UAlbion.Formats.MapEvents;

namespace UAlbion.Game.Combat.Spells;

/// <summary>
/// Adds a <see cref="PlayerCondition"/> to the target. Mirror of <see cref="HealStatusEffect"/>
/// for the opposite direction — used by SleepSpores (Asleep), ThornSnare (Paralysed),
/// BlindingSpark/Ray/Storm (Blind on top of their damage component, applied via a separate
/// effect instance), Fungification (Insane-style flag), etc.
/// </summary>
public sealed class InflictStatusEffect : ISpellEffect
{
    public SpellId SpellId { get; }
    public PlayerCondition Condition { get; }

    public InflictStatusEffect(SpellId spellId, PlayerCondition condition)
    {
        SpellId = spellId;
        Condition = condition;
    }

    public SpellCastOutcome Apply(SpellCastContext context)
    {
        if (context == null) return SpellCastOutcome.Failed;
        var combat = context.Target?.Effective?.Combat;
        if (combat == null) return SpellCastOutcome.Failed;

        var flag = Condition.ToFlag();
        // Don't waste the cast if the condition is already present — matches the AP-loop's
        // "no-op = retry" semantics that we already use for HealStatusEffect.
        if ((combat.Conditions & flag) != 0)
            return SpellCastOutcome.Failed;

        if (context.RaiseEvent != null && context.Target?.SheetId != null)
        {
            var targetId = (TargetId)(AssetId)context.Target.SheetId;
            context.RaiseEvent(new ChangeStatusEvent(targetId, Condition, NumericOperation.AddAmount, 1));
        }
        return SpellCastOutcome.Hit;
    }
}
