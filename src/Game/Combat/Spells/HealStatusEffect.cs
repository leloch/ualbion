using System;
using UAlbion.Config;
using UAlbion.Formats.Assets.Sheets;
using UAlbion.Formats.Ids;
using UAlbion.Formats.MapEvents;

namespace UAlbion.Game.Combat.Spells;

/// <summary>
/// Generic heal-status spell — clears a single <see cref="PlayerCondition"/> on the target.
/// Used for the four Dji-Kas "Heal X" spells (ids 16..19):
///   16 HealParalysis     → Paralysed
///   17 HealIntoxication  → Intoxicated
///   18 HealBlindness     → Blind
///   19 HealPoisoning     → Poisoned
/// </summary>
/// <remarks>
/// Returns <see cref="SpellCastOutcome.Failed"/> when the target doesn't have the condition
/// (mirrors the original engine's "don't consume the cast if it's a no-op" behaviour — the
/// AP-loop in <c>fcn.0004ef8b</c> retries on Failed). The cure routes through
/// <c>ChangeStatusEvent</c> → SheetApplier for party targets; monster conditions live on
/// their transient clone sheets and clear with the battle.
/// </remarks>
public sealed class HealStatusEffect : ISpellEffect
{
    public SpellId SpellId { get; }
    public PlayerCondition Condition { get; }

    public HealStatusEffect(SpellId spellId, PlayerCondition condition)
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
        if ((combat.Conditions & flag) == 0)
            return SpellCastOutcome.Failed;

        // Clear the status condition by raising ChangeStatusEvent — SheetApplier routes
        // SubtractAmount on a Status property through PlayerConditions &= ~flag.
        var raise = context.RaiseEvent;
        if (raise != null && context.Target?.SheetId.Type == UAlbion.Config.AssetType.PartySheet)
        {
            var targetId = new TargetId(UAlbion.Config.AssetType.PartyMember, context.Target.SheetId.Id);
            raise(new ChangeStatusEvent(targetId, Condition, NumericOperation.SubtractAmount, 1));
            return SpellCastOutcome.Hit;
        }

        // SPL-02: we can only route a cure to a party target. For anything else (e.g. a monster
        // with the condition on its base sheet) we can't clear it — return Failed so the cast/SP
        // isn't consumed for a no-op (the original only ever cured party members).
        return SpellCastOutcome.Failed;
    }

    /// <summary>
    /// Register the four Dji-Kas heal-status spells (16..19) plus LightHealing (9). Idempotent —
    /// re-registering replaces the existing handler for a given spell-id.
    /// </summary>
    /// <remarks>
    /// Kept under the original name for memory continuity; <see cref="DjiKasSpells.RegisterAll"/>
    /// is the new entry point that also brings in damage + status-inflict spells.
    /// </remarks>
    public static void RegisterDjiKasHealSpells()
    {
        SpellEffectRegistry.Register(new HealStatusEffect(Base.Spell.HealParalysis,    PlayerCondition.Paralysed));
        SpellEffectRegistry.Register(new HealStatusEffect(Base.Spell.HealIntoxication, PlayerCondition.Intoxicated));
        SpellEffectRegistry.Register(new HealStatusEffect(Base.Spell.HealBlindness,    PlayerCondition.Blind));
        SpellEffectRegistry.Register(new HealStatusEffect(Base.Spell.HealPoisoning,    PlayerCondition.Poisoned));
        SpellEffectRegistry.Register(new HealHpEffect(Base.Spell.LightHealing, k: 25));
    }
}
