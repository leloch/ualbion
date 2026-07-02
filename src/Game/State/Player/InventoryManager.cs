using System;
using System.Linq;
using System.Numerics;
using UAlbion.Api;
using UAlbion.Api.Eventing;
using UAlbion.Config;
using UAlbion.Core;
using UAlbion.Formats.Assets;
using UAlbion.Formats.Assets.Inv;
using UAlbion.Formats.Assets.Maps;
using UAlbion.Formats.Assets.Sheets;
using UAlbion.Formats.Ids;
using UAlbion.Formats.MapEvents;
using UAlbion.Game.Events;
using UAlbion.Game.Events.Inventory;
using UAlbion.Game.Events.Transitions;
using UAlbion.Game.Input;
using UAlbion.Game.Text;

namespace UAlbion.Game.State.Player;

// TODO: Refactor / break this class up if possible
public class InventoryManager : GameServiceComponent<IInventoryManager>, IInventoryManager
{
    readonly Func<InventoryId, Inventory> _getInventory;
    readonly Func<ItemId, ItemData> _getItem;
    readonly ItemSlot _hand = new(new InventorySlotId(InventoryType.Temporary, 0, ItemSlotId.None));
    IEvent _returnItemInHandEvent;
    int _activeMerchantPercent = MerchantPricing.DefaultPercent; // per-shop buy/sell percent while a merchant is open
    public int ActiveMerchantPercent => _activeMerchantPercent;

    ItemSlot GetSlot(InventorySlotId id) => _getInventory(id.Id)?.GetSlot(id.Slot);
    public ReadOnlyItemSlot ItemInHand { get; }
    public InventoryManager(Func<InventoryId, Inventory> getInventory, Func<ItemId, ItemData> getItem)
    {
        _getInventory = getInventory ?? throw new ArgumentNullException(nameof(getInventory));
        _getItem = getItem ?? throw new ArgumentNullException(nameof(getItem));

        On<InventoryReturnItemInHandEvent>(_ => ReturnItemInHand());
        On<InventoryDestroyItemInHandEvent>(_ =>
        {
            if (_hand.Amount > 0)
                _hand.Amount--;
            ReturnItemInHand();
        });
        OnAsync<InventorySwapEvent>(OnSlotEvent);
        OnAsync<InventoryPickupEvent>(OnSlotEvent);
        On<InventoryGiveItemEvent>(OnGiveItem);
        On<InventorySellEvent>(OnBuyFromMerchant);
        On<InventorySellToMerchantEvent>(OnSellToMerchant);
        // Capture the active shop's per-shop buy/sell percent (PlaceActionEvent.Unk6 → MerchantEvent)
        // so buy/sell and the price hover use it; reset to default when the screen closes.
        On<MerchantEvent>(e => _activeMerchantPercent = e.PricePercent <= 0 ? MerchantPricing.DefaultPercent : e.PricePercent);
        On<InventoryCloseEvent>(_ => _activeMerchantPercent = MerchantPricing.DefaultPercent);
        OnAsync<InventoryDiscardEvent>(OnDiscard);
        On<SetInventorySlotUiPositionEvent>(OnSetSlotUiPosition);
        On<ActivateItemEvent>(OnActivateItem);
        On<ActivateItemSpellEvent>(OnActivateItemSpell);
        OnAsync<DrinkItemEvent>(OnDrinkItem);
        OnAsync<ReadItemEvent>(OnReadItem);
        On<ReadSpellScrollEvent>(OnReadSpellScroll);
        On<ConsumeItemChargeEvent>(OnConsumeCharge);
        On<ConsumeAmmoEvent>(OnConsumeAmmo);
        On<BreakInventorySlotEvent>(OnBreakSlot);
        On<RepairInventorySlotEvent>(OnRepairSlot);
        On<IdentifyInventorySlotEvent>(OnIdentifySlot);
        On<RechargeInventorySlotEvent>(OnRechargeSlot);
        On<DestroyCursedEquipmentEvent>(OnDestroyCursed);

        ItemInHand = new ReadOnlyItemSlot(_hand);
    }

    void ReturnItemInHand()
    {
        if (_returnItemInHandEvent == null || _hand.Item.IsNone)
            return;

        Receive(_returnItemInHandEvent, null);
    }

    void OnSetSlotUiPosition(SetInventorySlotUiPositionEvent e)
    {
        var inventory = _getInventory(e.InventorySlotId.Id);
        inventory.SetSlotUiPosition(e.InventorySlotId.Slot, new Vector2(e.X, e.Y));
    }

    public InventoryAction GetInventoryAction(InventorySlotId id)
    {
        var slot = GetSlot(id);
        if (slot == null || slot.Id.Slot == ItemSlotId.None)
            return InventoryAction.Nothing;

        // Merchant stock is never free to grab — buying goes through the "Sell" context option
        // (InventorySellEvent → OnBuyFromMerchant), which charges gold. Block the drag-pickup.
        if (id.Id.Type == InventoryType.Merchant)
            return InventoryAction.Nothing;

        return (_hand.Item.Type, slot.Item.Type) switch
        {
            (AssetType.None, AssetType.None) => InventoryAction.Nothing,
            (AssetType.None, _) => InventoryAction.Pickup,
            (AssetType.Gold, AssetType.Gold) => InventoryAction.Coalesce,
            (AssetType.Rations, AssetType.Rations) => InventoryAction.Coalesce,
            (AssetType.Gold, AssetType.None) =>
                id.Slot == ItemSlotId.Gold
                    ? InventoryAction.PutDown
                    : InventoryAction.Nothing,
            (AssetType.Rations, AssetType.None) => 
                id.Slot == ItemSlotId.Rations
                    ? InventoryAction.PutDown
                    : InventoryAction.Nothing,
            (AssetType.Item, AssetType.None) => InventoryAction.PutDown,
            (AssetType.Item, AssetType.Item) when slot.CanCoalesce(_hand, _getItem) =>
                slot.Amount >= ItemSlot.MaxItemCount
                    ? InventoryAction.NoCoalesceFullStack
                    : InventoryAction.Coalesce,
            (AssetType.Item, AssetType.Item) => InventoryAction.Swap,
            _ => InventoryAction.Nothing
        };
    }

