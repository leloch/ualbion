using UAlbion.Game.State;
using Xunit;

namespace UAlbion.Game.Tests;

public class LevelUpCurveTests
{
    [Theory]
    [InlineData(0, 100)]   // L0 → L1 needs 100 XP
    [InlineData(1, 400)]   // L1 → L2 needs 400
    [InlineData(2, 900)]
    [InlineData(9, 10_000)]
    public void XpForNextLevel_Is_Next_Level_Squared_Times_Hundred(int currentLevel, int expected)
        => Assert.Equal(expected, SheetApplier.XpForNextLevel(currentLevel));

    [Fact]
    public void XpForNextLevel_Is_Strictly_Increasing()
    {
        int previous = -1;
        for (int level = 0; level < 30; level++)
        {
            int threshold = SheetApplier.XpForNextLevel(level);
            Assert.True(threshold > previous, $"Threshold at level {level} ({threshold}) not > previous ({previous})");
            previous = threshold;
        }
    }
}
