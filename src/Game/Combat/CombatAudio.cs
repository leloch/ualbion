using System.Collections.Generic;
using UAlbion.Api.Eventing;
using UAlbion.Formats.Assets.Save;
using UAlbion.Formats.Ids;
using UAlbion.Formats.MapEvents;

namespace UAlbion.Game.Combat;

/// <summary>
/// Plays the RE'd combat sound effects (see _RE_NOTES.md "Combat SFX"):
/// melee swings/hits/misses are SILENT in the original engine; the only per-combatant
/// sound is the shared death scream (sample 268) — party deaths at 11000 Hz / vol 100,
/// monster deaths at 15000 Hz / vol 60 (pitch differs, not the sample). Heals play
/// sample 38; each spell handler hardcodes its own sample sequence (table below,
/// CONFIRMED from the per-spell handler disassembly). PLACEHOLDER: the original
/// sequences cast samples with the projectile flight; we play them together.
/// </summary>
public class CombatAudio : Component
{
    static readonly Dictionary<SpellId, Base.Sample[]> CastSamples = BuildCastTable();

    static Dictionary<SpellId, Base.Sample[]> BuildCastTable()
    {
        var t = new Dictionary<SpellId, Base.Sample[]>();
        void Add(Base.Spell spell, params Base.Sample[] samples) => t[spell] = samples;

        Add(Base.Spell.ThornSnare,     Base.Sample.ThrowMagicSeed, Base.Sample.PoweringUp);
        Add(Base.Spell.ViewOfLife,     Base.Sample.TechTension);
        Add(Base.Spell.FrostSplinter,  Base.Sample.ThrowMagicSeed, Base.Sample.LaserDoor, Base.Sample.MiniPyiew);
        Add(Base.Spell.FrostCrystal,   Base.Sample.ThrowMagicSeed, Base.Sample.LaserDoor, Base.Sample.MiniPyiew);
        Add(Base.Spell.FrostAvalanche, Base.Sample.ThrowMagicSeed, Base.Sample.LaserDoor, Base.Sample.MiniPyiew);
        Add(Base.Spell.BlindingSpark,  Base.Sample.ThrowMagicSeed, Base.Sample.BeamMeDown, Base.Sample.Choonk);
        Add(Base.Spell.BlindingRay,    Base.Sample.ThrowMagicSeed, Base.Sample.BeamMeDown, Base.Sample.Choonk);
        Add(Base.Spell.BlindingStorm,  Base.Sample.ThrowMagicSeed, Base.Sample.BeamMeDown, Base.Sample.Choonk);
        Add(Base.Spell.SleepSpores,    Base.Sample.ThrowMagicSeed, Base.Sample.DiddlyDiddly);
        Add(Base.Spell.ThornTrap,      Base.Sample.ThrowMagicSeed, Base.Sample.Drrzh, Base.Sample.RockCrumbling, Base.Sample.Takwow);
        Add(Base.Spell.RemoveTrapDK,   Base.Sample.Bwoowoo, Base.Sample.EchoingPing);
        Add(Base.Spell.Fungification,  Base.Sample.ThrowMagicSeed, Base.Sample.LoHiHiHiHi, Base.Sample.MultipleImpacts);
        Add(Base.Spell.Teleporter,     Base.Sample.DiddlyDiddly, Base.Sample.Strings, Base.Sample.MiniPyiew);
        Add(Base.Spell.GoddessWrath,   Base.Sample.GoddessWrathA, Base.Sample.GoddessWrathB);
        Add(Base.Spell.Irritation,     Base.Sample.DiddlyDiddly);
        Add(Base.Spell.Boasting,       Base.Sample.LongGrowlWithLaugh, Base.Sample.AngryDissonantBuzz);
        Add(Base.Spell.SmallFireball,  Base.Sample.FireballChargeUp, Base.Sample.FireballLaunch, Base.Sample.Unknown235);
        Add(Base.Spell.Fireball,       Base.Sample.FireballChargeUp, Base.Sample.FireballLaunch, Base.Sample.Unknown235);
        Add(Base.Spell.LightningStrike, Base.Sample.FireballLaunch, Base.Sample.Unknown235);
        Add(Base.Spell.FireRain,       Base.Sample.FireRainImpact);
        Add(Base.Spell.Thunderbolt,    Base.Sample.Unknown239, Base.Sample.Unknown235);
        Add(Base.Spell.FireHail,       Base.Sample.FireHailImpact, Base.Sample.Unknown235, Base.Sample.Unknown241);
        Add(Base.Spell.Thunderstorm,   Base.Sample.Unknown239, Base.Sample.Unknown235);
        Add(Base.Spell.LightningTrap,  Base.Sample.Unknown242, Base.Sample.Unknown243, Base.Sample.LightningCrackle);
        Add(Base.Spell.LightningMine,  Base.Sample.Unknown242, Base.Sample.EchoingPing);

        // Heal-school casts all share sample 38
        foreach (var heal in new[]
        {
            Base.Spell.LightHealing, Base.Spell.HealIntoxication, Base.Spell.HealBlindness,
            Base.Spell.HealPoisoning, Base.Spell.Light, Base.Spell.Regeneration,
            Base.Spell.MapView, Base.Spell.Lifebringer, Base.Spell.HealingDC,
            Base.Spell.Recuperation, Base.Spell.HealingD,
        })
            Add(heal, Base.Sample.Healing);

        return t;
    }

    public CombatAudio()
    {
        On<CombatHitEvent>(e =>
        {
            if (e.Heal)
            {
                Play(Base.Sample.Healing, 100, 0);
                return;
            }

            if (!e.Killed)
                return; // melee swings/hits/misses are silent in the original

            // One shared death scream; party-side deaths play at normal pitch/volume,
            // monster deaths higher-pitched and quieter (fcn.0004dec9).
            bool isPartyTile = e.TileIndex / SavedGame.CombatColumns >= SavedGame.CombatRowsForMobs;
            if (isPartyTile)
                Play(Base.Sample.DeathScream, 100, 0);
            else
                Play(Base.Sample.DeathScream, 60, 15000);
        });

        On<CombatCastEvent>(e =>
        {
            if (!CastSamples.TryGetValue(e.SpellId, out var samples))
                return;
            foreach (var sample in samples)
                Play(sample, 100, 0);
        });
    }

    void Play(Base.Sample sample, byte volume, ushort frequency)
        => Raise(new SoundEffectEvent(sample, volume, 0, 0, frequency, SoundMode.GlobalOneShot));
}
