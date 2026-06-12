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

        // Lightning line — K constants CONFIRMED. LightningStrike is the ONLY damage
        // spell that skips the success gate (RE 5B: raw M·33/100, no MagicResist check).
        SpellEffectRegistry.Register(new DamageSpellEffect(Base.Spell.LightningStrike, k: 33, gated: false));
        SpellEffectRegistry.Register(new DamageSpellEffect(Base.Spell.Thunderbolt,     k: 36));
        SpellEffectRegistry.Register(new DamageSpellEffect(Base.Spell.Thunderstorm,    k: 40));

        // Traps and mines — placed on target tile(s), trigger when stepped on.
        // All four K constants CONFIRMED; the Big variants place on the ENTIRE 6-tile
        // row of the picked tile (RE 5B area fn 0x5f60b) with the same K.
        // (Trap = visible to enemies, mine = hidden; not visually distinguished yet.)
        SpellEffectRegistry.Register(new TrapSpellEffect(Base.Spell.LightningTrap,    k: 30));
        SpellEffectRegistry.Register(new TrapSpellEffect(Base.Spell.BigLightningTrap, k: 30, wholeRow: true));
        SpellEffectRegistry.Register(new TrapSpellEffect(Base.Spell.LightningMine,    k: 42));
        SpellEffectRegistry.Register(new TrapSpellEffect(Base.Spell.BigLightningMine, k: 42, wholeRow: true));
        SpellEffectRegistry.Register(new RemoveTrapEffect(Base.Spell.RemoveTrapKK));

        // Drains (30 % of target max — CONFIRMED) + defense + gaze
        SpellEffectRegistry.Register(new StealLifeEffect(Base.Spell.StealLife));
        SpellEffectRegistry.Register(new StealMagicEffect(Base.Spell.StealMagic));
        SpellEffectRegistry.Register(new BuffSpellEffect(Base.Spell.PersonalProtection, CombatBuffs.BuffKind.Defense, amount: 0, baseDuration: 0, persistent: true)); // pct-only: defense × (1 + M/100), battle-persistent
        // KamulosGaze — CONFIRMED (RE batch 4): deterministic instant kill, not damage;
        // creature class bit 0x80 grants immunity.
        SpellEffectRegistry.Register(new KamulosGazeEffect(Base.Spell.KamulosGaze));
    }
}
