using System;
using System.Numerics;
using ImGuiNET;

namespace UAlbion.Game.Veldrid.Diag.Cockpit;

/// <summary>Tab F — Event/Chain firehose + crash guard. Renders the window's live log ring buffer
/// (fed from ILogExchange), filterable, with errors and unhandled Unk*/QueryType opcodes
/// red-flagged — the early-warning board for the back-half run's known map-load crash risk.</summary>
public sealed class CockpitFirehosePanel
{
    readonly PlaythroughCockpitWindow _owner;
    readonly byte[] _filter = new byte[128];
    bool _flaggedOnly;

    public CockpitFirehosePanel(PlaythroughCockpitWindow owner) => _owner = owner;

    public void Draw()
    {
        if (ImGui.Button("Clear")) _owner.ClearFirehose();
        ImGui.SameLine();
        ImGui.Checkbox("Flagged only", ref _flaggedOnly);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(180);
        ImGui.InputText("filter##fh", _filter, (uint)_filter.Length);
        string filter = PlaythroughCockpitWindow.ReadBuffer(_filter);

        ImGui.TextDisabled("Red = error / unhandled Unk*/QueryType (possible map-load crash).");
        ImGui.Separator();

        ImGui.BeginChild("fh_scroll", new Vector2(0, 0), ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar);
        foreach (var entry in _owner.FirehoseSnapshot())
        {
            if (_flaggedOnly && !entry.Flagged)
                continue;
            if (!string.IsNullOrEmpty(filter) && entry.Message.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            if (entry.Flagged)
                ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), entry.Message);
            else
                ImGui.TextUnformatted(entry.Message);
        }
        if (ImGui.GetScrollY() >= ImGui.GetScrollMaxY())
            ImGui.SetScrollHereY(1.0f);
        ImGui.EndChild();
    }
}
