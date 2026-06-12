using System.Numerics;
using UAlbion.Api.Eventing;

namespace UAlbion.Game.Events;

[Event("party_move_3d")]
public class PartyMove3DEvent : Event, IVerboseEvent
{
    public PartyMove3DEvent() { } // Cached-instance path used by Normal3DMouseMode

    [EventConstructor]
    public PartyMove3DEvent(float x, float y) => Velocity = new Vector2(x, y);

    public Vector2 Velocity { get; set; }
    public float Yaw { get; set; }
    public float Pitch { get; set; }

    // Text-parseable parts so the console / HTTP harness can drive 3D movement,
    // e.g. "party_move_3d 0 1" steps the party forward one input pulse.
    [EventPart("x")] public float X => Velocity.X;
    [EventPart("y")] public float Y => Velocity.Y;
}
