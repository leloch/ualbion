using UAlbion.Game.Entities.Map3D;
using Xunit;

namespace UAlbion.Game.Tests;

/// <summary>
/// Automap glyph-region selection (RE automap.c: tile pass 0x5e12c, wall connection
/// 0x5e8a1, goto marker 0x5e7e5). Region count 256 = a normal tile-set; small counts
/// exercise the graceful-degrade fallback.
/// </summary>
public class AutomapTests
{
    // A real AUTOGFX tile-set is large enough to hold the wall-mask band (560..575) and
    // the party-marker glyph (213); only undersized sets hit the degrade path.
    const int FullSet = 1024;

    [Theory]
    [InlineData(false, false, false, false, 0)]
    [InlineData(true,  false, false, false, 1)]  // N
    [InlineData(false, true,  false, false, 2)]  // E
    [InlineData(false, false, true,  false, 4)]  // S
    [InlineData(false, false, false, true,  8)]  // W
    [InlineData(true,  true,  true,  true,  15)] // all four
    [InlineData(true,  false, true,  false, 5)]  // N+S corridor
    public void ConnectionMask_PacksCardinalBits(bool n, bool e, bool s, bool w, int expected)
        => Assert.Equal(expected, AutomapGlyphs.ConnectionMask(n, e, s, w));

    [Fact]
    public void WallGlyph_Type1_IsBasePlusConnectionMask()
    {
        Assert.Equal(560, AutomapGlyphs.WallGlyph(1, 0, FullSet));
        Assert.Equal(575, AutomapGlyphs.WallGlyph(1, 15, FullSet)); // 560 + 15
    }

    [Theory]
    [InlineData(2)]
    [InlineData(10)]
    [InlineData(19)]
    public void WallGlyph_MarkerTypes_MapToTheirOwnIndex(int autoGfxType)
        => Assert.Equal(autoGfxType, AutomapGlyphs.WallGlyph(autoGfxType, 0, FullSet));

    [Theory]
    [InlineData(0)]   // no glyph
    [InlineData(20)]  // out of the 2..19 marker band
    [InlineData(-1)]
    public void WallGlyph_UnmappedTypes_ReturnMinusOne(int autoGfxType)
        => Assert.Equal(-1, AutomapGlyphs.WallGlyph(autoGfxType, 0, FullSet));

    [Fact]
    public void WallGlyph_DegradesWhenGlyphExceedsRegionCount()
    {
        // A 12-region set can't show glyph 560 → falls back to min(count-1, 8) = 8.
        Assert.Equal(8, AutomapGlyphs.WallGlyph(1, 0, 12));
        // A tiny 5-region set falls to count-1 = 4.
        Assert.Equal(4, AutomapGlyphs.WallGlyph(1, 0, 5));
    }

    [Fact]
    public void PartyMarkerRegion_Is213_OnAFullSet_AndClampsOnSmallSets()
    {
        Assert.Equal(213, AutomapGlyphs.PartyMarkerRegion(FullSet));
        Assert.Equal(9, AutomapGlyphs.PartyMarkerRegion(10));
    }

    [Fact]
    public void GotoGlyph_IsEighteen()
        => Assert.Equal(18, AutomapGlyphs.GotoGlyph);
}
