using System;
using UAlbion.Api.Eventing;
using UAlbion.Core;
using UAlbion.Core.Events;
using UAlbion.Core.Veldrid;
using UAlbion.Core.Veldrid.Diag;
using UAlbion.Core.Veldrid.Etm;
using UAlbion.Core.Veldrid.Events;
using UAlbion.Core.Veldrid.Meshes;
using UAlbion.Core.Veldrid.Skybox;
using UAlbion.Core.Veldrid.Sprites;
using UAlbion.Api.Visual;
using UAlbion.Core.Visual;
using UAlbion.Game;
using UAlbion.Game.Veldrid.Diag;
using UAlbion.Game.Veldrid.Diag.Cockpit;
using UAlbion.Game.Veldrid.Visual;
using Veldrid;
using VeldridGen.Interfaces;
using static UAlbion.Game.Veldrid.AlbionRenderSystemConstants;
using ImGuiRenderer = UAlbion.Core.Veldrid.Diag.ImGuiRenderer;

namespace UAlbion;

public sealed class AlbionRenderSystem : Component, IDisposable
{
    readonly RenderManager _manager;
    readonly IRenderSystem _default;
    readonly IRenderSystem _debug;
    (float Red, float Green, float Blue, float Alpha) _clearColour;
    bool _debugMode;
    bool _modeDirty;
    bool _openCockpitPending;

