using UAlbion.Api.Eventing;

namespace UAlbion.Game.Events.Inventory;

// Raised when a SKILL-based lockpick attempt fails, so the inventory screen manager can run the
// trap check. A lock is "trapped" iff its chest/door event has a false-branch (NextIfFalse) — the
// per-chest trap chain (RE'd in docs/re/RE_CHEST_TRAP.md: there's no trap byte; the false branch IS the
// trap). On a failed pick of a trapped lock the manager rolls Dexterity to evade; a failed evade
// fires the trap chain. The key/Lockpick-item path bypasses the trap, as in the original.
[Event("inv:lockpick_failed")]
public class LockPickFailedEvent : GameEvent { }
