using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using UAlbion.Api.Eventing;
using UAlbion.Core;
using UAlbion.Core.Events;
using UAlbion.Core.Veldrid.Events;
using UAlbion.Formats.Assets.Sheets;
using UAlbion.Game.Events;
using UAlbion.Game.Gui;
using UAlbion.Game.State;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Veldrid;
using Veldrid.Sdl2;

namespace UAlbion.Game.Veldrid.Diag;

/// <summary>
/// HTTP remote-control harness for autonomous test agents and CI smoke runs. Listens on
/// <c>http://localhost:&lt;port&gt;/</c>; lets external tooling inject events, observe game
/// state, enumerate UI elements, click by stable ID or screen coordinates, and capture
/// screenshots. See <c>_RE_NOTES.md → harness</c> for the protocol.
/// </summary>
/// <remarks>
/// Threading model:
/// <list type="bullet">
///   <item>The HttpListener acceptor thread accepts connections and enqueues
///     <see cref="PendingRequest"/> records onto a thread-safe queue.</item>
///   <item>Each <see cref="EngineUpdateEvent"/> (fired every frame from
///     <c>Engine.InnerLoop()</c>) the main thread drains the queue and runs the handlers,
///     so all game-state access happens on the game thread — no locks needed in handlers.</item>
///   <item>Each <see cref="PendingRequest"/> carries a <see cref="TaskCompletionSource"/>
///     that the game thread completes; the acceptor thread then writes the HTTP response.</item>
/// </list>
///
/// Endpoints:
/// <code>
///   GET  /healthz                       → { ok, fps, frame }
///   GET  /state                         → JSON snapshot (map, party, dialogs, ...)
///   GET  /ui                            → list of visible UI elements with bounds
///   POST /event/raw   "load_game 7"     → fire any UAlbion event by its `-c` text form
///   POST /event       { name, args }    → same, but JSON-structured
///   POST /click       { id }            → synthetic click on UI element by stable ID
///   POST /click/at    { x, y, button? } → synthetic click at screen coordinates
///   POST /screenshot                    → image/png of the current frame (501 if unavailable)
///   POST /quit                          → graceful shutdown
/// </code>
/// </remarks>
public sealed class HarnessHttpServer : Component, IDisposable
{
    sealed class PendingRequest
    {
        public HttpListenerContext Context { get; init; }
        public TaskCompletionSource<bool> Done { get; } = new();
    }

    readonly int _port;
    readonly HttpListener _listener;
    readonly ConcurrentQueue<PendingRequest> _queue = new();
    readonly ConcurrentQueue<HttpListenerContext> _pendingScreenshots = new();
    readonly CancellationTokenSource _shutdownCts = new();
    long _frameCount;
    DateTime _lastFrameTime = DateTime.UtcNow;
    double _smoothedFps;
    string _lastError;
    int _commandsProcessed;
    bool _disposed;

    public HarnessHttpServer(int port)
    {
        _port = port;
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{port}/");
        _listener.Start();
        Task.Run(AcceptLoopAsync);

        On<EngineUpdateEvent>(_ => Pump());
        On<PreSwapBuffersEvent>(_ => FulfillScreenshots());
    }

