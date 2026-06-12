using UAlbion.Api.Eventing;

namespace UAlbion.Core.Events;

[Event("e:window_size", "Set the game window size in pixels")]
public class SetWindowSizeEvent : EngineEvent
{
    public SetWindowSizeEvent(int width, int height)
    {
        Width = width;
        Height = height;
    }

    [EventPart("width")] public int Width { get; }
    [EventPart("height")] public int Height { get; }
}
