using System;
using UAlbion.Config;
using UAlbion.Formats.Assets.Sheets;
using UAlbion.Formats.Ids;
using UAlbion.Formats.MapEvents;

namespace UAlbion.Game.Combat.Spells;

/// <summary>
/// Heals HP on the target by a flat amount + spell-strength bonus. Used today by Dji-Kas
/// spell 9 (LightHealing); higher-tier heal spells (HealingDC, Recuperation, etc.) will
/// reuse this with larger base amounts once their formulas are decoded.
/// </summary>
/// <remarks>
/// Placeholder formula until the real one is reverse-engineered: <c>baseAmount +
/// spellStrength * scale</c>. SpellStrength is the caster's school-strength byte 0..15
/// (see CharacterSheet.SpellStrengths). The original engine almost certainly adds a
/// random roll on top — that variance gets added later.
/// </remarks>
public sealed class HealHpEffect : ISpellEffect
{
    public SpellId SpellId { get; }
    public int BaseAmount { get; }
    public int StrengthScale { get; }

    public HealHpEffect(SpellId spellId, int baseAmount, int strengthScale = 1)
    {
        SpellId = spellId;
        BaseAmount = baseAmount;
        StrengthScale = strengthScale;
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

        int healed = BaseAmount + context.SpellStrength * StrengthScale;
        if (healed <= 0) return SpellCastOutcome.Failed;
        var amount = (ushort)Math.Min(ushort.MaxValue, healed);

        if (context.RaiseEvent != null && context.Target?.SheetId != null)
        {
            var targetId = (TargetId)(AssetId)context.Target.SheetId;
            context.RaiseEvent(new DataChangeEvent(targetId, ChangeProperty.Health, NumericOperation.AddAmount, amount));
        }
        return SpellCastOutcome.Hit;
    }
}
