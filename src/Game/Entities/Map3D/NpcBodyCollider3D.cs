using System;
using System.Collections.Generic;
using UAlbion.Api.Eventing;
using UAlbion.Formats.Assets.Labyrinth;

namespace UAlbion.Game.Entities.Map3D;

/// <summary>
/// NPC body collision (RE docs/re/RE_COLLISION3D.md, fcn.0001e832 step 5): every mover is
/// overlap-tested against the bodies of all live NPCs — each body is the NPC's own
/// object group's solid sub-object AABBs (half-extent MapWidth/2), centred on the NPC's
/// position. The original pre-culls at one tile's distance; we do the same. This is what
/// stops the party walking straight through people/monsters in dungeons.
/// Contributes only the point query — tiles never become solid because an NPC stands
/// there (pathing routes around via the fine movement, like the original).
/// </summary>
public class NpcBodyCollider3D(
    Dictionary<int, Npc3D> npcs,
    LabyrinthData labyrinth) : Component, IMovementCollider
{
    readonly Dictionary<int, Npc3D> _npcs = npcs ?? throw new ArgumentNullException(nameof(npcs));
    readonly LabyrinthData _labyrinth = labyrinth ?? throw new ArgumentNullException(nameof(labyrinth));

    protected override void Subscribed() => Resolve<ICollisionManager>()?.Register(this);
    protected override void Unsubscribed() => Resolve<ICollisionManager>()?.Unregister(this);

    public bool IsOccupied(int fromX, int fromY, int toX, int toY) => false;
    public bool IsTileBlocked(int tileX, int tileY, int collisionClass) => false;

    public bool HitsObjectAt(float posX, float posZ, int collisionClass)
    {
        float ts = _labyrinth.EffectiveWallWidth;
        if (ts <= 0) ts = 512f;
        uint bit = 0x08u << Math.Clamp(collisionClass, 0, 3);

        foreach (var npc in _npcs.Values)
        {
            var state = npc?.State;
            if (state == null || state.Id.IsNone)
                continue;

            var pos = npc.Position; // continuous glide position, tile units
            if (MathF.Abs(pos.X - posX) >= 1f || MathF.Abs(pos.Y - posZ) >= 1f)
                continue; // one-tile pre-cull like the original

            var groupId = state.SpriteOrGroup;
            if (groupId.Type != UAlbion.Config.AssetType.ObjectGroup
                || groupId.Id <= 0 || groupId.Id > _labyrinth.ObjectGroups.Count)
                continue;

            var group = _labyrinth.ObjectGroups[groupId.Id - 1];
            foreach (var sub in group.SubObjects)
            {
                if (sub == null) continue;
                if (sub.ObjectInfoNumber >= _labyrinth.Objects.Count) continue;
                var info = _labyrinth.Objects[sub.ObjectInfoNumber];
                if (info == null || (info.Collision & bit) == 0) continue;

                float half = info.MapWidth / 2f;
                if (half <= 0) continue;

                // Same frame as static tile objects (Collider3D.HitsObjectAt): sub-object
                // positions are world units from the TILE ORIGIN, and the NPC's continuous
                // position is its tile coordinate — so the box centre just glides with it.
                float cx = pos.X * ts + sub.X;
                float cz = pos.Y * ts + sub.Z;
                if (MathF.Abs(posX * ts - cx) < half && MathF.Abs(posZ * ts - cz) < half)
                    return true;
            }
        }

        return false;
    }
}
