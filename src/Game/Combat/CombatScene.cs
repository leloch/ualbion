using UAlbion.Api.Eventing;
using UAlbion.Core;
using UAlbion.Core.Visual;
using UAlbion.Formats.Config;
using UAlbion.Formats.ScriptEvents;
using UAlbion.Game.Events;
using UAlbion.Game.Scenes;

namespace UAlbion.Game.Combat;

[Scene(SceneId.Combat)]
public class CombatScene : Container, IScene
{
    bool _clockWasRunning;
    public ICamera Camera { get; }
    public CombatScene() : base(nameof(SceneId.Combat))
    {
        Camera = AttachChild(new PerspectiveCamera());
    }

    protected override void Subscribed()
    {
        _clockWasRunning = Resolve<IClock>().IsRunning;
        if (_clockWasRunning)
            Raise(new StopClockEvent());

        // Note: the world keeps rendering during combat; the painted backdrop covers it
        // (a NoDepthTest sprite in Battle, like the combat UI). ShowMapEvent(false/true)
        // hides the map but does NOT restore it cleanly on re-show, so it stays unused.
        Raise(new PushMouseModeEvent(MouseMode.Normal2D));
        Raise(new PushInputModeEvent(InputMode.Combat));
    }

    protected override void Unsubscribed()
    {
        Raise(new PopMouseModeEvent());
        Raise(new PopInputModeEvent());

        if (_clockWasRunning)
            Raise(new StartClockEvent());
    }
}