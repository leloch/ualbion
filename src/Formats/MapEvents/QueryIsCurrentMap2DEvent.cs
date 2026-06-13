using System;
using SerdesNet;
using UAlbion.Api.Eventing;

namespace UAlbion.Formats.MapEvents;

/// <summary>True when the current map is 2D (no params used). Previously threw at map load.</summary>
[Event("query_is_current_map_2d")]
public class QueryIsCurrentMap2DEvent : QueryEvent
{
    public override QueryType QueryType => QueryType.IsCurrentMap2D;
    [EventPart("op")] public QueryOperation Operation { get; private set; }
    [EventPart("imm")] public byte Immediate { get; private set; }
    [EventPart("arg")] public ushort Argument { get; private set; }
    QueryIsCurrentMap2DEvent() { }
    public QueryIsCurrentMap2DEvent(QueryOperation operation, byte immediate, ushort argument)
    {
        Operation = operation;
        Immediate = immediate;
        Argument = argument;
    }
    public static QueryIsCurrentMap2DEvent Serdes(QueryIsCurrentMap2DEvent e, ISerdes s)
    {
        ArgumentNullException.ThrowIfNull(s);
        e ??= new QueryIsCurrentMap2DEvent();
        e.Operation = s.EnumU8(nameof(Operation), e.Operation);
        e.Immediate = s.UInt8(nameof(Immediate), e.Immediate);
        int zeroes = s.UInt8(null, 0);
        zeroes += s.UInt8(null, 0);
        e.Argument = s.UInt16(nameof(Argument), e.Argument);
        s.Assert(zeroes == 0, "QueryIsCurrentMap2DEvent: Expected fields 3,4 to be 0");
        return e;
    }
}
