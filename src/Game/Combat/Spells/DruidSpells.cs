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

        // Status — Panic clearly maps to Panicking
        SpellEffectRegistry.Register(new InflictStatusEffect(Base.Spell.Panic, PlayerCondition.Panicking));

        // Buffs. Berserk is grounded in RE: the "powered" flag at Combatant+0x04 bit 0
        // doubles the AP attempt count (MAIN.EXE fcn.0004ef8b). Boasting / MagicShield
        // magnitudes are PLACEHOLDERs.
        SpellEffectRegistry.Register(new BuffSpellEffect(Base.Spell.Berserk,     CombatBuffs.BuffKind.Berserk, amount: 0,  rounds: 3));
        SpellEffectRegistry.Register(new BuffSpellEffect(Base.Spell.Boasting,    CombatBuffs.BuffKind.Attack,  amount: 6,  rounds: 3));
        SpellEffectRegistry.Register(new BuffSpellEffect(Base.Spell.MagicShield, CombatBuffs.BuffKind.Defense, amount: 8,  rounds: 4));

        // Anti-demon line — plain damage with ascending magnitude; the "only affects
        // demons" gate needs monster-class data (PLACEHOLDER: hits anything).
        SpellEffectRegistry.Register(new DamageSpellEffect(Base.Spell.BanishDemon,  baseDamage: 15, strengthScale: 2));
        SpellEffectRegistry.Register(new DamageSpellEffect(Base.Spell.BanishDemons, baseDamage: 20, strengthScale: 2));
        SpellEffectRegistry.Register(new DamageSpellEffect(Base.Spell.DemonExodus,  baseDamage: 30, strengthScale: 3));
    }
}
