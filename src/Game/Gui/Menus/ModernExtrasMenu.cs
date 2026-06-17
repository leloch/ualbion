using System;
using UAlbion.Api.Settings;

namespace UAlbion.Game.Gui.Menus;

/// <summary>Modern (native-res) Extras / customisation menu - the opt-in enhancement toggles + gamma,
/// applied live and persisted on OK. Mirrors ExtrasMenu but in the high-res UI.</summary>
public class ModernExtrasMenu : NativeMenuDialog
{
    protected override string Title => "Extras";

    bool Get(BoolVar v) => ReadVar(v);
    void Set(BoolVar v, bool value) { v.Write(Resolve<ISettings>(), value); }

    protected override void BuildWidgets()
    {
        AddHeader("3D RENDERING");
        AddToggle("Smooth textures", () => Get(V.Game.Graphics.SmoothDungeonTextures), x => Set(V.Game.Graphics.SmoothDungeonTextures, x));
        AddToggle("Highlight interactable", () => Get(V.Game.Graphics.HighlightInteractableTiles), x => Set(V.Game.Graphics.HighlightInteractableTiles, x));
        AddToggle("Distance fog / lighting", () => Get(V.Game.Graphics.DungeonFog), x => Set(V.Game.Graphics.DungeonFog, x));
        AddToggle("Bump mapping", () => Get(V.Game.Graphics.BumpMapping), x => Set(V.Game.Graphics.BumpMapping, x));

        AddHeader("INTERFACE");
        AddToggle("3D minimap", () => Get(V.Game.Graphics.Minimap), x => Set(V.Game.Graphics.Minimap, x));
        AddToggle("Context-menu reach limit", () => Get(V.Game.Ui.ContextMenuReachLimit), x => Set(V.Game.Ui.ContextMenuReachLimit, x));
        AddToggle("Classic main menu", () => Get(V.Game.Ui.ClassicMainMenu), x => Set(V.Game.Ui.ClassicMainMenu, x));

        AddHeader("DISPLAY");
        AddSlider("Gamma",
            () => (int)MathF.Round(ReadVar(V.Core.Gfx.Gamma) * 100),
            x => V.Core.Gfx.Gamma.Write(Resolve<ISettings>(), x / 100f),
            50, 250, x => (x / 100f).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));

        AddSpacer();
        AddButton("Done", () => { Resolve<ISettings>().Save(); Close(); }, primary: true);
    }
}
