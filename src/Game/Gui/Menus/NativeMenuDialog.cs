using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Reflection;
using UAlbion.Api.Eventing;
using UAlbion.Api.Visual;
using UAlbion.Core;
using UAlbion.Core.Events;
using UAlbion.Core.Visual;
using UAlbion.Game.Events;
using UAlbion.Game.Gui.Controls;
using UAlbion.Game.Input;

namespace UAlbion.Game.Gui.Menus;

/// <summary>
/// Base class for the modern, native-resolution menus. Provides the shared look (dark stone panel +
/// gold accents, crisp TTF text via ITextRasterizer) and a small widget framework (header / button /
/// toggle / slider) drawn and hit-tested in native pixels over the live backdrop. Subclasses just
/// fill in Title and BuildWidgets(); interaction (hover, click, toggle, slider drag) is handled here.
/// </summary>
public abstract class NativeMenuDialog : Dialog
{
    protected enum Kind { Header, Button, Toggle, Slider, Spacer }

    protected sealed class Widget
    {
        public Kind Kind { get; set; }
        public string Label { get; set; }
        public Action Action { get; set; }                 // Button
        public Func<bool> GetBool { get; set; }            // Toggle
        public Action<bool> SetBool { get; set; }
        public Func<int> GetInt { get; set; }              // Slider
        public Action<int> SetInt { get; set; }
        public int Min { get; set; }
        public int Max { get; set; }
        public Func<int, string> Fmt { get; set; }
        public bool Primary { get; set; }
        public Rectangle Rect { get; set; }                // pixel rect (set during layout)
        public Rectangle TrackRect { get; set; }           // slider track (pixel)
    }

    protected static readonly (byte r, byte g, byte b, byte a) Panel = (18, 14, 10, 236);
    protected static readonly (byte r, byte g, byte b, byte a) Border = (150, 120, 60, 255);
    protected static readonly (byte r, byte g, byte b, byte a) Gold = (224, 188, 104, 255);
    protected static readonly (byte r, byte g, byte b, byte a) White = (228, 220, 198, 255);
    protected static readonly (byte r, byte g, byte b, byte a) Dim = (165, 152, 130, 255);
    protected static readonly (byte r, byte g, byte b, byte a) HoverText = (255, 232, 150, 255);
    protected static readonly (byte r, byte g, byte b, byte a) HoverBg = (120, 92, 40, 150);
    protected static readonly (byte r, byte g, byte b, byte a) TrackBg = (8, 6, 4, 220);
    protected static readonly (byte r, byte g, byte b, byte a) TrackFill = (150, 120, 60, 230);

    protected List<Widget> Widgets { get; } = [];
    readonly List<BatchLease<SpriteKey, SpriteInfo>> _leases = [];
    readonly Dictionary<uint, ITexture> _solids = [];
    static readonly Dictionary<string, ITexture> AssetCache = [];
    int _hover = -1;
    int _pressed = -1;
    bool _draggingSlider;
    bool _dirty = true;
    bool _cursorMoved;
    int _lastW, _lastH;
    Vector2 _lastCursor = new(float.NaN, float.NaN);

    protected abstract string Title { get; }
    protected abstract void BuildWidgets();
    protected virtual void OnCancel() => Close();
    protected float Scale { get; private set; } = 1f;

    /// <summary>Raised when the menu closes (OK or cancel), for the opener to re-attach itself.</summary>
    public event EventHandler Closed;
    protected void Close() { Closed?.Invoke(this, EventArgs.Empty); Remove(); }

    protected NativeMenuDialog() : base(DialogPositioning.Center)
    {
        On<BackendChangedEvent>(_ => _dirty = true);
        On<GameWindowResizedEvent>(_ => _dirty = true);
        On<UiLeftClickEvent>(OnPress);
        On<UiLeftReleaseEvent>(OnRelease);
        On<UiRightClickEvent>(e => { e.Propagating = false; OnCancel(); });
        On<CloseWindowEvent>(e => { e.Propagating = false; OnCancel(); });
    }

    protected override void Subscribed()
    {
        Widgets.Clear();
        BuildWidgets();
        _dirty = true;
        _cursorMoved = false;
        _pressed = -1;
        _draggingSlider = false;
        _lastCursor = new Vector2(float.NaN, float.NaN);
    }

    protected override void Unsubscribed() => ClearLeases();
    void ClearLeases() { foreach (var l in _leases) l.Dispose(); _leases.Clear(); }
    protected void MarkDirty() => _dirty = true;

