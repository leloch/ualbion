using System.Numerics;
using UAlbion.Api.Eventing;
using UAlbion.Api.Visual;
using UAlbion.Core;
using UAlbion.Core.Events;
using UAlbion.Core.Visual;
using UAlbion.Formats.Assets;
using UAlbion.Formats.Config;
using UAlbion.Formats.Ids;
using UAlbion.Game.Events;

namespace UAlbion.Game.Scenes;

[Scene(SceneId.MainMenu)]
public class MenuScene : Container, IScene
{
    bool _clockWasRunning;
    IComponent _backdrop; // either the classic 2D picture or the live 3D vista, rebuilt per activation
    bool _backdropClassic;
    public ICamera Camera { get; }

    public MenuScene() : base(nameof(SceneId.MainMenu))
    {
        // Perspective camera so the modern menu can render a live 3D vista behind the UI. The menu's
        // 2D UI (dialogs, the classic background sprite) is screen-space (NoTransform), so it renders
        // identically regardless of camera type - classic mode is unaffected.
        Camera = AttachChild(new PerspectiveCamera(true));

        // Live-switch the backdrop the instant the Classic-menu toggle changes (e.g. from the Extras
        // menu open over this scene), so the choice previews immediately rather than on next entry.
        On<EngineUpdateEvent>(_ =>
        {
            if (_backdrop != null && ReadVar(V.Game.Ui.ClassicMainMenu) != _backdropClassic)
            {
                RemoveChild(_backdrop);
                _backdrop = null;
                BuildBackdrop();
            }
        });
    }

    protected override void Subscribed()
    {
        _clockWasRunning = Resolve<IClock>().IsRunning;
        if (_clockWasRunning)
            Raise(new StopClockEvent());

        Raise(new PushMouseModeEvent(MouseMode.Normal2D));
        Raise(new PushInputModeEvent(InputMode.MainMenu));

        BuildBackdrop();
    }

    void BuildBackdrop()
    {
        if (_backdrop != null)
            return;

        bool classic = ReadVar(V.Game.Ui.ClassicMainMenu);
        _backdropClassic = classic;
        if (classic)
        {
            // Vanilla 1996 look: a single full-screen title picture. (TODO: random selection like the original.)
            _backdrop = AttachChild(new Sprite(
                (SpriteId)Base.Picture.MenuBackground8,
                DrawLayer.Interface,
                SpriteKeyFlags.NoTransform,
                SpriteFlags.LeftAligned)
            {
                Position = new Vector3(-1.0f, 1.0f, 0),
                Size = new Vector2(2.0f, -2.0f)
            });
        }
        else
        {
            // Modern: a slowly-panning live 3D Albion vista (showcases the skybox/fog/bump rendering).
            _backdrop = AttachChild(new MenuBackdrop3D(Camera, Base.Map.Jirinaar));
        }
    }

    protected override void Unsubscribed()
    {
        Raise(new PopMouseModeEvent());
        Raise(new PopInputModeEvent());
        if (_clockWasRunning)
            Raise(new StartClockEvent());

        if (_backdrop != null)
        {
            RemoveChild(_backdrop);
            _backdrop = null;
        }
    }
}
