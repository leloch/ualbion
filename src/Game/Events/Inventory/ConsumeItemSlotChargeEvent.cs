using UAlbion.Api.Eventing;
using UAlbion.Formats.Assets.Inv;

namespace UAlbion.Game.Events.Inventory;

/// <summary>Consume one charge (or one of a stack) from a SPECIFIC inventory slot —
/// the wand/magic-item consumption rules applied to an exact slot rather than the
/// first matching item (which ConsumeItemChargeEvent does for combat).</summary>
public class ConsumeItemSlotChargeEvent : IEvent
{
    public InventorySlotId SlotId { get; }
    public ConsumeItemSlotChargeEvent(InventorySlotId slotId) => SlotId = slotId;
    public void Format(IScriptBuilder builder) => builder?.Add(ScriptPartType.EventName, "inv:consume_slot_charge");
}
