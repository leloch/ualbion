using System;
using UAlbion.Formats.Ids;

namespace UAlbion.Game.Combat.Spells;

/// <summary>
/// Applies a temporary combat buff to the target (or the caster when no target tile was
/// picked). RE'd duration formula (fcn.0004b8a1): rounds = max(1, M·base/100) + 1 where
/// base is per-spell (Hurry 10, Berserk 10, Blind 10, Freeze 3). Persistent buffs
/// (MagicShield / PersonalProtection) last for the whole battle — the original tracks
/// them in the active-spell percentage table, not the round timer.
/// </summary>
public sealed class BuffSpellEffect : ISpellEffect
{
    public SpellId SpellId { get; }
    readonly CombatBuffs.BuffKind _kind;
    readonly int _amount;
    readonly int _baseDuration;
    readonly bool _persistent;

    public BuffSpellEffect(SpellId spellId, CombatBuffs.BuffKind kind, int amount, int baseDuration, bool persistent = false)
    {
        SpellId = spellId;
        _kind = kind;
        _amount = amount;
        _baseDuration = baseDuration;
        _persistent = persistent;
    }

    public SpellCastOutcome Apply(SpellCastContext context)
    {
        var target = context?.Target ?? context?.Caster;
        if (target?.SheetId == null)
            return SpellCastOutcome.Failed;

        int rounds = _persistent
            ? int.MaxValue / 2 // battle-scoped (CombatBuffs clears when a fight starts)
            : Math.Max(1, context.MasteryMultiplier * _baseDuration / 100) + 1;

        CombatBuffs.Add(target.SheetId, _kind, _amount, rounds);
        return SpellCastOutcome.Hit;
    }
}

/// <summary>
/// Drains HP from the target and heals the caster by the same amount (Steal Life).
/// RE'd magnitude (_RE_COMBAT.md "Punch-list RE" item 1): drains
/// max(1, M*30/100) percent of the TARGET's max LP.
/// </summary>
public sealed class StealLifeEffect : ISpellEffect
{
    public SpellId SpellId { get; }
    readonly int _k;

    public StealLifeEffect(SpellId spellId, int k = 30)
    {
        SpellId = spellId;
        _k = k;
    }

    public SpellCastOutcome Apply(SpellCastContext context)
    {
        if (context?.Target == null || context.Caster == null || context.ApplyDamage == null || context.ApplyHeal == null)
            return SpellCastOutcome.Failed;

        int maxLp = context.Target.Effective?.Combat?.LifePoints?.Max ?? 0;
        if (maxLp <= 0)
            return SpellCastOutcome.Failed;

        int pct = Math.Max(1, context.MasteryMultiplier * _k / 100);
        int amount = Math.Max(1, maxLp * pct / 100);
        context.ApplyDamage(context.Target, amount);
        context.ApplyHeal(context.Caster, amount);
        return SpellCastOutcome.Hit;
    }
}

/// <summary>
/// Drains SP from the target into the caster (Steal Magic). RE'd magnitude: drains
/// max(1, M*30/100) percent of the TARGET's max SP. Routed through Mana
/// DataChangeEvents so only party members persist the change — monsters don't track SP
/// in the battle shadow yet (PLACEHOLDER).
/// </summary>
public sealed class StealMagicEffect : ISpellEffect
{
    public SpellId SpellId { get; }
    readonly int _k;

    public StealMagicEffect(SpellId spellId, int k = 30)
    {
        SpellId = spellId;
        _k = k;
    }

    public SpellCastOutcome Apply(SpellCastContext context)
    {
        if (context?.Caster == null || context.RaiseEvent == null)
            return SpellCastOutcome.Failed;

        int maxSp = context.Target?.Effective?.Magic?.SpellPoints?.Max ?? 0;
        int pct = Math.Max(1, context.MasteryMultiplier * _k / 100);
        int amount = Math.Max(1, maxSp * pct / 100);

        if (context.Target?.SheetId.Type == UAlbion.Config.AssetType.PartySheet)
        {
            var targetId = new TargetId(UAlbion.Config.AssetType.PartyMember, context.Target.SheetId.Id);
            context.RaiseEvent(new UAlbion.Formats.MapEvents.DataChangeEvent(
                targetId, UAlbion.Formats.MapEvents.ChangeProperty.Mana,
                UAlbion.Formats.MapEvents.NumericOperation.SubtractAmount, (ushort)amount));
        }

        if (context.Caster.SheetId.Type == UAlbion.Config.AssetType.PartySheet)
        {
            var casterId = new TargetId(UAlbion.Config.AssetType.PartyMember, context.Caster.SheetId.Id);
            context.RaiseEvent(new UAlbion.Formats.MapEvents.DataChangeEvent(
                casterId, UAlbion.Formats.MapEvents.ChangeProperty.Mana,
                UAlbion.Formats.MapEvents.NumericOperation.AddAmount, (ushort)amount));
        }

        return SpellCastOutcome.Hit;
    }
}

