using System;
using System.Numerics;
using UAlbion.Api.Eventing;
using UAlbion.Core.Visual;
using UAlbion.Formats;
using UAlbion.Formats.ScriptEvents;
using UAlbion.Game.Events;
using UAlbion.Game.State;

namespace UAlbion.Game.Entities.Map3D;

/// <summary>
/// Faithful first-person dungeon movement, RE'd from MAIN.EXE (_RE_3D_MOVEMENT.md). The original
/// 3D model is DISCRETE: one tile per forward/back command, 90° quadrant turns, and NO strafe —
/// the side inputs TURN. We keep a single source of truth (the quantised target yaw) and derive
/// the forward tile-step from the camera's OWN look transform, so forward/back/turn/camera can
/// never disagree. (The previous continuous free-camera derived the step from a drifting yaw,
/// which is what made backtracking "reverse" and made the mouse turn the wrong way.)
/// </summary>
public class Movement3D : Component
{
    // 60 fast-ticks/second. ~8 ticks per action = a snappy ~0.13s slide/turn (the original
    // animated turns at a fixed angular rate along the shorter arc; a smooth lerp matches the feel).
    const float TurnRadiansPerTick = (float)(Math.PI / 2.0) / 8f;
    const float StepFractionPerTick = 1f / 8f;
    const float MousePitchRate = 0.02f;
    const float Eps = 0.001f;

    readonly ICamera _camera;
    bool _noclip;
    bool _turning;
    bool _stepping;
    float _targetYaw;
    float _stepProgress;
    Vector3 _stepFrom;
    Vector3 _stepTo;
    int _lastTileX = int.MinValue;
    int _lastTileY = int.MinValue;

    // The camera is injected (the rendered DungeonScene camera the leader's position is bound to)
    // rather than resolved — Movement3D must drive the SAME camera, not whatever TryResolve finds.
    public Movement3D(ICamera camera)
    {
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        On<PartyMove3DEvent>(OnMove3D);
        On<PartyTurn3DEvent>(e => RequestTurn(-e.YawDegrees * (float)Math.PI / 180f)); // +deg = turn right
        On<PartyTurnEvent>(OnTurn);
        On<FastClockEvent>(OnTick);
        On<NoClipEvent>(_ =>
        {
            _noclip = !_noclip;
            Info($"Clipping {(_noclip ? "off" : "on")}");
        });
    }

    bool Busy => _turning || _stepping;

    // The party's forward tile-delta for a given yaw, taken from the engine's own look transform
    // (camera looks along -Z at yaw 0 = north = tile -Y) and snapped to the dominant cardinal. By
    // sourcing this from the same transform the renderer uses, "forward" always goes where the
    // camera looks and "back" is its exact negation — no handedness guess, no world mirroring.
    static (int dx, int dz) StepDeltaForYaw(float yaw)
    {
        var look = Vector3.Transform(-Vector3.UnitZ, Quaternion.CreateFromYawPitchRoll(yaw, 0f, 0f));
        if (MathF.Abs(look.X) >= MathF.Abs(look.Z))
            return (look.X >= 0f ? 1 : -1, 0);
        return (0, look.Z >= 0f ? 1 : -1);
    }

    static float SnapYaw(float yaw)
    {
        const float q = (float)(Math.PI / 2.0);
        return MathF.Round(yaw / q) * q;
    }

    static float NormalizeAngle(float a)
    {
        while (a <= -MathF.PI) a += MathF.Tau;
        while (a > MathF.PI) a -= MathF.Tau;
        return a;
    }

