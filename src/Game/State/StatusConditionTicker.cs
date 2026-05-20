using System;
using System.Collections.Generic;
using UAlbion.Api.Eventing;
using UAlbion.Config;
using UAlbion.Formats.Assets.Sheets;
using UAlbion.Formats.Ids;
using UAlbion.Formats.MapEvents;
using UAlbion.Game.Events;

namespace UAlbion.Game.State;

/// <summary>
/// Wears off transient status conditions over game-time, listening to <see cref="HourElapsedEvent"/>.
/// Persistent / serious conditions (Unconscious, Poisoned, Ill, Paralysed) are left alone — they
/// require explicit cures from spells or items in the original game's design.
/// </summary>
/// <remarks>
/// **Deliberately more lenient than the original.** Confirmed via radare2 of
/// `fcn.0003822d` (the rest-recovery clear-conditions function): Albion's original engine
/// uses a **binary on-rest model** — Unconscious / Paralysed / Insane / Asleep / Panicking
/// / Fleeing are cleared in a single batch when the party sleeps; **there is no
/// per-condition timer in the original**. UAlbion's ticker auto-clears these over game-time
/// to be friendlier to non-RPG-genre players who don't know they need to rest.
///
/// Conditions NOT cleared by rest in the original (and not in our timer): Poisoned, Ill,
/// Exhausted, Intoxicated, Blind, Irritated. Those require explicit cures (HealPoisoning,
/// HealBlindness, etc.) — already implemented in `DjiKasSpells.RegisterAll`.
///
/// Hour durations below are PLACEHOLDER. The original has zero data here — any value is a
/// judgment call about pacing.
/// <list type="bullet">
///   <item><b>Asleep</b> — 1 hour. Original requires explicit rest.</item>
///   <item><b>Intoxicated</b> — 6 hours. Not in the rest-clear list either; original may
///     have a tavern-specific clear we haven't found yet.</item>
///   <item><b>Panicking</b> — 1 hour. Battle-flag, cleared by rest in original.</item>
///   <item><b>Insane</b> — 12 hours. Cleared by rest in original.</item>
///   <item><b>Fleeing</b> — 1 hour. Cleared by rest in original.</item>
/// </list>
/// </remarks>
public class StatusConditionTicker : GameComponent
{
    // Per-condition hours remaining until it clears. Conditions not in this dictionary
    // are not auto-cleared (e.g. Unconscious/Poisoned/Ill/Paralysed/Blind/Irritated).
    static readonly Dictionary<PlayerCondition, int> DefaultHours = new()
    {
        [PlayerCondition.Asleep]      = 1,
        [PlayerCondition.Intoxicated] = 6,
        [PlayerCondition.Panicking]   = 1,
        [PlayerCondition.Insane]      = 12,
        [PlayerCondition.Fleeing]     = 1,
    };

    // (SheetId, Condition) → hours-remaining counter. Decremented each HourElapsedEvent.
    readonly Dictionary<(SheetId, PlayerCondition), int> _timers = new();

    public StatusConditionTicker()
    {
        On<HourElapsedEvent>(_ => TickAll());
    }

    void TickAll()
    {
        var party = TryResolve<IParty>();
        if (party == null) return;

        // Mirror the dictionary up-front so we can mutate during iteration.
        var snapshot = new List<(SheetId, PlayerCondition, int)>();
        foreach (var ((sheetId, cond), hours) in _timers)
            snapshot.Add((sheetId, cond, hours));

        foreach (var (sheetId, cond, _) in snapshot)
        {
            // Re-check the live condition each tick — something else may have cleared it
            // (e.g. a Heal-status spell), in which case we just drop the timer.
            if (!HasCondition(party, sheetId, cond))
            {
                _timers.Remove((sheetId, cond));
                continue;
            }

            var key = (sheetId, cond);
            _timers[key]--;
            if (_timers[key] > 0) continue;

            // Timer expired — clear the condition.
            _timers.Remove(key);
            var target = (TargetId)(AssetId)sheetId;
            Raise(new ChangeStatusEvent(target, cond, NumericOperation.SubtractAmount, 1));
        }

        // Discover newly-applied conditions on party members and seed timers for them.
        foreach (var pm in party.StatusBarOrder)
        {
            if (pm?.Apparent?.Combat == null) continue;
            var sheetId = (SheetId)(AssetId)pm.Id;
            foreach (var (cond, defaultHours) in DefaultHours)
            {
                if ((pm.Apparent.Combat.Conditions & cond.ToFlag()) == 0) continue;
                var key = (sheetId, cond);
                if (!_timers.ContainsKey(key))
                    _timers[key] = defaultHours;
            }
        }
    }

    static bool HasCondition(IParty party, SheetId id, PlayerCondition cond)
    {
        foreach (var pm in party.StatusBarOrder)
            if ((SheetId)(AssetId)pm.Id == id)
                return (pm.Apparent?.Combat?.Conditions & cond.ToFlag()) != 0;
        return false;
    }
}
