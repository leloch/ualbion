using System;
using System.Collections.Generic;
using System.Numerics;
using UAlbion.Api.Eventing;
using UAlbion.Formats;
using UAlbion.Formats.ScriptEvents;
using UAlbion.Game.Events;
using UAlbion.Game.State;

namespace UAlbion.Game.Entities.Map3D;

/// <summary>
/// 3D counterpart of the 2D click-to-walk for the party_goto event (the harness's organic-walk
/// primitive): BFS over the collision grid (4-connected, per-directed-edge passability via
/// ICollisionManager — the same test manual movement uses, so a found path is walkable by
/// construction), then glide the party through the waypoint tile centres each tick via
/// CameraMove3DWorldEvent (the same integration path as manual movement). The party snap-faces
/// along each leg so facing-sensitive zones and the automap behave as if the player walked it.
/// </summary>
public class PartyGoto3D : Component
{
    const float Speed = 3.0f;      // tiles/sec — slightly under manual ForwardSpeed (4.0)
    const float ArriveDist = 0.2f; // tiles

    readonly List<(int X, int Y)> _path = new();
    int _stuckTicks;
    Vector2 _lastPos;

    public PartyGoto3D()
    {
        On<PartyGotoEvent>(e => StartGoto(e.X, e.Y));
        On<FastClockEvent>(_ => Follow());
    }

    void StartGoto(int tx, int ty)
    {
        _path.Clear();
        _stuckTicks = 0;
        var leader = TryResolve<IParty>()?.Leader;
        var detector = TryResolve<ICollisionManager>();
        var map = TryResolve<IMapManager>()?.Current;
        if (leader == null || detector == null || map?.MapData == null)
            return;

        var pos = leader.GetPosition();
        int sx = (int)MathF.Floor(pos.X), sy = (int)MathF.Floor(pos.Z);
        int w = map.MapData.Width, h = map.MapData.Height;
        if (tx < 0 || ty < 0 || tx >= w || ty >= h || (sx == tx && sy == ty))
            return;

        int Key(int x, int y) => y * w + x;
        var prev = new Dictionary<int, int> { [Key(sx, sy)] = -1 };
        var queue = new Queue<int>();
        queue.Enqueue(Key(sx, sy));
        bool found = false;
        Span<int> dx = [1, -1, 0, 0];
        Span<int> dy = [0, 0, 1, -1];
        while (queue.Count > 0)
        {
            int cur = queue.Dequeue();
            int cx = cur % w, cy = cur / w;
            if (cx == tx && cy == ty) { found = true; break; }
            for (int i = 0; i < 4; i++)
            {
                int nx = cx + dx[i], ny = cy + dy[i];
                if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                int nk = Key(nx, ny);
                if (prev.ContainsKey(nk)) continue;
                if (detector.IsOccupied(cx, cy, nx, ny)) continue;
                prev[nk] = cur;
                queue.Enqueue(nk);
            }
        }

        if (!found)
        {
            Info($"[Goto3D] no path from ({sx},{sy}) to ({tx},{ty})");
            return;
        }

        for (int k = Key(tx, ty); k != Key(sx, sy); k = prev[k])
            _path.Insert(0, (k % w, k / w));
        Info($"[Goto3D] pathing ({sx},{sy}) -> ({tx},{ty}): {_path.Count} tiles");
    }

    void Follow()
    {
        if (_path.Count == 0)
            return;
        var leader = TryResolve<IParty>()?.Leader;
        if (leader == null) { _path.Clear(); return; }

        var pos = leader.GetPosition();
        var cur = new Vector2(pos.X, pos.Z);

        // Stuck watchdog: no progress for ~60 ticks (e.g. an NPC blocking) → abandon.
        if (Vector2.Distance(cur, _lastPos) < 0.002f)
        {
            if (++_stuckTicks > 60)
            {
                Info("[Goto3D] stuck, abandoning path");
                _path.Clear();
                _stuckTicks = 0;
                return;
            }
        }
        else _stuckTicks = 0;
        _lastPos = cur;

        var target = new Vector2(_path[0].X + 0.5f, _path[0].Y + 0.5f);
        if (Vector2.Distance(cur, target) < ArriveDist)
        {
            _path.RemoveAt(0);
            if (_path.Count == 0)
            {
                Info("[Goto3D] arrived");
                return;
            }
            target = new Vector2(_path[0].X + 0.5f, _path[0].Y + 0.5f);
        }

        var dir = Vector2.Normalize(target - cur);

        // Face the direction of travel (Movement3D.OnTurn yaw convention: N=0 looks along -Z,
        // so +Z = South, +X = East).
        var facing = MathF.Abs(dir.X) > MathF.Abs(dir.Y)
            ? (dir.X > 0 ? Direction.East : Direction.West)
            : (dir.Y > 0 ? Direction.South : Direction.North);
        Raise(new PartyTurnEvent(facing));
        Raise(new CameraMove3DWorldEvent(dir.X * Speed, dir.Y * Speed));
    }
}
