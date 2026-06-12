using UAlbion.Api.Eventing;

namespace UAlbion.Game.Events;

/// <summary>
/// Continuous world-axis camera movement in tile units (no yaw transform applied by the
/// receiver). Raised by Movement3D after sub-tile collision filtering so individual world
/// axes can be blocked independently (wall sliding).
/// </summary>
[Event("camera_move_3d_world")]
public class CameraMove3DWorldEvent : Event, IVerboseEvent
{
    [EventConstructor]
    public CameraMove3DWorldEvent(float x, float z) { X = x; Z = z; }
    [EventPart("x")] public float X { get; }
    [EventPart("z")] public float Z { get; }
}
