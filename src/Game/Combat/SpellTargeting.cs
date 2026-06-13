using UAlbion.Formats.Assets;

namespace UAlbion.Game.Combat;

/// <summary>The resolved shape of a spell's target area (RE 5B fcn.0005ef24/fcn.0005fb21).</summary>
public enum SpellArea
{
    /// <summary>One tile (or nearest-enemy / self retarget on an empty pick).</summary>
    SingleTarget,
    /// <summary>Every enemy sharing the picked tile's 6-wide grid row.</summary>
    Row,
    /// <summary>Every living enemy.</summary>
    AllMonsters,
    /// <summary>The whole living party (SPELLDAT 0x04, misnamed "DeadParty").</summary>
    WholeParty,
}

/// <summary>
/// Pure decoding of a spell's SPELLDAT <c>Targets</c> byte into an area shape, extracted from
/// Battle.CastQueuedSpell so the priority (All &gt; Row &gt; WholeParty &gt; Single) and the
/// offensive-retarget rule are unit-testable. Self-managed spells (GoddessWrath) pick their
/// own victims, so they classify as Single (the cast loop hands them a placeholder recipient).
/// </summary>
public static class SpellTargeting
{
    public static SpellArea Classify(SpellTargets targets, bool selfManaged)
    {
        if (selfManaged)
            return SpellArea.SingleTarget;
        if ((targets & SpellTargets.AllMonsters) != 0)
            return SpellArea.AllMonsters;
        if ((targets & SpellTargets.RowOfMonsters) != 0)
            return SpellArea.Row;
        if ((targets & SpellTargets.DeadParty) != 0)
            return SpellArea.WholeParty;
        return SpellArea.SingleTarget;
    }

    /// <summary>
    /// True when an empty/dead-tile pick should retarget the nearest enemy (any
    /// monster-targeting bit) rather than self-cast.
    /// </summary>
    public static bool IsOffensive(SpellTargets targets)
        => (targets & (SpellTargets.OneMonster | SpellTargets.RowOfMonsters | SpellTargets.AllMonsters)) != 0;
}
