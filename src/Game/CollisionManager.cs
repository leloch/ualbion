using System.Collections.Generic;
using UAlbion.Api.Eventing;

namespace UAlbion.Game;

public class CollisionManager : ServiceComponent<ICollisionManager>, ICollisionManager
{
    readonly List<IMovementCollider> _colliders = [];

    public bool IsOccupied(int fromX, int fromY, int toX, int toY)
    {
        foreach (var collider in _colliders)
            if (collider.IsOccupied(fromX, fromY, toX, toY))
                return true;
        return false;
    }

    public bool IsBlockedAt(float posX, float posZ, int collisionClass)
    {
        foreach (var collider in _colliders)
            if (collider.IsBlockedAt(posX, posZ, collisionClass))
                return true;
        return false;
    }

    public bool IsTileBlocked(int tileX, int tileY, int collisionClass)
    {
        foreach (var collider in _colliders)
            if (collider.IsTileBlocked(tileX, tileY, collisionClass))
                return true;
        return false;
    }

    public bool HitsObjectAt(float posX, float posZ, int collisionClass)
    {
        foreach (var collider in _colliders)
            if (collider.HitsObjectAt(posX, posZ, collisionClass))
                return true;
        return false;
    }

    public void Register(IMovementCollider collider)
    {
        if (!_colliders.Contains(collider))
            _colliders.Add(collider);
    }

    public void Unregister(IMovementCollider collider) => _colliders.Remove(collider);
}