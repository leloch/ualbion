using System;
using UAlbion.Formats.Assets.Sheets;

namespace UAlbion.Game.Combat;

/// <summary>
/// Combat damage / hit-chance helpers. First-pass, deterministic formulas suitable for
/// auto-resolution; should be refined once original Albion's formulas are reverse-engineered.
/// </summary>
public static class DamageCalculator
{
    public static int TotalAttack(ICombatAttributes combat)
    {
        ArgumentNullException.ThrowIfNull(combat);
        return combat.BaseAttack + Math.Max(0, (int)combat.BonusAttack);
    }

    /// <summary>
    /// Total attack including the Strength/25 component the original engine adds to melee
    /// damage. Reverse-engineered from MAIN.EXE `fcn.0004ee3b` at +0x29: <c>damage += STR/25</c>.
    /// Class modifiers (mostly &gt; 100 %) are applied separately by Battle when known.
    /// </summary>
    public static int TotalAttackWithStrength(ICombatAttributes combat, int strength)
    {
        ArgumentNullException.ThrowIfNull(combat);
        int strBonus = Math.Max(0, strength) / 25;
        return combat.BaseAttack + Math.Max(0, (int)combat.BonusAttack) + strBonus;
    }

    public static int TotalDefense(ICombatAttributes combat)
    {
        ArgumentNullException.ThrowIfNull(combat);
        return combat.BaseDefense + Math.Max(0, (int)combat.BonusDefense);
    }

    /// <summary>
    /// Reverse-engineered damage formula from MAIN.EXE `fcn.0004ee3b`:
    /// <c>finalDmg = max(0, RandomVary(rawAtk) - RandomVary(rawDef))</c>.
    /// This function returns the *raw* damage (before variance) — the caller in
    /// <c>Battle.ApplyMeleeAttack</c> applies <see cref="VaryDamage"/> to both sides
    /// independently and subtracts. Original game floors negative damage at zero (not 1).
    /// </summary>
    public static int ComputeMeleeDamage(ICombatAttributes attacker, ICombatAttributes defender)
    {
        var atk = TotalAttack(attacker);
        if (atk <= 0)
            return 0;

        // Subtract full defense (the original engine subtracts varied_def from varied_atk;
        // we apply variance separately so use the raw delta here). Zero-floor matches the
        // original — Albion combat does not have a guaranteed-1-damage rule despite older
        // RPG conventions.
        var def = TotalDefense(defender);
        var raw = atk - def;
        return Math.Max(0, raw);
    }

    public static float HitChance(ICombatAttributes attacker, ICombatAttributes defender)
    {
        var atk = TotalAttack(attacker);
        var def = TotalDefense(defender);
        var total = atk + def;
        if (total <= 0)
            return 0.5f;

        var ratio = (float)atk / total;
        return Math.Clamp(ratio, 0.05f, 0.95f);
    }

    public static int ComputeMagicDamage(CombatAttributes attacker, CombatAttributes defender, int spellPower)
    {
        ArgumentNullException.ThrowIfNull(attacker);
        ArgumentNullException.ThrowIfNull(defender);
        if (spellPower <= 0) spellPower = 1;
        var raw = (int)attacker.MagicAttack + spellPower - (int)defender.MagicDefense / 2;
        return Math.Max(1, raw);
    }

    /// <summary>
    /// Apply the original engine's <c>RandomVary</c> — 50..100 % of input, uniform.
    /// **Confirmed against MAIN.EXE `fcn.00035c22`** (called from the combat damage
    /// resolver at `fcn.0004ee3b`): <c>(rand() % 51 + 50) * value / 100</c>. Caller
    /// supplies the raw RNG value 0..50 (the result of <c>rand() % 51</c>); we add 50
    /// internally so the multiplier lands in [50, 100].
    /// </summary>
    /// <remarks>
    /// The original engine applies this variance independently to **both** attacker
    /// damage *and* defender protection — see `Battle.ApplyMeleeAttack` for the two-roll
    /// pattern. The wide 50 % floor explains why Albion combat feels swingy: a weak
    /// attacker can sometimes still land for full damage if the defender's protection
    /// rolls low on the same swing.
    /// </remarks>
    public static int VaryDamage(int baseDamage, int randomValue)
    {
        if (baseDamage <= 0) return 0;
        int variance = ((randomValue % 51) + 51) % 51;      // [0, 50]
        int adjusted = baseDamage * (50 + variance) / 100;  // multiplier in [50, 100]
        return Math.Max(1, adjusted);
    }

    /// <summary>
    /// Roll a hit using the supplied 0..99 RNG value. Returns true if the attack lands.
    /// </summary>
    public static bool RollHit(float hitChance, int randomValue0To99)
    {
        if (hitChance <= 0f) return false;
        if (hitChance >= 1f) return true;
        int normalized = ((randomValue0To99 % 100) + 100) % 100;
        return normalized < (int)(hitChance * 100);
    }

    /// <summary>
    /// Critical-hit roll. PLACEHOLDER: flat 5 % chance regardless of attacker/defender
    /// stats. The original engine likely scales with Luck or weapon-skill — search the
    /// `prtlogic.c` / `combat.c` randomness sites once the hit-roll site itself is fully
    /// RE'd. A crit doubles the rolled damage.
    /// </summary>
    public const int CritChancePercent = 5;
    public const int CritDamageMultiplier = 2;

    public static bool RollCrit(int randomValue0To99)
    {
        int normalized = ((randomValue0To99 % 100) + 100) % 100;
        return normalized < CritChancePercent;
    }

    /// <summary>
    /// Defender parry / evasion roll — runs *after* the attacker's hit-roll lands but
    /// *before* damage is applied. Returns true if the attack is parried (no damage).
    /// PLACEHOLDER: flat 8 % chance. Original likely scales with defender's Dexterity
    /// or shield. Implemented separately from <see cref="HitChance"/> so the two rolls
    /// stack rather than collapsing into one mega-roll, matching the original's two-stage
    /// "hit then parry" sequence visible in `fcn.00052b71`'s second weapon-slot read.
    /// </summary>
    public const int ParryChancePercent = 8;

    public static bool RollParry(int randomValue0To99)
    {
        int normalized = ((randomValue0To99 % 100) + 100) % 100;
        return normalized < ParryChancePercent;
    }
}
