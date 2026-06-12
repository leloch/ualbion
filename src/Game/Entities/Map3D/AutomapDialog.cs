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

        _floorMinis.Clear();
        for (int y = 0; y < _map.Height; y++)
        {
            for (int x = 0; x < _map.Width; x++)
            {
                if (!_discovered[x, y] && !(x == partyX && y == partyY))
                    continue;

                // Base layer (original tile pass fcn.0005e12c): floor mini, then object
                // glyph, then wall glyph — later layers overwrite earlier ones.
                var mini = FloorMini(x, y);
                if (mini != null)
                    BlitMini(mini, buffer, x * TilePx, y * TilePx, w);

                int region = PickTileRegion(x, y, tiles.Regions.Count);
                if (x == partyX && y == partyY)
                    region = PartyMarkerRegion(tiles.Regions.Count);

                if (region < 0)
                    continue;

                BlitRegion(tiles, region, buffer, x * TilePx, y * TilePx, w);
            }
        }

        // Goto-point markers (glyph 18) for visited markers — fcn.0005e7e5 draws them
        // when switch(7, MarkerId) is set (stepping on the tile sets it).
        var state = TryResolve<IGameState>();
        if (state != null && _mapData.Automap != null && GotoGlyph < tiles.Regions.Count)
        {
            foreach (var marker in _mapData.Automap)
            {
                if (marker == null || !state.IsAutomapMarkerFound(marker.MarkerId))
                    continue;
                if (marker.X >= _map.Width || marker.Y >= _map.Height)
                    continue;
                BlitRegion(tiles, GotoGlyph, buffer, marker.X * TilePx, marker.Y * TilePx, w);
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

    const int WallMaskGlyphBase = 560; // AUTOGFX 0x230 + connection mask (fcn.0005e8a1)
    const int GotoGlyph = 18;          // goto-point marker glyph (fcn.0005e7e5)

    readonly Dictionary<byte, byte[]> _floorMinis = [];

    /// <summary>
    /// The original glyph rules (tile pass fcn.0005e12c, see _RE_NOTES.md "automap.c"):
    /// a wall's <c>AutoGfxType</c> selects the glyph — type 1 = connectable wall
    /// (AUTOGFX 560 + connection mask, bits N/E/S/W set when that neighbour is
    /// discovered AND sight-blocking), types 2..19 = that AUTOGFX marker glyph, 0 =
    /// nothing; objects draw their group's AutoGraphicsId; floors are handled
    /// separately as texture minis.
    /// </summary>
    int PickTileRegion(int x, int y, int regionCount)
    {
        var (wallIndex, wall) = _map.GetWall(x, y);
        if (wallIndex != 0 && wall != null)
        {
            int glyph = wall.AutoGfxType switch
            {
                1 => WallMaskGlyphBase + WallConnectionMask(x, y),
                >= 2 and <= 19 => wall.AutoGfxType,
                _ => -1
            };
            if (glyph >= regionCount)
                glyph = Math.Min(regionCount - 1, 8); // set lacks the frame — degrade gracefully
            return glyph;
        }

        var group = _map.GetObject(x, y);
        if (group != null && group.AutoGraphicsId > 0 && group.AutoGraphicsId < regionCount)
            return group.AutoGraphicsId;

        return -1; // floor mini (blitted separately) or blank
    }

    /// <summary>
    /// Wall connection mask (fcn.0005e8a1): bit0=N, bit1=E, bit2=S, bit3=W — set when
    /// that cardinal neighbour is discovered and sight-blocking, so adjoining wall
    /// pieces join up on the map.
    /// </summary>
    int WallConnectionMask(int x, int y)
    {
        int mask = 0;
        if (Connectable(x, y - 1)) mask |= 1;
        if (Connectable(x + 1, y)) mask |= 2;
        if (Connectable(x, y + 1)) mask |= 4;
        if (Connectable(x - 1, y)) mask |= 8;
        return mask;

        bool Connectable(int nx, int ny) =>
            nx >= 0 && ny >= 0 && nx < _map.Width && ny < _map.Height
            && _discovered[nx, ny]
            && BlocksSight(nx, ny);
    }

    /// <summary>
    /// The original draws discovered floors as an 8×8 downscaled copy of the tile's
    /// actual floor texture (fcn.0005dca5/fcn.0005dfc8), not an AUTOGFX glyph.
    /// </summary>
    byte[] FloorMini(int x, int y)
    {
        var (floorIndex, fc) = _map.GetFloor(x, y);
        if (floorIndex == 0 || fc == null)
            return null;
        if (_floorMinis.TryGetValue(floorIndex, out var cached))
            return cached;

        byte[] mini = null;
        if (Assets.LoadTexture(fc.SpriteId) is IReadOnlyTexture<byte> floorTex && floorTex.Regions.Count > 0)
        {
            var src = floorTex.GetRegionBuffer(0);
            if (src.Width > 0 && src.Height > 0)
            {
                mini = new byte[TilePx * TilePx];
                for (int j = 0; j < TilePx; j++)
                    for (int i = 0; i < TilePx; i++)
                        mini[j * TilePx + i] = src.Buffer[(j * src.Height / TilePx) * src.Stride + i * src.Width / TilePx];
            }
        }

        _floorMinis[floorIndex] = mini; // cache nulls too — don't retry failures per tile
        return mini;
    }

    static void BlitMini(byte[] mini, ImageBuffer<byte> dest, int dx, int dy, int destWidth)
    {
        for (int row = 0; row < TilePx; row++)
        {
            var from = new ReadOnlySpan<byte>(mini, row * TilePx, TilePx);
            var to = dest.Buffer.Slice((dy + row) * destWidth + dx, TilePx);
            from.CopyTo(to);
        }
    }

    // The original doesn't blit a party glyph into the compose buffer (the UI cursor
    // marks the position); the remake draws one as a usability affordance.
    static int PartyMarkerRegion(int regionCount)
        => Math.Min(regionCount - 1, 213);

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
