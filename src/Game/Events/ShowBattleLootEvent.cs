using System.Collections.Generic;
using UAlbion.Api.Eventing;
using UAlbion.Formats.Ids;

namespace UAlbion.Game.Events;

/// <summary>
/// Raised at combat victory with the accumulated loot (dead monsters' equipment +
/// backpack + gold + rations — MAIN.EXE fcn.0004e124). The DialogManager opens the
/// battle-loot window; "take all" distributes it to the party.
/// </summary>
public class ShowBattleLootEvent : GameEvent
{
    public ShowBattleLootEvent(IReadOnlyList<(ItemId Item, ushort Amount)> items, int gold, int rations)
    {
        Items = items;
        Gold = gold;
        Rations = rations;
    }

    public IReadOnlyList<(ItemId Item, ushort Amount)> Items { get; }
    public int Gold { get; }
    public int Rations { get; }
}
