using System;
using System.Collections.Generic;
using System.Numerics;
using UAlbion.Api.Eventing;
using UAlbion.Core.Visual;
using UAlbion.Formats.Assets.Maps;
using UAlbion.Formats.Assets.Save;
using UAlbion.Game.Events;
using UAlbion.Game.State;

namespace UAlbion.Game.Entities.Map3D;

/// <summary>
/// A 3D-map NPC: a group of billboard MapObjects that follows the NPC's movement schedule.
/// Waypoint NPCs walk toward the waypoint for the current M-tick (1152 slots/day, one per
/// 75 game-seconds — same resolution as the 2D <see cref="Npc2D"/> path); random-wander
/// NPCs pick adjacent tiles. Replaces the old static-prop placement in DungeonMap.BuildNpc.
/// </summary>
public class Npc3D : GameComponent
{
    // 3D NPC walk speed, RE 5D (fcn.0004166c): step = Speed(30) × Δt ÷ 5 world units
    // per logic frame ≈ 360 units/s at the original's 20 Hz logic rate — about 0.7
    // tiles/s on the standard 512-unit tiles (the party moves ~5.6× faster). At the
    // remake's ~8 FastClock ticks/s that is ≈ 0.0875 tiles per tick.
    const float TilesPerTick = 0.0875f;

    readonly NpcState _state;
    readonly MapNpc _mapData;
    readonly TilemapRequest _properties;
    readonly int _mapWidth;
    readonly int _mapHeight;
    readonly byte _npcNumber;
    readonly List<(MapObject Object, Vector3 Offset)> _parts = [];
    Vector2 _position; // Tile units (continuous)
    bool _inContactCombat;
    bool _contactTriggered;

    /// <summary>The live save-state record (position, object group) — read by the NPC
    /// body collider (e832 step 5: movers collide with live NPC bodies).</summary>
    internal NpcState State => _state;
    /// <summary>Continuous position in tile units (the visual glide position).</summary>
    internal Vector2 Position => _position;

