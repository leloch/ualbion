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
        // Healing line. HealingDC = 40 % of max LP at full mastery (CONFIRMED K);
        // the others' K constants weren't extracted yet (PLACEHOLDER percentages).
        SpellEffectRegistry.Register(new HealHpEffect(Base.Spell.HealingDC,     k: 40));
        SpellEffectRegistry.Register(new HealHpEffect(Base.Spell.Recuperation,  k: 60));
        SpellEffectRegistry.Register(new HealHpEffect(Base.Spell.Regeneration,  k: 10)); // weak per-tick heal
        SpellEffectRegistry.Register(new HealHpEffect(Base.Spell.Lifebringer,   k: 100)); // full heal

        // Single damage spell in this school — K not extracted (open item, PLACEHOLDER)
        SpellEffectRegistry.Register(new DamageSpellEffect(Base.Spell.GoddessWrath, k: 35));

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
