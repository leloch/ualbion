using System;
using System.Collections.Generic;
using UAlbion.Api;
using UAlbion.Api.Visual;
using UAlbion.Config;
using UAlbion.Core.Events;
using UAlbion.Core.Visual;
using UAlbion.Formats.Assets.Labyrinth;
using UAlbion.Formats.MapEvents;
using UAlbion.Formats.ScriptEvents;
using UAlbion.Game.Entities.Map2D;
using UAlbion.Game.Events;
using UAlbion.Game.State;

namespace UAlbion.Game.Entities.Map3D;

public class MapRenderable3D : GameComponent
{
    readonly LogicalMap3D _logicalMap;
    readonly LabyrinthData _labyrinthData;
    readonly TilemapRequest _properties;
    // readonly IDictionary<int, IList<int>> _tilesByDistance = new Dictionary<int, IList<int>>();
    readonly HashSet<int> _dirty = [];
    IExtrudedTilemap _tilemap;
    // bool _isSorting;
    bool _fullUpdate = true;
    int _frameCount;
    /// <summary>
    /// Recompute the effective dungeon light (RE 5C): mode = mapFlags &amp; 3; mode 1 →
    /// min(100, max(Light-spell pct, party light-item total)) + Iskai-leader bonus;
    /// other modes are fully lit. Mapped onto uAmbient as
    /// max(LABDATA base, light·255/100) — the 0..255 additive ambient (INFERRED mapping;
    /// the original applies light through palette fades).
    /// </summary>
    void RecomputeLight()
    {
        if (_tilemap == null)
            return;
        var state = TryResolve<IGameState>();
        var party = TryResolve<IParty>();
        var assets = TryResolve<UAlbion.Formats.IAssetManager>();
        var mapData = _logicalMap.Events as UAlbion.Formats.Assets.IMapData;
        int light = UAlbion.Game.Magic.DungeonLighting.EffectiveLight(state, party, mapData, assets);
        uint level = (uint)Math.Clamp(Math.Max(_labyrinthData.Lighting, light * 255 / 100), 0, 255);
        if (level != _tilemap.AmbientLightLevel)
        {
            _tilemap.AmbientLightLevel = level;
            Info($"[Light] dungeon ambient recomputed: light {light}% → level {level}");
        }
    }

    public MapRenderable3D(LogicalMap3D logicalMap, LabyrinthData labyrinthData, TilemapRequest properties)
    {
        ArgumentNullException.ThrowIfNull(logicalMap);
        ArgumentNullException.ThrowIfNull(labyrinthData);

        On<PrepareFrameEvent>(_ => Update());
        // The real dungeon light model (RE 5C fcn.0001473c): recompute whenever the
        // Light spell state changes, hourly (decay), and when inventories change
        // (light items = torches etc contribute their Activate byte).
        On<UAlbion.Game.Events.DungeonLightChangedEvent>(_ => RecomputeLight());
        On<HourElapsedEvent>(_ => RecomputeLight());
        On<UAlbion.Game.Events.Inventory.InventoryChangedEvent>(_ => RecomputeLight());
        On<AmbientLightEvent>(e =>
        {
            // Debug/script override: direct delta on top of the computed level.
            if (_tilemap == null) return;
            int level = (int)_tilemap.AmbientLightLevel + e.Delta;
            _tilemap.AmbientLightLevel = (uint)Math.Clamp(level, 0, 255);
        });
        // On<SortMapTilesEvent>(e => _isSorting = e.IsSorting);
        _logicalMap = logicalMap;
        _labyrinthData = labyrinthData;
        _properties = properties;

        _logicalMap.Dirty += (_, args) =>
        {
            if (args.Type is IconChangeType.Floor or IconChangeType.Ceiling or IconChangeType.Wall)
                _dirty.Add(_logicalMap.Index(args.X, args.Y));
        };
    }

    protected override void Subscribed()
    {
        Raise(new LoadPaletteEvent(_logicalMap.PaletteId));

        if (_tilemap != null)
            return;

        _properties.TileCount = _logicalMap.Width * _logicalMap.Height;
        _properties.DayPalette = Assets.LoadPalette(_logicalMap.PaletteId);

        if (NightPalettes.TryGetValue(_logicalMap.PaletteId, out var nightPaletteId))
            _properties.NightPalette = Assets.LoadPalette(nightPaletteId);

        var etmManager = Resolve<IEtmManager>();
        _tilemap = etmManager.CreateTilemap(_properties);

        for (int i = 0; i < _labyrinthData.FloorAndCeilings.Count; i++)
        {
            var floorInfo = _labyrinthData.FloorAndCeilings[i];
            _tilemap.DefineFloor(i + 1, Assets.LoadTexture(floorInfo?.SpriteId ?? AssetId.None));
        }

        for (int i = 0; i < _labyrinthData.Walls.Count; i++)
        {
            var wallInfo = _labyrinthData.Walls[i];
            if (wallInfo == null)
                continue;

            ITexture wall = Assets.LoadTexture(wallInfo.SpriteId);
            if (wall == null)
                continue;

            bool isAlphaTested = (wallInfo.Properties & Wall.WallFlags.AlphaTested) != 0;
            _tilemap.DefineWall(i + 1, wall, 0, 0, wallInfo.TransparentColour, isAlphaTested);

            foreach(var overlayInfo in wallInfo.Overlays)
            {
                if (overlayInfo.SpriteId.IsNone)
                    continue;

                var overlay = Assets.LoadTexture(overlayInfo.SpriteId);
                _tilemap.DefineWall(i + 1, overlay, overlayInfo.XOffset, overlayInfo.YOffset, wallInfo.TransparentColour, isAlphaTested);
            }
        }

        _fullUpdate = true;
    }

