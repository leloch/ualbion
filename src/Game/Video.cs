using System;
using System.Numerics;
using UAlbion.Api.Visual;
using UAlbion.Core.Events;
using UAlbion.Core.Visual;
using UAlbion.Formats.Assets.Flic;
using UAlbion.Formats.Ids;
using UAlbion.Formats.ScriptEvents;

namespace UAlbion.Game;

public class Video : GameComponent
{
    readonly VideoId _id;
    readonly bool _looping;
    readonly Vector2? _uiPosition;
    Sprite _sprite;
    FlicPlayer _player;
    SimpleTexture<byte> _texture;
    TextureDirtyEvent _dirtyEvent;
    PaletteId _previousPaletteId;
    int _lastPaletteVersion;

    event Action Complete;

    /// <summary>Fires every time the animation wraps back to frame 0 (looping videos).</summary>
    public event Action CycleCompleted;

    /// <param name="uiPosition">When set, the video renders at this UI-pixel position
    /// (360×240 space) at its native size instead of fullscreen — used by the script
    /// `start_anim` overlays (e.g. the intro cockpit window animations).</param>
    public Video(VideoId id, bool looping, Vector2? uiPosition = null)
    {
        _id = id;
        _looping = looping;
        _uiPosition = uiPosition;
        On<IdleClockEvent>(OnIdleClock);
    }

    void OnIdleClock(IdleClockEvent _)
    {
        if (_player == null)
            return;

        if (!_looping && _player.Frame == _player.FrameCount - 2)
        {
            Info($"Vid {_id} complete");
            Complete?.Invoke();
            Remove();
        }
        else
        {
            _player.NextFrame();
            if (_player.Frame == 0)
                CycleCompleted?.Invoke();

            // Multi-scene FLICs (endgame montage etc.) carry palette chunks mid-stream; the
            // palette manager copied the entries at load, so re-raise on change or every
            // scene after the first renders with the previous scene's colours (garbled).
            if (_player.PaletteVersion != _lastPaletteVersion)
            {
                _lastPaletteVersion = _player.PaletteVersion;
                Raise(new LoadRawPaletteEvent($"P:V:{_id}", _player.Palette));
            }

            Raise(_dirtyEvent);
        }
    }

    public Vector3 Position
    {
        get => _sprite.Position;
        set => _sprite.Position = value;
    }

    protected override void Subscribed()
    {
        if (_player != null)
            return;

        var flic = Assets.LoadVideo(_id);
        if (flic == null)
        {
            Complete?.Invoke();
            Remove();
            return;
        }

        var size = new Vector2(flic.Width, flic.Height);

        var texture = new SimpleTexture<byte>(
            _id,
            $"V:{_id}",
            flic.Width, flic.Height,
            [new Region(Vector2.Zero, size, size, 0)]);

        _texture = texture;
        _dirtyEvent = new TextureDirtyEvent(_texture);
        _player = flic.Play(() => texture.GetMutableLayerBuffer(0).Buffer);
        // A fullscreen cutscene must cover ALL the UI (status bar party portraits etc) - otherwise,
        // when View Intro / Credits is launched mid-game, the party portraits show through the video
        // and pick up the FLIC's palette (the "purple health bars" bug). Positioned overlays (the
        // intro cockpit anims) stay on the normal Interface layer, above the backdrop picture.
        var layer = _uiPosition.HasValue ? DrawLayer.Interface : DrawLayer.InterfaceOverlay;
        _sprite = AttachChild(new Sprite(SpriteId.None,
            layer,
            SpriteKeyFlags.NoTransform,
            SpriteFlags.LeftAligned | SpriteFlags.FlipVertical,
            _ => _texture)
        {
            Position = new Vector3(-1, -1, 0),
        });

        if (_uiPosition is { } uiPos)
        {
            // Positioned overlay: UI pixels (360×240 logical space) → NDC.
            const float UiW = 360f, UiH = 240f;
            float w = 2 * flic.Width / UiW;
            float h = 2 * flic.Height / UiH;
            float x = -1 + 2 * uiPos.X / UiW;
            float yTop = 1 - 2 * uiPos.Y / UiH; // UI origin is top-left; NDC +Y is up
            _sprite.Position = new Vector3(x, yTop - h, 0);
            _sprite.Size = new Vector2(w, h);
        }
        else
        {
            _sprite.Size = 2 * Vector2.One;
        }

        var oldId = Resolve<IPaletteManager>().Day?.Id;
        if (oldId.HasValue)
            _previousPaletteId = PaletteId.FromUInt32(oldId.Value);
        Raise(new LoadRawPaletteEvent($"P:V:{_id}", _player.Palette));
    }

    protected override void Unsubscribed()
    {
        base.Unsubscribed();
        Raise(new LoadPaletteEvent(_previousPaletteId));
    }

    public Video OnComplete(Action continuation)
    {
        Complete += continuation;
        return this;
    }
}
