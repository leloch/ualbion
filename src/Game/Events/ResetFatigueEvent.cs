using UAlbion.Api.Eventing;

namespace UAlbion.Game.Events;

/// <summary>
/// Resets the party's hours-awake counter (MAIN.EXE global 0x153cd2) — raised by the
/// Recuperation spell, which acts as a magical full rest.
/// </summary>
[Event("reset_fatigue")]
public class ResetFatigueEvent : GameEvent
{
}
