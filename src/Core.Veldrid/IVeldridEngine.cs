using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Veldrid;

namespace UAlbion.Core.Veldrid;

public interface IVeldridEngine : IEngine
{
    GraphicsDevice Device { get; }

    /// <summary>
    /// Capture the current swapchain colour attachment as an in-memory image. Used by the
    /// HTTP harness's /screenshot endpoint. Returns null if the device hasn't been set up
    /// yet. Must be called on the game thread.
    /// </summary>
    Image<Bgra32> CaptureSwapchain();
}