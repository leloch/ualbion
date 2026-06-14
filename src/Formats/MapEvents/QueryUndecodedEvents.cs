using System;
using SerdesNet;
using UAlbion.Api.Eventing;

namespace UAlbion.Formats.MapEvents;

// Phase-0 crash guard: query subtypes whose semantics are still undecoded (Unk8/B/D/13/24-27).
// Every Albion query record is a fixed 10-byte map event — type(1)+queryType(1)+op(1)+imm(1)+
// 2 zero bytes+arg(2), with the false-branch target read by the surrounding BranchEventNode — so
// these share the exact layout of the decoded Unk queries (QueryUnk21Event). Before this, any of
// them appearing in real map data threw FormatException in QueryEvent.Serdes => the whole MAP
// failed to load. They now round-trip byte-faithfully; the runtime Querier has no handler so the
// branch evaluates to its default (false) until the semantics are RE'd. (_TODO_100PCT.md Phase 0.)

public sealed class QueryUnk8Event : QueryEvent
{
    public override QueryType QueryType => QueryType.Unk8;
    [EventPart("op")] public QueryOperation Operation { get; private set; }
    [EventPart("imm")] public byte Immediate { get; private set; }
    [EventPart("arg")] public ushort Argument { get; set; }
    QueryUnk8Event() { }
    public QueryUnk8Event(QueryOperation operation, byte immediate, ushort argument) { Operation = operation; Immediate = immediate; Argument = argument; }
    public static QueryUnk8Event Serdes(QueryUnk8Event e, ISerdes s)
    {
        ArgumentNullException.ThrowIfNull(s);
        e ??= new QueryUnk8Event();
        e.Operation = s.EnumU8(nameof(Operation), e.Operation);
        e.Immediate = s.UInt8(nameof(Immediate), e.Immediate);
        int zeroes = s.UInt8(null, 0); zeroes += s.UInt8(null, 0);
        e.Argument = s.UInt16(nameof(Argument), e.Argument);
        s.Assert(zeroes == 0, "QueryUnk8Event: Expected fields 3,4 to be 0");
        return e;
    }
}

public sealed class QueryUnkBEvent : QueryEvent
{
    public override QueryType QueryType => QueryType.UnkB;
    [EventPart("op")] public QueryOperation Operation { get; private set; }
    [EventPart("imm")] public byte Immediate { get; private set; }
    [EventPart("arg")] public ushort Argument { get; set; }
    QueryUnkBEvent() { }
    public QueryUnkBEvent(QueryOperation operation, byte immediate, ushort argument) { Operation = operation; Immediate = immediate; Argument = argument; }
    public static QueryUnkBEvent Serdes(QueryUnkBEvent e, ISerdes s)
    {
        ArgumentNullException.ThrowIfNull(s);
        e ??= new QueryUnkBEvent();
        e.Operation = s.EnumU8(nameof(Operation), e.Operation);
        e.Immediate = s.UInt8(nameof(Immediate), e.Immediate);
        int zeroes = s.UInt8(null, 0); zeroes += s.UInt8(null, 0);
        e.Argument = s.UInt16(nameof(Argument), e.Argument);
        s.Assert(zeroes == 0, "QueryUnkBEvent: Expected fields 3,4 to be 0");
        return e;
    }
}

public sealed class QueryUnkDEvent : QueryEvent
{
    public override QueryType QueryType => QueryType.UnkD;
    [EventPart("op")] public QueryOperation Operation { get; private set; }
    [EventPart("imm")] public byte Immediate { get; private set; }
    [EventPart("arg")] public ushort Argument { get; set; }
    QueryUnkDEvent() { }
    public QueryUnkDEvent(QueryOperation operation, byte immediate, ushort argument) { Operation = operation; Immediate = immediate; Argument = argument; }
    public static QueryUnkDEvent Serdes(QueryUnkDEvent e, ISerdes s)
    {
        ArgumentNullException.ThrowIfNull(s);
        e ??= new QueryUnkDEvent();
        e.Operation = s.EnumU8(nameof(Operation), e.Operation);
        e.Immediate = s.UInt8(nameof(Immediate), e.Immediate);
        int zeroes = s.UInt8(null, 0); zeroes += s.UInt8(null, 0);
        e.Argument = s.UInt16(nameof(Argument), e.Argument);
        s.Assert(zeroes == 0, "QueryUnkDEvent: Expected fields 3,4 to be 0");
        return e;
    }
}

public sealed class QueryUnk13Event : QueryEvent
{
    public override QueryType QueryType => QueryType.Unk13;
    [EventPart("op")] public QueryOperation Operation { get; private set; }
    [EventPart("imm")] public byte Immediate { get; private set; }
    [EventPart("arg")] public ushort Argument { get; set; }
    QueryUnk13Event() { }
    public QueryUnk13Event(QueryOperation operation, byte immediate, ushort argument) { Operation = operation; Immediate = immediate; Argument = argument; }
    public static QueryUnk13Event Serdes(QueryUnk13Event e, ISerdes s)
    {
        ArgumentNullException.ThrowIfNull(s);
        e ??= new QueryUnk13Event();
        e.Operation = s.EnumU8(nameof(Operation), e.Operation);
        e.Immediate = s.UInt8(nameof(Immediate), e.Immediate);
        int zeroes = s.UInt8(null, 0); zeroes += s.UInt8(null, 0);
        e.Argument = s.UInt16(nameof(Argument), e.Argument);
        s.Assert(zeroes == 0, "QueryUnk13Event: Expected fields 3,4 to be 0");
        return e;
    }
}

