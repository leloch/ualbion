using UAlbion.Formats.Assets.Sheets;

namespace UAlbion.Game.Combat.Spells;

/// <summary>
/// Druid (Sira) spell registrations. Spell ids 61..90. BanishDemon/Demons/DemonExodus
/// require monster-class introspection to detect "is a demon" — left unregistered until
/// that data path is wired.
/// </summary>
public static class DruidSpells
{
    public static void RegisterAll()
    {
        // Fire/lightning damage
        SpellEffectRegistry.Register(new DamageSpellEffect(Base.Spell.SmallFireball, baseDamage: 5,  strengthScale: 1));
        SpellEffectRegistry.Register(new DamageSpellEffect(Base.Spell.Shock,         baseDamage: 8,  strengthScale: 1));

        // Heal
        SpellEffectRegistry.Register(new HealHpEffect(Base.Spell.HealingD, baseAmount: 8, strengthScale: 1));

        // Status — Panic clearly maps to Panicking; Boasting / Berserk semantics deferred
        SpellEffectRegistry.Register(new InflictStatusEffect(Base.Spell.Panic, PlayerCondition.Panicking));
    }
}
