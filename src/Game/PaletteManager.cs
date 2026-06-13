using System;
using System.Linq;
using UAlbion.Api;
using UAlbion.Api.Visual;
using UAlbion.Core.Events;
using UAlbion.Core.Visual;
using UAlbion.Formats.Assets;
using UAlbion.Formats.Ids;
using UAlbion.Formats.ScriptEvents;
using UAlbion.Game.State;

namespace UAlbion.Game;

public class PaletteManager : GameServiceComponent<IPaletteManager>, IPaletteManager
{
    public IPalette Day { get; private set; }
    public IPalette Night { get; private set; }
    public int Frame { get; private set; }
    public float Blend
    {
        get
        {
            var state = TryResolve<IGameState>();
            // Day/night cycling is gated on the MAP's lighting mode (mapFlags & 3 ==
            // DayNightCycle), not on whether the palette happens to be in the night-palette
            // table — so dungeons/shops never darken, and every outdoor map does.
            if (state == null || Night == null || !IsDayNightMap())
                return 0;

            var daysElapsed = (float)state.Time.TimeOfDay.TotalDays; // how far through the day we are (0..1)
            return MathF.Cos(daysElapsed * MathF.PI * 2) * 0.5f + 0.5f;
        }
    }

    bool IsDayNightMap() =>
        TryResolve<IMapManager>()?.Current?.MapData is { } map
        && map.LightingMode == UAlbion.Formats.Assets.Maps.MapLightingMode.DayNightCycle;

    public PaletteManager()
    {
        On<LoadPaletteEvent>(e => SetPalette(e.PaletteId));
        On<SlowClockEvent>(_ => Frame++);
        On<LoadRawPaletteEvent>(e =>
        {
            Day = new AlbionPalette(0, "Raw", e.Entries);
            Night = null;
        });
    }

    protected override void Subscribed()
    {
        base.Subscribed();
        if (Day == null)
            SetPalette(Base.Palette.Common);
    }

    void SetPalette(PaletteId paletteId)
    {
        var day = Assets.LoadPalette(paletteId);
        if (day == null)
        {
            Error($"Palette ID {paletteId} could not be loaded!");
            return;
        }

        Day = day;
        // Specific day→night mapping if one exists; otherwise, for an outdoor day/night map,
        // fall back to the generic OutdoorsNight so later regions (whose exact night palette
        // isn't in the table) still darken instead of staying bright all night. Indoor/dungeon
        // maps get no night palette (Blend also gates them out via IsDayNightMap).
        if (NightPalettes.TryGetValue(paletteId, out var nightPaletteId))
            Night = Assets.LoadPalette(nightPaletteId);
        else if (IsDayNightMap())
            Night = Assets.LoadPalette(Base.Palette.OutdoorsNight);
        else
            Night = null;

        if (Night != null)
        {
            ApiUtil.Assert(Day.AnimatedEntries.SequenceEqual(Night.AnimatedEntries),
                "Expected day and night palettes to have identical animated entries!" +
                $" Day palette {Day.Id} had entries [ {string.Join(", ", Day.AnimatedEntries)} ] and " +
                $"Night palette {Night.Id} had entries [ {string.Join(", ", Night.AnimatedEntries)} ]");
        }
    }
}