    public AlbionRenderSystem(ICameraProvider mainCamera, IImGuiMenuManager menus)
    {
        OutputDescription screenFormat = new(
            new OutputAttachmentDescription(PixelFormat.D24_UNorm_S8_UInt),
            new OutputAttachmentDescription(PixelFormat.B8_G8_R8_A8_UNorm));

        var globalProvider1 = new GlobalResourceSetProvider();
        var globalProvider2 = new GlobalResourceSetProvider();

        // Offscreen mirror so /screenshot can read the rendered frame. The D3D11/Vulkan
        // swapchain back buffer is not readable via Veldrid's CopyTexture, but a
        // SimpleFramebuffer's color attachment is. P_Game targets FB_Render and
        // P_Composite blits FB_Render -> FB_Screen. Unlike the first (reverted) attempt
        // this is window-resize-aware: C_WindowUpdater resizes FB_Render alongside the
        // GameWindow whenever WindowResizedEvent fires, so the mirror always matches the
        // swapchain dimensions.
        var fbRender = new SimpleFramebuffer(FB_Render, 720, 480);

        _manager = RenderManagerBuilder.Create()
            .Framebuffer(FB_Render, fbRender)
            .Renderer(R_Sprite, new SpriteRenderer(screenFormat))
            .Renderer(R_Blended, new BlendedSpriteRenderer(screenFormat))
            .Renderer(R_Tile, new TileRenderer(screenFormat))
            .Renderer(R_Etm, new EtmRenderer(screenFormat))
            .Renderer(R_Mesh, new MeshRenderer(screenFormat))
            .Renderer(R_Sky, new SkyboxRenderer(screenFormat))
            .Renderer(R_Debug, new ImGuiRenderer(screenFormat))
            .Renderer(R_Quad, new FullscreenQuadRenderer())

            .Source(S_Sprite,  new BatchManager<SpriteKey, SpriteInfo>(       static (key, f) => f.CreateSpriteBatch(key)))
            .Source(S_Blended, new BatchManager<SpriteKey, BlendedSpriteInfo>(static (key, f) => f.CreateBlendedSpriteBatch(key)))
            .Source(S_Mesh,    new BatchManager<MeshId, GpuMeshInstanceData>( static (key, f) => ((VeldridCoreFactory)f).CreateMeshBatch(key)))
            .Source(S_Tile, new TileRenderableManager())
            .Source(S_Etm, new EtmManager())
            .Source(S_Sky, new SkyboxManager())
            .Source(S_Debug, new ImGuiRenderable())
            // S_Composite emits a single FullscreenQuad whose source is FB_Render's
            // color attachment. P_Composite uses R_Quad to sample it onto FB_Screen.
            .Source(S_Composite, new SingleQuadSource(
                new FullscreenQuad(
                    "composite",
                    DrawLayer.Compositing,
                    fbRender.Color,
                    new System.Numerics.Vector4(0, 0, 1, 1),
                    screenFormat)
                { ApplyGamma = true })) // #71: the final composite applies Core.Visual.Gamma

            .System(Sys_Default, sys =>
                sys
                .Framebuffer(FB_Screen, new MainFramebuffer(FB_Screen))
                .Component(C_InputRouter, new AdHocComponent(C_InputRouter,
                    static x =>
                    { 
                        // When running fullscreen, just echo the mouse input through to the game's mouse modes,
                        // when showing the debug UI the pass-through of input is done in ImGuiGameWindow.
                        var mouseEvent = new MouseInputEvent();
                        var keyboardEvent = new KeyboardInputEvent();
                        x.On<InputEvent>(e =>
                        {
                            keyboardEvent.DeltaSeconds   = e.DeltaSeconds;
                            keyboardEvent.InputEvents    = e.Snapshot.InputEvents;
                            keyboardEvent.KeyEvents      = e.Snapshot.KeyEvents;
                            x.Raise(keyboardEvent);

                            mouseEvent.DeltaSeconds  = e.DeltaSeconds;
                            mouseEvent.MouseDelta    = e.MouseDelta;
                            mouseEvent.WheelDelta    = e.Snapshot.WheelDelta;
                            mouseEvent.MousePosition = e.Snapshot.MousePosition;
                            mouseEvent.MouseEvents   = e.Snapshot.MouseEvents;
                            mouseEvent.Snapshot      = e.Snapshot;
                            x.Raise(mouseEvent);
                        });
                    }))
                .Component(C_GameWindow, new GameWindow(1,1, UiConstants.UiExtents.Width, UiConstants.UiExtents.Height))
                .Component(C_WindowUpdater, // Minimal component to ensure the game (and FB_Render mirror) resize with the window
                    AdHocComponent.Build(C_WindowUpdater,
                        ((GameWindow)sys.GetComponent(C_GameWindow), fbRender),
                        static (ctx, x)
                            => x.On<WindowResizedEvent>(e =>
                            {
                                ctx.Item1.Resize(e.Width, e.Height);
                                if (e.Width >= 1 && e.Height >= 1)
                                {
                                    ctx.Item2.Width = (uint)e.Width;
                                    ctx.Item2.Height = (uint)e.Height;
                                }
                            })))
                .Resources(globalProvider1)
                .Component("c_globalUpdater", new GlobalResourceSetUpdater(globalProvider1))
                .Pass(P_Game, pass =>
                    pass
                    .Renderers(R_Sprite, R_Blended, R_Tile, R_Etm, R_Mesh, R_Sky)
                    .Sources(S_Sprite, S_Blended, S_Tile, S_Etm, S_Mesh, S_Sky)
                    .Target(FB_Render)
                    .Resources(new MainPassResourceProvider(sys.GetFramebuffer(FB_Render), mainCamera))
                    .Render(MainRenderFunc)
                    .Build()
                )
                .Pass(P_Composite, pass =>
                    pass
                    .Renderer(R_Quad)
                    .Source(S_Composite)
                    .Target(FB_Screen)
                    .Dependency(P_Game)
                    .Build()
                )
                .Build()
            )
            .System(Sys_Debug, sys => 
                sys
                .Framebuffer(FB_Screen, new MainFramebuffer(FB_Screen))
                .Framebuffer(FB_Game, new SimpleFramebuffer(FB_Game, 360, 240))
                .Component(C_GameWindow, new GameWindow(360, 240, UiConstants.UiExtents.Width, UiConstants.UiExtents.Height))
                .Component(C_ImGui, new ImGuiManager((ImGuiRenderer)sys.GetRenderer(R_Debug)))
                .Action(() =>
                {
                    var framebuffer = sys.GetFramebuffer(FB_Game);
                    var window =  (GameWindow)sys.GetComponent(C_GameWindow);
                    menus.AddMenuItem(new ShowWindowMenuItem(
                        "Game",
                        "Windows",
                        name => new ImGuiGameWindow(name, framebuffer, window)));

                    menus.AddMenuItem(new ShowWindowMenuItem(
                        "Positions",
                        "Windows/Debug",
                        name => new PositionsWindow(name, mainCamera)));
                })
                .Resources(globalProvider2)
                .Component("c_globalUpdater", new GlobalResourceSetUpdater(globalProvider2))
                .Pass(P_Game, pass => 
                    pass
                    .Renderers(R_Sprite, R_Blended, R_Tile, R_Etm, R_Mesh, R_Sky)
                    .Sources(S_Sprite, S_Blended, S_Tile, S_Etm, S_Mesh, S_Sky)
                    .Target(FB_Game)
                    .Resources(new MainPassResourceProvider(sys.GetFramebuffer(FB_Game), mainCamera))
                    .Render(MainRenderFunc)
                    .Build()
                )
                .Pass(P_Debug, pass =>
                    pass
                    .Renderer(R_Debug)
                    .Source(S_Debug)
                    .Target(FB_Screen)
                    .ClearColor(RgbaFloat.Grey)
                    .Dependency(P_Game)
                    .Build()
                )
                .Build()
            )
            .Build();

        AttachChild(_manager);

        _default = _manager.GetSystem(Sys_Default);
        _debug = _manager.GetSystem(Sys_Debug);

        On<ToggleDiagnosticsEvent>(_ =>
        {
            _debugMode = !_debugMode;
            _modeDirty = true;
        });

        // show_cockpit: force the debug overlay on (so ImGui renders) and queue the cockpit
        // window to be opened once the debug render system is active (next BeginFrame).
        On<ShowCockpitEvent>(_ =>
        {
            if (!_debugMode)
            {
                _debugMode = true;
                _modeDirty = true;
            }
            _openCockpitPending = true;
        });

        On<BeginFrameEvent>(_ =>
        {
            if (_modeDirty)
                SetRenderSystem();

            // Retry until the debug system's IImGuiManager is actually resolvable (it registers
            // when Sys_Debug becomes active, which may lag a frame behind the mode switch).
            if (_openCockpitPending && _debugMode && OpenCockpit())
                _openCockpitPending = false;
        });
        On<SetClearColourEvent>(e => _clearColour = (e.Red, e.Green, e.Blue, e.Alpha));
    }

