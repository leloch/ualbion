using System;
using System.Numerics;
using UAlbion.Api;
using UAlbion.Api.Eventing;
using UAlbion.Config;
using UAlbion.Core;
using UAlbion.Core.Events;
using UAlbion.Core.Visual;
using UAlbion.Formats.Assets;
using UAlbion.Formats.Assets.Labyrinth;
using UAlbion.Formats.Assets.Maps;
using UAlbion.Formats.Ids;
using UAlbion.Formats.MapEvents;
using UAlbion.Game.Entities.Map2D;
using UAlbion.Game.Events;
using UAlbion.Game.State;

namespace UAlbion.Game.Entities.Map3D;

public class DungeonMap : GameComponent, IMap
{
    readonly MapData3D _mapData;
    readonly ICamera _camera;
    readonly Container _sceneObjects;
    LabyrinthData _labyrinthData;
    LogicalMap3D _logicalMap;
    AutomapDialog _automap;
    TilemapRequest _tilemapProperties;
    readonly System.Collections.Generic.Dictionary<int, Npc3D> _npc3ds = [];
    float _backgroundRed;
    float _backgroundGreen;
    float _backgroundBlue;
    ISkybox _skybox;

    public DungeonMap(MapId mapId, MapData3D mapData, ICamera camera)
    {
        MapId = mapId;
        _mapData = mapData ?? throw new ArgumentNullException(nameof(mapData));
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        _sceneObjects = new($"MapObjects_{mapId}");

        On<TriggerMapTileEvent>(TileTriggered);
        On<MapInitEvent>(_ => FireEventChains(TriggerType.MapInit, true));
        On<SlowClockEvent>(_ => FireEventChains(TriggerType.EveryStep, false));
        On<HourElapsedEvent>(_ => FireEventChains(TriggerType.EveryHour, false));
        On<DayElapsedEvent>(_ => FireEventChains(TriggerType.EveryDay, false));
        On<PlayerEnteredTileEvent>(OnPlayerEnteredTile);
        On<ChangeNpcMovementEvent>(OnChangeNpcMovement);
        On<ChangeNpcSpriteEvent>(OnChangeNpcSprite);
        On<ChangeIconEvent>(ChangeIcon);
        On<SpinnerEvent>(OnSpinner);
        // On<UnloadMapEvent>(_ => Unload());
    }

    // Spinner tile (RE _RE_OPCODES_WORLD.md, handler 0x3a8a1): sets the party's FACING — 0..3 are
    // absolute quadrants (N/E/S/W); 4 = random direction (the classic disorientation tile). It only
    // acts in 3D and never moves the party. (PLACEHOLDER: the random case doesn't exclude the
    // current facing — a small chance of no turn; the original re-rolls until different.)
    void OnSpinner(SpinnerEvent e)
    {
        int dir = e.Unk1;
        if (dir == 4)
            dir = Resolve<UAlbion.Game.IRandom>().Generate(4);
        Raise(new UAlbion.Formats.ScriptEvents.PartyTurnEvent((UAlbion.Formats.Direction)(dir & 3)));
    }

    // Wall/floor/ceiling changes (levers, pressure plates → portcullis/wall-dissolve in
    // Drinno/Kounos/Kenget Kamulos/Toronto). 2D had this; 3D did not, so those chains were
    // silent no-ops — a physical hard-stop (gap-audit §2). LogicalMap3D.ChangeWall/Floor/
    // Ceiling already exist and Collider3D reads passability live, so this handler is the
    // only missing link. Same logic as FlatMap.ChangeIcon.
    void ChangeIcon(ChangeIconEvent e)
    {
        if (_logicalMap == null)
            return;

        var context = (EventContext)Context;
        bool relative = e.Scope is EventScope.RelPerm or EventScope.RelTemp;
        bool temp = e.Scope is EventScope.AbsTemp or EventScope.RelTemp;

        if (relative && context.Source.AssetId.Type != AssetType.Map)
        {
            ApiUtil.Assert($"Event {e} must be triggered from a map event if using relative coordinates");
            return;
        }

        byte x = relative ? (byte)(e.X + context.Source.X) : (byte)e.X;
        byte y = relative ? (byte)(e.Y + context.Source.Y) : (byte)e.Y;
        _logicalMap.Modify(x, y, e.ChangeType, temp, e.Layers, e.Value);
    }

