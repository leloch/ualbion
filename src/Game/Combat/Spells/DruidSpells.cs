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

        // Buffs (all CONFIRMED — fcn.0004b8a1): BERSERK costs 25 % of current LP and
        // multiplies STR + the three combat skills + base damage by 1.5 for
        // max(1, M·10/100)+1 rounds; Boasting inflicts Panicking on the target.
        SpellEffectRegistry.Register(new BerserkSpellEffect(Base.Spell.Berserk));
        SpellEffectRegistry.Register(new InflictStatusEffect(Base.Spell.Boasting, PlayerCondition.Panicking));
        SpellEffectRegistry.Register(new BuffSpellEffect(Base.Spell.MagicShield, CombatBuffs.BuffKind.Defense, amount: 0, baseDuration: 0, persistent: true)); // pct-only: defense × (1 + M/100), battle-persistent

        // Anti-demon line — CONFIRMED (handlers 0xa1aec/0xa1b20): demon-class targets
        // (creature mask 0x44) die outright iff M > MagicResist; no damage K exists.
        // The three spells differ only in target area (single/row/all).
        SpellEffectRegistry.Register(new BanishDemonEffect(Base.Spell.BanishDemon));
        SpellEffectRegistry.Register(new BanishDemonEffect(Base.Spell.BanishDemons));
        SpellEffectRegistry.Register(new BanishDemonEffect(Base.Spell.DemonExodus));
    }
}
