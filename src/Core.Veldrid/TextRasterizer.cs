using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using StbTrueTypeSharp;
using UAlbion.Api.Visual;
using UAlbion.Api.Eventing;
using UAlbion.Core.Visual;

namespace UAlbion.Core.Veldrid;

/// <summary>
/// TTF-backed text rasteriser for the modern high-res UI. Rasterises glyphs with StbTrueType (public
/// domain) into RGBA (SimpleTexture&lt;uint&gt;) at native resolution, so the bespoke menu gets crisp
/// scalable text instead of the 360x240 bitmap font. Results are cached by text/size/colour.
/// </summary>
public sealed unsafe class TextRasterizer : ServiceComponent<ITextRasterizer>, ITextRasterizer, IDisposable
{
    static readonly string[] FontCandidates =
    [
        "constan.ttf", "georgia.ttf", "georgiab.ttf", "times.ttf", "seguisb.ttf", "segoeui.ttf", "arial.ttf"
    ];

    readonly Dictionary<(string, int, uint), ITexture> _cache = [];
    byte[] _ttf;
    GCHandle _pin;
    StbTrueType.stbtt_fontinfo _font;
    bool _init;
    bool _haveFont;

    void EnsureFont()
    {
        if (_init) return;
        _init = true;

        try
        {
            string dir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            string path = null;
            foreach (var c in FontCandidates)
            {
                var p = Path.Combine(dir, c);
                if (File.Exists(p)) { path = p; break; }
            }
            if (path == null) { Warn("[TextRasterizer] no system TTF found; high-res text disabled"); return; }

            _ttf = File.ReadAllBytes(path);
            _pin = GCHandle.Alloc(_ttf, GCHandleType.Pinned);
            _font = new StbTrueType.stbtt_fontinfo();
            var p0 = (byte*)_pin.AddrOfPinnedObject();
            if (StbTrueType.stbtt_InitFont(_font, p0, 0) == 0)
            {
                Warn($"[TextRasterizer] failed to init font {path}");
                return;
            }
            _haveFont = true;
            Info($"[TextRasterizer] using {Path.GetFileName(path)}");
        }
        catch (Exception ex) { Warn($"[TextRasterizer] font load failed: {ex.Message}"); }
    }

    public float Measure(string text, float pixelHeight)
    {
        EnsureFont();
        if (!_haveFont || string.IsNullOrEmpty(text))
            return 0;

        float scale = StbTrueType.stbtt_ScaleForPixelHeight(_font, pixelHeight);
        float x = 0;
        for (int i = 0; i < text.Length; i++)
        {
            int adv, lsb;
            StbTrueType.stbtt_GetCodepointHMetrics(_font, text[i], &adv, &lsb);
            x += adv * scale;
            if (i + 1 < text.Length)
                x += StbTrueType.stbtt_GetCodepointKernAdvance(_font, text[i], text[i + 1]) * scale;
        }
        return x;
    }

    public ITexture Render(string text, float pixelHeight, byte r, byte g, byte b, byte a = 255)
    {
        EnsureFont();
        text ??= "";
        uint colourKey = (uint)(r | (g << 8) | (b << 16) | (a << 24));
        var key = (text, (int)MathF.Round(pixelHeight), colourKey);
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        int h = Math.Max(1, (int)MathF.Ceiling(pixelHeight * 1.4f));
        int w = Math.Max(1, (int)MathF.Ceiling(Measure(text, pixelHeight)) + 4);
        var pixels = new uint[w * h];

        if (_haveFont && text.Length > 0)
        {
            float scale = StbTrueType.stbtt_ScaleForPixelHeight(_font, pixelHeight);
            int ascent, descent, lineGap;
            StbTrueType.stbtt_GetFontVMetrics(_font, &ascent, &descent, &lineGap);
            int baseline = (int)MathF.Round(ascent * scale) + (int)(pixelHeight * 0.05f);

            float xpos = 1;
            for (int i = 0; i < text.Length; i++)
            {
                int gw, gh, xoff, yoff;
                byte* bmp = StbTrueType.stbtt_GetCodepointBitmap(_font, scale, scale, text[i], &gw, &gh, &xoff, &yoff);
                int gx = (int)xpos + xoff;
                int gy = baseline + yoff;
                if (bmp != null)
                {
                    for (int j = 0; j < gh; j++)
                    {
                        int py = gy + j;
                        if (py < 0 || py >= h) continue;
                        for (int k = 0; k < gw; k++)
                        {
                            int px = gx + k;
                            if (px < 0 || px >= w) continue;
                            byte cov = bmp[j * gw + k];
                            if (cov == 0) continue;
                            uint alpha = (uint)(cov * a / 255);
                            // R8G8B8A8 little-endian = A<<24 | B<<16 | G<<8 | R
                            pixels[py * w + px] = (alpha << 24) | ((uint)b << 16) | ((uint)g << 8) | r;
                        }
                    }
                    StbTrueType.stbtt_FreeBitmap(bmp, null);
                }

                int adv, lsb;
                StbTrueType.stbtt_GetCodepointHMetrics(_font, text[i], &adv, &lsb);
                xpos += adv * scale;
                if (i + 1 < text.Length)
                    xpos += StbTrueType.stbtt_GetCodepointKernAdvance(_font, text[i], text[i + 1]) * scale;
            }
        }

        var tex = new SimpleTexture<uint>(null, $"txt:{text}:{(int)pixelHeight}", w, h, pixels,
            [new Region(0, 0, w, h, w, h, 0)]);
        _cache[key] = tex;
        return tex;
    }

    protected override void Unsubscribed() => ReleaseFont();
    public void Dispose() => ReleaseFont();

    // Also reset the init flags: the font data references the pinned buffer, so once the pin is
    // freed the stbtt_fontinfo must not be used again — a resubscribe re-initialises from scratch.
    void ReleaseFont()
    {
        _font?.Dispose();
        _font = null;
        if (_pin.IsAllocated)
            _pin.Free();
        _haveFont = false;
        _init = false;
    }
}
