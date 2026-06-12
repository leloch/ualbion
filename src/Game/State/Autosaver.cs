using UAlbion.Api.Eventing;
using UAlbion.Game.Events;

namespace UAlbion.Game.State;

/// <summary>
/// QoL: writes an autosave whenever a map finishes loading (map transitions, including
/// the map a save was just loaded on). The save is enqueued so it runs on the game
/// thread after map initialisation completes. Slot 98 is reserved for the autosave and
/// 99 for the quicksave (bound to Ctrl+F5 / Ctrl+F9 in mods/Common/input.json).
/// </summary>
public class Autosaver : Component
{
    public const ushort AutosaveSlot = 98;

    public Autosaver()
    {
        On<MapInitEvent>(_ =>
        {
            var state = TryResolve<IGameState>();
            if (state == null || !state.Loaded)
                return;
            Enqueue(new SaveGameEvent(AutosaveSlot, "Autosave"));
        });
    }
}
