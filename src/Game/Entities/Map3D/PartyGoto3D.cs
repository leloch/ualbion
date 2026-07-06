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
    int _repaths;
    int _goalX = -1, _goalY = -1;
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
        _repaths = 0;
        _goalX = tx;
        _goalY = ty;
        Repath(tx, ty);
    }

    void Repath(int tx, int ty)
    {
        _path.Clear();
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

        // Weighted (Dijkstra) route: geometry is impassable, but a tile occupied by an NPC
        // BODY is passable at HIGH cost. So the path always completes, yet strongly prefers
        // to detour around live NPC bodies — a chaser parked on the party's next tile gets
        // routed around (via a body-free neighbour) instead of wedging the glide against its
        // AABB, WITHOUT the all-or-nothing failure of a hard body-avoiding pass (one far
        // wanderer on the only corridor would otherwise force the whole route back through
        // the near blocker). The body may also move by the time the glide arrives.
        const int BodyCost = 1000;
        var dist = new Dictionary<int, int> { [Key(sx, sy)] = 0 };
        var prev = new Dictionary<int, int> { [Key(sx, sy)] = -1 };
        var pq = new PriorityQueue<int, int>();
        pq.Enqueue(Key(sx, sy), 0);
        Span<int> ddx = [1, -1, 0, 0];
        Span<int> ddy = [0, 0, 1, -1];
        bool found = false;
        while (pq.TryDequeue(out int cur, out int curCost))
        {
            if (curCost > dist[cur]) continue;
            int cx = cur % w, cy = cur / w;
            if (cx == tx && cy == ty) { found = true; break; }
            for (int i = 0; i < 4; i++)
            {
                int nx = cx + ddx[i], ny = cy + ddy[i];
                if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                if (detector.IsOccupied(cx, cy, nx, ny)) continue;
                int step = (nx == tx && ny == ty) || !detector.HitsObjectAt(nx + 0.5f, ny + 0.5f, 0) ? 1 : BodyCost;
                int nk = Key(nx, ny), nd = curCost + step;
                if (dist.TryGetValue(nk, out int old) && old <= nd) continue;
                dist[nk] = nd;
                prev[nk] = cur;
                pq.Enqueue(nk, nd);
            }
        }

        if (!found)
        {
            Info($"[Goto3D] no path from ({sx},{sy}) to ({tx},{ty})");
            return;
        }
        var route = prev;

        for (int k = Key(tx, ty); k != Key(sx, sy); k = route[k])
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

        // Stuck watchdog: no progress for ~20 ticks (e.g. a chasing NPC body-blocking the
        // next tile). RE-PATH first — the blocker (a wandering/chasing NPC) has usually
        // moved, so a fresh BFS from the current tile routes around it. Only abandon after
        // several failed re-paths (genuinely walled in), so a follower can't soft-lock a walk.
        if (Vector2.Distance(cur, _lastPos) < 0.002f)
        {
            if (++_stuckTicks > 20)
            {
                _stuckTicks = 0;
                if (_repaths++ < 4 && _goalX >= 0)
                {
                    Info($"[Goto3D] stuck, re-pathing (attempt {_repaths})");
                    Repath(_goalX, _goalY);
                    if (_path.Count > 0) { _lastPos = cur; return; }
                }
                Info("[Goto3D] stuck, abandoning path");
                _path.Clear();
                return;
            }
        }
        else { _stuckTicks = 0; _repaths = 0; }
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

        // Run the same one-step arbiter as manual movement (walls/margins/objects) so the
        // glide can't clip a pylon the BFS path passes near.
        var detector = TryResolve<ICollisionManager>();
        if (detector != null)
        {
            float ts = TryResolve<IMapManager>()?.Current?.TileSize.X ?? 512f;
            float margin = MathF.Max(ts / 4f, 50f) / ts;
            float dx = dir.X * Speed * 0.05f, dz = dir.Y * Speed * 0.05f;
            if (!Collision3DStep.IsAllowed(detector, cur.X, cur.Y, dx, dz, margin, 0))
            {
                // Try the axis-slide like the manual mover; if fully blocked, let the stuck
                // watchdog abandon the path.
                if (Collision3DStep.IsAllowed(detector, cur.X, cur.Y, dx, 0f, margin, 0))
                    dir = new Vector2(MathF.Sign(dir.X), 0f);
                else if (Collision3DStep.IsAllowed(detector, cur.X, cur.Y, 0f, dz, margin, 0))
                    dir = new Vector2(0f, MathF.Sign(dir.Y));
                else
                    return;
            }
        }

        // Face the direction of travel (Movement3D.OnTurn yaw convention: N=0 looks along -Z,
        // so +Z = South, +X = East).
        var facing = MathF.Abs(dir.X) > MathF.Abs(dir.Y)
            ? (dir.X > 0 ? Direction.East : Direction.West)
            : (dir.Y > 0 ? Direction.South : Direction.North);
        Raise(new PartyTurnEvent(facing));
        Raise(new CameraMove3DWorldEvent(dir.X * Speed, dir.Y * Speed));
    }
}
