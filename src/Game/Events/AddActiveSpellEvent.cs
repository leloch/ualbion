using UAlbion.Api.Eventing;
using UAlbion.Formats.Ids;

namespace UAlbion.Game.Events;

/// <summary>
/// Adds an active-spell entry for a party member (the original's 0x153b3e table,
/// fcn.000607da): type 1 = physical-defense percent, type 2 = magic-resistance percent.
/// Duration is in GAME HOURS, decremented by the hour tick; an already-active entry is
/// NOT refreshed. MagicShield/PersonalProtection write types 1+2 with
/// hours = max(1, M·10/100), pct = M.
/// </summary>
[Event("add_active_spell")]
public class AddActiveSpellEvent : GameEvent
{
    public AddActiveSpellEvent(PartyMemberId memberId, int entryType, ushort hours, ushort percent)
    {
        MemberId = memberId;
        EntryType = entryType;
        Hours = hours;
        Percent = percent;
    }

    [EventPart("member")] public PartyMemberId MemberId { get; }
    [EventPart("type")] public int EntryType { get; }
    [EventPart("hours")] public ushort Hours { get; }
    [EventPart("pct")] public ushort Percent { get; }
}
