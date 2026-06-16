using System.Numerics;
using UAlbion.Api.Visual;
using UAlbion.Core;
using UAlbion.Core.Events;
using UAlbion.Core.Visual;
using UAlbion.Formats.Assets;

namespace UAlbion.Game.Gui.Controls;

public class ButtonColorScheme
{
    public CommonColor? TopLeft { get; set; }
    public CommonColor? BottomRight { get; set; }
    public CommonColor? Corners { get; set; }
    public CommonColor? Background { get; set; }
    public float Alpha { get; set; }
}

public class ButtonFrame : UiElement
{
    public delegate ButtonColorScheme ThemeFunction(ButtonState state);

    BatchLease<SpriteKey, SpriteInfo> _sprite;
    Rectangle _lastExtents;
    ButtonState _state = ButtonState.Normal;
    ThemeFunction _theme = ButtonTheme.Default;
    int _padding = 2;
    bool _doubleFrame;

    // #27: the original draws some buttons (e.g. the inventory page 1/2/3 tabs) with a two-pixel
    // thick border. When set, every border edge/corner is drawn at thickness 2 and the content is
    // inset by an extra pixel to keep the same internal spacing.
    public bool DoubleFrame
    {
        get => _doubleFrame;
        set { if (value != _doubleFrame) { _doubleFrame = value; _lastExtents = new Rectangle(); } }
    }

    int BorderThickness => _doubleFrame ? 2 : 1;
    int ExtraInset => _doubleFrame ? 1 : 0; // extra content inset for the thicker border

    public ThemeFunction Theme
    {
        get => _theme;
        set
        {
            if (_theme == value) return;
            _theme = value;
            _lastExtents = new Rectangle();
        }
    }

    public ButtonState State
    {
        get => _state;
        set { if (value != _state) { _state = value; _lastExtents = new Rectangle(); } }
    }

    public int Padding // Adjust by 1 pixel to account for the border.
    {
        get => _padding - 1;
        set { if (value != _padding - 1) { _padding = value + 1; _lastExtents = new Rectangle(); } }
    }

    public ButtonFrame(IUiElement child)
    {
        On<BackendChangedEvent>(_ => _lastExtents = new Rectangle());
        On<GameWindowResizedEvent>(_ => _lastExtents = new Rectangle());

        if (child != null)
            AttachChild(child);
    }

    protected override void Unsubscribed()
    {
        _sprite?.Dispose();
        _sprite = null;
    }

    void Rebuild(Rectangle extents, DrawLayer order)
    {
        if (!IsSubscribed || _sprite != null && _lastExtents == extents && _sprite.Key.RenderOrder == order)
            return;

        _lastExtents = extents;

        var window = Resolve<IGameWindow>();
        var cc = Resolve<ICommonColors>();
        var theme = _theme(_state);

        int instanceCount =
            (theme.TopLeft.HasValue     ? 2 : 0)
          + (theme.BottomRight.HasValue ? 2 : 0)
          + (theme.Corners.HasValue     ? 2 : 0)
          + (theme.Background.HasValue  ? 1 : 0);

        if (_sprite?.Key.RenderOrder != order || instanceCount != _sprite?.Length)
        {
            _sprite?.Dispose();

            if (instanceCount == 0)
                return;

            var sm = Resolve<IBatchManager<SpriteKey, SpriteInfo>>();
            var key = new SpriteKey(cc.BorderTexture, SpriteSampler.Point, order, SpriteKeyFlags.NoTransform | SpriteKeyFlags.NoDepthTest);
            _sprite = sm.Borrow(key, instanceCount, this);
        }

        bool lockWasTaken = false;
        var instances = _sprite.Lock(ref lockWasTaken);
        SpriteInfo Build(SpriteFlags flags,
            int x, int y, int w, int h,
            CommonColor color) => new(
                flags,
                new Vector3(window.UiToNorm(x, y), 0),
                window.UiToNormRelative(w, h),
                cc.GetRegion(color));

        try
        {
            var (x, y, w, h) = (extents.X, extents.Y, extents.Width, extents.Height);
            int curInstance = 0;
            int t = BorderThickness; // 1 normally, 2 for a double-thick frame (#27). t==1 reduces to the original layout.
            var flags = SpriteFlags.TopLeft | SpriteFlags.None.SetOpacity(theme.Alpha);

            if (theme.TopLeft.HasValue)
            {
                // Top
                instances[curInstance++] = Build(flags, x, y, w - t, t, theme.TopLeft.Value);

                // Left
                instances[curInstance++] = Build(flags, x, y + t, t, h - 2 * t, theme.TopLeft.Value);
            }

            if (theme.BottomRight.HasValue)
            {
                // Bottom
                instances[curInstance++] = Build(flags, x + t, y + h - t, w - t, t, theme.BottomRight.Value);

                // Right
                instances[curInstance++] = Build(flags, x + w - t, y + t, t, h - 2 * t, theme.BottomRight.Value);
            }

            if (theme.Corners.HasValue)
            {
                // Bottom Left Corner
                instances[curInstance++] = Build(flags, x, y + h - t, t, t, theme.Corners.Value);

                // Top Right Corner
                instances[curInstance++] = Build(flags, x + w - t, y, t, t, theme.Corners.Value);
            }

            if (theme.Background.HasValue)
            {
                // Background
                instances[curInstance] = Build(
                    flags.SetOpacity(theme.Alpha < 1.0f ? theme.Alpha / 2 : theme.Alpha),
                    x + t, y + t, w - 2 * t, h - 2 * t,
                    theme.Background.Value);
            }
        }
        finally { _sprite.Unlock(lockWasTaken); }
    }

    public override Vector2 GetSize() => GetMaxChildSize() + (_padding + ExtraInset) * 2 * Vector2.One;

    protected override int DoLayout<T>(Rectangle extents, int order, T context, LayoutFunc<T> func)
    {
        int pad = _padding + ExtraInset;
        var innerExtents = new Rectangle(
            extents.X + pad,
            extents.Y + pad,
            extents.Width - pad * 2,
            extents.Height - pad * 2);

        return base.DoLayout(innerExtents, order + 1, context, func);
    }

    public override int Render(Rectangle extents, int order, LayoutNode parent)
    {
        Rebuild(extents, (DrawLayer)order);
        return base.Render(extents, order, parent);
    }

    public override int Selection(Rectangle extents, int order, SelectionContext context)
    {
        Rebuild(extents, (DrawLayer)order);
        return base.Selection(extents, order, context);
    }
}