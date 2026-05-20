using System;
using System.Collections.Generic;
using UAlbion.Formats.Assets.Save;
using UAlbion.Formats.Assets.Sheets;

namespace UAlbion.Game.Combat;

/// <summary>
/// Monster combat AI, reverse-engineered from MAIN.EXE (combat.c / comobs.c).
/// See <c>_RE_COMBAT.md</c> for the disassembly notes that justify these formulas.
/// </summary>
public static class MonsterAi
{
    /// <summary>
    /// Per-combatant turn gate (mirror of <c>fcn.0004bf77</c>).
    /// Returns the action to take given the combatant's current status conditions,
    /// or <see cref="CombatAction.None"/> if the combatant skips its turn this round
    /// (e.g. Asleep).
    /// </summary>
    /// <remarks>
    /// Bits checked (from the original code):
    /// <list type="bullet">
    ///   <item>0x200 = Asleep    → skip turn</item>
    ///   <item>0x100 = Panicking → flee behavior</item>
    ///   <item>0x400 = Insane    → random action (50/50 melee or cast)</item>
    /// </list>
    /// </remarks>
    public static StatusOutcome ResolveStatusBehavior(PlayerConditions conds)
    {
        if ((conds & PlayerConditions.Asleep) != 0) return StatusOutcome.SkipTurn;
        if ((conds & PlayerConditions.Panicking) != 0) return StatusOutcome.Flee;
        if ((conds & PlayerConditions.Insane) != 0) return StatusOutcome.InsaneRandomAct;
        return StatusOutcome.Normal;
    }

    /// <summary>
    /// 50/50 random pick between two actions (mirror of <c>fcn.0004c1e4</c>, used for Insane mobs).
    /// The original uses <c>(rand() % 8) &lt; 4</c> which is mathematically a 50% split; we expose
    /// the same predicate so the caller can plug in any RNG.
    /// </summary>
    public static bool ChooseActionAFirst(int randomValue) => (randomValue & 0x7) < 4;

    /// <summary>
    /// Stat-randomisation formula used by <see cref="MonsterFactory.RandomiseStat"/> and
    /// verified against the original: <c>(rand() % 11 + 95) * value / 100</c>.
    /// </summary>
    /// <remarks>
    /// Equivalent to "vary the stat by ±5 % uniformly". <c>statRandomisationPercentage</c>
    /// game-var must be 10 to match the original. Lower values produce tighter ranges.
    /// </remarks>
    public static int RandomiseAttribute(int value, int randomValue, int percentage = 10)
    {
        if (value <= 0) return value;
        int offset = 100 - percentage / 2;
        int modulus = percentage + 1;
        int rolled = (((randomValue % modulus) + modulus) % modulus) + offset;
        return rolled * value / 100;
    }

    /// <summary>
    /// Pick a random set bit in <paramref name="mask"/> using <paramref name="randomValue"/>
    /// (mirror of <c>fcn.000512e7</c>). Returns -1 if no bits are set.
    /// </summary>
    public static int PickRandomSetBit(uint mask, int randomValue)
    {
        if (mask == 0) return -1;
        // Count set bits in the 30-bit grid (the original iterates 0..29 explicitly).
        int count = 0;
        for (int i = 0; i < 30; i++)
            if (((mask >> i) & 1) != 0)
                count++;
        if (count == 0) return -1;

        // Pick the n-th set bit where n = rand() % count.
        int target = ((randomValue % count) + count) % count;
        for (int i = 0; i < 30; i++)
        {
            if (((mask >> i) & 1) == 0) continue;
            if (target == 0) return i;
            target--;
        }
        return -1;
    }

