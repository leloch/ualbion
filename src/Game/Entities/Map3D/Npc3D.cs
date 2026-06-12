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
    // PLACEHOLDER: walk speed in tiles per FastClock tick. The original engine's 3D NPC
    // step rate isn't RE'd yet; 0.06 ≈ a bit slower than the party so towns feel calm.
    const float TilesPerTick = 0.06f;

    readonly NpcState _state;
    readonly MapNpc _mapData;
    readonly TilemapRequest _properties;
    readonly int _mapWidth;
    readonly int _mapHeight;
    readonly List<(MapObject Object, Vector3 Offset)> _parts = [];
    Vector2 _position; // Tile units (continuous)

    public Npc3D(NpcState state, MapNpc mapData, TilemapRequest properties, int mapWidth, int mapHeight)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _mapData = mapData ?? throw new ArgumentNullException(nameof(mapData));
        _properties = properties ?? throw new ArgumentNullException(nameof(properties));
        _mapWidth = mapWidth;
        _mapHeight = mapHeight;
        _position = new Vector2(_state.X, _state.Y);
        _targetX = _state.X;
        _targetY = _state.Y;
        On<FastClockEvent>(_ => Update());
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
                    if (detector == null || !detector.IsOccupied(_state.X, _state.Y, nx, ny))
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
            _position += Vector2.Normalize(delta) * step;
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
