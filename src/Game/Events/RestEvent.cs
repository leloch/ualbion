using UAlbion.Api.Eventing;

namespace UAlbion.Game.Events;

[Event("rest", "Rest the party for the given number of hours (default 8)")]
public class RestEvent : GameEvent
{
    [EventConstructor]
    public RestEvent(int hours = 8) => Hours = hours;
    [EventPart("hours", true, 8)] public int Hours { get; }
}
