using System;
using UAlbion.Config;
using UAlbion.Formats.Assets.Sheets;
using UAlbion.Formats.Ids;
using UAlbion.Formats.MapEvents;

namespace UAlbion.Game.Combat.Spells;

/// <summary>
/// Regeneration (31) / Lifebringer (33), RE'd from the shared continuation 0x643fa:
/// clears NINE conditions — everything except Unconscious, Exhausted and Fleeing —
/// then heals max(1, MaxLP·M/100) (a full heal at 100 % mastery). The two spells'
/// per-target effect is identical; only SPELLDAT cost/targeting differ.
/// </summary>
public sealed class CleanseHealEffect : ISpellEffect
{
    static readonly PlayerCondition[] CleansedConditions =
    [
        PlayerCondition.Poisoned,
        PlayerCondition.Ill,
        PlayerCondition.Paralysed,
        PlayerCondition.Intoxicated,
        PlayerCondition.Blind,
        PlayerCondition.Panicking,
        PlayerCondition.Asleep,
        PlayerCondition.Insane,
        PlayerCondition.Irritated
    ];

    const PlayerConditions CleansedMask =
        PlayerConditions.Poisoned | PlayerConditions.Ill | PlayerConditions.Paralysed |
        PlayerConditions.Intoxicated | PlayerConditions.Blind | PlayerConditions.Panicking |
        PlayerConditions.Asleep | PlayerConditions.Insane | PlayerConditions.Irritated;

    public SpellId SpellId { get; }
    public CleanseHealEffect(SpellId spellId) => SpellId = spellId;

    public SpellCastOutcome Apply(SpellCastContext context)
    {
        var combat = context?.Target?.Effective?.Combat;
        if (combat?.LifePoints == null)
            return SpellCastOutcome.Failed;

        bool fullHealth = combat.LifePoints.Current >= combat.LifePoints.Max;
        bool hasCleansable = (combat.Conditions & CleansedMask) != 0;
        if (fullHealth && !hasCleansable)
            return SpellCastOutcome.Failed; // nothing to do — AP-loop retries

        if (hasCleansable
            && context.RaiseEvent != null
            && context.Target.SheetId.Type == AssetType.PartySheet)
        {
            var targetId = new TargetId(AssetType.PartyMember, context.Target.SheetId.Id);
            foreach (var condition in CleansedConditions)
                context.RaiseEvent(new ChangeStatusEvent(targetId, condition, NumericOperation.SetToMinimum));
        }

        if (!fullHealth)
        {
            int pct = Math.Max(1, context.MasteryMultiplier * 100 / 100);
            int healed = Math.Max(1, combat.LifePoints.Max * pct / 100);
            var amount = (ushort)Math.Min(ushort.MaxValue, healed);
            if (context.ApplyHeal != null)
            {
                context.ApplyHeal(context.Target, amount);
            }
            else if (context.RaiseEvent != null && context.Target.SheetId.Type == AssetType.PartySheet)
            {
                var targetId = new TargetId(AssetType.PartyMember, context.Target.SheetId.Id);
                context.RaiseEvent(new DataChangeEvent(targetId, ChangeProperty.Health, NumericOperation.AddAmount, amount));
            }
        }

        return SpellCastOutcome.Hit;
    }
}

/// <summary>
/// Recuperation (41), RE'd from handler 0x6470b: a magical full rest. Requires the party
/// to have been awake more than 8 hours (else the cast fails); then EVERY party member is
/// fully restored — LP and SP to max, Exhausted cured — and the hours-awake counter
/// resets. Mastery-independent.
/// </summary>
public sealed class RecuperationEffect : ISpellEffect
{
    public SpellId SpellId { get; }
    public RecuperationEffect(SpellId spellId) => SpellId = spellId;

    public SpellCastOutcome Apply(SpellCastContext context)
    {
        if (context?.RaiseEvent == null)
            return SpellCastOutcome.Failed;

        if ((context.HoursAwake?.Invoke() ?? 0) <= 8)
            return SpellCastOutcome.Failed; // party not tired enough yet

        var allies = context.GetAllies?.Invoke();
        if (allies == null || allies.Count == 0)
            return SpellCastOutcome.Failed;

        foreach (var ally in allies)
        {
            if (ally?.SheetId.Type != AssetType.PartySheet)
                continue;

            int maxLp = ally.Effective?.Combat?.LifePoints?.Max ?? 0;
            int maxSp = ally.Effective?.Magic?.SpellPoints?.Max ?? 0;
            var targetId = new TargetId(AssetType.PartyMember, ally.SheetId.Id);

            if (maxLp > 0)
            {
                if (context.ApplyHeal != null)
                    context.ApplyHeal(ally, maxLp); // battle shadow + sheet both clamp at max
                else
                    context.RaiseEvent(new DataChangeEvent(targetId, ChangeProperty.Health, NumericOperation.AddAmount, (ushort)Math.Min(ushort.MaxValue, maxLp)));
            }

            if (maxSp > 0)
                context.RaiseEvent(new DataChangeEvent(targetId, ChangeProperty.Mana, NumericOperation.AddAmount, (ushort)Math.Min(ushort.MaxValue, maxSp)));

            context.RaiseEvent(new ChangeStatusEvent(targetId, PlayerCondition.Exhausted, NumericOperation.SetToMinimum));
        }

        context.RaiseEvent(new UAlbion.Game.Events.ResetFatigueEvent());
        return SpellCastOutcome.Hit;
    }
}
