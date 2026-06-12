using UAlbion.Api.Eventing;

namespace UAlbion.Game.Events;

[Event("rest", "Rest the party. Hours 0 (default) = auto from map RestMode + time of day; an explicit count (e.g. inn stays) bypasses the map-mode gate.")]
public class RestEvent : GameEvent
{
    [EventConstructor]
    public RestEvent(int hours = 0) => Hours = hours;
    [EventPart("hours", true, 0)] public int Hours { get; }
}
