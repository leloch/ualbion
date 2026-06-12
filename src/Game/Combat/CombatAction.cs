namespace UAlbion.Game.Combat;

/// <summary>
/// Per-round combat action kinds. Values match the action_kind field at offset 0x4E of the
/// original engine's in-memory Combatant struct (decoded from MAIN.EXE — see
/// <c>_RE_COMBAT.md</c>: action vtable at 0x13e196 dispatches on these).
/// </summary>
public enum CombatAction : ushort
{
    /// <summary>No action chosen yet this round.</summary>
    None = 0,

    /// <summary>Melee attack on a single target tile.</summary>
    Melee = 1,

    /// <summary>Cast a spell from school 5 (`fcn.0004e9f5`).</summary>
    CastSchool5 = 2,

    /// <summary>Cast a spell from school 6 (`fcn.0004ef8b`).</summary>
    CastSchool6 = 3,

    /// <summary>Retreat — back-row only (row 0 for mobs, row 4 for party). Sets Fleeing
    /// condition. Decoded from <c>fcn.0004f5d3</c>.</summary>
    Retreat = 4,

    /// <summary>Summon — copies the caster into a free monster slot. Mob-only.
    /// Decoded from <c>fcn.0004f6a2</c>.</summary>
    Summon = 5,

    /// <summary>Use Item — reads slot index from combatant +0x56 and dispatches the
    /// item's effect. Decoded from <c>fcn.0004f829</c>.</summary>
    UseItem = 6,

    /// <summary>Targeted-tile action (likely throw / ranged-non-magic).
    /// Partial decode at <c>fcn.0004eac1</c>.</summary>
    TargetedTile = 7,

    // --- UAlbion-side extensions (not original action_kind values) -------------------
    // The original engine folds these into the universal action executor's sub-actions
    // (movement = path-find sub-action 2 at fcn.00051b51; casting = per-school kinds 2/3).
    // UAlbion models them as distinct queued actions carrying SpellId/ItemId payload.

    /// <summary>Move to the chosen empty target tile (original: path-find sub-action).</summary>
    Move = 8,

    /// <summary>Cast the spell in <c>QueueCombatActionEvent.Spell</c> at the target tile.
    /// (Original used per-school action kinds 2/3; the school is derived from the spell.)</summary>
    CastSpell = 9,
}
