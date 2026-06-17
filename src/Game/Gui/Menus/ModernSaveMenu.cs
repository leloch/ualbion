using System;
using System.Collections.Generic;
using System.Numerics;
using UAlbion.Api;
using UAlbion.Api.Eventing;
using UAlbion.Api.Visual;
using UAlbion.Config;
using UAlbion.Core;
using UAlbion.Core.Events;
using UAlbion.Core.Visual;
using UAlbion.Formats;
using UAlbion.Formats.Assets.Save;
using UAlbion.Game.Events;
using UAlbion.Game.Gui.Controls;
using UAlbion.Game.Input;

namespace UAlbion.Game.Gui.Menus;

/// <summary>
/// Rich, native-resolution save/load browser: one row per slot showing a real screenshot thumbnail
/// (captured at save time, #74), the save name and timestamp. Hover highlights, click selects.
/// Used by the modern menu; the classic menu keeps the original PickSaveSlotMenu.
/// </summary>
public class ModernSaveMenu : Dialog
{
    const int MaxSlots = 10;

    sealed class Slot
    {
        public ushort Number;
        public bool Exists;
        public string Name;
        public string Date;
        public ITexture Thumb;
        public Rectangle Rect;
    }

    readonly bool _saveMode;
    readonly List<Slot> _slots = [];
    readonly List<BatchLease<SpriteKey, SpriteInfo>> _leases = [];
    readonly Dictionary<uint, ITexture> _solids = [];
    int _hover = -1;
    int _pressed = -1;
    bool _dirty = true;
    bool _cursorMoved;
    int _lastW, _lastH;
    Vector2 _lastCursor = new(float.NaN, float.NaN);

    static readonly (byte r, byte g, byte b, byte a) Panel = (18, 14, 10, 236);
    static readonly (byte r, byte g, byte b, byte a) Border = (150, 120, 60, 255);
    static readonly (byte r, byte g, byte b, byte a) Gold = (224, 188, 104, 255);
    static readonly (byte r, byte g, byte b, byte a) White = (228, 220, 198, 255);
    static readonly (byte r, byte g, byte b, byte a) Dim = (160, 150, 130, 255);
    static readonly (byte r, byte g, byte b, byte a) HoverText = (255, 232, 150, 255);
    static readonly (byte r, byte g, byte b, byte a) HoverBg = (120, 92, 40, 150);
    static readonly (byte r, byte g, byte b, byte a) RowBg = (40, 32, 22, 140);

    public event EventHandler<ushort?> Closed;

    public ModernSaveMenu(bool saveMode) : base(DialogPositioning.Center)
    {
        _saveMode = saveMode;
        On<BackendChangedEvent>(_ => _dirty = true);
        On<GameWindowResizedEvent>(_ => _dirty = true);
        On<UiLeftClickEvent>(OnPress);
        On<UiLeftReleaseEvent>(OnRelease);
        On<UiRightClickEvent>(e => { e.Propagating = false; Close(null); });
        On<CloseWindowEvent>(e => { e.Propagating = false; Close(null); });
    }

    protected override void Subscribed()
    {
        LoadSlots();
        _dirty = true;
        _cursorMoved = false;
        _lastCursor = new Vector2(float.NaN, float.NaN);
        _pressed = -1;
    }

    protected override void Unsubscribed() => ClearLeases();

    void ClearLeases() { foreach (var l in _leases) l.Dispose(); _leases.Clear(); }

    void LoadSlots()
    {
        _slots.Clear();
        var disk = Resolve<IFileSystem>();
        var pathResolver = Resolve<IPathResolver>();
        var imgLoader = TryResolve<IRgbaImageLoader>();

        for (ushort i = 1; i <= MaxSlots; i++)
        {
            var savePath = pathResolver.ResolvePath($"$(SAVES)/SAVE.{i:D3}");
            var slot = new Slot { Number = i };
            if (disk.FileExists(savePath))
            {
                slot.Exists = true;
                try
                {
                    using var s = AlbionSerdes.CreateReader(disk.OpenRead(savePath));
                    slot.Name = SavedGame.GetName(s) ?? "Saved game";
                }
                catch { slot.Name = "Saved game"; }

                try { if (System.IO.File.Exists(savePath)) slot.Date = System.IO.File.GetLastWriteTime(savePath).ToString("yyyy-MM-dd HH:mm"); }
                catch { /* ignore */ }

                var thumbPath = savePath + ".png";
                if (imgLoader != null && disk.FileExists(thumbPath))
                {
                    try
                    {
                        using var ts = disk.OpenRead(thumbPath);
                        using var ms = new System.IO.MemoryStream();
                        ts.CopyTo(ms);
                        slot.Thumb = imgLoader.LoadPng(ms.ToArray());
                    }
                    catch { /* no thumb */ }
                }
            }
            if (slot.Exists || _saveMode)
                _slots.Add(slot);
        }
    }

