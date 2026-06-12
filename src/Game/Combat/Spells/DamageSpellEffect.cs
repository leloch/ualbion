using System;
using UAlbion.Config;
using UAlbion.Formats.Ids;
using UAlbion.Formats.MapEvents;

namespace UAlbion.Game.Combat.Spells;

/// <summary>
/// Direct-damage spell. Damage = <see cref="BaseDamage"/> + caster spell-strength × <see
/// cref="StrengthScale"/>, with the same ±10 % uniform variance applied by melee attacks.
/// Used today by Dji-Kas FrostSplinter / FrostCrystal / FrostAvalanche / BlindingSpark /
/// BlindingRay / BlindingStorm — magnitudes are first-pass estimates pending the real
/// formula RE.
/// </summary>
public sealed class DamageSpellEffect : ISpellEffect
{
    public SpellId SpellId { get; }
    public int BaseDamage { get; }
    public int StrengthScale { get; }

    public DamageSpellEffect(SpellId spellId, int baseDamage, int strengthScale = 1)
    {
        SpellId = spellId;
        BaseDamage = baseDamage;
        StrengthScale = strengthScale;
    }

    public SpellCastOutcome Apply(SpellCastContext context)
    {
        if (context == null) return SpellCastOutcome.Failed;
        var combat = context.Target?.Effective?.Combat;
        if (combat?.LifePoints == null) return SpellCastOutcome.Failed;
        if (combat.LifePoints.Current <= 0) return SpellCastOutcome.Failed;

        int raw = BaseDamage + context.SpellStrength * StrengthScale;
        if (raw <= 0) return SpellCastOutcome.Failed;
        int rolled = context.Random?.Invoke(21) ?? 10;          // mid-range default if no RNG
        int varied = DamageCalculator.VaryDamage(raw, rolled);
        var amount = (ushort)Math.Min(ushort.MaxValue, varied);

        if (context.ApplyDamage != null)
        {
            // In combat: route through the battle's HP shadow so monster damage actually
            // lands (monsters aren't in GameState.Sheets so events can't reach them).
            context.ApplyDamage(context.Target, amount);
        }
        else if (context.RaiseEvent != null && context.Target?.SheetId.Type == UAlbion.Config.AssetType.PartySheet)
        {
            // TargetId only accepts PartyMember (not PartySheet) — remap the same numeric id.
            var targetId = new TargetId(UAlbion.Config.AssetType.PartyMember, context.Target.SheetId.Id);
            context.RaiseEvent(new DataChangeEvent(targetId, ChangeProperty.Health, NumericOperation.SubtractAmount, amount));
        }
        return SpellCastOutcome.Hit;
    }
}
