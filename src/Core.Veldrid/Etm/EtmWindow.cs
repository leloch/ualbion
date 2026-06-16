using System;
using System.Numerics;
using UAlbion.Api.Eventing;
using UAlbion.Api.Visual;
using UAlbion.Core.Events;
using UAlbion.Core.Visual;
using Veldrid;

namespace UAlbion.Core.Veldrid.Etm;

public class EtmWindow : Component, IRenderable
{
    readonly bool _transparent;
    int _version;
    Vector3 _lastSortCamera = new(float.NaN, float.NaN, float.NaN);
    float[] _sortDist;
    ushort[] _sortTmp;

    public string Name { get; }
    public DrawLayer RenderOrder { get; }
    public MultiBuffer<ushort> ActiveInstances { get; }
    public ExtrudedTilemap Tilemap { get; }
    public int ActiveCount { get; private set; }

    public EtmWindow(string name, ExtrudedTilemap tilemap, int maxCount, bool transparent)
    {
        Name = name;
        RenderOrder = transparent ? DrawLayer.TranslucentTerrain : DrawLayer.OpaqueTerrain;
        Tilemap = tilemap ?? throw new ArgumentNullException(nameof(tilemap));
        ActiveInstances = new MultiBuffer<ushort>(maxCount, BufferUsage.VertexBuffer, $"B:EtmActive_{name}");
        _transparent = transparent;
        ActiveCount = maxCount;
        AttachChild(ActiveInstances);
        On<PrepareFrameEvent>(_ =>
        {
            if (_version < Tilemap.Version)
            {
                _version = Tilemap.Version;

                if (Tilemap.TileCount != ActiveInstances.Count)
                    ActiveInstances.Resize(Tilemap.TileCount);

                int j = 0;
                var active = ActiveInstances.Borrow();
                for (int i = 0; i < Tilemap.TileCount; i++)
                {
                    bool isTransparent = (Tilemap.Tiles[i].Flags & DungeonTileFlags.Transparent) != 0;
                    if (isTransparent == _transparent)
                        active[j++] = (ushort)i;
                }

                ActiveCount = j;
                _lastSortCamera = new Vector3(float.NaN, float.NaN, float.NaN); // force a re-sort of the new set
            }

            // #28: translucent terrain must blend back-to-front, so the alpha window is kept sorted
            // far-to-near by tile distance to the camera (re-sorted only when the camera actually
            // moves). The opaque window is left in index order on purpose: the depth buffer makes its
            // draw order irrelevant to correctness, and sorting the whole opaque grid every frame would
            // cost more than the early-z it could buy. Frustum/occlusion culling is intentionally not
            // done - in a small bounded dungeon grid it's pure overhead (the original author's TODO
            // asked "worth bothering?"; for this geometry size the answer is no).
            if (_transparent && ActiveCount > 1)
                SortFarToNear();
        });
    }

    void SortFarToNear()
    {
        var camera = TryResolve<ICameraProvider>()?.Camera;
        if (camera == null)
            return;

        var cam = camera.Position;
        if (cam == _lastSortCamera)
            return; // camera hasn't moved since the last sort - the order still holds

        var props = Tilemap.Properties;
        uint width = props.Width;
        if (width == 0)
            return;

        var origin = new Vector3(props.Origin.X, props.Origin.Y, props.Origin.Z);
        var hs = new Vector3(props.HorizontalSpacing.X, props.HorizontalSpacing.Y, props.HorizontalSpacing.Z);
        var vs = new Vector3(props.VerticalSpacing.X, props.VerticalSpacing.Y, props.VerticalSpacing.Z);

        int n = ActiveCount;
        if (_sortDist == null || _sortDist.Length < n) { _sortDist = new float[n]; _sortTmp = new ushort[n]; }

        var active = ActiveInstances.Borrow();
        for (int k = 0; k < n; k++)
        {
            int tile = active[k];
            int tx = (int)(tile % width);
            int ty = (int)(tile / width);
            var pos = origin + tx * hs + ty * vs;
            _sortDist[k] = -Vector3.DistanceSquared(pos, cam); // negative => Array.Sort ascending gives far-to-near
            _sortTmp[k] = active[k];
        }

        Array.Sort(_sortDist, _sortTmp, 0, n);
        for (int k = 0; k < n; k++)
            active[k] = _sortTmp[k];

        _lastSortCamera = cam;
    }
}