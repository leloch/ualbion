using UAlbion.Api.Eventing;
using UAlbion.Formats.Ids;

namespace UAlbion.Game.Combat;

/// <summary>
/// Records a single character's choice for the next combat round. Raised by the per-tile
/// context menu (<c>LogicalCombatTile</c>) when the player picks an action. <c>Battle</c>
/// listens, stores the choice keyed by <see cref="Actor"/>, and applies it when the round
/// begins. Characters who haven't chosen by round-start fall through to the auto-attack
/// default — matches Albion's original "leave defaults" behaviour.
/// </summary>
/// <param name="Actor">The combatant whose turn this records.</param>
/// <param name="Action">Which action kind to perform (Melee/Cast/Move/Retreat/etc.).</param>
/// <param name="TargetTile">Target tile index 0..29 in the 5×6 combat grid, or
/// <c>-1</c> for actions that don't require an explicit target (Retreat, Defend).</param>
[Event("queue_combat_action")]
public record QueueCombatActionEvent(
    [property: EventPart("actor")] SheetId Actor,
    [property: EventPart("action")] CombatAction Action,
    [property: EventPart("target", true, -1)] int TargetTile) : EventRecord;
