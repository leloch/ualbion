using System;
using UAlbion.Config;
using UAlbion.Formats.Ids;
using UAlbion.Formats.MapEvents;

namespace UAlbion.Game.Combat.Spells;

/// <summary>
/// HP heal. RE'd formula (docs/re/RE_COMBAT.md "Punch-list RE" item 1): the heal is a percentage
/// of the target's MAX life points — pct = max(1, M*K/100) where K is the percentage at
/// 100 % mastery (LightHealing 25, HealingDC/HealingD 40) and M the caster's mastery
/// multiplier.
/// </summary>
public sealed class HealHpEffect : ISpellEffect
{
    public SpellId SpellId { get; }

    /// <summary>Percent of target max LP healed at 100 % mastery.</summary>
    public int K { get; }

    public HealHpEffect(SpellId spellId, int k)
    {
        SpellId = spellId;
        K = k;
    }

    public SpellCastOutcome Apply(SpellCastContext context)
    {
        if (context == null) return SpellCastOutcome.Failed;
        var combat = context.Target?.Effective?.Combat;
        if (combat?.LifePoints == null) return SpellCastOutcome.Failed;

        // Don't waste the cast on a fully healed target (matches the original's "no-op = retry"
        // pattern for ineffective spells).
        if (combat.LifePoints.Current >= combat.LifePoints.Max)
            return SpellCastOutcome.Failed;

        int pct = Math.Max(1, context.MasteryMultiplier * K / 100);
        int healed = Math.Max(1, combat.LifePoints.Max * pct / 100);
        var amount = (ushort)Math.Min(ushort.MaxValue, healed);

        if (context.ApplyHeal != null)
        {
            // In combat: keep the battle HP shadow in sync (covers monsters too).
            context.ApplyHeal(context.Target, amount);
        }
        else if (context.RaiseEvent != null && context.Target?.SheetId.Type == UAlbion.Config.AssetType.PartySheet)
        {
            // TargetId only accepts PartyMember; PartySheet.N → PartyMember.N (same numeric id).
            var targetId = new TargetId(UAlbion.Config.AssetType.PartyMember, context.Target.SheetId.Id);
            context.RaiseEvent(new DataChangeEvent(targetId, ChangeProperty.Health, NumericOperation.AddAmount, amount));
        }
        return SpellCastOutcome.Hit;
    }
}
