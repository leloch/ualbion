using UAlbion.Formats.Assets.Sheets;

namespace UAlbion.Game.Combat.Spells;

/// <summary>
/// Bulk registration for the Dji-Kas spell school (the Iskai mage class, spell ids 1..30).
/// Effect magnitudes are first-pass: original formulas live in MAIN.EXE's deferred-action
/// dispatcher (see <c>_RE_COMBAT.md</c> → Phase 3.x) and will be tuned once the per-school
/// caster-info blocks at <c>0x13e1be / 0x13e1d6</c> are fully decoded.
/// </summary>
public static class DjiKasSpells
{
    /// <summary>
    /// Register every implemented Dji-Kas spell with the supplied (static) registry.
    /// Spells whose effects haven't been decoded yet are not registered — the registry's
    /// default behaviour (return <see cref="SpellCastOutcome.Failed"/>) lets the caster
    /// retry on the next AP point, matching the original engine's "stop on success" pattern.
    /// </summary>
    public static void RegisterAll()
    {
        // --- Heal school (status clears + HP heal) — magnitudes RE'd at placeholder level ---
        SpellEffectRegistry.Register(new HealStatusEffect(Base.Spell.HealParalysis,    PlayerCondition.Paralysed));
        SpellEffectRegistry.Register(new HealStatusEffect(Base.Spell.HealIntoxication, PlayerCondition.Intoxicated));
        SpellEffectRegistry.Register(new HealStatusEffect(Base.Spell.HealBlindness,    PlayerCondition.Blind));
        SpellEffectRegistry.Register(new HealStatusEffect(Base.Spell.HealPoisoning,    PlayerCondition.Poisoned));
        SpellEffectRegistry.Register(new HealHpEffect(Base.Spell.LightHealing, baseAmount: 5, strengthScale: 1));

        // --- Frost damage line (single-target, ascending magnitude) ---
        SpellEffectRegistry.Register(new DamageSpellEffect(Base.Spell.FrostSplinter,  baseDamage: 4,  strengthScale: 1));
        SpellEffectRegistry.Register(new DamageSpellEffect(Base.Spell.FrostCrystal,   baseDamage: 8,  strengthScale: 1));
        SpellEffectRegistry.Register(new DamageSpellEffect(Base.Spell.FrostAvalanche, baseDamage: 16, strengthScale: 2));

        // --- Blinding line (light damage + Blind status) — register both effects on the
        // same spell-id collapses to the last one; we keep them as pure damage spells for now,
        // and rely on a separate StatusInflictEffect-on-cast path once that's wired.
        SpellEffectRegistry.Register(new DamageSpellEffect(Base.Spell.BlindingSpark,  baseDamage: 3,  strengthScale: 1));
        SpellEffectRegistry.Register(new DamageSpellEffect(Base.Spell.BlindingRay,    baseDamage: 6,  strengthScale: 1));
        SpellEffectRegistry.Register(new DamageSpellEffect(Base.Spell.BlindingStorm,  baseDamage: 12, strengthScale: 2));

        // --- Status-inflict ---
        SpellEffectRegistry.Register(new InflictStatusEffect(Base.Spell.SleepSpores,  PlayerCondition.Asleep));
        SpellEffectRegistry.Register(new InflictStatusEffect(Base.Spell.ThornSnare,   PlayerCondition.Paralysed));

        // --- Buffs / traps / utility (PLACEHOLDER magnitudes pending RE) ---
        SpellEffectRegistry.Register(new BuffSpellEffect(Base.Spell.Hurry, CombatBuffs.BuffKind.Speed, amount: 10, rounds: 3));
        SpellEffectRegistry.Register(new TrapSpellEffect(Base.Spell.ThornTrap, damage: 8));
        SpellEffectRegistry.Register(new RemoveTrapEffect(Base.Spell.RemoveTrapDK));
        // Fungification: the wiki gives no mechanic; treated as damage + poison pending RE.
        SpellEffectRegistry.Register(new DamageSpellEffect(Base.Spell.Fungification, baseDamage: 6, strengthScale: 1));
        // Light raises the dungeon ambient level (ETM uAmbient → fragment shader multiply).
        // PLACEHOLDER: +50 percent-points and no duration decay (the original tracks Light
        // as an active spell percentage in SavedGame.ActiveSpells[0..1]).
        SpellEffectRegistry.Register(new EventSpellEffect(Base.Spell.Light,
            () => new UAlbion.Game.Events.AmbientLightEvent(50)));
        SpellEffectRegistry.Register(new UtilitySpellEffect(Base.Spell.ViewOfLife, "show monster LP in combat UI (needs combat UI hookup)"));
    }
}
