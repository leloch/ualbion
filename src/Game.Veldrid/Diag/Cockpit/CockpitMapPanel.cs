using ImGuiNET;

namespace UAlbion.Game.Veldrid.Diag.Cockpit;

/// <summary>Tab E — Map / World jump. Read current map + leader position; teleport/load by id;
/// toggle 3D noclip.</summary>
public sealed class CockpitMapPanel
{
    readonly PlaythroughCockpitWindow _owner;
    int _mapId;
    int _x = 1;
    int _y = 1;

    public CockpitMapPanel(PlaythroughCockpitWindow owner) => _owner = owner;

    public void Draw()
    {
        var state = _owner.State;
        if (state is not { Loaded: true })
        {
            ImGui.TextDisabled("No game loaded.");
            return;
        }

        ImGui.Text($"Current map: {state.MapId}");
        var leader = _owner.Party?.Leader;
        if (leader != null)
        {
            var p = leader.GetPosition();
            ImGui.Text($"Leader pos: ({p.X:F1}, {p.Y:F1}, {p.Z:F1})");
        }
        ImGui.Separator();

        ImGui.SetNextItemWidth(100); ImGui.InputInt("Map##tp", ref _mapId); if (_mapId < 0) _mapId = 0;
        ImGui.SetNextItemWidth(90);  ImGui.InputInt("X##tp", ref _x); if (_x < 0) _x = 0;
        ImGui.SameLine();
        ImGui.SetNextItemWidth(90);  ImGui.InputInt("Y##tp", ref _y); if (_y < 0) _y = 0;

        if (ImGui.Button("Teleport")) _owner.RunCommand($"teleport {_mapId} {_x} {_y}");
        ImGui.SameLine();
        if (ImGui.Button("Load map")) _owner.RunCommand($"load_map {_mapId}");

        ImGui.TextDisabled("Note: in-session cross-map load/teleport can crash (pre-existing engine");
        ImGui.TextDisabled("fragility, B6). Prefer a Scenario warp via load_game for a clean state.");

        ImGui.Separator();
        if (ImGui.Button("Toggle noclip (3D)")) _owner.RunCommand("noclip");
    }
}
