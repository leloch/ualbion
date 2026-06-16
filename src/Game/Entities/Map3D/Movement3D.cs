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
/// First-person dungeon movement. The original Albion 3D is SMOOTH/continuous — you glide
/// forward/back, strafe, and rotate gradually — EXCEPT the four mouse screen-CORNER buttons which
/// snap-turn 90° (top corners) / 180° (bottom corners). So:
///   - Velocity.Y (W/S, Up/Down, mouse fwd/back zones) = continuous forward/back glide.
///   - Velocity.X (mouse strafe edge-zones)            = continuous strafe.
///   - Yaw       (mouse gradual-turn zones; A/D + L/R arrows via cam_rotate) = continuous rotation.
///   - Pitch     (mouse look zones)                    = look up/down.
///   - PartyTurn3DEvent(±90/±180) (mouse corners)      = animated discrete snap-turn.
/// Translation flows through CameraMove3DWorldEvent → CameraMotion3D (integrated × dt = smooth,
/// frame-rate independent + collision-filtered here). Rotation is applied to the injected camera.
/// </summary>
public class Movement3D : Component
{
    // Feel constants — ratios RE'd from MAIN.EXE (_RE_SMOOTH_MOVE.md): per-key scalars
    // forward 166 : back 100 : strafe 67 (so forward is fastest, strafe slowest), and turn
    // keyboard 40 vs mouse 60 (mouse 1.5×). Absolute tile/sec speed picked to feel right; the
    // ratios are what's faithful. Translation is integrated × dt by CameraMotion3D (frame-rate
    // independent); these are tiles/second.
    const float ForwardSpeed = 4.0f;
    const float BackSpeed = ForwardSpeed * 100f / 166f;   // ~2.41
    const float StrafeSpeed = ForwardSpeed * 67f / 166f;  // ~1.61
    const float MouseTurnRate = 0.06f;         // radians per unit mouse turn-intensity (≈1.5× the 0.04 keyboard cam_rotate)
    const float MousePitchRate = 0.02f;
    const float SnapDegreesPerTick = 22.5f;    // corner snap animation: 0x400/frame = 22.5°/frame (90°=4, 180°=8 frames)
    const float Eps = 0.0005f;

    // RE'd 5D corrected: wall margin = MAX(tileSize/4, 50) world units → a quarter tile on 512-tiles.
    const float CollisionRadiusTiles = 0.25f;

    readonly ICamera _camera;
    bool _noclip;
    bool _overloadNotified;
    float _pendingSnapDegrees; // remaining degrees of an in-progress corner snap-turn (signed)
    int _lastTileX = int.MinValue;
    int _lastTileY = int.MinValue;

    // The camera is injected (the rendered DungeonScene camera the leader's position is bound to)
    // so rotation and collision read/write the SAME camera, not whatever TryResolve finds.
    public Movement3D(ICamera camera)
    {
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        On<PartyMove3DEvent>(OnMove3D);
        On<PartyTurn3DEvent>(OnSnapTurn);
        On<PartyTurnEvent>(OnTurn);
        On<FastClockEvent>(OnTick);
        On<NoClipEvent>(_ =>
        {
            _noclip = !_noclip;
            Info($"Clipping {(_noclip ? "off" : "on")}");
        });
    }

    void OnMove3D(PartyMove3DEvent e)
    {
        // Gradual look up/down (mouse top/bottom-centre zones); the camera clamps pitch.
        if (MathF.Abs(e.Pitch) > Eps)
            _camera.Pitch += e.Pitch * MousePitchRate;

        // Gradual continuous turn (mouse turn-edge zones). +Yaw = turn RIGHT (Normal3DMouseMode:
        // TurnRight = +intensity). Turning right rotates the view clockwise = camera yaw DECREASES
        // (the camera looks along -Z at yaw 0; +yaw rotates the look toward the player's left).
        if (MathF.Abs(e.Yaw) > Eps)
            _camera.Yaw -= e.Yaw * MouseTurnRate;

        // Continuous forward/back (Velocity.Y, +=forward) + strafe (Velocity.X, +=right), camera-
        // relative. The camera looks along -Z at yaw 0, so forward maps to -Z and strafe (right) to
        // +X before the yaw rotation. CameraMotion3D integrates the world velocity over dt (smooth).
        float strafe = MathF.Abs(e.Velocity.X) > Eps ? e.Velocity.X : 0f;
        float forward = MathF.Abs(e.Velocity.Y) > Eps ? e.Velocity.Y : 0f;
        if (strafe == 0f && forward == 0f)
            return;

        // #47: an over-encumbered party can't move (turning/looking above is still allowed). Drop
        // items via the inventory to recover.
        var overloaded = State.Player.PartyEncumbrance.FirstOverloaded(TryResolve<IParty>());
        if (overloaded != null)
        {
            if (!_overloadNotified) { ShowOverloadWarning(overloaded); _overloadNotified = true; }
            return;
        }
        _overloadNotified = false;

        // Per-axis speed (forward fastest, back slower, strafe slowest) applied BEFORE the yaw
        // rotation. Camera looks along -Z at yaw 0, so forward → -Z and strafe-right → +X.
        float fSpeed = forward >= 0f ? ForwardSpeed : BackSpeed;
        var local = new Vector3(strafe * StrafeSpeed, 0f, -forward * fSpeed);
        var world = Vector3.Transform(local, Quaternion.CreateFromYawPitchRoll(_camera.Yaw, 0f, 0f));
        if (!_noclip)
        {
            world = FilterCollision(world);
            if (world.X == 0f && world.Z == 0f)
                return;
        }

        Raise(new CameraMove3DWorldEvent(world.X, world.Z)); // already tiles/sec; CameraMotion3D integrates × dt
    }

