using System;
using System.Collections.Generic;
using System.Numerics;
using UAlbion.Api.Eventing;
using UAlbion.Core;
using UAlbion.Core.Events;
using UAlbion.Core.Visual;
using UAlbion.Formats;
using UAlbion.Formats.Assets.Maps;
using UAlbion.Formats.Config;
using UAlbion.Formats.Ids;
using UAlbion.Game.Events;
using UAlbion.Game.Gui.Controls;
using UAlbion.Game.Scenes;
using UAlbion.Game.State;
using UAlbion.Game.State.Player;
using UAlbion.Game.Text;

namespace UAlbion.Game.Entities.Map2D;

public sealed class SelectionHandler2D : GameComponent
{
    static readonly Vector3 Normal = Vector3.UnitZ;
    readonly LogicalMap2D _map;
    readonly MapRenderable2D _renderable;
    readonly MapTileHit _mapTileHit = new();
    readonly DebugMapTileHit _debugMapTileHit = new();
    Func<object, string> _formatChain;
    int _lastHighlightIndex;
    UAlbion.Game.Input.CursorMode _cursorMode = UAlbion.Game.Input.CursorMode.Normal;

    public SelectionHandler2D(LogicalMap2D map, MapRenderable2D renderable)
    {
        On<WorldCoordinateSelectEvent>(OnSelect);
        On<ShowMapMenuEvent>(_ => ShowMapMenu());
        On<CursorModeEvent>(e => _cursorMode = e.Mode);
        On<UiLeftClickEvent>(OnLeftClick); // #5: click-to-walk (single step toward the clicked tile)
        On<UiRightClickEvent>(e =>
        {
            e.Propagating = false;
            Raise(new PushMouseModeEvent(MouseMode.RightButtonHeld));
        });

        _map = map ?? throw new ArgumentNullException(nameof(map));
        _renderable = renderable;
    }

    // #5: left-clicking the 2D map steps the party one tile toward the clicked tile (the original's
    // click-to-walk; 2D was keyboard-only). Single-step per click — only in the Normal/PathFinding
    // cursor mode (the verb modes Examine/Manipulate/Take/Talk go through the right-click menu).
    void OnLeftClick(UiLeftClickEvent e)
    {
        if (_cursorMode is not (UAlbion.Game.Input.CursorMode.Normal or UAlbion.Game.Input.CursorMode.PathFinding))
            return;
        var leader = TryResolve<IParty>()?.Leader;
        if (leader == null)
            return;

        var pos = leader.GetPosition();
        int lx = (int)MathF.Round(pos.X), ly = (int)MathF.Round(pos.Y);
        int cx = _lastHighlightIndex % _map.Width, cy = _lastHighlightIndex / _map.Width;
        int dx = Math.Sign(cx - lx), dy = Math.Sign(cy - ly);
        if (dx == 0 && dy == 0)
            return;

        e.Propagating = false;
        Raise(new UAlbion.Formats.ScriptEvents.PartyMoveEvent(dx, dy)); // +y = south, matches the W/S keybinds
    }

    public event EventHandler<int> HighlightIndexChanged;
    protected override void Subscribed()
    {
        var eventFormatter = new EventFormatter(Assets.LoadStringSafe, _map.Id.ToMapText());
        _formatChain = x =>
        {
            var builder = new UnformattedScriptBuilder(false);
            eventFormatter.FormatChain(builder, (IEventNode)x);
            return builder.Build();
        };
    }

    void OnSelect(WorldCoordinateSelectEvent e)
    {
        float denominator = Vector3.Dot(Normal, e.Direction);
        if (Math.Abs(denominator) < 0.00001f)
            return;

        float t = Vector3.Dot(-e.Origin, Normal) / denominator;
        if (t < 0)
            return;

        Vector3 intersectionPoint = e.Origin + t * e.Direction;
        int x = (int)(intersectionPoint.X / _renderable.TileSize.X);
        int y = (int)(intersectionPoint.Y / _renderable.TileSize.Y);

        _mapTileHit.Tile = new Vector2(x, y);
        e.Selections.Add(new Selection(e.Origin, e.Direction, t, _mapTileHit));

        if (e.Debug)
        {
            _debugMapTileHit.Tile = new Vector2(x, y);
            _debugMapTileHit.IntersectionPoint = intersectionPoint;
            _debugMapTileHit.UnderlayTile = _map.GetUnderlay(x, y);
            _debugMapTileHit.OverlayTile = _map.GetOverlay(x, y);
            e.Selections.Add(new Selection(e.Origin, e.Direction, t, _debugMapTileHit));
        }
        e.Selections.Add(new Selection(e.Origin, e.Direction, t, this));

        if (e.Debug)
        {
            var zone = _map.GetZone(x, y);
            if (zone != null)
                e.Selections.Add(new Selection(e.Origin, e.Direction, t, zone));

            var chain = zone?.Chain;
            if (chain != null)
                e.Selections.Add(new Selection(e.Origin, e.Direction, t, zone.Node, _formatChain));
        }

        int highlightIndex = y * _map.Width + x;
        if (_lastHighlightIndex != highlightIndex)
        {
            HighlightIndexChanged?.Invoke(this, highlightIndex);
            _lastHighlightIndex = highlightIndex;
        }
    }

