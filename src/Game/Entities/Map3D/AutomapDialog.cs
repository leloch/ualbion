using System;
using System.Collections.Generic;
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

[Event("reveal_automap", "Reveal the entire automap for the current map (Map View spell)")]
public class RevealAutomapEvent : Event { }

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
    const int TilePx = 8;         // AutomapTiles graphics are 8×8
    const int DiscoveryDepth = 10; // AUTOMAP_Discover(10): both original call sites pass 10
                                   // (map entry fcn.0001183b, movement fcn.0001e52d; the
                                   // function clamps 3..10 but only ever receives 10)

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
        On<RevealAutomapEvent>(_ =>
        {
            for (int y = 0; y < _map.Height; y++)
                for (int x = 0; x < _map.Width; x++)
                    _discovered.Set(x, y, true);
            Persist();
            if (_visible) { Hide(); Show(); } // Refresh if currently open
        });
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

    /// <summary>
    /// Original discovery rule (MAIN.EXE fcn.0005c40e — see _RE_NOTES.md "automap.c"):
    /// build a facing-oriented candidate region around an origin one tile behind the
    /// party (cardinal facing → widening 90° wedge, row i at depth i is 2i+1 wide;
    /// diagonal facing → the full 11×11 quadrant), then flood-fill from the party tile
    /// through sight-open cells. Cardinal steps propagate freely; diagonal steps are
    /// blocked when either adjacent cardinal neighbour is a sight-blocking wall
    /// (corner occlusion). Reached floors and every wall the flood touches become
    /// discovered.
    /// </summary>
    public void MarkDiscovered(int partyX, int partyY)
    {
        var (fx, fy) = FacingStep();
        int ox = partyX - fx, oy = partyY - fy; // origin one tile behind the party

        var candidates = new HashSet<(int X, int Y)>();
        if (fx == 0 || fy == 0)
        {
            for (int i = 0; i <= DiscoveryDepth; i++)
                for (int j = -i; j <= i; j++)
                    candidates.Add((ox + fx * i + (fx == 0 ? j : 0),
                                    oy + fy * i + (fy == 0 ? j : 0)));
        }
        else
        {
            for (int a = 0; a <= DiscoveryDepth; a++)
                for (int b = 0; b <= DiscoveryDepth; b++)
                    candidates.Add((ox + fx * a, oy + fy * b));
        }
        candidates.Add((partyX, partyY));

        var reached = new HashSet<(int X, int Y)> { (partyX, partyY) };
        var queue = new Queue<(int X, int Y)>();
        queue.Enqueue((partyX, partyY));
        Discover(partyX, partyY);

        while (queue.Count > 0)
        {
            var (cx, cy) = queue.Dequeue();
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = cx + dx, ny = cy + dy;
                    if (nx < 0 || ny < 0 || nx >= _map.Width || ny >= _map.Height) continue;
                    if (reached.Contains((nx, ny)) || !candidates.Contains((nx, ny))) continue;
                    if (dx != 0 && dy != 0 && (BlocksSight(cx + dx, cy) || BlocksSight(cx, cy + dy)))
                        continue; // corner occlusion

                    if (BlocksSight(nx, ny))
                    {
                        Discover(nx, ny); // walls touched by the flood become visible but don't propagate
                        continue;
                    }

                    reached.Add((nx, ny));
                    Discover(nx, ny);
                    queue.Enqueue((nx, ny));
                }
            }
        }
    }

    void Discover(int x, int y)
    {
        if (x >= 0 && y >= 0 && x < _map.Width && y < _map.Height)
            _discovered.Set(x, y, true);
    }

    /// <summary>
    /// A wall blocks sight when its flags bit 0x04 is set (MAP_BlocksSight,
    /// fcn.00012f4f) — the bit the remake names WriteOverlay; on the automap it means
    /// "opaque". Out-of-map blocks. Tiles without walls are sight-open.
    /// </summary>
    bool BlocksSight(int x, int y)
    {
        if (x < 0 || y < 0 || x >= _map.Width || y >= _map.Height)
            return true;
        var (wallIndex, wall) = _map.GetWall(x, y);
        if (wallIndex == 0 || wall == null)
            return false;
        return (wall.Properties & UAlbion.Formats.Assets.Labyrinth.Wall.WallFlags.WriteOverlay) != 0;
    }

    /// <summary>
    /// Facing octant as a unit tile step (tile Y = world Z; the camera looks -Z at
    /// yaw 0, so octant 0 = (0,-1)). Matches the original's yaw→octant compass at
    /// 0x134bf4. Defaults to north when no camera is available.
    /// </summary>
    (int X, int Y) FacingStep()
    {
        var camera = TryResolve<ICameraProvider>()?.Camera;
        if (camera == null)
            return (0, -1);

        var look = camera.LookDirection;
        double angle = Math.Atan2(look.X, -look.Z); // 0 = -Z, clockwise positive
        int octant = (int)Math.Round(angle / (Math.PI / 4), MidpointRounding.AwayFromZero);
        octant = (octant % 8 + 8) % 8;
        return octant switch
        {
            0 => (0, -1),
            1 => (1, -1),
            2 => (1, 0),
            3 => (1, 1),
            4 => (0, 1),
            5 => (-1, 1),
            6 => (-1, 0),
            _ => (-1, -1),
        };
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
