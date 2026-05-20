using UAlbion.Formats.Assets.Sheets;
using UAlbion.Game.Combat;
using Xunit;

namespace UAlbion.Game.Tests;

public class MonsterAiTests
{
    [Fact]
    public void Asleep_Skips_Turn()
        => Assert.Equal(MonsterAi.StatusOutcome.SkipTurn, MonsterAi.ResolveStatusBehavior(PlayerConditions.Asleep));

    [Fact]
    public void Panicking_Flees()
        => Assert.Equal(MonsterAi.StatusOutcome.Flee, MonsterAi.ResolveStatusBehavior(PlayerConditions.Panicking));

    [Fact]
    public void Insane_Picks_Random()
        => Assert.Equal(MonsterAi.StatusOutcome.InsaneRandomAct, MonsterAi.ResolveStatusBehavior(PlayerConditions.Insane));

    [Fact]
    public void Asleep_Takes_Priority_Over_Insane_And_Panicking()
    {
        var conds = PlayerConditions.Asleep | PlayerConditions.Insane | PlayerConditions.Panicking;
        Assert.Equal(MonsterAi.StatusOutcome.SkipTurn, MonsterAi.ResolveStatusBehavior(conds));
    }

    [Fact]
    public void Panicking_Takes_Priority_Over_Insane()
    {
        var conds = PlayerConditions.Panicking | PlayerConditions.Insane;
        Assert.Equal(MonsterAi.StatusOutcome.Flee, MonsterAi.ResolveStatusBehavior(conds));
    }

    [Fact]
    public void None_Returns_Normal()
        => Assert.Equal(MonsterAi.StatusOutcome.Normal, MonsterAi.ResolveStatusBehavior(PlayerConditions.None));

    [Fact]
    public void Choose_Action_A_First_50_50()
    {
        // Pattern: (rand & 7) < 4 → 4/8 = 50%
        Assert.True(MonsterAi.ChooseActionAFirst(0));      // 0 & 7 = 0 < 4
        Assert.True(MonsterAi.ChooseActionAFirst(3));      // 3 & 7 = 3 < 4
        Assert.False(MonsterAi.ChooseActionAFirst(4));     // 4 & 7 = 4 ≥ 4
        Assert.False(MonsterAi.ChooseActionAFirst(7));     // 7 & 7 = 7 ≥ 4
        Assert.True(MonsterAi.ChooseActionAFirst(8));      // 8 & 7 = 0 < 4
    }

    [Fact]
    public void Randomise_Attribute_Matches_Original_Formula()
    {
        // Original: (rand() % 11 + 95) * value / 100
        // For rand=0:  (0+95) * 100/100 = 95
        // For rand=10: (10+95) * 100/100 = 105
        // For rand=5:  (5+95) * 100/100 = 100 (identity)
        Assert.Equal(95, MonsterAi.RandomiseAttribute(100, 0));
        Assert.Equal(105, MonsterAi.RandomiseAttribute(100, 10));
        Assert.Equal(100, MonsterAi.RandomiseAttribute(100, 5));
        // For rand=22 (% 11 = 0): same as rand=0
        Assert.Equal(95, MonsterAi.RandomiseAttribute(100, 22));
    }

    [Fact]
    public void Randomise_Attribute_Zero_Stays_Zero()
        => Assert.Equal(0, MonsterAi.RandomiseAttribute(0, 5));

    [Fact]
    public void Pick_Random_Set_Bit_Empty_Returns_Negative_One()
        => Assert.Equal(-1, MonsterAi.PickRandomSetBit(0, 42));

    [Fact]
    public void Pick_Random_Set_Bit_Single_Returns_That_Bit()
    {
        Assert.Equal(7, MonsterAi.PickRandomSetBit(1u << 7, 0));
        Assert.Equal(7, MonsterAi.PickRandomSetBit(1u << 7, 999));
    }

    [Fact]
    public void Pick_Random_Set_Bit_Cycles_Through_Set_Bits()
    {
        // bits 0, 5, 10 → 3 set bits
        uint mask = (1u << 0) | (1u << 5) | (1u << 10);
        Assert.Equal(0, MonsterAi.PickRandomSetBit(mask, 0));   // 0 % 3 = 0 → first set bit
        Assert.Equal(5, MonsterAi.PickRandomSetBit(mask, 1));   // 1 % 3 = 1 → second
        Assert.Equal(10, MonsterAi.PickRandomSetBit(mask, 2));  // 2 % 3 = 2 → third
        Assert.Equal(0, MonsterAi.PickRandomSetBit(mask, 3));   // wraps
    }

