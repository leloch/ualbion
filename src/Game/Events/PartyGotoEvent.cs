using UAlbion.Api.Eventing;

namespace UAlbion.Game.Events;

/// <summary>
/// Auto-walk the party to a tile, exactly as if the player had clicked there: A* pathing on
/// 2D maps (SelectionHandler2D), BFS pathing + smooth glide on 3D maps (PartyGoto3D). Used by
/// the HTTP harness to play the game organically (walk through doors, onto event zones, to
/// NPCs) instead of teleporting.
/// </summary>
[Event("party_goto", "Auto-walk the party to the given tile (pathfinds; 2D and 3D maps).")]
public class PartyGotoEvent : GameEvent
{
    PartyGotoEvent() { }
    public PartyGotoEvent(int x, int y) { X = x; Y = y; }
    [EventPart("x")] public int X { get; private set; }
    [EventPart("y")] public int Y { get; private set; }
}
