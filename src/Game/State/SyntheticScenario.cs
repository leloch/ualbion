using System.Collections.Generic;
using UAlbion.Formats;
using UAlbion.Formats.Ids;

namespace UAlbion.Game.State;

/// <summary>One NPC to hide on a given map (so a synthetic scenario can suppress an NPC that a
/// real save would have removed at this point in the story).</summary>
public sealed class NpcDisableSpec
{
    public MapId Map { get; init; }
    public int Npc { get; init; }
}

/// <summary>
/// A named synthetic game state — a parameterised "new game" that drops the party into an
/// arbitrary phase WITHOUT a real save file. Built procedurally by <c>GameState</c> the same way
/// <c>NewGame</c> is (all member sheets are loaded, the roster just selects them), then the spec
/// is applied (party / gold / spells / flags / words / position) and the standard load path runs.
///
/// <see cref="X"/>/<see cref="Y"/> may be null → the spawn tile is auto-picked from the live
/// collider after the map loads (see <see cref="SpawnValidator"/>), so a scenario can never strand
/// the party in a wall.
/// </summary>
public sealed class SyntheticScenario
{
    /// <summary>Lowercase single-token key used by the <c>synth_scenario &lt;name&gt;</c> event.</summary>
    public string Name { get; init; }
    public string Description { get; init; }

    public MapId Map { get; init; }
    public ushort? X { get; init; }
    public ushort? Y { get; init; }
    public Direction Direction { get; init; } = Direction.East;

    /// <summary>Active party roster (max 6). Empty ⇒ Tom only (the NewGame default).</summary>
    public IList<PartyMemberId> Party { get; init; } = new List<PartyMemberId>();
    public ushort Gold { get; init; }

    public IList<SpellId> KnownSpells { get; init; } = new List<SpellId>();
    public IList<SwitchId> SwitchesToSet { get; init; } = new List<SwitchId>();
    public IList<WordId> WordsKnown { get; init; } = new List<WordId>();
    public IList<ChestId> ChestsOpen { get; init; } = new List<ChestId>();
    public IList<DoorId> DoorsOpen { get; init; } = new List<DoorId>();
    public IList<NpcDisableSpec> NpcsDisabled { get; init; } = new List<NpcDisableSpec>();

    /// <summary>Time of day the scenario starts at (game hours since midnight).</summary>
    public double TimeHours { get; init; } = 12;
}
