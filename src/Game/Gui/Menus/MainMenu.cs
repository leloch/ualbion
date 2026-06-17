using System.Collections.Generic;
using UAlbion.Api.Eventing;
using UAlbion.Core.Events;
using UAlbion.Formats.Assets;
using UAlbion.Formats.MapEvents;
using UAlbion.Game.Events;
using UAlbion.Game.Gui.Controls;
using UAlbion.Game.State;

namespace UAlbion.Game.Gui.Menus;

public class MainMenu : Dialog
{
    public MainMenu() : base(DialogPositioning.Center)
    {
        On<CloseWindowEvent>(_ => Raise(new PopSceneEvent()));
    }

    protected override void Subscribed()
    {
        RemoveAllChildren();

        var state = Resolve<IGameState>();
        var elements = new List<IUiElement>
        {
            new Spacing(0, 2),
            new HorizontalStacker(
                new Spacing(5, 0),
                new BoldHeader(Base.SystemText.MainMenu_MainMenu),
                new Spacing(5, 0)),
            new Divider(CommonColor.Yellow3),
            new Spacing(0, 2),
        };

        if (state.Loaded)
        {

            elements.AddRange([
                new Button(Base.SystemText.MainMenu_ContinueGame).OnClick(() => Raise(new PopSceneEvent())),
                new Spacing(0, 4)
            ]);
        }

        elements.AddRange([
            new Button(Base.SystemText.MainMenu_NewGame).OnClick(() => _ = NewGame()),
            new Button(Base.SystemText.MainMenu_LoadGame).OnClick(LoadGame)
        ]);

        if (state.Loaded)
            elements.Add(new Button(Base.SystemText.MainMenu_SaveGame).OnClick(SaveGame));

        elements.AddRange([
            new Spacing(0,4),
            new Button(Base.SystemText.MainMenu_Options).OnClick(Options),
            new Button("Extras").OnClick(Extras), // remake-only: opt-in enhancements
            new Button(Base.SystemText.MainMenu_ViewIntro).OnClick(() => _ = PlayVideo(Base.Video.ApproachToAlbion)),
            new Button(Base.SystemText.MainMenu_Credits).OnClick(() => _ = PlayVideo(Base.Video.Endgame4)),
            new Spacing(0,3),
            new Button(Base.SystemText.MainMenu_QuitGame).OnClick(() => Raise(new QuitEvent())),
            new Spacing(0,2),
            new UAlbion.Game.Gui.Text.SimpleText($"UAlbion remake  v{Version}").Center(),
            new Spacing(0,1)
        ]);

        var stack = new VerticalStacker(elements);
        AttachChild(new DialogFrame(stack));
    }

    static string Version
    {
        get
        {
            var v = typeof(MainMenu).Assembly.GetName().Version;
            return v == null ? "dev" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    // Replay a menu cinematic via the FLIC player (the buttons were previously dead). The
    // menu is detached during playback and reattached after. ViewIntro = ApproachToAlbion;
    // Credits has no dedicated enum entry, so it plays the final endgame FLIC (best available).
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
        Detach(); // Hide the main menu while the prompt is active

        var e = new YesNoPromptEvent(Base.SystemText.MainMenu_DoYouReallyWantToStartANewGame);
        var response = await RaiseQueryA(e);
        
        if (response)
        {
            // Play the intro cinematic before the game starts (the original opens a new game with
            // the approach-to-Albion FLIC). Menu stays detached through the video, then the new
            // game loads. ApproachToAlbion is the same FLIC the ViewIntro button uses.
            await RaiseA(new PlayAnimationEvent(Base.Video.ApproachToAlbion, 0, 0, 0, 0, 0, 0));
            await RaiseA(new NewGameEvent(Base.Map.TorontoBegin, 31, 76)); // TODO: Move this to config?
        }
        else
        {
            Attach(exchange); // Cancelled — restore the menu
        }
    }

    void LoadGame()
    {
        var menu = new PickSaveSlotMenu(false, Base.SystemText.MainMenu_WhichSavedGameDoYouWantToLoad, 1);
        var exchange = Exchange;
        menu.Closed += (_, id) =>
        {
            Attach(exchange);
            if (id.HasValue)
                Raise(new LoadGameEvent(id.Value));
        };
        Exchange.Attach(menu);
        Detach();
    }

    void SaveGame()
    {
        var menu = new PickSaveSlotMenu(true, Base.SystemText.MainMenu_SaveOnWhichPosition, 1);
        var exchange = Exchange;
        menu.Closed += (_, id) =>
        {
            Attach(exchange);
            if (id.HasValue)
                _ = SaveWithName(id.Value); // fire-and-forget: prompts for a name then saves
        };
        Exchange.Attach(menu);
        Detach();
    }

    async AlbionTask SaveWithName(ushort slot)
    {
        // Prompt for the save name (the same text-entry prompt conversations use);
        // an empty entry falls back to a default name. The exchange is captured because
        // closing the prompt can also close/detach this menu before the await resumes —
        // the save must fire regardless.
        var exchange = Exchange;
        var name = await RaiseQueryA(new TextPromptEvent());
        if (string.IsNullOrWhiteSpace(name))
            name = $"Save {slot}";

        exchange.Raise(new SaveGameEvent(slot, name), this);
        exchange.Raise(new PopSceneEvent(), this); // back to the game after saving
    }

    void Options()
    {
        var optionsMenu = new OptionsMenu();
        var exchange = Exchange;
        optionsMenu.Closed += (_, _) => Attach(exchange);
        Exchange.Attach(optionsMenu);
        Detach();
    }

    void Extras()
    {
        var extrasMenu = new ExtrasMenu();
        var exchange = Exchange;
        extrasMenu.Closed += (_, _) => Attach(exchange);
        Exchange.Attach(extrasMenu);
        Detach();
    }
}