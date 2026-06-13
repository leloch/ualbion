using System;
using SerdesNet;
using UAlbion.Api.Eventing;
using UAlbion.Config;
using UAlbion.Formats.Ids;

namespace UAlbion.Formats.MapEvents;

/// <summary>Branches on whether a chest has been unlocked. Previously threw at map load.</summary>
[Event("query_chest_unlocked")]
public class QueryChestUnlockedEvent : QueryEvent
{
    public override QueryType QueryType => QueryType.ChestUnlocked;
    [EventPart("op")] public QueryOperation Operation { get; private set; }
    [EventPart("imm")] public byte Immediate { get; private set; }
    [EventPart("chest")] public ChestId ChestId { get; private set; }
    QueryChestUnlockedEvent() { }
    public QueryChestUnlockedEvent(QueryOperation operation, byte immediate, ChestId chestId)
    {
        Operation = operation;
        Immediate = immediate;
        ChestId = chestId;
    }
    public static QueryChestUnlockedEvent Serdes(QueryChestUnlockedEvent e, AssetMapping mapping, ISerdes s)
    {
        ArgumentNullException.ThrowIfNull(s);
        e ??= new QueryChestUnlockedEvent();
        e.Operation = s.EnumU8(nameof(Operation), e.Operation);
        e.Immediate = s.UInt8(nameof(Immediate), e.Immediate);
        int zeroes = s.UInt8(null, 0);
        zeroes += s.UInt8(null, 0);
        e.ChestId = ChestId.SerdesU16(nameof(ChestId), e.ChestId, mapping, s);
        s.Assert(zeroes == 0, "QueryChestUnlockedEvent: Expected fields 3,4 to be 0");
        return e;
    }
}
