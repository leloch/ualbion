using UAlbion.Api.Eventing;
using UAlbion.Formats.Assets.Inv;
using UAlbion.Formats.Ids;

namespace UAlbion.Game.Events;

/// <summary>
/// Marks the item in a party member's equipment slot as broken (combat equipment wear —
/// MAIN.EXE fcn.0004f920 rolls the item's break-rate vs 1000 on every connecting swing).
/// </summary>
[Event("break_item_slot")]
public class BreakInventorySlotEvent : GameEvent
{
    public BreakInventorySlotEvent(PartyMemberId memberId, ItemSlotId slotId)
    {
        MemberId = memberId;
        SlotId = slotId;
    }

    [EventPart("member")] public PartyMemberId MemberId { get; }
    [EventPart("slot")] public ItemSlotId SlotId { get; }
}
