using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using UAlbion.Config;

namespace UAlbion.Game.Veldrid.Diag.Cockpit;

/// <summary>
/// Loads playthrough scenario presets from <c>$(CONFIG)/cockpit_scenarios.json</c>. The file is
/// hand-editable (no recompile needed) and uses the same event vocabulary as the HTTP harness.
/// If the file is missing it writes a starter template with built-in defaults so there is always
/// something to edit. Tolerant: a missing/garbled file falls back to the built-in defaults and
/// never throws.
/// </summary>
public static class ScenarioPresetLoader
{
    public const string ConfigPath = "$(CONFIG)/cockpit_scenarios.json";

    static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// <summary>Built-in defaults — guaranteed-valid commands the user can replace.</summary>
    public static ScenarioPresetFile BuiltInDefaults() => new()
    {
        Scenarios =
        [
            new ScenarioPreset
            {
                Name = "Load save 2 (Warniak fight)",
                Description = "Auto-starts a combat on load — quick way into the battle UI.",
                Events = ["load_game 2"]
            },
            new ScenarioPreset
            {
                Name = "Load save 7",
                Description = "Mid-intro save. Replace these presets with your own chapter warps.",
                Events = ["load_game 7"]
            },
            new ScenarioPreset
            {
                Name = "Example — load + gift gold (edit me)",
                Description =
                    "Template showing the macro form: each line is an event command in harness/console " +
                    "syntax, fired in order. Edit $(CONFIG)/cockpit_scenarios.json to build real chapter warps " +
                    "(load_map / teleport / add_party_member / change_item / word_known / switch / clock).",
                Events = ["load_game 7", "modify_gold AddAmount 5000"]
            }
        ]
    };

    /// <summary>
    /// Resolve, load and return the presets plus a human-readable source description for the UI.
    /// Writes the default template if the file is absent.
    /// </summary>
    public static (List<ScenarioPreset> Presets, string Source) Load(IPathResolver pathResolver)
    {
        string path;
        try { path = pathResolver?.ResolvePathAbsolute(ConfigPath); }
        catch (Exception ex) { return (BuiltInDefaults().Scenarios, $"built-in (path resolve failed: {ex.Message})"); }

        if (string.IsNullOrEmpty(path))
            return (BuiltInDefaults().Scenarios, "built-in (no path resolver)");

        try
        {
            if (!File.Exists(path))
            {
                TryWriteTemplate(path);
                return (BuiltInDefaults().Scenarios, $"built-in (template written to {path})");
            }

            var json = File.ReadAllText(path);
            var file = JsonSerializer.Deserialize<ScenarioPresetFile>(json, ReadOptions);
            if (file?.Scenarios == null || file.Scenarios.Count == 0)
                return (BuiltInDefaults().Scenarios, $"built-in (empty file at {path})");

            return (file.Scenarios, path);
        }
        catch (Exception ex)
        {
            return (BuiltInDefaults().Scenarios, $"built-in (read error: {ex.Message})");
        }
    }

    static void TryWriteTemplate(string path)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonSerializer.Serialize(BuiltInDefaults(), WriteOptions));
        }
        catch { /* best-effort; non-fatal if the config dir isn't writable */ }
    }
}
