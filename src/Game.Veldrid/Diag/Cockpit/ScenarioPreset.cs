using System.Collections.Generic;

namespace UAlbion.Game.Veldrid.Diag.Cockpit;

/// <summary>
/// A named playthrough-test scenario: a description plus an ordered list of event commands
/// (in the same text form the HTTP harness `/event/raw` and the in-game console accept, e.g.
/// "load_game 7", "teleport 110 20 20 0", "add_party_member PartySheet.Tom"). Firing them in
/// order drops the game into an arbitrary state for testing without save-scumming.
/// </summary>
public sealed class ScenarioPreset
{
    public string Name { get; set; }
    public string Description { get; set; }
    public List<string> Events { get; set; } = new();
}

/// <summary>Root object of cockpit_scenarios.json.</summary>
public sealed class ScenarioPresetFile
{
    public List<ScenarioPreset> Scenarios { get; set; } = new();
}
