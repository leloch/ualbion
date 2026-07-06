namespace UAlbion.Game;

public interface ICollisionManager
{
    bool IsOccupied(int fromX, int fromY, int toX, int toY);
    /// <summary>Faithful 3D tile passability — see <see cref="IMovementCollider.IsTileBlocked"/>.</summary>
    bool IsTileBlocked(int tileX, int tileY, int collisionClass);
    /// <summary>Faithful 3D object overlap — see <see cref="IMovementCollider.HitsObjectAt"/>.</summary>
    bool HitsObjectAt(float posX, float posZ, int collisionClass);
    /// <summary>Combined point query — see <see cref="IMovementCollider.IsBlockedAt"/>.</summary>
    bool IsBlockedAt(float posX, float posZ, int collisionClass);
    void Register(IMovementCollider collider);
    void Unregister(IMovementCollider collider);
}
