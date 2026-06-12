using UAlbion.Api.Eventing;

namespace UAlbion.Game.Events;

/// <summary>
/// The city map-menu "Wait" action (RestMode 0): prompts for an hour count and advances
/// the game clock without any recovery. The original shows this in place of Rest on city
/// maps (popup builder 0x2204e).
/// </summary>
[Event("party_wait")]
public class PartyWaitEvent : GameEvent
{
}
