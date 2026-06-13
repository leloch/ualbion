using UAlbion.Formats.Assets;
using UAlbion.Game.Combat;
using Xunit;

namespace UAlbion.Game.Tests;

/// <summary>
/// Spell target-area decoding (RE 5B fcn.0005ef24/fcn.0005fb21). The Targets byte can carry
/// several bits; the cast loop resolves them by priority: AllMonsters &gt; RowOfMonsters &gt;
/// DeadParty(whole party) &gt; single. Self-managed spells (GoddessWrath) always classify as
/// single because they pick their own victims.
/// </summary>
public class SpellTargetingTests
{
    [Fact]
    public void AllMonsters_WinsOverEveryOtherBit()
    {
        var targets = SpellTargets.AllMonsters | SpellTargets.RowOfMonsters | SpellTargets.DeadParty;
        Assert.Equal(SpellArea.AllMonsters, SpellTargeting.Classify(targets, selfManaged: false));
    }

    [Fact]
    public void Row_WinsOverWholeParty()
    {
        var targets = SpellTargets.RowOfMonsters | SpellTargets.DeadParty;
        Assert.Equal(SpellArea.Row, SpellTargeting.Classify(targets, selfManaged: false));
    }

    [Fact]
    public void DeadPartyBit_IsWholeParty()
        => Assert.Equal(SpellArea.WholeParty, SpellTargeting.Classify(SpellTargets.DeadParty, false));

    [Theory]
    [InlineData(SpellTargets.OneMonster)]
    [InlineData(SpellTargets.Party)]
    [InlineData((SpellTargets)0)]
    public void NoAreaBits_AreSingleTarget(SpellTargets targets)
        => Assert.Equal(SpellArea.SingleTarget, SpellTargeting.Classify(targets, false));

    [Fact]
    public void SelfManaged_AlwaysSingle_EvenWithAreaBits()
        => Assert.Equal(SpellArea.SingleTarget,
            SpellTargeting.Classify(SpellTargets.AllMonsters, selfManaged: true));

    [Theory]
    [InlineData(SpellTargets.OneMonster, true)]
    [InlineData(SpellTargets.RowOfMonsters, true)]
    [InlineData(SpellTargets.AllMonsters, true)]
    [InlineData(SpellTargets.Party, false)]
    [InlineData(SpellTargets.DeadParty, false)]
    public void IsOffensive_TrueForMonsterTargetingBits(SpellTargets targets, bool expected)
        => Assert.Equal(expected, SpellTargeting.IsOffensive(targets));
}
