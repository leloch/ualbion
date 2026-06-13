using UAlbion.Config;
using UAlbion.Formats.Ids;
using UAlbion.Game.State;
using Xunit;

namespace UAlbion.Game.Tests;

/// <summary>
/// Home-NPC-index encoding (RE 5D §6). Anchored to the decompiled Edjirr recruit chain where
/// remove_party_member carries Unk6 = 26888 and the matching modify_npc_off targets slot 8 on
/// the map whose id is 280 (26888 = 280*96 + 8).
/// </summary>
public class HomeNpcIndexTests
{
    public HomeNpcIndexTests()
    {
        AssetMapping.GlobalIsThreadLocal = true;
        AssetMapping.Global.Clear().RegisterAssetType(typeof(Base.Map), AssetType.Map);
    }

    [Fact]
    public void Decode_MatchesDecompiledEdjirrRecruitChain()
    {
        // The script literal 26888 is (mapId-1)*96+slot = (281-1)*96+8 (Edjirr.Id == 281).
        // Decode must return MapId 281 so SetNpcDisabled clears the SAME FlagSet bit
        // (281*96+8 = 26984) the join's `modify_npc_off Set 8 Map.Edjirr` set — not 280
        // (the earlier off-by-one that cleared 26888 and soft-locked re-recruitment).
        Assert.True(HomeNpcIndex.TryDecode(26888, out var map, out var slot));
        Assert.Equal(281, map.Id);
        Assert.Equal(8, slot);

        Assert.True(HomeNpcIndex.TryDecode(26889, out var map2, out var slot2)); // Mellthas, slot 9
        Assert.Equal(281, map2.Id);
        Assert.Equal(9, slot2);
    }

    [Fact]
    public void LeaveBit_MatchesJoinModifyNpcOffBit()
    {
        // The bug the audit caught: leave must clear the EXACT RemovedNpcs FlagSet bit the
        // join's modify_npc_off set, or the home NPC never reappears (recruit soft-lock).
        const int npcsPerMap = HomeNpcIndex.NpcsPerMap;
        const int edjirrId = 281, slot = 8;
        // Join — `modify_npc_off Set 8 Map.Edjirr` → FlagSet key = MapId.Id*96 + slot.
        int joinBit = edjirrId * npcsPerMap + slot;
        // Leave — `remove_party_member ... 26888` → decode → SetNpcDisabled(map, slot) →
        // FlagSet key = map.Id*96 + slot. Must equal joinBit.
        Assert.True(HomeNpcIndex.TryDecode(26888, out var map, out var s));
        int leaveBit = map.Id * npcsPerMap + s;
        Assert.Equal(joinBit, leaveBit);
    }

    [Fact]
    public void Zero_IsNoHomeNpc()
    {
        Assert.False(HomeNpcIndex.TryDecode(0, out var map, out var slot));
        Assert.True(map.IsNone);
        Assert.Equal(0, slot);
    }

    [Theory]
    [InlineData(110, 0)]
    [InlineData(110, 8)]
    [InlineData(280, 95)] // last slot in a map
    [InlineData(1, 1)]
    public void EncodeDecode_RoundTrips(int mapId, int slot)
    {
        ushort index = HomeNpcIndex.Encode(new MapId(mapId), slot);
        Assert.True(HomeNpcIndex.TryDecode(index, out var map, out var decoded));
        Assert.Equal(mapId, map.Id);
        Assert.Equal(slot, decoded);
    }

    [Fact]
    public void Slot_StaysWithinMapStride()
    {
        // Slot is always 0..95 regardless of map; map carries the high part.
        ushort index = HomeNpcIndex.Encode(new MapId(5), 95);
        HomeNpcIndex.TryDecode(index, out var map, out var slot);
        Assert.Equal(5, map.Id);
        Assert.Equal(95, slot);
        Assert.True(slot < HomeNpcIndex.NpcsPerMap);
    }
}