    void OnMove3D(PartyMove3DEvent e)
    {
        // Mouse look-up/down (compass top/bottom edges) — a small pitch nudge; the camera clamps.
        if (MathF.Abs(e.Pitch) > Eps)
            _camera.Pitch += e.Pitch * MousePitchRate;

        if (Busy)
            return; // one discrete action at a time; held keys re-fire and step again once idle

        // Side input = TURN (keyboard A/D = Velocity.X, mouse turn edges = Yaw). NO strafe.
        int turn = 0;
        if (MathF.Abs(e.Velocity.X) > Eps) turn = e.Velocity.X > 0 ? 1 : -1;
        else if (MathF.Abs(e.Yaw) > Eps) turn = e.Yaw > 0 ? 1 : -1;
        if (turn != 0)
        {
            RequestTurn(turn > 0 ? -(float)(Math.PI / 2.0) : (float)(Math.PI / 2.0)); // right = yaw down
            return;
        }

        // Vertical input = forward/back one tile.
        if (MathF.Abs(e.Velocity.Y) > Eps)
            RequestStep(e.Velocity.Y > 0 ? 1 : -1);
    }

    void RequestTurn(float deltaYaw)
    {
        if (Busy)
            return;
        _targetYaw = SnapYaw(_camera.Yaw) + deltaYaw;
        _turning = true;
    }

    void RequestStep(int forward)
    {
        if (Busy)
            return;
        var map = TryResolve<IMapManager>()?.Current;
        if (map == null)
            return;

        float tsx = map.TileSize.X, tsz = map.TileSize.Z;
        int curX = (int)MathF.Round(_camera.Position.X / tsx);
        int curY = (int)MathF.Round(_camera.Position.Z / tsz);

        var (dx, dz) = StepDeltaForYaw(SnapYaw(_camera.Yaw));
        if (forward < 0) { dx = -dx; dz = -dz; }
        int tx = curX + dx, ty = curY + dz;

        if (!_noclip)
        {
            var detector = TryResolve<ICollisionManager>();
            if (detector != null && detector.IsOccupied(curX, curY, tx, ty))
            {
                TraceLog.Emit("move3d_blocked", ("from_x", curX), ("from_y", curY), ("to_x", tx), ("to_y", ty));
                return;
            }
        }

        _stepFrom = _camera.Position;
        _stepTo = new Vector3(tx * tsx, _camera.Position.Y, ty * tsz);
        _stepProgress = 0f;
        _stepping = true;
        TraceLog.Emit("move3d", ("from_x", curX), ("from_y", curY), ("to_x", tx), ("to_y", ty));
    }

    void OnTick(FastClockEvent e)
    {
        if (_turning)
        {
            float diff = NormalizeAngle(_targetYaw - _camera.Yaw);
            float maxStep = TurnRadiansPerTick * Math.Max(1, e.Frames);
            if (MathF.Abs(diff) <= maxStep)
            {
                _camera.Yaw = NormalizeAngle(_targetYaw);
                _turning = false;
            }
            else
            {
                _camera.Yaw += MathF.Sign(diff) * maxStep;
            }
        }

        if (_stepping)
        {
            _stepProgress += StepFractionPerTick * Math.Max(1, e.Frames);
            if (_stepProgress >= 1f)
            {
                _camera.Position = _stepTo;
                _stepping = false;
            }
            else
            {
                _camera.Position = Vector3.Lerp(_stepFrom, _stepTo, _stepProgress);
            }
        }

        // Tile-cross detection: 2D fires PlayerEnteredTileEvent from PartyCaterpillar; 3D needs
        // its own emitter so DungeonMap (and anything watching tile entry) hears about steps.
        var leader = TryResolve<IParty>()?.Leader;
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

    void OnTurn(PartyTurnEvent e)
    {
        // Absolute facing request (save-restore / map-entry raise one to establish PartyDirection).
        // Snap instantly so loading a dungeon doesn't visibly spin the camera. Keeps the existing
        // North=0 / East=90 / South=180 / West=270 yaw convention, so saved facings (and the world
        // orientation the renderer already produces) are preserved exactly.
        if (e.Direction == Direction.Unchanged)
            return;

        float yaw = e.Direction switch
        {
            Direction.North => 0f,
            Direction.East => (float)(Math.PI / 2.0),
            Direction.South => MathF.PI,
            Direction.West => (float)(3.0 * Math.PI / 2.0),
            _ => 0f
        };

        _targetYaw = NormalizeAngle(yaw);
        _camera.Yaw = _targetYaw;
        _turning = false;
        _stepping = false;
    }
}
