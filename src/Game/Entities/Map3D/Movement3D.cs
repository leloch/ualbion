using System;
using System.Numerics;
using UAlbion.Api.Eventing;
using UAlbion.Formats;
using UAlbion.Formats.Assets;
using UAlbion.Formats.ScriptEvents;
using UAlbion.Game;
using UAlbion.Game.Events;
using UAlbion.Game.State;

namespace UAlbion.Game.Entities.Map3D;

public class Movement3D : Component
{
    const float TurnRateDegreesPerFrame = 3.0f;
    const float MoveDeadZone = 0.05f;

    float _pendingYawDegrees;
    bool _noclip;
    int _lastTileX = int.MinValue;
    int _lastTileY = int.MinValue;

    public Movement3D()
    {
        On<PartyMove3DEvent>(OnMove3D);
        On<PartyTurn3DEvent>(e => _pendingYawDegrees = e.YawDegrees);
        On<PartyTurnEvent>(OnTurn);
        On<FastClockEvent>(OnTick);
        On<NoClipEvent>(_ =>
        {
            _noclip = !_noclip;
            Info($"Clipping {(_noclip ? "off" : "on")}");
        });
    }

    void OnTick(FastClockEvent e)
    {
        var absPendingYaw = Math.Abs(_pendingYawDegrees);
        if (absPendingYaw > 0.1f)
        {
            int sign = Math.Sign(_pendingYawDegrees);
            float d = e.Frames * TurnRateDegreesPerFrame;
            float newAbs = Math.Max(0, absPendingYaw - d);
            _pendingYawDegrees = sign * newAbs;

            var dRadians = -sign * d * MathF.PI / 180.0f;
            Raise(new CameraRotateEvent(dRadians, 0));
        }

        // Tile-cross detection: 2D fires PlayerEnteredTileEvent from PartyCaterpillar; 3D needs
        // its own emitter so DungeonMap (or anything else watching tile entry) hears about steps.
        var party = TryResolve<IParty>();
        var leader = party?.Leader;
        if (leader == null)
            return;

        var pos = leader.GetPosition();
        int tileX = (int)MathF.Round(pos.X);
        int tileY = (int)MathF.Round(pos.Z);

        if (tileX == _lastTileX && tileY == _lastTileY)
            return;

        // Skip the very first sample to avoid synthesising a tile-cross at load.
        if (_lastTileX != int.MinValue)
            Raise(new PlayerEnteredTileEvent(tileX, tileY));

        _lastTileX = tileX;
        _lastTileY = tileY;
    }

    void OnMove3D(PartyMove3DEvent e)
    {
        // Velocity.X = strafe intensity (positive = right), Velocity.Y = forward intensity.
        // CameraMoveEvent uses tile-relative deltas that CameraMotion3D yaw-transforms and
        // integrates with the engine's delta time. Integer sign is enough for forward/strafe;
        // CameraMotion3D scales by tile size and delta time.
        int strafe = Math.Abs(e.Velocity.X) > MoveDeadZone ? Math.Sign(e.Velocity.X) : 0;
        int forward = Math.Abs(e.Velocity.Y) > MoveDeadZone ? Math.Sign(e.Velocity.Y) : 0;
        if (strafe == 0 && forward == 0)
            return;

        if (!_noclip && IsBlocked(strafe, forward))
        {
            TraceLog.Emit("move3d_blocked", ("strafe", strafe), ("forward", forward));
            return;
        }

        TraceLog.Emit("move3d", ("strafe", strafe), ("forward", forward));
        Raise(new CameraMoveEvent(strafe, forward, null));
    }

    void OnTurn(PartyTurnEvent e)
    {
        // PartyTurnEvent is an ABSOLUTE facing request (save-restore raises one to re-establish
        // PartyDirection). Translate to the additive yaw tween used by PartyTurn3DEvent by
        // diffing the target yaw against the camera's current yaw and snapping into the
        // range (-180, +180] so we always take the short way round.
        // Direction.Unchanged is a no-op (preserves current facing).
        if (e.Direction == Direction.Unchanged)
            return;

        var camera = TryResolve<UAlbion.Core.Visual.ICamera>();
        if (camera == null)
            return;

        float targetDegrees = e.Direction switch
        {
            Direction.North => 0f,
            Direction.East => 90f,
            Direction.South => 180f,
            Direction.West => 270f,
            _ => 0f
        };

        float currentDegrees = camera.Yaw * 180f / MathF.PI;
        float delta = targetDegrees - currentDegrees;
        // Normalise to (-180, 180]
        while (delta <= -180f) delta += 360f;
        while (delta > 180f) delta -= 360f;

        if (MathF.Abs(delta) < 0.5f)
            return;

        _pendingYawDegrees = delta;
    }

    bool IsBlocked(int strafe, int forward)
    {
        var detector = TryResolve<ICollisionManager>();
        if (detector == null)
            return false;

        var party = TryResolve<IParty>();
        var leader = party?.Leader;
        if (leader == null)
            return false;

        var leaderPos = leader.GetPosition();
        // Camera/world axes in this engine: X = east-west, Z = north-south (Y is up).
        // The map's tile Y maps to world Z (see DungeonMap.Setup: VerticalSpacing = TileSize * UnitZ).
        // leader.GetPosition() returns position/TileSize so it's already in tile units.
        //
        // We use Floor (not Round) for the "current" tile: tile coordinate N owns the
        // half-open range [N, N+1), so position 31.6 is INSIDE tile 31. Round would flip
        // to tile 32 once we pass the half-tile mark and check the wrong destination tile
        // — that was the bug the user reported as "no collisions".
        int fromX = (int)MathF.Floor(leaderPos.X);
        int fromY = (int)MathF.Floor(leaderPos.Z);

        // Predict next tile from the leader's facing direction. The math must match exactly
        // what CameraMotion3D does: world delta = Vector3.Transform((eX, 0, -eY), Q(yaw)).
        // The camera looks along -Z at yaw 0, so forward=+1 moves -Z = -tileY; strafe=+1
        // moves +X = +tileX.
        var camera = TryResolve<UAlbion.Core.Visual.ICamera>();
        float yaw = camera?.Yaw ?? 0f;
        var localVel = new System.Numerics.Vector3(strafe, 0, -forward);
        var rotation = System.Numerics.Quaternion.CreateFromYawPitchRoll(yaw, 0f, 0f);
        var worldVel = System.Numerics.Vector3.Transform(localVel, rotation);
        int toX = fromX + (int)MathF.Round(worldVel.X);
        int toY = fromY + (int)MathF.Round(worldVel.Z);

        var blocked = detector.IsOccupied(fromX, fromY, toX, toY);
        TraceLog.Emit("collide_check",
            ("from_x", fromX), ("from_y", fromY),
            ("to_x",   toX),   ("to_y",   toY),
            ("yaw",    yaw),
            ("blocked", blocked));
        return blocked;
    }
}
