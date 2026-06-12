using UAlbion.Api.Eventing;
using UAlbion.Formats.Ids;

namespace UAlbion.Game.Events;

/// <summary>
/// Decrements one charge from the first slot in a party member's inventory holding the
/// given item with charges remaining (combat magic-item use).
/// </summary>
[Event("consume_charge")]
public class ConsumeItemChargeEvent : GameEvent
{
    public ConsumeItemChargeEvent(PartyMemberId memberId, ItemId itemId)
    {
        MemberId = memberId;
        ItemId = itemId;
    }

    [EventPart("member")] public PartyMemberId MemberId { get; }
    [EventPart("item")] public ItemId ItemId { get; }
}
