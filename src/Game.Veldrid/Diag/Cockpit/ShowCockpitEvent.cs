using UAlbion.Api.Eventing;

namespace UAlbion.Game.Veldrid.Diag.Cockpit;

/// <summary>
/// Opens the Playthrough Test Cockpit dev window, enabling the debug overlay first if needed.
/// Bound to a hotkey in input.json and reachable from the HTTP harness (`/event/raw show_cockpit`).
/// Handled by AlbionRenderSystem, which owns the debug-mode swap and the ImGui window list.
/// </summary>
[Event("show_cockpit", "Open the Playthrough Test Cockpit dev window (enables the debug overlay).")]
public class ShowCockpitEvent : Event { }
