using UAlbion.Api.Visual;

namespace UAlbion.Core.Visual;

/// <summary>
/// Loads an RGBA image (e.g. a save-game screenshot PNG) into a true-colour texture for the modern
/// high-res UI. Implemented in the rendering backend (it needs the image codec).
/// </summary>
public interface IRgbaImageLoader
{
    /// <summary>Decode PNG bytes into a single-region RGBA texture, or null on failure.</summary>
    ITexture LoadPng(byte[] data);
}
