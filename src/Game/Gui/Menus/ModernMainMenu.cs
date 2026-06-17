using UAlbion.Api;
using UAlbion.Api.Eventing;
using UAlbion.Core.Events;
using UAlbion.Formats.MapEvents;
using UAlbion.Game.Events;
using UAlbion.Game.State;

namespace UAlbion.Game.Gui.Menus;

/// <summary>
/// The modern, native-resolution main menu, rendered over the live 3D vista. All visuals/interaction
/// come from NativeMenuDialog; this just defines the items and wires the actions. The classic menu
/// stays available via Game.UI.ClassicMainMenu.
/// </summary>
public class ModernMainMenu : NativeMenuDialog
{
    protected override string Title => "ALBION";

    protected override void BuildWidgets()
    {
        bool loaded = TryResolve<IGameState>()?.Loaded == true;

        if (loaded) AddButton("Continue", () => Raise(new PopSceneEvent()), primary: true);
        AddButton("New Game", () => _ = NewGame(), primary: !loaded);
        AddButton("Load Game", LoadGame);
        if (loaded) AddButton("Save Game", SaveGame);
        AddButton("Options", OpenOptions);
        AddButton("Extras", OpenExtras);
        AddButton("View Intro", () => _ = PlayVideo(Base.Video.ApproachToAlbion));
        AddButton("Credits", () => _ = PlayVideo(Base.Video.Endgame4));
        AddButton("Quit", () => Raise(new QuitEvent()));
    }

    protected override void OnCancel()
    {
        if (TryResolve<IGameState>()?.Loaded == true)
            Raise(new PopSceneEvent());
    }

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
        var menu = new ModernSaveMenu(false);
        var exchange = Exchange;
        menu.Closed += (_, id) => { Attach(exchange); if (id.HasValue) Raise(new LoadGameEvent(id.Value)); };
        Exchange.Attach(menu);
        Detach();
    }

    void SaveGame()
    {
        var menu = new ModernSaveMenu(true);
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

    void OpenOptions() => OpenSub(new ModernOptionsMenu());
    void OpenExtras() => OpenSub(new ModernExtrasMenu());

    void OpenSub(NativeMenuDialog menu)
    {
        var exchange = Exchange;
        menu.Closed += (_, _) => Attach(exchange);
        Exchange.Attach(menu);
        Detach();
    }
}
