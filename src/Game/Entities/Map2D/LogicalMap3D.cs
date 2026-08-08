using System;
using UAlbion.Formats.Assets.Labyrinth;
using UAlbion.Formats.Assets.Maps;
using UAlbion.Formats.Assets.Save;
using UAlbion.Formats.MapEvents;

namespace UAlbion.Game.Entities.Map2D;

public class LogicalMap3D : LogicalMap
{
    readonly MapData3D _mapData;
    readonly LabyrinthData _labyrinth;

    public LogicalMap3D(MapData3D mapData,
        LabyrinthData labyrinth,
        MapChangeCollection tempChanges,
        MapChangeCollection permChanges) : base(mapData, tempChanges, permChanges)
    {
        _mapData = mapData ?? throw new ArgumentNullException(nameof(mapData));
        _labyrinth = labyrinth ?? throw new ArgumentNullException(nameof(labyrinth));
    }

    public LabyrinthData Labyrinth => _labyrinth;

    protected override void ChangeFloor(byte x, byte y, ushort value)
    {
        var index = Index(x, y);
        if (index < 0 || index >= _mapData.Floors.Length)
        {
            Error($"Tried to update invalid floor index {index} (max {_mapData.Floors.Length}");
        }
        else
        {
            _mapData.Floors[index] = (byte)value;
            OnDirty(x, y, IconChangeType.Floor);
        }
    }

    protected override void ChangeCeiling(byte x, byte y, ushort value)
    {
        var index = Index(x, y);
        if (index < 0 || index >= _mapData.Ceilings.Length) // MAP-05: was Floors.Length (copy-paste)
        {
            Error($"Tried to update invalid ceiling index {index} (max {_mapData.Ceilings.Length}");
        }
        else
        {
            _mapData.Ceilings[index] = (byte)value;
            OnDirty(x, y, IconChangeType.Ceiling);
        }
    }

    protected override void ChangeWall(byte x, byte y, ushort value)
    {
        var index = Index(x, y);
        if (index < 0 || index >= _mapData.Contents.Length)
        {
            Error($"Tried to update invalid wall/content index {index} (max {_mapData.Contents.Length}");
        }
        else
        {
            _mapData.Contents[index] = (byte)value;
            OnDirty(x, y, IconChangeType.Wall);
        }
    }

    public (byte, FloorAndCeiling) GetFloor(int x, int y) => GetFloor(Index(x, y));
    public (byte, FloorAndCeiling) GetFloor(int index)
    {
        if (index < 0 || index >= _mapData.Floors.Length)
            return (0, null);

        byte tileIndex = _mapData.Floors[index];
        // The stored value is 1-based: value v → FloorAndCeilings[v-1] (same as GetWall, and the
        // original engine's `dec eax` at fcn.0001eeb8 @0x1efe6). The old [tileIndex] read was an
        // off-by-one that returned the NEXT record — which is why water (value 3 → record 2 = Water,
        // Unk1=8) was read as record 3 (a walkable floor) and the party walked over it. Texture
        // rendering is unaffected (it uses a separate DefineFloor(i+1 ← record i) loop). docs/re/RE_COLLISION_DATA.md.
        var tile = tileIndex > 0 && tileIndex - 1 < _labyrinth.FloorAndCeilings.Count
            ? _labyrinth.FloorAndCeilings[tileIndex - 1]
            : null;
        return (tileIndex, tile);
    }

    public (byte, FloorAndCeiling) GetCeiling(int x, int y) => GetCeiling(Index(x, y));
    public (byte, FloorAndCeiling) GetCeiling(int index)
    {
        if (index < 0 || index >= _mapData.Ceilings.Length)
            return (0, null);

        byte tileIndex = _mapData.Ceilings[index];
        var tile = tileIndex > 0 && tileIndex - 1 < _labyrinth.FloorAndCeilings.Count
            ? _labyrinth.FloorAndCeilings[tileIndex - 1]
            : null;
        return (tileIndex, tile);
    }

    public (byte, Wall) GetWall(int x, int y) => GetWall(Index(x, y));
    public (byte, Wall) GetWall(int index)
    {
        byte tileIndex = _mapData.GetWall(index);
        // MOV3D-01: wall ids are 1-based, so the valid range is 1..Walls.Count (indexing
        // Walls[tileIndex-1]). The guard must be tileIndex-1 < Count (== Count is valid),
        // matching GetFloor/GetCeiling/GetObject; `< Count` dropped the highest wall to null,
        // making it walkable and invisible to the automap despite rendering solid.
        var tile = tileIndex > 0 && tileIndex - 1 < _labyrinth.Walls.Count
            ? _labyrinth.Walls[tileIndex - 1]
            : null;
        return (tileIndex, tile);
    }

    public ObjectGroup GetObject(int x, int y) => GetObject(Index(x, y));
    public ObjectGroup GetObject(int index)
    {
        var contents = _mapData.GetObject(index);
        return contents > 0 && contents <= _labyrinth.ObjectGroups.Count
            ? _labyrinth.ObjectGroups[contents - 1]
            : null;
    }
}