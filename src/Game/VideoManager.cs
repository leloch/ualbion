using System.Numerics;
using UAlbion.Api.Eventing;
using UAlbion.Api.Visual;
using UAlbion.Config;
using UAlbion.Core.Visual;
using UAlbion.Formats.Ids;
using UAlbion.Formats.MapEvents;
using UAlbion.Formats.ScriptEvents;
using UAlbion.Game.Events;

namespace UAlbion.Game;

/// <summary>
/// Plays FLIC videos and the script-driven overlay graphics:
///   play_anim   — fullscreen video, blocks the chain until done (cutscenes).
///   show_pic / show_picture — fullscreen still picture under subsequent overlays
///                 (e.g. Picture.ScoutShip, the intro cockpit).
///   start_anim &lt;flic&gt; &lt;x&gt; &lt;y&gt; &lt;layer&gt; — positioned looping animation at UI pixels
///                 (the cockpit window star/planet anims). Layer semantics undecoded —
///                 PLACEHOLDER: drawn above the picture in start order.
///   play &lt;n&gt;    — waits until the current positioned anim completes n full cycles;
///                 the script language's pacing primitive.
///   stop_anim   — removes all overlays. Overlays also clear on map unload.
/// </summary>
public class VideoManager : Component
{
    Video _currentAnim;
    Sprite _picture;

    public VideoManager()
    {
        OnAsync<PlayAnimationEvent>(Play);
        On<StartAnimEvent>(Start);
        On<StopAnimEvent>(_ => Clear());
        OnAsync<PlayEvent>(PlayCycles);
        On<ShowPicEvent>(e => ShowPicture(e.PicId, e.X ?? 0, e.Y ?? 0));
        On<ShowPictureEvent>(e => ShowPicture(e.PictureId, e.X, e.Y));
        On<UnloadMapEvent>(_ => Clear());
    }

    AlbionTask Play(PlayAnimationEvent e)
    {
        var source = new AlbionTaskCore("VideoManager.Play");
        AttachChild(new Video(e.VideoId, false)).OnComplete(source.Complete);
        return source.UntypedTask;
    }

    void Start(StartAnimEvent e)
    {
        // start_anim <flicId> <x> <y> <layer>: positioned looping overlay. The flic id
        // indexes the same FLICS0.XLD as the cutscene videos (only ids 1..19 are named
        // in Base.Video; the intro cockpit anims are unnamed entries).
        _currentAnim?.Remove();
        _currentAnim = AttachChild(new Video(new VideoId(AssetType.Video, e.Unk1), true, new Vector2(e.Unk2, e.Unk3)));
    }

    AlbionTask PlayCycles(PlayEvent e)
    {
        // play <n>: let the current overlay anim run n full cycles before the script
        // continues — the original's pacing primitive for scripted sequences.
        if (_currentAnim == null || e.Unknown <= 0)
            return AlbionTask.CompletedTask;

        var source = new AlbionTaskCore("VideoManager.PlayCycles");
        int remaining = e.Unknown;
        var anim = _currentAnim;

        void OnCycle()
        {
            if (--remaining > 0)
                return;
            anim.CycleCompleted -= OnCycle;
            source.Complete();
        }

        anim.CycleCompleted += OnCycle;
        return source.UntypedTask;
    }

    void ShowPicture(PictureId pictureId, int x, int y)
    {
        if (_picture != null)
        {
            RemoveChild(_picture);
            _picture = null;
        }

        // Still picture behind any subsequent overlay anims (NoDepthTest sprite on the
        // UI layer — same mechanism as the combat backdrop). (0,0) draws fullscreen
        // like the intro; a non-zero origin positions the picture at UI pixels
        // (360x240 space) at its native size, matching the original's blit-at-x/y.
        Vector3 position;
        Vector2 size;
        if (x == 0 && y == 0)
        {
            position = new Vector3(-1.0f, 1.0f, 0);
            size = new Vector2(2.0f, -2.0f);
        }
        else
        {
            var texture = TryResolve<UAlbion.Formats.IAssetManager>()?.LoadTexture((SpriteId)(AssetId)pictureId);
            float w = texture?.Width ?? 360;
            float h = texture?.Height ?? 240;
            position = new Vector3(-1.0f + 2.0f * x / 360.0f, 1.0f - 2.0f * y / 240.0f, 0);
            size = new Vector2(2.0f * w / 360.0f, -2.0f * h / 240.0f);
        }

        _picture = AttachChild(new Sprite(
            (SpriteId)(AssetId)pictureId,
            DrawLayer.Interface,
            SpriteKeyFlags.NoTransform | SpriteKeyFlags.NoDepthTest,
            SpriteFlags.LeftAligned)
        {
            Position = position,
            Size = size
        });
    }

    void Clear()
    {
        _currentAnim = null;
        _picture = null;
        RemoveAllChildren();
    }
}