    void Update(InventoryId id) => Raise(new InventoryChangedEvent(id));

    void PickupItem(ItemSlot slot, ushort? quantity)
    {
        if (!CanItemBeTaken(slot))
        {
            ShowCannotTakeMessage(slot);
            return;
        }

        _hand.TransferFrom(slot, quantity, _getItem);
        _returnItemInHandEvent = new InventorySwapEvent(slot.Id.Id, slot.Id.Slot);
    }

    void ShowCannotTakeMessage(ItemSlot slot)
    {
        // CanItemBeTaken currently only blocks on cursed equipped items; if that ever expands,
        // pick a more specific message here. Surface the same hover text the original engine
        // shows when you try to pull a cursed item off a character.
        if (slot.Item.Type != AssetType.Item)
            return;
        var tf = Resolve<ITextFormatter>();
        Raise(new HoverTextEvent(tf.Format(Base.SystemText.InvMsg_ThisItemIsCursed)));
    }

    bool DoesSlotAcceptItem(ICharacterSheet sheet, ItemSlotId slotId, ItemData item)
    {
        switch (sheet.Gender)
        {
            case Gender.Male: if ((item.AllowedGender & Genders.Male) == 0) return false; break;
            case Gender.Female: if ((item.AllowedGender & Genders.Female) == 0) return false; break;
            case Gender.Neuter: if ((item.AllowedGender & Genders.Neutral) == 0) return false; break;
        }

        if (!item.Class.IsAllowed(sheet.PlayerClass))
            return false;

        // if (!item.Races.IsAllowed(sheet.Races)) // Apparently never implemented in original game?
        //     return false;

        bool force = item.SlotType == ItemSlotId.RightHandOrTail && slotId is ItemSlotId.RightHand or ItemSlotId.Tail;
        if (item.SlotType != slotId && !force)
            return false;

        switch (slotId)
        {
            case ItemSlotId.LeftHand:
            {
                var rightHandId = sheet.Inventory.RightHand.Item;
                if (rightHandId.Type != AssetType.Item)
                    return false;

                var rightHandItem = _getItem(rightHandId);
                return rightHandItem.Hands <= 1;
            }
            case ItemSlotId.Tail:
                return
                    item.TypeId == ItemType.CloseRangeWeapon
                    && item.SlotType is ItemSlotId.Tail or ItemSlotId.RightHandOrTail;
            case ItemSlotId.RightHand:
                // INV-04: a two-handed weapon needs the off-hand free (symmetric with the
                // LeftHand check above). Without this the player could wield a 2H weapon AND a
                // shield and gain both bonuses.
                if (item.Hands > 1 && sheet.Inventory.LeftHand.Item.Type == AssetType.Item)
                    return false;
                return true;
            default:
                return true;
        }
    }

    bool DoesSlotAcceptItemInHand(InventoryId id, ItemSlotId slotId)
    {
        switch (_hand?.Item.Type ?? AssetType.None)
        {
            case AssetType.None: return true;
            case AssetType.Gold: return slotId == ItemSlotId.Gold;
            case AssetType.Rations: return slotId == ItemSlotId.Rations;
            case AssetType.Item when slotId < ItemSlotId.NormalSlotCount: return true;
            case AssetType.Item when id.Type != InventoryType.Player: return false;
            case AssetType.Item:
            {
                var item = _getItem(_hand!.Item);
                var state = Resolve<IGameState>();
                var sheet = state.GetSheet(id.ToSheetId());
                return DoesSlotAcceptItem(sheet, slotId, item);
            }

            default:
                throw new InvalidOperationException($"Unexpected item type in hand: {_hand?.GetType()}");
        }
    }

    ItemSlotId GetBestSlot(InventorySlotId id)
    {
        if (_hand.Item.Type == AssetType.Gold) return ItemSlotId.Gold;
        if (_hand.Item.Type == AssetType.Rations) return ItemSlotId.Rations;
        if (_hand.Item.Type != AssetType.Item) return id.Slot; // Shouldn't be possible

        if (id.Id.Type != InventoryType.Player || !id.Slot.IsBodyPart())
            return ItemSlotId.None;

        var state = Resolve<IGameState>();
        var sheet = state.GetSheet(id.Id.ToSheetId());
        var item = _getItem(_hand.Item);

        if (DoesSlotAcceptItem(sheet, id.Slot, item)) return id.Slot;
        if (DoesSlotAcceptItem(sheet, ItemSlotId.Head, item)) return ItemSlotId.Head;
        if (DoesSlotAcceptItem(sheet, ItemSlotId.Neck, item)) return ItemSlotId.Neck;
        if (DoesSlotAcceptItem(sheet, ItemSlotId.Tail, item)) return ItemSlotId.Tail;
        if (DoesSlotAcceptItem(sheet, ItemSlotId.RightHand, item)) return ItemSlotId.RightHand;
        if (DoesSlotAcceptItem(sheet, ItemSlotId.LeftHand, item)) return ItemSlotId.LeftHand;
        if (DoesSlotAcceptItem(sheet, ItemSlotId.Chest, item)) return ItemSlotId.Chest;
        if (DoesSlotAcceptItem(sheet, ItemSlotId.RightFinger, item)) return ItemSlotId.RightFinger;
        if (DoesSlotAcceptItem(sheet, ItemSlotId.LeftFinger, item)) return ItemSlotId.LeftFinger;
        if (DoesSlotAcceptItem(sheet, ItemSlotId.Feet, item)) return ItemSlotId.Feet;

        return ItemSlotId.None;
    }

