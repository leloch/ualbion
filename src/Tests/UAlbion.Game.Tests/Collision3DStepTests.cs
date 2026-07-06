using System.Collections.Generic;
using UAlbion.Game;
using UAlbion.Game.Entities.Map3D;
using Xunit;

namespace UAlbion.Game.Tests;

// The faithful one-step arbiter (fcn.0001e832 port, _RE_COLLISION3D.md): destination-tile
// passability, the 8-neighbour forbidden-zone margin, the already-hugging 9-case escape and
// the object overlap. Margin 0.25 tiles (512-unit maps).
public class Collision3DStepTests
{
    const float Margin = 0.25f;

    sealed class Detector : ICollisionManager
    {
        public HashSet<(int X, int Y)> BlockedTiles { get; } = [];
        public List<(float X, float Z, float Half)> Objects { get; } = [];

        public bool IsOccupied(int fromX, int fromY, int toX, int toY) => BlockedTiles.Contains((toX, toY));
        public bool IsTileBlocked(int tileX, int tileY, int collisionClass) => BlockedTiles.Contains((tileX, tileY));
        public bool HitsObjectAt(float posX, float posZ, int collisionClass)
        {
            foreach (var (x, z, half) in Objects)
                if (System.MathF.Abs(posX - x) < half && System.MathF.Abs(posZ - z) < half)
                    return true;
            return false;
        }
        public bool IsBlockedAt(float posX, float posZ, int collisionClass)
            => IsTileBlocked((int)posX, (int)posZ, collisionClass) || HitsObjectAt(posX, posZ, collisionClass);
        public void Register(IMovementCollider collider) { }
        public void Unregister(IMovementCollider collider) { }
    }

    static bool Step(Detector d, float px, float pz, float dx, float dz)
        => Collision3DStep.IsAllowed(d, px, pz, dx, dz, Margin, 0);

    [Fact]
    public void Open_Space_Moves_Freely()
    {
        var d = new Detector();
        Assert.True(Step(d, 5.5f, 5.5f, 0.1f, 0.1f));
    }

    [Fact]
    public void Blocked_Destination_Tile_Refused()
    {
        var d = new Detector();
        d.BlockedTiles.Add((6, 5));
        Assert.False(Step(d, 5.9f, 5.5f, 0.2f, 0f)); // endpoint lands in (6,5)
    }

    [Fact]
    public void Margin_Zone_Next_To_Wall_Refused_From_Outside()
    {
        var d = new Detector();
        d.BlockedTiles.Add((6, 5)); // wall to the +X side of tile (5,5)
        // From the centre band, moving into the forbidden high-X band (zone zx=2) of (5,5):
        Assert.False(Step(d, 5.5f, 5.5f, 0.3f, 0f)); // endpoint 5.8 → zone zx=2, forbidden
        // Stopping short of the margin band is fine:
        Assert.True(Step(d, 5.5f, 5.5f, 0.2f, 0f));  // endpoint 5.7 < 0.75 band edge
    }

    [Fact]
    public void Hugging_The_Wall_Allows_Parallel_And_Away_Moves_Only()
    {
        var d = new Detector();
        d.BlockedTiles.Add((6, 5)); // wall on +X
        // Already hugging: position inside the forbidden zx=2 band of (5,5).
        Assert.True(Step(d, 5.9f, 5.5f, 0f, 0.05f));   // parallel (along the wall) — allowed
        Assert.True(Step(d, 5.9f, 5.5f, -0.05f, 0f));  // away from the wall — allowed
        Assert.False(Step(d, 5.9f, 5.5f, 0.05f, 0f));  // further into the wall — refused
    }

    [Fact]
    public void Corner_Neighbour_Forbids_Only_The_Corner_Zone()
    {
        var d = new Detector();
        d.BlockedTiles.Add((6, 6)); // diagonal corner of (5,5)
        Assert.False(Step(d, 5.5f, 5.5f, 0.4f, 0.4f)); // into the corner zone (zx=2, zz=2)
        Assert.True(Step(d, 5.5f, 5.5f, 0.4f, 0f));    // high-X band alone is not forbidden
        Assert.True(Step(d, 5.5f, 5.5f, 0f, 0.4f));    // high-Z band alone is not forbidden
    }

    [Fact]
    public void Object_Aabb_Blocks_Its_Box_Not_The_Tile()
    {
        var d = new Detector();
        d.Objects.Add((5.5f, 5.5f, 0.06f)); // a pylon: ~58/512 half-extent at the tile centre
        Assert.False(Step(d, 5.3f, 5.5f, 0.15f, 0f)); // straight into the pylon
        Assert.True(Step(d, 5.3f, 5.2f, 0.3f, 0f));   // past it along the tile edge
    }
}