    void Close(ushort? slot) { Closed?.Invoke(this, slot); Remove(); }

    ITexture Solid((byte r, byte g, byte b, byte a) c)
    {
        uint key = (uint)(c.r | (c.g << 8) | (c.b << 16) | (c.a << 24));
        if (_solids.TryGetValue(key, out var t)) return t;
        t = new SimpleTexture<uint>(null, $"solid:{key:X8}", 1, 1, [key], [new Region(0, 0, 1, 1, 1, 1, 0)]);
        _solids[key] = t; return t;
    }

    public override Vector2 GetSize()
    {
        var w = Resolve<IGameWindow>();
        return new Vector2(w.UiWidth, w.UiHeight);
    }

    int RowAtCursor()
    {
        var cur = TryResolve<ICursorManager>()?.Position ?? Vector2.Zero;
        for (int i = 0; i < _slots.Count; i++)
            if (_slots[i].Rect.Contains((int)cur.X, (int)cur.Y))
                return i;
        return -1;
    }

    public override int Selection(Rectangle extents, int order, SelectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (RowAtCursor() >= 0 || true) context.AddHit(order, this); // modal: capture all clicks
        return order;
    }

    void OnPress(UiLeftClickEvent e) { _pressed = RowAtCursor(); e.Propagating = false; }

    void OnRelease(UiLeftReleaseEvent e)
    {
        int row = RowAtCursor();
        e.Propagating = false;
        if (row >= 0 && row == _pressed && _cursorMoved)
        {
            var slot = _slots[row];
            if (slot.Exists || _saveMode)
                Close(slot.Number);
        }
        _pressed = -1;
    }

    public override int Render(Rectangle extents, int order, LayoutNode parent)
    {
        if (!IsSubscribed) return order;
        _ = parent == null ? null : new LayoutNode(parent, this, extents, order);

        var window = Resolve<IGameWindow>();
        int W = window.PixelWidth, H = window.PixelHeight;
        if (W <= 0 || H <= 0) return order;

        var cur = TryResolve<ICursorManager>()?.Position ?? Vector2.Zero;
        if (!float.IsNaN(_lastCursor.X) && cur != _lastCursor) _cursorMoved = true;
        _lastCursor = cur;

        int hv = RowAtCursor();
        if (W != _lastW || H != _lastH || hv != _hover) { _lastW = W; _lastH = H; _hover = hv; _dirty = true; }

        if (_dirty) Rebuild(window, (DrawLayer)order, W, H);
        return order + 3;
    }

