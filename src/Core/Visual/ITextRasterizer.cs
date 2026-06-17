using UAlbion.Api.Visual;

namespace UAlbion.Core.Visual;

/// <summary>
/// Rasterises crisp, native-resolution text into a true-colour (RGBA) texture, for the modern
/// high-res UI (the bespoke menu) - distinct from the game's 360x240 bitmap-font pipeline.
/// </summary>
public interface ITextRasterizer
{
    /// <summary>Render <paramref name="text"/> at the given pixel height in the given colour.
    /// Returns a single-region RGBA texture sized to the text (cached by text/size/colour).</summary>
    ITexture Render(string text, float pixelHeight, byte r, byte g, byte b, byte a = 255);

    /// <summary>Pixel width the given text would occupy at the given height (for layout).</summary>
    float Measure(string text, float pixelHeight);
}
