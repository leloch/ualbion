using System;
using System.Collections.Generic;
using UAlbion.Api.Eventing;
using UAlbion.Formats.Assets.Save;
using UAlbion.Formats.Ids;
using UAlbion.Formats.MapEvents;

namespace UAlbion.Game.Combat;

/// <summary>
/// Plays the RE'd combat sound effects (superseding _RE_NOTES's "silent melee" claim —
/// see _RE_COMBATAUDIO.md): swings/misses/absorbed show TEXT only, but every DAMAGING hit
/// plays the shared thud (sample 268) — party victims at 11025 Hz / vol 100,
/// monster deaths at 15000 Hz / vol 60 (pitch differs, not the sample). Heals play
/// sample 38; each spell handler hardcodes its own sample sequence (table below,
/// CONFIRMED from the per-spell handler disassembly). Deliberate deviation: the original
/// sequences cast samples with the projectile flight; we play them together (the samples
/// and order match — only the inter-sample timing differs, inaudible in practice).
/// </summary>
public class CombatAudio : Component
{
    static readonly Dictionary<SpellId, Base.Sample[]> CastSamples = BuildCastTable();

    /// <summary>The RE'd per-spell cast sample sequence (empty when the spell has none) —
    /// shared with the out-of-combat cast path (PartyMagicMenu).</summary>
    public static IReadOnlyList<Base.Sample> GetCastSamples(SpellId id)
        => CastSamples.TryGetValue(id, out var samples) ? samples : [];

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

            if (e.Amount <= 0)
                return; // swings/misses/absorbed hits show text only (_RE_COMBATAUDIO.md)

            // Sample 268 is the shared HIT thud, played on EVERY damaging hit — not a death
            // scream (fcn.0004dec9 plays it before the LP check; death adds no extra sound).
            // Party-side victims at vol 100 / 11025 Hz, monsters at vol 60 / 15000 Hz,
            // both with the original's ±50 variation on volume/pitch.
            bool isPartyTile = e.TileIndex / SavedGame.CombatColumns >= SavedGame.CombatRowsForMobs;
            var rng = TryResolve<IRandom>();
            int jitter = rng == null ? 0 : rng.Generate(101) - 50; // -50..50, the "variation 50" knob
            if (isPartyTile)
                Play(Base.Sample.DeathScream, (byte)Math.Clamp(100 + jitter / 4, 1, 127), (ushort)(11025 + jitter * 25));
            else
                Play(Base.Sample.DeathScream, (byte)Math.Clamp(60 + jitter / 4, 1, 127), (ushort)(15000 + jitter * 25));
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
