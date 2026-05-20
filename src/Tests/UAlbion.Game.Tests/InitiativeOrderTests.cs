using System.Collections.Generic;
using System.Linq;
using UAlbion.Game.Combat;
using Xunit;

namespace UAlbion.Game.Tests;

public class InitiativeOrderTests
{
    sealed record P(string Name, int Speed);

    static int Speed(P p) => p.Speed;

    [Fact]
    public void Sorts_Descending_By_Speed()
    {
        var party = new List<P> { new("alice", 10), new("bob", 50) };
        var mobs  = new List<P> { new("orc", 30), new("rat", 5) };

        var ordered = InitiativeOrder.Order(party, mobs, Speed).ToList();

        Assert.Equal(new[] { "bob", "orc", "alice", "rat" }, ordered.Select(x => x.Attacker.Name));
    }

    [Fact]
    public void Stable_Tie_Break_Puts_Party_Before_Mobs_On_Equal_Speed()
    {
        var party = new List<P> { new("partyA", 20) };
        var mobs  = new List<P> { new("mobA",   20) };

        var ordered = InitiativeOrder.Order(party, mobs, Speed).ToList();

        Assert.Equal("partyA", ordered[0].Attacker.Name);
        Assert.True(ordered[0].IsParty);
        Assert.Equal("mobA", ordered[1].Attacker.Name);
        Assert.False(ordered[1].IsParty);
    }

    [Fact]
    public void Marks_Side_Correctly()
    {
        var party = new List<P> { new("p", 1) };
        var mobs  = new List<P> { new("m", 1) };

        var ordered = InitiativeOrder.Order(party, mobs, Speed).ToList();

        Assert.Contains(ordered, x => x.Attacker.Name == "p" && x.IsParty);
        Assert.Contains(ordered, x => x.Attacker.Name == "m" && !x.IsParty);
    }

    [Fact]
    public void Empty_Inputs_Return_Empty_Sequence()
    {
        var ordered = InitiativeOrder.Order(new List<P>(), new List<P>(), Speed).ToList();
        Assert.Empty(ordered);
    }
}
