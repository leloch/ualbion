using UAlbion.Api.Eventing;
using UAlbion.Formats.Assets.Inv;
using UAlbion.Formats.Ids;

namespace UAlbion.Game.Events;

/// <summary>
/// Consumes one round of matching ammunition for a ranged strike (MAIN.EXE
/// fcn.0004f3e2/fcn.0004f4da: the backpack stack depletes first — it refills the
/// equipped slot in the original — then the equipped stack itself).
/// </summary>
[Event("consume_ammo")]
public class ConsumeAmmoEvent : GameEvent
{
    public ConsumeAmmoEvent(PartyMemberId memberId, AmmunitionType ammoType)
    {
        MemberId = memberId;
        AmmoType = ammoType;
    }

    [EventPart("member")] public PartyMemberId MemberId { get; }
    [EventPart("ammoType")] public AmmunitionType AmmoType { get; }
}
