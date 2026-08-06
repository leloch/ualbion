using UAlbion.Api.Eventing;
using UAlbion.Game.Events;

namespace UAlbion.Game.Diag;

// Harness-only diagnostic events. These exist so automated end-to-end tests can read the
// game's own data (rather than guessing ids from docs) and can exercise RE'd code paths that
// no shipped map data reaches. Handled by DiagnosticsManager; results go to the trace log.

/// <summary>
/// Fabricate a genuinely TRAPPED chest and open it. No shipped map arms a chest trap (every
/// base-game chest's false branch is "end"), so this is the only way to exercise the RE'd trap
/// path (<c>_RE_CHEST_TRAP.md</c>: the chest node's NextIfFalse link IS the arm signal).
/// A subsequent failed lockpick with a low-Dexterity leader springs the trap down that branch.
/// </summary>
[Event("debug_trapped_chest", "Harness: open a fabricated trapped chest to exercise the trap path")]
public class DebugTrappedChestEvent : GameEvent
{
    public DebugTrappedChestEvent(byte damage) => Damage = damage;
    [EventPart("damage", true, (byte)5)] public byte Damage { get; }
}

/// <summary>
/// Scan the full ITEMLIST and report every item template with <c>ItemFlags.Cursed</c> set
/// (kind=cursed_item), so tests can drive the cursed-equip trap and the RemoveCurse service
/// against real game data.
/// </summary>
[Event("debug_find_cursed_items", "Harness: list all cursed item templates in the trace log")]
public class DebugFindCursedItemsEvent : GameEvent;

/// <summary>
/// Scan every MonsterSheet for a carried long-range weapon (kind=ranged_monster). Determines
/// whether the monster ranged-attack path has any base-game data arming it.
/// </summary>
[Event("debug_find_ranged_monsters", "Harness: list monsters carrying long-range weapons in the trace log")]
public class DebugFindRangedMonstersEvent : GameEvent;

/// <summary>
/// Dump the raw text of an event-text string, including <c>{BLOK}</c>/<c>{WORD}</c> markers
/// (kind=string), for diagnosing conversation block parsing.
/// </summary>
[Event("dump_string", "Harness: dump a raw event-text string to the trace log")]
public class DumpStringEvent : GameEvent
{
    public DumpStringEvent(ushort eventSetId, ushort subId) { EventSetId = eventSetId; SubId = subId; }
    [EventPart("set")] public ushort EventSetId { get; }
    [EventPart("sub")] public ushort SubId { get; }
}

/// <summary>
/// Walk a map chain's node graph from its entry, following both branch arms, and log every
/// node with its next/false links (kind=chain_node). Lets silent or mis-routing chains be
/// diagnosed from data instead of disassembly.
/// </summary>
[Event("dump_chain", "Harness: log every node of a chain on the current map")]
public class DumpChainEvent : GameEvent
{
    public DumpChainEvent(ushort chain) => Chain = chain;
    [EventPart("chain")] public ushort Chain { get; }
}
