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
///   GET  /state                         → JSON snapshot (map, mapType, tileset, useSmallSprites, party, ...)
///   GET  /ui                            → list of visible UI elements with bounds
///   GET  /log [?n=N&clear=1&level=&since=] → on-screen text + captured engine warn/error/critical (filter by level substr / frame)
///   GET  /collisionscan                 → classify every 3D tile (wall/water/object/open) + IsOccupied, report blockMismatch
///   GET  /tile?x=&y=                     → inspect one 3D tile: floor/wall/ceiling/object records + per-direction occupancy
///   GET  /findtiles?kind=water|wall|open|floor0|object[&max=N] → coords of tiles matching a kind
///   GET  /switch?id=Switch.X            → current value of a story switch
///   GET  /ticker?id=Ticker.X           → current value of a ticker counter
///   GET  /quest                         → progress snapshot: all set switches, non-zero tickers, discovered words, pooled gold/rations
///   GET  /zones [?near=1&trigger=...]   → the current map's event zones (2D and 3D) with trigger/chain/first-event
///     (organic play: `party_goto x y` walks the party there — A* 2D / BFS 3D — and
///      `trigger_tile <Type> x y` fires a zone like the context-menu verbs; both via /event/raw)
///   POST /input  {velX,velY,yaw,pitch,frames?} → inject a PartyMove3DEvent (mouse-compass paths; frames>1 = held)
///   GET  /combat                        → combat state: combatants, team, tile, hp, conditions
///   GET  /inventory                     → party inventories: gold, rations, item counts (B5 trade testing)
///   GET  /sprites [?filter=...]         → rendered sprites: id, position, render size (sprite-sizing bugs)
///   GET  /npcs                          → NPC positions/movement on the current map
///   POST /event/raw   "load_game 7"     → fire any UAlbion event by its `-c` text form
///   POST /event       { name, args }    → same, but JSON-structured
///   POST /key         { key, frames? }  → simulate a keypress (held N frames) through the input pipeline
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
    string _lastErrorDetail;
    int _commandsProcessed;
    bool _disposed;

    // On-screen text log — a ring buffer of the messages the game prints to the description /
    // hover areas (combat narration, examine text, status messages). Lets a test agent read what
    // the player would read without OCR'ing a screenshot. All access is on the game thread.
    const int MaxLogEntries = 256;
    readonly List<(long frame, string kind, string text)> _messageLog = new();

    // Simulated held keys: physical key → frame on which to auto-release. Each frame Pump injects
    // the appropriate key-down/up so continuous ("+") keybindings fire for the held duration —
    // this exercises the full key→binding→event chain (not just the bound event).
    readonly Dictionary<global::Veldrid.Sdl2.Key, long> _heldKeyReleaseFrame = new();
    // /input frame-hold: re-raise these PartyMove3DEvents each frame until the release frame, so the
    // continuous mouse-compass paths (strafe / gradual turn / look — Velocity/Yaw/Pitch) are testable.
    readonly List<(UAlbion.Game.Events.PartyMove3DEvent ev, long until)> _pendingInputs = new();

    public HarnessHttpServer(int port)
    {
        _port = port;
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{port}/");
        _listener.Start();
        Task.Run(AcceptLoopAsync);

        On<EngineUpdateEvent>(_ => Pump());
        On<PreSwapBuffersEvent>(_ => FulfillScreenshots());
        On<DescriptionTextEvent>(e => AppendMessage("desc", e.Source));
        On<HoverTextEvent>(e => AppendMessage("hover", e.Source));
        // Capture engine warnings/errors/critical (Component.Error/Warn → LogEvent) into the log so
        // /log?level=error surfaces them (e.g. "[ECM] Event threw") without grepping stderr.
        On<UAlbion.Api.Eventing.LogEvent>(e =>
        {
            if ((int)e.Severity >= (int)UAlbion.Api.Eventing.LogLevel.Warning)
                AppendLine(e.Severity.ToString().ToLowerInvariant(), e.Message);
        });
    }

    void AppendMessage(string kind, UAlbion.Game.Text.IText text) => AppendLine(kind, RenderText(text));

    void AppendLine(string kind, string s)
    {
        if (string.IsNullOrEmpty(s)) return;
        // Collapse consecutive duplicates (the same hover text re-fires every frame).
        if (_messageLog.Count > 0 && _messageLog[^1].kind == kind && _messageLog[^1].text == s)
            return;
        _messageLog.Add((_frameCount, kind, s));
        if (_messageLog.Count > MaxLogEntries)
            _messageLog.RemoveRange(0, _messageLog.Count - MaxLogEntries);
    }

    static string RenderText(UAlbion.Game.Text.IText text)
    {
        if (text == null) return null;
        var sb = new StringBuilder();
        try { foreach (var b in text.GetBlocks()) sb.Append(b.Text); }
        catch { return null; }
        return sb.ToString();
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

        ReleaseExpiredHeldKeys();
        RaisePendingInputs();

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
                _lastErrorDetail = ex.ToString(); // Full stack for /lasterror
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
            case "GET /collisionscan":   WriteJson(ctx, BuildCollisionScan()); break;
            case "GET /tile":            WriteJson(ctx, BuildTileInspect(ctx)); break;
            case "GET /findtiles":       WriteJson(ctx, BuildFindTiles(ctx)); break;
            case "GET /switch":          WriteJson(ctx, BuildSwitchQuery(ctx)); break;
            case "GET /ticker":          WriteJson(ctx, BuildTickerQuery(ctx)); break;
            case "GET /quest":           WriteJson(ctx, BuildQuestDump()); break;
            case "GET /conversation":    WriteJson(ctx, BuildConversationDump()); break;
            case "GET /clock":           WriteJson(ctx, BuildClockDump()); break;
            case "GET /zones":           WriteJson(ctx, BuildZonesDump(ctx)); break;
            case "POST /input":          HandleInput(ctx); break;
            case "GET /camera":          WriteJson(ctx, BuildCameraDump()); break;
            case "GET /tilemap":         WriteJson(ctx, BuildTilemapDump()); break;
            case "GET /npcs":            WriteJson(ctx, BuildNpcsDump()); break;
            case "GET /log":             WriteJson(ctx, BuildLog(ctx)); break;
            case "GET /combat":          WriteJson(ctx, BuildCombatDump()); break;
            case "GET /inventory":       WriteJson(ctx, BuildInventoryDump()); break;
            case "GET /sprites":         WriteJson(ctx, BuildSpritesDump(ctx)); break;
            case "GET /pick":            WriteJson(ctx, BuildPickDump(ctx)); break;
            case "POST /key":            HandleKey(ctx); break;
            case "GET /lasterror":       WriteJson(ctx, $"{{\"detail\":{JsonString(_lastErrorDetail)}}}"); break;
            case "GET /wallpixels":      HandlePixelDump(ctx, useWalls: true); break;
            case "GET /floorpixels":     HandlePixelDump(ctx, useWalls: false); break;
            case "GET /gpuwallpixels":   WriteGpuLayerPng(ctx, useWalls: true); break;
            case "GET /gpufloorpixels":  WriteGpuLayerPng(ctx, useWalls: false); break;
            case "POST /event/raw":      HandleEventRaw(ctx); break;
            case "POST /event":          HandleEventJson(ctx); break;
            case "POST /firechains":     HandleFireChains(ctx); break;
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

            // Map classification — to spot a misclassified outdoor map at a glance. mapType
            // TwoDOutdoors ⇒ small NPC gfx; TwoD ⇒ large. tileset is what drives that decision
            // (MapData2D.OutdoorTilesets); useSmallSprites is the resulting NPC-gfx selector.
            var cur = TryResolve<UAlbion.Game.IMapManager>()?.Current;
            if (cur != null)
            {
                sb.Append($"\"mapType\":{JsonString(cur.MapType.ToString())},");
                sb.Append($"\"useSmallSprites\":{(cur.MapType == UAlbion.Formats.Assets.Maps.MapType.TwoDOutdoors ? "true" : "false")},");
                var tileset = (cur.MapData as UAlbion.Formats.Assets.Maps.MapData2D)?.TilesetId.ToString();
                sb.Append($"\"tileset\":{JsonString(tileset)},");
            }
            else
            {
                sb.Append("\"mapType\":null,\"useSmallSprites\":null,\"tileset\":null,");
            }
        }
        else
        {
            sb.Append("\"map\":null,\"time\":null,\"tickCount\":0,\"mapType\":null,\"useSmallSprites\":null,\"tileset\":null,");
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
    void HandlePixelDump(HttpListenerContext ctx, bool useWalls)
    {
        // ?format=png returns the full layer as an image so a human (or agent with
        // vision) can see the actual CPU-side atlas content rather than statistics.
        if (ctx.Request.QueryString["format"] == "png")
            WriteLayerPng(ctx, useWalls);
        else
            WriteJson(ctx, BuildWallPixelsDump(ctx, useWalls));
    }

    void WriteLayerPng(HttpListenerContext ctx, bool useWalls)
    {
        int wantLayer = int.TryParse(ctx.Request.QueryString["layer"], out var lv) && lv >= 0 ? lv : 1;

        var tilemap = FindActiveTilemap();
        if (tilemap == null) { TryWriteError(ctx, HttpStatusCode.ServiceUnavailable, "no active ExtrudedTilemap"); return; }

        var atlas = useWalls ? tilemap.DayWalls : tilemap.DayFloors;
        if (atlas == null) { TryWriteError(ctx, HttpStatusCode.NotFound, useWalls ? "no DayWalls" : "no DayFloors"); return; }
        if (wantLayer >= atlas.ArrayLayers) { TryWriteError(ctx, HttpStatusCode.NotFound, $"layer {wantLayer} out of range (max {atlas.ArrayLayers - 1})"); return; }

        var buffer = atlas.GetLayerBuffer(wantLayer);
        using var image = new Image<Rgba32>(buffer.Width, buffer.Height);
        for (int y = 0; y < buffer.Height; y++)
        {
            for (int x = 0; x < buffer.Width; x++)
            {
                int idx = y * buffer.Stride + x;
                uint p = idx < buffer.Buffer.Length ? buffer.Buffer[idx] : 0;
                image[x, y] = new Rgba32((byte)(p & 0xff), (byte)((p >> 8) & 0xff), (byte)((p >> 16) & 0xff), (byte)((p >> 24) & 0xff));
            }
        }

        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        var bytes = ms.ToArray();
        ctx.Response.StatusCode = (int)HttpStatusCode.OK;
        ctx.Response.ContentType = "image/png";
        ctx.Response.ContentLength64 = bytes.Length;
        ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
    }

    // Reads back a single layer of the LIVE GPU-side wall/floor array texture so it can be
    // compared against the CPU-side atlas (/wallpixels?format=png). If they differ, the
    // CPU->GPU upload is garbling data.
    unsafe void WriteGpuLayerPng(HttpListenerContext ctx, bool useWalls)
    {
        int wantLayer = int.TryParse(ctx.Request.QueryString["layer"], out var lv) && lv >= 0 ? lv : 1;

        var tilemap = FindActiveTilemap();
        if (tilemap == null) { TryWriteError(ctx, HttpStatusCode.ServiceUnavailable, "no active ExtrudedTilemap"); return; }

        var atlas = useWalls ? tilemap.DayWalls : tilemap.DayFloors;
        if (atlas == null) { TryWriteError(ctx, HttpStatusCode.NotFound, "no atlas"); return; }

        var engine = TryResolve<UAlbion.Core.Veldrid.IVeldridEngine>();
        var textureSource = TryResolve<UAlbion.Core.Veldrid.Textures.ITextureSource>();
        if (engine?.Device == null || textureSource == null) { TryWriteError(ctx, HttpStatusCode.ServiceUnavailable, "no engine/texture source"); return; }

        var holder = textureSource.GetArrayTexture(atlas);
        var tex = holder?.DeviceTexture;
        if (tex == null) { TryWriteError(ctx, HttpStatusCode.ServiceUnavailable, "no device texture"); return; }
        if (wantLayer >= tex.ArrayLayers) { TryWriteError(ctx, HttpStatusCode.NotFound, $"layer {wantLayer} out of range (GPU tex has {tex.ArrayLayers})"); return; }

        var device = engine.Device;
        var stagingDesc = new TextureDescription(tex.Width, tex.Height, 1, 1, 1, tex.Format, TextureUsage.Staging, TextureType.Texture2D);
        using var staging = device.ResourceFactory.CreateTexture(in stagingDesc);
        using var cl = device.ResourceFactory.CreateCommandList();
        cl.Begin();
        cl.CopyTexture(tex, 0, 0, 0, 0, (uint)wantLayer, staging, 0, 0, 0, 0, 0, tex.Width, tex.Height, 1, 1);
        cl.End();
        device.SubmitCommands(cl);
        device.WaitForIdle();

        var mapped = device.Map(staging, MapMode.Read);
        try
        {
            using var image = new Image<Rgba32>((int)tex.Width, (int)tex.Height);
            var src = new Span<uint>(mapped.Data.ToPointer(), (int)(mapped.SizeInBytes / sizeof(uint)));
            int stride = (int)(mapped.RowPitch / sizeof(uint));
            for (int y = 0; y < tex.Height; y++)
            {
                for (int x = 0; x < tex.Width; x++)
                {
                    uint p = src[y * stride + x];
                    image[x, y] = new Rgba32((byte)(p & 0xff), (byte)((p >> 8) & 0xff), (byte)((p >> 16) & 0xff), (byte)((p >> 24) & 0xff));
                }
            }

            using var ms = new MemoryStream();
            image.SaveAsPng(ms);
            var bytes = ms.ToArray();
            ctx.Response.StatusCode = (int)HttpStatusCode.OK;
            ctx.Response.ContentType = "image/png";
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
        }
        finally { device.Unmap(staging); }
    }

    UAlbion.Core.Veldrid.Etm.ExtrudedTilemap FindActiveTilemap()
    {
        var etmMgr = TryResolve<UAlbion.Core.Visual.IEtmManager>();
        if (etmMgr == null) return null;

        var childrenField = typeof(UAlbion.Api.Eventing.Component).GetField("_children",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (childrenField?.GetValue(etmMgr) is not System.Collections.IEnumerable children)
            return null;

        foreach (var c in children)
            if (c is UAlbion.Core.Veldrid.Etm.ExtrudedTilemap tm)
                return tm;
        return null;
    }

    string BuildWallPixelsDump(HttpListenerContext ctx, bool useWalls)
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

        var walls = useWalls ? tilemap.DayWalls : tilemap.DayFloors;
        if (walls == null) return useWalls ? "{\"error\":\"no DayWalls\"}" : "{\"error\":\"no DayFloors\"}";
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

    // Diagnostic: cast the selection ray at the given PIXEL coordinates and report what it
    // hits (target type, ToString, t) — for debugging click/hover hit detection.
    string BuildPickDump(HttpListenerContext ctx)
    {
        var q = ctx.Request.QueryString;
        int x = int.TryParse(q["x"], out var xv) ? xv : 0;
        int y = int.TryParse(q["y"], out var yv) ? yv : 0;

        var selectionManager = TryResolve<UAlbion.Core.ISelectionManager>();
        if (selectionManager == null) return "{\"hits\":[]}";

        var hits = new List<UAlbion.Core.Events.Selection>();
        selectionManager.CastRayFromScreenSpace(hits, new Vector2(x, y), true, false);

        var sb = new StringBuilder();
        sb.Append("{\"hits\":[");
        for (int i = 0; i < hits.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append('{');
            sb.Append($"\"type\":{JsonString(hits[i].Target?.GetType().Name)},");
            sb.Append($"\"target\":{JsonString(hits[i].Target?.ToString())},");
            sb.Append($"\"t\":{F(hits[i].Distance)}");
            sb.Append('}');
        }
        sb.Append("]}");
        return sb.ToString();
    }

    string BuildNpcsDump()
    {
        var state = TryResolve<IGameState>();
        if (state?.Loaded != true) return "{\"npcs\":[]}";

        var sb = new StringBuilder();
        sb.Append("{\"mticks\":").Append(state.MTicksToday).Append(",\"npcs\":[");
        bool first = true;
        for (int i = 0; i < state.Npcs.Count; i++)
        {
            var npc = state.Npcs[i];
            if (npc == null || npc.Id.IsNone) continue;
            if (!first) sb.Append(',');
            first = false;
            sb.Append('{');
            sb.Append($"\"n\":{i},");
            sb.Append($"\"id\":{JsonString(npc.Id.ToString())},");
            sb.Append($"\"x\":{npc.X},\"y\":{npc.Y},");
            sb.Append($"\"movement\":{JsonString(npc.MovementType.ToString())}");
            sb.Append('}');
        }
        sb.Append("]}");
        return sb.ToString();
    }

    // Recent on-screen text (combat narration / examine / hover / status). ?n=N limits the
    // number returned (default 50, newest last); ?clear=1 empties the buffer.
    string BuildLog(HttpListenerContext ctx)
    {
        if (ctx.Request.QueryString["clear"] == "1")
            _messageLog.Clear();
        int n = int.TryParse(ctx.Request.QueryString["n"], out var nv) && nv > 0 ? nv : 50;
        // Optional filters: level=<substr of kind, e.g. "error"> and since=<frame> — so a test can
        // pull JUST the errors since a known frame instead of grepping a huge stderr dump.
        string level = ctx.Request.QueryString["level"];
        long since = long.TryParse(ctx.Request.QueryString["since"], out var sv) ? sv : -1;

        var matches = new List<(long frame, string kind, string text)>();
        foreach (var entry in _messageLog)
        {
            if (since >= 0 && entry.Item1 < since) continue;
            if (!string.IsNullOrEmpty(level) && (entry.Item2 == null || entry.Item2.IndexOf(level, StringComparison.OrdinalIgnoreCase) < 0)) continue;
            matches.Add(entry);
        }
        int start = Math.Max(0, matches.Count - n);

        var sb = new StringBuilder();
        sb.Append("{\"total\":").Append(_messageLog.Count).Append(",\"matched\":").Append(matches.Count).Append(",\"frame\":").Append(_frameCount).Append(",\"messages\":[");
        for (int i = start; i < matches.Count; i++)
        {
            if (i > start) sb.Append(',');
            var (frame, kind, text) = matches[i];
            sb.Append('{');
            sb.Append($"\"frame\":{frame},");
            sb.Append($"\"kind\":{JsonString(kind)},");
            sb.Append($"\"text\":{JsonString(text)}");
            sb.Append('}');
        }
        sb.Append("]}");
        return sb.ToString();
    }

    UAlbion.Game.Combat.IReadOnlyBattle FindBattle()
    {
        var scene = TryResolve<ISceneManager>()?.ActiveScene;
        if (scene == null) return null;

        // Battle is added as a child of the active (combat) scene — reflect _children like the
        // other harness dumps do (Battle isn't a resolvable service).
        var childrenField = typeof(UAlbion.Api.Eventing.Component).GetField("_children",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (childrenField?.GetValue(scene) is not System.Collections.IEnumerable children)
            return null;
        foreach (var c in children)
            if (c is UAlbion.Game.Combat.IReadOnlyBattle b)
                return b;
        return null;
    }

    // Combat state: every combatant with team, grid position, HP (from the battle's shadow,
    // which covers monsters), conditions, and alive flag. {"active":false} when not in combat.
    string BuildCombatDump()
    {
        var battle = FindBattle();
        if (battle == null) return "{\"active\":false}";

        string language = ReadVar(V.User.Gameplay.Language);
        var sb = new StringBuilder();
        sb.Append("{\"active\":true,\"combatants\":[");
        bool first = true;
        var mobs = battle.Mobs;
        for (int i = 0; i < mobs.Count; i++)
        {
            var p = mobs[i];
            if (p == null) continue;
            if (!first) sb.Append(',');
            first = false;

            var eff = p.Effective;
            int hp = battle.GetLifePoints(p);
            bool isMonster = p.SheetId.Type == UAlbion.Config.AssetType.MonsterSheet;
            sb.Append('{');
            sb.Append($"\"sheet\":{JsonString(p.SheetId.ToString())},");
            sb.Append($"\"name\":{JsonString(eff?.GetName(language))},");
            sb.Append($"\"team\":{JsonString(isMonster ? "monster" : "party")},");
            sb.Append($"\"tileX\":{p.X},\"tileY\":{p.Y},\"tile\":{p.CombatPosition},");
            sb.Append($"\"hp\":{hp},");
            sb.Append($"\"hpMax\":{eff?.Combat?.LifePoints?.Max ?? 0},");
            sb.Append($"\"sp\":{eff?.Magic?.SpellPoints?.Current ?? 0},");
            sb.Append($"\"conditions\":{JsonString(eff?.Combat?.Conditions.ToString())},");
            sb.Append($"\"alive\":{(hp > 0 ? "true" : "false")}");
            sb.Append('}');
        }
        sb.Append("]}");
        return sb.ToString();
    }

    static readonly System.Reflection.FieldInfo ChildrenField = typeof(UAlbion.Api.Eventing.Component)
        .GetField("_children", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

    static void CollectSprites(object component, List<UAlbion.Core.Visual.Sprite> outList, int depth)
    {
        if (component == null || depth > 64) return;
        if (component is UAlbion.Core.Visual.Sprite s)
            outList.Add(s);
        if (ChildrenField?.GetValue(component) is System.Collections.IEnumerable children)
            foreach (var c in children)
                CollectSprites(c, outList, depth + 1);
    }

    // Rendered sprites in the active scene with their id, world position and render Dimensions
    // (logical Size). Diagnoses sprite-sizing bugs (e.g. oversized world-map NPCs) by comparing
    // an NPC's Dimensions against the player's. ?filter=substring matches the sprite id.
    string BuildSpritesDump(HttpListenerContext ctx)
    {
        string filter = ctx.Request.QueryString["filter"];

        // Walk both the active scene (3D billboards live here) AND the current map (2D map
        // entities — NPCs/player — hang off the FlatMap, not the scene). Dedup by reference.
        var seen = new HashSet<UAlbion.Core.Visual.Sprite>();
        var sprites = new List<UAlbion.Core.Visual.Sprite>();
        var roots = new object[] { TryResolve<ISceneManager>()?.ActiveScene, TryResolve<UAlbion.Game.IMapManager>()?.Current };
        foreach (var root in roots)
        {
            if (root == null) continue;
            var found = new List<UAlbion.Core.Visual.Sprite>();
            CollectSprites(root, found, 0);
            foreach (var s in found) if (seen.Add(s)) sprites.Add(s);
        }

        var sb = new StringBuilder();
        sb.Append("{\"count\":").Append(sprites.Count).Append(",\"sprites\":[");
        bool first = true;
        foreach (var sp in sprites)
        {
            string id = sp.Id?.ToString() ?? "None";
            if (filter != null && id.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
            if (!first) sb.Append(',');
            first = false;
            var pos = sp.Position;
            var dim = sp.Dimensions;
            sb.Append('{');
            sb.Append($"\"id\":{JsonString(id)},");
            sb.Append($"\"kind\":{JsonString(sp.GetType().Name)},");
            sb.Append($"\"x\":{F(pos.X)},\"y\":{F(pos.Y)},\"z\":{F(pos.Z)},");
            sb.Append($"\"w\":{F(dim.X)},\"h\":{F(dim.Y)}");
            sb.Append('}');
        }
        sb.Append("]}");
        return sb.ToString();
    }

    // 3D collision verifier: classify every tile of the current dungeon (wall / water-hazard floor
    // / blocking object / open) and confirm IsOccupied agrees, so the collision rules (water blocks,
    // open arches pass, solid walls block, normal floors walkable) can be checked WITHOUT a human.
    string BuildCollisionScan()
    {
        var map = TryResolve<UAlbion.Game.IMapManager>()?.Current;
        if (map is not UAlbion.Game.Entities.Map3D.DungeonMap dungeon)
            return "{\"is3d\":false}";

        var lm = typeof(UAlbion.Game.Entities.Map3D.DungeonMap)
            .GetField("_logicalMap", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?.GetValue(dungeon) as UAlbion.Game.Entities.Map2D.LogicalMap3D;
        if (lm == null) return "{\"is3d\":true,\"logicalMap\":null}";
        var collider = TryResolve<UAlbion.Game.ICollisionManager>();

        int walls = 0, water = 0, objs = 0, open = 0, occupiedMismatch = 0;
        int wallSprites = 0, openArches = 0, seeThru = 0;
        string waterSample = null, archSample = null, wallSample = null;
        var seeThruSamples = new List<string>();
        var wallCollisionHisto = new SortedDictionary<int, int>();
        // Object (prop) collision analysis: outdoor "walls" are often blocking object-groups, so an
        // object with collision bits OUTSIDE the 0x78 block mask is a walk-through building.
        var objCollisionHisto = new SortedDictionary<int, int>();
        var seeThruObjSamples = new List<string>();
        int seeThruObjs = 0;
        var labObjects = lm.Labyrinth?.Objects;
        for (int y = 0; y < lm.Height; y++)
        for (int x = 0; x < lm.Width; x++)
        {
            var (_, wall) = lm.GetWall(x, y);
            var (floorIdx, floor) = lm.GetFloor(x, y);
            bool isWall  = wall != null && (wall.Collision & 0x78) != 0;
            bool isWater = floor != null && (floor.Unk1 & 0x08) != 0;
            bool occ = collider?.IsOccupied(x, y, x, y) ?? false;

            // Wall-sprite accounting: every tile that DRAWS a wall. A wall sprite that doesn't block
            // (and isn't IsOccupied via another layer) is something the player sees but walks through.
            //   openArches = Collision==0 (intentional doorway/gate — correct to pass)
            //   seeThru    = Collision!=0 but the directional-block mask 0x78 is clear AND nothing
            //                else occupies the tile → a SOLID-looking wall you can walk through (bug
            //                signature: collision bits set outside 0x78 that should block).
            if (wall != null)
            {
                wallSprites++;
                int col = (int)wall.Collision;
                wallCollisionHisto.TryGetValue(col, out var n); wallCollisionHisto[col] = n + 1;
                if (col == 0) openArches++;
                else if ((col & 0x78) == 0 && !occ)
                {
                    seeThru++;
                    if (seeThruSamples.Count < 12) seeThruSamples.Add($"{x},{y} col=0x{col:x}");
                }
            }

            // Object-group collision: histogram every non-floor sub-object's Collision byte and flag
            // any that aren't blocked by the 0x78 mask while nothing else occupies the tile.
            var group = lm.GetObject(x, y);
            if (group != null && labObjects != null)
            {
                foreach (var sub in group.SubObjects)
                {
                    if (sub == null || sub.ObjectInfoNumber >= labObjects.Count) continue;
                    var info = labObjects[sub.ObjectInfoNumber];
                    if (info == null) continue;
                    if ((info.Properties & UAlbion.Formats.Assets.Labyrinth.LabyrinthObjectFlags.FloorObject) != 0) continue;
                    int oc = (int)info.Collision;
                    objCollisionHisto.TryGetValue(oc, out var on); objCollisionHisto[oc] = on + 1;
                    if (oc != 0 && (oc & 0x78) == 0 && !occ)
                    {
                        seeThruObjs++;
                        if (seeThruObjSamples.Count < 12) seeThruObjSamples.Add($"{x},{y} obj#{sub.ObjectInfoNumber} col=0x{oc:x}");
                    }
                }
            }

            if (isWall) { walls++; wallSample ??= $"{x},{y} occ={occ}"; }
            else if (isWater) { water++; waterSample ??= $"{x},{y} floorIdx={floorIdx} unk1={floor.Unk1} occ={occ}"; }
            else if (occ) objs++;
            else { open++; if (floorIdx == 0) archSample ??= $"{x},{y} floor0 occ={occ}"; }

            // Water/wall MUST read occupied; an open non-wall non-water tile must NOT.
            if ((isWater || isWall) && !occ) occupiedMismatch++;
        }

        var histo = new StringBuilder("{");
        bool fh = true;
        foreach (var kv in wallCollisionHisto) { if (!fh) histo.Append(','); fh = false; histo.Append($"\"0x{kv.Key:x}\":{kv.Value}"); }
        histo.Append('}');
        var stSamples = new StringBuilder("[");
        for (int i = 0; i < seeThruSamples.Count; i++) { if (i > 0) stSamples.Append(','); stSamples.Append(JsonString(seeThruSamples[i])); }
        stSamples.Append(']');
        var objHisto = new StringBuilder("{");
        bool foh = true;
        foreach (var kv in objCollisionHisto) { if (!foh) objHisto.Append(','); foh = false; objHisto.Append($"\"0x{kv.Key:x}\":{kv.Value}"); }
        objHisto.Append('}');
        var stObjSamples = new StringBuilder("[");
        for (int i = 0; i < seeThruObjSamples.Count; i++) { if (i > 0) stObjSamples.Append(','); stObjSamples.Append(JsonString(seeThruObjSamples[i])); }
        stObjSamples.Append(']');

        return "{" +
            $"\"is3d\":true,\"map\":{JsonString(map.MapId.ToString())},\"w\":{lm.Width},\"h\":{lm.Height}," +
            $"\"wallTiles\":{walls},\"waterTiles\":{water},\"objectBlocked\":{objs},\"openTiles\":{open}," +
            $"\"blockMismatch\":{occupiedMismatch}," +
            $"\"wallSprites\":{wallSprites},\"openArches\":{openArches},\"seeThruWalls\":{seeThru}," +
            $"\"seeThruSamples\":{stSamples}," +
            $"\"wallCollisionHisto\":{histo}," +
            $"\"seeThruObjects\":{seeThruObjs},\"seeThruObjSamples\":{stObjSamples},\"objCollisionHisto\":{objHisto}," +
            $"\"waterSample\":{JsonString(waterSample)},\"wallSample\":{JsonString(wallSample)},\"floor0Sample\":{JsonString(archSample)}" +
            "}";
    }

    // The current 3D map's logical map (reflected — it's a private DungeonMap field), or null if not 3D.
    UAlbion.Game.Entities.Map2D.LogicalMap3D GetLogicalMap3D()
    {
        if (TryResolve<UAlbion.Game.IMapManager>()?.Current is not UAlbion.Game.Entities.Map3D.DungeonMap dungeon)
            return null;
        return typeof(UAlbion.Game.Entities.Map3D.DungeonMap)
            .GetField("_logicalMap", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?.GetValue(dungeon) as UAlbion.Game.Entities.Map2D.LogicalMap3D;
    }

    // GET /tile?x=&y= — full inspection of one 3D tile: floor/wall/ceiling/object records (index +
    // sprite + the raw collision bytes) and the resolved passability (per approach direction). The
    // tile-inspector that makes spatial/collision bugs diagnosable without dumping LABDATA by hand.
    string BuildTileInspect(HttpListenerContext ctx)
    {
        var lm = GetLogicalMap3D();
        if (lm == null) return "{\"is3d\":false}";
        int x = int.TryParse(ctx.Request.QueryString["x"], out var xv) ? xv : -1;
        int y = int.TryParse(ctx.Request.QueryString["y"], out var yv) ? yv : -1;
        if (x < 0 || y < 0 || x >= lm.Width || y >= lm.Height) return "{\"error\":\"x/y out of range\"}";

        var (fIdx, floor) = lm.GetFloor(x, y);
        var (wIdx, wall) = lm.GetWall(x, y);
        var (cIdx, ceil) = lm.GetCeiling(x, y);
        var group = lm.GetObject(x, y);
        var collider = TryResolve<UAlbion.Game.ICollisionManager>();
        // Per-approach occupancy (Collider3D currently direction-agnostic, so these match; reported
        // separately to stay correct if per-direction blocking is added later).
        bool occN = collider?.IsOccupied(x, y + 1, x, y) ?? false;
        bool occE = collider?.IsOccupied(x - 1, y, x, y) ?? false;
        bool occS = collider?.IsOccupied(x, y - 1, x, y) ?? false;
        bool occW = collider?.IsOccupied(x + 1, y, x, y) ?? false;

        var sb = new StringBuilder();
        sb.Append('{');
        sb.Append($"\"x\":{x},\"y\":{y},");
        sb.Append("\"floor\":").Append(floor == null
            ? $"{{\"idx\":{fIdx}}}"
            : $"{{\"idx\":{fIdx},\"sprite\":{JsonString(floor.SpriteId.ToString())},\"unk1\":{floor.Unk1},\"props\":{JsonString(floor.Properties.ToString())}}}").Append(',');
        sb.Append("\"wall\":").Append(wall == null
            ? $"{{\"idx\":{wIdx}}}"
            : $"{{\"idx\":{wIdx},\"sprite\":{JsonString(wall.SpriteId.ToString())},\"collision\":\"0x{wall.Collision:x}\"}}").Append(',');
        sb.Append("\"ceiling\":").Append(ceil == null ? $"{{\"idx\":{cIdx}}}" : $"{{\"idx\":{cIdx},\"unk1\":{ceil.Unk1}}}").Append(',');
        sb.Append("\"objectGroup\":").Append(group == null ? "false" : "true").Append(',');
        sb.Append($"\"occupied\":{(occN || occE || occS || occW).ToString().ToLowerInvariant()},");
        sb.Append($"\"occ\":{{\"n\":{occN.ToString().ToLowerInvariant()},\"e\":{occE.ToString().ToLowerInvariant()},\"s\":{occS.ToString().ToLowerInvariant()},\"w\":{occW.ToString().ToLowerInvariant()}}}");
        sb.Append('}');
        return sb.ToString();
    }

    // GET /findtiles?kind=water|wall|open|floor0|object[&max=N] — coords of tiles matching a kind, so
    // a test can locate a water/arch/wall tile to assert against instead of guessing.
    string BuildFindTiles(HttpListenerContext ctx)
    {
        var lm = GetLogicalMap3D();
        if (lm == null) return "{\"is3d\":false}";
        string kind = (ctx.Request.QueryString["kind"] ?? "water").ToLowerInvariant();
        int max = int.TryParse(ctx.Request.QueryString["max"], out var mv) && mv > 0 ? mv : 40;
        var collider = TryResolve<UAlbion.Game.ICollisionManager>();

        var sb = new StringBuilder();
        sb.Append($"{{\"kind\":{JsonString(kind)},\"tiles\":[");
        int found = 0;
        for (int y = 0; y < lm.Height && found < max; y++)
        for (int x = 0; x < lm.Width && found < max; x++)
        {
            var (fIdx, floor) = lm.GetFloor(x, y);
            var (_, wall) = lm.GetWall(x, y);
            bool match = kind switch
            {
                "water"  => floor != null && (floor.Unk1 & 0x08) != 0,
                "wall"   => wall != null && (wall.Collision & 0x78) != 0,
                "floor0" => fIdx == 0,
                "object" => lm.GetObject(x, y) != null,
                "open"   => !(collider?.IsOccupied(x, y, x, y) ?? false),
                _ => false
            };
            if (!match) continue;
            if (found > 0) sb.Append(',');
            sb.Append($"[{x},{y}]");
            found++;
        }
        sb.Append($"],\"count\":{found}}}");
        return sb.ToString();
    }

    // GET /zones[?trigger=Normal&near=1] — the current 3D map's event zones (x,y,trigger,first-event
    // type). Locates building-entry / teleport zones to test why stepping on a door does nothing.
    // near=1 limits to zones within 6 tiles of the party.
    string BuildZonesDump(HttpListenerContext ctx)
    {
        string triggerFilter = ctx.Request.QueryString["trigger"];
        bool near = ctx.Request.QueryString["near"] == "1";
        var lm = GetLogicalMap3D();
        bool is3d = lm != null;

        int pcx = -100, pcy = -100;
        var p = TryResolve<IParty>()?.Leader?.GetPosition();
        if (p != null)
        {
            pcx = (int)MathF.Round(is3d ? p.Value.X : p.Value.X);
            pcy = (int)MathF.Round(is3d ? p.Value.Z : p.Value.Y);
        }

        // 2D maps read the raw map data (same source as dump_map_zones); 3D goes through
        // LogicalMap3D so replayed wall/zone changes are reflected.
        var mapData = TryResolve<UAlbion.Game.IMapManager>()?.Current?.MapData as UAlbion.Formats.Assets.Maps.BaseMapData;
        if (!is3d && mapData == null)
            return "{\"zones\":[],\"error\":\"no map loaded\"}";

        int width = is3d ? lm.Width : mapData.Width;
        int height = is3d ? lm.Height : mapData.Height;

        var seen = new HashSet<int>();
        var sb = new StringBuilder();
        sb.Append("{\"is3d\":").Append(is3d ? "true" : "false")
          .Append(",\"party\":[").Append(pcx).Append(',').Append(pcy).Append("],\"zones\":[");
        bool first = true;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            if (near && (System.Math.Abs(x - pcx) > 6 || System.Math.Abs(y - pcy) > 6)) continue;
            var zone = is3d ? lm.GetZone(x, y) : mapData.GetZone(y * width + x);
            if (zone?.Node == null) continue;
            int key = y * 1000 + x;
            if (!seen.Add(key)) continue;
            string trig = zone.Trigger.ToString();
            if (!string.IsNullOrEmpty(triggerFilter) && trig.IndexOf(triggerFilter, StringComparison.OrdinalIgnoreCase) < 0) continue;
            string evt = zone.Node.Event?.ToString() ?? "?";
            if (!first) sb.Append(',');
            first = false;
            sb.Append($"{{\"x\":{x},\"y\":{y},\"trigger\":{JsonString(trig)},\"event\":{JsonString(evt)},\"chain\":{zone.EventIndex}}}");
        }
        sb.Append("]}");
        return sb.ToString();
    }

    // GET /quest — quest-progress introspection: every set switch, every non-zero ticker, the
    // discovered conversation words, and the party's pooled gold/rations. Lets an autonomous
    // playthrough assert story progress ("did the trial verdict set its switch?") without
    // poking single ids blind via /switch.
    string BuildQuestDump()
    {
        var state = TryResolve<UAlbion.Game.State.IGameState>();
        if (state?.Loaded != true) return "{\"loaded\":false}";

        var sb = new StringBuilder();
        sb.Append("{\"loaded\":true,\"map\":").Append(JsonString(state.MapId.ToString()));
        sb.Append(",\"time\":").Append(JsonString(state.Time.ToString("O")));

        sb.Append(",\"switches\":[");
        bool first = true;
        for (int i = 0; i < 1024; i++)
        {
            var id = new UAlbion.Formats.Ids.SwitchId(UAlbion.Config.AssetType.Switch, i);
            if (!state.GetSwitch(id)) continue;
            if (!first) sb.Append(',');
            first = false;
            sb.Append(JsonString(id.ToString()));
        }
        sb.Append("],\"tickers\":{");
        first = true;
        for (int i = 0; i < 256; i++)
        {
            var id = new UAlbion.Formats.Ids.TickerId(UAlbion.Config.AssetType.Ticker, i);
            var value = state.GetTicker(id);
            if (value == 0) continue;
            if (!first) sb.Append(',');
            first = false;
            sb.Append(JsonString(id.ToString())).Append(':').Append(value);
        }
        sb.Append("},\"words\":[");
        first = true;
        foreach (var word in state.DiscoveredWords)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append(JsonString(word.ToString()));
        }
        sb.Append(']');

        int gold = 0, rations = 0;
        var party = TryResolve<IParty>();
        if (party != null)
        {
            foreach (var member in party.StatusBarOrder)
            {
                var inv = member?.Effective?.Inventory;
                gold += inv?.Gold?.Amount ?? 0;
                rations += inv?.Rations?.Amount ?? 0;
            }
        }
        sb.Append(",\"gold\":").Append(gold).Append(",\"rations\":").Append(rations);
        sb.Append('}');
        return sb.ToString();
    }

    // GET /clock — clock running state + active scene + active event-chain count. Diagnoses a frozen
    // overworld (clock stopped while the player has free control = NPCs stuck) vs a legit cutscene
    // (clock stopped because an event chain is running).
    string BuildClockDump()
    {
        var clock = TryResolve<UAlbion.Core.IClock>();
        var scene = TryResolve<UAlbion.Game.State.ISceneManager>();
        var events = TryResolve<UAlbion.Game.IEventManager>();
        var state = TryResolve<UAlbion.Game.State.IGameState>();
        return "{" +
            $"\"clockRunning\":{((clock?.IsRunning ?? false).ToString().ToLowerInvariant())}," +
            $"\"activeScene\":{JsonString(scene?.ActiveSceneId.ToString())}," +
            $"\"activeChains\":{(events?.Contexts?.Count ?? -1)}," +
            $"\"loaded\":{((state?.Loaded ?? false).ToString().ToLowerInvariant())}," +
            $"\"time\":{JsonString(state?.Time.ToString())}" +
            "}";
    }

    // GET /conversation — live dialogue state: NPC, current text, the numbered options (with text +
    // block), and discovered topic-words. Lets a driver pick real options ("respond N") and assert
    // the resulting state instead of firing chains blind. {"active":false} when not in conversation.
    string BuildConversationDump()
    {
        var conv = TryResolve<UAlbion.Game.Gui.Text.IConversationManager>()?.Conversation;
        if (conv == null) return "{\"active\":false}";

        const System.Reflection.BindingFlags BF =
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var ct = typeof(UAlbion.Game.Gui.Dialogs.Conversation);

        var sb = new StringBuilder();
        sb.Append("{\"active\":true,");

        if (ct.GetField("_npc", BF)?.GetValue(conv) is UAlbion.Formats.Assets.Sheets.ICharacterSheet npc)
        {
            string name = null;
            try { name = npc.GetName("English"); } catch { /* missing string table */ }
            sb.Append("\"npc\":{")
              .Append($"\"id\":{JsonString(npc.Id.ToString())},")
              .Append($"\"name\":{JsonString(name)},")
              .Append($"\"eventSet\":{JsonString(npc.EventSetId.ToString())}").Append("},");
        }

        // The text window's TextSourceWrapper is itself an IText → render directly.
        if (ct.GetField("_textWindow", BF)?.GetValue(conv) is { } tw
            && tw.GetType().GetField("_text", BF)?.GetValue(tw) is UAlbion.Game.Text.IText text)
            sb.Append($"\"text\":{JsonString(RenderText(text))},");

        sb.Append("\"options\":[");
        if (ct.GetField("_optionsWindow", BF)?.GetValue(conv) is { } ow
            && ow.GetType().GetField("_optionElements", BF)?.GetValue(ow) is System.Collections.IEnumerable elems)
        {
            int n = 1; bool first = true;
            foreach (var el in elems)
            {
                if (el is not UAlbion.Game.Gui.Dialogs.ConversationOption opt) continue;
                if (!first) sb.Append(',');
                first = false;
                sb.Append($"{{\"n\":{n},\"text\":{JsonString(RenderText(opt.Text))},\"block\":{JsonString(opt.BlockId?.ToString())}}}");
                n++;
            }
        }
        sb.Append("],");

        sb.Append("\"words\":[");
        if (ct.GetField("_topics", BF)?.GetValue(conv) is System.Collections.IDictionary topics)
        {
            bool first = true;
            foreach (System.Collections.DictionaryEntry kv in topics)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append($"{{\"word\":{JsonString(kv.Key?.ToString())},\"status\":{JsonString(kv.Value?.ToString())}}}");
            }
        }
        sb.Append("],");
        sb.Append($"\"frame\":{_frameCount}");
        sb.Append('}');
        return sb.ToString();
    }

    // GET /switch?id=Switch.X — current value of a story switch (for chain/quest assertions).
    string BuildSwitchQuery(HttpListenerContext ctx)
    {
        var state = TryResolve<UAlbion.Game.State.IGameState>();
        string idStr = ctx.Request.QueryString["id"];
        if (state == null || string.IsNullOrEmpty(idStr)) return "{\"error\":\"need ?id=Switch.X and a loaded game\"}";
        try
        {
            var id = UAlbion.Formats.Ids.SwitchId.Parse(idStr);
            return $"{{\"id\":{JsonString(id.ToString())},\"value\":{state.GetSwitch(id).ToString().ToLowerInvariant()}}}";
        }
        catch (Exception ex) { return $"{{\"error\":{JsonString(ex.Message)}}}"; }
    }

    // GET /ticker?id=Ticker.X — current value of a ticker counter.
    string BuildTickerQuery(HttpListenerContext ctx)
    {
        var state = TryResolve<UAlbion.Game.State.IGameState>();
        string idStr = ctx.Request.QueryString["id"];
        if (state == null || string.IsNullOrEmpty(idStr)) return "{\"error\":\"need ?id=Ticker.X and a loaded game\"}";
        try
        {
            var id = UAlbion.Formats.Ids.TickerId.Parse(idStr);
            return $"{{\"id\":{JsonString(id.ToString())},\"value\":{state.GetTicker(id)}}}";
        }
        catch (Exception ex) { return $"{{\"error\":{JsonString(ex.Message)}}}"; }
    }

    // POST /input {velX,velY,yaw,pitch,frames?} — inject a full PartyMove3DEvent (the mouse-compass
    // continuous paths that the text `party_move_3d x y` can't reach: Yaw=gradual turn, Velocity.X=
    // strafe, Pitch=look). frames>1 re-raises it each frame (held), like /key, for continuous motion.
    void HandleInput(HttpListenerContext ctx)
    {
        string body = ReadBody(ctx);
        float velX = 0, velY = 0, yaw = 0, pitch = 0; int frames = 1;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            var r = doc.RootElement;
            if (r.TryGetProperty("velX", out var a)) velX = (float)a.GetDouble();
            if (r.TryGetProperty("velY", out var b)) velY = (float)b.GetDouble();
            if (r.TryGetProperty("yaw", out var c)) yaw = (float)c.GetDouble();
            if (r.TryGetProperty("pitch", out var d)) pitch = (float)d.GetDouble();
            if (r.TryGetProperty("frames", out var f)) frames = f.GetInt32();
        }
        catch (Exception ex) { TryWriteError(ctx, HttpStatusCode.BadRequest, "expected {velX,velY,yaw,pitch[,frames]}: " + ex.Message); return; }

        if (frames < 1) frames = 1;
        var ev = new UAlbion.Game.Events.PartyMove3DEvent { Velocity = new System.Numerics.Vector2(velX, velY), Yaw = yaw, Pitch = pitch };
        if (frames == 1) Raise(ev);
        else _pendingInputs.Add((ev, _frameCount + frames));
        _commandsProcessed++;
        WriteJson(ctx, $"{{\"ok\":true,\"velX\":{F(velX)},\"velY\":{F(velY)},\"yaw\":{F(yaw)},\"pitch\":{F(pitch)},\"frames\":{frames}}}");
    }

    void RaisePendingInputs()
    {
        for (int i = _pendingInputs.Count - 1; i >= 0; i--)
        {
            if (_frameCount >= _pendingInputs[i].until) { _pendingInputs.RemoveAt(i); continue; }
            Raise(_pendingInputs[i].ev);
        }
    }

    // Party inventories: per member, pooled gold/rations and non-empty backpack slots. Lets the
    // merchant economy (B5 buy/sell) be verified — read gold + item counts before/after a trade.
    string BuildInventoryDump()
    {
        var party = TryResolve<IParty>();
        var state = TryResolve<IGameState>();
        if (party == null || state == null) return "{\"members\":[]}";

        var sb = new StringBuilder();
        sb.Append("{\"totalGold\":").Append(party.TotalGold).Append(",\"members\":[");
        bool firstM = true;
        foreach (var pm in party.StatusBarOrder)
        {
            if (pm == null) continue;
            var inv = state.GetInventory(new UAlbion.Formats.Assets.Inv.InventoryId(pm.Id));
            if (inv == null) continue;
            if (!firstM) sb.Append(',');
            firstM = false;
            sb.Append('{');
            sb.Append($"\"id\":{JsonString(pm.Id.ToString())},");
            sb.Append($"\"gold\":{inv.Gold?.Amount ?? 0},");
            sb.Append($"\"rations\":{inv.Rations?.Amount ?? 0},");
            sb.Append("\"items\":[");
            bool firstI = true;
            foreach (var slot in inv.EnumerateAll())
            {
                if (slot == null || slot.Item.IsNone || slot.Item.Type != UAlbion.Config.AssetType.Item) continue;
                if (!firstI) sb.Append(',');
                firstI = false;
                sb.Append($"{{\"item\":{JsonString(slot.Item.ToString())},\"amount\":{slot.Amount}}}");
            }
            sb.Append("]}");
        }
        sb.Append("]}");
        return sb.ToString();
    }

    // Simulate a keypress through the real input pipeline (key → binding → event), so keybindings
    // can be tested end-to-end. Body: {"key":"W"[,"frames":N]}. Holds the key for N frames so
    // continuous ("+") bindings fire each frame; releases automatically.
    void HandleKey(HttpListenerContext ctx)
    {
        string body = ReadBody(ctx);
        string keyName; int frames = 1;
        try
        {
            using var doc = JsonDocument.Parse(body);
            keyName = doc.RootElement.GetProperty("key").GetString();
            if (doc.RootElement.TryGetProperty("frames", out var f)) frames = f.GetInt32();
        }
        catch (Exception ex) { TryWriteError(ctx, HttpStatusCode.BadRequest, "expected {\"key\":\"W\"[,\"frames\":N]}: " + ex.Message); return; }

        if (!Enum.TryParse<global::Veldrid.Sdl2.Key>(keyName, true, out var key))
        {
            TryWriteError(ctx, HttpStatusCode.BadRequest, $"unknown key '{keyName}' (use global::Veldrid.Sdl2.Key names, e.g. W, Up, ShiftLeft)");
            return;
        }
        if (frames < 1) frames = 1;

        RaiseKey(key, true);                                  // key-down → InputBinder._pressedKeys
        _heldKeyReleaseFrame[key] = _frameCount + frames;     // auto key-up scheduled in Pump
        WriteJson(ctx, $"{{\"ok\":true,\"key\":{JsonString(key.ToString())},\"frames\":{frames}}}");
    }

    void ReleaseExpiredHeldKeys()
    {
        if (_heldKeyReleaseFrame.Count == 0) return;
        List<global::Veldrid.Sdl2.Key> toRelease = null;
        foreach (var kvp in _heldKeyReleaseFrame)
            if (_frameCount >= kvp.Value)
                (toRelease ??= new List<global::Veldrid.Sdl2.Key>()).Add(kvp.Key);
        if (toRelease == null) return;
        foreach (var k in toRelease)
        {
            RaiseKey(k, false);
            _heldKeyReleaseFrame.Remove(k);
        }
    }

    void RaiseKey(global::Veldrid.Sdl2.Key key, bool down)
    {
        // KeyEvent(timestamp, windowId, down, repeat, physical, virtual, modifiers). InputBinder
        // reads only Physical/Down/Modifiers, so Virtual can be default.
        var ke = new global::Veldrid.Sdl2.KeyEvent(0u, 0u, down, false, key, default, global::Veldrid.Sdl2.ModifierKeys.None);
        Raise(new KeyboardInputEvent
        {
            DeltaSeconds = 0,
            KeyEvents = new List<global::Veldrid.Sdl2.KeyEvent> { ke },
            InputEvents = Array.Empty<System.Text.Rune>()
        });
    }

    string BuildCameraDump()
    {
        // Cameras live on the active scene, not as a global ICamera service — go via
        // the camera provider so this works for both 2D (orthographic) and 3D scenes.
        var camera = TryResolve<UAlbion.Core.Visual.ICamera>()
                     ?? TryResolve<UAlbion.Core.Visual.ICameraProvider>()?.Camera;
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

    // Deep narrative shakeout: fire EVERY event chain of the current map (the player-triggered
    // zone/NPC chains, not just the map-init ones that run on load) so a chapter walk surfaces any
    // crash / unhandled-handler exception / weirdness without a human. Each chain is fired in a
    // try/catch; chains that open a dialog/combat/teleport park asynchronously (their crashes, if
    // any, surface via lastError). Reset state by reloading the map between calls.
    void HandleFireChains(HttpListenerContext ctx)
    {
        var map = TryResolve<UAlbion.Game.IMapManager>()?.Current;
        if (map?.MapData is not UAlbion.Formats.Assets.IEventSet eventSet)
        {
            WriteJson(ctx, "{\"map\":null,\"chains\":0,\"errors\":[]}");
            return;
        }

        var source = new UAlbion.Formats.MapEvents.EventSource(eventSet.Id, UAlbion.Formats.Assets.Maps.TriggerType.Action);
        string errBefore = _lastError;
        var sb = new StringBuilder();
        sb.Append("{\"map\":").Append(JsonString(eventSet.Id.ToString()));
        sb.Append(",\"chains\":").Append(eventSet.Chains.Count);
        sb.Append(",\"errors\":[");
        bool first = true;
        var sceneMgr = TryResolve<UAlbion.Game.State.ISceneManager>();
        for (int i = 0; i < eventSet.Chains.Count; i++)
        {
            // If a chain left an inventory/chest query parked open, STOP — firing more chains now
            // would pile up chests and cascade OpenChest's SetResult into a re-entrant StackOverflow
            // (a tool artifact; real play opens one chest at a time, UI-gated). Skip the remainder.
            if (sceneMgr?.ActiveSceneId == UAlbion.Game.Scenes.SceneId.Inventory)
            {
                sb.Append(first ? "" : ",").Append($"{{\"chain\":{i},\"note\":\"stopped: inventory/chest parked open\"}}");
                first = false;
                break;
            }
            ushort entry = eventSet.Chains[i];
            if (entry >= eventSet.Events.Count) continue;
            // Skip chains that open a modal inventory query (chest/door/merchant): they park awaiting
            // UI and, dismissed in a tight loop, the synchronous SetResult→resume→next-query cascade
            // recurses into a StackOverflow (a TOOL artifact, not a game bug — real play opens one at
            // a time). They need real UI and are exercised by the dedicated buy/sell/trap tests.
            var entryEvent = eventSet.Events[entry].Event;
            if (entryEvent is UAlbion.Formats.MapEvents.ChestEvent
                or UAlbion.Formats.MapEvents.DoorEvent
                or UAlbion.Formats.MapEvents.MerchantEvent)
                continue;
            try
            {
                Raise(new UAlbion.Game.Events.TriggerChainEvent(eventSet, entry, source));
                // Dismiss text popups so they don't stack across 60+ chains (which bogs the thread).
                // DismissMessage closes ONLY TextDialogs — deliberately NOT CloseWindowEvent, which
                // would resolve a parked chest/inventory query and cascade into a re-entrant
                // StackOverflow when several chests are reached in one sweep.
                Raise(new UAlbion.Game.Events.DismissMessageEvent());
            }
            catch (Exception ex)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append($"{{\"chain\":{i},\"entry\":{entry},\"error\":{JsonString(ex.Message)}}}");
            }
        }
        sb.Append("],\"lastErrorBefore\":").Append(JsonString(errBefore));
        sb.Append(",\"lastErrorAfter\":").Append(JsonString(_lastError));
        sb.Append('}');
        _commandsProcessed++;
        WriteJson(ctx, sb.ToString());
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
        // Send hover + click + release + blur the same way the UI normally would —
        // Button ignores clicks unless it believes the cursor is over it (IsHovered).
        if (button.Equals("right", StringComparison.OrdinalIgnoreCase))
        {
            component.Receive(new UAlbion.Core.Events.HoverEvent(), this);
            component.Receive(new UiRightClickEvent(), this);
            component.Receive(new UAlbion.Core.Events.BlurEvent(), this);
        }
        else
        {
            component.Receive(new UAlbion.Core.Events.HoverEvent(), this);
            component.Receive(new UiLeftClickEvent(), this);
            component.Receive(new UiLeftReleaseEvent(), this);
            component.Receive(new UAlbion.Core.Events.BlurEvent(), this);
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
