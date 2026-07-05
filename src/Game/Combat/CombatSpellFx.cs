using System.Collections.Generic;
using UAlbion.Base;
using UAlbion.Formats.Ids;

namespace UAlbion.Game.Combat;

/// <summary>
/// The per-spell cast-VISUAL table, RE'd in _RE_SPELLANIM.md. The original has no data
/// table — each spell's visual is hard-coded in its effect fn — so this reconstructs the
/// observed sequences as data: a school-intro orb (Dji-Kas), an optional caster→target
/// projectile, and an on-target impact sprite (+ optional flash overlay), plus the per-spell
/// samples. Buffs/heals with no battle-view visual are simply absent (null lookup).
///
/// Fidelity note: this captures the dominant visual per spell (orb → fly → impact) that a
/// player recognises; the finer particle bursts / driver clouds / clone-flash tints of the
/// original are approximated by the impact sprite. See _RE_SPELLANIM.md for the full spec.
/// </summary>
public sealed record CombatSpellFx(
    CombatGfx? Orb,        // Dji-Kas school-intro orb (#18), flies caster→target first
    CombatGfx? Projectile, // flies caster→target (fireball, bolt, breeze cloud); null = no flight
    CombatGfx Impact,      // spawned on the target on arrival (burst/flash/overlay)
    bool CasterOrigin);    // projectile starts at the caster (true) or appears on the target (false)

public static class CombatSpellFxTable
{
    // Spell ids from Base.Spell; gfx ids from Base.CombatGfx (COMBATGFX file 0x28).
    static readonly Dictionary<SpellId, CombatSpellFx> Table = Build();

    public static CombatSpellFx Get(SpellId spellId)
        => Table.TryGetValue(spellId, out var fx) ? fx : null;

    static Dictionary<SpellId, CombatSpellFx> Build()
    {
        var t = new Dictionary<SpellId, CombatSpellFx>();
        void Add(Base.Spell spell, CombatGfx? orb, CombatGfx? projectile, CombatGfx impact, bool casterOrigin = true)
            => t[(SpellId)spell] = new CombatSpellFx(orb, projectile, impact, casterOrigin);

        // --- Dji-Kas (school 0): every spell opens with the TriifalaiSeed orb (#18) ---
        Add(Base.Spell.ThornSnare,     CombatGfx.TriifalaiSeed, null, CombatGfx.Vines1);
        Add(Base.Spell.FrostSplinter,  CombatGfx.TriifalaiSeed, null, CombatGfx.SplashBlue);
        Add(Base.Spell.FrostCrystal,   CombatGfx.TriifalaiSeed, null, CombatGfx.SplashBlue);
        Add(Base.Spell.FrostAvalanche, CombatGfx.TriifalaiSeed, null, CombatGfx.SplashBlue);
        Add(Base.Spell.BlindingSpark,  CombatGfx.TriifalaiSeed, null, CombatGfx.HGradientBlue);
        Add(Base.Spell.BlindingRay,    CombatGfx.TriifalaiSeed, null, CombatGfx.HGradientBlue);
        Add(Base.Spell.BlindingStorm,  CombatGfx.TriifalaiSeed, null, CombatGfx.HGradientBlue);
        Add(Base.Spell.SleepSpores,    CombatGfx.TriifalaiSeed, null, CombatGfx.SplashGreen);
        Add(Base.Spell.Fungification,  CombatGfx.TriifalaiSeed, null, CombatGfx.SplashGreen);

        // --- Druid / nature: projectile + impact ---
        Add(Base.Spell.Fireball,       null, CombatGfx.Fireball,    CombatGfx.Explosion);
        Add(Base.Spell.SmallFireball,  null, CombatGfx.Fireball,    CombatGfx.Explosion);
        Add(Base.Spell.LightningStrike,null, CombatGfx.ElectricStone, CombatGfx.Flash, casterOrigin: false);
        Add(Base.Spell.FireRain,       null, CombatGfx.Flames,      CombatGfx.Explosion, casterOrigin: false);
        Add(Base.Spell.Thunderbolt,    null, CombatGfx.ElectricStone, CombatGfx.PlasmaBolt, casterOrigin: false);
        Add(Base.Spell.FireHail,       null, CombatGfx.Explosion,   CombatGfx.Explosion, casterOrigin: false);
        Add(Base.Spell.Thunderstorm,   null, CombatGfx.ElectricStone, CombatGfx.Flash, casterOrigin: false);

        // --- Oqulo Kamulos: fear / drain / gaze ---
        Add(Base.Spell.Boasting,       null, null, CombatGfx.FearIcon, casterOrigin: false);
        Add(Base.Spell.Shock,          null, null, CombatGfx.FearIcon, casterOrigin: false);
        Add(Base.Spell.Panic,          null, null, CombatGfx.FearIcon, casterOrigin: false);
        Add(Base.Spell.StealLife,      null, null, CombatGfx.GreenGem, casterOrigin: false);
        Add(Base.Spell.StealMagic,     null, null, CombatGfx.RedGem,   casterOrigin: false);
        Add(Base.Spell.KamulosGaze,    null, null, CombatGfx.SplashOrange, casterOrigin: false);
        Add(Base.Spell.Irritation,     null, null, CombatGfx.SplashGreen,  casterOrigin: false);

        // --- Banish demons: flash on the victim ---
        Add(Base.Spell.BanishDemon,    null, null, CombatGfx.HGradientBlue, casterOrigin: false);
        Add(Base.Spell.BanishDemons,   null, null, CombatGfx.HGradientBlue, casterOrigin: false);
        Add(Base.Spell.DemonExodus,    null, null, CombatGfx.HGradientBlue, casterOrigin: false);

        // --- Zombie magic breezes: coloured cloud projectiles (RE #68/#66/#67/#65) ---
        Add(Base.Spell.ZombiePanic,        null, CombatGfx.CloudPink,  CombatGfx.CloudPink);
        Add(Base.Spell.ZombiePoisonBreeze, null, CombatGfx.CloudBrown, CombatGfx.CloudBrown);
        Add(Base.Spell.ZombieIrritation,   null, CombatGfx.CloudRed,   CombatGfx.CloudRed);
        Add(Base.Spell.ZombiePlagueBreeze, null, CombatGfx.CloudGreen, CombatGfx.CloudGreen);

        return t;
    }
}