    async Task AcceptLoopAsync()
    {
        while (!_shutdownCts.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync().ConfigureAwait(false); }
            catch (HttpListenerException) { break; }
            catch (ObjectDisposedException) { break; }

            var req = new PendingRequest { Context = ctx };
            _queue.Enqueue(req);
        }
    }

    void Pump()
    {
        _frameCount++;
        var now = DateTime.UtcNow;
        var dt = (now - _lastFrameTime).TotalSeconds;
        _lastFrameTime = now;
        if (dt > 0)
        {
            double inst = 1.0 / dt;
            _smoothedFps = _smoothedFps == 0 ? inst : _smoothedFps * 0.9 + inst * 0.1;
        }

        // Drain at most 32 requests per frame so a burst can't starve the engine.
        int budget = 32;
        while (budget-- > 0 && _queue.TryDequeue(out var req))
        {
            bool deferred = false;
            try
            {
                deferred = IsScreenshotRequest(req.Context);
                Handle(req.Context);
            }
            catch (Exception ex)
            {
                _lastError = $"{ex.GetType().Name}: {ex.Message}";
                TryWriteError(req.Context, HttpStatusCode.InternalServerError, _lastError);
            }
            finally
            {
                // Screenshot requests are queued to PreSwapBuffersEvent — don't close them
                // here or the client never sees the PNG. FulfillScreenshots() owns the close.
                if (!deferred)
                {
                    try { req.Context.Response.Close(); } catch { /* client may have disconnected */ }
                }
                req.Done.TrySetResult(true);
            }
        }
    }

    static bool IsScreenshotRequest(HttpListenerContext ctx)
        => ctx.Request.HttpMethod == "POST" && ctx.Request.Url?.AbsolutePath == "/screenshot";

    void Handle(HttpListenerContext ctx)
    {
        var path = ctx.Request.Url?.AbsolutePath ?? "/";
        var method = ctx.Request.HttpMethod;

        switch (method + " " + path)
        {
            case "GET /healthz":         WriteJson(ctx, Healthz()); break;
            case "GET /state":           WriteJson(ctx, BuildState()); break;
            case "GET /ui":              WriteJson(ctx, BuildUiTree()); break;
            case "GET /labyrinth":       WriteJson(ctx, BuildLabyrinthDump()); break;
            case "GET /camera":          WriteJson(ctx, BuildCameraDump()); break;
            case "GET /tilemap":         WriteJson(ctx, BuildTilemapDump()); break;
            case "GET /wallpixels":      WriteJson(ctx, BuildWallPixelsDump(ctx)); break;
            case "POST /event/raw":      HandleEventRaw(ctx); break;
            case "POST /event":          HandleEventJson(ctx); break;
            case "POST /click":          HandleClickById(ctx); break;
            case "POST /click/at":       HandleClickAt(ctx); break;
            case "POST /screenshot":     HandleScreenshot(ctx); break;
            case "POST /quit":           HandleQuit(ctx); break;
            default:                     TryWriteError(ctx, HttpStatusCode.NotFound, $"unknown route: {method} {path}"); break;
        }
    }

    // --- Endpoint handlers ---------------------------------------------------------

    string Healthz()
    {
        var sb = new StringBuilder();
        sb.Append('{');
        sb.Append("\"ok\":true,");
        sb.Append($"\"fps\":{_smoothedFps.ToString("F2", CultureInfo.InvariantCulture)},");
        sb.Append($"\"frame\":{_frameCount},");
        sb.Append($"\"commandsProcessed\":{_commandsProcessed},");
        sb.Append($"\"lastError\":{JsonString(_lastError)}");
        sb.Append('}');
        return sb.ToString();
    }

    string BuildState()
    {
        var sb = new StringBuilder();
        sb.Append('{');
        var state = TryResolve<IGameState>();
        var party = TryResolve<IParty>();

        // IGameState.MapId / Time / TickCount throw NRE pre-load because they deref the
        // internal SavedGame ref. Gate on Loaded to keep /state callable on the main menu.
        bool loaded = state?.Loaded == true;
        sb.Append($"\"loaded\":{(loaded ? "true" : "false")},");
        if (loaded)
        {
            sb.Append($"\"map\":{JsonString(state.MapId.ToString())},");
            sb.Append($"\"time\":{JsonString(state.Time.ToString("O"))},");
            sb.Append($"\"tickCount\":{state.TickCount},");
        }
        else
        {
            sb.Append("\"map\":null,\"time\":null,\"tickCount\":0,");
        }

        var leader = party?.Leader;
        if (leader != null)
        {
            var pos = leader.GetPosition();
            sb.Append("\"leader\":{");
            sb.Append($"\"id\":{JsonString(leader.Id.ToString())},");
            sb.Append($"\"x\":{F(pos.X)},\"y\":{F(pos.Y)},\"z\":{F(pos.Z)}");
            sb.Append("},");
        }
        else sb.Append("\"leader\":null,");

        sb.Append("\"party\":[");
        bool first = true;
        if (party != null)
        {
            foreach (var pm in party.StatusBarOrder)
            {
                if (pm?.Apparent?.Combat == null) continue;
                if (!first) sb.Append(',');
                first = false;
                var c = pm.Apparent.Combat;
                sb.Append('{');
                sb.Append($"\"id\":{JsonString(pm.Id.ToString())},");
                sb.Append($"\"hp\":{c.LifePoints?.Current ?? 0},");
                sb.Append($"\"hpMax\":{c.LifePoints?.Max ?? 0},");
                var sp = pm.Apparent.Magic?.SpellPoints;
                sb.Append($"\"sp\":{sp?.Current ?? 0},");
                sb.Append($"\"spMax\":{sp?.Max ?? 0},");
                sb.Append($"\"conditions\":{JsonString(c.Conditions.ToString())},");
                sb.Append($"\"level\":{pm.Apparent.Level}");
                sb.Append('}');
            }
        }
        sb.Append("],");

        sb.Append($"\"fps\":{_smoothedFps.ToString("F2", CultureInfo.InvariantCulture)},");
        sb.Append($"\"frame\":{_frameCount},");
        sb.Append($"\"commandsProcessed\":{_commandsProcessed},");
        sb.Append($"\"lastError\":{JsonString(_lastError)}");
        sb.Append('}');
        return sb.ToString();
    }

    string BuildLabyrinthDump()
    {
        var mapMgr = TryResolve<UAlbion.Game.IMapManager>();
        var map = mapMgr?.Current;
        if (map == null) return "{\"loaded\":false}";

        var sb = new StringBuilder();
        sb.Append('{');
        sb.Append($"\"mapId\":{JsonString(map.MapId.ToString())},");
        sb.Append($"\"mapType\":{JsonString(map.MapType.ToString())},");
        sb.Append($"\"tileSizeX\":{F(map.TileSize.X)},");
        sb.Append($"\"tileSizeY\":{F(map.TileSize.Y)},");
        sb.Append($"\"tileSizeZ\":{F(map.TileSize.Z)},");
        sb.Append($"\"baseCameraHeight\":{F(map.BaseCameraHeight)}");

        // Cast to DungeonMap to pull labyrinth-specific fields if we're in a 3D map.
        if (map is UAlbion.Game.Entities.Map3D.DungeonMap dungeon)
        {
            sb.Append(',');
            // Use reflection to read the private _labyrinthData field (we're outside the
            // assembly that owns it). Falling back to public TileSize / BaseCameraHeight
            // if reflection fails. Strictly diagnostic — fine to be loose here.
            try
            {
                var field = typeof(UAlbion.Game.Entities.Map3D.DungeonMap)
                    .GetField("_labyrinthData", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var lab = field?.GetValue(dungeon) as UAlbion.Formats.Assets.Labyrinth.LabyrinthData;
                if (lab != null)
                {
                    sb.Append($"\"wallHeight\":{lab.WallHeight},");
                    sb.Append($"\"cameraHeight\":{lab.CameraHeight},");
                    sb.Append($"\"wallWidth\":{lab.WallWidth},");
                    sb.Append($"\"effectiveWallWidth\":{lab.EffectiveWallWidth},");
                    sb.Append($"\"backgroundColour\":{lab.BackgroundColour},");
                    sb.Append($"\"fogDistance\":{lab.FogDistance},");
                    sb.Append($"\"fogR\":{lab.FogRed},\"fogG\":{lab.FogGreen},\"fogB\":{lab.FogBlue},");
                    sb.Append($"\"fogMode\":{lab.FogMode},");
                    sb.Append($"\"maxLight\":{lab.MaxLight},");
                    sb.Append($"\"lighting\":{lab.Lighting},");
                    sb.Append($"\"maxVisibleTiles\":{lab.MaxVisibleTiles},");
                    sb.Append($"\"backgroundYPosition\":{lab.BackgroundYPosition},");
                    sb.Append($"\"backgroundTileAmount\":{lab.BackgroundTileAmount},");
                    sb.Append($"\"wallCount\":{lab.Walls?.Count ?? 0},");
                    sb.Append($"\"floorCount\":{lab.FloorAndCeilings?.Count ?? 0},");
                    sb.Append($"\"objectGroupCount\":{lab.ObjectGroups?.Count ?? 0},");
                    sb.Append($"\"objectCount\":{lab.Objects?.Count ?? 0},");
                    sb.Append($"\"objectYScaling\":{F(lab.ObjectYScaling)},");
                    sb.Append($"\"fogColorPacked\":{lab.FogColor}");
                }
                else
                {
                    sb.Append("\"labyrinth\":\"not-accessible\"");
                }
            }
            catch (Exception ex)
            {
                sb.Append($"\"labyrinthError\":{JsonString(ex.Message)}");
            }
        }

        sb.Append('}');
        return sb.ToString();
    }

    string BuildTilemapDump()
    {
        // Walk the EtmManager's children to find the active ExtrudedTilemap and dump its
        // texture atlas dimensions. This tells us whether the wall texture regions have
        // sensible sizes vs being collapsed to 1×N or having TexSize=(1,1) bugs.
        var etmMgr = TryResolve<UAlbion.Core.Visual.IEtmManager>();
        if (etmMgr == null)
            return "{\"error\":\"no IEtmManager\"}";

        // Children is protected on Component — reach it via reflection. Diagnostic-only.
        var childrenField = etmMgr.GetType().BaseType?.BaseType?.GetField("_children",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? typeof(UAlbion.Api.Eventing.Component).GetProperty("Children",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic) as object;
        System.Collections.IEnumerable children = null;
        if (childrenField is System.Reflection.FieldInfo fi)
            children = fi.GetValue(etmMgr) as System.Collections.IEnumerable;
        else if (childrenField is System.Reflection.PropertyInfo pi)
            children = pi.GetValue(etmMgr) as System.Collections.IEnumerable;
        if (children == null)
            return "{\"error\":\"could not reflect Children\"}";

        var sb = new StringBuilder();
        sb.Append("{\"tilemaps\":[");
        bool firstTm = true;
        foreach (var child in children)
        {
            if (child is not UAlbion.Core.Veldrid.Etm.ExtrudedTilemap tilemap) continue;
            if (!firstTm) sb.Append(',');
            firstTm = false;

            sb.Append('{');
            sb.Append($"\"name\":{JsonString(tilemap.Name)},");
            sb.Append($"\"tileCount\":{tilemap.TileCount},");

            // DayWalls atlas: dimensions + per-region size sample
            var walls = tilemap.DayWalls;
            if (walls != null)
            {
                sb.Append($"\"wallsAtlas\":{{\"w\":{walls.Width},\"h\":{walls.Height},\"layers\":{walls.ArrayLayers},\"regionCount\":{walls.Regions.Count}}},");
                sb.Append("\"firstWallRegions\":[");
                int show = Math.Min(8, walls.Regions.Count);
                for (int i = 0; i < show; i++)
                {
                    if (i > 0) sb.Append(',');
                    var r = walls.Regions[i];
                    sb.Append($"{{\"x\":{r.X},\"y\":{r.Y},\"w\":{r.Width},\"h\":{r.Height},\"texSize\":[{F(r.TexSize.X)},{F(r.TexSize.Y)}],\"layer\":{r.Layer}}}");
                }
                sb.Append("],");
            }

            var floors = tilemap.DayFloors;
            if (floors != null)
            {
                sb.Append($"\"floorsAtlas\":{{\"w\":{floors.Width},\"h\":{floors.Height},\"layers\":{floors.ArrayLayers},\"regionCount\":{floors.Regions.Count}}}");
            }

            sb.Append('}');
        }
        sb.Append("]}");
        return sb.ToString();
    }

    /// <summary>
    /// Dump a small sample of raw pixel data from a wall texture layer to verify the
    /// asset-loading path. Query string: ?layer=N&amp;w=8&amp;h=8 (defaults: layer=1, 8×8).
    /// Returns hex-encoded ARGB pixels in row-major order plus column/row statistics.
    /// </summary>
    string BuildWallPixelsDump(HttpListenerContext ctx)
    {
        var query = ctx.Request.QueryString;
        int wantLayer = int.TryParse(query["layer"], out var lv) && lv > 0 ? lv : 1;
        int sampleW = int.TryParse(query["w"], out var wv) && wv > 0 ? wv : 8;
        int sampleH = int.TryParse(query["h"], out var hv) && hv > 0 ? hv : 8;

        var etmMgr = TryResolve<UAlbion.Core.Visual.IEtmManager>();
        if (etmMgr == null)
            return "{\"error\":\"no IEtmManager\"}";

        var childrenField = typeof(UAlbion.Api.Eventing.Component).GetField("_children",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var children = childrenField?.GetValue(etmMgr) as System.Collections.IEnumerable;
        if (children == null)
            return "{\"error\":\"could not reflect Children\"}";

        UAlbion.Core.Veldrid.Etm.ExtrudedTilemap tilemap = null;
        foreach (var c in children)
            if (c is UAlbion.Core.Veldrid.Etm.ExtrudedTilemap tm) { tilemap = tm; break; }
        if (tilemap == null) return "{\"error\":\"no active ExtrudedTilemap\"}";

        var walls = tilemap.DayWalls;
        if (walls == null) return "{\"error\":\"no DayWalls\"}";
        if (wantLayer >= walls.ArrayLayers)
            return $"{{\"error\":\"layer {wantLayer} out of range (max {walls.ArrayLayers - 1})\"}}";

        var buffer = walls.GetLayerBuffer(wantLayer);
        if (buffer.Buffer.Length == 0)
            return "{\"error\":\"empty layer buffer\"}";

        int actualSampleW = Math.Min(sampleW, buffer.Width);
        int actualSampleH = Math.Min(sampleH, buffer.Height);

        var sb = new StringBuilder();
        sb.Append('{');
        sb.Append($"\"layer\":{wantLayer},");
        sb.Append($"\"bufferWidth\":{buffer.Width},");
        sb.Append($"\"bufferHeight\":{buffer.Height},");
        sb.Append($"\"bufferStride\":{buffer.Stride},");
        sb.Append($"\"sampleW\":{actualSampleW},");
        sb.Append($"\"sampleH\":{actualSampleH},");

        // Centre-pixel sample to compare with shader-side sampling at UV (0.5, 0.5)
        int cx = buffer.Width / 2;
        int cy = buffer.Height / 2;
        int cidx = cy * buffer.Stride + cx;
        uint centrePixel = cidx < buffer.Buffer.Length ? buffer.Buffer[cidx] : 0;
        sb.Append($"\"centrePixel\":\"{centrePixel:X8}\",");

        // Sample top-left corner pixels
        sb.Append("\"topLeft\":[");
        for (int y = 0; y < actualSampleH; y++)
        {
            if (y > 0) sb.Append(',');
            sb.Append('[');
            for (int x = 0; x < actualSampleW; x++)
            {
                if (x > 0) sb.Append(',');
                int idx = y * buffer.Stride + x;
                uint pixel = idx < buffer.Buffer.Length ? buffer.Buffer[idx] : 0;
                sb.Append($"\"{pixel:X8}\"");
            }
            sb.Append(']');
        }
        sb.Append("],");

        // Compute per-column "unique color count" stat — if walls are vertical stripes
        // (one solid color per column), each column would have only 1 unique color.
        var colSet = new System.Collections.Generic.HashSet<uint>();
        int distinctColsWithSingle = 0;
        for (int x = 0; x < buffer.Width; x++)
        {
            colSet.Clear();
            for (int y = 0; y < buffer.Height; y++)
            {
                int idx = y * buffer.Stride + x;
                if (idx >= buffer.Buffer.Length) break;
                colSet.Add(buffer.Buffer[idx]);
            }
            if (colSet.Count == 1) distinctColsWithSingle++;
        }
        sb.Append($"\"columnsWithSingleColor\":{distinctColsWithSingle},");
        sb.Append($"\"totalColumns\":{buffer.Width},");

        // Per-row uniformity check
        int distinctRowsWithSingle = 0;
        for (int y = 0; y < buffer.Height; y++)
        {
            colSet.Clear();
            for (int x = 0; x < buffer.Width; x++)
            {
                int idx = y * buffer.Stride + x;
                if (idx >= buffer.Buffer.Length) break;
                colSet.Add(buffer.Buffer[idx]);
            }
            if (colSet.Count == 1) distinctRowsWithSingle++;
        }
        sb.Append($"\"rowsWithSingleColor\":{distinctRowsWithSingle},");
        sb.Append($"\"totalRows\":{buffer.Height},");

        // Count distinct colors total
        var allColors = new System.Collections.Generic.HashSet<uint>();
        for (int i = 0; i < buffer.Buffer.Length; i++)
            allColors.Add(buffer.Buffer[i]);
        sb.Append($"\"distinctColors\":{allColors.Count}");

        sb.Append('}');
        return sb.ToString();
    }

    string BuildCameraDump()
    {
        var camera = TryResolve<UAlbion.Core.Visual.ICamera>();
        if (camera == null) return "{\"camera\":null}";

        var pos = camera.Position;
        var look = camera.LookDirection;
        var sb = new StringBuilder();
        sb.Append('{');
        sb.Append($"\"position\":{{\"x\":{F(pos.X)},\"y\":{F(pos.Y)},\"z\":{F(pos.Z)}}},");
        sb.Append($"\"lookDir\":{{\"x\":{F(look.X)},\"y\":{F(look.Y)},\"z\":{F(look.Z)}}},");
        sb.Append($"\"yaw\":{F(camera.Yaw)},");
        sb.Append($"\"pitch\":{F(camera.Pitch)},");
        sb.Append($"\"viewport\":{{\"w\":{F(camera.Viewport.X)},\"h\":{F(camera.Viewport.Y)}}},");
        sb.Append($"\"nearDistance\":{F(camera.NearDistance)},");
        sb.Append($"\"farDistance\":{F(camera.FarDistance)},");
        sb.Append($"\"fieldOfView\":{F(camera.FieldOfView)},");
        sb.Append($"\"aspectRatio\":{F(camera.AspectRatio)},");
        sb.Append($"\"magnification\":{F(camera.Magnification)}");
        sb.Append('}');
        return sb.ToString();
    }

    string BuildUiTree()
    {
        var layoutMgr = TryResolve<ILayoutManager>();
        if (layoutMgr == null)
            return "{\"elements\":[],\"reason\":\"layout-manager-unavailable\"}";

        LayoutNode root;
        try { root = layoutMgr.GetLayout(); }
        catch (Exception ex)
        {
            return $"{{\"elements\":[],\"error\":{JsonString(ex.Message)}}}";
        }

        var sb = new StringBuilder();
        sb.Append("{\"elements\":[");
        bool first = true;
        VisitNode(root, parentId: null, sb, ref first);
        sb.Append("]}");
        return sb.ToString();
    }

    static void VisitNode(LayoutNode node, string parentId, StringBuilder sb, ref bool first)
    {
        if (node.Element != null)
        {
            string id = ElementId(node.Element);
            if (!first) sb.Append(',');
            first = false;
            sb.Append('{');
            sb.Append($"\"id\":{JsonString(id)},");
            sb.Append($"\"kind\":{JsonString(node.Element.GetType().Name)},");
            sb.Append($"\"label\":{JsonString(LabelFor(node.Element))},");
            sb.Append($"\"x\":{node.Extents.X},");
            sb.Append($"\"y\":{node.Extents.Y},");
            sb.Append($"\"w\":{node.Extents.Width},");
            sb.Append($"\"h\":{node.Extents.Height},");
            sb.Append($"\"order\":{node.Order},");
            sb.Append($"\"parentId\":{JsonString(parentId)}");
            sb.Append('}');
            parentId = id;
        }
        foreach (var child in node.Children)
            VisitNode(child, parentId, sb, ref first);
    }

    void HandleEventRaw(HttpListenerContext ctx)
    {
        string body = ReadBody(ctx);
        if (string.IsNullOrWhiteSpace(body))
        {
            TryWriteError(ctx, HttpStatusCode.BadRequest, "empty command body");
            return;
        }
        // Strip surrounding whitespace + optional quotes (curl users often quote).
        body = body.Trim();
        if (body.Length >= 2 && body[0] == '"' && body[^1] == '"')
            body = body[1..^1];

        var evt = Event.Parse(body, out var err);
        if (evt == null)
        {
            TryWriteError(ctx, HttpStatusCode.BadRequest, $"parse-error: {err}");
            return;
        }
        Raise(evt);
        _commandsProcessed++;
        WriteJson(ctx, $"{{\"ok\":true,\"command\":{JsonString(body)},\"seq\":{_commandsProcessed}}}");
    }

    void HandleEventJson(HttpListenerContext ctx)
    {
        // Body: { "command": "<text>" }  or  { "name": "load_game", "args": [7] }
        string body = ReadBody(ctx);
        string cmdText = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("command", out var cmdEl))
                cmdText = cmdEl.GetString();
            else if (root.TryGetProperty("name", out var nameEl))
            {
                var name = nameEl.GetString();
                var parts = new StringBuilder(name);
                if (root.TryGetProperty("args", out var argsEl) && argsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var arg in argsEl.EnumerateArray())
                    {
                        parts.Append(' ');
                        switch (arg.ValueKind)
                        {
                            case JsonValueKind.String: parts.Append(arg.GetString()); break;
                            case JsonValueKind.Number: parts.Append(arg.GetRawText()); break;
                            case JsonValueKind.True:   parts.Append("true"); break;
                            case JsonValueKind.False:  parts.Append("false"); break;
                            default: parts.Append(arg.GetRawText()); break;
                        }
                    }
                }
                cmdText = parts.ToString();
            }
        }
        catch (JsonException ex)
        {
            TryWriteError(ctx, HttpStatusCode.BadRequest, "invalid-json: " + ex.Message);
            return;
        }
        if (string.IsNullOrWhiteSpace(cmdText))
        {
            TryWriteError(ctx, HttpStatusCode.BadRequest, "missing 'command' or 'name' field");
            return;
        }
        var evt = Event.Parse(cmdText, out var err);
        if (evt == null) { TryWriteError(ctx, HttpStatusCode.BadRequest, "parse-error: " + err); return; }
        Raise(evt);
        _commandsProcessed++;
        WriteJson(ctx, $"{{\"ok\":true,\"command\":{JsonString(cmdText)},\"seq\":{_commandsProcessed}}}");
    }

    void HandleClickById(HttpListenerContext ctx)
    {
        string body = ReadBody(ctx);
        string id;
        string button = "left";
        try
        {
            using var doc = JsonDocument.Parse(body);
            id = doc.RootElement.GetProperty("id").GetString();
            if (doc.RootElement.TryGetProperty("button", out var b))
                button = b.GetString() ?? "left";
        }
        catch (Exception ex) { TryWriteError(ctx, HttpStatusCode.BadRequest, "expected {\"id\":\"...\",[\"button\":\"left|right\"]}: " + ex.Message); return; }
        if (string.IsNullOrEmpty(id)) { TryWriteError(ctx, HttpStatusCode.BadRequest, "missing 'id'"); return; }

        var layoutMgr = TryResolve<ILayoutManager>();
        var root = layoutMgr?.GetLayout();
        if (root == null) { TryWriteError(ctx, HttpStatusCode.ServiceUnavailable, "no layout available"); return; }

        IUiElement found = null;
        foreach (var node in root.DepthFirstSearch(_ => true))
        {
            if (node.Element != null && ElementId(node.Element) == id)
            {
                found = node.Element;
                break;
            }
        }
        if (found is not IComponent component)
        {
            TryWriteError(ctx, HttpStatusCode.NotFound, $"no clickable element with id '{id}'");
            return;
        }
        // Send the click+release pair the same way the UI normally would.
        if (button.Equals("right", StringComparison.OrdinalIgnoreCase))
        {
            component.Receive(new UiRightClickEvent(), this);
        }
        else
        {
            component.Receive(new UiLeftClickEvent(), this);
            component.Receive(new UiLeftReleaseEvent(), this);
        }
        WriteJson(ctx, $"{{\"ok\":true,\"id\":{JsonString(id)},\"kind\":{JsonString(found.GetType().Name)},\"button\":{JsonString(button)}}}");
    }

    void HandleClickAt(HttpListenerContext ctx)
    {
        string body = ReadBody(ctx);
        int x, y; string buttonName = "left";
        try
        {
            using var doc = JsonDocument.Parse(body);
            x = doc.RootElement.GetProperty("x").GetInt32();
            y = doc.RootElement.GetProperty("y").GetInt32();
            if (doc.RootElement.TryGetProperty("button", out var b))
                buttonName = b.GetString() ?? "left";
        }
        catch (Exception ex) { TryWriteError(ctx, HttpStatusCode.BadRequest, "expected {x,y[,button]}: " + ex.Message); return; }

        var button = buttonName.ToLowerInvariant() switch
        {
            "left" => MouseButton.Left,
            "right" => MouseButton.Right,
            "middle" => MouseButton.Middle,
            _ => MouseButton.Left
        };
        var pos = new Vector2(x, y);
        // MouseButtonEvent(uint timestamp, uint windowID, MouseButton button, bool down, byte clicks)
        var down = new MouseInputEvent { MousePosition = pos, MouseEvents = new List<MouseButtonEvent> { new(0, 0, button, true, 1) } };
        var up   = new MouseInputEvent { MousePosition = pos, MouseEvents = new List<MouseButtonEvent> { new(0, 0, button, false, 1) } };
        Raise(down);
        Raise(up);
        WriteJson(ctx, $"{{\"ok\":true,\"x\":{x},\"y\":{y},\"button\":{JsonString(buttonName)}}}");
    }

    void HandleScreenshot(HttpListenerContext ctx)
    {
        // Defer to PreSwapBuffersEvent so we capture AFTER render writes the swapchain
        // back buffer but BEFORE Device.SwapBuffers rotates it. Capturing on
        // EngineUpdateEvent (before render) reads the cleared / previous-frame contents
        // which is why the first attempt at this returned a blank white image.
        _pendingScreenshots.Enqueue(ctx);
        // Note: the outer Pump() will Response.Close() this context — we MUST NOT close it
        // there until the screenshot is fulfilled. Steal it from the queue by returning a
        // sentinel that Pump treats as "do not close" (see Pump for handling).
    }

    void FulfillScreenshots()
    {
        while (_pendingScreenshots.TryDequeue(out var ctx))
        {
            try
            {
                var engine = TryResolve<UAlbion.Core.Veldrid.IVeldridEngine>();
                if (engine == null)
                {
                    TryWriteError(ctx, HttpStatusCode.ServiceUnavailable, "no IVeldridEngine — game not running");
                    continue;
                }

                using var image = engine.CaptureSwapchain();
                if (image == null)
                {
                    TryWriteError(ctx, HttpStatusCode.ServiceUnavailable, "swapchain framebuffer not ready");
                    continue;
                }

                using var ms = new MemoryStream();
                image.SaveAsPng(ms);
                var bytes = ms.ToArray();
                ctx.Response.StatusCode = (int)HttpStatusCode.OK;
                ctx.Response.ContentType = "image/png";
                ctx.Response.ContentLength64 = bytes.Length;
                ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            }
            catch (Exception ex)
            {
                _lastError = $"{ex.GetType().Name}: {ex.Message}";
                TryWriteError(ctx, HttpStatusCode.InternalServerError, _lastError);
            }
            finally
            {
                try { ctx.Response.Close(); } catch { }
            }
        }
    }

    void HandleQuit(HttpListenerContext ctx)
    {
        Raise(new QuitEvent());
        WriteJson(ctx, "{\"ok\":true,\"quitting\":true}");
    }

    // --- Helpers -------------------------------------------------------------------

    static string ReadBody(HttpListenerContext ctx)
    {
        using var sr = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding);
        return sr.ReadToEnd();
    }

    static void WriteJson(HttpListenerContext ctx, string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        ctx.Response.ContentType = "application/json; charset=utf-8";
        ctx.Response.StatusCode = (int)HttpStatusCode.OK;
        ctx.Response.ContentLength64 = bytes.Length;
        ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
    }

    static void TryWriteError(HttpListenerContext ctx, HttpStatusCode code, string message)
    {
        try
        {
            var json = $"{{\"ok\":false,\"error\":{JsonString(message)}}}";
            var bytes = Encoding.UTF8.GetBytes(json);
            ctx.Response.StatusCode = (int)code;
            ctx.Response.ContentType = "application/json; charset=utf-8";
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
        }
        catch { /* connection probably dead */ }
    }

    /// <summary>
    /// Stable per-frame ID for a UI element. Uses the underlying Component instance id
    /// when available (monotonic; survives the lifetime of the object) plus the element
    /// type name as a sanity prefix. Returns the same string each call for the same
    /// element instance — the driver should re-fetch /ui after layout-mutating actions.
    /// </summary>
    static string ElementId(IUiElement element)
    {
        if (element is Component c)
            return $"{element.GetType().Name}#{c.ComponentId}";
        return $"{element.GetType().Name}#anon{element.GetHashCode():X8}";
    }

    /// <summary>
    /// Best-effort label extraction for a UI element. Buttons / ContextMenuOptions have
    /// text content; tiles do not. Falls back to ToString() when no obvious label exists.
    /// </summary>
    static string LabelFor(IUiElement element)
    {
        var typeName = element.GetType().Name;
        // ToString often returns a useful debug label for IText-bearing elements.
        var asString = element.ToString();
        // Filter the default Type.FullName fallback so we return null instead of "UAlbion..."
        if (asString == element.GetType().FullName) return null;
        if (string.IsNullOrEmpty(asString)) return null;
        if (asString == typeName) return null;
        return asString;
    }

    static string F(float f) => f.ToString("F4", CultureInfo.InvariantCulture);

    static string JsonString(string s)
    {
        if (s == null) return "null";
        var sb = new StringBuilder(s.Length + 2);
        sb.Append('"');
        foreach (var c in s)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"':  sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append(CultureInfo.InvariantCulture, $"\\u{(int)c:x4}");
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _shutdownCts.Cancel(); } catch { }
        try { _listener.Stop(); } catch { }
        try { _listener.Close(); } catch { }
    }
}