    protected override void Unsubscribed()
    {
        _tilemap?.Dispose();
        _tilemap = null;
    }

    void SetTile(int index, int order, int frameCount)
    {
        var (floorIndex, floor) = _logicalMap.GetFloor(index);
        var (ceilingIndex, ceiling) = _logicalMap.GetCeiling(index);
        var (wallIndex, wall) = _logicalMap.GetWall(index);

        EtmTileFlags flags = 0;
        if (floor != null)
        {
            if ((floor.Properties & FloorAndCeiling.FcFlags.Bouncy) != 0)
                flags |= EtmTileFlags.FloorBackAndForth;

            if ((floor.Properties & FloorAndCeiling.FcFlags.SelfIlluminating) != 0)
                flags |= EtmTileFlags.SelfIlluminating;
        }

        if (ceiling != null)
        {
            if ((ceiling.Properties & FloorAndCeiling.FcFlags.Bouncy) != 0)
                flags |= EtmTileFlags.CeilingBackAndForth;

            if ((ceiling.Properties & FloorAndCeiling.FcFlags.SelfIlluminating) != 0)
                flags |= EtmTileFlags.SelfIlluminating;
        }

        if (wall != null)
        {
            if ((wall.Properties & Wall.WallFlags.Bouncy) != 0)
                flags |= EtmTileFlags.WallBackAndForth;

            if ((wall.Properties & Wall.WallFlags.AlphaTested) != 0)
                flags |= EtmTileFlags.Translucent | (EtmTileFlags)((uint)wall.TransparentColour << 24);

            if ((wall.Properties & Wall.WallFlags.SelfIlluminating) != 0)
                flags |= EtmTileFlags.SelfIlluminating;
        }

        // #65 (opt-in, default off): brighten tiles the party can interact with (zones carrying an
        // action trigger), using the existing Highlight shader path.
        if (ReadVar(V.Game.Graphics.HighlightInteractableTiles))
        {
            const UAlbion.Formats.Assets.Maps.TriggerTypes actionMask =
                UAlbion.Formats.Assets.Maps.TriggerTypes.Examine | UAlbion.Formats.Assets.Maps.TriggerTypes.Manipulate |
                UAlbion.Formats.Assets.Maps.TriggerTypes.Take | UAlbion.Formats.Assets.Maps.TriggerTypes.TalkTo |
                UAlbion.Formats.Assets.Maps.TriggerTypes.UseItem;
            var zone = _logicalMap.GetZone(index);
            if (zone?.Node != null && (zone.Trigger & actionMask) != 0)
                flags |= EtmTileFlags.Highlight;
        }

        _tilemap.SetTile(order, floorIndex, ceilingIndex, wallIndex, frameCount, flags);
    }

    void Update()
    {
        var frameCount =  (Resolve<IGameState>()?.TickCount ?? 0) / ReadVar(V.Game.Time.FastTicksPerMapTileFrame);

        if (_frameCount != frameCount)
        {
            _dirty.UnionWith(_tilemap.AnimatedTiles);
            _frameCount = frameCount;
        }

        if (_fullUpdate)
        {
            using var _ = PerfTracker.FrameEvent("5.1 Update tilemap");
            for (int j = 0; j < _logicalMap.Height; j++)
            {
                for (int i = 0; i < _logicalMap.Width; i++)
                {
                    int index = _logicalMap.Index(i, j);
                    SetTile(index, index, _frameCount);
                }
            }

            _fullUpdate = false;
        }
        else if (_dirty.Count > 0)
        {
            foreach (var index in _dirty)
                SetTile(index, index, _frameCount);
        }
        _dirty.Clear();
    }

/*
    void SortingUpdate()
    {
        using var _ = PerfTracker.FrameEvent("5.1 Update tilemap (sorting)");

        foreach (var list in _tilesByDistance.Values)
            list.Clear();

        var camera = Resolve<ICameraProvider>().Camera;
        var cameraTilePosition = camera.Position;

        var map = Resolve<IMapManager>().Current;
        if (map != null)
            cameraTilePosition /= map.TileSize;

        int cameraTileX = (int)cameraTilePosition.X;
        int cameraTileY = (int)cameraTilePosition.Y;

        for (int j = 0; j < _logicalMap.Height; j++)
        {
            for (int i = 0; i < _logicalMap.Width; i++)
            {
                int distance = Math.Abs(j - cameraTileY) + Math.Abs(i - cameraTileX);
                if(!_tilesByDistance.TryGetValue(distance, out var list))
                {
                    list = new List<int>();
                    _tilesByDistance[distance] = list;
                }

                int index = j * _logicalMap.Width + i;
                list.Add(index);
            }
        }

        int order = 0;
        foreach (var distance in _tilesByDistance.OrderByDescending(x => x.Key).ToList())
        {
            if (distance.Value.Count == 0)
            {
                _tilesByDistance.Remove(distance.Key);
                continue;
            }

            foreach (var index in distance.Value)
            {
                SetTile(index, order, _frameCount);
                order++;
            }
        }
    }
*/
}
