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
    // Feel constants — ratios RE'd from MAIN.EXE (docs/re/RE_SMOOTH_MOVE.md): per-key scalars
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
    float _frameDt = 1f / 60f; // last frame's delta time — the collision step must test the
                               // REAL per-frame delta (the original tests exactly one frame's
                               // move), not the 0.05 s integration cap, or the wall standoff
                               // inflates by up to a whole max-step.
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
        On<UAlbion.Core.Events.EngineUpdateEvent>(e => _frameDt = Math.Clamp(e.DeltaSeconds, 0.001f, 0.05f));
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
        // MOV3D-02: use Floor (not Round) so the reported tile matches the convention used by
        // FilterCollision and the automap (tile N owns [N, N+1)); Round flipped a half-tile early.
        int tileX = (int)MathF.Floor(pos.X);
        int tileY = (int)MathF.Floor(pos.Z);
        if (tileX == _lastTileX && tileY == _lastTileY)
            return;

        if (_lastTileX != int.MinValue)
            Raise(new PlayerEnteredTileEvent(tileX, tileY));
        _lastTileX = tileX;
        _lastTileY = tileY;
    }

    /// <summary>
    /// The original's movement collision, ported 1:1 from docs/re/RE_COLLISION3D.md:
    /// the mover is a POINT; the proposed endpoint's TILE must be class-passable
    /// (fcn.0001eeb8, raw byte &amp; 0x08 for the party); the 8 neighbours of the destination
    /// tile mark forbidden 3×3 margin zones (margin = MAX(tileSize/4, 50)) — entering a
    /// forbidden zone is refused unless already hugging that same tile+zone, in which case
    /// only components moving away from / parallel to the solid edge pass (the 9-case switch
    /// @0x1ea7d); then the destination tile's object group is overlap-tested (fcn.0001f07e,
    /// point-in-square, MapWidth/2, no margin). Axis fallback (dx,dz) → one axis → other
    /// (fcn.0001e650) produces the wall slide.
    /// </summary>
    Vector3 FilterCollision(Vector3 worldVel)
    {
        var detector = TryResolve<ICollisionManager>();
        if (detector == null)
            return worldVel;

        var pos = _camera.Position;
        var map = TryResolve<IMapManager>()?.Current;
        float ts = map?.TileSize.X ?? 512f;
        float px = pos.X / ts, pz = pos.Z / ts; // tile units

        const int PartyClass = 0; // word[0x153ccc]: only ever written 0

        // Remake-only escape hatch: if the current TILE is already solid (bad spawn, stale
        // save) the original would wedge; let the player walk out instead.
        if (detector.IsTileBlocked((int)MathF.Floor(px), (int)MathF.Floor(pz), PartyClass))
            return worldVel;

        // Proposed delta = this frame's actual move (velocity × real frame dt), like the
        // original's one-step endpoint test.
        float dx = worldVel.X * _frameDt;
        float dz = worldVel.Z * _frameDt;
        if (dx == 0f && dz == 0f)
            return worldVel;

        float marginTiles = MathF.Max(ts / 4f, 50f) / ts; // MAX(tileSize/4, 50) world units

        bool StepAllowed(float sdx, float sdz)
            => Collision3DStep.IsAllowed(detector, px, pz, sdx, sdz, marginTiles, PartyClass);

        // fcn.0001e650: full move, then single axes — first axis picked by the original's
        // raw signed dx > dz comparison (a Watcom quirk kept for fidelity).
        if (StepAllowed(dx, dz))
            return worldVel;

        bool xFirst = dx > dz;
        if (xFirst)
        {
            if (dx != 0f && StepAllowed(dx, 0f)) return new Vector3(worldVel.X, 0f, 0f);
            if (dz != 0f && StepAllowed(0f, dz)) return new Vector3(0f, 0f, worldVel.Z);
        }
        else
        {
            if (dz != 0f && StepAllowed(0f, dz)) return new Vector3(0f, 0f, worldVel.Z);
            if (dx != 0f && StepAllowed(dx, 0f)) return new Vector3(worldVel.X, 0f, 0f);
        }

        return Vector3.Zero;
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
