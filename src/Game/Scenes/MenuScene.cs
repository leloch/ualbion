using System.Numerics;
using UAlbion.Api.Eventing;
using UAlbion.Api.Visual;
using UAlbion.Core;
using UAlbion.Core.Events;
using UAlbion.Core.Visual;
using UAlbion.Formats.Assets;
using UAlbion.Formats.Config;
using UAlbion.Formats.Ids;
using UAlbion.Formats.ScriptEvents;
using UAlbion.Game.Events;
using UAlbion.Game.State;

namespace UAlbion.Game.Scenes;

[Scene(SceneId.MainMenu)]
public class MenuScene : Container, IScene
{
    bool _clockWasRunning;
    IComponent _backdrop; // either the classic 2D picture or the live 3D vista, rebuilt per activation
    IComponent _menuUi;   // classic MainMenu dialog or the modern native-res ModernMainMenu
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
                TearDown();
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

        // Title music - only on the boot/title menu (no game loaded). When the menu is opened over a
        // running game (Escape), leave the map's music playing instead of stomping it.
        var state = TryResolve<IGameState>();
        if (state == null || !state.Loaded)
            Raise(new SongEvent(Base.Song.JirinaarMusic));

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
            // Vanilla 1996 look: a single full-screen title picture + the original bitmap-UI menu.
            _backdrop = AttachChild(new Sprite(
                (SpriteId)Base.Picture.MenuBackground8,
                DrawLayer.Interface,
                SpriteKeyFlags.NoTransform,
                SpriteFlags.LeftAligned)
            {
                Position = new Vector3(-1.0f, 1.0f, 0),
                Size = new Vector2(2.0f, -2.0f)
            });
            _menuUi = AttachChild(new Gui.Menus.MainMenu());
        }
        else
        {
            // Modern: a remastered, high-res painted Albion vista + the bespoke high-res menu over it.
            _backdrop = AttachChild(new MenuBackdropImage());
            _menuUi = AttachChild(new Gui.Menus.ModernMainMenu());
        }
    }

    void TearDown()
    {
        if (_menuUi != null) { RemoveChild(_menuUi); _menuUi = null; }
        if (_backdrop != null) { RemoveChild(_backdrop); _backdrop = null; }
    }

    protected override void Unsubscribed()
    {
        Raise(new PopMouseModeEvent());
        Raise(new PopInputModeEvent());
        if (_clockWasRunning)
            Raise(new StartClockEvent());

        TearDown();
    }
}
