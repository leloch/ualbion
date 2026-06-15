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

    // Passability rule RE'd against REAL labyrinth data (_RE_COLLISION_DATA.md). The original
    // (fcn.0001eeb8) tests bit (dir+11) of the dword read from record offset 0 — i.e. bits 3..6 of
    // the raw Collision low-byte (wall/object) or Unk1 (floor/ceiling). So the directional block
    // bits are RAW mask 0x78 (NOT 0x7800 — the prior RE mis-mapped the shift, making the test inert).
    //   WALLS/OBJECTS: solid sides set 0x08/0x10/0x18 (block); open doorways/gates/arches = 0 (pass).
    //   FLOORS: ONLY bit 3 (0x08) marks a hazard floor (Water/deep-water = block); bit 4 (0x10) and
    //   0xF0 are NORMAL walkable floors (mossy stone, jewels, wood) — so the floor mask is 0x08, not
    //   0x78 (using 0x78 would wrongly block ~160 ordinary floors).
    const uint WallObjMask = 0x78;  // wall/object solid-side bits 3..6
    const byte FloorHazardBit = 0x08; // floor/ceiling hazard bit (water)

    public bool IsOccupied(int fromX, int fromY, int toX, int toY)
    {
        // Stepping onto the source tile is fine — we only need to check the destination.
        // Off-map → blocked.
        if (toX < 0 || toY < 0 || toX >= _logicalMap.Width || toY >= _logicalMap.Height)
            return true;

        // WALL: blocks only when a solid-side bit is set — open archways/gates (Collision==0) pass.
        var (_, wall) = _logicalMap.GetWall(toX, toY);
        if (wall != null && (wall.Collision & WallObjMask) != 0)
            return true;

        // FLOOR: a hazard floor (Water, Unk1 bit 3) blocks. Normal floors (Unk1 0/0x10/0xF0) pass.
        // NO floorIndex==0 "pit" block: the original treats a missing floor as passable (skip the
        // floor test) — that phantom guard was what blocked open archway/threshold tiles (floor 0).
        var (_, floor) = _logicalMap.GetFloor(toX, toY);
        if (floor != null && (floor.Unk1 & FloorHazardBit) != 0)
            return true;

        // CEILING: same hazard mechanism (rare for movement).
        var (_, ceiling) = _logicalMap.GetCeiling(toX, toY);
        if (ceiling != null && (ceiling.Unk1 & FloorHazardBit) != 0)
            return true;

        // OBJECT-GROUP props: block when a non-floor sub-object has a solid-side bit set.
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
                if ((info.Collision & WallObjMask) != 0)
                    return true;
            }
        }

        return false;
    }
}