    [Fact]
    public void Get_Enemy_Mask_Filters_Self_And_Allies()
    {
        // 30-slot grid. selfTeam=0 (party), positions:
        //   0 = self
        //   1 = ally (team 0)
        //   2 = enemy (team 1)
        //   3 = empty (-1)
        //   29 = enemy
        // expect bits 2 and 29 set.
        var teams = new int[30];
        for (int i = 0; i < 30; i++) teams[i] = -1;
        teams[0] = 0;   // self
        teams[1] = 0;   // ally
        teams[2] = 1;   // enemy
        teams[29] = 1;  // enemy

        uint mask = MonsterAi.GetEnemyMask(0, 0, teams);
        Assert.Equal((1u << 2) | (1u << 29), mask);
    }

    [Fact]
    public void Get_Enemy_Mask_Empty_Grid_Returns_Zero()
    {
        var teams = new int[30];
        for (int i = 0; i < 30; i++) teams[i] = -1;
        Assert.Equal(0u, MonsterAi.GetEnemyMask(0, 0, teams));
    }

    [Fact]
    public void Ai_Weight_Table_Has_Sixteen_Entries()
        => Assert.Equal(16, MonsterAi.AiWeightTable.Length);

    [Fact]
    public void Ai_Weight_Table_Bit_Distribution_Matches_Original()
    {
        // 6 × bit-0x01 (Summon), 4 × bit-0x02, 6 × bit-0x04 → matches 0x4facb in MAIN.EXE
        int b1 = 0, b2 = 0, b4 = 0;
        foreach (var e in MonsterAi.AiWeightTable)
        {
            if (e == 0x01) b1++;
            if (e == 0x02) b2++;
            if (e == 0x04) b4++;
        }
        Assert.Equal(6, b1);
        Assert.Equal(4, b2);
        Assert.Equal(6, b4);
    }

    [Fact]
    public void Choose_Normal_Action_Returns_None_If_No_Actions_Available()
    {
        var result = MonsterAi.ChooseNormalAction(MonsterAi.AvailableActions.None, () => 0, _ => true);
        Assert.Equal(MonsterAi.AvailableActions.None, result);
    }

    [Fact]
    public void Choose_Normal_Action_Picks_Available_Bit()
    {
        // rand=0 → idx 0 → bit Summon. Mob has Summon available. Should commit.
        var result = MonsterAi.ChooseNormalAction(
            MonsterAi.AvailableActions.Summon,
            () => 0,
            bit =>
            {
                Assert.Equal(MonsterAi.AvailableActions.Summon, bit);
                return true;
            });
        Assert.Equal(MonsterAi.AvailableActions.Summon, result);
    }

    [Fact]
    public void Choose_Normal_Action_Re_Rolls_If_Picked_Bit_Not_Available()
    {
        // rng yields indices 0,0,0... → bit Summon repeatedly. Mob only has Action4. Loop bails out.
        int calls = 0;
        var result = MonsterAi.ChooseNormalAction(
            MonsterAi.AvailableActions.Action4,
            () => { calls++; return 0; },     // always idx 0 → bit Summon (unavailable)
            _ => true);
        // The loop should keep re-rolling because Summon is never available. Since we never
        // remove Action4 from the pool, the loop runs forever in theory — but ChooseNormalAction
        // is supposed to terminate. Verify by giving a counted RNG.
        Assert.True(calls > 0);
        // Without a way to commit Action4, we'll never call tryCommit, and Action4 stays in pool.
        // We need to give an rng that eventually rolls onto an Action4 index.
        // Adjusted test: rng cycle 0,8,8,... → first 0 (Summon, not avail, re-roll), then 8 (Action4, avail, commit).
        var sequence = new System.Collections.Generic.Queue<int>(new[] { 0, 8 });
        var result2 = MonsterAi.ChooseNormalAction(
            MonsterAi.AvailableActions.Action4,
            () => sequence.Dequeue(),
            bit => bit == MonsterAi.AvailableActions.Action4);
        Assert.Equal(MonsterAi.AvailableActions.Action4, result2);
    }

    [Fact]
    public void Choose_Normal_Action_Returns_None_If_Commit_Always_Fails()
    {
        // Mob has Summon available, rng always picks index 0 = Summon. But tryCommit always
        // returns false → the bit gets cleared on each attempt → eventually pool is empty.
        var sequence = new System.Collections.Generic.Queue<int>(new[] { 0, 0, 0, 0, 0, 0, 0, 0 });
        var result = MonsterAi.ChooseNormalAction(
            MonsterAi.AvailableActions.Summon,
            () => sequence.Count > 0 ? sequence.Dequeue() : 0,
            _ => false);
        Assert.Equal(MonsterAi.AvailableActions.None, result);
    }
}
