using System.Collections.Generic;
using UAlbion.Api.Eventing;
using UAlbion.Config;
using UAlbion.Formats.Assets.Maps;
using UAlbion.Formats.Ids;
using UAlbion.Game.Events;
using UAlbion.Game.State;

namespace UAlbion.Game.Audio;

/// <summary>
/// Drives per-NPC ambient sounds, RE batch 6 (cadence fcn.000433ef): each game tick it
/// walks the active map NPCs and, by the NPC's <c>Sound</c> set (0..12, table in
/// <see cref="AmbientSoundSets"/>):
///   slot 0 (ambient) — ensured running as a positional loop while the NPC is live;
///   slot 1 (movement) — looped only while the NPC is moving this tick;
///   slot 3 (chase loop) — looped while a ChaseParty NPC is actively pursuing;
///   slot 2 (chase alert) — a one-shot fired the tick a chase begins.
/// Live voices are repositioned each tick. Keyed voices are managed via the audio
/// backend's StartAmbientLoop/StopAmbientLoop/MoveAmbientLoop events; when no audio
/// device/AudioManager is present (headless/muted) the events are simply unhandled, so
/// this component is safe and side-effect-free in tests and smoke runs.
/// </summary>
public sealed class AmbientSoundManager : GameComponent
{
    sealed class Voice { public int Slot; public ushort X; public ushort Y; }

    // Live looping voices, keyed by npcIndex*4 + slot (matches the audio-event key space).
    readonly Dictionary<int, Voice> _live = [];
    // Per-NPC previous tile, to detect "is moving this tick" (slot 1) and chase activity.
    readonly Dictionary<int, (ushort X, ushort Y)> _prevPos = [];
    MapId _lastMap = MapId.None;

    static int Key(int npcIndex, int slot) => npcIndex * AmbientSoundSets.SlotCount + slot;

    public AmbientSoundManager()
    {
        On<FastClockEvent>(_ => Tick());
    }

    protected override void Unsubscribed() => StopAll();

    void Tick()
    {
        var state = TryResolve<IGameState>();
        if (state?.Npcs == null)
            return;

        // Map change → drop all voices from the old map (their NPC slots are reused).
        if (state.MapId != _lastMap)
        {
            StopAll();
            _lastMap = state.MapId;
            _prevPos.Clear();
        }

        for (int i = 0; i < state.Npcs.Count; i++)
        {
            var npc = state.Npcs[i];
            bool active = npc != null
                          && !npc.Id.IsNone
                          && !state.IsNpcDisabled(MapId.None, (byte)i)
                          && AmbientSoundSets.HasAny(npc.Sound);

            if (!active)
            {
                StopVoice(i, AmbientSoundSets.SlotAmbient);
                StopVoice(i, AmbientSoundSets.SlotMovement);
                StopVoice(i, AmbientSoundSets.SlotChaseLoop);
                _prevPos.Remove(i);
                continue;
            }

            bool moving = _prevPos.TryGetValue(i, out var prev) && (prev.X != npc.X || prev.Y != npc.Y);
            bool chasing = npc.MovementType == NpcMovement.ChaseParty && moving;
            bool wasChasing = _prevChasing.Contains(i);

            // slot 0 — ambient: ensure running while the NPC is live.
            EnsureVoice(i, AmbientSoundSets.SlotAmbient, npc.X, npc.Y);

            // slot 1 — movement: loop only while moving.
            if (moving) EnsureVoice(i, AmbientSoundSets.SlotMovement, npc.X, npc.Y);
            else StopVoice(i, AmbientSoundSets.SlotMovement);

            // slot 3 — active-chase loop.
            if (chasing) EnsureVoice(i, AmbientSoundSets.SlotChaseLoop, npc.X, npc.Y);
            else StopVoice(i, AmbientSoundSets.SlotChaseLoop);

            // slot 2 — chase alert: one-shot the tick chasing begins.
            if (chasing && !wasChasing)
                PlayOneShot(i, AmbientSoundSets.SlotChaseAlert, npc.X, npc.Y);
            if (chasing) _prevChasing.Add(i); else _prevChasing.Remove(i);

            // Reposition all live voices for this NPC.
            foreach (int slot in _movableSlots)
            {
                int key = Key(i, slot);
                if (_live.TryGetValue(key, out var v) && (v.X != npc.X || v.Y != npc.Y))
                {
                    v.X = npc.X; v.Y = npc.Y;
                    Raise(new MoveAmbientLoopEvent(key, npc.X, npc.Y));
                }
            }

            _prevPos[i] = (npc.X, npc.Y);
        }
    }

    static readonly int[] _movableSlots =
        [AmbientSoundSets.SlotAmbient, AmbientSoundSets.SlotMovement, AmbientSoundSets.SlotChaseLoop];

    readonly HashSet<int> _prevChasing = [];

    void EnsureVoice(int npcIndex, int slot, ushort x, ushort y)
    {
        int key = Key(npcIndex, slot);
        if (_live.ContainsKey(key))
            return;

        var npc = TryResolve<IGameState>()?.Npcs[npcIndex];
        var entry = AmbientSoundSets.Get(npc?.Sound ?? 0, slot);
        if (entry.IsEmpty || !entry.Looped)
            return;

        _live[key] = new Voice { Slot = slot, X = x, Y = y };
        Raise(new StartAmbientLoopEvent(key, new SampleId(entry.SampleId), entry.EffectiveVolume / 100f,
            looping: true, entry.EffectiveRadius, x, y));
    }

    void PlayOneShot(int npcIndex, int slot, ushort x, ushort y)
    {
        var npc = TryResolve<IGameState>()?.Npcs[npcIndex];
        var entry = AmbientSoundSets.Get(npc?.Sound ?? 0, slot);
        if (entry.IsEmpty)
            return;
        Raise(new StartAmbientLoopEvent(Key(npcIndex, slot), new SampleId(entry.SampleId),
            entry.EffectiveVolume / 100f, looping: false, entry.EffectiveRadius, x, y));
    }

    void StopVoice(int npcIndex, int slot)
    {
        int key = Key(npcIndex, slot);
        if (_live.Remove(key))
            Raise(new StopAmbientLoopEvent(key));
    }

    void StopAll()
    {
        foreach (var key in new List<int>(_live.Keys))
            Raise(new StopAmbientLoopEvent(key));
        _live.Clear();
        _prevChasing.Clear();
    }
}
