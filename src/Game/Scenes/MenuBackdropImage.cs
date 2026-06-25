using System.IO;
using System.Numerics;
using System.Reflection;
using UAlbion.Api.Eventing;
using UAlbion.Api.Visual;
using UAlbion.Core.Visual;
using UAlbion.Formats.Ids;

namespace UAlbion.Game.Scenes;

/// <summary>
/// The modern main-menu backdrop: a remastered, high-resolution Albion key-art painting embedded in
/// the assembly, decoded to a true-colour (RGBA) texture and drawn as a single full-screen, screen-space
/// sprite behind the menu UI. Replaces the old live-3D vista (<see cref="MenuBackdrop3D"/>), which the
/// game felt clunky next to a painted hero image. Renders on a low draw layer so the bespoke menu
/// dialog always sits on top.
/// </summary>
public sealed class MenuBackdropImage : Component
{
    const string ResourceName = "UAlbion.Game.Resources.MenuBackground.png";
    Sprite _sprite;

    protected override void Subscribed()
    {
        if (_sprite != null)
            return;

        var loader = TryResolve<IRgbaImageLoader>();
        if (loader == null)
        {
            Warn("[MenuBackdropImage] no IRgbaImageLoader available - backdrop disabled");
            return;
        }

        byte[] data;
        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName))
        {
            if (stream == null)
            {
                Warn($"[MenuBackdropImage] embedded resource '{ResourceName}' not found");
                return;
            }
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            data = ms.ToArray();
        }

        var texture = loader.LoadPng(data);
        if (texture == null)
            return;

        // Full-screen, screen-space (NoTransform), no depth test - same mapping as the classic 2D
        // title sprite (top-left origin, negative height flips +Y down). Underlay keeps it behind the
        // menu dialog, which draws around the Interface layer.
        _sprite = AttachChild(new Sprite(
            SpriteId.None,
            DrawLayer.Underlay,
            SpriteKeyFlags.NoTransform | SpriteKeyFlags.NoDepthTest,
            SpriteFlags.LeftAligned,
            _ => texture)
        {
            Position = new Vector3(-1.0f, 1.0f, 0),
            Size = new Vector2(2.0f, -2.0f)
        });
    }
}