    protected void AddHeader(string label) => Widgets.Add(new Widget { Kind = Kind.Header, Label = label });
    protected void AddSpacer() => Widgets.Add(new Widget { Kind = Kind.Spacer });
    protected void AddButton(string label, Action action, bool primary = false)
        => Widgets.Add(new Widget { Kind = Kind.Button, Label = label, Action = action, Primary = primary });
    protected void AddToggle(string label, Func<bool> get, Action<bool> set)
        => Widgets.Add(new Widget { Kind = Kind.Toggle, Label = label, GetBool = get, SetBool = set });
    protected void AddSlider(string label, Func<int> get, Action<int> set, int min, int max, Func<int, string> fmt = null)
        => Widgets.Add(new Widget { Kind = Kind.Slider, Label = label, GetInt = get, SetInt = set, Min = min, Max = max, Fmt = fmt });

    Vector2 Cursor => TryResolve<ICursorManager>()?.Position ?? Vector2.Zero;

    int WidgetAtCursor()
    {
        var c = Cursor;
        for (int i = 0; i < Widgets.Count; i++)
        {
            var w = Widgets[i];
            if ((w.Kind is Kind.Button or Kind.Toggle or Kind.Slider) && w.Rect.Contains((int)c.X, (int)c.Y))
                return i;
        }
        return -1;
    }

    public override Vector2 GetSize()
    {
        var w = Resolve<IGameWindow>();
        return new Vector2(w.UiWidth, w.UiHeight);
    }

    public override int Selection(Rectangle extents, int order, SelectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.AddHit(order, this); // modal: capture interaction over the whole screen
        return order;
    }

    void OnPress(UiLeftClickEvent e)
    {
        _pressed = WidgetAtCursor();
        if (_pressed >= 0)
        {
            e.Propagating = false;
            if (Widgets[_pressed].Kind == Kind.Slider) { _draggingSlider = true; ApplySlider(Widgets[_pressed]); }
        }
    }

    void OnRelease(UiLeftReleaseEvent e)
    {
        _draggingSlider = false;
        int w = WidgetAtCursor();
        if (w >= 0 && w == _pressed && _cursorMoved)
        {
            e.Propagating = false;
            var widget = Widgets[w];
            switch (widget.Kind)
            {
                case Kind.Button: try { widget.Action?.Invoke(); } catch (Exception ex) { Error($"[menu] action threw: {ex}"); } break;
                case Kind.Toggle: widget.SetBool?.Invoke(!(widget.GetBool?.Invoke() ?? false)); _dirty = true; break;
                case Kind.Slider: ApplySlider(widget); break;
            }
        }
        _pressed = -1;
    }

    void ApplySlider(Widget w)
    {
        if (w.TrackRect.Width <= 0 || w.SetInt == null) return;
        float t = Math.Clamp((Cursor.X - w.TrackRect.X) / (float)w.TrackRect.Width, 0f, 1f);
        int val = w.Min + (int)MathF.Round(t * (w.Max - w.Min));
        w.SetInt(val);
        _dirty = true;
    }

    public override int Render(Rectangle extents, int order, LayoutNode parent)
    {
        if (!IsSubscribed) return order;
        _ = parent == null ? null : new LayoutNode(parent, this, extents, order);
        var window = Resolve<IGameWindow>();
        int W = window.PixelWidth, H = window.PixelHeight;
        if (W <= 0 || H <= 0) return order;

        var c = Cursor;
        if (!float.IsNaN(_lastCursor.X) && c != _lastCursor) _cursorMoved = true;
        _lastCursor = c;

        if (_draggingSlider && _pressed >= 0 && Widgets[_pressed].Kind == Kind.Slider)
            ApplySlider(Widgets[_pressed]);

        int hv = WidgetAtCursor();
        if (W != _lastW || H != _lastH || hv != _hover) { _lastW = W; _lastH = H; _hover = hv; _dirty = true; }
        if (_dirty) Rebuild((DrawLayer)order, W, H);
        return order + 3;
    }

