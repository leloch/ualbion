using ImGuiNET;
using UAlbion.Game.State;

namespace UAlbion.Game.Veldrid.Diag.Cockpit;

/// <summary>
/// Tab D — Quest-state board. Live reads of the progression state that gates the back half
/// (discovered words, current-map NPC active flags) plus write controls (Set/Clear/Toggle) for
/// switches, chests, doors and words. Writes go through the same event vocabulary as the harness.
/// v1: switch/chest/door/word writes are by-id (no per-id read-back yet — see plan §D).
/// </summary>
public sealed class CockpitQuestPanel
{
    readonly PlaythroughCockpitWindow _owner;
    int _switchId;
    int _chestId;
    int _doorId;

    public CockpitQuestPanel(PlaythroughCockpitWindow owner) => _owner = owner;

    public void Draw()
    {
        var state = _owner.State;
        if (state is not { Loaded: true })
        {
            ImGui.TextDisabled("No game loaded.");
            return;
        }

        if (ImGui.TreeNodeEx("Flags (write)", ImGuiTreeNodeFlags.DefaultOpen))
        {
            FlagRow("Switch", "switch", ref _switchId);
            FlagRow("Chest", "set_chest_open", ref _chestId);
            FlagRow("Door", "set_door_open", ref _doorId);
            ImGui.TreePop();
        }

        if (ImGui.TreeNodeEx("Discovered words", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var words = state.DiscoveredWords;
            ImGui.Text($"{words.Count} known");
            foreach (var w in words)
                ImGui.BulletText(w.ToString());
            ImGui.TreePop();
        }

        if (ImGui.TreeNode("NPCs on current map (active flags)"))
        {
            var mapId = state.MapId;
            ImGui.TextDisabled($"map {mapId}");
            for (int i = 0; i < state.Npcs.Count; i++)
            {
                var npc = state.Npcs[i];
                if (npc == null || npc.Id.IsNone)
                    continue;
                bool disabled = state.IsNpcDisabled(mapId, (byte)i);
                ImGui.Text($"#{i} {npc.Id} @({npc.X},{npc.Y}) {(disabled ? "[disabled]" : "active")}");
                ImGui.SameLine();
                if (ImGui.SmallButton($"{(disabled ? "Enable" : "Disable")}##npc{i}"))
                    _owner.RunCommand($"modify_npc_off {(disabled ? "Clear" : "Set")} {i} {mapId.Id}");
            }
            ImGui.TreePop();
        }
    }

    // id input + Set/Clear/Toggle, each firing "<command> <op> <id>".
    void FlagRow(string label, string command, ref int id)
    {
        ImGui.SetNextItemWidth(120);
        ImGui.InputInt($"{label} id##{command}", ref id);
        if (id < 0) id = 0;
        ImGui.SameLine();
        if (ImGui.SmallButton($"Set##{command}"))    _owner.RunCommand($"{command} Set {id}");
        ImGui.SameLine();
        if (ImGui.SmallButton($"Clear##{command}"))  _owner.RunCommand($"{command} Clear {id}");
        ImGui.SameLine();
        if (ImGui.SmallButton($"Toggle##{command}")) _owner.RunCommand($"{command} Toggle {id}");
    }
}
