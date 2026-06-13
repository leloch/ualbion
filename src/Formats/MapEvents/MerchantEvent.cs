using UAlbion.Api.Eventing;
using UAlbion.Formats.Ids;

namespace UAlbion.Formats.MapEvents;

[Event("inv:merchant", "Opens the inventory screen for the given merchant")]
public class MerchantEvent : Event
{
    public MerchantEvent(MerchantId merchantId, PartyMemberId member, ushort pricePercent = 100) // member None → current leader
    {
        MerchantId = merchantId;
        PartyMemberId = member;
        PricePercent = pricePercent;
    }

    [EventPart("id")] public MerchantId MerchantId { get; }
    [EventPart("partyMember")] public PartyMemberId PartyMemberId { get; }
    // Per-shop buy/sell percent (the opening PlaceActionEvent.Unk6). 100 = price == item Value.
    [EventPart("pricePercent", true, (ushort)100)] public ushort PricePercent { get; }
}