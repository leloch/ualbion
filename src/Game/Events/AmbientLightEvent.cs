using UAlbion.Api.Eventing;

namespace UAlbion.Game.Events;

/// <summary>Adjusts the 3D dungeon's ambient light level (the Light spell line).
/// The level rides the ETM Properties uniform (uAmbient) into the fragment shader.</summary>
[Event("ambient_light", "Adjust the dungeon ambient light level by a delta")]
public class AmbientLightEvent : GameEvent
{
    [EventConstructor]
    public AmbientLightEvent(int delta) => Delta = delta;
    [EventPart("delta")] public int Delta { get; }
}