    // Corner snap-turn (mouse top corners = ±90°, bottom corners = ±180°). +deg = turn RIGHT, which
    // (as above) is a NEGATIVE camera-yaw delta. Accumulated + animated in OnTick along the shorter
    // path so successive corner clicks queue smoothly rather than teleporting the view.
    void OnSnapTurn(PartyTurn3DEvent e) => _pendingSnapDegrees -= e.YawDegrees;

    void OnTick(FastClockEvent e)
    {
        if (MathF.Abs(_pendingSnapDegrees) > 0.5f)
        {
            float step = Math.Min(SnapDegreesPerTick * Math.Max(1, e.Frames), MathF.Abs(_pendingSnapDegrees));
            float signed = MathF.Sign(_pendingSnapDegrees) * step;
            _camera.Yaw += signed * MathF.PI / 180f;
            _pendingSnapDegrees -= signed;
        }

        // Tile-cross detection (2D fires PlayerEnteredTileEvent from PartyCaterpillar; 3D needs its
        // own emitter so DungeonMap and anything watching tile entry hears about movement).
        var leader = TryResolve<IParty>()?.Leader;
        if (leader == null)
            return;

        var pos = leader.GetPosition();
        int tileX = (int)MathF.Round(pos.X);
        int tileY = (int)MathF.Round(pos.Z);
        if (tileX == _lastTileX && tileY == _lastTileY)
            return;

        if (_lastTileX != int.MinValue)
            Raise(new PlayerEnteredTileEvent(tileX, tileY));
        _lastTileX = tileX;
        _lastTileY = tileY;
    }

    /// <summary>
    /// Axis-separated sub-tile collision: each world axis of the velocity is tested independently
    /// against the tile the party's collision margin would enter, so motion into a wall is cancelled
    /// on that axis only and the remainder slides along the wall (the original's wall-hugging).
    /// </summary>
    Vector3 FilterCollision(Vector3 worldVel)
    {
        var detector = TryResolve<ICollisionManager>();
        if (detector == null)
            return worldVel;

        var pos = _camera.Position;
        var map = TryResolve<IMapManager>()?.Current;
        float tsx = map?.TileSize.X ?? 512f, tsz = map?.TileSize.Z ?? 512f;
        float px = pos.X / tsx, pz = pos.Z / tsz; // tile units
        int curX = (int)MathF.Floor(px);
        int curY = (int)MathF.Floor(pz);

        bool xBlocked = false, yBlocked = false;
        int targetX = curX, targetY = curY;

        // Look-ahead = the resting wall margin OR the per-frame step, whichever is larger — NOT their
        // sum. The old `radius + vel*0.05` ADDED the step to the margin, so the party stopped
        // ~0.25 + speed*0.05 tiles from a wall (≈0.4-0.45 while walking) — the "invisible barrier,
        // can't get close" the original doesn't have. MAX keeps the faithful 0.25-tile rest margin
        // (RE 5D: MAX(tile/4,50)) at normal speed while still looking far enough ahead at sprint
        // speed to not overshoot INTO a wall (the "clip one square in" symptom). The per-frame step
        // is bounded by CameraMotion3D's MaxMoveStepSeconds (0.05s) so |vel|*0.05 is the true step.
        if (worldVel.X != 0f)
        {
            float aheadX = MathF.Sign(worldVel.X) * MathF.Max(CollisionRadiusTiles, MathF.Abs(worldVel.X) * 0.05f);
            targetX = (int)MathF.Floor(px + aheadX);
            if (targetX != curX && detector.IsOccupied(curX, curY, targetX, curY))
                xBlocked = true;
        }
        if (worldVel.Z != 0f)
        {
            float aheadZ = MathF.Sign(worldVel.Z) * MathF.Max(CollisionRadiusTiles, MathF.Abs(worldVel.Z) * 0.05f);
            targetY = (int)MathF.Floor(pz + aheadZ);
            if (targetY != curY && detector.IsOccupied(curX, curY, curX, targetY))
                yBlocked = true;
        }

        // Diagonal corner: both axes individually clear but the corner tile is solid → keep the Z
        // component so we slide rather than stop dead.
        if (!xBlocked && !yBlocked && targetX != curX && targetY != curY
            && detector.IsOccupied(curX, curY, targetX, targetY))
            xBlocked = true;

        return new Vector3(xBlocked ? 0f : worldVel.X, 0f, yBlocked ? 0f : worldVel.Z);
    }

    void OnTurn(PartyTurnEvent e)
    {
        // Absolute facing request (save-restore / map-entry establishes PartyDirection). Snap the
        // camera instantly so loading a dungeon doesn't spin the view. Keeps the N=0/E=90/S=180/
        // W=270 yaw convention so saved facings and the renderer's world orientation are preserved.
        if (e.Direction == Direction.Unchanged)
            return;

        _camera.Yaw = e.Direction switch
        {
            Direction.North => 0f,
            Direction.East => (float)(Math.PI / 2.0),
            Direction.South => MathF.PI,
            Direction.West => (float)(3.0 * Math.PI / 2.0),
            _ => 0f
        };
        _pendingSnapDegrees = 0f;
    }

    void ShowOverloadWarning(IPlayer member)
    {
        Raise(new SetContextEvent(UAlbion.Game.Text.ContextType.Subject, member.Id));
        var tf = TryResolve<UAlbion.Game.Text.ITextFormatter>();
        if (tf != null)
            Raise(new DescriptionTextEvent(tf.Format(UAlbion.Base.SystemText.Misc_XIsCarryingTooMuch)));
    }
}
