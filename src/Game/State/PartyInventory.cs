using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using UAlbion.Api.Eventing;
using UAlbion.Config;
using UAlbion.Core;
using UAlbion.Core.Visual;
using UAlbion.Formats.Assets.Inv;
using UAlbion.Formats.Ids;
using UAlbion.Formats.MapEvents;
using UAlbion.Game.Events.Inventory;
using UAlbion.Game.Events.Transitions;
using UAlbion.Game.State.Player;

namespace UAlbion.Game.State;

public class PartyInventory : Component
{
    readonly Func<InventoryId, Inventory> _getInventory;

    public PartyInventory(Func<InventoryId, Inventory> getInventory)
    {
        _getInventory = getInventory;
        On<InventoryTakeAllEvent>(TakeAll);
        On<ModifyGoldEvent>(e => ChangePartyItemAmount(AssetId.Gold, e.Operation, e.Amount));
        On<ModifyRationsEvent>(e => ChangePartyItemAmount(AssetId.Rations, e.Operation, e.Amount));
        // SCRIPT-01: honour the NumericOperation (Subtract must REMOVE, SetToMinimum clears) like
        // the sibling ChangeItemEvent — the old GiveToParty ignored it and always added.
        On<ModifyItemCountEvent>(e => ChangePartyItemAmount(e.ItemId, e.Operation, e.Amount));
        // INV-01: ChangeItemEvent is handled by GameState -> SheetApplier.ApplyItem (target-aware,
        // the faithful path that honours the event's TargetId incl. Everyone). A second handler
        // here applied it party-wide as well, doubling every change_item. Removed to apply once.

        On<SimpleChestEvent>(e =>
        {
            ItemId itemId = e.ChestType switch
            {
                SimpleChestItemType.Item => e.ItemId,
                SimpleChestItemType.Gold => AssetId.Gold,
                SimpleChestItemType.Rations => AssetId.Rations,
                _ => throw new System.ComponentModel.InvalidEnumArgumentException(nameof(e.ChestType), (int)e.ChestType, typeof(SimpleChestItemType))
            };

            var recipient = ChangePartyItemAmount(itemId, NumericOperation.AddAmount, e.Amount);
            if (recipient.HasValue)
                MapItemTransition(itemId, recipient.Value);
        });
    }

    void TakeAll(InventoryTakeAllEvent e)
    {
        var party = Resolve<IParty>();
        var inventoryManager = Resolve<IInventoryManager>();
        var chest = _getInventory(new InventoryId(e.ChestId));
        if (chest == null)
            return;

        var changedMembers = new HashSet<PartyMemberId>();
        foreach (var slot in chest.EnumerateAll())
        {
            if (slot.Item.IsNone) 
                continue;

            foreach (var member in party.WalkOrder)
            {
                ushort itemsGiven = inventoryManager.TryGiveItems(new InventoryId(member.Id), slot, null);
                if (itemsGiven > 0)
                    changedMembers.Add(member.Id);

                if (slot.Item.IsNone)
                    break;
            }
        }

        foreach(var memberId in changedMembers)
            Raise(new InventoryChangedEvent(new InventoryId(memberId)));
        Raise(new InventoryChangedEvent(new InventoryId(e.ChestId)));
    }

    static void SetLastResult(bool result) => ((EventContext)Context).LastEventResult = result;

    int GetTotalItemCount(ItemId itemId)
    {
        var im = Resolve<IInventoryManager>();
        var party = Resolve<IParty>();
        return party.WalkOrder.Sum(x => im.GetItemCount(new InventoryId(x.Id), itemId));
    }

    PartyMemberId? ChangePartyItemAmount(ItemId itemId, NumericOperation operation, ushort amount)
    {
        int currentTotal = GetTotalItemCount(itemId);
        int newTotal = operation.Apply(currentTotal, amount);
        var delta = newTotal - currentTotal;

        if (delta > 0)
            return GiveToParty(itemId, (ushort)delta);
        if (delta < 0)
            return TakeFromParty(itemId, (ushort)-delta);
        return null;
    }

    PartyMemberId? GiveToParty(ItemId itemId, ushort amount)
    {
        PartyMemberId? recipient = null;
        var party = Resolve<IParty>();
        var inventoryManager = Resolve<IInventoryManager>();
        var slot = new ItemSlot(new InventorySlotId(new InventoryId(InventoryType.Temporary, 0), 0));
        // Items CREATED by events (chest finds, change_used_item products, scripts) carry the
        // ITEMLIST template's initial charges — a lit torch arrives with its 3-hour lifetime,
        // a wand with its full charge count. Without this they arrive at 0 charges: torches
        // never burnt out and looted wands couldn't cast.
        byte charges = 0;
        if (itemId.Type == UAlbion.Config.AssetType.Item)
            charges = TryResolve<UAlbion.Formats.IAssetManager>()?.LoadItem(itemId)?.Charges ?? 0;
        slot.Set(itemId, amount, 0, charges);

        foreach (var member in party.WalkOrder)
        {
            ushort amountGiven = inventoryManager.TryGiveItems(new InventoryId(member.Id), slot, slot.Amount);

            if (amountGiven > 0)
            {
                Raise(new InventoryChangedEvent((InventoryId)member.Id));
                recipient = member.Id;
            }

            if (slot.Amount == 0)
                break;
        }
        SetLastResult(slot.Amount == 0);
        return recipient;
    }

    PartyMemberId? TakeFromParty(ItemId itemId, ushort amount)
    {
        var party = Resolve<IParty>();
        var inventoryManager = Resolve<IInventoryManager>();
        var slot = new ItemSlot(new InventorySlotId(new InventoryId(InventoryType.Temporary, 0), 0));
        PartyMemberId? donor = null;

        foreach (var member in party.WalkOrder)
        {
            ushort amountTaken = inventoryManager.TryTakeItems(new InventoryId(member.Id), slot, itemId, amount);
            amount -= amountTaken;

            if (amountTaken > 0)
            {
                Raise(new InventoryChangedEvent((InventoryId)member.Id));
                donor = member.Id;
            }

            if (amount == 0)
                break;
        }
        // INV-03: `slot` is the accumulating acceptor (grows as items are taken), so success is
        // `amount == 0` (full requested quantity removed), NOT slot.Amount == 0 (which inverts it).
        SetLastResult(amount == 0);
        return donor;
    }

    void MapItemTransition(ItemId itemId, PartyMemberId recipientId)
    {
        var context = (EventContext)Context;
        if (context?.Source.AssetId.Type != AssetType.Map)
            return;

        var map = TryResolve<IMapManager>()?.Current;
        var window = TryResolve<IGameWindow>();
        var party = TryResolve<IParty>();
        var camera = TryResolve<ICameraProvider>()?.Camera;

        if (map == null || window == null || party == null || camera == null)
            return;

        var player = party[recipientId];
        var worldPosition = new Vector3(context.Source.X, context.Source.Y, 0) * map.TileSize;
        var normPosition = camera.ProjectWorldToNorm(worldPosition);
        var uiPosition = window.NormToUi(new Vector2(normPosition.X, normPosition.Y));

        Raise(new LinearItemTransitionEvent(itemId,
            (int)uiPosition.X,
            (int)uiPosition.Y,
            (int)player.StatusBarUiPosition.X,
            (int)player.StatusBarUiPosition.Y,
            null));
    }
}
