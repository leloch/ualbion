using System;
using UAlbion.Api.Eventing;
using UAlbion.Formats.Assets.Labyrinth;
using UAlbion.Game.Entities.Map2D;

namespace UAlbion.Game.Entities.Map3D;

/// <summary>
/// Faithful 3D collision primitives (RE docs/re/RE_COLLISION3D.md, address-stamped from MAIN.EXE):
///
/// - TILE passability (fcn.0001eeb8): the wall/floor/ceiling record's raw collision byte is
///   tested against ONE bit selected by the mover's collision class — `raw &amp; (0x08 &lt;&lt; class)`.
///   The party is class 0 (word[0x153ccc], only ever written 0) → bit 0x08 exactly. NoClip
///   NPCs (MapNpc flag 0x40) are class 1 → bit 0x10: they pass normal walls (0x08) but are
///   stopped by dedicated 0x10 fence records; 0x18 blocks both. The old union mask 0x78
///   wrongly blocked the party on 0x10/0xF0 records — the impassable archway/pillar-floor bug.
///   Floor idx 0 is never a block; objects are NOT part of tile passability.
///
/// - OBJECT overlap (fcn.0001f07e): each solid sub-object of the tile's object group is a
///   SQUARE of half-extent MapWidth/2 (objInfo+0xC) on both axes around its sub-position;
///   the mover is a point; no margin. A pylon blocks its ~29-unit box, not the whole tile.
/// </summary>
public class Collider3D(LogicalMap3D logicalMap) : Component, IMovementCollider
{
    readonly LogicalMap3D _logicalMap = logicalMap ?? throw new System.ArgumentNullException(nameof(logicalMap));

    protected override void Subscribed()
    {
        // New map = new collider; world-scoped active spells (Levitation) don't follow
        // the party across maps.
        Magic.ActivePartySpells.OnMapChange();
        Resolve<ICollisionManager>()?.Register(this);
    }

    protected override void Unsubscribed() => Resolve<ICollisionManager>()?.Unregister(this);

    const int PartyClass = 0;
    static uint ClassBit(int collisionClass) => 0x08u << Math.Clamp(collisionClass, 0, 3);

    float TileSize => _logicalMap.Labyrinth?.EffectiveWallWidth ?? 512f;

    /// <summary>Tile-granular query for pathing / spawns (party class). Includes a
    /// remake-only object heuristic: a tile whose solid object AABB covers the tile centre
    /// is unusable as a BFS waypoint (fine movement dodges edge objects itself).</summary>
    public bool IsOccupied(int fromX, int fromY, int toX, int toY)
        => IsTileBlocked(toX, toY, PartyClass)
           || HitsObjectAt(toX + 0.5f, toY + 0.5f, PartyClass);

    public bool IsTileBlocked(int tileX, int tileY, int collisionClass)
    {
        if (tileX < 0 || tileY < 0 || tileX >= _logicalMap.Width || tileY >= _logicalMap.Height)
            return true; // off-map counts as solid (fcn.0001eeb8 bounds check)

        uint bit = ClassBit(collisionClass);

        var (_, wall) = _logicalMap.GetWall(tileX, tileY);
        if (wall != null && (wall.Collision & bit) != 0)
            return true;

        var (_, floor) = _logicalMap.GetFloor(tileX, tileY);
        if (floor != null && (floor.Unk1 & bit) != 0)
            return true;

        var (_, ceiling) = _logicalMap.GetCeiling(tileX, tileY);
        if (ceiling != null && (ceiling.Unk1 & bit) != 0)
            return true;

        return false;
    }

    public bool HitsObjectAt(float posX, float posZ, int collisionClass)
    {
        int tx = (int)MathF.Floor(posX);
        int tz = (int)MathF.Floor(posZ);
        if (tx < 0 || tz < 0 || tx >= _logicalMap.Width || tz >= _logicalMap.Height)
            return false;

        var group = _logicalMap.GetObject(tx, tz);
        var objects = _logicalMap.Labyrinth?.Objects;
        if (group == null || objects == null)
            return false;

        uint bit = ClassBit(collisionClass);
        float tileSize = TileSize;
        float subX = (posX - tx) * tileSize; // world units within the tile, like the original
        float subZ = (posZ - tz) * tileSize;

        foreach (var sub in group.SubObjects)
        {
            if (sub == null) continue;
            if (sub.ObjectInfoNumber >= objects.Count) continue;
            var info = objects[sub.ObjectInfoNumber];
            if (info == null) continue;
            if ((info.Collision & bit) == 0) continue;

            float half = info.MapWidth / 2f;
            if (half <= 0) continue;
            if (MathF.Abs(subX - sub.X) < half && MathF.Abs(subZ - sub.Z) < half)
                return true;
        }

        return false;
    }
}
