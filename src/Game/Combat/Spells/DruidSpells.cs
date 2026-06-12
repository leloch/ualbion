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
        // Fire damage — K CONFIRMED from the per-spell handler
        SpellEffectRegistry.Register(new DamageSpellEffect(Base.Spell.SmallFireball, k: 16));

        // Shock inflicts Panicking — CONFIRMED (no damage component)
        SpellEffectRegistry.Register(new InflictStatusEffect(Base.Spell.Shock, PlayerCondition.Panicking));

        // Heal — 40 % of max LP at full mastery (CONFIRMED K)
        SpellEffectRegistry.Register(new HealHpEffect(Base.Spell.HealingD, k: 40));

        // Status — Panic maps to Panicking (CONFIRMED)
        SpellEffectRegistry.Register(new InflictStatusEffect(Base.Spell.Panic, PlayerCondition.Panicking));

        // Buffs. RE corrected the mechanics (fcn.0004b8a1): HURRY is the AP×2 "powered"
        // flag; BERSERK costs 25 % of LP and multiplies STR/CloseCombat by 1.5; Boasting
        // inflicts Panicking on the target (CONFIRMED). The additive buff system can't
        // express Berserk's ×1.5 yet — PLACEHOLDER additive bonus until it can.
        SpellEffectRegistry.Register(new BuffSpellEffect(Base.Spell.Berserk,     CombatBuffs.BuffKind.Attack,  amount: 10, rounds: 3));
        SpellEffectRegistry.Register(new InflictStatusEffect(Base.Spell.Boasting, PlayerCondition.Panicking));
        SpellEffectRegistry.Register(new BuffSpellEffect(Base.Spell.MagicShield, CombatBuffs.BuffKind.Defense, amount: 8,  rounds: 4));

        // Anti-demon line — K not extracted yet (PLACEHOLDER magnitudes); the "only
        // affects demons" gate needs monster-class data (PLACEHOLDER: hits anything).
        SpellEffectRegistry.Register(new DamageSpellEffect(Base.Spell.BanishDemon,  k: 30));
        SpellEffectRegistry.Register(new DamageSpellEffect(Base.Spell.BanishDemons, k: 40));
        SpellEffectRegistry.Register(new DamageSpellEffect(Base.Spell.DemonExodus,  k: 60));
    }
}
