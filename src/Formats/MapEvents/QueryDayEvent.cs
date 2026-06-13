using System;
using SerdesNet;
using UAlbion.Api.Eventing;

namespace UAlbion.Formats.MapEvents;

/// <summary>Branches on DayCount modulo the immediate byte (scheduled/periodic events).
/// Previously threw at map load.</summary>
[Event("query_day")]
public class QueryDayEvent : QueryEvent
{
    public override QueryType QueryType => QueryType.Day;
    [EventPart("op")] public QueryOperation Operation { get; private set; }
    [EventPart("imm")] public byte Immediate { get; private set; } // modulo divisor
    [EventPart("arg")] public ushort Argument { get; private set; }
    QueryDayEvent() { }
    public QueryDayEvent(QueryOperation operation, byte immediate, ushort argument)
    {
        Operation = operation;
        Immediate = immediate;
        Argument = argument;
    }
    public static QueryDayEvent Serdes(QueryDayEvent e, ISerdes s)
    {
        ArgumentNullException.ThrowIfNull(s);
        e ??= new QueryDayEvent();
        e.Operation = s.EnumU8(nameof(Operation), e.Operation);
        e.Immediate = s.UInt8(nameof(Immediate), e.Immediate);
        int zeroes = s.UInt8(null, 0);
        zeroes += s.UInt8(null, 0);
        e.Argument = s.UInt16(nameof(Argument), e.Argument);
        s.Assert(zeroes == 0, "QueryDayEvent: Expected fields 3,4 to be 0");
        return e;
    }
}