    protected override void Subscribed() => SetRenderSystem();

    void SetRenderSystem()
    {
        var activeSystem = _debugMode ? _debug : _default;
        var inactiveSystem = _debugMode ? _default : _debug;

        inactiveSystem.IsActive = false; // Make sure both systems aren't active at the same time, or any overlapping ServiceComponents will throw
        activeSystem.IsActive = true;

        Enqueue(new ShowHardwareCursorEvent(_debugMode));

        var engine = (Engine)TryResolve<IEngine>();
        if (engine != null)
            engine.RenderSystem = _debugMode ? _debug : _default;

        _modeDirty = false;
    }

    // Open the Playthrough Test Cockpit (idempotent — no-op if already open). Returns false if
    // the debug system's IImGuiManager isn't resolvable yet, so the caller can retry next frame.
    bool OpenCockpit()
    {
        var manager = TryResolve<IImGuiManager>();
        if (manager == null)
            return false;

        foreach (var _ in manager.FindWindows("Cockpit"))
            return true; // already open

        manager.AddWindow(new PlaythroughCockpitWindow($"Cockpit##{manager.GetNextWindowId()}"));
        return true;
    }

    void MainRenderFunc(RenderPass pass, GraphicsDevice device, CommandList cl, IResourceSetHolder set1)
    {
        cl.SetFramebuffer(pass.Target.Framebuffer);
        cl.SetFullViewports();
        cl.SetFullScissorRects();
        cl.ClearColorTarget(0, new RgbaFloat(_clearColour.Red, _clearColour.Green, _clearColour.Blue, _clearColour.Alpha));
        cl.ClearDepthStencil(device.IsDepthRangeZeroToOne ? 1f : 0f);
        pass.CollectAndDraw(device, cl, set1);
    }

    public void Dispose() => _manager.Dispose();
}
