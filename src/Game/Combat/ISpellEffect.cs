using UAlbion.Api.Eventing;
using UAlbion.Formats.Ids;
using UAlbion.Game.State;

namespace UAlbion.Game.Combat;

/// <summary>
/// Per-spell effect handler. One implementation per spell, registered with
/// <see cref="SpellEffectRegistry"/>. The runtime resolves the right effect by spell-id
/// when the caster picks "Cast" from combat.
/// </summary>
/// <remarks>
/// The interface deliberately mirrors what we decoded from the original engine's
/// spell-cast flow: a spell takes the caster and a target description (which may be
/// a single tile, a group of tiles, or "everyone"), and returns a result describing
/// what happened (consumed-SP, hit/missed, damage-dealt, etc).
/// </remarks>
public interface ISpellEffect
{
    /// <summary>The spell this effect handler is registered for.</summary>
    SpellId SpellId { get; }

    /// <summary>
    /// Apply the spell. Returns whether the cast succeeded (in the original engine's
    /// sense — see the AP-loop pattern in MAIN.EXE fcn.0004ef8b: failed casts retry on
    /// the next AP point, successful casts consume the action).
    /// </summary>
    SpellCastOutcome Apply(SpellCastContext context);
}

/// <summary>Per-cast context passed to <see cref="ISpellEffect.Apply"/>.</summary>
public sealed class SpellCastContext
{
    public ICombatParticipant Caster { get; init; }
    public ICombatParticipant Target { get; init; }
    public int CombatTargetPosition { get; init; }
    /// <summary>Caster's spell-strength in the relevant school (0..15 byte from CharacterSheet).</summary>
    public byte SpellStrength { get; init; }

    /// <summary>
    /// The RE'd mastery multiplier M = max(1, (mastery + 50) / 100) where mastery is the
    /// per-spell 0..10000 value (MagicSkills.SpellStrengths; sheet offset
    /// 0x140 + school*60 + (n-1)*2). Every spell magnitude in the original is
    /// max(1, M*K/100) for a per-spell constant K — see _RE_COMBAT.md "Punch-list RE"
    /// item 1 (cast core fcn.0005fdf7). 100 = full mastery.
    /// </summary>
    public int MasteryMultiplier { get; init; } = 1;
    /// <summary>Random helper — provides a uniform int in [0, max).</summary>
    public System.Func<int, int> Random { get; init; }
    /// <summary>
    /// Raise an event so it can be routed through GameState/SheetApplier — used by effects
    /// that mutate persistent state (e.g. clearing a status condition). Null in tests that
    /// don't exercise the mutation path; effects must tolerate a null delegate.
    /// </summary>
    public System.Action<IEvent> RaiseEvent { get; init; }

    /// <summary>
    /// Apply damage to a combat participant through the battle's HP tracking (which owns
    /// the monster HP shadow that DataChangeEvent cannot reach). Null outside combat —
    /// effects fall back to RaiseEvent-based health changes for party targets.
    /// </summary>
    public System.Action<ICombatParticipant, int> ApplyDamage { get; init; }

    /// <summary>Heal a combat participant through the battle's HP tracking. Null outside combat.</summary>
    public System.Action<ICombatParticipant, int> ApplyHeal { get; init; }

    /// <summary>Place a damage trap on a combat tile (trap/mine spells). Null outside combat.</summary>
    public System.Action<int, int> PlaceTrap { get; init; }

    /// <summary>Remove any trap from a combat tile (remove-trap spells). Null outside combat.</summary>
    public System.Action<int> RemoveTrap { get; init; }

    /// <summary>
    /// All live combatants on the side OPPOSING the caster — used by whole-side spells
    /// (GoddessWrath's random-kill selection). Null outside combat.
    /// </summary>
    public System.Func<System.Collections.Generic.IReadOnlyList<ICombatParticipant>> GetLiveEnemies { get; init; }

    /// <summary>
    /// Kill a combatant outright (the original's instant-kill fcn.0004e247 — LP wipe with
    /// normal death/XP handling). Used by GoddessWrath and the Banish-demon family.
    /// Null outside combat.
    /// </summary>
    public System.Action<ICombatParticipant> InstantKill { get; init; }

    /// <summary>
    /// All combatants on the CASTER's side (in combat) or the whole party (outside) —
    /// used by party-wide spells (Recuperation). Null when unavailable.
    /// </summary>
    public System.Func<System.Collections.Generic.IReadOnlyList<ICombatParticipant>> GetAllies { get; init; }

    /// <summary>
    /// The party's hours-awake counter (MAIN.EXE 0x153cd2) — gates Recuperation (&gt; 8 h).
    /// Null when unavailable.
    /// </summary>
    public System.Func<int> HoursAwake { get; init; }

    /// <summary>
    /// Add (positive) or drain (negative) spell points on a combatant — party members
    /// route through Mana events, monsters through the battle SP shadow. Null outside
    /// combat (party-only effects fall back to RaiseEvent). Used by Steal Magic.
    /// </summary>
    public System.Action<ICombatParticipant, int> ModifySp { get; init; }

    /// <summary>
    /// Active-spell percent lookup (participant, type) — type 2 feeds the success gate's
    /// MagicResistance boost for party targets (the original's 0x153b3e table). Null when
    /// no game state is available (tests).
    /// </summary>
    public System.Func<ICombatParticipant, int, int> GetActiveSpellPct { get; init; }
}

public enum SpellCastOutcome
{
    /// <summary>Spell landed and consumed the action.</summary>
    Hit,
    /// <summary>Spell was resisted/dodged. Original engine retries with remaining AP.</summary>
    Resisted,
    /// <summary>Spell failed to dispatch (no implementation, bad target, etc).</summary>
    Failed,
}
