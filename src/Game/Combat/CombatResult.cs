namespace UAlbion.Game.Combat;

public enum CombatResult
{
    Victory,
    Retreat,
    PartyKilled,
    ExitGame,

    // The original's combat outcome 4: the end-game boss "asks for surrender" once the party
    // is mostly downed (see CombatFormulas.ShouldRequestSurrender / _RE_ASK_SURRENDER.md).
    // Like Victory/Retreat it ends the battle and continues the encounter's follow-on chain
    // (the scripted ending) — CombatManager only special-cases PartyKilled.
    Surrender
}