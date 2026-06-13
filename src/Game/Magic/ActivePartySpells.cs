namespace UAlbion.Game.Magic;

/// <summary>
/// World-scoped active spell flags (the original's SavedGame.ActiveSpells percentage
/// table — Light lives in slots 0..1, Levitation is tracked the same way).
/// Deliberate deviation (ledger §8): tracked as booleans without the original's percentage
/// decay; cleared on map change (a defensible approximation of the spell running out).
/// Light's real hourly decay is handled separately in the GameState/DungeonLighting path;
/// Levitation is confirmed dead in the original, so its decay is moot.
/// </summary>
public static class ActivePartySpells
{
    /// <summary>Levitation: the party floats over pit tiles (no-floor cells) in 3D maps.</summary>
    public static bool Levitating { get; set; }

    /// <summary>Reset on map change — active spells don't follow the party between maps.</summary>
    public static void OnMapChange() => Levitating = false;
}