    static bool CanItemBeTaken(ItemSlot slot)
    {
        // Vital plot items can be moved between members but never discarded (the
        // discard path blocks ItemFlags.PlotItem); cursed equipped gear can't be taken.
        switch (slot.Item.Type)
        {
            case AssetType.Gold:
            case AssetType.Rations: return true;
            case AssetType.Item:
                bool curseActive = (slot.Flags & ItemSlotFlags.Cursed) != 0 &&
                                   slot.Id.Slot.IsBodyPart();
                return !curseActive;
            default: return true;
        }
    }

    // Buy one unit of a merchant's wares (the merchant-slot "Sell" option = merchant sells to
    // the player). Charges pooled party gold at the item's Value (per-shop multiplier
    // RE-pending), gives the item to the leader, and only debits/decrements if it fit. This is
    // the core gold-loop fix — buying used to be a free pickup with no debit and the event had
    // no subscriber (B5).
    void OnBuyFromMerchant(InventorySellEvent e)
    {
        var merchant = _getInventory(e.Id);
        var slot = merchant?.GetSlot(e.SlotId);
        if (slot == null || slot.Item.Type != AssetType.Item || slot.Amount == 0)
            return;

        var item = _getItem(slot.Item);
        if (item == null)
            return;

        int price = MerchantPricing.Price(item.Value, _activeMerchantPercent);
        var party = TryResolve<IParty>();
        if (party == null)
            return;

        var tf = TryResolve<ITextFormatter>();
        if (party.TotalGold < price)
        {
            if (tf != null)
                Raise(new HoverTextEvent(tf.Format(Base.SystemText.Shop_ThePartyDoesNotHaveEnoughGold)));
            return;
        }

        // Hand the item to the leader; abort (no charge) if there's no room.
        var leaderInv = new InventoryId(party.Leader.Id);
        var donor = new ItemSlot(new InventorySlotId(InventoryType.Temporary, 0, ItemSlotId.None)) { Item = slot.Item, Amount = 1 };
        ushort given = TryGiveItems(leaderInv, donor, 1);
        if (given == 0)
            return;

        SpendPartyGold(price);
        // Stock 0xFF (ItemSlot.Unlimited) is the infinite-stock sentinel — never deplete it.
        if (slot.Amount != ItemSlot.Unlimited)
        {
            slot.Amount -= given;
            if (slot.Amount == 0)
                slot.Clear();
        }
        Update(e.Id);
        Update(leaderInv); // MERCH-02: refresh the buyer's inventory (TryGiveItems doesn't raise it)
    }

    // Sell one unit of an owned backpack item to the active merchant (the backpack "Sell"
    // option = player sells to merchant). Pays the per-shop price (same percent/formula as buy —
    // Albion has no buy/sell spread), deposits the ware into the merchant so it can be bought
    // back, and only removes/credits if the merchant had room. Mirror of OnBuyFromMerchant (B5).
    void OnSellToMerchant(InventorySellToMerchantEvent e)
    {
        if (e.Merchant.Type != InventoryType.Merchant)
            return;

        var slot = GetSlot(new InventorySlotId(e.Id, e.SlotId));
        if (slot == null || slot.Item.Type != AssetType.Item || slot.Amount == 0)
            return;

        var item = _getItem(slot.Item);
        if (item == null || (item.Flags & ItemFlags.PlotItem) != 0) // vital items can't be sold
            return;

        // Deposit one unit into the merchant first; abort (no payment) if there's no room.
        var merchantInv = e.Merchant;
        var donor = new ItemSlot(new InventorySlotId(InventoryType.Temporary, 0, ItemSlotId.None)) { Item = slot.Item, Amount = 1 };
        ushort taken = TryGiveItems(merchantInv, donor, 1);
        if (taken == 0)
            return;

        slot.Amount -= taken;
        if (slot.Amount == 0)
            slot.Clear();

        int price = MerchantPricing.Price(item.Value, _activeMerchantPercent);
        if (price > 0)
        {
            var party = TryResolve<IParty>();
            var leaderId = party?.Leader?.Id ?? PartyMemberId.None;
            if (!leaderId.IsNone)
                Raise(new DataChangeEvent(new TargetId(AssetType.PartyMember, leaderId.Id), ChangeProperty.Gold, NumericOperation.AddAmount, (ushort)price));
        }

        Update(e.Id);
        Update(merchantInv);
    }

    // Pooled party-gold spend, member by member (mirrors PlaceActionManager.TrySpendGold).
    void SpendPartyGold(int amount)
    {
        int remaining = amount;
        foreach (var member in Resolve<IParty>().StatusBarOrder)
        {
            if (remaining <= 0) break;
            int gold = member?.Effective?.Inventory?.Gold?.Amount ?? 0;
            if (gold <= 0) continue;
            int take = Math.Min(gold, remaining);
            Raise(new DataChangeEvent(new TargetId(AssetType.PartyMember, member.Id.Id), ChangeProperty.Gold, NumericOperation.SubtractAmount, (ushort)take));
            remaining -= take;
        }
    }

    void OnGiveItem(InventoryGiveItemEvent e)
    {
        var inventory = _getInventory((InventoryId)e.MemberId);
        switch (_hand.Item.Type)
        {
            case AssetType.Gold: inventory.Gold.TransferFrom(_hand, null, _getItem); break;
            case AssetType.Rations: inventory.Rations.TransferFrom(_hand, null, _getItem); break;
            case AssetType.Item:
            {
                var item = _getItem(_hand.Item);
                ItemSlot slot = null;
                if (item.IsStackable)
                    slot = inventory.BackpackSlots.FirstOrDefault(x => x.Item == _hand.Item);

                slot ??= inventory.BackpackSlots.FirstOrDefault(x => x.Item.IsNone);
                slot?.TransferFrom(_hand, null, _getItem);
                break;
            }

            default: return; // Unknown or null
        }

        Update(inventory.Id);
        SetCursor();
    }

