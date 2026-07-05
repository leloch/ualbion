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

    // Click-to-walk path state: the remaining waypoints (tiles) the party auto-walks toward,
    // plus a stuck-watchdog so a blocked path can't spin forever issuing moves into a wall.
    readonly List<(int X, int Y)> _path = new();
    int _stuckTicks;
    int _lastTileX = int.MinValue, _lastTileY = int.MinValue;

    public SelectionHandler2D(LogicalMap2D map, MapRenderable2D renderable)
    {
        On<WorldCoordinateSelectEvent>(OnSelect);
        On<ShowMapMenuEvent>(_ => ShowMapMenu());
        On<CursorModeEvent>(e => _cursorMode = e.Mode);
        On<UiLeftClickEvent>(OnLeftClick); // #5: click-to-walk (A* path to the clicked tile)
        On<UAlbion.Game.Events.PartyGotoEvent>(e => StartGoto(e.X, e.Y)); // harness: walk like a click
        On<UAlbion.Game.Events.FastClockEvent>(_ => FollowPath());
        // Cancel an auto-walk the instant the player steers manually. EventExchange.Raise SKIPS the
        // sender's own subscriptions, so FollowPath's own PartyMoveEvent never triggers this — only
        // a keyboard/other-source move does.
        On<UAlbion.Formats.ScriptEvents.PartyMoveEvent>(_ => _path.Clear());
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
        if ((cx == lx && cy == ly) || cx < 0 || cy < 0 || cx >= _map.Width || cy >= _map.Height)
            return;

        e.Propagating = false;
        StartGoto(cx, cy);
    }

    // Faithful 2D click-to-walk: route to the target tile with A* (8-directional, walkability
    // via the registered Collider2D). FollowPath then steps the party tile-by-tile each tick.
    // If no route exists (unreachable / off-map), fall back to a single greedy step so a click
    // still nudges the party (the original behaviour before pathing). Also reachable via the
    // party_goto event (the harness's organic-walk primitive).
    void StartGoto(int cx, int cy)
    {
        var leader = TryResolve<IParty>()?.Leader;
        if (leader == null)
            return;

        var pos = leader.GetPosition();
        int lx = (int)MathF.Round(pos.X), ly = (int)MathF.Round(pos.Y);
        if ((cx == lx && cy == ly) || cx < 0 || cy < 0 || cx >= _map.Width || cy >= _map.Height)
            return;

        var path = FindPath(lx, ly, cx, cy);
        _path.Clear();
        _stuckTicks = 0;
        if (path != null && path.Count > 0)
        {
            _path.AddRange(path);
            return;
        }

        int dx = Math.Sign(cx - lx), dy = Math.Sign(cy - ly);
        if (dx != 0 || dy != 0)
            Raise(new UAlbion.Formats.ScriptEvents.PartyMoveEvent(dx, dy)); // +y = south
    }

    // Step the party toward the next waypoint each tick. Issues a PartyMoveEvent (a per-tick
    // direction intent the caterpillar integrates) toward the next path tile; pops the waypoint
    // when reached. A keyboard move or a new click replaces/ends the path. A stuck-watchdog
    // abandons the path if the party makes no tile progress for a while (e.g. an NPC blocks it).
    void FollowPath()
    {
        if (_path.Count == 0)
            return;

        var leader = TryResolve<IParty>()?.Leader;
        if (leader == null) { _path.Clear(); return; }

        var pos = leader.GetPosition();
        int lx = (int)MathF.Round(pos.X), ly = (int)MathF.Round(pos.Y);

        // Stuck detection: no tile change for ~40 ticks → give up on the path.
        if (lx == _lastTileX && ly == _lastTileY)
        {
            if (++_stuckTicks > 40) { _path.Clear(); _stuckTicks = 0; return; }
        }
        else { _stuckTicks = 0; _lastTileX = lx; _lastTileY = ly; }

        // Drop any waypoints we've already reached (including the immediate one).
        while (_path.Count > 0 && _path[0].X == lx && _path[0].Y == ly)
            _path.RemoveAt(0);
        if (_path.Count == 0)
            return;

        int dx = Math.Sign(_path[0].X - lx), dy = Math.Sign(_path[0].Y - ly);
        if (dx == 0 && dy == 0)
            return;
        Raise(new UAlbion.Formats.ScriptEvents.PartyMoveEvent(dx, dy));
    }

    // A* over the tile grid (8-connected). Walkability per directed edge comes from the registered
    // ICollisionManager (Collider2D), so it respects map passability exactly as keyboard movement
    // does. Returns the waypoint list (excluding the start), or null if unreachable. Bounded node
    // budget so a huge open map can't stall a click.
    List<(int X, int Y)> FindPath(int sx, int sy, int gx, int gy)
    {
        var detector = TryResolve<ICollisionManager>();
        if (detector == null)
            return null;

        const int MaxNodes = 20000;
        var open = new PriorityQueue<(int X, int Y), int>();
        var cameFrom = new Dictionary<(int, int), (int, int)>();
        var gScore = new Dictionary<(int, int), int> { [(sx, sy)] = 0 };
        open.Enqueue((sx, sy), Heuristic(sx, sy, gx, gy));
        int expanded = 0;

        while (open.Count > 0 && expanded++ < MaxNodes)
        {
            var cur = open.Dequeue();
            if (cur.X == gx && cur.Y == gy)
                return Reconstruct(cameFrom, cur);

            for (int ddy = -1; ddy <= 1; ddy++)
            for (int ddx = -1; ddx <= 1; ddx++)
            {
                if (ddx == 0 && ddy == 0) continue;
                int nx = cur.X + ddx, ny = cur.Y + ddy;
                if (nx < 0 || ny < 0 || nx >= _map.Width || ny >= _map.Height) continue;
                if (detector.IsOccupied(cur.X, cur.Y, nx, ny)) continue;
                // Diagonal: require both orthogonal neighbours open so we don't cut corners.
                if (ddx != 0 && ddy != 0 &&
                    (detector.IsOccupied(cur.X, cur.Y, cur.X + ddx, cur.Y) ||
                     detector.IsOccupied(cur.X, cur.Y, cur.X, cur.Y + ddy)))
                    continue;

                int tentative = gScore[(cur.X, cur.Y)] + ((ddx != 0 && ddy != 0) ? 14 : 10);
                var nkey = (nx, ny);
                if (gScore.TryGetValue(nkey, out var g) && tentative >= g)
                    continue;
                gScore[nkey] = tentative;
                cameFrom[nkey] = (cur.X, cur.Y);
                open.Enqueue((nx, ny), tentative + Heuristic(nx, ny, gx, gy));
            }
        }
        return null;
    }

    static int Heuristic(int x, int y, int gx, int gy)
    {
        int dx = Math.Abs(x - gx), dy = Math.Abs(y - gy);
        return 10 * (dx + dy) - 6 * Math.Min(dx, dy); // octile distance ×10
    }

    static List<(int X, int Y)> Reconstruct(Dictionary<(int, int), (int, int)> cameFrom, (int X, int Y) cur)
    {
        var path = new List<(int X, int Y)>();
        while (cameFrom.TryGetValue((cur.X, cur.Y), out var prev))
        {
            path.Add(cur);
            cur = prev;
        }
        path.Reverse();
        return path;
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

        // #36: reach limits (RE'd from MAIN.EXE fcn.0001f70a). The original gates each verb by the
        // rounded Euclidean distance from the party to the clicked tile: touch (Manipulate/Take/
        // UseItem) 2 tiles, TalkTo 3, Examine 4. Beyond that the verb wasn't actionable. Configurable
        // via Game.UI.ContextMenuReachLimit (off = interact with any visible tile, the old behaviour).
        bool reachLimit = ReadVar(V.Game.Ui.ContextMenuReachLimit);
        double reachDist = 0;
        if (reachLimit)
        {
            var leaderPos = Resolve<IParty>().Leader?.GetPosition() ?? new Vector3(x, y, 0);
            double rdx = x - leaderPos.X, rdy = y - leaderPos.Y;
            reachDist = Math.Round(Math.Sqrt(rdx * rdx + rdy * rdy));
        }
        bool InReach(int maxTiles) => !reachLimit || reachDist <= maxTiles;

        if (zone?.Chain != null && zone.Node != null)
        {
            if ((zone.Trigger & TriggerTypes.Examine) != 0 && InReach(4))
            {
                options.Add(new ContextMenuOption(
                    S(Base.SystemText.MapPopup_Examine),
                    new TriggerMapTileEvent(TriggerType.Examine, zone.X, zone.Y),
                    ContextMenuGroup.Actions));
            }

            if ((zone.Trigger & TriggerTypes.Manipulate) != 0 && InReach(2))
            {
                options.Add(new ContextMenuOption(
                    S(Base.SystemText.MapPopup_Manipulate),
                    new TriggerMapTileEvent(TriggerType.Manipulate, zone.X, zone.Y),
                    ContextMenuGroup.Actions));
            }

            if ((zone.Trigger & TriggerTypes.Take) != 0 && InReach(2))
            {
                options.Add(new ContextMenuOption(
                    S(Base.SystemText.MapPopup_Take),
                    new TriggerMapTileEvent(TriggerType.Take, zone.X, zone.Y),
                    ContextMenuGroup.Actions));
            }

            if ((zone.Trigger & TriggerTypes.TalkTo) != 0 && InReach(3))
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
            if ((zone.Trigger & TriggerTypes.UseItem) != 0 && InReach(2)
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
