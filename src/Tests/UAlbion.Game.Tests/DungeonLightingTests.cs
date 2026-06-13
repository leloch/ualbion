using UAlbion.Game.Magic;
using Xunit;

namespace UAlbion.Game.Tests;

/// <summary>
/// Dungeon light formula (RE 5C, fcn.0001473c / fcn.00038f24): light mode = mapFlags &amp; 3;
/// mode 1 = dungeon (min(100, max(spell, items)) +25 Iskai), modes 0/2 = full daylight.
/// Query 0x21 ("is it light enough") trips below 25.
/// </summary>
public class DungeonLightingTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    public void NonDungeonModes_AlwaysFullDaylight(int mode)
        => Assert.Equal(100, DungeonLighting.EffectiveLight(mode, spellPct: 0, lightItemTotal: 0, isIskaiLeader: false));

    [Fact]
    public void Dungeon_TakesMaxOfSpellAndItems()
    {
        Assert.Equal(40, DungeonLighting.EffectiveLight(1, spellPct: 40, lightItemTotal: 10, isIskaiLeader: false));
        Assert.Equal(60, DungeonLighting.EffectiveLight(1, spellPct: 20, lightItemTotal: 60, isIskaiLeader: false));
        Assert.Equal(0, DungeonLighting.EffectiveLight(1, spellPct: 0, lightItemTotal: 0, isIskaiLeader: false));
    }

    [Fact]
    public void Dungeon_IskaiAddsTwentyFive_ClampedAt100()
    {
        Assert.Equal(25, DungeonLighting.EffectiveLight(1, 0, 0, isIskaiLeader: true));   // pitch dark + bonus
        Assert.Equal(65, DungeonLighting.EffectiveLight(1, 40, 0, isIskaiLeader: true));  // 40 + 25
        Assert.Equal(100, DungeonLighting.EffectiveLight(1, 90, 0, isIskaiLeader: true)); // 90 + 25 clamped
    }

    [Fact]
    public void Dungeon_ClampsTotalAt100_BeforeBonus()
        => Assert.Equal(100, DungeonLighting.EffectiveLight(1, spellPct: 250, lightItemTotal: 0, isIskaiLeader: false));

    [Theory]
    [InlineData(1, 24, false, false)] // dungeon, 24% < 25 → too dark
    [InlineData(1, 25, false, true)]  // boundary
    [InlineData(1, 0,  true,  true)]  // Iskai's +25 lifts pitch dark to exactly 25
    [InlineData(0, 0,  false, true)]  // outdoors is always light enough
    public void IsLightEnough_TripsBelowTwentyFive(int mode, int spellPct, bool iskai, bool expected)
        => Assert.Equal(expected, DungeonLighting.IsLightEnough(mode, spellPct, lightItemTotal: 0, isIskaiLeader: iskai));
}
