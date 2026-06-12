using System;
using System.Collections.Generic;
using UAlbion.Config;
using UAlbion.Formats.Ids;
using UAlbion.Game.State;

namespace UAlbion.Game.Combat.Spells;

/// <summary>
/// The universal spell success gate, RE'd from MAIN.EXE fcn.000601a6 (used by every
/// "chance" spell): the target must be a party member or match the spell's creature-class
/// mask (else "unaffected creature type", effect 774), and the spell lands iff
/// mastery% &gt; the target's Magic Resistance. DETERMINISTIC — the original has no random
/// roll here. (Party targets additionally get their resistance boosted by the
/// MagicShield/PersonalProtection active-spell percentage — PLACEHOLDER: not applied yet,
/// the type-2 table entry isn't tracked per member.)
/// </summary>
public static class SpellSuccessGate
{
    /// <summary>Demon class bits of the creature-class bitmask (sheet+0x0E) — mask 0x44.</summary>
    public const int DemonClassMask = 0x44;

    public static bool Lands(SpellCastContext context, ICombatParticipant target, int classMask = 0xFFFF)
    {
        if (context == null || target?.Effective == null)
            return false;

        if (target.SheetId.Type != AssetType.PartySheet
            && classMask != 0xFFFF
            && (target.Effective.UnknownE & classMask) == 0)
        {
            return false; // unaffected creature type
        }

        int resist = target.Effective.Attributes?.MagicResistance?.Current ?? 0;
        return context.MasteryMultiplier > resist;
    }
}

/// <summary>
/// Goddess' Wrath (spell 39), RE'd from handler 0xa1134: kills
/// max(1, livingMonsters * M / 100) randomly-chosen DISTINCT monsters outright, each kill
/// individually gated by the success gate (class mask 0xFFFF). At 100 % mastery against
/// low-resistance monsters this wipes the whole enemy side. No damage constant exists.
/// </summary>
public sealed class GoddessWrathEffect : ISpellEffect
{
    public SpellId SpellId { get; }
    public GoddessWrathEffect(SpellId spellId) => SpellId = spellId;

    public SpellCastOutcome Apply(SpellCastContext context)
    {
        var living = context?.GetLiveEnemies?.Invoke();
        if (living == null || living.Count == 0 || context.InstantKill == null)
            return SpellCastOutcome.Failed;

        int count = Math.Max(1, living.Count * context.MasteryMultiplier / 100);
        var pool = new List<ICombatParticipant>(living);
        bool anyKilled = false;
        for (int i = 0; i < count && pool.Count > 0; i++)
        {
            int pick = context.Random?.Invoke(pool.Count) ?? 0;
            var victim = pool[pick];
            pool.RemoveAt(pick);
            if (!SpellSuccessGate.Lands(context, victim))
                continue;
            context.InstantKill(victim);
            anyKilled = true;
        }

        return anyKilled ? SpellCastOutcome.Hit : SpellCastOutcome.Resisted;
    }
}

/// <summary>
/// The Banish-demon family (spells 62/63/64), RE'd from handlers 0xa1aec/0xa1b20: the
/// target must be demon-class (creature mask 0x44) and the success gate must pass
/// (M &gt; MagicResist) — then it dies outright with the soul-rising animation. The three
/// spells differ only in SPELLDAT target area (single / row / all — PLACEHOLDER: all
/// three resolve single-target until area targeting exists).
/// </summary>
public sealed class BanishDemonEffect : ISpellEffect
{
    public SpellId SpellId { get; }
    public BanishDemonEffect(SpellId spellId) => SpellId = spellId;

    public SpellCastOutcome Apply(SpellCastContext context)
    {
        var target = context?.Target;
        if (target == null || context.InstantKill == null)
            return SpellCastOutcome.Failed;

        if (!SpellSuccessGate.Lands(context, target, SpellSuccessGate.DemonClassMask))
            return SpellCastOutcome.Resisted;

        context.InstantKill(target);
        return SpellCastOutcome.Hit;
    }
}

/// <summary>
/// Berserk (spell 65), RE'd from fcn.0004b8a1 kind 3 — applied once, on application:
/// an immediate self-cost of 25 % of CURRENT LP (through ApplyDamage, so on-damage
/// reactions can trigger), then STR, CloseRange/LongRange/CriticalHit skills and the
/// base-damage word all x150/100 until expiry (duration = max(1, M*10/100) + 1 rounds).
/// Expressed through the additive battle-buff table as +half the current values — the
/// same net effect as the original's in-place multiply/divide.
/// </summary>
public sealed class BerserkSpellEffect : ISpellEffect
{
    public SpellId SpellId { get; }
    public BerserkSpellEffect(SpellId spellId) => SpellId = spellId;

    public SpellCastOutcome Apply(SpellCastContext context)
    {
        var target = context?.Target ?? context?.Caster;
        if (target?.SheetId == null)
            return SpellCastOutcome.Failed;

        int rounds = Math.Max(1, context.MasteryMultiplier * 10 / 100) + 1;

        int lp = target.Effective?.Combat?.LifePoints?.Current ?? 0;
        if (lp > 1)
            context.ApplyDamage?.Invoke(target, Math.Max(1, lp / 4));

        int str     = target.Effective?.Attributes?.Strength?.Current ?? 0;
        int close   = target.Effective?.Skills?.CloseCombat?.Current ?? 0;
        int ranged  = target.Effective?.Skills?.RangedCombat?.Current ?? 0;
        int crit    = target.Effective?.Skills?.CriticalChance?.Current ?? 0;
        int baseAtk = target.Effective?.Combat?.BaseAttack ?? 0;

        CombatBuffs.Add(target.SheetId, CombatBuffs.BuffKind.Strength,          str / 2,     rounds);
        CombatBuffs.Add(target.SheetId, CombatBuffs.BuffKind.CloseCombatSkill,  close / 2,   rounds);
        CombatBuffs.Add(target.SheetId, CombatBuffs.BuffKind.RangedCombatSkill, ranged / 2,  rounds);
        CombatBuffs.Add(target.SheetId, CombatBuffs.BuffKind.CritSkill,         crit / 2,    rounds);
        CombatBuffs.Add(target.SheetId, CombatBuffs.BuffKind.Attack,            baseAtk / 2, rounds);
        return SpellCastOutcome.Hit;
    }
}
