using UAlbion.Api.Eventing;
using UAlbion.Formats.Assets.Inv;
using UAlbion.Formats.Ids;

namespace UAlbion.Game.Events;

/// <summary>
/// Clears the Broken flag on an inventory slot (the RepairItem NPC service —
/// PlaceAction dispatcher type 0xC, RE batch 4: price = item value × Unk6/100).
/// </summary>
[Event("repair_item_slot")]
public class RepairInventorySlotEvent : GameEvent
{
    public RepairInventorySlotEvent(PartyMemberId memberId, ItemSlotId slotId)
    {
        MemberId = memberId;
        SlotId = slotId;
    }

    [EventPart("member")] public PartyMemberId MemberId { get; }
    [EventPart("slot")] public ItemSlotId SlotId { get; }
}

/// <summary>
/// Sets the ExtraInfo (identified) flag on an inventory slot (the AskOpinion NPC
/// service — type 0x4, RE batch 4: price = item value × Unk6/100).
/// </summary>
[Event("identify_item_slot")]
public class IdentifyInventorySlotEvent : GameEvent
{
    public IdentifyInventorySlotEvent(PartyMemberId memberId, ItemSlotId slotId)
    {
        MemberId = memberId;
        SlotId = slotId;
    }

    [EventPart("member")] public PartyMemberId MemberId { get; }
    [EventPart("slot")] public ItemSlotId SlotId { get; }
}

/// <summary>
/// Restores charges to a spell-item slot and bumps its enchant counter (the
/// RestoreItemEnergy NPC service — type 0x5, RE batch 4: price = Unk6 per charge,
/// recharge count capped by missing charges; enchantments capped by MaxEnchantmentCount).
/// </summary>
[Event("recharge_item_slot")]
public class RechargeInventorySlotEvent : GameEvent
{
    public RechargeInventorySlotEvent(PartyMemberId memberId, ItemSlotId slotId, ushort charges)
    {
        MemberId = memberId;
        SlotId = slotId;
        Charges = charges;
    }

    [EventPart("member")] public PartyMemberId MemberId { get; }
    [EventPart("slot")] public ItemSlotId SlotId { get; }
    [EventPart("charges")] public ushort Charges { get; }
}

/// <summary>
/// Destroys every cursed EQUIPPED item on a party member (the RemoveCurse NPC
/// service — type 0x3, RE batch 4: flat price; the items are destroyed, not uncursed).
/// </summary>
[Event("destroy_cursed_equipment")]
public class DestroyCursedEquipmentEvent : GameEvent
{
    public DestroyCursedEquipmentEvent(PartyMemberId memberId) => MemberId = memberId;
    [EventPart("member")] public PartyMemberId MemberId { get; }
}
