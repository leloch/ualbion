using System;

namespace UAlbion.Game.Entities.Map3D;

/// <summary>
/// Pure automap glyph-selection rules (RE: automap.c tile pass fcn.0005e12c, wall
/// connection fcn.0005e8a1, goto marker fcn.0005e7e5). Extracted from AutomapDialog so the
/// region-index arithmetic can be unit-tested without a live map/texture.
/// </summary>
public static class AutomapGlyphs
{
    public const int WallMaskGlyphBase = 560; // AUTOGFX 0x230 + connection mask
    public const int GotoGlyph = 18;          // goto-point marker glyph

    /// <summary>
    /// Connection mask for a connectable wall: bit0=N, bit1=E, bit2=S, bit3=W, set when
    /// that cardinal neighbour is discovered and sight-blocking.
    /// </summary>
    public static int ConnectionMask(bool north, bool east, bool south, bool west)
        => (north ? 1 : 0) | (east ? 2 : 0) | (south ? 4 : 0) | (west ? 8 : 0);

    /// <summary>
    /// Glyph region for a wall by its <c>AutoGfxType</c>: type 1 = connectable wall
    /// (base 560 + connection mask), types 2..19 = that AUTOGFX marker glyph, anything
    /// else = none (-1). A glyph past the tile-set's region count degrades to min(count-1, 8).
    /// </summary>
    public static int WallGlyph(int autoGfxType, int connectionMask, int regionCount)
    {
        int glyph = autoGfxType switch
        {
            1 => WallMaskGlyphBase + connectionMask,
            >= 2 and <= 19 => autoGfxType,
            _ => -1
        };
        if (glyph >= regionCount)
            glyph = Math.Min(regionCount - 1, 8); // set lacks the frame — degrade gracefully
        return glyph;
    }

    /// <summary>The remake's party-position marker region (usability affordance — the
    /// original uses a UI cursor instead).</summary>
    public static int PartyMarkerRegion(int regionCount) => Math.Min(regionCount - 1, 213);
}
