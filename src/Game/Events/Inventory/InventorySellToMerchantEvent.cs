using UAlbion.Api.Eventing;
using UAlbion.Formats.Assets.Inv;

namespace UAlbion.Game.Events.Inventory;

// Player sells one unit of an owned item to the active merchant (the backpack "Sell" option).
// Carries the target merchant inventory so the InventoryManager can credit gold + deposit the ware.
[Event("inv:sell_to_merchant")]
public class InventorySellToMerchantEvent : InventorySlotEvent
{
    // Param order matches EventPart collection order (derived parts first, then base id/slot
    // — see InventoryDiscardEvent), otherwise the text-event parser maps the wrong slots.
    [EventPart("merchant")] public InventoryId Merchant { get; }

    public InventorySellToMerchantEvent(InventoryId merchant, InventoryId id, ItemSlotId slotId)
        : base(id, slotId) => Merchant = merchant;
}
