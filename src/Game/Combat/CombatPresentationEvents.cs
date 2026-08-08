using UAlbion.Api.Eventing;

namespace UAlbion.Game.Combat;

/// <summary>
/// Round-playback presentation: marks the combatant on TileIndex as the active one
/// (highlighted on the combat grid). TileIndex -1 clears. Raising this also clears any
/// hit feedback left over from the previous turn.
/// </summary>
public record CombatTurnHighlightEvent(int TileIndex) : EventRecord, IVerboseEvent;

/// <summary>
/// Planning-phase presentation: marks TileIndex as the target a queued action will hit, so
/// the player can see who they've ordered an attack/spell against before the round runs.
/// TileIndex -1 clears all target marks. Distinct from the active-turn highlight.
/// </summary>
public record CombatTargetHighlightEvent(int TileIndex) : EventRecord, IVerboseEvent;

/// <summary>
/// Round-playback presentation: damage/heal feedback for the occupant of a tile.
/// Amount 0 with Heal false is a miss (shown as "0" with no flash).
/// </summary>
public record CombatHitEvent(int TileIndex, int Amount, bool Killed, bool Heal) : EventRecord, IVerboseEvent;

/// <summary>
/// Round-playback presentation: a spell or magic item was cast (after SP gating).
/// CombatAudio maps the spell to its RE'd sample sequence.
/// </summary>
public record CombatCastEvent(UAlbion.Formats.Ids.SpellId SpellId) : EventRecord, IVerboseEvent;

/// <summary>
/// Round-playback presentation: play a spell's cast VISUAL — the school-intro orb, the
/// caster→target projectile flight and the on-target impact burst/flash (see
/// docs/re/RE_SPELLANIM.md / CombatSpellFx). CasterTile is the caster's grid tile; TargetTiles are
/// the recipient tiles (one for single-target, several for row/all). BattleView spawns the
/// effect sprites; the original blocks the round while they play — the remake plays them
/// fire-and-forget over the subsequent frames (timing is approximate, visuals are 1:1).
/// </summary>
public record CombatSpellCastEvent(
    UAlbion.Formats.Ids.SpellId SpellId,
    int CasterTile,
    System.Collections.Generic.IReadOnlyList<int> TargetTiles) : EventRecord, IVerboseEvent;

/// <summary>
/// Round-playback presentation: the occupant of TileIndex was banished (a Banish-demon
/// spell killed it) — the battle view plays the soul-rise dissolve (RE 6 worker 0xa1b20)
/// instead of the normal Die-and-freeze. Raised before the kill's CombatHitEvent.
/// </summary>
public record CombatSoulRiseEvent(int TileIndex) : EventRecord, IVerboseEvent;

/// <summary>
/// Planning-phase order: queue a one-row-forward Move for every live party member whose
/// destination tile is free (the combat menu's "Advance party"). Claim-mask rules apply —
/// members whose forward tile is occupied or already claimed keep their previous order.
/// </summary>
[UAlbion.Api.Eventing.Event("combat_advance_party", "Queue a one-row-forward Move for the whole party (the combat menu's Advance option)")]
public record CombatAdvancePartyEvent : EventRecord;

/// <summary>
/// Round-playback presentation: a combatant walks a (possibly multi-tile) path.
/// The battle state has already committed the final tile; the view lerps the sprite
/// along the waypoints (RE 5A: N engine frames per tile, N = Move anim length, with
/// the Move animation stepping every engine frame — vtable_2 sub 3 WalkPath 0x52019).
/// </summary>
public record CombatWalkEvent(int FromTile, System.Collections.Generic.IReadOnlyList<int> Waypoints) : EventRecord, IVerboseEvent;
