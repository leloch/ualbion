using System.Collections.Generic;
using UAlbion.Formats.Assets.Save;
using UAlbion.Game;
using UAlbion.Game.Entities.Map2D;
using Xunit;

namespace UAlbion.Game.Tests;

public class Map3DTests
{
    sealed class ScriptedCollisionManager : ICollisionManager
    {
        public HashSet<(int X, int Y)> Blocked { get; } = [];
        public bool IsOccupied(int fromX, int fromY, int toX, int toY) => Blocked.Contains((toX, toY));
        public void Register(IMovementCollider collider) { }
        public void Unregister(IMovementCollider collider) { }
    }

    [Fact]
    public void CollisionManager_Reports_Blocked_Tile()
    {
        var mgr = new ScriptedCollisionManager();
        mgr.Blocked.Add((3, 4));
        Assert.True(mgr.IsOccupied(2, 4, 3, 4));
        Assert.False(mgr.IsOccupied(2, 4, 2, 5));
    }

    [Fact]
    public void CollisionManager_Multiple_Blocks()
    {
        var mgr = new ScriptedCollisionManager();
        mgr.Blocked.Add((3, 4));
        mgr.Blocked.Add((5, 0));
        Assert.True(mgr.IsOccupied(0, 0, 3, 4));
        Assert.True(mgr.IsOccupied(0, 0, 5, 0));
        Assert.False(mgr.IsOccupied(0, 0, 1, 1));
    }

    [Fact]
    public void Combat_Position_Layout_Constants()
    {
        // Sanity-anchor of the position-grid convention: mobs occupy the top rows,
        // party the bottom rows. Battle.IsParty + GameState.GetCombatPositionForPlayer
        // both depend on this layout.
        Assert.Equal(5, SavedGame.CombatRows);
        Assert.Equal(2, SavedGame.CombatRowsForParty);
        Assert.Equal(3, SavedGame.CombatRowsForMobs);
        Assert.Equal(6, SavedGame.CombatColumns);

        // Mob band: positions 0..17 (3 rows × 6 cols)
        Assert.True(0 / SavedGame.CombatColumns < SavedGame.CombatRowsForMobs);
        Assert.True(17 / SavedGame.CombatColumns < SavedGame.CombatRowsForMobs);
        // Party band: positions 18..29 (2 rows × 6 cols)
        Assert.False(18 / SavedGame.CombatColumns < SavedGame.CombatRowsForMobs);
        Assert.False(29 / SavedGame.CombatColumns < SavedGame.CombatRowsForMobs);
    }
}
