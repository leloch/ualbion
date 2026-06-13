using UAlbion.Game.Combat;
using Xunit;

namespace UAlbion.Game.Tests;

/// <summary>
/// Pure-formula regression guards for the RE'd morale flight (fcn.00051506 + 0x13e1f0)
/// and lock-picking (door.c 0x5ab33) — previously untested private logic.
/// </summary>
public class CombatFormulasTests
{
    // --- Morale ---

    [Fact]
    public void ClassBit80_NeverFlees()
    {
        // Even at 0 LP and a wiped side, a 0x80 (frost/death-immune) creature holds.
        Assert.False(CombatFormulas.MoraleBroken(
            classBits: 0x80, strategy: 1, livingMonsters: 1, totalMonsters: 5,
            livingParty: 4, lp: 1, maxLp: 100, row: 0, morale: 0));
    }

    [Theory]
    // base formula: flee when (deadMonsterPct + lostLpPct)/2 >= Morale.
    // 5 of 5 alive, full LP → threat 0; morale 50 → fight.
    [InlineData(5, 5, 100, 100, 50, false)]
    // 1 of 5 alive (80% dead), 50% LP lost → threat (80+50)/2=65 >= 50 → flee.
    [InlineData(1, 5, 50, 100, 50, true)]
    // morale 100 → practically never flees even badly hurt.
    [InlineData(1, 5, 1, 100, 100, false)]
    // morale 0 → flees immediately (threat 0 >= 0).
    [InlineData(5, 5, 100, 100, 0, true)]
    public void BaseMoraleFormula(int living, int total, int lp, int maxLp, int morale, bool expectFlee)
        => Assert.Equal(expectFlee, CombatFormulas.MoraleBroken(
            classBits: 0, strategy: 1, livingMonsters: living, totalMonsters: total,
            livingParty: 4, lp: lp, maxLp: maxLp, row: 0, morale: morale));

    [Fact]
    public void Strategy2_FleesOnceAnyMonsterDies()
    {
        Assert.False(CombatFormulas.MoraleBroken(0, 2, 5, 5, 4, 100, 100, 0, 99)); // none dead → fight
        Assert.True(CombatFormulas.MoraleBroken(0, 2, 4, 5, 4, 100, 100, 0, 99));  // one dead → flee
    }

    [Fact]
    public void Strategy7_OutnumberedOrLowLp_AndDeeperRowsGiveUpSooner()
    {
        // monsters >= party AND LP% >= (row+1)*25 → fight.
        Assert.False(CombatFormulas.MoraleBroken(0, 7, 4, 5, 4, 100, 100, row: 0, morale: 99)); // 100% >= 25 → fight
        Assert.True(CombatFormulas.MoraleBroken(0, 7, 4, 5, 4, 20, 100, row: 0, morale: 99));   // 20% < 25 → flee
        Assert.True(CombatFormulas.MoraleBroken(0, 7, 2, 5, 4, 100, 100, row: 0, morale: 99));  // outnumbered → flee
        // row 3 needs LP% >= 100 to keep fighting.
        Assert.True(CombatFormulas.MoraleBroken(0, 7, 4, 5, 4, 90, 100, row: 3, morale: 99));   // 90% < 100 → flee
    }

    // --- Lockpicking ---

    [Fact]
    public void Lockpick_Difficulty100_Unpickable()
    {
        Assert.True(CombatFormulas.LockpickUnpickable(100));
        Assert.False(CombatFormulas.LockpickUnpickable(99));
    }

    [Fact]
    public void Lockpick_SkillMeetsDifficulty_AutoSucceeds()
    {
        Assert.True(CombatFormulas.LockpickAutoSuccess(60, 60));
        Assert.True(CombatFormulas.LockpickAutoSuccess(80, 60));
        Assert.False(CombatFormulas.LockpickAutoSuccess(40, 60));
        Assert.False(CombatFormulas.LockpickAutoSuccess(0, 0)); // no skill never picks
    }

    [Theory]
    [InlineData(50, 0, 50)]   // wide-open lock, skill 50 → 50%
    [InlineData(50, 50, 25)]  // skill 50, diff 50 → 50*50/100 = 25%
    [InlineData(40, 60, 16)]  // 40*40/100 = 16%
    [InlineData(0, 50, 0)]    // no skill → 0
    [InlineData(50, 100, 0)]  // unpickable → 0
    public void Lockpick_ChancePercent(int skill, int difficulty, int expected)
        => Assert.Equal(expected, CombatFormulas.LockpickChancePercent(skill, difficulty));

    // --- Action points per level (RE ApplyLevelUp: clamp(level/LevelsPerActionPoint,1,4)) ---

    [Theory]
    [InlineData(1, 5, 1)]    // level 1, 5 levels/AP → 1 (floored, min 1)
    [InlineData(4, 5, 1)]    // still below the first threshold
    [InlineData(5, 5, 1)]    // 5/5 = 1
    [InlineData(10, 5, 2)]   // 10/5 = 2
    [InlineData(20, 5, 4)]   // 20/5 = 4
    [InlineData(50, 5, 4)]   // 50/5 = 10, clamped to 4
    [InlineData(50, 10, 4)]  // 50/10 = 5, clamped to 4
    [InlineData(30, 10, 3)]  // 30/10 = 3
    public void ActionPoints_ClampedLevelOverDivisor(int level, int divisor, int expected)
        => Assert.Equal(expected, CombatFormulas.ActionPointsForLevel(level, divisor));

    [Fact]
    public void ActionPoints_ZeroDivisor_DefaultsToOne()
        => Assert.Equal(1, CombatFormulas.ActionPointsForLevel(40, 0));
}
