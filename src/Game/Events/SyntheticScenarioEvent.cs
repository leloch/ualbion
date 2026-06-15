using UAlbion.Api.Eventing;

namespace UAlbion.Game.Events;

/// <summary>
/// Warp into a named synthetic scenario (a parameterised new game — no save file needed).
/// Handled by GameState, which looks the name up in <c>SyntheticScenarioLibrary</c>. Drivable
/// from the cockpit, the console, and the HTTP harness (`/event/raw synth_scenario jirinaar`).
/// </summary>
[Event("synth_scenario", "Warp into a synthetic scenario by name (no save file needed).")]
public class SyntheticScenarioEvent : GameEvent
{
    public SyntheticScenarioEvent(string name) => Name = name;
    [EventPart("name", "The scenario key, e.g. jirinaar / drinno / finale")] public string Name { get; }
}
