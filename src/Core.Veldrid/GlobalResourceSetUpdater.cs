using UAlbion.Api.Eventing;
using UAlbion.Core.Events;
using UAlbion.Core.Veldrid.Textures;
using UAlbion.Core.Visual;

namespace UAlbion.Core.Veldrid;

public class GlobalResourceSetUpdater : Component
{
    readonly GlobalResourceSetProvider _globalSet;

    public GlobalResourceSetUpdater(GlobalResourceSetProvider globalSet)
    {
        _globalSet = globalSet;
        On<PrepareFrameEvent>(_ => UpdatePerFrameResources());
    }

    void UpdatePerFrameResources()
    {
        var clock = TryResolve<IClock>();
        var textureSource = TryResolve<ITextureSource>();
        if (textureSource == null)
            return;

        // Startup race: the first frames can render before the palette manager attaches
        // (especially when -c "load_game N" loads a map during boot). GlobalSet.Build
        // dereferences its texture holders, so seed dummies rather than leaving nulls.
        var paletteManager = TryResolve<IPaletteManager>();
        if (paletteManager?.Day == null)
        {
            _globalSet.DayPalette ??= textureSource.GetDummySimpleTexture();
            _globalSet.NightPalette ??= textureSource.GetDummySimpleTexture();
            return;
        }

        var engineFlags = ReadVar(V.Core.User.EngineFlags);

        var dayPalette = textureSource.GetSimpleTexture(paletteManager.Day.Texture);
        var nightTexture = paletteManager.Night?.Texture ?? paletteManager.Day.Texture;
        var nightPalette = textureSource.GetSimpleTexture(nightTexture);

        _globalSet.DayPalette = dayPalette;
        _globalSet.NightPalette = nightPalette;
        _globalSet.GlobalInfo = new GlobalInfo
        {
            Time = clock?.ElapsedTime ?? 0,
            EngineFlags = engineFlags,
            PaletteBlend = paletteManager.Blend,
            PaletteFrame = paletteManager.Frame,
        };
    }
}