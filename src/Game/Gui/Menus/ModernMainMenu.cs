using System;
using System.Collections.Generic;
using System.Numerics;
using UAlbion.Api;
using UAlbion.Api.Eventing;
using UAlbion.Api.Visual;
using UAlbion.Core;
using UAlbion.Core.Events;
using UAlbion.Core.Visual;
using UAlbion.Formats.MapEvents;
using UAlbion.Game.Events;
using UAlbion.Game.Gui.Controls;
using UAlbion.Game.Input;
using UAlbion.Game.State;

namespace UAlbion.Game.Gui.Menus;

/// <summary>
/// The modern, native-resolution main menu (bespoke high-res UI). Renders a dark stone panel with
/// gold accents and crisp TTF text (via ITextRasterizer) directly at the window's pixel resolution -
/// NOT the 360x240 bitmap UI - over the live 3D vista. Hover/selection animates; clicks are hit-tested
/// against pixel rects. Classic menu remains available via Game.UI.ClassicMainMenu.
/// </summary>
public class ModernMainMenu : Dialog
{
    sealed class Item
    {
        public string Label;
        public Action Action;
        public bool Primary;
        public Rectangle Rect; // pixel rect (set during layout)
    }

    readonly List<Item> _items = [];
    readonly List<BatchLease<SpriteKey, SpriteInfo>> _leases = [];
    int _hover = -1;
    bool _dirty = true;
    int _lastW, _lastH, _itemHash;
    int _pressedItem = -1;
    bool _cursorMoved;
    Vector2 _lastCursor = new(float.NaN, float.NaN);

    // Albion-vibe palette (R,G,B,A in R8G8B8A8 order via the rasterizer/solid helper).
    static readonly (byte r, byte g, byte b, byte a) PanelColor = (18, 14, 10, 232);
    static readonly (byte r, byte g, byte b, byte a) BorderColor = (150, 120, 60, 255);
    static readonly (byte r, byte g, byte b, byte a) GoldText = (224, 188, 104, 255);
    static readonly (byte r, byte g, byte b, byte a) NormalText = (228, 220, 198, 255);
    static readonly (byte r, byte g, byte b, byte a) HoverText = (255, 232, 150, 255);
    static readonly (byte r, byte g, byte b, byte a) HoverBg = (120, 92, 40, 150);

    readonly Dictionary<uint, ITexture> _solids = [];

    public ModernMainMenu() : base(DialogPositioning.Center)
    {
        On<BackendChangedEvent>(_ => _dirty = true);
        On<GameWindowResizedEvent>(_ => _dirty = true);
        On<UiLeftClickEvent>(OnPress);
        On<UiLeftReleaseEvent>(OnRelease);
    }

    protected override void Subscribed()
    {
        _dirty = true;
        _pressedItem = -1;
        _cursorMoved = false;
        _lastCursor = new Vector2(float.NaN, float.NaN);
        RebuildItems();
    }
    protected override void Unsubscribed() => ClearLeases();

    void ClearLeases()
    {
        foreach (var l in _leases) l.Dispose();
        _leases.Clear();
    }

    void RebuildItems()
    {
        _items.Clear();
        var state = TryResolve<IGameState>();
        bool loaded = state?.Loaded == true;

        if (loaded)
            _items.Add(new Item { Label = "Continue", Action = () => Raise(new PopSceneEvent()), Primary = true });

        _items.Add(new Item { Label = "New Game", Action = () => _ = NewGame(), Primary = !loaded });
        _items.Add(new Item { Label = "Load Game", Action = LoadGame });
        if (loaded)
            _items.Add(new Item { Label = "Save Game", Action = SaveGame });

        _items.Add(new Item { Label = "Options", Action = OpenOptions });
        _items.Add(new Item { Label = "Extras", Action = OpenExtras });
        _items.Add(new Item { Label = "View Intro", Action = () => _ = PlayVideo(Base.Video.ApproachToAlbion) });
        _items.Add(new Item { Label = "Credits", Action = () => _ = PlayVideo(Base.Video.Endgame4) });
        _items.Add(new Item { Label = "Quit", Action = () => Raise(new QuitEvent()) });
        _dirty = true;
    }

    ITexture Solid((byte r, byte g, byte b, byte a) c)
    {
        uint key = (uint)(c.r | (c.g << 8) | (c.b << 16) | (c.a << 24));
        if (_solids.TryGetValue(key, out var tex))
            return tex;
        tex = new SimpleTexture<uint>(null, $"solid:{key:X8}", 1, 1, [key], [new Region(0, 0, 1, 1, 1, 1, 0)]);
        _solids[key] = tex;
        return tex;
    }

    public override Vector2 GetSize()
    {
        var w = Resolve<IGameWindow>();
        return new Vector2(w.UiWidth, w.UiHeight);
    }

