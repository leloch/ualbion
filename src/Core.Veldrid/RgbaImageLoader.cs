using System;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using UAlbion.Api.Eventing;
using UAlbion.Api.Visual;
using UAlbion.Core.Visual;

namespace UAlbion.Core.Veldrid;

/// <summary>Decodes PNG bytes into a true-colour (SimpleTexture&lt;uint&gt;) texture for the modern UI.</summary>
public sealed class RgbaImageLoader : ServiceComponent<IRgbaImageLoader>, IRgbaImageLoader
{
    public ITexture LoadPng(byte[] data)
    {
        if (data == null || data.Length == 0)
            return null;
        try
        {
            using var img = Image.Load<Rgba32>(data);
            int w = img.Width, h = img.Height;
            var pixels = new uint[w * h];
            // Rgba32 packed value == R8G8B8A8 little-endian, matching SimpleTexture<uint>.
            img.CopyPixelDataTo(MemoryMarshal.AsBytes(pixels.AsSpan()));
            return new SimpleTexture<uint>(null, "rgba", w, h, pixels, [new Region(0, 0, w, h, w, h, 0)]);
        }
        catch (Exception ex) { Warn($"[RgbaImageLoader] decode failed: {ex.Message}"); return null; }
    }
}
