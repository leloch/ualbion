using UAlbion.Formats.Assets.Sheets;
using UAlbion.Game.Combat;
using Xunit;

namespace UAlbion.Game.Tests;

public class DamageCalculatorTests
{
    static CombatAttributes Stats(int baseAttack = 0, int bonusAttack = 0, int baseDefense = 0, int bonusDefense = 0, int magicAttack = 0, int magicDefense = 0)
        => new()
        {
            BaseAttack    = (ushort)baseAttack,
            BonusAttack   = (short)bonusAttack,
            BaseDefense   = (ushort)baseDefense,
            BonusDefense  = (short)bonusDefense,
            MagicAttack   = (ushort)magicAttack,
            MagicDefense  = (ushort)magicDefense,
        };

    [Fact]
    public void TotalAttack_Sums_Base_And_Bonus()
        => Assert.Equal(30, DamageCalculator.TotalAttack(Stats(baseAttack: 20, bonusAttack: 10)));

    [Fact] // Regression for STAT-01: DeepClone/CopyFrom dropped the attack + magic fields,
    public void DeepClone_PreservesAttackAndMagicFields() // collapsing all combat damage to ~0.
    {
        var src = Stats(baseAttack: 21, bonusAttack: 7, baseDefense: 9, bonusDefense: 3, magicAttack: 13, magicDefense: 5);
        src.LifePoints = new CharacterAttribute { Current = 40, Max = 50 };
        var clone = src.DeepClone();
        Assert.Equal((ushort)21, clone.BaseAttack);
        Assert.Equal((short)7, clone.BonusAttack);
        Assert.Equal((ushort)13, clone.MagicAttack);
        Assert.Equal((ushort)5, clone.MagicDefense);
        Assert.Equal((ushort)9, clone.BaseDefense);
        Assert.Equal((short)3, clone.BonusDefense);
        Assert.Equal(28, DamageCalculator.TotalAttack(clone)); // 21 base + 7 bonus
    }

    [Fact]
    public void TotalAttack_Floors_Negative_Bonus_At_Zero()
        => Assert.Equal(20, DamageCalculator.TotalAttack(Stats(baseAttack: 20, bonusAttack: -50)));

    [Theory]
    // RE'd from MAIN.EXE fcn.0004ee3b: damage += STR/25 (integer division)
    [InlineData(20, 0,  20)]    // base 20, str 0  → +0
    [InlineData(20, 24, 20)]    // str 24/25 = 0   → +0 (just under threshold)
    [InlineData(20, 25, 21)]    // str 25/25 = 1   → +1
    [InlineData(20, 99, 23)]    // str 99/25 = 3   → +3
    [InlineData(20, 100, 24)]   // str 100/25 = 4  → +4
    public void TotalAttackWithStrength_Adds_Str_Over_25(int baseAttack, int strength, int expected)
        => Assert.Equal(expected, DamageCalculator.TotalAttackWithStrength(Stats(baseAttack: baseAttack), strength));

    [Fact]
    public void TotalAttackWithStrength_Floors_Negative_Strength_At_Zero()
        => Assert.Equal(20, DamageCalculator.TotalAttackWithStrength(Stats(baseAttack: 20), -100));

    [Fact]
    public void Melee_Damage_Subtracts_Full_Defense_Per_Original_Engine()
    {
        var atk = Stats(baseAttack: 30);
        var def = Stats(baseDefense: 20);
        Assert.Equal(10, DamageCalculator.ComputeMeleeDamage(atk, def));
    }

    [Fact]
    public void Melee_Damage_Returns_Zero_With_No_Attack()
        => Assert.Equal(0, DamageCalculator.ComputeMeleeDamage(Stats(), Stats(baseDefense: 50)));

    [Fact]
    public void Melee_Damage_Zero_Floors_When_Defense_Overcomes_Attack()
    {
        // Per RE'd formula in fcn.0004ee3b: original engine does NOT have a min-1 floor;
        // it returns max(0, atk - def). A heavily armoured target can fully shrug off light hits.
        var atk = Stats(baseAttack: 1);
        var def = Stats(baseDefense: 1000);
        Assert.Equal(0, DamageCalculator.ComputeMeleeDamage(atk, def));
    }

    [Fact]
    public void Magic_Damage_Accounts_For_Spell_Power()
    {
        var atk = Stats(magicAttack: 10);
        var def = Stats(magicDefense: 4); // half = 2, raw = 10 + 5 - 2 = 13
        Assert.Equal(13, DamageCalculator.ComputeMagicDamage(atk, def, spellPower: 5));
    }

    [Theory]
    // RE'd from MAIN.EXE fcn.00035c22: baseDmg * (50 + roll%51) / 100 — multiplier in [50, 100]
    [InlineData(100, 0,  50)]    // floor: roll=0 → 50 %
    [InlineData(100, 25, 75)]    // mid:   roll=25 → 75 %
    [InlineData(100, 50, 100)]   // ceil:  roll=50 → 100 %
    [InlineData(200, 10, 120)]
    public void Vary_Damage_Matches_Original_Fifty_To_Hundred_Percent_Band(int baseDmg, int roll, int expected)
        => Assert.Equal(expected, DamageCalculator.VaryDamage(baseDmg, roll));

    [Fact]
    public void Vary_Damage_Returns_Zero_For_Zero_Input()
        => Assert.Equal(0, DamageCalculator.VaryDamage(0, 5));

    [Fact]
    public void Vary_Damage_Never_Returns_Below_One_For_Positive_Input()
        => Assert.Equal(1, DamageCalculator.VaryDamage(1, 0));

    [Theory]
    // RE'd from MAIN.EXE fcn.00035b15 (PercentRoll): success iff roll <= value, with
    // auto-fail for value <= 0 (so the effective chance is (value+1)/range).
    [InlineData(50, 50, true)]   // boundary: <= not <
    [InlineData(50, 51, false)]
    [InlineData(50, 0,  true)]
    [InlineData(0,  0,  false)]  // auto-fail at zero skill
    [InlineData(-5, 0,  false)]
    [InlineData(100, 99, true)]  // skill >= 99 always hits a 0..99 roll
    public void Percent_Roll_Succeeds_When_Roll_At_Most_Value(int value, int roll, bool expected)
        => Assert.Equal(expected, DamageCalculator.PercentRoll(value, roll));

    [Theory]
    // RE'd from MAIN.EXE fcn.00035fd5 (GetEffectiveSkill): Blind halves the WEAPON
    // skills (CloseRange/LongRange) only — CriticalHit and Lockpicking are unaffected.
    [InlineData(60, true,  false, 60)]
    [InlineData(60, true,  true,  30)]
    [InlineData(61, true,  true,  30)]  // integer halving
    [InlineData(60, false, true,  60)]  // crit skill not halved by Blind
    [InlineData(-10, true, false, 0)]   // clamped at zero
    public void Effective_Skill_Halves_Weapon_Skills_When_Blind(int skill, bool isWeapon, bool blind, int expected)
        => Assert.Equal(expected, DamageCalculator.EffectiveSkill(skill, isWeapon, blind));
}
