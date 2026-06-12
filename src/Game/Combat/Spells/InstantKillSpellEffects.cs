using System;
using System.Collections.Generic;
using UAlbion.Config;
using UAlbion.Formats.Ids;
using UAlbion.Game.State;

namespace UAlbion.Game.Combat.Spells;

/// <summary>
/// The universal spell success gate, RE'd from MAIN.EXE fcn.000601a6 (used by every
/// "chance" spell): the target must be a party member or match the spell's creature-class
/// masks (exclusion beats inclusion; "unaffected creature type", effect 774), and the
/// spell lands iff mastery% &gt; the target's Magic Resistance. DETERMINISTIC — no random
/// roll. The gate RETURNS THE MARGIN (M − resist): gated damage spells scale on the
/// margin, not raw M (RE batch 4 — verified for the frost line, SmallFireball and
/// Fungification). Party targets additionally get their resistance boosted by the
/// MagicShield/PersonalProtection active-spell type-2 percentage (resist·pct/100).
/// </summary>
public static class SpellSuccessGate
{
    /// <summary>Demon class bits of the creature-class bitmask (sheet+0x0E) — mask 0x44.</summary>
    public const int DemonClassMask = 0x44;

    /// <summary>Class bit 0x80 (also the crit-immunity bit) — KamulosGaze's exclusion mask.</summary>
    public const int GazeImmuneMask = 0x80;

    /// <summary>
    /// The gate margin: &gt; 0 = the spell lands (and gated damage scales on it);
    /// &lt;= 0 = resisted / unaffected creature type.
    /// </summary>
    public static int Margin(SpellCastContext context, ICombatParticipant target, int inclMask = 0xFFFF, int exclMask = 0)
    {
        if (context == null || target?.Effective == null)
            return 0;

        if (target.SheetId.Type != AssetType.PartySheet)
        {
            int classBits = target.Effective.UnknownE;
            if (exclMask != 0 && (classBits & exclMask) != 0)
                return 0; // excluded creature type (exclusion beats inclusion)
            if (inclMask != 0xFFFF && (classBits & inclMask) == 0)
                return 0; // unaffected creature type
        }

        int resist = target.Effective.Attributes?.MagicResistance?.Current ?? 0;

        // Party targets get the MagicShield/PersonalProtection type-2 boost:
        // resist += resist·pct/100 (table 0x153b3e — hour-duration entries, RE 5B).
        if (target.SheetId.Type == AssetType.PartySheet && context.GetActiveSpellPct != null)
        {
            int shieldPct = context.GetActiveSpellPct(target, 2);
            resist += resist * shieldPct / 100;
        }

        return context.MasteryMultiplier - resist;
    }

    public static bool Lands(SpellCastContext context, ICombatParticipant target, int classMask = 0xFFFF)
        => Margin(context, target, classMask) > 0;
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
        // RE 5B (fcn.0005f7ec): kills = max(1, living·M/100); selection is uniform
        // WITHOUT replacement via rejection re-roll on an ordinal bitmask (matches the
        // original's RNG stream); victims are then applied in GRID ORDER (row-major),
        // each still individually gated — a high-resist pick is wasted, not re-rolled.
        var living = context?.GetLiveEnemies?.Invoke(); // grid order (Battle enumerates tiles row-major)
        if (living == null || living.Count == 0 || context.InstantKill == null)
            return SpellCastOutcome.Failed;

        int kills = Math.Max(1, living.Count * context.MasteryMultiplier / 100);
        kills = Math.Min(kills, living.Count);

        ulong picked = 0;
        for (int i = 0; i < kills; i++)
        {
            int r;
            do { r = context.Random?.Invoke(living.Count) ?? i; }
            while ((picked & (1UL << r)) != 0);
            picked |= 1UL << r;
        }

        bool anyKilled = false;
        for (int ord = 0; ord < living.Count; ord++)
        {
            if ((picked & (1UL << ord)) == 0)
                continue;
            var victim = living[ord];
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
/// Kamulos' Gaze (RE batch 4, handler 0xa8b12 → 0xa8b46): NOT a damage spell — a
/// deterministic instant kill (fcn.0004e247) gated on M &gt; MagicResist, with creature
/// class bit 0x80 (the crit-immunity bit) granting immunity (exclusion mask).
/// </summary>
public sealed class KamulosGazeEffect : ISpellEffect
{
    public SpellId SpellId { get; }
    public KamulosGazeEffect(SpellId spellId) => SpellId = spellId;

    public SpellCastOutcome Apply(SpellCastContext context)
    {
        var target = context?.Target;
        if (target == null || context.InstantKill == null)
            return SpellCastOutcome.Failed;

        if (SpellSuccessGate.Margin(context, target, exclMask: SpellSuccessGate.GazeImmuneMask) <= 0)
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