    void Rebuild(DrawLayer layer, int W, int H)
    {
        _dirty = false;
        ClearLeases();
        var text = TryResolve<ITextRasterizer>();
        float s = Scale = H / 720f;

        var panelTex = Asset("MenuPanel.png");
        var btnTex = Asset("MenuButton.png");
        var btnHovTex = Asset("MenuButtonHover.png");
        var emblemTex = Asset("MenuEmblem.png");
        var dividerTex = Asset("MenuDivider.png");

        float titlePx = MathF.Round(42 * s);
        float rowPx = MathF.Round(25 * s);
        int rowH = (int)MathF.Round(rowPx * 1.95f);
        int rowGap = (int)MathF.Round(7 * s);
        int headerH = (int)MathF.Round(rowPx * 1.5f);
        int spacerH = (int)MathF.Round(rowPx * 0.5f);

        int panelBorder = (int)MathF.Round(54 * s); // dst thickness of the ornate 9-slice frame
        int contentPad = (int)MathF.Round(14 * s);
        int inset = panelBorder + contentPad;        // from panel edge to content
        int gap = (int)MathF.Round(10 * s);
        int emblemH = (int)MathF.Round(102 * s);
        int titleH = (int)MathF.Round(titlePx * 1.18f);
        int dividerH = (int)MathF.Round(30 * s);

        // Content width: widest of the title and the (padded) widget labels.
        float maxW = text?.Measure(Title, titlePx) ?? 200;
        foreach (var w in Widgets)
            if (w.Label != null) maxW = MathF.Max(maxW, (text?.Measure(w.Label, rowPx) ?? 0) + 240 * s);

        int contentH = emblemH + gap + titleH + gap + dividerH + gap;
        foreach (var w in Widgets)
            contentH += w.Kind switch { Kind.Header => headerH, Kind.Spacer => spacerH, _ => rowH + rowGap };

        int panelW = (int)MathF.Min(W * 0.74f, MathF.Max(600 * s, maxW + inset * 2));
        int panelH = (int)MathF.Min(inset * 2 + contentH, (int)(H * 0.96f));
        int panelX = (W - panelW) / 2;
        int panelY = (H - panelH) / 2;

        var midLayer = (DrawLayer)((int)layer + 1);
        var txtLayer = (DrawLayer)((int)layer + 2);

        // Ornate panel frame (9-slice), with a graceful solid fallback if the asset is missing.
        if (panelTex != null)
            DrawNineSlice(panelTex, panelX, panelY, panelW, panelH, 250, 250, panelBorder, panelBorder, layer, W, H);
        else
            DrawQuad(Solid(Panel), panelX, panelY, panelW, panelH, layer, W, H);

        int cx = panelX + panelW / 2;
        int innerX = panelX + inset, innerW = panelW - inset * 2;
        int y = panelY + inset;

        // Emblem crest.
        if (emblemTex != null)
        {
            int eh = emblemH, ew = (int)(eh * emblemTex.Width / (float)emblemTex.Height);
            DrawTex(emblemTex, cx - ew / 2, y, ew, eh, txtLayer, W, H);
        }
        y += emblemH + gap;

        // Title.
        if (text != null)
        {
            var t = text.Render(Title, titlePx, Gold.r, Gold.g, Gold.b);
            DrawTex(t, cx - t.Regions[0].Width / 2, y + (titleH - t.Regions[0].Height) / 2,
                t.Regions[0].Width, t.Regions[0].Height, txtLayer, W, H);
        }
        y += titleH + gap;

        // Divider flourish.
        if (dividerTex != null)
        {
            int dw = innerW, dh = dividerH;
            DrawTex(dividerTex, cx - dw / 2, y, dw, dh, txtLayer, W, H);
        }
        y += dividerH + gap;

        int btnSrcCap = 200, btnDstCap = (int)MathF.Min(rowH, innerW / 2);
        for (int i = 0; i < Widgets.Count; i++)
        {
            var w = Widgets[i];
            bool hov = i == _hover;
            switch (w.Kind)
            {
                case Kind.Spacer: y += spacerH; break;

                case Kind.Header:
                    if (text != null)
                    {
                        var ht = text.Render(w.Label, MathF.Round(rowPx * 0.95f), Dim.r, Dim.g, Dim.b);
                        DrawTex(ht, innerX, y + (headerH - ht.Regions[0].Height) / 2, ht.Regions[0].Width, ht.Regions[0].Height, txtLayer, W, H);
                    }
                    y += headerH;
                    break;

                case Kind.Button:
                    w.Rect = new Rectangle(innerX, y, innerW, rowH);
                    DrawPlate(hov ? btnHovTex : btnTex, innerX, y, innerW, rowH, btnSrcCap, btnDstCap, hov, midLayer, W, H);
                    if (text != null)
                    {
                        var col = hov ? HoverText : (w.Primary ? Gold : White);
                        var bt = text.Render(w.Label, rowPx, col.r, col.g, col.b);
                        DrawTex(bt, innerX + (innerW - bt.Regions[0].Width) / 2, y + (rowH - bt.Regions[0].Height) / 2, bt.Regions[0].Width, bt.Regions[0].Height, txtLayer, W, H);
                    }
                    y += rowH + rowGap;
                    break;

                case Kind.Toggle:
                    w.Rect = new Rectangle(innerX, y, innerW, rowH);
                    DrawPlate(hov ? btnHovTex : btnTex, innerX, y, innerW, rowH, btnSrcCap, btnDstCap, hov, midLayer, W, H);
                    if (text != null)
                    {
                        var col = hov ? HoverText : White;
                        var lt = text.Render(w.Label, rowPx, col.r, col.g, col.b);
                        DrawTex(lt, innerX + (int)(28 * s), y + (rowH - lt.Regions[0].Height) / 2, lt.Regions[0].Width, lt.Regions[0].Height, txtLayer, W, H);
                        bool on = w.GetBool?.Invoke() ?? false;
                        var pill = on ? Gold : Dim;
                        string st = on ? "ON" : "OFF";
                        var stt = text.Render(st, rowPx, pill.r, pill.g, pill.b);
                        DrawTex(stt, innerX + innerW - stt.Regions[0].Width - (int)(28 * s), y + (rowH - stt.Regions[0].Height) / 2, stt.Regions[0].Width, stt.Regions[0].Height, txtLayer, W, H);
                    }
                    y += rowH + rowGap;
                    break;

                case Kind.Slider:
                    w.Rect = new Rectangle(innerX, y, innerW, rowH);
                    DrawPlate(btnTex, innerX, y, innerW, rowH, btnSrcCap, btnDstCap, false, midLayer, W, H);
                    if (text != null)
                    {
                        var lt = text.Render(w.Label, rowPx, White.r, White.g, White.b);
                        DrawTex(lt, innerX + (int)(28 * s), y + (rowH - lt.Regions[0].Height) / 2, lt.Regions[0].Width, lt.Regions[0].Height, txtLayer, W, H);
                    }
                    int trackW = (int)(innerW * 0.40f);
                    int trackX = innerX + innerW - trackW - (int)(78 * s);
                    int trackH = Math.Max(4, (int)(8 * s));
                    int trackY = y + (rowH - trackH) / 2;
                    w.TrackRect = new Rectangle(trackX, y, trackW, rowH);
                    DrawQuad(Solid(TrackBg), trackX, trackY, trackW, trackH, txtLayer, W, H);
                    int val = w.GetInt?.Invoke() ?? 0;
                    float frac = w.Max > w.Min ? (val - w.Min) / (float)(w.Max - w.Min) : 0;
                    DrawQuad(Solid(TrackFill), trackX, trackY, (int)(trackW * frac), trackH, txtLayer, W, H);
                    int knob = (int)(14 * s);
                    DrawQuad(Solid(Gold), trackX + (int)(trackW * frac) - knob / 2, trackY + trackH / 2 - knob / 2, knob, knob, txtLayer, W, H);
                    if (text != null)
                    {
                        string vs = w.Fmt != null ? w.Fmt(val) : val.ToString();
                        var vt = text.Render(vs, MathF.Round(rowPx * 0.85f), Gold.r, Gold.g, Gold.b);
                        DrawTex(vt, innerX + innerW - vt.Regions[0].Width - (int)(20 * s), y + (rowH - vt.Regions[0].Height) / 2, vt.Regions[0].Width, vt.Regions[0].Height, txtLayer, W, H);
                    }
                    y += rowH + rowGap;
                    break;
            }
        }
    }

