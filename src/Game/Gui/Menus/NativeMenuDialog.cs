using System;
using System.Collections.Generic;
using System.Numerics;
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
        public Kind Kind;
        public string Label;
        public Action Action;                 // Button
        public Func<bool> GetBool;            // Toggle
        public Action<bool> SetBool;
        public Func<int> GetInt;              // Slider
        public Action<int> SetInt;
        public int Min, Max;
        public Func<int, string> Fmt;
        public bool Primary;
        public Rectangle Rect;                // pixel rect (set during layout)
        public Rectangle TrackRect;           // slider track (pixel)
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

    protected readonly List<Widget> Widgets = [];
    readonly List<BatchLease<SpriteKey, SpriteInfo>> _leases = [];
    readonly Dictionary<uint, ITexture> _solids = [];
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

        float titlePx = MathF.Round(40 * s);
        float rowPx = MathF.Round(26 * s);
        int rowH = (int)MathF.Round(rowPx * 1.7f);
        int headerH = (int)MathF.Round(rowPx * 1.5f);
        int spacerH = (int)MathF.Round(rowPx * 0.6f);
        int pad = (int)MathF.Round(26 * s);
        int titleArea = (int)(titlePx * 1.6f);

        // Panel size.
        float maxW = text?.Measure(Title, titlePx) ?? 200;
        foreach (var w in Widgets)
            if (w.Label != null) maxW = MathF.Max(maxW, (text?.Measure(w.Label, rowPx) ?? 0) + 220 * s);
        int contentH = 0;
        foreach (var w in Widgets)
            contentH += w.Kind switch { Kind.Header => headerH, Kind.Spacer => spacerH, _ => rowH };

        int panelW = (int)MathF.Min(W * 0.7f, MathF.Max(520 * s, maxW + pad * 2));
        int panelH = pad * 2 + titleArea + contentH;
        panelH = (int)MathF.Min(panelH, H * 0.94f);
        int panelX = (W - panelW) / 2;
        int panelY = (H - panelH) / 2;

        DrawQuad(Solid(Panel), panelX, panelY, panelW, panelH, layer, W, H);
        int bw = Math.Max(2, (int)(2 * s));
        DrawQuad(Solid(Border), panelX, panelY, panelW, bw, layer, W, H);
        DrawQuad(Solid(Border), panelX, panelY + panelH - bw, panelW, bw, layer, W, H);
        DrawQuad(Solid(Border), panelX, panelY, bw, panelH, layer, W, H);
        DrawQuad(Solid(Border), panelX + panelW - bw, panelY, bw, panelH, layer, W, H);

        var txtLayer = (DrawLayer)((int)layer + 2);
        var hiLayer = (DrawLayer)((int)layer + 1);

        if (text != null)
        {
            var t = text.Render(Title, titlePx, Gold.r, Gold.g, Gold.b);
            DrawTex(t, panelX + (panelW - t.Regions[0].Width) / 2, panelY + pad, t.Regions[0].Width, t.Regions[0].Height, txtLayer, W, H);
            DrawQuad(Solid(Border), panelX + pad, panelY + pad + titleArea - (int)(8 * s), panelW - pad * 2, Math.Max(1, (int)(2 * s)), layer, W, H);
        }

        int y = panelY + pad + titleArea;
        int innerX = panelX + pad, innerW = panelW - pad * 2;
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
                    if (hov) DrawQuad(Solid(HoverBg), innerX, y + (int)(2 * s), innerW, rowH - (int)(4 * s), hiLayer, W, H);
                    if (text != null)
                    {
                        var col = hov ? HoverText : (w.Primary ? Gold : White);
                        var bt = text.Render(w.Label, rowPx, col.r, col.g, col.b);
                        DrawTex(bt, innerX + (innerW - bt.Regions[0].Width) / 2, y + (rowH - bt.Regions[0].Height) / 2, bt.Regions[0].Width, bt.Regions[0].Height, txtLayer, W, H);
                    }
                    y += rowH;
                    break;

                case Kind.Toggle:
                    w.Rect = new Rectangle(innerX, y, innerW, rowH);
                    if (hov) DrawQuad(Solid(HoverBg), innerX, y + (int)(2 * s), innerW, rowH - (int)(4 * s), hiLayer, W, H);
                    if (text != null)
                    {
                        var col = hov ? HoverText : White;
                        var lt = text.Render(w.Label, rowPx, col.r, col.g, col.b);
                        DrawTex(lt, innerX + (int)(10 * s), y + (rowH - lt.Regions[0].Height) / 2, lt.Regions[0].Width, lt.Regions[0].Height, txtLayer, W, H);
                        bool on = w.GetBool?.Invoke() ?? false;
                        var pill = on ? Gold : Dim;
                        string st = on ? "ON" : "OFF";
                        var stt = text.Render(st, rowPx, pill.r, pill.g, pill.b);
                        DrawTex(stt, innerX + innerW - stt.Regions[0].Width - (int)(14 * s), y + (rowH - stt.Regions[0].Height) / 2, stt.Regions[0].Width, stt.Regions[0].Height, txtLayer, W, H);
                    }
                    y += rowH;
                    break;

                case Kind.Slider:
                    w.Rect = new Rectangle(innerX, y, innerW, rowH);
                    if (text != null)
                    {
                        var lt = text.Render(w.Label, rowPx, White.r, White.g, White.b);
                        DrawTex(lt, innerX + (int)(10 * s), y + (rowH - lt.Regions[0].Height) / 2, lt.Regions[0].Width, lt.Regions[0].Height, txtLayer, W, H);
                    }
                    int trackW = (int)(innerW * 0.42f);
                    int trackX = innerX + innerW - trackW - (int)(64 * s);
                    int trackH = Math.Max(4, (int)(8 * s));
                    int trackY = y + (rowH - trackH) / 2;
                    w.TrackRect = new Rectangle(trackX, y, trackW, rowH);
                    DrawQuad(Solid(TrackBg), trackX, trackY, trackW, trackH, hiLayer, W, H);
                    int val = w.GetInt?.Invoke() ?? 0;
                    float frac = w.Max > w.Min ? (val - w.Min) / (float)(w.Max - w.Min) : 0;
                    DrawQuad(Solid(TrackFill), trackX, trackY, (int)(trackW * frac), trackH, txtLayer, W, H);
                    int knob = (int)(14 * s);
                    DrawQuad(Solid(Gold), trackX + (int)(trackW * frac) - knob / 2, trackY + trackH / 2 - knob / 2, knob, knob, txtLayer, W, H);
                    if (text != null)
                    {
                        string vs = w.Fmt != null ? w.Fmt(val) : val.ToString();
                        var vt = text.Render(vs, MathF.Round(rowPx * 0.85f), Gold.r, Gold.g, Gold.b);
                        DrawTex(vt, innerX + innerW - vt.Regions[0].Width - (int)(6 * s), y + (rowH - vt.Regions[0].Height) / 2, vt.Regions[0].Width, vt.Regions[0].Height, txtLayer, W, H);
                    }
                    y += rowH;
                    break;
            }
        }
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
        var sm = Resolve<IBatchManager<SpriteKey, SpriteInfo>>();
        var key = new SpriteKey(tex, SpriteSampler.TriLinear, layer, SpriteKeyFlags.NoDepthTest | SpriteKeyFlags.NoTransform);
        var lease = sm.Borrow(key, 1, this);
        _leases.Add(lease);
        var position = new Vector3(px / W * 2f - 1f, 1f - py / H * 2f, 0);
        var size = new Vector2(pw / W * 2f, -(ph / H * 2f));
        bool lockTaken = false;
        var inst = lease.Lock(ref lockTaken);
        try { inst[0] = new SpriteInfo(SpriteFlags.TopLeft, position, size, tex.Regions[0]); }
        finally { lease.Unlock(lockTaken); }
    }
}
