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
        Assert.True(HomeNpcIndex.TryDecode(26888, out var map, out var slot));
        Assert.Equal(280, map.Id);
        Assert.Equal(8, slot);

        Assert.True(HomeNpcIndex.TryDecode(26889, out var map2, out var slot2)); // Mellthas, slot 9
        Assert.Equal(280, map2.Id);
        Assert.Equal(9, slot2);
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
