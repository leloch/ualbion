using System;
using System.Collections.Generic;
using UAlbion.Api.Eventing;
using UAlbion.Core.Veldrid;
using UAlbion.Core.Visual;

namespace UAlbion.Game.Veldrid.Visual;

/// <summary>
/// IRenderableSource that emits exactly one <see cref="FullscreenQuad"/>. Used by the
/// composite pass that blits the offscreen FB_Render mirror onto the swapchain
/// (FB_Screen), and by the harness's /screenshot path. Trivially small — just wraps
/// a single quad in the IRenderableSource shape that <see cref="RenderPass"/> expects.
/// </summary>
public sealed class SingleQuadSource : Component, IRenderableSource, IDisposable
{
    readonly FullscreenQuad _quad;

    public SingleQuadSource(FullscreenQuad quad)
    {
        _quad = quad ?? throw new ArgumentNullException(nameof(quad));
        AttachChild(_quad);
    }

    public void Collect(List<IRenderable> renderables)
    {
        ArgumentNullException.ThrowIfNull(renderables);
        renderables.Add(_quad);
    }

    public void Dispose()
    {
        _quad?.Dispose();
    }
}
