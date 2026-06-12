using System;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using UAlbion.Api;
using UAlbion.Api.Visual;

namespace UAlbion.Core.Veldrid.Textures;

public static class ImageSharpUtil
{
    // Blit into our own contiguous buffer then hand it to ImageSharp via LoadPixelData.
    // ImageSharp's allocator splits large images (e.g. the big 2D tilesets) into multiple
    // memory blocks, so DangerousTryGetSinglePixelMemory fails for them.
    public static Image<Rgba32> ToImageSharp(ReadOnlyImageBuffer<byte> from, ReadOnlySpan<uint> palette)
    {
        var buffer = new uint[from.Width * from.Height];
        var to = new ImageBuffer<uint>(from.Width, from.Height, from.Width, buffer);
        BlitUtil.BlitTiled8To32(from, to, palette, 255, 0);
        return Image.LoadPixelData<Rgba32>(MemoryMarshal.Cast<uint, Rgba32>(buffer), from.Width, from.Height);
    }

    public static Image<Rgba32> ToImageSharp(ReadOnlyImageBuffer<uint> from)
    {
        var buffer = new uint[from.Width * from.Height];
        var to = new ImageBuffer<uint>(from.Width, from.Height, from.Width, buffer);
        BlitUtil.BlitDirect(from, to);
        return Image.LoadPixelData<Rgba32>(MemoryMarshal.Cast<uint, Rgba32>(buffer), from.Width, from.Height);
    }

    public static Image<Rgba32> PackSpriteSheet(uint[] palette, int frameCount, GetFrameMethod<byte> getFrame)
    {
        ArgumentNullException.ThrowIfNull(getFrame);

        var layout = SpriteSheetUtil.ArrangeSpriteSheet(frameCount, 0, getFrame);
        if (layout.Layers > 1)
            throw new InvalidOperationException("Could not pack sprite sheet into a single layer");

        Image<Rgba32> image = new Image<Rgba32>(layout.Width, layout.Height);
        if (!image.DangerousTryGetSinglePixelMemory(out var rgbaMemory))
            throw new InvalidOperationException("Could not retrieve single span from Image");

        Span<uint> pixels = MemoryMarshal.Cast<Rgba32, uint>(rgbaMemory.Span);
        for (var i = 0; i < frameCount; i++)
        {
            var frame = getFrame(i);
            var (toX, toY, _) = layout.Positions[i];
            var target = pixels[(toX + toY * layout.Width)..];
            var to = new ImageBuffer<uint>(frame.Width, frame.Height, layout.Width, target);
            BlitUtil.BlitTiled8To32(frame, to, palette, 255, 0);
        }

        return image;
    }

    public static SimpleTexture<uint> FromImageSharp(IAssetId id, string name, Image<Rgba32> image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (!image.DangerousTryGetSinglePixelMemory(out var rgbaMemory))
            throw new InvalidOperationException("Could not retrieve single span from Image");

        var asUint = MemoryMarshal.Cast<Rgba32, uint>(rgbaMemory.Span);
        var result = new SimpleTexture<uint>(id, name, image.Width, image.Height, asUint);
        result.AddRegion(0, 0, image.Width, image.Height);
        return result;
    }
}