    AlbionTask<int> GetQuantity(bool discard, IInventory inventory, ItemSlotId slotId)
    {
        var slot = inventory.GetSlot(slotId);
        if (slot.Amount == 1)
            return AlbionTask.FromResult(1);

        var text = (slot.Item.Type, discard) switch
        {
            (AssetType.Gold, true) => Base.SystemText.Gold_ThrowHowMuchGoldAway,
            (AssetType.Gold, false) => Base.SystemText.Gold_TakeHowMuchGold,
            (AssetType.Rations, true) => Base.SystemText.Gold_ThrowHowManyRationsAway,
            (AssetType.Rations, false) => Base.SystemText.Gold_TakeHowManyRations,
            (AssetType.Item, true) => Base.SystemText.InvMsg_ThrowHowManyItemsAway,
            (AssetType.Item, false) => Base.SystemText.InvMsg_TakeHowManyItems,
            var x => throw new InvalidOperationException($"Unexpected item contents {x}")
        };

        var (sprite, subId, _) = GetSprite(slot.Item);
        var promptEvent = new ItemQuantityPromptEvent(new StringId(text), sprite, subId, slot.Amount, slotId == ItemSlotId.Gold);
        return RaiseQueryA(promptEvent);

        /* if (RaiseAsync(, continuation) == 0)
        {
            ApiUtil.Assert("ItemManager.GetQuantity tried to open a quantity dialog, but no-one was listening for the event.");
            return 0;
        } */
    }

