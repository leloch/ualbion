using System;
using System.Numerics;
using UAlbion.Api.Eventing;
using UAlbion.Core.Events;
using UAlbion.Core.Visual;
using UAlbion.Formats.ScriptEvents;
using UAlbion.Game.Events;

namespace UAlbion.Game.Entities;

public class CameraMotion3D : Component
{
    readonly ICamera _camera;
    Vector3 _velocity;
    Vector3 _worldVelocity;

    public CameraMotion3D(ICamera camera)
    {
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        On<BeginFrameEvent>(_ => { _velocity = Vector3.Zero; _worldVelocity = Vector3.Zero; });
        On<CameraJumpEvent>(e =>
        {
            var map = TryResolve<IMapManager>()?.Current;
            if (map == null)
                return;

            _camera.Position = new Vector3(e.X * map.TileSize.X, map.BaseCameraHeight, e.Y * map.TileSize.Y);
        });

        On<CameraMoveEvent>(e =>
        {
            var map = Resolve<IMapManager>().Current;
            if (map == null)
                return;

            // The camera looks along -Z at yaw 0 (CalculateView transforms -UnitZ), so a
            // positive "forward" input must advance along -Z. Without the negation the
            // party walked AWAY from whatever the camera was showing.
            _velocity += new Vector3(e.X, 0, -e.Y) * map.TileSize;
        });

        On<CameraMove3DWorldEvent>(e =>
        {
            // World-axis movement in tile units; no yaw transform (Movement3D has already
            // rotated and collision-filtered the velocity per world axis).
            var map = Resolve<IMapManager>().Current;
            if (map == null)
                return;

            _worldVelocity += new Vector3(e.X, 0, e.Z) * map.TileSize;
        });

        On<CameraRotateEvent>(e =>
        {
            _camera.Yaw += e.Yaw;
            _camera.Pitch += e.Pitch;
        });

        On<EngineUpdateEvent>(OnEngineUpdate);
    }

    void OnEngineUpdate(EngineUpdateEvent e)
    {
        if (_velocity == Vector3.Zero && _worldVelocity == Vector3.Zero)
            return;

        var lookRotation = Quaternion.CreateFromYawPitchRoll(_camera.Yaw, 0f, 0f);
        _camera.Position += (Vector3.Transform(_velocity, lookRotation) + _worldVelocity) * e.DeltaSeconds;
    }
}