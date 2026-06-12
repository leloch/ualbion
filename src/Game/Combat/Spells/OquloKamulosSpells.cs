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
        // Fire line — K constants CONFIRMED from the per-spell handlers
        SpellEffectRegistry.Register(new DamageSpellEffect(Base.Spell.Fireball, k: 22));
        SpellEffectRegistry.Register(new DamageSpellEffect(Base.Spell.FireRain, k: 22));
        SpellEffectRegistry.Register(new DamageSpellEffect(Base.Spell.FireHail, k: 20));

        // Lightning line — K constants CONFIRMED
        SpellEffectRegistry.Register(new DamageSpellEffect(Base.Spell.LightningStrike, k: 33));
        SpellEffectRegistry.Register(new DamageSpellEffect(Base.Spell.Thunderbolt,     k: 36));
        SpellEffectRegistry.Register(new DamageSpellEffect(Base.Spell.Thunderstorm,    k: 40));

        // Traps and mines — placed on a target tile, trigger when stepped on.
        // LightningTrap 30 / LightningMine 42 are CONFIRMED K; the Big variants weren't
        // extracted (PLACEHOLDER). (Trap = visible to enemies in the original, mine =
        // hidden; UAlbion doesn't distinguish yet — PLACEHOLDER.)
        SpellEffectRegistry.Register(new TrapSpellEffect(Base.Spell.LightningTrap,    k: 30));
        SpellEffectRegistry.Register(new TrapSpellEffect(Base.Spell.BigLightningTrap, k: 42));
        SpellEffectRegistry.Register(new TrapSpellEffect(Base.Spell.LightningMine,    k: 42));
        SpellEffectRegistry.Register(new TrapSpellEffect(Base.Spell.BigLightningMine, k: 56));
        SpellEffectRegistry.Register(new RemoveTrapEffect(Base.Spell.RemoveTrapKK));

        // Drains (30 % of target max — CONFIRMED) + defense + gaze
        SpellEffectRegistry.Register(new StealLifeEffect(Base.Spell.StealLife));
        SpellEffectRegistry.Register(new StealMagicEffect(Base.Spell.StealMagic));
        SpellEffectRegistry.Register(new BuffSpellEffect(Base.Spell.PersonalProtection, CombatBuffs.BuffKind.Defense, amount: 12, rounds: 4));
        SpellEffectRegistry.Register(new DamageSpellEffect(Base.Spell.KamulosGaze, k: 50)); // K not extracted — PLACEHOLDER
    }
}
