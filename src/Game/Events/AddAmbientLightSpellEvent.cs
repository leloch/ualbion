using UAlbion.Api.Eventing;

namespace UAlbion.Game.Events;

/// <summary>
/// Merges a Light cast into the ambient active-spell entry (RE 5C fcn.0006085d):
/// empty → {hours, pct}; active → hours accumulate, pct = max. Decays hourly.
/// </summary>
[Event("add_ambient_light")]
public class AddAmbientLightSpellEvent : GameEvent
{
    public AddAmbientLightSpellEvent(ushort hours, ushort percent)
    {
        Hours = hours;
        Percent = percent;
    }

    [EventPart("hours")] public ushort Hours { get; }
    [EventPart("pct")] public ushort Percent { get; }
}

/// <summary>Raised when the dungeon light inputs changed (Light cast / hourly decay) — the 3D renderer recomputes.</summary>
public class DungeonLightChangedEvent : GameEvent
{
}
