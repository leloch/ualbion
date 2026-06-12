using UAlbion.Api.Eventing;

namespace UAlbion.Game.Combat;

/// <summary>
/// Round-playback presentation: marks the combatant on TileIndex as the active one
/// (highlighted on the combat grid). TileIndex -1 clears. Raising this also clears any
/// hit feedback left over from the previous turn.
/// </summary>
public record CombatTurnHighlightEvent(int TileIndex) : EventRecord, IVerboseEvent;

/// <summary>
/// Round-playback presentation: damage/heal feedback for the occupant of a tile.
/// Amount 0 with Heal false is a miss (shown as "0" with no flash).
/// </summary>
public record CombatHitEvent(int TileIndex, int Amount, bool Killed, bool Heal) : EventRecord, IVerboseEvent;

/// <summary>
/// Round-playback presentation: a spell or magic item was cast (after SP gating).
/// CombatAudio maps the spell to its RE'd sample sequence.
/// </summary>
public record CombatCastEvent(UAlbion.Formats.Ids.SpellId SpellId) : EventRecord, IVerboseEvent;
