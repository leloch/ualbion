using UAlbion.Api.Eventing;
using UAlbion.Formats.Assets.Labyrinth;
using UAlbion.Game.Entities.Map2D;

namespace UAlbion.Game.Entities.Map3D;

public class Collider3D(LogicalMap3D logicalMap) : Component, IMovementCollider
{
    readonly LogicalMap3D _logicalMap = logicalMap ?? throw new System.ArgumentNullException(nameof(logicalMap));

    protected override void Subscribed() => Resolve<ICollisionManager>()?.Register(this);
    protected override void Unsubscribed() => Resolve<ICollisionManager>()?.Unregister(this);

    public bool IsOccupied(int fromX, int fromY, int toX, int toY)
    {
        // Stepping onto the source tile is fine — we only need to check the destination.
        // Off-map → blocked.
        if (toX < 0 || toY < 0 || toX >= _logicalMap.Width || toY >= _logicalMap.Height)
            return true;

        // Wall content: contents >= WallOffset (100). LogicalMap3D.GetWall returns
        // (tileIndex, Wall) where a non-null Wall means the tile is occupied by a wall.
        var (_, wall) = _logicalMap.GetWall(toX, toY);
        if (wall != null)
            return true;

        // No floor below → void / unwalkable.
        var (floorIndex, _) = _logicalMap.GetFloor(toX, toY);
        if (floorIndex == 0)
            return true;

        // Tile may also hold a prop (an ObjectGroup). Conservatively treat any sub-object that
        // has non-zero Collision and isn't flagged as a floor-prop as blocking. Floor-objects
        // (rugs, marks on the ground) leave the tile walkable.
        var group = _logicalMap.GetObject(toX, toY);
        if (group != null)
        {
            var objects = _logicalMap.Labyrinth.Objects;
            foreach (var sub in group.SubObjects)
            {
                if (sub == null) continue;
                if (sub.ObjectInfoNumber >= objects.Count) continue;
                var info = objects[sub.ObjectInfoNumber];
                if (info == null) continue;
                if ((info.Properties & LabyrinthObjectFlags.FloorObject) != 0) continue;
                if (info.Collision == 0) continue;
                return true;
            }
        }

        return false;
    }
}