    void OnPlayerEnteredTile(PlayerEnteredTileEvent e)
    {
        _automap?.MarkDiscovered(e.X, e.Y);

        // Stepping on a goto-point (automap marker) tile discovers it — the original's
        // fcn.0005c10a sets switch(7, MarkerId). Gates the automap goto glyph (18) and
        // the Teleporter spell's destination list.
        var state = TryResolve<IGameState>();
        if (state != null && _mapData.Automap != null)
        {
            foreach (var marker in _mapData.Automap)
            {
                if (marker == null || marker.X != e.X || marker.Y != e.Y)
                    continue;
                if (!state.IsAutomapMarkerFound(marker.MarkerId))
                {
                    state.SetAutomapMarkerFound(marker.MarkerId);
                    Info($"[Automap] Discovered goto-point {marker.MarkerId} '{marker.Name}' at ({e.X},{e.Y})");
                }
            }
        }

        // Fire any tile-scoped Normal-trigger event chain on the tile the party just entered.
        // Mirrors FlatMap.OnPlayerEnteredTile (2D); the 3D variant emits PlayerEnteredTileEvent
        // from Movement3D once the camera position crosses a tile boundary.
        var zone = _logicalMap?.GetOffsetZone(e.X, e.Y);
        if (zone?.Node == null)
            return;

        if ((zone.Trigger & TriggerTypes.Normal) == 0)
            return;

        var source = new EventSource(_mapData.Id, TriggerType.Normal, zone.X, zone.Y);
        Raise(new TriggerChainEvent(_mapData, zone.EventIndex, source));
    }

    public override string ToString() => $"DungeonMap:{MapId} TileSize: {TileSize}";
    public MapId MapId { get; }
    public MapType MapType => MapType.ThreeD;
    public IMapData MapData => _mapData;
    public Vector3 TileSize => _labyrinthData?.TileSize ?? Vector3.One * 512;
    public float BaseCameraHeight => (_labyrinthData?.CameraHeight ?? 0) != 0 ? _labyrinthData.CameraHeight * 8 : TileSize.Y / 2;

    void Setup(IGameState state)
    {
        _labyrinthData = Assets.LoadLabyrinthData(_mapData.LabDataId);
        if (_labyrinthData == null)
            return;

        // AttachChild (not bare new) so the map is part of the component tree and its Subscribed()
        // runs — that's where LogicalMap replays persisted Perm/Temp changes (#95). FlatMap (2D)
        // already does this; DungeonMap didn't, so 3D wall/floor/ceiling changes (opened doors,
        // dissolved walls) were never re-applied on map re-entry.
        _logicalMap = AttachChild(new LogicalMap3D(_mapData, _labyrinthData, state.TemporaryMapChanges, state.PermanentMapChanges));

        var properties = new TilemapRequest
        {
            Id = MapId,
            Width = (uint)_logicalMap.Width,
            Scale = _labyrinthData.TileSize,
            Origin = _labyrinthData.TileSize.Y / 2 * Vector3.UnitY,
            HorizontalSpacing = _labyrinthData.TileSize * Vector3.UnitX,
            VerticalSpacing = _labyrinthData.TileSize * Vector3.UnitZ,
            AmbientLightLevel = _labyrinthData.Lighting,
            FogColor = _labyrinthData.FogColor,
            ObjectYScaling = _labyrinthData.ObjectYScaling,
            Pipeline = DungeonTilemapPipeline.Normal,
            SmoothTextures = ReadVar(V.Game.Graphics.SmoothDungeonTextures) // #34: opt-in, default off = vanilla
        };

        // These belong to the scene so we don't render when in menus etc
        var renderable = new MapRenderable3D(_logicalMap, _labyrinthData, properties);
        var selection = new Selection3D();
        _sceneObjects.Add(renderable);
        _sceneObjects.Add(selection);
        _sceneObjects.Add(new SelectionHandler3D(_logicalMap, TileSize));
        _automap = new AutomapDialog(_logicalMap, _mapData);
        _sceneObjects.Add(_automap);

        AttachChild(new ScriptManager());
        AttachChild(new Collider3D(_logicalMap));
        AttachChild(new Movement3D(_camera));

        if (!_labyrinthData.BackgroundId.IsNone)
        {
            var background = Assets.LoadTexture(_labyrinthData.BackgroundId);
            if (background == null)
                Error($"Could not load background image {_labyrinthData.BackgroundId}");

            var factory = Resolve<ICoreFactory>();
            _skybox = factory.CreateSkybox(background, _camera);
        }

        var palette = Assets.LoadPalette(_logicalMap.PaletteId);
        uint backgroundColour = palette.GetPaletteAtTime(0)[_labyrinthData.BackgroundColour];
        // MAP-04: >> binds tighter than &, so without parens green/blue both read the red byte
        // (0xff00>>8 == 0xff). Parenthesise the masks.
        _backgroundRed = (backgroundColour & 0xff) / 255.0f;
        _backgroundGreen = ((backgroundColour & 0xff00) >> 8) / 255.0f;
        _backgroundBlue = ((backgroundColour & 0xff0000) >> 16) / 255.0f;

        //if (_labyrinthData.CameraHeight != 0)
        //    Debugger.Break();

        //if (_labyrinthData.Unk12 != 0) // 7=1|2|4 (Jirinaar), 54=32|16|4|2, 156=128|16|8|2 (Tall town)
        //    Debugger.Break();

        // Raise(new LogEvent(LogEvent.Level.Info, $"WallHeight: {_labyrinthData.WallHeight} MaxObj: {maxObjectHeightRaw} EffWallWidth: {_labyrinthData.EffectiveWallWidth}"));

        var game = Resolve<IGameState>();
        _tilemapProperties = properties;
        bool initialiseNpcState = game.MapIdForNpcs != MapId;
        if (initialiseNpcState)
        {
            for (int i = 0; i < _logicalMap.Npcs.Count; i++)
            {
                var npc = _logicalMap.Npcs[i];
                var npcState = game.Npcs[i];
                if (npcState == null)
                {
                    npcState = new UAlbion.Formats.Assets.Save.NpcState();
                    game.Npcs[i] = npcState;
                }
                NpcManager2D.InitialiseState(npc, npcState, true, _logicalMap.Events, Vector2.One);
            }

            // Re-apply persisted NPC morphs (change_npc_* map events) before building.
            foreach (var (npcNum, type, value) in _logicalMap.NpcChanges)
            {
                if (npcNum >= game.Npcs.Count || game.Npcs[npcNum] == null)
                    continue;
                if (type == IconChangeType.NpcMovement)
                    game.Npcs[npcNum].MovementType = (NpcMovement)value;
                else if (type == IconChangeType.NpcSprite)
                    game.Npcs[npcNum].SpriteOrGroup = new AssetId(AssetType.ObjectGroup, value);
            }
        }

        for (int i = 0; i < _logicalMap.Npcs.Count; i++)
            BuildNpc(_logicalMap.Npcs[i], i, properties, game);
        game.MapIdForNpcs = MapId;

        // Build props
        for (int y = 0; y < _logicalMap.Height; y++)
        {
            for (int x = 0; x < _logicalMap.Width; x++)
            {
                var group = _logicalMap.GetObject(x, y);
                if (group == null) continue;
                foreach (var subObject in group.SubObjects)
                    _sceneObjects.Add(MapObject.Build(x, y, _labyrinthData, subObject, properties));
            }
        }
    }