    void ShowMapMenu()
    {
        int x = _lastHighlightIndex % _map.Width;
        int y = _lastHighlightIndex / _map.Width;
        var window = Resolve<IGameWindow>();
        var camera = Resolve<ICameraProvider>().Camera;
        var tf = Resolve<ITextFormatter>();

        IText S(TextId textId) => tf.Center().Format(textId);
        var worldPosition = new Vector2(x, y) * _map.TileSize;
        var normPosition = camera.ProjectWorldToNorm(new Vector3(worldPosition, 0.0f));
        var uiPosition = window.NormToUi(normPosition.X, normPosition.Y);
        var heading = S(Base.SystemText.MapPopup_Environment);
        var options = new List<ContextMenuOption>();

        var zone = _map.GetOffsetZone(x, y);

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

            // Use-item: only when the player is holding an item and the zone accepts it
            // (tool-on-obstacle puzzles — pick-axe/screwdriver/staff). The held item is
            // carried into the UseItem trigger's EventSource by FlatMap so the zone chain's
            // query used_item matches. (B4: was never offered, so these puzzles were dead.)
            if ((zone.Trigger & TriggerTypes.UseItem) != 0
                && !(TryResolve<IInventoryManager>()?.ItemInHand.Item.IsNone ?? true))
            {
                options.Add(new ContextMenuOption(
                    S(Base.SystemText.MapPopup_UseItem),
                    new TriggerMapTileEvent(TriggerType.UseItem, zone.X, zone.Y),
                    ContextMenuGroup.Actions));
            }
        }

        // Rest availability by map RestMode (mapFlags & 0xC, RE'd popup builder 0x2204e):
        // cities (0) get a Wait option instead, interiors (3) get neither. The
        // "too dangerous" / "nobody is tired" gates run in GameState when the option fires.
        var restMode = Resolve<IMapManager>().Current?.MapData?.RestMode ?? RestMode.NoResting;
        switch (restMode)
        {
            case RestMode.Wait:
                options.Add(new ContextMenuOption(
                    S(Base.SystemText.MapPopup_Wait),
                    new PartyWaitEvent(),
                    ContextMenuGroup.Actions2));
                break;
            case RestMode.RestEightHours:
            case RestMode.RestUntilDawn:
                options.Add(new ContextMenuOption(
                    S(Base.SystemText.MapPopup_Rest),
                    new RestEvent(0), // 0 = duration auto-computed from RestMode + time of day
                    ContextMenuGroup.Actions2));
                break;
            default:
                break; // RestMode.NoResting: no option at all (interiors)
        }

        options.Add(new ContextMenuOption(
            S(Base.SystemText.MapPopup_MainMenu),
            new PushSceneEvent(SceneId.MainMenu),
            ContextMenuGroup.System
        ));

        Raise(new ContextMenuEvent(uiPosition, heading, options));
    }
}

/*

    Headers:
        MapPopup_Environment

    Actions:
      x MapPopup_Examine
      x MapPopup_Manipulate
      x MapPopup_Take
      x MapPopup_TalkTo
        MapPopup_Rest
      x MapPopup_MainMenu
        MapPopup_Map (3D only)
        MapPopup_Wait (3D only)

        MapPopup_Blocked1
        MapPopup_Blocked2
        MapPopup_CannotCarryThatMuch <--- consequence of "Take"
        MapPopup_ItsTooDangerousHere <-- consequence of "Rest"
        MapPopup_NoSpaceLeft <--- consequence of "Take"
        MapPopup_Person
        MapPopup_ReallyRest <--- consequence of "Rest"
        MapPopup_ThesePeopleDoNotSpeakTheSameLanguage <-- consequence of "TalkTo"
        MapPopup_ThisItemDoesntWorkHere
        MapPopup_ThisPersonIsAsleep
        MapPopup_ThisPersonSpeaksALanguageLeaderDoesntUnderstand <-- consequence of "TalkTo"
        MapPopup_ThisWordDoesntWorkHere
        MapPopup_TooFarAway1
        MapPopup_TooFarAway2
        MapPopup_TooFarAwayToTalkTo <-- consequence of "TalkTo"
        MapPopup_TooFarAwayToTouch
        MapPopup_TravelOnFoot
        MapPopup_UseItem
        MapPopup_UseWhichItem
        MapPopup_WaitForHowManyHours <-- consequence of "Wait"
*/
