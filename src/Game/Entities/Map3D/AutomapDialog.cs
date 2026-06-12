using System;
using System.Numerics;
using UAlbion.Api.Eventing;
using UAlbion.Api.Visual;
using UAlbion.Config;
using UAlbion.Core.Visual;
using UAlbion.Formats;
using UAlbion.Formats.Assets;
using UAlbion.Formats.Assets.Maps;
using UAlbion.Formats.Ids;
using UAlbion.Game.Entities.Map2D;
using UAlbion.Game.State;

namespace UAlbion.Game.Entities.Map3D;

[Event("show_automap", "Toggle the 3D dungeon automap overlay")]
public class ShowAutomapEvent : Event { }

/// <summary>
/// The 3D dungeon automap (Phase 5.3): a top-down overlay composed at runtime from the
/// AutomapTiles graphics. Wall tiles use the map's AutomapGraphics table (wall index →
/// automap tile graphic, the original's 1:1 mechanism from MAPDATA); discovered floor
/// tiles draw as open space; the party position is marked. Tiles the party hasn't been
/// near remain blank — discovery is tracked by <see cref="MarkDiscovered"/> (called from
/// DungeonMap on tile entry) and persisted through SavedGame.Automaps.
/// </summary>
public class AutomapDialog : GameComponent
{
    const int TilePx = 8;          // AutomapTiles graphics are 8×8
    const int DiscoveryRadius = 2; // PLACEHOLDER: original discovery radius not RE'd yet

    readonly LogicalMap3D _map;
    readonly MapData3D _mapData;
    readonly Automap _discovered;
    Sprite _sprite;
    SimpleTexture<byte> _texture;
    bool _visible;

    public AutomapDialog(LogicalMap3D map, MapData3D mapData)
    {
        _map = map ?? throw new ArgumentNullException(nameof(map));
        _mapData = mapData ?? throw new ArgumentNullException(nameof(mapData));
        _discovered = new Automap(_map.Width, _map.Height);
        On<ShowAutomapEvent>(_ => Toggle());
    }

    protected override void Subscribed()
    {
        // Restore persisted discovery state for this map, if any.
        var state = TryResolve<IGameState>();
        var automapId = new AutomapId(_mapData.Id.Id);
        if (state != null && state.Automaps.TryGetValue(automapId, out var bytes) && bytes != null)
        {
            _discovered.AsBytes = bytes;
            Info($"[Automap] restored {bytes.Length} bytes for {_mapData.Id} (map {_map.Width}x{_map.Height} = {_map.Width * _map.Height} tiles, bits {_discovered.Length})");
        }
    }

    protected override void Unsubscribed()
    {
        Persist();
        Hide();
    }

    void Persist()
    {
        var state = TryResolve<IGameState>();
        if (state != null)
            state.Automaps[new AutomapId(_mapData.Id.Id)] = _discovered.AsBytes;
    }

    public void MarkDiscovered(int centreX, int centreY)
    {
        for (int y = centreY - DiscoveryRadius; y <= centreY + DiscoveryRadius; y++)
        {
            for (int x = centreX - DiscoveryRadius; x <= centreX + DiscoveryRadius; x++)
            {
                if (x < 0 || y < 0 || x >= _map.Width || y >= _map.Height)
                    continue;
                _discovered.Set(x, y, true);
            }
        }
    }

    void Toggle()
    {
        if (_visible) Hide();
        else Show();
    }

    void Hide()
    {
        if (_sprite != null)
        {
            RemoveChild(_sprite);
            _sprite = null;
        }
        _visible = false;
    }

    void Show()
    {
        var tiles = Assets.LoadTexture(new SpriteId(AssetType.AutomapGfx, (int)Base.AutomapTiles.Set1)) as IReadOnlyTexture<byte>;
        if (tiles == null)
        {
            Warn("[Automap] Could not load AutomapTiles.Set1");
            return;
        }

        int w = _map.Width * TilePx;
        int h = _map.Height * TilePx;
        _texture = new SimpleTexture<byte>(
            new SpriteId(AssetType.AutomapGfx, 999), $"Automap:{_mapData.Id}",
            w, h,
            [new Region(Vector2.Zero, new Vector2(w, h), new Vector2(w, h), 0)]);

        var buffer = _texture.GetMutableLayerBuffer(0);
        var party = TryResolve<IParty>();
        var leaderPos = party?.Leader?.GetPosition() ?? Vector3.Zero;
        int partyX = (int)MathF.Floor(leaderPos.X);
        int partyY = (int)MathF.Floor(leaderPos.Z);

        for (int y = 0; y < _map.Height; y++)
        {
            for (int x = 0; x < _map.Width; x++)
            {
                if (!_discovered[x, y] && !(x == partyX && y == partyY))
                    continue;

                int region = PickTileRegion(x, y, tiles.Regions.Count);
                if (x == partyX && y == partyY)
                    region = PartyMarkerRegion(tiles.Regions.Count);

                if (region < 0)
                    continue;

                BlitRegion(tiles, region, buffer, x * TilePx, y * TilePx, w);
            }
        }

        _sprite = AttachChild(new Sprite(SpriteId.None,
            DrawLayer.Interface,
            SpriteKeyFlags.NoTransform,
            SpriteFlags.LeftAligned | SpriteFlags.FlipVertical,
            _ => _texture));

        // Keep the map's aspect ratio inside an NDC box of ±0.75.
        float aspect = w / (float)h;
        var size = aspect >= 1
            ? new Vector2(1.5f, 1.5f / aspect)
            : new Vector2(1.5f * aspect, 1.5f);
        _sprite.Position = new Vector3(-size.X / 2, -size.Y / 2, 0);
        _sprite.Size = size;
        _visible = true;
        Info($"[Automap] shown for {_mapData.Id} ({_map.Width}x{_map.Height}, party at {partyX},{partyY})");
    }

    /// <summary>
    /// Wall tiles map through MapData3D.AutomapGraphics (wall index → automap tile graphic,
    /// the on-disk table the original engine uses). Discovered floor uses tile 1; oob/none
    /// stays blank.
    /// </summary>
    int PickTileRegion(int x, int y, int regionCount)
    {
        var (wallIndex, _) = _map.GetWall(x, y);
        if (wallIndex != 0)
        {
            int idx = wallIndex - 1 < _mapData.AutomapGraphics.Length
                ? _mapData.AutomapGraphics[wallIndex - 1]
                : 0;
            if (idx <= 0 || idx >= regionCount)
                idx = 8; // PLACEHOLDER: generic solid wall tile when the table has no entry
            return idx;
        }

        var (floorIndex, _) = _map.GetFloor(x, y);
        return floorIndex != 0 ? 1 : -1; // PLACEHOLDER: tile 1 = open floor
    }

    static int PartyMarkerRegion(int regionCount)
        => Math.Min(regionCount - 1, 213); // PLACEHOLDER: directional party figures live near the set's end

    static void BlitRegion(IReadOnlyTexture<byte> tiles, int region, ImageBuffer<byte> dest, int dx, int dy, int destWidth)
    {
        var src = tiles.GetRegionBuffer(region);
        int wpx = Math.Min(src.Width, TilePx);
        int hpx = Math.Min(src.Height, TilePx);
        for (int row = 0; row < hpx; row++)
        {
            var from = src.Buffer.Slice(row * src.Stride, wpx);
            var to = dest.Buffer.Slice((dy + row) * destWidth + dx, wpx);
            from.CopyTo(to);
        }
    }
}
