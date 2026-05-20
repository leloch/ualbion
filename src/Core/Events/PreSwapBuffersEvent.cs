using UAlbion.Api.Eventing;

namespace UAlbion.Core.Events;

/// <summary>
/// Raised by the engine after <c>RenderSystem.Render</c> finishes writing the swapchain
/// framebuffer but before <c>Device.SwapBuffers()</c> presents it. Subscribe here to
/// observe the current frame's pixels (e.g. for screenshots / visual-regression diffs)
/// — after SwapBuffers the back buffer rotates and you'd be reading the previous frame.
/// </summary>
[Event("pre_swap_buffers")]
public class PreSwapBuffersEvent : EngineEvent, IVerboseEvent
{
    public static readonly PreSwapBuffersEvent Instance = new();
}
