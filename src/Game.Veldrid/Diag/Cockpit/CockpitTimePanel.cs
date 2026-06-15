using ImGuiNET;

namespace UAlbion.Game.Veldrid.Diag.Cockpit;

/// <summary>Tab G — Time &amp; environment. Read the clock; freeze/run it; rest or advance N hours
/// (exercises the per-hour-chain / bulk-advance path used by scripted time-skips).</summary>
public sealed class CockpitTimePanel
{
    readonly PlaythroughCockpitWindow _owner;
    int _hours = 1;

    public CockpitTimePanel(PlaythroughCockpitWindow owner) => _owner = owner;

    public void Draw()
    {
        var state = _owner.State;
        if (state is { Loaded: true })
            ImGui.Text($"Time: {state.Time:yyyy-MM-dd HH:mm}  (tick {state.TickCount})");
        else
            ImGui.TextDisabled("No game loaded.");

        ImGui.Separator();
        if (ImGui.Button("Toggle clock (freeze / run)")) _owner.RunCommand("toggle_clock");

        ImGui.SetNextItemWidth(100);
        ImGui.InputInt("Hours / cycles", ref _hours);
        if (_hours < 0) _hours = 0;
        ImGui.SameLine(); if (ImGui.SmallButton("Rest"))   _owner.RunCommand($"rest {_hours}");
        ImGui.SameLine(); if (ImGui.SmallButton("Update")) _owner.RunCommand($"update {_hours}");
        ImGui.TextDisabled("Rest = inn-style stay (heals + advances). Update = run N slow-clock cycles.");
    }
}