    public override int Selection(Rectangle extents, int order, SelectionContext context)
    {
        // Hit-test in pixel space so clicks land regardless of the 360x240 layout.
        ArgumentNullException.ThrowIfNull(context);
        var cursor = TryResolve<ICursorManager>()?.Position ?? Vector2.Zero;
        for (int i = 0; i < _items.Count; i++)
            if (_items[i].Rect.Contains((int)cursor.X, (int)cursor.Y))
                context.AddHit(order, this);
        return order;
    }

    int ItemAtCursor()
    {
        var cursor = TryResolve<ICursorManager>()?.Position ?? Vector2.Zero;
        for (int i = 0; i < _items.Count; i++)
            if (_items[i].Rect.Contains((int)cursor.X, (int)cursor.Y))
                return i;
        return -1;
    }

    void OnPress(UiLeftClickEvent e)
    {
        _pressedItem = ItemAtCursor();
        if (_pressedItem >= 0)
            e.Propagating = false;
    }

    void OnRelease(UiLeftReleaseEvent e)
    {
        int item = ItemAtCursor();
        // Require a press+release on the SAME item, and that the user has actually moved the cursor
        // since the menu appeared (defends against a stray click landing as the scene opens).
        if (item >= 0 && item == _pressedItem && _cursorMoved)
        {
            e.Propagating = false;
            _pressedItem = -1;
            _items[item].Action?.Invoke();
            return;
        }
        _pressedItem = -1;
    }

    public override int Render(Rectangle extents, int order, LayoutNode parent)
    {
        if (!IsSubscribed)
            return order;

        _ = parent == null ? null : new LayoutNode(parent, this, extents, order);

        var window = Resolve<IGameWindow>();
        int W = window.PixelWidth, H = window.PixelHeight;
        if (W <= 0 || H <= 0) return order;

        // Hover from the live cursor.
        var cursor = TryResolve<ICursorManager>()?.Position ?? Vector2.Zero;
        if (!float.IsNaN(_lastCursor.X) && cursor != _lastCursor)
            _cursorMoved = true;
        _lastCursor = cursor;
        int newHover = -1;
        for (int i = 0; i < _items.Count; i++)
            if (_items[i].Rect.Contains((int)cursor.X, (int)cursor.Y)) { newHover = i; break; }

        int hash = 0; foreach (var it in _items) hash = hash * 31 + it.Label.GetHashCode(StringComparison.Ordinal);
        if (W != _lastW || H != _lastH || newHover != _hover || hash != _itemHash)
        {
            _lastW = W; _lastH = H; _hover = newHover; _itemHash = hash;
            _dirty = true;
        }

        if (_dirty)
            Rebuild(window, (DrawLayer)order, W, H);

        return order + 3;
    }

