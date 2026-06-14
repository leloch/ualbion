using UAlbion.Api.Eventing;
using UAlbion.Formats.Assets.Labyrinth;
using UAlbion.Game.Entities.Map2D;

namespace UAlbion.Game.Entities.Map3D;

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

    // Directional collision bits live at bits 11..14 of the 24-bit Collision field (one per
    // approach direction N/E/S/W) — RE'd from MAIN.EXE fcn.0001eeb8 (_RE_3D_COLLISION.md). The
    // original tests bit (dir+11); we use the direction-agnostic union 0x7800 (any of the four)
    // which fixes both reported bugs (walk-over-water, stuck-on-passable) and is a strict
    // improvement over the old "any wall blocks / floorIndex==0" rule. One-way/partial barriers
    // (per-direction) are a later refinement.
    const uint CollisionMask = 0x7800; // bits 11,12,13,14

    // The original reads the floor/ceiling collision as the first dword of the 10-byte
    // FloorAndCeiling record (Properties|Unk1|Unk2|Unk3); bits 11..14 fall in Unk1 (bits 3..6).
    static uint FcCollisionDword(FloorAndCeiling fc) =>
        (uint)fc.Properties | ((uint)fc.Unk1 << 8) | ((uint)fc.Unk2 << 16) | ((uint)fc.Unk3 << 24);

    public bool IsOccupied(int fromX, int fromY, int toX, int toY)
    {
        // Stepping onto the source tile is fine — we only need to check the destination.
        // Off-map → blocked.
        if (toX < 0 || toY < 0 || toX >= _logicalMap.Width || toY >= _logicalMap.Height)
            return true;

        // WALL: blocks only when its directional collision bits are set — NOT "any wall blocks".
        // Decorative walls / open archways whose Collision bits are clear are walkable (this was
        // the "stuck on passable tiles" half of the bug). fcn.0001eeb8 wall test.
        var (_, wall) = _logicalMap.GetWall(toX, toY);
        if (wall != null && (wall.Collision & CollisionMask) != 0)
            return true;

        // FLOOR: blocks via the SAME collision-bit mechanism — this is how WATER blocks (it is a
        // floor type with the collision bits set, not a pit). The old code only checked
        // floorIndex==0 and so let the party walk over water. fcn.0001eeb8 floor test.
        var (floorIndex, floor) = _logicalMap.GetFloor(toX, toY);
        if (floor != null && (FcCollisionDword(floor) & CollisionMask) != 0)
            return true;

        // No floor at all → void / pit, unless levitating. The original predicate doesn't block
        // on a missing floor, but keep this conservative pit-guard (deliberate deviation,
        // ledger §8) so the party can't walk into genuinely empty tiles. Water is unaffected
        // (it has a non-zero floor index and blocks via the floor test above).
        if (floorIndex == 0 && !Magic.ActivePartySpells.Levitating)
            return true;

        // CEILING: same collision-bit mechanism (low/solid ceilings can block). fcn.0001eeb8.
        var (_, ceiling) = _logicalMap.GetCeiling(toX, toY);
        if (ceiling != null && (FcCollisionDword(ceiling) & CollisionMask) != 0)
            return true;

        // OBJECT-GROUP props: block when a sub-object's directional collision bits are set and it
        // isn't a floor-prop. (fcn.0001f07e also tests AABB footprint overlap; the per-tile
        // approximation here is kept but gated on the directional bits for consistency.)
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
                if ((info.Collision & CollisionMask) != 0)
                    return true;
            }
        }

        return false;
    }
}
