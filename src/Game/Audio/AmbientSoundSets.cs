namespace UAlbion.Game.Audio;

/// <summary>
/// One ambient-sound sub-entry (RE 6, table @0x13db10, fcn.0004356a / fcn.00062f60).
/// SampleId 0 = empty slot. Radius 0 → the engine default 11000. Volume 0 → 100.
/// Loop != 0 means the voice is tracked and looped; loop 0 plays once (fire-and-forget).
/// </summary>
public readonly record struct SfxEntry(int SampleId, int Unk1, int Volume, int Loop, int Radius)
{
    public bool IsEmpty => SampleId == 0;
    public int EffectiveVolume => Volume == 0 ? 100 : Volume;
    public int EffectiveRadius => Radius == 0 ? 11000 : Radius;
    public bool Looped => Loop != 0;
}

/// <summary>
/// The 13 ambient sound-sets (RE batch 6, extracted from MAIN.EXE table 0x13db10).
/// A map NPC's <c>Sound</c> byte (0..12) selects a row; each row has 4 slots:
///   0 = ambient (always-on positional loop),
///   1 = movement (loops only while the NPC is moving),
///   2 = chase alert (one-shot, fired when a chase begins),
///   3 = active-chase loop (loops while a ChaseParty NPC is actively pursuing).
/// Empty rows (0/7/8/10) are silent. Defaults (unk1/vol/loop=100, radius=11000) applied
/// per <see cref="SfxEntry"/>.
/// </summary>
public static class AmbientSoundSets
{
    public const int SetCount = 13;
    public const int SlotCount = 4;

    public const int SlotAmbient = 0;
    public const int SlotMovement = 1;
    public const int SlotChaseAlert = 2;
    public const int SlotChaseLoop = 3;

    static readonly SfxEntry[][] Sets =
    [
        /* 0  */ [default, default, default, default], // silent
        /* 1  */ [default, new(151, 50, 50, 100, 0), new(56, 100, 100, 0, 0), default],
        /* 2  */ [new(11, 75, 75, 100, 0), default, default, default],
        /* 3  */ [default, new(154, 75, 75, 25, 0), new(56, 100, 100, 0, 0), default],
        /* 4  */ [new(151, 100, 50, 100, 20000), default, default, default],
        /* 5  */ [new(11, 100, 100, 100, 0), default, default, default],
        /* 6  */ [default, new(150, 50, 50, 100, 0), default, default],
        /* 7  */ [default, default, default, default], // silent
        /* 8  */ [default, default, default, default], // silent
        /* 9  */ [default, new(23, 100, 50, 100, 0), default, default],
        /* 10 */ [default, default, default, default], // silent
        /* 11 */ [default, new(159, 100, 100, 100, 0), default, default],
        /* 12 */ [new(160, 100, 100, 50, 0), default, default, default],
    ];

    /// <summary>The sub-entry for a set + slot, or an empty entry if out of range / silent.</summary>
    public static SfxEntry Get(int soundSet, int slot)
        => soundSet >= 0 && soundSet < SetCount && slot >= 0 && slot < SlotCount
            ? Sets[soundSet][slot]
            : default;

    /// <summary>True when a set has any non-empty sub-entry (worth scheduling at all).</summary>
    public static bool HasAny(int soundSet)
    {
        if (soundSet < 0 || soundSet >= SetCount)
            return false;
        foreach (var e in Sets[soundSet])
            if (!e.IsEmpty)
                return true;
        return false;
    }
}
