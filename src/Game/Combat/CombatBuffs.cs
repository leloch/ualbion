using System.Collections.Generic;
using UAlbion.Formats.Ids;

namespace UAlbion.Game.Combat;

/// <summary>
/// Per-battle temporary combat modifiers from buff spells. Battle clears this when a fight
/// starts and ticks durations down at the end of each round.
/// </summary>
/// <remarks>
/// All RE'd: the Berserk KIND maps to the original's "powered" flag at Combatant+0x04
/// bit 0 (doubles the AP attempt count, fcn.0004ef8b — Hurry sets it); durations come
/// from fcn.0004b8a1 (max(1, margin·base/100)+1 rounds); the Berserk SPELL's stat
/// bonuses are +half the current values (the original's ×1.5, see BerserkSpellEffect);
/// the shields use the percentage entry (ShieldPct) only.
/// </remarks>
public static class CombatBuffs
{
    public enum BuffKind
    {
        /// <summary>Doubles AP attempts per turn (original "powered" flag).</summary>
        Berserk,
        /// <summary>Initiative bonus (Hurry).</summary>
        Speed,
        /// <summary>Melee damage bonus (Boasting).</summary>
        Attack,
        /// <summary>Protection bonus (Magic Shield / Personal Protection).</summary>
        Defense,
        /// <summary>Strength bonus (Berserk's STR x1.5, expressed additively as +STR/2).</summary>
        Strength,
        /// <summary>Close-combat skill bonus — feeds the melee to-hit roll (Berserk).</summary>
        CloseCombatSkill,
        /// <summary>Ranged-combat skill bonus — feeds the ranged to-hit roll (Berserk).</summary>
        RangedCombatSkill,
        /// <summary>Critical-hit skill bonus — feeds the instant-kill crit roll (Berserk).</summary>
        CritSkill,
        /// <summary>
        /// Frost-line freeze (the original's buff kind 1, base 3): the target skips its
        /// turns until the duration expires. Battle's turn gate checks IsFrozen.
        /// </summary>
        Freeze,
    }

    sealed class Buff
    {
        public BuffKind Kind;
        public int Amount;
        public int RoundsLeft;
    }

    static readonly Dictionary<SheetId, List<Buff>> Active = [];

    /// <summary>
    /// View of Life (Dji-Kas spell 30): when active, the combat grid shows every
    /// monster's current LP. Battle-scoped like the buffs.
    /// </summary>
    public static bool ViewOfLife { get; set; }

    public static void Clear()
    {
        Active.Clear();
        ViewOfLife = false;
    }

    public static void Add(SheetId target, BuffKind kind, int amount, int rounds)
    {
        if (!Active.TryGetValue(target, out var list))
        {
            list = [];
            Active[target] = list;
        }

        // Re-casting refreshes rather than stacks — matches the binary on/off model the
        // original engine uses for conditions.
        var existing = list.Find(b => b.Kind == kind);
        if (existing != null)
        {
            existing.Amount = amount;
            existing.RoundsLeft = rounds;
        }
        else
        {
            list.Add(new Buff { Kind = kind, Amount = amount, RoundsLeft = rounds });
        }
    }

    public static int Bonus(SheetId target, BuffKind kind)
    {
        if (!Active.TryGetValue(target, out var list))
            return 0;
        var buff = list.Find(b => b.Kind == kind);
        return buff?.Amount ?? 0;
    }

    public static bool IsBerserk(SheetId target)
        => Active.TryGetValue(target, out var list) && list.Exists(b => b.Kind == BuffKind.Berserk);

    public static bool IsFrozen(SheetId target)
        => Active.TryGetValue(target, out var list) && list.Exists(b => b.Kind == BuffKind.Freeze);

    /// <summary>Decrement all buff durations; called by Battle at the end of each round.</summary>
    public static void TickRound()
    {
        foreach (var list in Active.Values)
            list.RemoveAll(b => --b.RoundsLeft <= 0);
    }
}
