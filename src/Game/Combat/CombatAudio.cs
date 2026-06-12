using UAlbion.Api.Eventing;
using UAlbion.Formats.Ids;
using UAlbion.Formats.MapEvents;

namespace UAlbion.Game.Combat;

/// <summary>
/// Plays sound feedback for combat round playback by listening to the presentation
/// events Battle raises. PLACEHOLDER sample choices: the original's combat SFX come
/// from the combat song's wave library via the AIL driver (per-monster entries, not
/// yet RE'd — see _RE_NOTES.md "Sound id space"); these named SAMPLES entries are
/// stand-ins that audibly fit until that mapping is decoded.
/// </summary>
public class CombatAudio : Component
{
    static readonly SampleId HitSample  = (SampleId)Base.Sample.MetallicStrike;
    static readonly SampleId MissSample = (SampleId)Base.Sample.Woosh;
    static readonly SampleId KillSample = (SampleId)Base.Sample.MultipleImpacts;
    static readonly SampleId HealSample = (SampleId)Base.Sample.Healing;

    public CombatAudio()
    {
        On<CombatHitEvent>(e =>
        {
            var sample = e.Heal ? HealSample
                : e.Killed ? KillSample
                : e.Amount > 0 ? HitSample
                : MissSample;
            Raise(new SoundEffectEvent(sample, 100, 0, 0, 0, SoundMode.GlobalOneShot));
        });
    }
}