/// <summary>Ends the current combat as an escape (Quick Withdrawal).</summary>
public sealed class WithdrawEffect : ISpellEffect
{
    public SpellId SpellId { get; }
    public WithdrawEffect(SpellId spellId) => SpellId = spellId;

    public SpellCastOutcome Apply(SpellCastContext context)
    {
        if (context?.RaiseEvent == null)
            return SpellCastOutcome.Failed;
        context.RaiseEvent(new EndCombatEvent(CombatResult.Retreat));
        return SpellCastOutcome.Hit;
    }
}

/// <summary>
/// Places a damage trap on the chosen combat tile (trap/mine spells). Trap damage uses
/// the RE'd magnitude formula max(1, M*K/100) with the per-spell K constant.
/// </summary>
public sealed class TrapSpellEffect : ISpellEffect
{
    public SpellId SpellId { get; }
    readonly int _k;

    public TrapSpellEffect(SpellId spellId, int k)
    {
        SpellId = spellId;
        _k = k;
    }

    public SpellCastOutcome Apply(SpellCastContext context)
    {
        if (context?.PlaceTrap == null || context.CombatTargetPosition < 0)
            return SpellCastOutcome.Failed;
        context.PlaceTrap(context.CombatTargetPosition, Math.Max(1, context.MasteryMultiplier * _k / 100));
        return SpellCastOutcome.Hit;
    }
}

/// <summary>Removes a trap from the chosen combat tile.</summary>
public sealed class RemoveTrapEffect : ISpellEffect
{
    public SpellId SpellId { get; }
    public RemoveTrapEffect(SpellId spellId) => SpellId = spellId;

    public SpellCastOutcome Apply(SpellCastContext context)
    {
        if (context?.RemoveTrap == null || context.CombatTargetPosition < 0)
            return SpellCastOutcome.Failed;
        context.RemoveTrap(context.CombatTargetPosition);
        return SpellCastOutcome.Hit;
    }
}

/// <summary>Casts that resolve to raising one or more game events (Light, Map View).</summary>
public sealed class EventSpellEffect : ISpellEffect
{
    public SpellId SpellId { get; }
    readonly Func<UAlbion.Api.Eventing.IEvent>[] _events;

    public EventSpellEffect(SpellId spellId, params Func<UAlbion.Api.Eventing.IEvent>[] events)
    {
        SpellId = spellId;
        _events = events ?? [];
    }

    public SpellCastOutcome Apply(SpellCastContext context)
    {
        if (context?.RaiseEvent == null)
            return SpellCastOutcome.Failed;
        foreach (var build in _events)
            context.RaiseEvent(build());
        return SpellCastOutcome.Hit;
    }
}

/// <summary>
/// Utility spells whose systemic effect isn't wired yet (View of Life, Levitation,
/// Teleporter). They cast successfully (consuming SP/charges) and log what WOULD happen —
/// explicit PLACEHOLDERs so nothing silently fails, each naming the subsystem that needs
/// to exist before they can be completed 1:1.
/// </summary>
public sealed class UtilitySpellEffect : ISpellEffect
{
    public SpellId SpellId { get; }
    readonly string _description;

    public UtilitySpellEffect(SpellId spellId, string description)
    {
        SpellId = spellId;
        _description = description;
    }

    public SpellCastOutcome Apply(SpellCastContext context)
    {
        if (context == null)
            return SpellCastOutcome.Failed;
        TraceLog.Emit("spell_utility", ("spell", SpellId), ("todo", _description));
        return SpellCastOutcome.Hit;
    }
}
