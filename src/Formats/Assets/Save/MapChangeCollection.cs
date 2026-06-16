using System;
using System.Collections.Generic;
using SerdesNet;
using UAlbion.Api;
using UAlbion.Config;
using UAlbion.Formats.Ids;
using UAlbion.Formats.MapEvents;

namespace UAlbion.Formats.Assets.Save;

public class MapChangeCollection : List<MapChange>
{
    public static MapChangeCollection Serdes(SerdesName _, MapChangeCollection c, AssetMapping mapping, ISerdes s)
    {
        ArgumentNullException.ThrowIfNull(s);
        c ??= [];
        uint size = s.UInt32("Size", (uint)(c.Count * MapChange.SizeOnDisk + 2));
        ushort count = s.UInt16(nameof(Count), (ushort)c.Count);
        ApiUtil.Assert(count * MapChange.SizeOnDisk == size - 2);
        for (int i = 0; i < count; i++)
        {
            if(i < c.Count)
                c[i] = MapChange.Serdes(i, c[i], mapping, s);
            else
                c.Add(MapChange.Serdes(i, null, mapping, s));
        }

        return c;
    }

    public void Update(MapId mapId, byte x, byte y, IconChangeType type, ChangeIconLayers layers, ushort value)
    {
        foreach (var c in this)
        {
            if (c.MapId != mapId || c.X != x || c.Y != y || c.ChangeType != type)
                continue;

            c.Value = value;
            c.Layers = layers;
            return;
        }

        // BUGFIX (#95): the first-time add previously omitted Value AND Layers, so a tile's first
        // persisted change recorded Value=0 / Layers=None. On reload/re-entry that replayed as a
        // garbage tile (corruption + crash on the Hunter Clan cellar door). Record the real values.
        Add(new MapChange { MapId = mapId, X = x, Y = y, ChangeType = type, Layers = layers, Value = value });
    }
}