    (SpriteId sprite, int subId, int frameCount) GetSprite(ItemId id)
    {
        switch (_hand.Item.Type)
        {
            case AssetType.None: return (AssetId.None, 0, 1);
            case AssetType.Gold: return (Base.CoreGfx.UiGold, 0, 1);
            case AssetType.Rations: return (Base.CoreGfx.UiFood, 0, 1);
            case AssetType.Item:
                {
                    var item = _getItem(_hand.Item);
                    return (item.Icon, item.IconSubId, item.IconAnim);
                }
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(id),
                    id,
                    $"{id} was expected to be None, Gold, Rations or an Item");
        }
    }

    async AlbionTask OnSlotEvent(InventorySlotEvent e)
    {
        var slotId = new InventorySlotId(e.Id, e.SlotId);
        bool redirected = false;

        if (!DoesSlotAcceptItemInHand(e.Id, e.SlotId))
        {
            slotId = new InventorySlotId(slotId.Id, GetBestSlot(slotId));
            redirected = true;
        }

        if (slotId.Slot is ItemSlotId.None or ItemSlotId.CharacterBody)
            return;

        Inventory inventory = _getInventory(slotId.Id);
        ItemSlot slot = inventory?.GetSlot(slotId.Slot);
        if (slot == null)
            return;

        var cursorManager = Resolve<ICursorManager>();
        var window = Resolve<IGameWindow>();
        var cursorUiPosition = window.PixelToUi(cursorManager.Position);

        switch (GetInventoryAction(slotId))
        {
            case InventoryAction.Pickup:
                {
                    if (slot.Amount == 1)
                    {
                        PickupItem(slot, null);
                    }
                    else if (e is InventoryPickupEvent pickup)
                    {
                        PickupItem(slot, pickup.Amount);
                    }
                    else
                    {
                        var quantity = await GetQuantity(false, inventory, e.SlotId);
                        if (quantity > 0)
                            PickupItem(slot, (ushort)quantity);
                    }
                    break;
                }

            case InventoryAction.PutDown:
            {
                // #61: the off-hand (LeftHand) holds at most ONE ammunition item (the original caps
                // it for ranged weapons); the rest stays in the backpack. Non-ammo transfers in full.
                var heldData = _hand.Item.Type == AssetType.Item ? _getItem(_hand.Item) : null;
                ushort? putQuantity = (slotId.Slot == ItemSlotId.LeftHand && heldData?.TypeId == ItemType.Ammo)
                    ? (ushort)1
                    : null;

                if (!redirected)
                {
                    slot.TransferFrom(_hand, putQuantity, _getItem);
                }
                else
                {
                    var transitionEvent = new LinearItemTransitionEvent(
                        _hand.Item,
                        (int)cursorUiPosition.X,
                        (int)cursorUiPosition.Y,
                        (int)slot.LastUiPosition.X,
                        (int)slot.LastUiPosition.Y,
                        ReadVar(V.Game.Ui.Transitions.ItemMovementTransitionTimeSeconds));

                    ItemSlot temp = new(new InventorySlotId(InventoryType.Temporary, 0, 0));
                    temp.TransferFrom(_hand, putQuantity, _getItem);
                    SetCursor();
                    await RaiseA(transitionEvent);

                    slot.TransferFrom(temp, null, _getItem);
                }

                break;
            }

            case InventoryAction.Swap:
            {
                if (!redirected)
                {
                    SwapItems(slot);
                }
                else
                {
                    // Original game didn't handle this, but doesn't hurt.
                    var transitionEvent1 = new LinearItemTransitionEvent(
                        _hand.Item,
                        (int)cursorUiPosition.X,
                        (int)cursorUiPosition.Y,
                        (int)slot.LastUiPosition.X,
                        (int)slot.LastUiPosition.Y,
                        ReadVar(V.Game.Ui.Transitions.ItemMovementTransitionTimeSeconds));

                    var transitionEvent2 = new LinearItemTransitionEvent(
                        slot.Item,
                        (int)slot.LastUiPosition.X,
                        (int)slot.LastUiPosition.Y,
                        (int)cursorUiPosition.X,
                        (int)cursorUiPosition.Y,
                        ReadVar(V.Game.Ui.Transitions.ItemMovementTransitionTimeSeconds));

                    ItemSlot temp1 = new(new InventorySlotId(InventoryType.Temporary, 0, 0));
                    ItemSlot temp2 = new(new InventorySlotId(InventoryType.Temporary, 0, 0));
                    temp1.TransferFrom(_hand, null, _getItem);
                    temp2.TransferFrom(slot, null, _getItem);

                    var transition1 = RaiseA(transitionEvent1);
                    var transition2 = RaiseA(transitionEvent2);

                    await transition1;
                    await transition2;
                    slot.TransferFrom(temp1, null, _getItem);
                    _hand.TransferFrom(temp2, null, _getItem);
                    _returnItemInHandEvent = new InventorySwapEvent(slot.Id.Id, slot.Id.Slot);
                }

                break;
            }

            // Shouldn't be possible for this to be a redirect as redirects only happen between body parts and they don't allow stacks.
            case InventoryAction.Coalesce:
                {
                    if (e is InventoryPickupEvent pickup)
                        PickupItem(slot, pickup.Amount);
                    else
                        CoalesceItems(slot);
                    break;
                }
            case InventoryAction.NoCoalesceFullStack: break; // No-op
        }

        Update(slotId.Id);
        SetCursor();
    }

    async AlbionTask OnDiscard(InventoryDiscardEvent e)
    {
        var inventory = _getInventory(e.Id);

        // Vital plot items (ItemFlags.PlotItem — the Goddess' amulet, quest keys etc)
        // can never be thrown away: losing one would soft-lock the game.
        var discardSlot = inventory.GetSlot(e.SlotId);
        if (discardSlot?.Item.Type == AssetType.Item)
        {
            var itemData = _getItem(discardSlot.Item);
            if (itemData != null && (itemData.Flags & ItemFlags.PlotItem) != 0)
            {
                var tf = Resolve<ITextFormatter>();
                Raise(new DescriptionTextEvent(tf.Format(Base.SystemText.InvMsg_ThisIsAVitalItem)));
                return;
            }
        }

        var quantity = await GetQuantity(true, inventory, e.SlotId);

        if (quantity <= 0)
        {
            return;
        }

        var slot = inventory.GetSlot(e.SlotId);
        ushort itemsToDrop = Math.Min((ushort)quantity, slot.Amount);

        var prompt = slot.Item.Type switch
        {
            AssetType.Gold => Base.SystemText.Gold_ReallyThrowTheGoldAway,
            AssetType.Rations => Base.SystemText.Gold_ReallyThrowTheRationsAway,
            AssetType.Item when itemsToDrop == 1 => Base.SystemText.InvMsg_ReallyThrowThisItemAway,
            _ => Base.SystemText.InvMsg_ReallyThrowTheseItemsAway,
        };

        var response = await RaiseQueryA(new YesNoPromptEvent(prompt));

        if (!response)
            return;

        if (!slot.Item.IsNone)
        {
            var maxTransitions = ReadVar(V.Game.Ui.Transitions.MaxDiscardTransitions);
            var transitionsToShow = Math.Min(itemsToDrop, maxTransitions);

            for (int i = 0; i < transitionsToShow; i++)
                _ = RaiseA(new GravityItemTransitionEvent(slot.Item, e.NormX, e.NormY));
        }

        slot.Amount -= itemsToDrop;
        Update(e.Id);
        SetCursor();
    }

    void SetCursor()
    {
        var (sprite, subId, frameCount) = GetSprite(_hand.Item);
        Raise(new SetCursorEvent(_hand.Item.IsNone ? Base.CoreGfx.Cursor : Base.CoreGfx.CursorSmall));
        Raise(new SetHeldItemCursorEvent(sprite, subId, frameCount, _hand.Amount, _hand.Item == AssetId.Gold));
    }

    void CoalesceItems(ItemSlot slot)
    {
        ApiUtil.Assert(slot.CanCoalesce(_hand, _getItem));
        ApiUtil.Assert(slot.Amount < ItemSlot.MaxItemCount || slot.Item.Type is AssetType.Gold or AssetType.Rations);
        slot.TransferFrom(_hand, null, _getItem);
    }

    void SwapItems(ItemSlot slot)
    {
        // Check if the item can be taken
        if (!CanItemBeTaken(slot))
        {
            ShowCannotTakeMessage(slot);
            return;
        }

        _hand.Swap(slot);
        _returnItemInHandEvent = new InventorySwapEvent(slot.Id.Id, slot.Id.Slot);
    }

    // void RaiseStatusMessage(TextId textId) => Raise(new DescriptionTextEvent(Resolve<ITextFormatter>().Format(textId)));

    public int GetItemCount(InventoryId id, ItemId item) => _getInventory(id).EnumerateAll().Where(x => x.Item == item).Sum(x => (int?)x.Amount) ?? 0;
    public ushort TryGiveItems(InventoryId id, ItemSlot donor, ushort? amount)
    {
        ArgumentNullException.ThrowIfNull(donor);

        ushort totalTransferred = 0;
        ushort remaining = amount ?? ushort.MaxValue;

        // Carry-weight cap for party members: don't accept more than fits under
        // MaxWeight (Strength × grams-per-STR; the Effective sheet tracks totals).
        if (id.Type == InventoryType.Player && donor.Item.Type == AssetType.Item)
        {
            var member = TryResolve<IParty>()?.StatusBarOrder
                ?.FirstOrDefault(x => x.Id.Id == id.Id);
            var eff = member?.Effective;
            var itemData = _getItem(donor.Item);
            if (eff != null && itemData != null && itemData.Weight > 0)
            {
                long spare = (long)eff.MaxWeight - eff.TotalWeight;
                int fit = (int)Math.Max(0, spare / itemData.Weight);
                if (fit < remaining)
                    remaining = (ushort)fit;
                if (remaining == 0)
                    return 0;
            }
        }

        var inventory = _getInventory(id);

        if (donor.Item == AssetId.Gold)
            return inventory.Gold.TransferFrom(donor, remaining, _getItem);

        if (donor.Item == AssetId.Rations)
            return inventory.Rations.TransferFrom(donor, remaining, _getItem);

        for (int i = 0; i < (int)ItemSlotId.NormalSlotCount && amount != 0; i++)
        {
            if (!inventory.Slots[i].CanCoalesce(donor, _getItem))
                continue;

            ushort transferred = inventory.Slots[i].TransferFrom(donor, remaining, _getItem);
            remaining -= transferred;
            totalTransferred += transferred;
            if (remaining == 0 || donor.Item.IsNone)
                break;
        }

        return totalTransferred;
    }

    public ushort TryTakeItems(InventoryId id, ItemSlot acceptor, ItemId item, ushort? amount)
    {
        // A null acceptor means "remove / destroy" rather than transfer to a slot — the
        // items are absorbed into a throwaway scratch slot and discarded. Used by the
        // ChangeItem subtract map-event path (SheetApplier.ApplyItem), which has no
        // destination slot. Without this the subtract path would throw. The scratch is
        // cleared between transfers so it never caps at MaxItemCount on large removals.
        bool destroy = acceptor == null;
        acceptor ??= new ItemSlot(new InventorySlotId(InventoryType.Temporary, 0, ItemSlotId.None));

        ushort totalTransferred = 0;
        ushort remaining = amount ?? ushort.MaxValue;
        var inventory = _getInventory(id);

        if (item == AssetId.Gold)
            return acceptor.TransferFrom(inventory.Gold, remaining, _getItem);

        if (item == AssetId.Rations)
            return acceptor.TransferFrom(inventory.Rations, remaining, _getItem);

        for (int i = 0; i < (int)ItemSlotId.NormalSlotCount && remaining != 0; i++)
        {
            if (inventory.Slots[i].Item != item)
                continue;

            if (destroy) acceptor.Clear();
            ushort transferred = acceptor.TransferFrom(inventory.Slots[i], remaining, _getItem);
            totalTransferred += transferred;
            remaining -= transferred;
            if (remaining == 0)
                break;
        }

        return totalTransferred;
    }

    void OnActivateItem(ActivateItemEvent e)
    {
        var inv = _getInventory(e.SlotId.Id);
        var slot = inv.GetSlot(e.SlotId.Slot);
        if (slot.Item.Type != AssetType.Item)
            return;

        var item = _getItem(slot.Item);
        if (item.TypeId != ItemType.HeadsUpDisplayItem)
            return;

        var textId = TextId.None;
        if (item.Id == Base.Item.Clock)
            textId = Base.SystemText.SpecialItem_TheClockHasBeenActivated;
        if (item.Id == Base.Item.Compass)
            textId = Base.SystemText.SpecialItem_TheCompassHasBeenActivated;
        if (item.Id == Base.Item.MonsterEye)
            textId = Base.SystemText.SpecialItem_TheMonsterEyeHasBeenActivated;

        if (textId.IsNone)
            return;

        // INV-02: show the specific activation message (was Item_Add "Add"), and do NOT consume
        // the item — Compass/Clock/Monster-Eye are permanent toggleable HUD items.
        var tf = Resolve<ITextFormatter>();
        Raise(new SetSpecialItemActiveEvent(item.Id, true));
        Raise(new HoverTextEvent(tf.Format(textId)));
    }

    void OnActivateItemSpell(ActivateItemSpellEvent e)
    {
        var inv = _getInventory(e.SlotId.Id);
        var slot = inv.GetSlot(e.SlotId.Slot);
        if (slot.Item.Type != AssetType.Item)
            return;

        var item = _getItem(slot.Item);
        if (item.Charges <= 0 || item.Spell.IsNone)
            return;

        // Dispatch to the spell-effect registry. Unimplemented spells fall through with
        // Failed and we log the intended cast — the charge is still consumed so item upkeep
        // is consistent with the original game's behaviour for unfulfilled item charges.
        var context = new UAlbion.Game.Combat.SpellCastContext
        {
            // Item-cast doesn't have a real combatant context — caster/target are null when
            // cast outside of combat (e.g. on an inventory menu). Effect implementations
            // must tolerate null caster/target and use the spell metadata alone in that case.
            SpellStrength = 0,
            Random = max => max <= 0 ? 0 : Resolve<UAlbion.Game.IRandom>().Generate(max),
            RaiseEvent = Raise,
        };
        var outcome = UAlbion.Game.Combat.SpellEffectRegistry.Cast(item.Spell, context);
        switch (outcome)
        {
            case UAlbion.Game.Combat.SpellCastOutcome.Hit:
                Info($"ActivateItemSpell: {item.Id} cast {item.Spell} successfully");
                break;
            case UAlbion.Game.Combat.SpellCastOutcome.Resisted:
                Info($"ActivateItemSpell: {item.Id} cast {item.Spell} but it was resisted");
                break;
            default:
                Info($"ActivateItemSpell: {item.Id} would cast {item.Spell} (no handler registered — charge consumed)");
                break;
        }

        item.Charges--;
        Update(e.SlotId.Id);
    }

    async AlbionTask OnDrinkItem(DrinkItemEvent e)
    {
        var inv = _getInventory(e.SlotId.Id);
        var slot = inv.GetSlot(e.SlotId.Slot);
        if (slot.Item.Type != AssetType.Item)
            return;

        var item = _getItem(slot.Item);
        if (item.TypeId != ItemType.Drink)
            return;

        var result = await RaiseQueryA(new PartyMemberPromptEvent(Base.SystemText.InvMsg_WhoShouldDrinkThis));

        if (result == PartyMemberId.None)
            return;

        await RaiseA(new SetContextEvent(ContextType.Subject, result));
        await TriggerItemChain(slot.Item);

        slot.Amount--;
        Update(e.SlotId.Id);
    }

    void OnConsumeCharge(ConsumeItemChargeEvent e)
    {
        // Magic-item use, RE'd from MAIN.EXE fcn.00060013: items with the single-use
        // flag (ITEMLIST +0x1B bit 0x04 — UAlbion's Stackable: potions, scrolls etc)
        // lose one from the stack; otherwise a charge is decremented, and when charges
        // reach 0 an item with the vanish flag (bit 0x10) is destroyed.
        var invId = new InventoryId(InventoryType.Player, (ushort)e.MemberId.Id);
        var inv = _getInventory(invId);
        if (inv == null)
            return;

        foreach (var slot in inv.EnumerateAll())
        {
            if (slot.Item != e.ItemId)
                continue;

            var item = _getItem(slot.Item);
            if (item != null && (item.Flags & ItemFlags.Stackable) != 0)
            {
                slot.Amount--;
                if (slot.Amount == 0)
                    slot.Clear();
                Info($"[Inv] {e.MemberId} consumed one {e.ItemId} ({slot.Amount} left)");
                Raise(new InventoryChangedEvent(invId));
                return;
            }

            if (slot.Charges > 0)
            {
                slot.Charges--;
                if (slot.Charges == 0 && item != null && (item.Flags & ItemFlags.Unk4) != 0)
                {
                    // Vanishes when discharged (thrown weapons, some wands).
                    slot.Clear();
                    Info($"[Inv] {e.MemberId}'s {e.ItemId} was used up");
                }
                else
                {
                    Info($"[Inv] {e.MemberId} used a charge of {e.ItemId} ({slot.Charges} left)");
                }
                Raise(new InventoryChangedEvent(invId));
                return;
            }
        }
    }

    void OnConsumeAmmo(ConsumeAmmoEvent e)
    {
        // One round per ranged strike (fcn.0004f3e2/fcn.0004f4da): the backpack stack
        // depletes first (in the original it refills the equipped ammo slot), then the
        // equipped stack itself.
        var invId = new InventoryId(InventoryType.Player, (ushort)e.MemberId.Id);
        var inv = _getInventory(invId);
        if (inv == null)
            return;

        ItemSlot equipped = null;
        // Backpack slots first
        for (int i = 0; i < (int)ItemSlotId.NormalSlotCount; i++)
        {
            var slot = inv.Slots[i];
            if (IsMatchingAmmo(slot, e.AmmoType))
            {
                DecrementAmmo(slot, invId, e.MemberId);
                return;
            }
        }

        foreach (var slot in inv.EnumerateBodyParts())
            if (equipped == null && IsMatchingAmmo(slot, e.AmmoType))
                equipped = slot;

        if (equipped != null)
            DecrementAmmo(equipped, invId, e.MemberId);
    }

    bool IsMatchingAmmo(ItemSlot slot, AmmunitionType ammoType)
    {
        if (slot == null || slot.Item.Type != AssetType.Item || slot.Amount == 0)
            return false;
        if ((slot.Flags & ItemSlotFlags.Broken) != 0)
            return false;
        var item = _getItem(slot.Item);
        return item != null && item.TypeId == ItemType.Ammo && item.AmmoType == ammoType;
    }

    void DecrementAmmo(ItemSlot slot, InventoryId invId, PartyMemberId member)
    {
        slot.Amount--;
        if (slot.Amount == 0)
            slot.Clear();
        Info($"[Inv] {member} spends one round of ammo ({slot.Amount} left in slot)");
        Raise(new InventoryChangedEvent(invId));
    }

    void OnBreakSlot(BreakInventorySlotEvent e)
    {
        // Combat equipment wear (MAIN.EXE fcn.0004f920): flag the slot broken (the item
        // id never changes — RE batch 4 confirmed there is NO morph) and show the
        // "X is broken!" message. The original moves the broken item to the post-combat
        // loot list and empties the slot; we keep it equipped with the Broken flag — a
        // deliberate equivalent-outcome deviation. RepairItem clears the flag.
        var invId = new InventoryId(InventoryType.Player, (ushort)e.MemberId.Id);
        var inv = _getInventory(invId);
        var slot = inv?.GetSlot(e.SlotId);
        if (slot == null || slot.Item.Type != AssetType.Item || (slot.Flags & ItemSlotFlags.Broken) != 0)
            return;

        slot.Flags |= ItemSlotFlags.Broken;
        var item = _getItem(slot.Item);
        var tf = Resolve<ITextFormatter>();
        Raise(new DescriptionTextEvent(tf.Format(Base.SystemText.CombatMsg_XIsBroken, item)));
        Raise(new InventoryChangedEvent(invId));
    }

    void OnRepairSlot(RepairInventorySlotEvent e)
    {
        // RepairItem NPC service (RE batch 4): clears the Broken flag, item unchanged.
        var invId = new InventoryId(InventoryType.Player, (ushort)e.MemberId.Id);
        var slot = _getInventory(invId)?.GetSlot(e.SlotId);
        if (slot == null || slot.Item.Type != AssetType.Item || (slot.Flags & ItemSlotFlags.Broken) == 0)
            return;
        slot.Flags &= ~ItemSlotFlags.Broken;
        Info($"[Inv] Repaired {slot.Item} in {e.MemberId}'s {e.SlotId} slot");
        Raise(new InventoryChangedEvent(invId));
    }

    void OnIdentifySlot(IdentifyInventorySlotEvent e)
    {
        // AskOpinion NPC service (RE batch 4): sets the ExtraInfo (identified) flag.
        var invId = new InventoryId(InventoryType.Player, (ushort)e.MemberId.Id);
        var slot = _getInventory(invId)?.GetSlot(e.SlotId);
        if (slot == null || slot.Item.Type != AssetType.Item || (slot.Flags & ItemSlotFlags.ExtraInfo) != 0)
            return;
        slot.Flags |= ItemSlotFlags.ExtraInfo;
        Info($"[Inv] Identified {slot.Item} in {e.MemberId}'s {e.SlotId} slot");
        Raise(new InventoryChangedEvent(invId));
    }

    void OnRechargeSlot(RechargeInventorySlotEvent e)
    {
        // RestoreItemEnergy NPC service (RE batch 4): adds charges (capped at the item's
        // MaxCharges) and bumps the enchant counter once per service, capped by
        // MaxEnchantmentCount.
        var invId = new InventoryId(InventoryType.Player, (ushort)e.MemberId.Id);
        var slot = _getInventory(invId)?.GetSlot(e.SlotId);
        if (slot == null || slot.Item.Type != AssetType.Item || e.Charges == 0)
            return;
        var item = _getItem(slot.Item);
        if (item == null || item.MaxCharges == 0)
            return;

        slot.Charges = (byte)Math.Min(item.MaxCharges, slot.Charges + e.Charges);
        if (slot.Enchantment < item.MaxEnchantmentCount)
            slot.Enchantment++;
        Info($"[Inv] Recharged {slot.Item} to {slot.Charges}/{item.MaxCharges} charges (enchant {slot.Enchantment}/{item.MaxEnchantmentCount})");
        Raise(new InventoryChangedEvent(invId));
    }

    void OnDestroyCursed(DestroyCursedEquipmentEvent e)
    {
        // RemoveCurse NPC service (RE batch 4): cursed EQUIPPED items are DESTROYED,
        // not uncursed — the service frees the body slots.
        var invId = new InventoryId(InventoryType.Player, (ushort)e.MemberId.Id);
        var inv = _getInventory(invId);
        if (inv == null)
            return;

        bool any = false;
        foreach (var slot in inv.EnumerateBodyParts())
        {
            if (slot == null || slot.Item.Type != AssetType.Item || (slot.Flags & ItemSlotFlags.Cursed) == 0)
                continue;
            Info($"[Inv] Curse removal destroyed {slot.Item} ({e.MemberId}'s {slot.Id.Slot} slot)");
            slot.Clear();
            any = true;
        }

        if (any)
            Raise(new InventoryChangedEvent(invId));
    }

    AlbionTask OnReadItem(ReadItemEvent e)
    {
        var inv = _getInventory(e.SlotId.Id);
        var slot = inv.GetSlot(e.SlotId.Slot);
        if (slot.Item.Type != AssetType.Item)
            return AlbionTask.CompletedTask;

        var item = _getItem(slot.Item);
        if (item.TypeId != ItemType.Document)
            return AlbionTask.CompletedTask;

        return TriggerItemChain(item.Id);
    }

    void OnReadSpellScroll(ReadSpellScrollEvent e)
    {
        var inv = _getInventory(e.SlotId.Id);
        var slot = inv.GetSlot(e.SlotId.Slot);
        if (slot.Item.Type != AssetType.Item)
            return;

        var item = _getItem(slot.Item);
        if (item.TypeId != ItemType.SpellScroll)
            return;

        var spell = Assets.LoadSpell(item.Spell);
        if (spell == null)
            return;

        var tf = Resolve<ITextFormatter>();
        var party = Resolve<IParty>();

        var target = party.StatusBarOrder
            .FirstOrDefault(x => (x.Effective.Magic.SpellClasses & spell.Class.ToFlag()) != 0);

        if (target == null)
        {
            Raise(new HoverTextEvent(tf.Format(Base.SystemText.InvMsg_NoOneKnowsThisSpellClass)));
            return;
        }

        if (target.Effective.Magic.KnownSpells.Contains(spell.Id))
        {
            Raise(new HoverTextEvent(tf.Format(Base.SystemText.InvMsg_ThisSpellIsAlreadyKnown)));
            return;
        }

        if (target.Effective.Level < spell.LevelRequirement)
        {
            Raise(new HoverTextEvent(tf.Format(Base.SystemText.InvMsg_ThisSpellsLevelIsTooHigh)));
            return;
        }

        // INV-02: teach the spell to the validated learner (target), not the scroll's holder.
        Raise(new LearnSpellEvent(target.Id.ToSheet(), item.Spell));
        Raise(new HoverTextEvent(tf.Format(Base.SystemText.InvMsg_XLearnedTheSpell)));
        
        slot.Amount--;
        Update(e.SlotId.Id);
    }

    async AlbionTask TriggerItemChain(ItemId itemId)
    {
        var eventSet = Assets.LoadEventSet(Base.EventSet.InventoryItems);
        foreach (var eventIndex in eventSet.Chains)
        {
            if (eventSet.Events[eventIndex].Event is not ActionEvent action)
                continue;

            if (action.ActionType != ActionType.UseItem || action.Argument != (AssetId)itemId)
                continue;

            var triggerEvent = new TriggerChainEvent(
                eventSet,
                eventIndex,
                new EventSource(eventSet.Id, TriggerType.Action));

            await RaiseA(triggerEvent);
            return;
        }
    }
}
