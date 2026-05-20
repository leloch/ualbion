using UAlbion.Formats.Assets.Sheets;

namespace UAlbion.Game.Combat.Spells;

/// <summary>
/// Dji-Kantos (Iskai priest) spell registrations. Spell ids 31..60. Magnitudes are
/// first-pass — Recuperation should be stronger than HealingDC which should be stronger
/// than LightHealing. Spells whose semantics aren't yet RE'd (Teleporter / Levitation /
/// QuickWithdrawal / MapView) remain unregistered so the AP-loop retries.
/// </summary>
public static class DjiKantosSpells
{
    public static void RegisterAll()
    {
        // Healing line (ascending magnitude)
        SpellEffectRegistry.Register(new HealHpEffect(Base.Spell.HealingDC,     baseAmount: 10, strengthScale: 1));
        SpellEffectRegistry.Register(new HealHpEffect(Base.Spell.Recuperation,  baseAmount: 18, strengthScale: 2));
        SpellEffectRegistry.Register(new HealHpEffect(Base.Spell.Regeneration,  baseAmount: 3,  strengthScale: 0)); // weak per-tick heal
        SpellEffectRegistry.Register(new HealHpEffect(Base.Spell.Lifebringer,   baseAmount: 40, strengthScale: 3)); // full-heal-ish

        // Single damage spell in this school
        SpellEffectRegistry.Register(new DamageSpellEffect(Base.Spell.GoddessWrath, baseDamage: 12, strengthScale: 2));

        // Status — Irritation maps cleanly to the Irritated condition
        SpellEffectRegistry.Register(new InflictStatusEffect(Base.Spell.Irritation, PlayerCondition.Irritated));
    }
}
