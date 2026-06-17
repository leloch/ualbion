using System;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using UAlbion.Api.Eventing;
using UAlbion.Config;
using UAlbion.Core.Events;
using UAlbion.Core.Veldrid;
using UAlbion.Game.Events;
using UAlbion.Game.Scenes;
using UAlbion.Game.State;

namespace UAlbion.Game.Veldrid;

/// <summary>
/// Captures a small thumbnail of the live game view and writes it next to the save file when the
/// game is saved (SAVE.NNN.png), so the load/save menu can show a screenshot per slot (#74).
/// Saving is done from the menu (which shows the title vista, not the game), so we can't capture at
/// save time - instead the latest WORLD frame is captured periodically and cached, and that cached
/// frame is what gets written on save.
/// </summary>
public class SaveThumbnailManager : Component
{
    public const int ThumbWidth = 192;
    public const int ThumbHeight = 120;
    const float CaptureIntervalSeconds = 2.5f;

    readonly object _lock = new();
    Image<Bgra32> _latest; // cached, already downscaled to thumbnail size
    float _sinceCapture;
    bool _capturePending;

    public SaveThumbnailManager()
    {
        On<EngineUpdateEvent>(e =>
        {
            _sinceCapture += e.DeltaSeconds;
            if (_sinceCapture >= CaptureIntervalSeconds)
                _capturePending = true;
        });
        On<PreSwapBuffersEvent>(_ => { if (_capturePending) Capture(); });
        On<SaveGameEvent>(OnSave);
    }

    void Capture()
    {
        _capturePending = false;
        _sinceCapture = 0;

        // Only snapshot while a world scene is on screen, so the thumbnail shows the game and not a menu.
        var sceneId = TryResolve<ISceneManager>()?.ActiveSceneId;
        if (sceneId is not (SceneId.World2D or SceneId.World3D))
            return;

        var engine = TryResolve<IVeldridEngine>();
        if (engine == null)
            return;

        Image<Bgra32> full = null;
        try
        {
            full = engine.CaptureSwapchain();
            if (full == null)
                return;

            var thumb = full.Clone(c => c.Resize(ThumbWidth, ThumbHeight));
            lock (_lock)
            {
                _latest?.Dispose();
                _latest = thumb;
            }
        }
        catch (Exception ex) { Warn($"[SaveThumbnail] capture failed: {ex.Message}"); }
        finally { full?.Dispose(); }
    }

    void OnSave(SaveGameEvent e)
    {
        Image<Bgra32> snapshot;
        lock (_lock)
            snapshot = _latest?.Clone();

        if (snapshot == null)
            return;

        try
        {
            var path = Resolve<IPathResolver>().ResolvePath($"$(SAVES)/SAVE.{e.Id:D3}.png");
            using var stream = Resolve<UAlbion.Api.IFileSystem>().OpenWriteTruncate(path);
            snapshot.SaveAsPng(stream);
        }
        catch (Exception ex) { Warn($"[SaveThumbnail] write failed: {ex.Message}"); }
        finally { snapshot.Dispose(); }
    }

    protected override void Unsubscribed()
    {
        lock (_lock)
        {
            _latest?.Dispose();
            _latest = null;
        }
    }
}
