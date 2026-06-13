using UAlbion.Api.Eventing;
using UAlbion.Formats.Ids;

namespace UAlbion.Game.Events;

/// <summary>
/// Start (or no-op if already running) a keyed, positional ambient sample — the handle-based
/// voice control the original tracks in NpcState.ActiveSfx (RE 6 fcn.0004356a/fcn.00062f60).
/// Key uniquely identifies the voice (e.g. npcIndex*4 + slot) so it can be stopped/moved later.
/// Position is in TILE coordinates (the audio backend scales by the map's tile size, like
/// the normal positional sound path). Looping voices persist until stopped; non-looping
/// (Looping=false) play once and are not tracked.
/// </summary>
public class StartAmbientLoopEvent : GameEvent
{
    public StartAmbientLoopEvent(int key, SampleId sample, float volume, bool looping, int radius, float tileX, float tileY)
    {
        Key = key;
        Sample = sample;
        Volume = volume;
        Looping = looping;
        Radius = radius;
        TileX = tileX;
        TileY = tileY;
    }

    public int Key { get; }
    public SampleId Sample { get; }
    public float Volume { get; }     // 0..1
    public bool Looping { get; }
    public int Radius { get; }       // world units (RE default 11000)
    public float TileX { get; }
    public float TileY { get; }
}

/// <summary>Stop a keyed ambient voice started by <see cref="StartAmbientLoopEvent"/> (no-op if absent).</summary>
public class StopAmbientLoopEvent : GameEvent
{
    public StopAmbientLoopEvent(int key) => Key = key;
    public int Key { get; }
}

/// <summary>Reposition a live keyed ambient voice (the original refreshes voice position each NPC tick).</summary>
public class MoveAmbientLoopEvent : GameEvent
{
    public MoveAmbientLoopEvent(int key, float tileX, float tileY)
    {
        Key = key;
        TileX = tileX;
        TileY = tileY;
    }

    public int Key { get; }
    public float TileX { get; }
    public float TileY { get; }
}
