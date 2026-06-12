using System;
using System.Collections.Generic;
using System.Numerics;
using UAlbion.Api.Eventing;
using UAlbion.Core;
using UAlbion.Core.Events;
using UAlbion.Core.Visual;
using UAlbion.Formats;
using UAlbion.Formats.Assets.Maps;
using UAlbion.Formats.Assets.Save;
using UAlbion.Formats.Config;
using UAlbion.Formats.MapEvents;
using UAlbion.Game.State;
using UAlbion.Formats.Ids;
using UAlbion.Game.Entities.Map2D;
using UAlbion.Game.Events;
using UAlbion.Game.Gui.Controls;
using UAlbion.Game.Scenes;
using UAlbion.Game.Text;

namespace UAlbion.Game.Entities.Map3D;

/// <summary>
/// 3D counterpart of <see cref="SelectionHandler2D"/>: ray-marches the dungeon tile grid
/// to find the wall / object tile the cursor is pointing at, registers it as a selection
/// target, and shows the environment context menu (Examine / Manipulate / Take / TalkTo)
/// for the zone on that tile when the map menu is requested.
/// </summary>
public sealed class SelectionHandler3D : GameComponent
{
    const int MaxTilesTravelled = 16;

    readonly LogicalMap3D _map;
    readonly Vector3 _tileSize;
    readonly MapTileHit _mapTileHit = new();
    int _lastTileX = -1;
    int _lastTileY = -1;
    string _lastRayDebug = "(no ray cast yet)";

    public SelectionHandler3D(LogicalMap3D map, Vector3 tileSize)
    {
        _map = map ?? throw new ArgumentNullException(nameof(map));
        _tileSize = tileSize;

        On<WorldCoordinateSelectEvent>(OnSelect);
        On<ShowMapMenuEvent>(_ => ShowMapMenu());
        On<UiRightClickEvent>(e =>
        {
            Info($"[Sel3D] Right click on tile ({_lastTileX}, {_lastTileY}) ray={_lastRayDebug} — pushing RightButtonHeld");
            e.Propagating = false;
            Raise(new PushMouseModeEvent(MouseMode.RightButtonHeld));
        });
    }

    void OnSelect(WorldCoordinateSelectEvent e)
    {
        // The select ray is parameterised FAR-PLANE-FIRST: e.Origin sits on the far plane
        // and e.Direction points back toward the camera, so the camera/near end is at
        // e.Origin + e.Direction (see OrthographicCamera.TransformSelect — the perspective
        // camera uses the same convention). Rebase so we march from the camera INTO the
        // scene; a local parameter tLocal along -e.Direction maps back to the original
        // parameterisation as t = 1 - tLocal, keeping hit ordering consistent with the
        // sprite/mesh RayIntersect results.
        //
        // Amanatides & Woo grid traversal over the XZ plane in tile units. Tile X spans
        // world X, tile Y spans world Z (DungeonMap.Setup: HorizontalSpacing = TileSize.X
        // * UnitX, VerticalSpacing = TileSize.Z * UnitZ).
        var rayStart = e.Origin + e.Direction;
        float px = rayStart.X / _tileSize.X;
        float py = rayStart.Z / _tileSize.Z;
        float dx = -e.Direction.X / _tileSize.X;
        float dy = -e.Direction.Z / _tileSize.Z;

        int tileX = (int)MathF.Floor(px);
        int tileY = (int)MathF.Floor(py);

        _lastRayDebug = $"camera=({rayStart.X:F0},{rayStart.Y:F0},{rayStart.Z:F0}) dir=({dx:F4},{dy:F4}) start=({tileX},{tileY})";

        int stepX = MathF.Sign(dx) >= 0 ? 1 : -1;
        int stepY = MathF.Sign(dy) >= 0 ? 1 : -1;

        float tDeltaX = dx != 0 ? MathF.Abs(1.0f / dx) : float.PositiveInfinity;
        float tDeltaY = dy != 0 ? MathF.Abs(1.0f / dy) : float.PositiveInfinity;
        float tMaxX = dx != 0 ? ((stepX > 0 ? tileX + 1 : tileX) - px) / dx : float.PositiveInfinity;
        float tMaxY = dy != 0 ? ((stepY > 0 ? tileY + 1 : tileY) - py) / dy : float.PositiveInfinity;

        // NPC-occupied tiles also stop the ray (NPCs are entities, not map contents).
        var state = TryResolve<IGameState>();
        var npcs = state?.Loaded == true ? state.Npcs : null;

        float t = 0;
        bool hit = false;
        for (int i = 0; i < MaxTilesTravelled; i++)
        {
            // Skip the tile the camera is standing in (i == 0) — you can't point at the
            // inside of your own tile, and the party's own object would otherwise always
            // be the first hit.
            if (i > 0 && tileX >= 0 && tileY >= 0 && tileX < _map.Width && tileY < _map.Height)
            {
                var (wallIndex, _) = _map.GetWall(tileX, tileY);
                var objectGroup = _map.GetObject(tileX, tileY);
                bool npcHere = false;
                if (npcs != null)
                {
                    foreach (var npc in npcs)
                    {
                        if (npc != null && !npc.Id.IsNone && npc.X == tileX && npc.Y == tileY)
                        {
                            npcHere = true;
                            break;
                        }
                    }
                }

                if (wallIndex != 0 || objectGroup != null || npcHere)
                {
                    hit = true;
                    break;
                }
            }

            if (tMaxX < tMaxY)
            {
                t = tMaxX;
                tMaxX += tDeltaX;
                tileX += stepX;
            }
            else
            {
                t = tMaxY;
                tMaxY += tDeltaY;
                tileY += stepY;
            }
        }

        if (!hit)
        {
            // Nothing solid in range; still register the handler so right-click anywhere
            // in the 3D view opens the environment menu (matching the original game).
            e.Selections.Add(new Selection(e.Origin, e.Direction, 1.0f, this));
            return;
        }

        float tOriginal = 1 - t; // Back to the event's far-plane-first parameterisation
        _mapTileHit.Tile = new Vector2(tileX, tileY);
        e.Selections.Add(new Selection(e.Origin, e.Direction, tOriginal, _mapTileHit));
        e.Selections.Add(new Selection(e.Origin, e.Direction, tOriginal, this));

        if (e.Debug)
        {
            var zone = _map.GetZone(tileX, tileY);
            if (zone != null)
                e.Selections.Add(new Selection(e.Origin, e.Direction, tOriginal, zone));
        }

        _lastTileX = tileX;
        _lastTileY = tileY;
    }

