using System;
using UAlbion.Config;
using UAlbion.Formats;
using UAlbion.Formats.Assets;
using UAlbion.Formats.Assets.Inv;
using UAlbion.Formats.Assets.Sheets;
using UAlbion.Game.State;

namespace UAlbion.Game.Magic;

/// <summary>
/// The original's dungeon light formula (RE 5C, fcn.0001473c): light mode = mapFlags & 3.
/// Mode 1 (dungeon) → light = min(100, MAX(Light-spell pct, party light-item total)),
/// +25 when the leader is an Iskai (clamped 100). Modes 0/2 → always 100.
/// The light-item total is the SUM of the Activate byte over every LightSource (type 22)
/// item in any member's equipment or backpack, capped at 100 (fcn.00038f24).
/// </summary>
public static class DungeonLighting
{
    public static int PartyLightItemTotal(IParty party, IAssetManager assets)
    {
        if (party == null || assets == null)
            return 0;
        int total = 0;
        foreach (var member in party.StatusBarOrder)
        {
            var inv = member?.Effective?.Inventory;
            if (inv == null)
                continue;
            foreach (var slot in inv.EnumerateAll())
            {
                if (slot == null || slot.Item.Type != AssetType.Item)
                    continue;
                var item = assets.LoadItem(slot.Item);
                if (item?.TypeId == ItemType.LightSource)
                    total += item.Activate;
                if (total >= 100)
                    return 100;
            }
        }
        return total;
    }

    public static int EffectiveLight(IGameState state, IParty party, IMapData map, IAssetManager assets)
    {
        int mode = map != null ? (int)map.Flags & 3 : 0;
        if (mode != 1)
            return 100; // outdoor / non-dungeon lighting

        int spellPct = state?.AmbientLightSpellPct ?? 0;
        int light = Math.Min(100, Math.Max(spellPct, PartyLightItemTotal(party, assets)));
        if (state?.Leader?.Race == PlayerRace.Iskai)
            light = Math.Min(100, light + 25);
        return light;
    }

    /// <summary>Query 0x21 semantics: light enough unless dungeon-lit and below 25.</summary>
    public static bool IsLightEnough(IGameState state, IParty party, IMapData map, IAssetManager assets)
        => EffectiveLight(state, party, map, assets) >= 25;
}
