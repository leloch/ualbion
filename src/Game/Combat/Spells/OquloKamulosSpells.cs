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

        // Traps and mines — placed on a target tile, trigger when stepped on.
        // (Trap = visible to enemies in the original, mine = hidden; UAlbion doesn't
        // distinguish yet — PLACEHOLDER.)
        SpellEffectRegistry.Register(new TrapSpellEffect(Base.Spell.LightningTrap,    damage: 10));
        SpellEffectRegistry.Register(new TrapSpellEffect(Base.Spell.BigLightningTrap, damage: 18));
        SpellEffectRegistry.Register(new TrapSpellEffect(Base.Spell.LightningMine,    damage: 14));
        SpellEffectRegistry.Register(new TrapSpellEffect(Base.Spell.BigLightningMine, damage: 24));
        SpellEffectRegistry.Register(new RemoveTrapEffect(Base.Spell.RemoveTrapKK));

        // Drains + defense + gaze
        SpellEffectRegistry.Register(new StealLifeEffect(Base.Spell.StealLife, baseAmount: 8, strengthScale: 2));
        SpellEffectRegistry.Register(new StealMagicEffect(Base.Spell.StealMagic, amount: 10));
        SpellEffectRegistry.Register(new BuffSpellEffect(Base.Spell.PersonalProtection, CombatBuffs.BuffKind.Defense, amount: 12, rounds: 4));
        SpellEffectRegistry.Register(new DamageSpellEffect(Base.Spell.KamulosGaze, baseDamage: 25, strengthScale: 3));
    }
}
