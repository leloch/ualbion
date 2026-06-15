namespace UAlbion.Game.State;

/// <summary>
/// Picks a walkable spawn tile for a synthetic scenario using the LIVE collider (after the map
/// has loaded), so synthesis works uniformly for 2D and 3D maps without replicating collision
/// logic. The party is jumped with no validation (PartyCaterpillar), and an invalid tile crashes
/// a frame later — so callers resolve a known-good tile here and re-jump before the next frame.
/// </summary>
public static class SpawnValidator
{
    // Albion maps are well under this; out-of-bounds tiles report occupied so the scan is safe.
    const int MaxScan = 128;

    public static bool IsWalkable(ICollisionManager collision, int x, int y)
        => collision != null && x >= 0 && y >= 0 && !collision.IsOccupied(x, y, x, y);

    /// <summary>
    /// Prefer the requested tile; if it isn't walkable (or no preference), scan for the first
    /// walkable tile. Returns false only if the collider is missing or the whole map is blocked.
    /// </summary>
    public static bool TryResolve(ICollisionManager collision, int preferX, int preferY, out int x, out int y)
    {
        if (IsWalkable(collision, preferX, preferY))
        {
            x = preferX;
            y = preferY;
            return true;
        }

        if (collision != null)
        {
            for (int yy = 0; yy < MaxScan; yy++)
                for (int xx = 0; xx < MaxScan; xx++)
                    if (!collision.IsOccupied(xx, yy, xx, yy))
                    {
                        x = xx;
                        y = yy;
                        return true;
                    }
        }

        x = preferX;
        y = preferY;
        return false;
    }
}
