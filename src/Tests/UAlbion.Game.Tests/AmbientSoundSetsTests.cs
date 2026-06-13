using UAlbion.Game.Audio;
using Xunit;

namespace UAlbion.Game.Tests;

/// <summary>Locks the RE'd ambient sound-set table (RE 6 @0x13db10) against transcription drift.</summary>
public class AmbientSoundSetsTests
{
    [Fact]
    public void SilentSetsHaveNothing()
    {
        foreach (int set in new[] { 0, 7, 8, 10 })
            Assert.False(AmbientSoundSets.HasAny(set));
    }

    [Fact]
    public void AmbientLoops_AreOnSlot0_ForSets2_4_5_12()
    {
        Assert.Equal(11, AmbientSoundSets.Get(2, AmbientSoundSets.SlotAmbient).SampleId);
        Assert.Equal(151, AmbientSoundSets.Get(4, AmbientSoundSets.SlotAmbient).SampleId);
        Assert.Equal(11, AmbientSoundSets.Get(5, AmbientSoundSets.SlotAmbient).SampleId);
        Assert.Equal(160, AmbientSoundSets.Get(12, AmbientSoundSets.SlotAmbient).SampleId);
    }

    [Fact]
    public void Set4_Ambient_HasExplicitRadius20000()
    {
        var e = AmbientSoundSets.Get(4, AmbientSoundSets.SlotAmbient);
        Assert.Equal(20000, e.EffectiveRadius);
        Assert.Equal(50, e.EffectiveVolume);
        Assert.True(e.Looped);
    }

    [Fact]
    public void ChaseAlert_IsOneShot_NotLooped()
    {
        var alert = AmbientSoundSets.Get(1, AmbientSoundSets.SlotChaseAlert);
        Assert.Equal(56, alert.SampleId);
        Assert.False(alert.Looped); // loop flag 0 → fire-and-forget
    }

    [Fact]
    public void Defaults_AppliedForUnsetVolumeAndRadius()
    {
        var e = AmbientSoundSets.Get(5, AmbientSoundSets.SlotAmbient);
        Assert.Equal(100, e.EffectiveVolume);
        Assert.Equal(11000, e.EffectiveRadius); // radius 0 → engine default
    }

    [Fact]
    public void OutOfRange_ReturnsEmpty()
    {
        Assert.True(AmbientSoundSets.Get(99, 0).IsEmpty);
        Assert.True(AmbientSoundSets.Get(2, 9).IsEmpty);
    }
}
