using System;
using System.Collections.Generic;
using System.Linq;

namespace UAlbion.Game.Combat;

/// <summary>
/// Initiative-order builder reverse-engineered from <c>fcn.0004c3f5</c>: sort all combatants
/// descending by sheet attribute #3 (Speed). The original engine inserts two sentinel rows
/// (a "fast" / "slow" phase divider at initiative 51, and an "end-of-round" sentinel at 0)
/// to trigger between-phase events — we don't model those events yet because there's no
/// action-select UI to pause for them; the order they produce is identical to the live
/// sort here, so combat ordering is correct without them.
/// </summary>
public static class InitiativeOrder
{
    /// <summary>
    /// Order <paramref name="party"/> + <paramref name="mobs"/> descending by Speed.
    /// Stable on equal speed — party members before monsters — for deterministic tests.
    /// </summary>
    public static IEnumerable<(T Attacker, bool IsParty)> Order<T>(
        IReadOnlyList<T> party,
        IReadOnlyList<T> mobs,
        Func<T, int> getSpeed)
    {
        ArgumentNullException.ThrowIfNull(getSpeed);
        ArgumentNullException.ThrowIfNull(party);
        ArgumentNullException.ThrowIfNull(mobs);

        var combined = new List<(T, bool, int)>(party.Count + mobs.Count);
        foreach (var p in party) combined.Add((p, true, getSpeed(p)));
        foreach (var m in mobs)  combined.Add((m, false, getSpeed(m)));
        // OrderByDescending is stable — preserves the party-then-mob tiebreak.
        return combined.OrderByDescending(t => t.Item3).Select(t => (t.Item1, t.Item2));
    }
}
