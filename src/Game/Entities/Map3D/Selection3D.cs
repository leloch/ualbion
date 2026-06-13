using UAlbion.Api.Eventing;
using UAlbion.Core;
using UAlbion.Core.Events;

namespace UAlbion.Game.Entities.Map3D;

// Selects 3D SCENE GEOMETRY (sprites / wall + object meshes) along a pick ray. Per-TILE
// selection (zones, NPCs, the Examine/Manipulate/Take/TalkTo context menu, the
// Rest/Wait menu) is handled separately by SelectionHandler3D, which ray-marches to the
// targeted floor tile and queries the LogicalMap3D zones — so this component only needs
// the scene-graph ray test. (An earlier ground-plane tile-pick alternative lived here as
// dead code; it was redundant with SelectionHandler3D and has been removed.)
public class Selection3D : Component
{
    public Selection3D()
    {
        On<WorldCoordinateSelectEvent>(OnSelect);
    }

    void OnSelect(WorldCoordinateSelectEvent e)
    {
        var scene = TryResolve<ISceneGraph>();
        scene?.RayIntersect(e.Origin, e.Direction, e.Selections);
    }
}