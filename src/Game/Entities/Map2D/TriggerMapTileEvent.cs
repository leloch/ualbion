using UAlbion.Api.Eventing;
using UAlbion.Formats.Assets.Maps;

namespace UAlbion.Game.Entities.Map2D;

/// <summary>
/// Fire a tile's event zone with the given trigger type, exactly as the context-menu verbs do
/// (Examine / Manipulate / Take / TalkTo / UseItem). Raised internally by the 2D/3D selection
/// handlers; also text-parseable ("trigger_tile Manipulate 38 70") so the HTTP harness can
/// interact with the world organically.
/// </summary>
[Event("trigger_tile", "Fire a tile's event zone with the given trigger type (as the context-menu verbs do).")]
public class TriggerMapTileEvent : Event, IVerboseEvent
{
    TriggerMapTileEvent() { }
    public TriggerMapTileEvent(TriggerType type, int x, int y)
    {
        Type = type;
        X = x;
        Y = y;
    }

    [EventPart("type")] public TriggerType Type { get; private set; }
    [EventPart("x")] public int X { get; private set; }
    [EventPart("y")] public int Y { get; private set; }
}