    void ShowMapMenu()
    {
        Info($"[Sel3D] ShowMapMenu for tile ({_lastTileX}, {_lastTileY})");
        if (_lastTileX < 0 || _lastTileY < 0)
            return;

        var window = Resolve<IGameWindow>();
        var camera = Resolve<ICameraProvider>().Camera;
        var tf = Resolve<ITextFormatter>();

        IText S(TextId textId) => tf.Center().Format(textId);

        var worldPosition = new Vector3(
            (_lastTileX + 0.5f) * _tileSize.X,
            _tileSize.Y / 2,
            (_lastTileY + 0.5f) * _tileSize.Z);
        var normPosition = camera.ProjectWorldToNorm(worldPosition);
        var uiPosition = window.NormToUi(normPosition.X, normPosition.Y);
        var heading = S(Base.SystemText.MapPopup_Environment);
        var options = new List<ContextMenuOption>();

        var zone = _map.GetOffsetZone(_lastTileX, _lastTileY);
        if (zone?.Chain != null && zone.Node != null)
        {
            if ((zone.Trigger & TriggerTypes.Examine) != 0)
            {
                options.Add(new ContextMenuOption(
                    S(Base.SystemText.MapPopup_Examine),
                    new TriggerMapTileEvent(TriggerType.Examine, zone.X, zone.Y),
                    ContextMenuGroup.Actions));
            }

            if ((zone.Trigger & TriggerTypes.Manipulate) != 0)
            {
                options.Add(new ContextMenuOption(
                    S(Base.SystemText.MapPopup_Manipulate),
                    new TriggerMapTileEvent(TriggerType.Manipulate, zone.X, zone.Y),
                    ContextMenuGroup.Actions));
            }

            if ((zone.Trigger & TriggerTypes.Take) != 0)
            {
                options.Add(new ContextMenuOption(
                    S(Base.SystemText.MapPopup_Take),
                    new TriggerMapTileEvent(TriggerType.Take, zone.X, zone.Y),
                    ContextMenuGroup.Actions));
            }

            if ((zone.Trigger & TriggerTypes.TalkTo) != 0)
            {
                options.Add(new ContextMenuOption(
                    S(Base.SystemText.MapPopup_TalkTo),
                    new TriggerMapTileEvent(TriggerType.TalkTo, zone.X, zone.Y),
                    ContextMenuGroup.Actions));
            }
        }

        // NPC on the selected tile → talk option (the 3D counterpart of Npc2D.OnRightClick).
        var state = TryResolve<IGameState>();
        if (state?.Loaded == true)
        {
            foreach (var npc in state.Npcs)
            {
                if (npc == null || npc.Id.IsNone)
                    continue;
                if (npc.X != _lastTileX || npc.Y != _lastTileY)
                    continue;

                var talkEvent = BuildNpcInteraction(npc);
                if (talkEvent != null)
                {
                    options.Add(new ContextMenuOption(
                        S(Base.SystemText.MapPopup_TalkTo),
                        talkEvent,
                        ContextMenuGroup.Actions));
                }
                break;
            }
        }

        options.Add(new ContextMenuOption(
            S(Base.SystemText.MapPopup_Map),
            new ShowAutomapEvent(),
            ContextMenuGroup.System));

        options.Add(new ContextMenuOption(
            S(Base.SystemText.MapPopup_MainMenu),
            new PushSceneEvent(SceneId.MainMenu),
            ContextMenuGroup.System));

        Raise(new ContextMenuEvent(uiPosition, heading, options));
    }

    /// <summary>Mirror of Npc2D.BuildInteractionEvent for 3D-map NPC states.</summary>
    static IEvent BuildNpcInteraction(NpcState npc)
    {
        IEvent result = null;
        if (npc.EventIndex != EventNode.UnusedEventId && npc.EventSet != null)
            result = new TriggerChainEvent(
                npc.EventSet,
                npc.EventIndex,
                new EventSource(npc.Id, TriggerType.TalkTo));
        else if (npc.Id.Type == UAlbion.Config.AssetType.NpcSheet)
            result = new StartDialogueEvent(npc.Id);
        else if (npc.Id.Type == UAlbion.Config.AssetType.MapTextIndex)
            result = new TextEvent((ushort)npc.Id.Id, TextLocation.NoPortrait, SheetId.None);
        return result;
    }
}
