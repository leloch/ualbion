using System;
using UAlbion.Config;
using UAlbion.Formats.Ids;
using UAlbion.Formats.MapEvents;

namespace UAlbion.Game.Combat.Spells;

/// <summary>
/// Direct-damage spell. RE'd formula (MAIN.EXE fcn.0005fdf7 → per-spell handlers, see
/// _RE_COMBAT.md "Punch-list RE" item 1): damage = max(1, M*K/100) where M is the caster's
/// mastery multiplier (max(1, (mastery+50)/100), mastery 0..10000 grown by MagicTalent per
/// cast) and K is the per-spell constant — the damage dealt at 100 % mastery. No variance
/// roll and no caster-attribute term.
/// </summary>
public sealed class DamageSpellEffect : ISpellEffect
{
    public SpellId SpellId { get; }

    /// <summary>Damage at 100 % mastery (the per-spell K constant).</summary>
    public int K { get; }

    public DamageSpellEffect(SpellId spellId, int k)
    {
        SpellId = spellId;
        K = k;
    }

    public SpellCastOutcome Apply(SpellCastContext context)
    {
        if (context == null) return SpellCastOutcome.Failed;
        var combat = context.Target?.Effective?.Combat;
        if (combat?.LifePoints == null) return SpellCastOutcome.Failed;
        if (combat.LifePoints.Current <= 0) return SpellCastOutcome.Failed;

        int damage = Math.Max(1, context.MasteryMultiplier * K / 100);
        var amount = (ushort)Math.Min(ushort.MaxValue, damage);

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
