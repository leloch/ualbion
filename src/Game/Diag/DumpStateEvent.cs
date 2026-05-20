using UAlbion.Api.Eventing;

namespace UAlbion.Game.Diag;

/// <summary>
/// Diagnostic event for the harness: serialise a JSON snapshot of the current game state
/// (current map, party leader position, party HP / SP / conditions, dialog stack, last
/// error) to the supplied path. Used by external tooling that drives the game over a
/// file-watcher channel to observe state between actions.
/// </summary>
/// <param name="Path">Where to write the JSON snapshot (overwrites if it exists).</param>
[Event("dump_state")]
public record DumpStateEvent([property: EventPart("path")] string Path) : EventRecord, IVerboseEvent;
