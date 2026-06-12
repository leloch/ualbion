using UAlbion.Api.Eventing;

namespace UAlbion.Game.Events;

/// <summary>
/// Opens the Teleporter spell's destination picker — the current 3D map's automap
/// markers (goto-points); choosing one jumps the party there.
/// </summary>
[Event("teleporter_menu")]
public class ShowTeleporterMenuEvent : GameEvent
{
}
