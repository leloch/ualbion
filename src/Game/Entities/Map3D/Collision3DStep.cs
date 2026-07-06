using System;

namespace UAlbion.Game.Entities.Map3D;

/// <summary>
/// The original's one-step movement arbiter (fcn.0001e832), ported from _RE_COLLISION3D.md.
/// Positions/deltas in fractional TILE units; margin in tile fractions
/// (= MAX(tileSize/4, 50) world units / tileSize).
///
/// Order of tests, exactly as the original:
///  1. the proposed endpoint's tile must be class-passable (walls/floors/ceilings only);
///  2. the 8 neighbours of the destination tile contribute a forbidden-zone mask over the
///     3×3 margin zones of the destination sub-position — entering a forbidden zone is
///     refused unless the mover is ALREADY in that same tile+zone, in which case only
///     delta components moving away from / parallel to the solid edge are allowed
///     (the 9-case switch @0x1ea7d, centre zone unrestricted);
///  3. the destination tile's object group is overlap-tested (point-in-square).
/// </summary>
public static class Collision3DStep
{
    public static bool IsAllowed(
        ICollisionManager detector,
        float posX, float posZ,      // current position, tile units
        float dx, float dz,          // proposed delta, tile units
        float marginTiles,           // MAX(tileSize/4, 50) / tileSize
        int collisionClass)
    {
        ArgumentNullException.ThrowIfNull(detector);

        float nx = posX + dx, nz = posZ + dz;
        int dtx = (int)MathF.Floor(nx);
        int dtz = (int)MathF.Floor(nz);

        // (1) destination tile passability (off-map counts as blocked)
        if (detector.IsTileBlocked(dtx, dtz, collisionClass))
            return false;

        // (2) neighbour forbidden-zone mask. Zones: zx(0/1/2) + 3·zz(0/1/2) over the
        // destination tile; a solid side neighbour forbids its 3-zone band, a solid corner
        // neighbour its single corner zone (table @0x1d640).
        int forbidden = 0;
        if (detector.IsTileBlocked(dtx - 1, dtz, collisionClass)) forbidden |= (1 << 0) | (1 << 3) | (1 << 6);
        if (detector.IsTileBlocked(dtx + 1, dtz, collisionClass)) forbidden |= (1 << 2) | (1 << 5) | (1 << 8);
        if (detector.IsTileBlocked(dtx, dtz - 1, collisionClass)) forbidden |= (1 << 0) | (1 << 1) | (1 << 2);
        if (detector.IsTileBlocked(dtx, dtz + 1, collisionClass)) forbidden |= (1 << 6) | (1 << 7) | (1 << 8);
        if (detector.IsTileBlocked(dtx - 1, dtz - 1, collisionClass)) forbidden |= 1 << 0;
        if (detector.IsTileBlocked(dtx + 1, dtz - 1, collisionClass)) forbidden |= 1 << 2;
        if (detector.IsTileBlocked(dtx - 1, dtz + 1, collisionClass)) forbidden |= 1 << 6;
        if (detector.IsTileBlocked(dtx + 1, dtz + 1, collisionClass)) forbidden |= 1 << 8;

        if (forbidden != 0)
        {
            int destZone = Zone(nx - dtx, nz - dtz, marginTiles);
            if ((forbidden & (1 << destZone)) != 0)
            {
                // Only tolerated when already hugging that exact tile+zone…
                int ctx = (int)MathF.Floor(posX);
                int ctz = (int)MathF.Floor(posZ);
                int curZone = Zone(posX - ctx, posZ - ctz, marginTiles);
                if (ctx != dtx || ctz != dtz || curZone != destZone)
                    return false;

                // …and then only moving away from / parallel to the solid edge.
                bool ok = destZone switch
                {
                    0 => dx >= 0 && dz >= 0,
                    1 => dz >= 0,
                    2 => dx <= 0 && dz >= 0,
                    3 => dx >= 0,
                    4 => true,
                    5 => dx <= 0,
                    6 => dx >= 0 && dz <= 0,
                    7 => dz <= 0,
                    8 => dx <= 0 && dz <= 0,
                    _ => true,
                };
                if (!ok)
                    return false;
            }
        }

        // (3) object-group overlap on the destination tile
        return !detector.HitsObjectAt(nx, nz, collisionClass);
    }

    static int Zone(float fx, float fz, float m)
    {
        int zx = fx < m ? 0 : fx >= 1f - m ? 2 : 1;
        int zz = fz < m ? 0 : fz >= 1f - m ? 2 : 1;
        return zx + 3 * zz;
    }
}