    void Rebuild(IGameWindow window, DrawLayer layer, int W, int H)
    {
        _dirty = false;
        ClearLeases();
        var text = TryResolve<ITextRasterizer>();

        float s = H / 720f;
        float titlePx = MathF.Round(40 * s);
        float namePx = MathF.Round(24 * s);
        float datePx = MathF.Round(16 * s);
        int thumbH = (int)MathF.Round(64 * s);
        int thumbW = (int)MathF.Round(thumbH * 1.6f);
        int rowH = thumbH + (int)(14 * s);
        int pad = (int)MathF.Round(22 * s);

        int titleArea = (int)(titlePx * 1.6f);
        int panelW = (int)MathF.Min(W * 0.7f, MathF.Max(560 * s, thumbW + 360 * s));
        int panelH = pad * 2 + titleArea + _slots.Count * rowH;
        panelH = (int)MathF.Min(panelH, H * 0.92f);
        int panelX = (W - panelW) / 2;
        int panelY = (H - panelH) / 2;

        DrawQuad(Solid(Panel), panelX, panelY, panelW, panelH, layer, W, H);
        int bw = Math.Max(2, (int)(2 * s));
        DrawQuad(Solid(Border), panelX, panelY, panelW, bw, layer, W, H);
        DrawQuad(Solid(Border), panelX, panelY + panelH - bw, panelW, bw, layer, W, H);
        DrawQuad(Solid(Border), panelX, panelY, bw, panelH, layer, W, H);
        DrawQuad(Solid(Border), panelX + panelW - bw, panelY, bw, panelH, layer, W, H);

        if (text != null)
        {
            var title = text.Render(_saveMode ? "Save Game" : "Load Game", titlePx, Gold.r, Gold.g, Gold.b);
            DrawTex(title, panelX + (panelW - title.Regions[0].Width) / 2, panelY + pad,
                title.Regions[0].Width, title.Regions[0].Height, (DrawLayer)((int)layer + 2), W, H);
            DrawQuad(Solid(Border), panelX + pad, panelY + pad + titleArea - (int)(8 * s), panelW - pad * 2, Math.Max(1, (int)(2 * s)), layer, W, H);
        }

        int rowY = panelY + pad + titleArea;
        int innerX = panelX + pad;
        int innerW = panelW - pad * 2;
        for (int i = 0; i < _slots.Count; i++)
        {
            var slot = _slots[i];
            slot.Rect = new Rectangle(innerX, rowY, innerW, rowH);
            bool hov = i == _hover;

            DrawQuad(Solid(hov ? HoverBg : RowBg), innerX, rowY + (int)(2 * s), innerW, rowH - (int)(4 * s), (DrawLayer)((int)layer + 1), W, H);

            // Thumbnail (or placeholder).
            int tx = innerX + (int)(8 * s);
            int ty = rowY + (rowH - thumbH) / 2;
            if (slot.Thumb != null)
                DrawTex(slot.Thumb, tx, ty, thumbW, thumbH, (DrawLayer)((int)layer + 2), W, H);
            else
                DrawQuad(Solid((30, 26, 20, 200)), tx, ty, thumbW, thumbH, (DrawLayer)((int)layer + 2), W, H);
            // thumbnail frame
            DrawQuad(Solid(Border), tx, ty, thumbW, Math.Max(1, (int)s), (DrawLayer)((int)layer + 2), W, H);
            DrawQuad(Solid(Border), tx, ty + thumbH - Math.Max(1, (int)s), thumbW, Math.Max(1, (int)s), (DrawLayer)((int)layer + 2), W, H);
            DrawQuad(Solid(Border), tx, ty, Math.Max(1, (int)s), thumbH, (DrawLayer)((int)layer + 2), W, H);
            DrawQuad(Solid(Border), tx + thumbW - Math.Max(1, (int)s), ty, Math.Max(1, (int)s), thumbH, (DrawLayer)((int)layer + 2), W, H);

            if (text != null)
            {
                int textX = tx + thumbW + (int)(16 * s);
                var nameCol = hov ? HoverText : (slot.Exists ? White : Dim);
                string nameStr = slot.Exists ? $"{slot.Number}.  {slot.Name}" : $"{slot.Number}.  -- empty --";
                var nameTex = text.Render(nameStr, namePx, nameCol.r, nameCol.g, nameCol.b);
                DrawTex(nameTex, textX, rowY + (int)(10 * s), nameTex.Regions[0].Width, nameTex.Regions[0].Height, (DrawLayer)((int)layer + 2), W, H);
                if (!string.IsNullOrEmpty(slot.Date))
                {
                    var dateTex = text.Render(slot.Date, datePx, Dim.r, Dim.g, Dim.b);
                    DrawTex(dateTex, textX, rowY + (int)(10 * s) + nameTex.Regions[0].Height + (int)(4 * s),
                        dateTex.Regions[0].Width, dateTex.Regions[0].Height, (DrawLayer)((int)layer + 2), W, H);
                }
            }
            rowY += rowH;
        }
    }

    void DrawQuad(ITexture tex, int px, int py, int pw, int ph, DrawLayer layer, int W, int H)
        => DrawTex(tex, px, py, pw, ph, layer, W, H);

    void DrawTex(ITexture tex, float px, float py, float pw, float ph, DrawLayer layer, int W, int H)
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
