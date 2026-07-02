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
        // CMB-01: byte-exact RandomVary (fcn.00035c22) has NO lower floor — small values
        // legitimately truncate to 0 (e.g. 1 * 50/100). The final damage is floored at 0 by
        // the caller's max(0, variedAtk - variedDef); a Max(1) here diverged on both rolls.
        return baseDamage * (50 + variance) / 100;          // multiplier in [50, 100]
    }

    /// <summary>
    /// The original engine's PercentRoll — **confirmed against MAIN.EXE `fcn.00035b15`**:
    /// success iff <c>rand() % range &lt;= value</c> (note &lt;=, so the effective chance is
    /// (value+1)/range), with auto-fail when value &lt;= 0. Used for the melee/ranged to-hit
    /// roll (attacker's weapon skill vs 100, via RollSkill `fcn.00035bdc`), the critical-hit
    /// roll (CriticalHit skill vs 100) and equipment break rolls (item break-rate vs 1000).
    /// The caller supplies the raw roll (<c>rand() % range</c>).
    /// </summary>
    public static bool PercentRoll(int value, int roll)
    {
        if (value <= 0) return false;
        return roll <= value;
    }

    /// <summary>
    /// Effective skill value for combat rolls, RE'd from `fcn.00035fd5` (GetEffectiveSkill):
    /// the sheet skill (equipment bonuses already folded into Effective sheets) is HALVED
    /// when the combatant is Blind — but only for the weapon skills (CloseRange/LongRange),
    /// not CriticalHit or Lockpicking.
    /// </summary>
    public static int EffectiveSkill(int skillValue, bool isWeaponSkill, bool isBlind)
        => isWeaponSkill && isBlind ? Math.Max(0, skillValue) / 2 : Math.Max(0, skillValue);
}
