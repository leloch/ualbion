using UAlbion.Api.Eventing;

namespace UAlbion.Game.Combat;

[Event("begin_combat_round", "Run one combat round using actions queued via queue_combat_action")]
public record BeginCombatRoundEvent : EventRecord;