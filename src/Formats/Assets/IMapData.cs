using System.Collections.Generic;
using UAlbion.Formats.Assets.Maps;
using UAlbion.Formats.Ids;

namespace UAlbion.Formats.Assets;

public interface IMapData : IEventSet
{
    MapFlags Flags { get; }
    RestMode RestMode { get; } // (Flags & 0xC) >> 2 — gates the map-menu Rest/Wait options

    // Light environment (mapFlags & 3): AlwaysLight=0 / AlwaysDark=1 / DayNightCycle=2.
    // Day/night palette cycling applies only to DayNightCycle (outdoor) maps.
    MapLightingMode LightingMode => (MapLightingMode)(
        ((Flags & MapFlags.SubMode1) != 0 ? 1 : 0) |
        ((Flags & MapFlags.SubMode2) != 0 ? 2 : 0));
    MapType MapType { get; }
    SongId SongId { get; }
    int Width { get;  }
    int Height { get;  }
    CombatBackgroundId CombatBackgroundId { get;  }
    PaletteId PaletteId { get;  }

    List<MapNpc> Npcs { get; }
    HashSet<ushort> UniqueZoneNodeIds { get; }
}