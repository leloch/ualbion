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
        int strafe = Math.Abs(e.Velocity.X) > MoveDeadZone ? Math.Sign(e.Velocity.X) : 0;
        int forward = Math.Abs(e.Velocity.Y) > MoveDeadZone ? Math.Sign(e.Velocity.Y) : 0;
        if (strafe == 0 && forward == 0)
            return;

        // Rotate the camera-local input into world tile axes. The camera looks along -Z at
        // yaw 0, so forward=+1 maps to -Z before rotation.
        var camera = TryResolve<UAlbion.Core.Visual.ICamera>();
        float yaw = camera?.Yaw ?? 0f;
        var local = new Vector3(strafe, 0, -forward);
        var world = Vector3.Transform(local, Quaternion.CreateFromYawPitchRoll(yaw, 0f, 0f));

        if (!_noclip)
        {
            world = FilterCollision(world);
            if (world.X == 0 && world.Z == 0)
            {
                TraceLog.Emit("move3d_blocked", ("strafe", strafe), ("forward", forward));
                return;
            }
        }

        TraceLog.Emit("move3d", ("strafe", strafe), ("forward", forward), ("wx", world.X), ("wz", world.Z));
        Raise(new CameraMove3DWorldEvent(world.X, world.Z));
    }

    // RE'd from MAIN.EXE fcn.0001ede1 (9-zone sub-tile collision test): the wall margin
    // is MAX(tileSize/4, 50) world units (RE 5D corrected the earlier min() reading) —
    // on the standard 512-unit tiles that's 128/512 = exactly a QUARTER tile.
    const float CollisionRadiusTiles = 0.25f;

    /// <summary>
    /// Axis-separated sub-tile collision: each world axis of the velocity is tested
    /// independently against the tile the party's collision margin would enter, so motion
    /// into a wall is cancelled on that axis only and the remainder slides along the wall —
    /// matching the original engine's smooth wall-hugging movement.
    /// </summary>
    Vector3 FilterCollision(Vector3 worldVel)
    {
        var detector = TryResolve<ICollisionManager>();
        var party = TryResolve<IParty>();
        var leader = party?.Leader;
        if (detector == null || leader == null)
            return worldVel;

        var pos = leader.GetPosition(); // Tile units; tile N owns [N, N+1)
        int curX = (int)MathF.Floor(pos.X);
        int curY = (int)MathF.Floor(pos.Z);

        bool xBlocked = false, yBlocked = false;
        int targetX = curX, targetY = curY;

        if (worldVel.X != 0)
        {
            targetX = (int)MathF.Floor(pos.X + MathF.Sign(worldVel.X) * CollisionRadiusTiles + worldVel.X * 0.05f);
            if (targetX != curX && detector.IsOccupied(curX, curY, targetX, curY))
                xBlocked = true;
        }

        if (worldVel.Z != 0)
        {
            targetY = (int)MathF.Floor(pos.Z + MathF.Sign(worldVel.Z) * CollisionRadiusTiles + worldVel.Z * 0.05f);
            if (targetY != curY && detector.IsOccupied(curX, curY, curX, targetY))
                yBlocked = true;
        }

        // Diagonal corner case: both axes individually clear but the corner tile is solid.
        if (!xBlocked && !yBlocked && targetX != curX && targetY != curY
            && detector.IsOccupied(curX, curY, targetX, targetY))
        {
            xBlocked = true; // Arbitrarily keep the Z component so we slide rather than stop dead
        }

        TraceLog.Emit("collide_check",
            ("from_x", curX), ("from_y", curY),
            ("to_x", targetX), ("to_y", targetY),
            ("x_blocked", xBlocked), ("y_blocked", yBlocked));

        return new Vector3(xBlocked ? 0 : worldVel.X, 0, yBlocked ? 0 : worldVel.Z);
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

}
