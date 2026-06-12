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

        // Escape / utility (PLACEHOLDERs documented per-spell)
        SpellEffectRegistry.Register(new WithdrawEffect(Base.Spell.QuickWithdrawal)); // Ends combat as Retreat
        // Map View reveals the whole automap and opens it.
        SpellEffectRegistry.Register(new EventSpellEffect(Base.Spell.MapView,
            () => new UAlbion.Game.Entities.Map3D.RevealAutomapEvent(),
            () => new UAlbion.Game.Entities.Map3D.ShowAutomapEvent()));
        SpellEffectRegistry.Register(new UtilitySpellEffect(Base.Spell.Teleporter, "open the teleporter destination picker (needs the Goto-marker map list UI)"));
        SpellEffectRegistry.Register(new UtilitySpellEffect(Base.Spell.Levitation, "float over pit tiles in 3D maps (needs pit-tile collision exemption)"));
    }
}
