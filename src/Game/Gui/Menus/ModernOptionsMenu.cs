using System.Collections.Generic;
using System.Linq;
using UAlbion.Api.Settings;
using UAlbion.Formats;
using UAlbion.Game.Events;

namespace UAlbion.Game.Gui.Menus;

/// <summary>Modern (native-res) Options menu - language, audio volumes and combat-text delay,
/// applied live and persisted on OK. Mirrors the classic OptionsMenu in the high-res UI.</summary>
public class ModernOptionsMenu : NativeMenuDialog
{
    readonly List<(string code, string name)> _languages = [];
    int _langIndex;

    protected override string Title => "Options";

    protected override void BuildWidgets()
    {
        if (_languages.Count == 0)
        {
            var modApplier = TryResolve<IModApplier>();
            if (modApplier != null)
            {
                foreach (var kvp in modApplier.Languages.OrderBy(x => x.Value.ShortName))
                    if (Assets.IsStringDefined(Base.SystemText.MainMenu_MainMenu, kvp.Key))
                        _languages.Add((kvp.Key, kvp.Value.ShortName));
            }
            var cur = ReadVar(V.User.Gameplay.Language);
            _langIndex = System.Math.Max(0, _languages.FindIndex(l => l.code == cur));
        }

        AddHeader("AUDIO");
        AddSlider("Music volume", () => ReadVar(V.User.Audio.MusicVolume), x => Raise(new SetMusicVolumeEvent(x)), 0, 127);
        AddSlider("FX volume", () => ReadVar(V.User.Audio.FxVolume), x => Raise(new SetFxVolumeEvent(x)), 0, 127);

        AddHeader("GAMEPLAY");
        AddSlider("Combat text delay", () => ReadVar(V.User.Gameplay.CombatDelay), x => Raise(new SetCombatDelayEvent(x)), 1, 50);

        if (_languages.Count > 0)
        {
            AddHeader("LANGUAGE");
            AddButton($"Language:  {_languages[_langIndex].name}", CycleLanguage);
        }

        AddSpacer();
        AddButton("Done", () => { Resolve<ISettings>().Save(); Close(); }, primary: true);
    }

    void CycleLanguage()
    {
        if (_languages.Count == 0) return;
        _langIndex = (_langIndex + 1) % _languages.Count;
        Raise(new SetLanguageEvent(_languages[_langIndex].code));
        Widgets.Clear();
        BuildWidgets(); // rebuild to refresh the language label
        MarkDirty();
    }
}
