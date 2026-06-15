using System.Collections.Generic;
using ImGuiNET;
using UAlbion.Config;
using UAlbion.Game.State;

namespace UAlbion.Game.Veldrid.Diag.Cockpit;

/// <summary>Tab A — Scenario Warp. Jump into arbitrary game phases without real saves: the
/// built-in synthetic-scenario library (parameterised new games, crash-safe spawns) plus the
/// user-editable event-command presets from cockpit_scenarios.json.</summary>
public sealed class CockpitScenarioPanel
{
    readonly PlaythroughCockpitWindow _owner;
    List<ScenarioPreset> _presets;
    string _source = "(not loaded)";

    public CockpitScenarioPanel(PlaythroughCockpitWindow owner) => _owner = owner;

    void EnsureLoaded()
    {
        if (_presets != null) return;
        Reload();
    }

    void Reload()
    {
        var (presets, source) = ScenarioPresetLoader.Load(_owner.Service<IPathResolver>());
        _presets = presets;
        _source = source;
    }

    public void Draw()
    {
        EnsureLoaded();

        // --- Synthetic scenarios (no save file needed) — the headline feature ----------------
        ImGui.TextWrapped(
            "Synthetic scenarios build an arbitrary state (map + party + gold) on the fly and " +
            "load it like a save — no real save required. Spawn tiles are auto-validated.");
        if (ImGui.BeginChild("synth_list", new System.Numerics.Vector2(0, 220), ImGuiChildFlags.None))
        {
            var scenarios = SyntheticScenarioLibrary.BuiltIns();
            for (int i = 0; i < scenarios.Count; i++)
            {
                var s = scenarios[i];
                if (ImGui.Button($"Warp##synth{i}"))
                {
                    _owner.LogInfo($"[cockpit] synth warp '{s.Name}'");
                    _owner.RunCommand($"synth_scenario {s.Name}");
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip($"map {s.Map}, party {(s.Party?.Count ?? 1)}, gold {s.Gold}\nsynth_scenario {s.Name}");
                ImGui.SameLine();
                ImGui.Text(s.Name);
                if (!string.IsNullOrEmpty(s.Description))
                {
                    ImGui.Indent();
                    ImGui.TextDisabled(s.Description);
                    ImGui.Unindent();
                }
            }
        }
        ImGui.EndChild();

        ImGui.Separator();

        // --- Event-command presets (user-editable JSON; e.g. load_game) ----------------------
        if (ImGui.Button("Reload presets"))
            Reload();
        ImGui.SameLine();
        ImGui.TextDisabled($"presets: {_source}");
        ImGui.TextWrapped("Event-command presets (harness/console syntax) — edit cockpit_scenarios.json.");

        if (_presets.Count == 0)
        {
            ImGui.TextDisabled("No event presets.");
            return;
        }

        for (int i = 0; i < _presets.Count; i++)
        {
            var preset = _presets[i];
            // ##i keeps button ids unique even if two presets share a name.
            if (ImGui.Button($"Run##{i}"))
                RunScenario(preset);

            if (ImGui.IsItemHovered() && preset.Events != null)
                ImGui.SetTooltip(string.Join("\n", preset.Events));

            ImGui.SameLine();
            ImGui.Text(preset.Name ?? "(unnamed)");
        }
    }

    void RunScenario(ScenarioPreset preset)
    {
        if (preset?.Events == null) return;
        _owner.LogInfo($"[cockpit] preset '{preset.Name}' ({preset.Events.Count} events)");
        foreach (var command in preset.Events)
            _owner.RunCommand(command);
    }
}