    public Npc3D(NpcState state, MapNpc mapData, TilemapRequest properties, int mapWidth, int mapHeight, byte npcNumber)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _mapData = mapData ?? throw new ArgumentNullException(nameof(mapData));
        _properties = properties ?? throw new ArgumentNullException(nameof(properties));
        _mapWidth = mapWidth;
        _mapHeight = mapHeight;
        _npcNumber = npcNumber;
        _position = new Vector2(_state.X, _state.Y);
        _targetX = _state.X;
        _targetY = _state.Y;
        On<FastClockEvent>(_ => Update());
        On<Combat.EndCombatEvent>(e =>
        {
            if (!_inContactCombat)
                return;
            _inContactCombat = false;
            if (e.Result == Combat.CombatResult.Victory)
                Raise(new UAlbion.Formats.ScriptEvents.NpcOffEvent(_npcNumber));
        });
    }

    int _targetX;
    int _targetY;

    protected override void Subscribed()
    {
        if (!_loggedCreation)
        {
            _loggedCreation = true;
            Info($"[Npc3D] active: {_state.Id} {_state.MovementType} @ ({_state.X},{_state.Y}) parts={_parts.Count}");
        }
    }
    bool _loggedCreation;

    public void AddPart(MapObject obj, int tileX, int tileY)
    {
        if (obj == null)
            return;

        AttachChild(obj);
        // The per-part offset is whatever Build added beyond the tile contribution, so the
        // group can be repositioned to any tile by re-adding the tile contribution.
        var tileContribution = tileX * _properties.HorizontalSpacing + tileY * _properties.VerticalSpacing;
        _parts.Add((obj, obj.InitialPosition - tileContribution));
    }

    void Update()
    {
        // Waypoint NPCs are schedule-driven: the target is always the current M-tick's
        // waypoint (redirecting mid-step when the slot changes). Wanderers keep their
        // in-flight target until they arrive, then occasionally pick an adjacent tile.
        if (_state.MovementType is NpcMovement.Waypoints or NpcMovement.Waypoints2)
        {
            var (wx, wy) = GetWaypointTarget();
            _targetX = wx;
            _targetY = wy;
        }
        else if (_state.MovementType == NpcMovement.ChaseParty && AtTarget)
        {
            var party = TryResolve<IParty>();
            var pos = party?.Leader?.GetPosition();
            if (pos != null)
            {
                // 3D: tile Y comes from world Z (the camera IS the party).
                int px = (int)MathF.Floor(pos.Value.X);
                int py = (int)MathF.Floor(pos.Value.Z);
                int dx = Math.Abs(px - _state.X);
                int dy = Math.Abs(py - _state.Y);

                // Contact triggers (same/adjacent tile), mirroring Npc2D.MovementChaseParty:
                // a chasing MONSTER group starts combat (victory removes it via npc_off); any
                // other chaser fires its talk interaction — dialogue, event chain or map-text
                // popup (city guards etc). Re-arms once the party moves away, so a body-blocked
                // talker doesn't wedge the player in a doorway with no way to trigger it.
                bool inContact = Math.Max(dx, dy) <= 1;
                if (!inContact)
                    _contactTriggered = false;

                if (inContact && !_inContactCombat && !_contactTriggered
                    && _state.Id.Type == UAlbion.Config.AssetType.MonsterGroup)
                {
                    _inContactCombat = true;
                    Raise(new UAlbion.Formats.MapEvents.EncounterEvent(
                        (UAlbion.Formats.Ids.MonsterGroupId)_state.Id,
                        UAlbion.Formats.Ids.CombatBackgroundId.None));
                }
                else if (inContact && !_contactTriggered
                    && _state.Id.Type != UAlbion.Config.AssetType.MonsterGroup)
                {
                    var talk = SelectionHandler3D.BuildNpcInteraction(_state, TryResolve<UAlbion.Formats.IAssetManager>());
                    if (talk != null)
                    {
                        _contactTriggered = true;
                        Raise(talk);
                    }
                }
                else if (HasLineOfSight(px, py)) // 3D detection = Bresenham LOS, no distance cap (RE 5D fcn.00041c3c)
                {
                    // Step one tile toward the party, axis-major, respecting walls. Collision
                    // class per the RE: NoClip NPCs are class 1 (pass 0x08 walls, blocked by
                    // 0x10 fences); everything else class 0 like the party.
                    int nx = _state.X + Math.Sign(px - _state.X);
                    int ny = _state.Y + Math.Sign(py - _state.Y);
                    var detector = TryResolve<ICollisionManager>();
                    if (nx != _state.X && (detector == null || !TileBlockedForNpc(detector, nx, _state.Y)))
                        _targetX = nx;
                    else if (ny != _state.Y && (detector == null || !TileBlockedForNpc(detector, _state.X, ny)))
                        _targetY = ny;
                }
            }
        }
        else if (_state.MovementType == NpcMovement.RandomWander && AtTarget)
        {
            var rng = Resolve<IRandom>();
            if (rng.Generate(8) == 0) // Only occasionally start a new step
            {
                int nx = _state.X, ny = _state.Y;
                switch (rng.Generate(4))
                {
                    case 0: nx = _state.X - 1; break;
                    case 1: nx = _state.X + 1; break;
                    case 2: ny = _state.Y - 1; break;
                    default: ny = _state.Y + 1; break;
                }

                // Stay on the map (state coords are ushort — walking past 0 wraps to 65535)
                // and respect walls/props via the 3D collider.
                if (nx >= 0 && ny >= 0 && nx < _mapWidth && ny < _mapHeight)
                {
                    var detector = TryResolve<ICollisionManager>();
                    if (detector == null || !TileBlockedForNpc(detector, nx, ny))
                    {
                        _targetX = nx;
                        _targetY = ny;
                    }
                }
            }
        }

        var target = new Vector2(_targetX, _targetY);
        var delta = target - _position;
        float dist = delta.Length();
        if (dist > 8)
        {
            // Way off schedule (e.g. player just arrived / big time jump): snap like Npc2D.
            _position = target;
        }
        else if (dist > 0.01f)
        {
            float step = Math.Min(TilesPerTick, dist);
            var next = _position + Vector2.Normalize(delta) * step;

            // e832 step 4 for the glide too: never walk INTO the party's body (chasers stop
            // adjacent where contact combat fires; schedule NPCs wait until the player moves).
            // Only refuse when the step gets CLOSER, so an NPC already overlapping can leave.
            var pp = TryResolve<IParty>()?.Leader?.GetPosition();
            if (pp != null)
            {
                var party = new Vector2(pp.Value.X, pp.Value.Z);
                var body = _position + new Vector2(0.5f, 0.5f); // body centre vs tile-origin position
                float curDist = (party - body).Length();
                float nextDist = (party - (next + new Vector2(0.5f, 0.5f))).Length();
                if (nextDist < 0.75f && nextDist < curDist)
                    return;
            }

            _position = next;
        }
        else
        {
            return; // At target, nothing to update
        }

        _state.X = (ushort)MathF.Round(_position.X);
        _state.Y = (ushort)MathF.Round(_position.Y);
        SyncParts();
    }

    bool AtTarget => (new Vector2(_targetX, _targetY) - _position).Length() <= 0.01f;

    // NPC collision class (RE _RE_COLLISION3D.md): NoClip NPCs (MapNpc flag 0x40 → NpcState+5)
    // are class 1 — they test collision bit 0x10 instead of the party's 0x08, letting them
    // pass normal walls while dedicated fence records stop them. Others are class 0.
    // Also e832 step 4: NPC movers are blocked by the PARTY's body — an NPC never steps
    // onto the party's tile (chasers stop adjacent, where contact combat triggers).
    bool TileBlockedForNpc(ICollisionManager detector, int x, int y)
    {
        int cls = _state.NoClip ? 1 : 0;
        if (detector.IsTileBlocked(x, y, cls) || detector.HitsObjectAt(x + 0.5f, y + 0.5f, cls))
            return true;

        var leader = TryResolve<IParty>()?.Leader;
        if (leader != null)
        {
            var p = leader.GetPosition();
            if (MathF.Abs(p.X - (x + 0.5f)) < 0.75f && MathF.Abs(p.Z - (y + 0.5f)) < 0.75f)
                return true;
        }

        return false;
    }

    /// <summary>
    /// 3D chase detection (RE 5D, fcn.00041c3c): a Bresenham line from the NPC to the
    /// party with NO distance cap; any sight-blocking wall (the automap's BlocksSight
    /// flag — wall Properties bit 0x04, queried via the collision manager as a proxy)
    /// breaks detection. On loss the original drops to wandering.
    /// </summary>
    bool HasLineOfSight(int px, int py)
    {
        var detector = TryResolve<ICollisionManager>();
        if (detector == null)
            return true;

        int x0 = _state.X, y0 = _state.Y, x1 = px, y1 = py;
        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;
        int guard = 0;
        while ((x0 != x1 || y0 != y1) && guard++ < 256)
        {
            int e2 = 2 * err;
            int nx = x0, ny = y0;
            if (e2 >= dy) { err += dy; nx += sx; }
            if (e2 <= dx) { err += dx; ny += sy; }
            if ((nx != x1 || ny != y1) && detector.IsOccupied(x0, y0, nx, ny))
                return false; // wall in the way
            x0 = nx; y0 = ny;
        }
        return true;
    }

    (int X, int Y) GetWaypointTarget()
    {
        if (_mapData.Waypoints == null || _mapData.Waypoints.Length == 0)
            return (_targetX, _targetY);

        var game = Resolve<IGameState>();
        int index = game.MTicksToday;
        if (index >= _mapData.Waypoints.Length)
            index = 0;
        var wp = _mapData.Waypoints[index];
        return (wp.X, wp.Y);
    }

    void SyncParts()
    {
        var tileContribution = _position.X * _properties.HorizontalSpacing + _position.Y * _properties.VerticalSpacing;
        foreach (var (obj, offset) in _parts)
            obj.Position = tileContribution + offset;
    }

    public override string ToString() => $"Npc3D {_state.Id} @ ({_position.X:F1},{_position.Y:F1}) {_state.MovementType}";
}
