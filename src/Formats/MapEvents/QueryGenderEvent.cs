using System;
using SerdesNet;
using UAlbion.Api.Eventing;

namespace UAlbion.Formats.MapEvents;

/// <summary>Branches on the party leader's Gender (Iskai-only / gendered-dialogue gates).
/// Previously threw FormatException at map load.</summary>
[Event("query_gender")]
public class QueryGenderEvent : QueryEvent
{
    public override QueryType QueryType => QueryType.Gender;
    [EventPart("op")] public QueryOperation Operation { get; private set; }
    [EventPart("imm")] public byte Immediate { get; private set; }
    [EventPart("arg")] public ushort Argument { get; private set; }
    QueryGenderEvent() { }
    public QueryGenderEvent(QueryOperation operation, byte immediate, ushort argument)
    {
        Operation = operation;
        Immediate = immediate;
        Argument = argument;
    }
    public static QueryGenderEvent Serdes(QueryGenderEvent e, ISerdes s)
    {
        ArgumentNullException.ThrowIfNull(s);
        e ??= new QueryGenderEvent();
        e.Operation = s.EnumU8(nameof(Operation), e.Operation);
        e.Immediate = s.UInt8(nameof(Immediate), e.Immediate);
        int zeroes = s.UInt8(null, 0);
        zeroes += s.UInt8(null, 0);
        e.Argument = s.UInt16(nameof(Argument), e.Argument);
        s.Assert(zeroes == 0, "QueryGenderEvent: Expected fields 3,4 to be 0");
        return e;
    }
}
