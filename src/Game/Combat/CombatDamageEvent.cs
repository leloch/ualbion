using UAlbion.Api.Eventing;

namespace UAlbion.Game.Combat;

/// <summary>
/// Diagnostic/test hook: apply direct damage to the combatant on a combat tile (or, with
/// tile -1, to every living enemy) through the normal damage+death path. Used by the harness
/// to stage a deterministic kill so the post-combat loot pipeline can be verified without
/// having to fight through positioning/morale (which is faithful but un-driveable headlessly).
/// Does NOT change combat mechanics — it just injects damage the same way a strike would.
/// </summary>
[Event("combat_damage", "Diagnostic: apply direct damage to a combat tile (-1 = all enemies)")]
public class CombatDamageEvent : Event
{
    public CombatDamageEvent(int tile, int amount)
    {
        Tile = tile;
        Amount = amount;
    }

    [EventPart("tile")] public int Tile { get; }
    [EventPart("amount")] public int Amount { get; }
}
