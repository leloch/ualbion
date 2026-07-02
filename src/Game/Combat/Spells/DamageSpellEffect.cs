using System;
using UAlbion.Config;
using UAlbion.Formats.Ids;
using UAlbion.Formats.MapEvents;

namespace UAlbion.Game.Combat.Spells;

/// <summary>
/// Direct-damage spell. RE'd formula (MAIN.EXE fcn.0005fdf7 → per-spell handlers; margin
/// scaling corrected in _RE_COMBAT.md "RE batch 4"): the handler first runs the success
/// gate fcn.000601a6 — resisted outright when mastery% &lt;= target MagicResist — and the
/// damage scales on the gate MARGIN: damage = max(1, (M − resist) * K/100). Verified for
/// the frost line, SmallFireball and Fungification; applied uniformly to the per-spell-
/// handler damage school. No variance roll and no caster-attribute term.
/// </summary>
public sealed class DamageSpellEffect : ISpellEffect
{
    public SpellId SpellId { get; }

    /// <summary>Damage at 100 % mastery (the per-spell K constant).</summary>
    public int K { get; }

    /// <summary>
    /// Freeze rider base duration (fcn.0004b8a1 kind 1, base 3 for the frost line):
    /// on a successful hit the target also skips its turns for max(1, M·base/100)+1
    /// rounds (2-4 in practice). 0 = no rider.
    /// </summary>
    public int FreezeBase { get; }

    /// <summary>
    /// Whether the success gate applies. Every damage spell runs it EXCEPT
    /// LightningStrike (RE batch 5B: its continuation 0xa3406 never calls the gate —
    /// damage = max(1, raw M·33/100), ignoring MagicResist entirely).
    /// </summary>
    public bool Gated { get; }

    public DamageSpellEffect(SpellId spellId, int k, int freezeBase = 0, bool gated = true)
    {
        SpellId = spellId;
        K = k;
        FreezeBase = freezeBase;
        Gated = gated;
    }

    public SpellCastOutcome Apply(SpellCastContext context)
    {
        if (context == null) return SpellCastOutcome.Failed;
        var combat = context.Target?.Effective?.Combat;
        if (combat?.LifePoints == null) return SpellCastOutcome.Failed;
        if (combat.LifePoints.Current <= 0) return SpellCastOutcome.Failed;

        int margin;
        if (Gated)
        {
            margin = SpellSuccessGate.Margin(context, context.Target);
            if (margin <= 0)
                return SpellCastOutcome.Resisted;
        }
        else
        {
            margin = context.MasteryMultiplier; // LightningStrike: raw M, no resist check
        }

        int damage = Math.Max(1, margin * K / 100);
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

        if (FreezeBase > 0)
        {
            // Duration scales on the gate margin too (fcn.0004b8a1 receives the margin).
            int rounds = Math.Max(1, margin * FreezeBase / 100) + 1;
            CombatBuffs.Add(context.Target, CombatBuffs.BuffKind.Freeze, 0, rounds);
        }

        return SpellCastOutcome.Hit;
    }
}
