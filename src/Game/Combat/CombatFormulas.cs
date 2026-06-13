using System;

namespace UAlbion.Game.Combat;

/// <summary>
/// Pure, side-effect-free combat/interaction formulas extracted from the RE'd handlers so
/// they can be unit-tested in isolation (the call sites — Battle, InventoryLockPane — are
/// component/harness-bound). Each method cites its MAIN.EXE origin.
/// </summary>
public static class CombatFormulas
{
    /// <summary>
    /// Monster flee predicate (RE 5A, fcn.00051506 + behaviour table 0x13e1f0, variant by
    /// the MONCHAR strategy byte sheet+0x0C). Returns true when the monster should flee.
    /// Class-bit 0x80 creatures never flee.
    /// </summary>
    /// <param name="classBits">sheet+0x0E creature-class bitmask (bit 0x80 = never-flee).</param>
    /// <param name="strategy">sheet+0x0C behaviour-strategy id.</param>
    /// <param name="livingMonsters">monsters still on the grid.</param>
    /// <param name="totalMonsters">monsters at battle start (round-start total).</param>
    /// <param name="livingParty">active party members.</param>
    /// <param name="lp">the monster's current LP.</param>
    /// <param name="maxLp">the monster's max LP.</param>
    /// <param name="row">the monster's grid row (0 = front).</param>
    public static bool MoraleBroken(
        int classBits, int strategy, int livingMonsters, int totalMonsters,
        int livingParty, int lp, int maxLp, int row, int morale)
    {
        if ((classBits & 0x80) != 0)
            return false;

        totalMonsters = Math.Max(1, totalMonsters);
        maxLp = Math.Max(1, maxLp);

        switch (strategy)
        {
            case 2: // flees once any monster has died
                return livingMonsters < totalMonsters;
            case 7: // outnumbered + LP threshold (deeper rows give up sooner)
            {
                bool fight = livingMonsters >= livingParty && lp * 100 / maxLp >= (row + 1) * 25;
                return !fight;
            }
            default:
            {
                int deadPct = 100 - livingMonsters * 100 / totalMonsters;
                int lostPct = 100 - lp * 100 / maxLp;
                return (deadPct + lostPct) / 2 >= morale;
            }
        }
    }

    /// <summary>
    /// Lock-pick success (RE 5C, door.c cb 0x5ab33): difficulty >= 100 is unpickable;
    /// effective Lockpicking >= difficulty auto-succeeds; otherwise it's a percent roll of
    /// <see cref="LockpickChancePercent"/>. Returns the pass/fail given a 0..99 roll.
    /// </summary>
    public static bool LockpickUnpickable(int difficulty) => difficulty >= 100;

    public static bool LockpickAutoSuccess(int skill, int difficulty)
        => skill > 0 && skill >= difficulty;

    /// <summary>Pick chance % = skill·(100−difficulty)/100 (clamped 0..100).</summary>
    public static int LockpickChancePercent(int skill, int difficulty)
    {
        if (skill <= 0 || difficulty >= 100)
            return 0;
        int chance = skill * (100 - difficulty) / 100;
        return Math.Clamp(chance, 0, 100);
    }

    /// <summary>
    /// Combat action points (strikes/round) at a given level (RE: ApplyLevelUp fcn.00037c22,
    /// sheet+0x11 = clamp(level / LevelsPerActionPoint(0xE2), 1, 4); divisor 0 ⇒ 1). AP is
    /// recomputed on every level-up.
    /// </summary>
    public static int ActionPointsForLevel(int level, int levelsPerActionPoint)
    {
        if (levelsPerActionPoint <= 0)
            return 1;
        return Math.Clamp(level / levelsPerActionPoint, 1, 4);
    }

    /// <summary>
    /// Spell mastery grows with use: mastery += MagicTalent on each cast, capped at the 10000
    /// ceiling (M = max(1,(mastery+50)/100) ⇒ 100% at 10000). RE post-cast fcn.000603ae.
    /// Negative talent is ignored.
    /// </summary>
    public const int MaxMastery = 10000;
    public static ushort GrowMastery(int current, int magicTalent)
        => (ushort)Math.Clamp(current + Math.Max(0, magicTalent), 0, MaxMastery);

    /// <summary>
    /// The final-boss surrender win-condition (RE: monster behaviour-strategy row 8 col2,
    /// fcn.0x51a2e — see _RE_ASK_SURRENDER.md). The end-game AI is intentionally unkillable;
    /// instead, after a surrender-capable monster's strike that deals damage, it "asks for
    /// surrender" once the party's conscious count drops to the threshold, which the original
    /// signals via combat outcome 4. threshold = max(1, partySize - 2); surrender fires when
    /// conscious &lt;= threshold. (The map-event opcode ask_surrender / 0x1C is inert — the
    /// 9 data bytes are never read; the logic is pure monster AI.)
    /// </summary>
    public static int SurrenderThreshold(int partySize) => Math.Max(1, partySize - 2);
    public static bool ShouldRequestSurrender(int partySize, int consciousCount)
        => consciousCount <= SurrenderThreshold(partySize);
}
