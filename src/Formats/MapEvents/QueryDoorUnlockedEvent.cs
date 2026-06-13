using System;
using SerdesNet;
using UAlbion.Api.Eventing;
using UAlbion.Config;
using UAlbion.Formats.Ids;

namespace UAlbion.Formats.MapEvents;

/// <summary>Branches on whether a door has been unlocked. Previously threw at map load.</summary>
[Event("query_door_unlocked")]
public class QueryDoorUnlockedEvent : QueryEvent
{
    public override QueryType QueryType => QueryType.DoorUnlocked;
    [EventPart("op")] public QueryOperation Operation { get; private set; }
    [EventPart("imm")] public byte Immediate { get; private set; }
    [EventPart("door")] public DoorId DoorId { get; private set; }
    QueryDoorUnlockedEvent() { }
    public QueryDoorUnlockedEvent(QueryOperation operation, byte immediate, DoorId doorId)
    {
        Operation = operation;
        Immediate = immediate;
        DoorId = doorId;
    }
    public static QueryDoorUnlockedEvent Serdes(QueryDoorUnlockedEvent e, AssetMapping mapping, ISerdes s)
    {
        ArgumentNullException.ThrowIfNull(s);
        e ??= new QueryDoorUnlockedEvent();
        e.Operation = s.EnumU8(nameof(Operation), e.Operation);
        e.Immediate = s.UInt8(nameof(Immediate), e.Immediate);
        int zeroes = s.UInt8(null, 0);
        zeroes += s.UInt8(null, 0);
        e.DoorId = DoorId.SerdesU16(nameof(DoorId), e.DoorId, mapping, s);
        s.Assert(zeroes == 0, "QueryDoorUnlockedEvent: Expected fields 3,4 to be 0");
        return e;
    }
}
