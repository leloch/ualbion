using UAlbion.Api.Eventing;

namespace UAlbion.Game.Events;

// Fires the canonical end-of-game sequence: the Endgame FLICs (the Seed detonation / outro /
// credits) followed by a terminal return to the main menu — the game's "you have won" state.
// Raised by the combat layer when the final boss surrenders (CombatResult.Surrender) and
// available as a script opcode ("game_complete") so the boss map's ending chain can trigger it
// too. (B3 — see _TODO_100PCT.md.)
[Event("game_complete", "Plays the endgame sequence and returns to the main menu (victory terminal)")]
public class GameCompleteEvent : GameEvent { }