    protected override void Subscribed()
    {
        var state = Resolve<IGameState>();
        if (state.Party == null)
            return;

        foreach (var player in state.Party.StatusBarOrder)
            player.SetPositionFunc(() => _camera.Position / TileSize);

        if (_labyrinthData == null)
            Setup(state);

        var scene = Resolve<ISceneManager>().ActiveScene;
        scene.Add(_sceneObjects);

        Raise(new SetClearColourEvent(_backgroundRed, _backgroundGreen, _backgroundBlue, 1.0f));
    }

    protected override void Unsubscribed()
    {
        if (_sceneObjects.Parent is IScene scene)
            scene.Remove(_sceneObjects);

        _skybox?.Dispose();
        _skybox = null;
        base.Unsubscribed();
    }

    void BuildNpc(MapNpc npc, int index, TilemapRequest properties, IGameState game)
    {
        var state = game.Npcs[index];
        if (state == null)
        {
            state = new UAlbion.Formats.Assets.Save.NpcState();
            game.Npcs[index] = state;
        }

        // Saved NpcState sprites come through the save serdes typed as NpcLargeGfx (SavedGame.cs
        // hardcodes MapType.TwoD); on a 3D map they must be ObjectGroup. The stored value is a
        // raw, type-agnostic index (all these gfx types are identity-mapped), so re-key to
        // ObjectGroup preserving the id — the 3D counterpart of NpcManager2D's small/large re-key.
        if (state.SpriteOrGroup.Type is AssetType.NpcLargeGfx or AssetType.NpcSmallGfx)
            state.SpriteOrGroup = new AssetId(AssetType.ObjectGroup, state.SpriteOrGroup.Id);

        // The GROUP comes from the live NpcState (so change_npc_sprite morphs apply);
        // freshly initialised states carry the MapNpc's group.
        var group = state.SpriteOrGroup.IsNone ? npc.SpriteOrGroup : state.SpriteOrGroup;
        if (group.IsNone)
            return;

        if (group.Type != AssetType.ObjectGroup)
        {
            Warn($"[3DMap] Tried to load npc with object group of incorrect type: {group}");
            return;
        }

        // ObjectGroup references are 1-based on disk (0 = "no NPC"; 1..N selects ObjectGroups[0..N-1]).
        // LogicalMap3D.GetObject uses the same convention for tile contents.
        if (group.Id <= 0 || group.Id > _labyrinthData.ObjectGroups.Count)
        {
            Warn($"[3DMap] Tried to load object group {group.Id}, valid range 1..{_labyrinthData.ObjectGroups.Count}.");
            return;
        }

        var npc3d = new Npc3D(state, npc, properties, _logicalMap.Width, _logicalMap.Height, (byte)index);
        var objectData = _labyrinthData.ObjectGroups[group.Id - 1];
        foreach (var subObject in objectData.SubObjects)
        {
            var obj = MapObject.Build(state.X, state.Y, _labyrinthData, subObject, properties);
            npc3d.AddPart(obj, state.X, state.Y);
        }

        _npc3ds[index] = npc3d;
        _sceneObjects.Add(npc3d);
    }

