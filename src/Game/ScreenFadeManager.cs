using System;
using System.Numerics;
using UAlbion.Api.Eventing;
using UAlbion.Api.Visual;
using UAlbion.Core.Events;
using UAlbion.Core.Visual;
using UAlbion.Formats.Assets;
using UAlbion.Formats.MapEvents;
using UAlbion.Formats.ScriptEvents;
using UAlbion.Game.Events;
using UAlbion.Game.Gui;

namespace UAlbion.Game;

/// <summary>
/// Script-driven full-screen colour blends — the scene-transition primitives that the
/// original wraps cutscenes and the finale in:
///   fade_to_black / fade_from_black — ramp a black overlay in/out (blocks the chain).
///   fade_to_white / fade_from_white — same, but a white overlay (e.g. flashbacks / the finale).
///   fill_screen &lt;color&gt; / fill_screen_0 — instant flood-fill before/after a video.
/// Without these the engine hard-cuts and flashes the wrong frame around every cutscene.
///
/// A single full-screen quad is drawn from the common-colour border texture (same source as
/// <see cref="UAlbion.Game.Gui.Controls.UiRectangle"/>) on the InterfaceOverlay layer, above
/// the GUI, with per-frame animated opacity (top byte of <see cref="SpriteFlags"/>). The
/// overlay is removed once a fade-out completes and on map unload.
/// </summary>
public class ScreenFadeManager : Component
{
    const float FadeDurationSeconds = 0.5f; // original fades are quick

    BatchLease<SpriteKey, SpriteInfo> _lease;
    CommonColor _color = CommonColor.Black1;
    float _opacity;
    float _targetOpacity;
    bool _animating;
    AlbionTaskCore _pending;

    public ScreenFadeManager()
    {
        OnAsync<FadeToBlackEvent>(_ => StartFade(CommonColor.Black1, 1.0f));
        OnAsync<FadeFromBlackEvent>(_ => StartFade(CommonColor.Black1, 0.0f));
        OnAsync<FadeToWhiteEvent>(_ => StartFade(CommonColor.White, 1.0f));
        OnAsync<FadeFromWhiteEvent>(_ => StartFade(CommonColor.White, 0.0f));
        On<FillScreenEvent>(e => Fill(MapColor(e.Color)));
        On<FillScreen0Event>(_ => Fill(CommonColor.Black1));
        // wipe (map opcode 0x17, RE docs/re/RE_MISC7.md §C): value 0 = instant redraw (no-op here),
        // 1 = blank the viewport (hold black), 2-5 = a ~10-step palette fade. We approximate
        // 2-5 with a fade-to-then-from-black, and 1 with a held black fill.
        OnAsync<WipeEvent>(OnWipe);
        On<EngineUpdateEvent>(e => Update(e.DeltaSeconds));
        On<UnloadMapEvent>(_ => Clear());
    }

    async AlbionTask OnWipe(WipeEvent e)
    {
        switch (e.Value)
        {
            case 0: return; // instant redraw — nothing to animate
            case 1: Fill(CommonColor.Black1); return; // blank the viewport (held)
            default: // 2-5: palette fade out then back in
                await StartFade(CommonColor.Black1, 1.0f);
                await StartFade(CommonColor.Black1, 0.0f);
                return;
        }
    }

    static CommonColor MapColor(int paletteIndex)
        // CommonColor's underlying type is byte, so Enum.IsDefined needs a byte-typed value.
        => paletteIndex is >= 0 and <= 255 && Enum.IsDefined(typeof(CommonColor), (byte)paletteIndex)
            ? (CommonColor)paletteIndex
            : CommonColor.Black1;

    AlbionTask StartFade(CommonColor color, float target)
    {
        // Complete any in-flight fade so its awaiter resumes rather than hanging.
        CompletePending();

        _color = color;
        _targetOpacity = target;
        _animating = true;

        // Already at the target → nothing to animate, resolve immediately.
        if (Math.Abs(_opacity - _targetOpacity) < 1e-3f)
        {
            _animating = false;
            if (_targetOpacity <= 0f)
                Clear();
            return AlbionTask.CompletedTask;
        }

        _pending = new AlbionTaskCore("ScreenFadeManager.Fade");
        return _pending.UntypedTask;
    }

    void Fill(CommonColor color)
    {
        CompletePending();
        _color = color;
        _opacity = 1.0f;
        _targetOpacity = 1.0f;
        _animating = false;
        UpdateInstance();
    }

    void Update(float deltaSeconds)
    {
        if (!_animating)
            return;

        float step = deltaSeconds <= 0 ? 1.0f : deltaSeconds / FadeDurationSeconds;
        if (_opacity < _targetOpacity)
            _opacity = Math.Min(_targetOpacity, _opacity + step);
        else
            _opacity = Math.Max(_targetOpacity, _opacity - step);

        if (Math.Abs(_opacity - _targetOpacity) < 1e-3f)
        {
            _opacity = _targetOpacity;
            _animating = false;
            if (_targetOpacity <= 0f)
                Clear();
            else
                UpdateInstance();
            CompletePending();
        }
        else
        {
            UpdateInstance();
        }
    }

    void UpdateInstance()
    {
        if (_opacity <= 0f)
        {
            ReleaseLease();
            return;
        }

        var sm = TryResolve<IBatchManager<SpriteKey, SpriteInfo>>();
        var commonColors = TryResolve<ICommonColors>();
        if (sm == null || commonColors == null)
            return;

        var key = new SpriteKey(commonColors.BorderTexture, SpriteSampler.Point, DrawLayer.InterfaceOverlay,
            SpriteKeyFlags.NoDepthTest | SpriteKeyFlags.NoTransform);

        if (_lease == null || key != _lease.Key)
        {
            _lease?.Dispose();
            _lease = sm.Borrow(key, 1, this);
        }

        var flags = SpriteFlags.TopLeft.SetOpacity(_opacity);
        bool lockWasTaken = false;
        var instances = _lease.Lock(ref lockWasTaken);
        try
        {
            // NoTransform → NDC coords directly: full screen from top-left (-1,1) sized (2,-2).
            instances[0] = new SpriteInfo(
                flags,
                new Vector3(-1.0f, 1.0f, 0),
                new Vector2(2.0f, -2.0f),
                commonColors.GetRegion(_color));
        }
        finally { _lease.Unlock(lockWasTaken); }
    }

    void CompletePending()
    {
        var pending = _pending;
        _pending = null;
        pending?.Complete();
    }

    void ReleaseLease()
    {
        _lease?.Dispose();
        _lease = null;
    }

    void Clear()
    {
        _opacity = 0f;
        _targetOpacity = 0f;
        _animating = false;
        ReleaseLease();
        CompletePending();
    }

    protected override void Unsubscribed() => ReleaseLease();
}
