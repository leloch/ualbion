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
        // Healing line — CONFIRMED by RE: HealingDC = 40 % of max LP; Recuperation is a
        // FULL restore (the original additionally requires the target to have been awake
        // >8 hours — gate not modelled, PLACEHOLDER); Regeneration and Lifebringer are
        // IDENTICAL in the original: clear 9 conditions + heal max(1, MaxLP·M/100)
        // (the condition-cleanse part needs a combined effect — PLACEHOLDER: heal only).
        SpellEffectRegistry.Register(new HealHpEffect(Base.Spell.HealingDC,     k: 40));
        SpellEffectRegistry.Register(new HealHpEffect(Base.Spell.Recuperation,  k: 100));
        SpellEffectRegistry.Register(new HealHpEffect(Base.Spell.Regeneration,  k: 100));
        SpellEffectRegistry.Register(new HealHpEffect(Base.Spell.Lifebringer,   k: 100));

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