    /// <summary>
    /// Build the enemy-target mask for the combatant at <paramref name="selfPosition"/>
    /// (mirror of <c>fcn.0004db61</c>). Returns a 30-bit mask: bit N = position N is occupied
    /// by an enemy combatant.
    /// </summary>
    /// <param name="selfPosition">The acting combatant's grid position 0..29.</param>
    /// <param name="selfTeam">0 = party / 1 = monster.</param>
    /// <param name="positionTeams">For each of the 30 grid positions, the team of the
    /// occupant (-1 if empty, 0 = party, 1 = monster).</param>
    public static uint GetEnemyMask(int selfPosition, int selfTeam, int[] positionTeams)
    {
        if (positionTeams == null || positionTeams.Length != SavedGame.CombatRows * SavedGame.CombatColumns)
            throw new ArgumentException($"positionTeams must be a {SavedGame.CombatRows * SavedGame.CombatColumns}-element array", nameof(positionTeams));

        uint mask = 0;
        for (int row = 0; row < SavedGame.CombatRows; row++)
        {
            for (int col = 0; col < SavedGame.CombatColumns; col++)
            {
                int pos = row * SavedGame.CombatColumns + col;
                if (pos == selfPosition) continue;
                int team = positionTeams[pos];
                if (team < 0) continue;       // empty tile
                if (team == selfTeam) continue; // ally
                mask |= 1u << pos;
            }
        }
        return mask;
    }

    /// <summary>Outcome of the per-combatant status gate.</summary>
    public enum StatusOutcome
    {
        Normal,
        SkipTurn,
        Flee,
        InsaneRandomAct,
    }

    /// <summary>
    /// Normal-monster action-availability bit. The original engine stores a bitmask
    /// at Combatant offset +0x06 listing which actions this mob can do (a wolf can't
    /// cast, a spectre can't melee, etc).
    /// </summary>
    [System.Flags]
    public enum AvailableActions : ushort
    {
        None = 0,
        /// <summary>Bit 0x01 — corresponds to the original engine's "summon/spawn" action (vtable_1[5]).</summary>
        Summon = 0x01,
        /// <summary>Bit 0x02 — action type 2 (handler `fcn.00050651`); semantics not yet decoded.</summary>
        Action2 = 0x02,
        /// <summary>Bit 0x04 — action type 4 (handler `fcn.00050488`); semantics not yet decoded.</summary>
        Action4 = 0x04,
    }

    /// <summary>
    /// 16-entry random-weight table for normal-monster action selection — verbatim from the
    /// original engine's MAIN.EXE address `0x4facb`. The original picks an index 0..15 via
    /// <c>rand() &amp; 0xf</c>, looks up the bit-flag at this index, and if the mob has that
    /// action available, dispatches to the type-specific handler.
    /// </summary>
    public static readonly ushort[] AiWeightTable =
    {
        // Indices 0..5: bit 0x01 (Summon / spawn) — 6/16 = 37.5 %
        0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
        // Indices 6..7: bit 0x02 — 2/16 = 12.5 %
        0x02, 0x02,
        // Indices 8..13: bit 0x04 — 6/16 = 37.5 %
        0x04, 0x04, 0x04, 0x04, 0x04, 0x04,
        // Indices 14..15: bit 0x02 — 2/16 = 12.5 % (total bit-2: 25 %)
        0x02, 0x02,
    };

    /// <summary>
    /// Mirror of `fcn.0004fc99` — normal-monster AI loop.
    /// Repeatedly picks a weighted-random action bit from <see cref="AiWeightTable"/>. If
    /// the mob has that action available, attempts to commit (caller supplies a delegate
    /// that returns true if the action could be set up). Picked bits are cleared from the
    /// available mask on each attempt so we don't keep retrying the same bit forever.
    /// </summary>
    /// <returns>The bit that was committed, or <see cref="AvailableActions.None"/> if no
    /// action could be taken.</returns>
    public static AvailableActions ChooseNormalAction(
        AvailableActions available,
        System.Func<int> rng,
        System.Func<AvailableActions, bool> tryCommit)
    {
        System.ArgumentNullException.ThrowIfNull(rng);
        System.ArgumentNullException.ThrowIfNull(tryCommit);

        // Hard iteration cap so a degenerate RNG (always returning the same index that maps
        // to an unavailable bit) cannot infinite-loop. The original engine doesn't need this
        // because rand() always cycles eventually; we keep it for defensive cleanliness and
        // for tests with scripted RNGs.
        const int MaxAttempts = 256;
        int attempts = 0;

        while (available != AvailableActions.None && attempts++ < MaxAttempts)
        {
            int idx = rng() & 0xf;
            var bit = (AvailableActions)AiWeightTable[idx];
            if ((available & bit) == 0) continue;            // not available — re-roll
            available &= ~bit;                                // remove from pool
            if (tryCommit(bit))
                return bit;
        }
        return AvailableActions.None;
    }
}
