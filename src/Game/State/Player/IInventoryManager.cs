using UAlbion.Formats.Assets.Inv;
using UAlbion.Formats.Ids;

namespace UAlbion.Game.State.Player;

public interface IInventoryManager
{
    ReadOnlyItemSlot ItemInHand { get; }
    int ActiveMerchantPercent { get; } // per-shop buy/sell percent of the open merchant (100 = item Value)
    InventoryAction GetInventoryAction(InventorySlotId id);
    int GetItemCount(InventoryId id, ItemId item);
    ushort TryGiveItems(InventoryId id, ItemSlot donor, ushort? amount); // Return the number of items that were given
    ushort TryTakeItems(InventoryId id, ItemSlot acceptor, ItemId item, ushort? amount); // Return the number of items that were taken
    bool HasUseChain(ItemId itemId); // gates the context menu's generic "Use" verb
}