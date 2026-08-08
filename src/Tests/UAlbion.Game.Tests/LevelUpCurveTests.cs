using UAlbion.Formats.Assets.Sheets;
using UAlbion.Game.State;
using Xunit;

namespace UAlbion.Game.Tests;

public class LevelUpCurveTests
{
    // RE'd curve: XP to reach N+1 = max(1, floor(1.25*N^2) + N - 14) * classMul.
    // Pilot classMul = 25, Technician = 40 (see docs/re/RE_COMBAT.md "Placeholder formulas").
    [Theory]
    [InlineData(0, PlayerClass.Pilot, 25)]        // max(1, 0+0-14)=1 → 25
    [InlineData(1, PlayerClass.Pilot, 25)]        // max(1, 1+1-14)=1 → 25
    [InlineData(4, PlayerClass.Pilot, 250)]       // floor(20)+4-14=10 → 250
    [InlineData(10, PlayerClass.Pilot, 3025)]     // 125+10-14=121 → 3025
    [InlineData(10, PlayerClass.Technician, 4840)] // 121 × 40
    public void XpForNextLevel_Matches_The_REd_Curve(int currentLevel, PlayerClass playerClass, int expected)
        => Assert.Equal(expected, SheetApplier.XpForNextLevel(currentLevel, playerClass));

    [Fact]
    public void XpForNextLevel_Is_Non_Decreasing()
    {
        int previous = 0;
        for (int level = 0; level < 50; level++)
        {
            int threshold = SheetApplier.XpForNextLevel(level, PlayerClass.Pilot);
            Assert.True(threshold >= previous, $"Threshold at level {level} ({threshold}) < previous ({previous})");
            previous = threshold;
        }
    }
}
