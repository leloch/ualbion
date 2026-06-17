using UAlbion.Api.Eventing;
using UAlbion.Game.Events;

namespace UAlbion.Game.Gui.Menus;

/// <summary>
/// Global handler for <see cref="ShowExtrasMenuEvent"/> ("extras"): toggles the Extras /
/// customisation menu as a modal over whatever scene is currently active. Opening it in-world
/// (via the bound hotkey) lets the graphics toggles be previewed live; the same menu is also
/// reachable from the main menu's Extras button.
/// </summary>
public class ExtrasMenuManager : Component
{
    ExtrasMenu _menu;

    public ExtrasMenuManager() => On<ShowExtrasMenuEvent>(_ => Toggle());

    void Toggle()
    {
        if (_menu != null)
        {
            _menu.Remove();
            _menu = null;
            return;
        }

        _menu = new ExtrasMenu();
        _menu.Closed += (_, _) => _menu = null;
        Exchange.Attach(_menu);
    }
}