    /// <summary>
    /// Live NPC morphs on 3D maps (the 2D counterpart lives in NpcManager2D/Npc2D):
    /// movement changes apply via NpcState (Npc3D reads MovementType per update);
    /// sprite (object-group) changes rebuild the NPC's parts. Both are recorded in the
    /// map-change collection so they replay on map re-entry.
    /// </summary>
    void OnChangeNpcMovement(ChangeNpcMovementEvent e)
    {
        var game = Resolve<IGameState>();
        if (e.NpcNum >= game.Npcs.Count || game.Npcs[e.NpcNum] == null)
            return;
        game.Npcs[e.NpcNum].MovementType = e.Mode;
        RecordNpcChange(e.NpcNum, IconChangeType.NpcMovement, (ushort)e.Mode, e.Scope);
    }

    void OnChangeNpcSprite(ChangeNpcSpriteEvent e)
    {
        var game = Resolve<IGameState>();
        if (e.NpcNum >= game.Npcs.Count || game.Npcs[e.NpcNum] == null)
            return;
        if (e.SpriteOrGroup.Type != AssetType.ObjectGroup)
        {
            Warn($"[3DMap] change_npc_sprite with non-ObjectGroup id {e.SpriteOrGroup} on a 3D map");
            return;
        }

        game.Npcs[e.NpcNum].SpriteOrGroup = e.SpriteOrGroup;
        RecordNpcChange(e.NpcNum, IconChangeType.NpcSprite, (ushort)e.SpriteOrGroup.Id, e.Scope);

        // Rebuild the NPC's visual parts from the new group.
        if (_npc3ds.TryGetValue(e.NpcNum, out var old) && old != null)
        {
            _sceneObjects.Remove(old);
            _npc3ds.Remove(e.NpcNum);
        }

        if (_tilemapProperties != null && e.NpcNum < _logicalMap.Npcs.Count)
            BuildNpc(_logicalMap.Npcs[e.NpcNum], e.NpcNum, _tilemapProperties, game);
    }

    void RecordNpcChange(byte npcNum, IconChangeType type, ushort value, EventScope scope)
    {
        bool temp = scope is EventScope.AbsTemp or EventScope.RelTemp;
        _logicalMap.Modify(npcNum, 0, type, temp, ChangeIconLayers.None, value);
    }

    void TileTriggered(TriggerMapTileEvent e)
    {
        // Raised by SelectionHandler3D's context menu (Examine / Manipulate / Take / TalkTo / UseItem)
        var zone = _logicalMap?.GetOffsetZone(e.X, e.Y);
        if (zone?.Node == null)
            return;

        // UseItem carries the held item id as the source so `query used_item == X` matches.
        AssetId sourceId = _mapData.Id;
        if (e.Type == TriggerType.UseItem)
        {
            var held = TryResolve<UAlbion.Game.State.Player.IInventoryManager>()?.ItemInHand.Item ?? AssetId.None;
            if (!held.IsNone)
                sourceId = held;
        }

        var source = new EventSource(sourceId, e.Type, zone.X, zone.Y);
        Raise(new TriggerChainEvent(_mapData, zone.EventIndex, source));
    }

    void FireEventChains(TriggerType type, bool log)
    {
        var zones = ZoneListPool.Shared.Borrow();
        _mapData.GetZonesOfType(zones, type.ToBitField());

        try
        {
            if (!log)
                Raise(new SetLogLevelEvent(LogLevel.Warning));

            foreach (var zone in zones)
                Raise(new TriggerChainEvent(_mapData, zone.EventIndex,
                    new EventSource(_mapData.Id, type, zone.X, zone.Y)));

            if (!log)
                Raise(new SetLogLevelEvent(LogLevel.Info));
        }
        finally
        {
            ZoneListPool.Shared.Return(zones);
        }
    }
}