    /// <summary>Draw a row's button plate (3-slice horizontal), or a solid hover wash if the asset
    /// is unavailable.</summary>
    void DrawPlate(ITexture plate, int x, int y, int w, int h, int srcCap, int dstCap, bool hov, DrawLayer layer, int W, int H)
    {
        if (plate != null)
            DrawNineSlice(plate, x, y, w, h, srcCap, 0, dstCap, 0, layer, W, H);
        else if (hov)
            DrawQuad(Solid(HoverBg), x, y + (int)(2 * Scale), w, h - (int)(4 * Scale), layer, W, H);
    }

    protected ITexture Solid((byte r, byte g, byte b, byte a) c)
    {
        uint key = (uint)(c.r | (c.g << 8) | (c.b << 16) | (c.a << 24));
        if (_solids.TryGetValue(key, out var t)) return t;
        t = new SimpleTexture<uint>(null, $"solid:{key:X8}", 1, 1, [key], [new Region(0, 0, 1, 1, 1, 1, 0)]);
        _solids[key] = t; return t;
    }

    protected void DrawQuad(ITexture tex, int px, int py, int pw, int ph, DrawLayer layer, int W, int H)
        => DrawTex(tex, px, py, pw, ph, layer, W, H);

    protected void DrawTex(ITexture tex, float px, float py, float pw, float ph, DrawLayer layer, int W, int H)
    {
        if (tex == null) return;
        DrawTexRegion(tex, tex.Regions[0], px, py, pw, ph, layer, W, H);
    }

