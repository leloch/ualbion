using System;
using UAlbion.Api.Eventing;
using UAlbion.Api.Visual;
using UAlbion.Core.Events;
using UAlbion.Core.Visual;
using Veldrid;
using VeldridGen.Interfaces;

namespace UAlbion.Core.Veldrid.Skybox;

public sealed class SkyboxRenderable : Component, ISkybox
{
    readonly SkyboxManager _manager;
    readonly SingleBuffer<SkyboxUniformInfo> _uniformBuffer;

    internal SkyboxRenderable(ITextureHolder texture, ISamplerHolder sampler, SkyboxManager manager, ICamera camera)
    {
        ArgumentNullException.ThrowIfNull(texture);
        ArgumentNullException.ThrowIfNull(sampler);
        _manager = manager;

        _uniformBuffer = new SingleBuffer<SkyboxUniformInfo>(new SkyboxUniformInfo(), BufferUsage.UniformBuffer, "SpriteUniformBuffer");
        ResourceSet = new SkyboxResourceSet
        {
            Name = $"RS_Sky:{texture.Name}",
            Texture = texture,
            Sampler = sampler,
            Uniform = _uniformBuffer
        };

        AttachChild(_uniformBuffer);
        AttachChild(ResourceSet);

        On<EngineUpdateEvent>(_ =>
        {
            // #54: pan the skybox at the same angular rate as the world geometry instead of an
            // arbitrary hardcoded constant. The vertex shader maps one screen width to 1.0 texture
            // units horizontally, so to keep the panorama locked to the walls the texture must scroll
            // by 1 / horizontalFov texture-units per radian of yaw. The horizontal FOV is derived from
            // the camera's vertical FOV and the live aspect ratio (so it stays correct at any window
            // size, which a fixed constant never could).
            float vFov = camera.FieldOfView;
            float aspect = camera.AspectRatio;
            float hFov = 2f * MathF.Atan(MathF.Tan(vFov * 0.5f) * (aspect > 0 ? aspect : 1f));
            float yawScale = hFov > 0 ? 1f / hFov : 0.6f;

            _uniformBuffer.Data = new SkyboxUniformInfo
            {
                uYaw = camera.Yaw,
                uPitch = camera.Pitch,
                uVisibleProportion = ReadVar(V.Core.Gfx.Skybox.VisibleProportion),
                uYawScale = yawScale
            };
        });

    }

    public string Name => ResourceSet.Texture.Name;
    public DrawLayer RenderOrder => DrawLayer.Background;
    internal SkyboxResourceSet ResourceSet { get; }

    public void Dispose()
    {
        _uniformBuffer?.Dispose();
        ResourceSet?.Dispose();
        _manager.DisposeSkybox(this);
    }
}