public sealed class QueryUnk24Event : QueryEvent
{
    public override QueryType QueryType => QueryType.Unk24;
    [EventPart("op")] public QueryOperation Operation { get; private set; }
    [EventPart("imm")] public byte Immediate { get; private set; }
    [EventPart("arg")] public ushort Argument { get; set; }
    QueryUnk24Event() { }
    public QueryUnk24Event(QueryOperation operation, byte immediate, ushort argument) { Operation = operation; Immediate = immediate; Argument = argument; }
    public static QueryUnk24Event Serdes(QueryUnk24Event e, ISerdes s)
    {
        ArgumentNullException.ThrowIfNull(s);
        e ??= new QueryUnk24Event();
        e.Operation = s.EnumU8(nameof(Operation), e.Operation);
        e.Immediate = s.UInt8(nameof(Immediate), e.Immediate);
        int zeroes = s.UInt8(null, 0); zeroes += s.UInt8(null, 0);
        e.Argument = s.UInt16(nameof(Argument), e.Argument);
        s.Assert(zeroes == 0, "QueryUnk24Event: Expected fields 3,4 to be 0");
        return e;
    }
}

public sealed class QueryUnk25Event : QueryEvent
{
    public override QueryType QueryType => QueryType.Unk25;
    [EventPart("op")] public QueryOperation Operation { get; private set; }
    [EventPart("imm")] public byte Immediate { get; private set; }
    [EventPart("arg")] public ushort Argument { get; set; }
    QueryUnk25Event() { }
    public QueryUnk25Event(QueryOperation operation, byte immediate, ushort argument) { Operation = operation; Immediate = immediate; Argument = argument; }
    public static QueryUnk25Event Serdes(QueryUnk25Event e, ISerdes s)
    {
        ArgumentNullException.ThrowIfNull(s);
        e ??= new QueryUnk25Event();
        e.Operation = s.EnumU8(nameof(Operation), e.Operation);
        e.Immediate = s.UInt8(nameof(Immediate), e.Immediate);
        int zeroes = s.UInt8(null, 0); zeroes += s.UInt8(null, 0);
        e.Argument = s.UInt16(nameof(Argument), e.Argument);
        s.Assert(zeroes == 0, "QueryUnk25Event: Expected fields 3,4 to be 0");
        return e;
    }
}

public sealed class QueryUnk26Event : QueryEvent
{
    public override QueryType QueryType => QueryType.Unk26;
    [EventPart("op")] public QueryOperation Operation { get; private set; }
    [EventPart("imm")] public byte Immediate { get; private set; }
    [EventPart("arg")] public ushort Argument { get; set; }
    QueryUnk26Event() { }
    public QueryUnk26Event(QueryOperation operation, byte immediate, ushort argument) { Operation = operation; Immediate = immediate; Argument = argument; }
    public static QueryUnk26Event Serdes(QueryUnk26Event e, ISerdes s)
    {
        ArgumentNullException.ThrowIfNull(s);
        e ??= new QueryUnk26Event();
        e.Operation = s.EnumU8(nameof(Operation), e.Operation);
        e.Immediate = s.UInt8(nameof(Immediate), e.Immediate);
        int zeroes = s.UInt8(null, 0); zeroes += s.UInt8(null, 0);
        e.Argument = s.UInt16(nameof(Argument), e.Argument);
        s.Assert(zeroes == 0, "QueryUnk26Event: Expected fields 3,4 to be 0");
        return e;
    }
}

public sealed class QueryUnk27Event : QueryEvent
{
    public override QueryType QueryType => QueryType.Unk27;
    [EventPart("op")] public QueryOperation Operation { get; private set; }
    [EventPart("imm")] public byte Immediate { get; private set; }
    [EventPart("arg")] public ushort Argument { get; set; }
    QueryUnk27Event() { }
    public QueryUnk27Event(QueryOperation operation, byte immediate, ushort argument) { Operation = operation; Immediate = immediate; Argument = argument; }
    public static QueryUnk27Event Serdes(QueryUnk27Event e, ISerdes s)
    {
        ArgumentNullException.ThrowIfNull(s);
        e ??= new QueryUnk27Event();
        e.Operation = s.EnumU8(nameof(Operation), e.Operation);
        e.Immediate = s.UInt8(nameof(Immediate), e.Immediate);
        int zeroes = s.UInt8(null, 0); zeroes += s.UInt8(null, 0);
        e.Argument = s.UInt16(nameof(Argument), e.Argument);
        s.Assert(zeroes == 0, "QueryUnk27Event: Expected fields 3,4 to be 0");
        return e;
    }
}
