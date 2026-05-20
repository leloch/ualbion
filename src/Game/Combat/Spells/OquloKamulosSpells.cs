namespace UAlbion.Game.Combat.Spells;

/// <summary>
/// Oqulo Kamulos (human mage, "Brotherhood of the Old Power") spell registrations.
/// Spell ids 91..120. Steal/Personal-Protection/KamulosGaze/RemoveTrap require special
/// behaviour and remain unregistered until decoded.
/// </summary>
public static class OquloKamulosSpells
{
    public static void RegisterAll()
    {
        // Fire line
        SpellEffectRegistry.Register(new DamageSpellEffect(Base.Spell.Fireball,    baseDamage: 10, strengthScale: 1));
        SpellEffectRegistry.Register(new DamageSpellEffect(Base.Spell.FireRain,    baseDamage: 14, strengthScale: 2));
        SpellEffectRegistry.Register(new DamageSpellEffect(Base.Spell.FireHail,    baseDamage: 20, strengthScale: 2));

        // Lightning line
        SpellEffectRegistry.Register(new DamageSpellEffect(Base.Spell.LightningStrike, baseDamage: 12, strengthScale: 1));
        SpellEffectRegistry.Register(new DamageSpellEffect(Base.Spell.Thunderbolt,     baseDamage: 16, strengthScale: 2));
        SpellEffectRegistry.Register(new DamageSpellEffect(Base.Spell.Thunderstorm,    baseDamage: 22, strengthScale: 3));
    }
}
