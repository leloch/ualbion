namespace UAlbion.Game;

public interface IMovementCollider
{
    bool IsOccupied(int fromX, int fromY, int toX, int toY);

    /// <summary>
    /// Faithful 3D tile passability (RE _RE_COLLISION3D.md, fcn.0001eeb8): does the tile's
    /// wall/floor/ceiling record block a mover of the given collision class
    /// (raw collision byte &amp; (0x08 &lt;&lt; class); party = class 0)? Objects are NOT part of
    /// tile passability — they are overlap-tested separately. Colliders without 3D
    /// knowledge fall back to the tile-granular query.
    /// </summary>
    bool IsTileBlocked(int tileX, int tileY, int collisionClass) => IsOccupied(tileX, tileY, tileX, tileY);

    /// <summary>
    /// Faithful 3D object overlap (RE fcn.0001f07e): does a point at (posX, posZ) in
    /// fractional TILE units hit a solid sub-object of the containing tile's object group?
    /// Point-in-square, half-extent MapWidth/2 on both axes, no margin.
    /// </summary>
    bool HitsObjectAt(float posX, float posZ, int collisionClass) => false;

    /// <summary>Combined point query (tile + object) — used by pathing glue and the
    /// stuck-escape check, not by the faithful per-step mover.</summary>
    bool IsBlockedAt(float posX, float posZ, int collisionClass)
        => IsTileBlocked((int)posX, (int)posZ, collisionClass) || HitsObjectAt(posX, posZ, collisionClass);
}