    void Rebuild(IGameWindow window, DrawLayer layer, int W, int H)
    {
        _dirty = false;
        ClearLeases();

        var text = TryResolve<ITextRasterizer>();

        // --- Layout (native pixels). Scale with window height so it's consistent at any resolution.
        float s = H / 720f;                       // design scale (720p baseline)
        float titlePx = MathF.Round(64 * s);
        float itemPx = MathF.Round(30 * s);
        int rowH = (int)MathF.Round(itemPx * 1.6f);
        int pad = (int)MathF.Round(28 * s);

        // Measure the widest item to size the panel.
        float maxItemW = text?.Measure("Continue", itemPx) ?? 120;
        foreach (var it in _items)
            maxItemW = MathF.Max(maxItemW, text?.Measure(it.Label, itemPx) ?? 0);
        float titleW = text?.Measure("ALBION", titlePx) ?? 200;

        int panelW = (int)MathF.Max(maxItemW + pad * 4, titleW + pad * 2);
        int titleArea = (int)(titlePx * 1.5f);
        int panelH = pad * 2 + titleArea + _items.Count * rowH + (int)(itemPx * 1.2f); // + version line
        int panelX = (W - panelW) / 2;
        int panelY = (int)(H * 0.5f - panelH * 0.5f);
        // Bias slightly above centre so the vista breathes underneath.
        panelY = Math.Max((int)(H * 0.12f), panelY - (int)(H * 0.04f));

        // --- Panel + gold border.
        DrawQuad(Solid(PanelColor), panelX, panelY, panelW, panelH, layer, W, H);
        int bw = Math.Max(2, (int)MathF.Round(2 * s));
        var border = Solid(BorderColor);
        DrawQuad(border, panelX, panelY, panelW, bw, layer, W, H);                       // top
        DrawQuad(border, panelX, panelY + panelH - bw, panelW, bw, layer, W, H);         // bottom
        DrawQuad(border, panelX, panelY, bw, panelH, layer, W, H);                       // left
        DrawQuad(border, panelX + panelW - bw, panelY, bw, panelH, layer, W, H);         // right

        // --- Title.
        if (text != null)
        {
            var titleTex = text.Render("ALBION", titlePx, GoldText.r, GoldText.g, GoldText.b);
            int tx = panelX + (panelW - titleTex.Regions[0].Width) / 2;
            int ty = panelY + pad;
            DrawTex(titleTex, tx, ty, titleTex.Regions[0].Width, titleTex.Regions[0].Height, (DrawLayer)((int)layer + 2), W, H);
            // gold divider under the title
            DrawQuad(border, panelX + pad, ty + titleArea - (int)(8 * s), panelW - pad * 2, Math.Max(1, (int)(2 * s)), layer, W, H);
        }

        // --- Items.
        int rowY = panelY + pad + titleArea;
        for (int i = 0; i < _items.Count; i++)
        {
            var it = _items[i];
            int rx = panelX + pad;
            int rw = panelW - pad * 2;
            it.Rect = new Rectangle(rx, rowY, rw, rowH);

            bool hovered = i == _hover;
            if (hovered)
                DrawQuad(Solid(HoverBg), rx, rowY + (int)(2 * s), rw, rowH - (int)(4 * s), (DrawLayer)((int)layer + 1), W, H);

            if (text != null)
            {
                var col = hovered ? HoverText : (it.Primary ? GoldText : NormalText);
                var tex = text.Render(it.Label, itemPx, col.r, col.g, col.b);
                int lx = rx + (rw - tex.Regions[0].Width) / 2;
                int ly = rowY + (rowH - tex.Regions[0].Height) / 2;
                DrawTex(tex, lx, ly, tex.Regions[0].Width, tex.Regions[0].Height, (DrawLayer)((int)layer + 2), W, H);
            }
            rowY += rowH;
        }

        // --- Version line.
        if (text != null)
        {
            var v = typeof(ModernMainMenu).Assembly.GetName().Version;
            string vs = v == null ? "UAlbion remake" : $"UAlbion remake  v{v.Major}.{v.Minor}.{v.Build}";
            float vpx = MathF.Round(15 * s);
            var vtex = text.Render(vs, vpx, 150, 140, 120);
            DrawTex(vtex, panelX + (panelW - vtex.Regions[0].Width) / 2, panelY + panelH - (int)(vpx * 1.5f),
                vtex.Regions[0].Width, vtex.Regions[0].Height, (DrawLayer)((int)layer + 2), W, H);
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
        var instances = lease.Lock(ref lockTaken);
        try { instances[0] = new SpriteInfo(SpriteFlags.TopLeft, position, size, tex.Regions[0]); }
        finally { lease.Unlock(lockTaken); }
    }

    // ---- Actions (mirrors MainMenu) ----
    async AlbionTask PlayVideo(Base.Video video)
    {
        var exchange = Exchange;
        Detach();
        await RaiseA(new PlayAnimationEvent(video, 0, 0, 0, 0, 0, 0));
        Attach(exchange);
    }

    async AlbionTask NewGame()
    {
        var exchange = Exchange;
        Detach();
        var response = await RaiseQueryA(new YesNoPromptEvent(Base.SystemText.MainMenu_DoYouReallyWantToStartANewGame));
        if (response)
        {
            await RaiseA(new PlayAnimationEvent(Base.Video.ApproachToAlbion, 0, 0, 0, 0, 0, 0));
            await RaiseA(new NewGameEvent(Base.Map.TorontoBegin, 31, 76));
        }
        else Attach(exchange);
    }

    void LoadGame()
    {
        var menu = new PickSaveSlotMenu(false, Base.SystemText.MainMenu_WhichSavedGameDoYouWantToLoad, 1);
        var exchange = Exchange;
        menu.Closed += (_, id) => { Attach(exchange); if (id.HasValue) Raise(new LoadGameEvent(id.Value)); };
        Exchange.Attach(menu);
        Detach();
    }

    void SaveGame()
    {
        var menu = new PickSaveSlotMenu(true, Base.SystemText.MainMenu_SaveOnWhichPosition, 1);
        var exchange = Exchange;
        menu.Closed += (_, id) => { Attach(exchange); if (id.HasValue) _ = SaveWithName(id.Value); };
        Exchange.Attach(menu);
        Detach();
    }

    async AlbionTask SaveWithName(ushort slot)
    {
        var exchange = Exchange;
        var name = await RaiseQueryA(new TextPromptEvent());
        if (string.IsNullOrWhiteSpace(name)) name = $"Save {slot}";
        exchange.Raise(new SaveGameEvent(slot, name), this);
        exchange.Raise(new PopSceneEvent(), this);
    }

    void OpenOptions()
    {
        var menu = new OptionsMenu();
        var exchange = Exchange;
        menu.Closed += (_, _) => Attach(exchange);
        Exchange.Attach(menu);
        Detach();
    }

    void OpenExtras()
    {
        var menu = new ExtrasMenu();
        var exchange = Exchange;
        menu.Closed += (_, _) => Attach(exchange);
        Exchange.Attach(menu);
        Detach();
    }
}
