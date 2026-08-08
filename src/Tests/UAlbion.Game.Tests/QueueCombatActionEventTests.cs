using UAlbion.Formats.Ids;
using UAlbion.Game.Combat;
using Xunit;

namespace UAlbion.Game.Tests;

public class QueueCombatActionEventTests
{
    [Fact]
    public void Event_Round_Trips_Actor_Action_And_Target()
    {
        var evt = new QueueCombatActionEvent(new SheetId(UAlbion.Config.AssetType.PartySheet, 5), CombatAction.Melee, 23);
        Assert.Equal(new SheetId(UAlbion.Config.AssetType.PartySheet, 5), evt.Actor);
        Assert.Equal(CombatAction.Melee, evt.Action);
        Assert.Equal(23, evt.TargetTile);
    }

    [Fact]
    public void Default_Target_Tile_Is_Minus_One_For_Untargeted_Actions()
    {
        var evt = new QueueCombatActionEvent(new SheetId(UAlbion.Config.AssetType.PartySheet, 1), CombatAction.Retreat, -1);
        Assert.Equal(-1, evt.TargetTile);
    }

    [Theory]
    [InlineData(CombatAction.None)]
    [InlineData(CombatAction.Melee)]
    [InlineData(CombatAction.CastSchool5)]
    [InlineData(CombatAction.CastSchool6)]
    [InlineData(CombatAction.Retreat)]
    [InlineData(CombatAction.Summon)]
    [InlineData(CombatAction.UseItem)]
    [InlineData(CombatAction.TargetedTile)]
    public void All_CombatAction_Values_Accepted(CombatAction action)
    {
        var evt = new QueueCombatActionEvent(new SheetId(UAlbion.Config.AssetType.PartySheet, 7), action, -1);
        Assert.Equal(action, evt.Action);
    }

    [Fact]
    public void CombatAction_Values_Match_Original_Engine_Action_Kinds()
    {
        // Per docs/re/RE_COMBAT.md / MAIN.EXE: action_kind at combatant +0x4E uses these values.
        Assert.Equal(0, (ushort)CombatAction.None);
        Assert.Equal(1, (ushort)CombatAction.Melee);
        Assert.Equal(2, (ushort)CombatAction.CastSchool5);
        Assert.Equal(3, (ushort)CombatAction.CastSchool6);
        Assert.Equal(4, (ushort)CombatAction.Retreat);
        Assert.Equal(5, (ushort)CombatAction.Summon);
        Assert.Equal(6, (ushort)CombatAction.UseItem);
        Assert.Equal(7, (ushort)CombatAction.TargetedTile);
    }
}
