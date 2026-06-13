using System;
using SerdesNet;
using UAlbion.Api.Eventing;

namespace UAlbion.Formats.MapEvents;

/// <summary>Branches on the party leader's PlayerClass. Previously threw at map load.</summary>
[Event("query_class")]
public class QueryClassEvent : QueryEvent
{
    public override QueryType QueryType => QueryType.Class;
    [EventPart("op")] public QueryOperation Operation { get; private set; }
    [EventPart("imm")] public byte Immediate { get; private set; }
    [EventPart("arg")] public ushort Argument { get; private set; }
    QueryClassEvent() { }
    public QueryClassEvent(QueryOperation operation, byte immediate, ushort argument)
    {
        Operation = operation;
        Immediate = immediate;
        Argument = argument;
    }
    public static QueryClassEvent Serdes(QueryClassEvent e, ISerdes s)
    {
        ArgumentNullException.ThrowIfNull(s);
        e ??= new QueryClassEvent();
        e.Operation = s.EnumU8(nameof(Operation), e.Operation);
        e.Immediate = s.UInt8(nameof(Immediate), e.Immediate);
        int zeroes = s.UInt8(null, 0);
        zeroes += s.UInt8(null, 0);
        e.Argument = s.UInt16(nameof(Argument), e.Argument);
        s.Assert(zeroes == 0, "QueryClassEvent: Expected fields 3,4 to be 0");
        return e;
    }
}
