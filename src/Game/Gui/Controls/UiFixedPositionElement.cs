using System;
using System.Numerics;
using UAlbion.Api.Visual;
using UAlbion.Core;
using UAlbion.Core.Events;
using UAlbion.Core.Visual;
using UAlbion.Formats.Ids;

namespace UAlbion.Game.Gui.Controls;

public class UiFixedPositionElement : UiElement
{
    readonly SpriteId _id;
    readonly Rectangle _extents;
    BatchLease<SpriteKey, SpriteInfo> _sprite;

    public UiFixedPositionElement(SpriteId id, Rectangle extents)
    {
        On<BackendChangedEvent>(_ => Rebuild());
        On<GameWindowResizedEvent>(_ => Rebuild());
        _id = id;
        _extents = extents;
    }

    public override string ToString() => $"{_id} @ {_extents}";
    public override Vector2 GetSize() => new(_extents.Width, _extents.Height);

    protected override void Subscribed()
    {
        if (_sprite == null)
        {
            var sm = Resolve<IBatchManager<SpriteKey, SpriteInfo>>();
            var texture = _id.IsNone ? null : Assets.LoadTexture(_id);
            if (texture == null)
            {
                // Missing/None texture: render nothing rather than crash — e.g. combat on
                // a map with no CombatBackgroundId set.
                Warn($"UiFixedPositionElement: no texture for {_id}");
                return;
            }
            Info($"UiFixedPositionElement: {_id} loaded {texture.Width}x{texture.Height} ({texture.GetType().Name})");
            var key = new SpriteKey(texture, SpriteSampler.Point, DrawLayer.Interface, SpriteKeyFlags.NoTransform | SpriteKeyFlags.NoDepthTest);
            _sprite = sm.Borrow(key, 1, this);
        }

        Rebuild();
    }

    protected override void Unsubscribed()
    {
        _sprite?.Dispose();
        _sprite = null;
    }

    void Rebuild()
    {
        if (_sprite == null)
            return;

        var window = Resolve<IGameWindow>();
        var position = new Vector3(window.UiToNorm(_extents.X, _extents.Y), 0);
        var size = window.UiToNormRelative(_extents.Width, _extents.Height);

        bool lockWasTaken = false;
        var instances = _sprite.Lock(ref lockWasTaken);
        try
        {
            instances[0] = new SpriteInfo(SpriteFlags.TopLeft, position, size, _sprite.Key.Texture.Regions[0]);
        }
        finally { _sprite.Unlock(lockWasTaken); }
    }

    public override int Selection(Rectangle extents, int order, SelectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (extents.Contains((int)context.UiPosition.X, (int)context.UiPosition.Y))
            context.AddHit(order, this);
        return order;
    }
}
