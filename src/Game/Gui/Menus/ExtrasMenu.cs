using System;
using System.Collections.Generic;
using System.Globalization;
using UAlbion.Api.Settings;
using UAlbion.Formats.Assets;
using UAlbion.Game.Events;
using UAlbion.Game.Gui.Controls;
using UAlbion.Game.Gui.Text;
using UAlbion.Game.Text;

namespace UAlbion.Game.Gui.Menus;

/// <summary>
/// A remake-specific "Extras / Customisation" menu, deliberately SEPARATE from the vanilla
/// OptionsMenu so the original options screen stays pristine. It collects all the opt-in
/// enhancements (the Game.Graphics.* toggles + gamma) behind one screen. Each control applies
/// its change live (so it can be previewed when opened over the world) and OK persists; cancelling
/// (right-click / close) reverts everything to the values that were in effect when it opened.
/// </summary>
public class ExtrasMenu : ModalDialog
{
    public event EventHandler Closed;

    sealed class Toggle
    {
        public BoolVar Var;
        public bool Original;
        public bool Current;
    }

    readonly List<Toggle> _toggles = [];
    float _originalGamma;
    int _gamma100; // gamma * 100, for the integer Slider
    int _version = 1; // bumped on every toggle so the dynamic button labels refresh

    public ExtrasMenu() : base(DialogPositioning.Center)
    {
        On<UiRightClickEvent>(e => { Cancel(); e.Propagating = false; });
        On<CloseWindowEvent>(e => { Cancel(); e.Propagating = false; });
    }

    protected override void Subscribed()
    {
        _toggles.Clear();
        _originalGamma = ReadVar(V.Core.Gfx.Gamma);
        _gamma100 = (int)Math.Round(Math.Clamp(_originalGamma, 0.5f, 2.5f) * 100);

        var elements = new List<IUiElement>
        {
            new Spacing(192, 2),
            new BoldHeader(new SimpleText("Extras / Customisation").Fat().Center()),
            new Divider(CommonColor.Yellow3),
            new Spacing(0, 3),

            new SimpleText("3D rendering").Center(),
            ToggleRow("Smooth textures", V.Game.Graphics.SmoothDungeonTextures),
            ToggleRow("Highlight interactable", V.Game.Graphics.HighlightInteractableTiles),
            ToggleRow("Distance fog / lighting", V.Game.Graphics.DungeonFog),
            ToggleRow("Bump mapping", V.Game.Graphics.BumpMapping),
            new Spacing(0, 3),

            new SimpleText("Interface").Center(),
            ToggleRow("3D minimap", V.Game.Graphics.Minimap),
            ToggleRow("Context-menu reach limit", V.Game.Ui.ContextMenuReachLimit),
            new Spacing(0, 3),

            new SimpleText("Gamma").Center(),
            new Slider(() => _gamma100, SetGamma, 50, 250,
                x => (x / 100f).ToString("0.00", CultureInfo.InvariantCulture)),
            new Spacing(0, 3),

            new Button(Base.SystemText.MsgBox_OK).OnClick(SaveAndClose),
            new Spacing(0, 2),
        };

        AttachChild(new DialogFrame(new VerticalStacker(elements)));
    }

    IUiElement ToggleRow(string name, BoolVar var)
    {
        var toggle = new Toggle { Var = var, Original = ReadVar(var), Current = ReadVar(var) };
        _toggles.Add(toggle);

        return new Button(new DynamicText(
                () => [new TextBlock($"{name}: {(toggle.Current ? "On" : "Off")}") { Alignment = TextAlignment.Center }],
                _ => _version))
            .OnClick(() =>
            {
                toggle.Current = !toggle.Current;
                _version++; // refresh the label
                var.Write(Resolve<ISettings>(), toggle.Current); // live-apply (no save yet)
            });
    }

    void SetGamma(int value)
    {
        _gamma100 = value;
        V.Core.Gfx.Gamma.Write(Resolve<ISettings>(), value / 100f); // live-apply
    }

    void SaveAndClose()
    {
        Resolve<ISettings>().Save(); // persist whatever is currently applied
        Closed?.Invoke(this, EventArgs.Empty);
        Remove();
    }

    void Cancel()
    {
        var settings = Resolve<ISettings>();
        foreach (var toggle in _toggles)
            toggle.Var.Write(settings, toggle.Original); // revert live changes (without saving)
        V.Core.Gfx.Gamma.Write(settings, _originalGamma);

        Closed?.Invoke(this, EventArgs.Empty);
        Remove();
    }
}