    void DrawTexRegion(ITexture tex, Region src, float px, float py, float pw, float ph, DrawLayer layer, int W, int H)
    {
        if (tex == null) return;
        var sm = Resolve<IBatchManager<SpriteKey, SpriteInfo>>();
        var key = new SpriteKey(tex, SpriteSampler.TriLinear, layer, SpriteKeyFlags.NoDepthTest | SpriteKeyFlags.NoTransform);
        var lease = sm.Borrow(key, 1, this);
        _leases.Add(lease);
        var position = new Vector3(px / W * 2f - 1f, 1f - py / H * 2f, 0);
        var size = new Vector2(pw / W * 2f, -(ph / H * 2f));
        bool lockTaken = false;
        var inst = lease.Lock(ref lockTaken);
        try { inst[0] = new SpriteInfo(SpriteFlags.TopLeft, position, size, src); }
        finally { lease.Unlock(lockTaken); }
    }

    /// <summary>Draw <paramref name="tex"/> as a 9-slice into the dst rect: corners keep their size,
    /// edges stretch along one axis, the centre stretches both. srcCorner/dstCorner of 0 on an axis
    /// degenerates to a 3-slice along the other axis (used for the horizontal button plates).</summary>
    protected void DrawNineSlice(ITexture tex, int px, int py, int pw, int ph,
        int srcCornerX, int srcCornerY, int dstCornerX, int dstCornerY, DrawLayer layer, int W, int H)
    {
        if (tex == null) return;
        int tw = tex.Width, th = tex.Height;
        srcCornerX = Math.Min(srcCornerX, tw / 2); srcCornerY = Math.Min(srcCornerY, th / 2);
        dstCornerX = Math.Min(dstCornerX, pw / 2); dstCornerY = Math.Min(dstCornerY, ph / 2);
        int[] sx = [0, srcCornerX, tw - srcCornerX, tw];
        int[] sy = [0, srcCornerY, th - srcCornerY, th];
        int[] dx = [px, px + dstCornerX, px + pw - dstCornerX, px + pw];
        int[] dy = [py, py + dstCornerY, py + ph - dstCornerY, py + ph];
        for (int r = 0; r < 3; r++)
        {
            int sh = sy[r + 1] - sy[r], dh = dy[r + 1] - dy[r];
            if (sh <= 0 || dh <= 0) continue;
            for (int c = 0; c < 3; c++)
            {
                int sw = sx[c + 1] - sx[c], dw = dx[c + 1] - dx[c];
                if (sw <= 0 || dw <= 0) continue;
                DrawTexRegion(tex, new Region(sx[c], sy[r], sw, sh, tw, th, 0), dx[c], dy[r], dw, dh, layer, W, H);
            }
        }
    }

    /// <summary>Lazily load + cache an embedded menu asset (Resources\&lt;fileName&gt;) as a true-colour texture.</summary>
    protected ITexture Asset(string fileName)
    {
        if (AssetCache.TryGetValue(fileName, out var cached)) return cached;
        var loader = TryResolve<IRgbaImageLoader>();
        if (loader == null) return null; // not ready yet - retry next frame, don't cache the miss
        var data = ReadResource("UAlbion.Game.Resources." + fileName);
        var tex = data == null ? null : loader.LoadPng(data);
        AssetCache[fileName] = tex;
        return tex;
    }

    static byte[] ReadResource(string resource)
    {
        using var stream = typeof(NativeMenuDialog).Assembly.GetManifestResourceStream(resource);
        if (stream == null) return null;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}
