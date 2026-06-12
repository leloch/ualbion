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
        // Healing line — all CONFIRMED by RE: HealingDC = 40 % of max LP; Recuperation
        // (handler 0x6470b) is a magical full rest — whole party restored, Exhausted
        // cured, fatigue counter reset, gated on >8 hours awake; Regeneration and
        // Lifebringer are IDENTICAL in the original (shared continuation 0x643fa):
        // clear 9 conditions + heal max(1, MaxLP·M/100).
        SpellEffectRegistry.Register(new HealHpEffect(Base.Spell.HealingDC, k: 40));
        SpellEffectRegistry.Register(new RecuperationEffect(Base.Spell.Recuperation));
        SpellEffectRegistry.Register(new CleanseHealEffect(Base.Spell.Regeneration));
        SpellEffectRegistry.Register(new CleanseHealEffect(Base.Spell.Lifebringer));

        // Goddess' Wrath — CONFIRMED (handler 0xa1134): not a damage spell at all; it
        // kills max(1, living·M/100) randomly-chosen monsters outright, each gated by
        // M > MagicResist. At full mastery it wipes the whole monster side.
        SpellEffectRegistry.Register(new GoddessWrathEffect(Base.Spell.GoddessWrath));

        // Status — Irritation maps cleanly to the Irritated condition
        SpellEffectRegistry.Register(new InflictStatusEffect(Base.Spell.Irritation, PlayerCondition.Irritated));

        // Escape / utility (PLACEHOLDERs documented per-spell)
        SpellEffectRegistry.Register(new WithdrawEffect(Base.Spell.QuickWithdrawal)); // Ends combat as Retreat
        // Map View reveals the whole automap and opens it.
        SpellEffectRegistry.Register(new EventSpellEffect(Base.Spell.MapView,
            () => new UAlbion.Game.Entities.Map3D.RevealAutomapEvent(),
            () => new UAlbion.Game.Entities.Map3D.ShowAutomapEvent()));
        SpellEffectRegistry.Register(new UtilitySpellEffect(Base.Spell.Teleporter, "open the teleporter destination picker (needs the Goto-marker map list UI)"));
        // Levitation: the party floats over pit (no-floor) tiles until the map changes.
        SpellEffectRegistry.Register(new LevitationEffect(Base.Spell.Levitation));
    }
}
