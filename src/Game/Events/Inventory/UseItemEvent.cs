using UAlbion.Api.Eventing;
using UAlbion.Formats.Assets.Inv;

namespace UAlbion.Game.Events.Inventory;

/// <summary>The inventory context menu's generic "Use" verb: runs the item's UseItem
/// chain from the InventoryItems event set (torches, tools, quest items).</summary>
public class UseItemEvent : IEvent
{
    public InventorySlotId SlotId { get; }
    public UseItemEvent(InventorySlotId slotId) => SlotId = slotId;
    public void Format(IScriptBuilder builder) => builder?.Add(ScriptPartType.EventName, "inv:use_item");
}
