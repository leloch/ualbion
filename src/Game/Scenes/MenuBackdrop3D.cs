using System;
using System.Numerics;
using UAlbion.Api.Eventing;
using UAlbion.Core.Events;
using UAlbion.Core.Visual;
using UAlbion.Formats.Assets;
using UAlbion.Formats.Assets.Maps;
using UAlbion.Formats.Assets.Save;
using UAlbion.Formats.Ids;
using UAlbion.Game.Entities.Map2D;
using UAlbion.Game.Entities.Map3D;
using UAlbion.Game.Events;

namespace UAlbion.Game.Scenes;

/// <summary>
/// A display-only live 3D vista rendered behind the modern main menu. It stands up just the visual
/// slice of a 3D map (LogicalMap3D + MapRenderable3D + skybox) straight from asset data - no game
/// state, no gameplay components (collider/scripts/movement) - and slowly pans the camera so the
/// menu sits over a moving Albion landscape that showcases the skybox/fog/bump rendering. Building
/// it needs no SavedGame; the game-state lookups inside MapRenderable3D are all null-safe TryResolve.
/// </summary>
public class MenuBackdrop3D : GameComponent
{
    readonly ICamera _camera;
    readonly MapId _mapId;
    LogicalMap3D _logicalMap;
    ISkybox _skybox;
    float _yaw;
    Vector3 _origin;
    bool _ready;

    public MenuBackdrop3D(ICamera camera, MapId mapId)
    {
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        _mapId = mapId;
        On<EngineUpdateEvent>(e => Animate(e.DeltaSeconds));
    }

    protected override void Subscribed()
    {
        if (_ready)
            return;

        if (Assets.LoadMap(_mapId) is not MapData3D mapData)
        {
            Warn($"[MenuBackdrop3D] {_mapId} is not a 3D map; backdrop disabled");
            return;
        }

        var labyrinth = Assets.LoadLabyrinthData(mapData.LabDataId);
        if (labyrinth == null)
        {
            Warn($"[MenuBackdrop3D] no labyrinth for {_mapId}; backdrop disabled");
            return;
        }

        // Empty change collections - this is a display-only map with no persisted edits.
        _logicalMap = AttachChild(new LogicalMap3D(mapData, labyrinth, new MapChangeCollection(), new MapChangeCollection()));

        var properties = new TilemapRequest
        {
            Id = _mapId,
            Width = (uint)_logicalMap.Width,
            Scale = labyrinth.TileSize,
            Origin = labyrinth.TileSize.Y / 2 * Vector3.UnitY,
            HorizontalSpacing = labyrinth.TileSize * Vector3.UnitX,
            VerticalSpacing = labyrinth.TileSize * Vector3.UnitZ,
            AmbientLightLevel = labyrinth.Lighting,
            FogColor = labyrinth.FogColor,
            ObjectYScaling = labyrinth.ObjectYScaling,
            Pipeline = DungeonTilemapPipeline.Normal,
            SmoothTextures = ReadVar(V.Game.Graphics.SmoothDungeonTextures)
        };

        AttachChild(new MapRenderable3D(_logicalMap, labyrinth, properties));

        if (!labyrinth.BackgroundId.IsNone)
        {
            var background = Assets.LoadTexture(labyrinth.BackgroundId);
            if (background != null)
                _skybox = Resolve<ICoreFactory>().CreateSkybox(background, _camera); // auto-registers with the SkyboxManager
        }

        // Park the camera near the centre of the map at eye height and look level; Animate() pans it.
        var tile = labyrinth.TileSize;
        _origin = new Vector3(
            _logicalMap.Width / 2f * tile.X,
            tile.Y * 0.6f,
            _logicalMap.Height / 2f * tile.Z);
        _camera.Position = _origin;
        _camera.Pitch = 0f;
        _camera.Yaw = 0f;
        _ready = true;
    }

    protected override void Unsubscribed()
    {
        _skybox?.Dispose();
        _skybox = null;
    }

    void Animate(float dt)
    {
        if (!_ready)
            return;

        // Slow continuous pan so the panorama drifts past; a gentle bob adds life.
        _yaw += dt * 0.12f;
        if (_yaw > MathF.PI * 2) _yaw -= MathF.PI * 2;
        _camera.Yaw = _yaw;
        _camera.Position = _origin + new Vector3(0, MathF.Sin(_yaw * 0.5f) * 6f, 0);
    }
}
