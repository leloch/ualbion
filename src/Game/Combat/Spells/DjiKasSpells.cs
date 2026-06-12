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
        // --- Heal school (status clears + HP heal) ---
        // HealParalysis (spell 16) has a NULL handler in the original engine — casting it
        // does nothing (engine quirk, CONFIRMED by RE). Kept 1:1: SP is consumed, no effect.
        SpellEffectRegistry.Register(new UtilitySpellEffect(Base.Spell.HealParalysis,
            "nothing — the original engine's function pointer for this spell is NULL"));
        SpellEffectRegistry.Register(new HealStatusEffect(Base.Spell.HealIntoxication, PlayerCondition.Intoxicated));
        SpellEffectRegistry.Register(new HealStatusEffect(Base.Spell.HealBlindness,    PlayerCondition.Blind));
        SpellEffectRegistry.Register(new HealStatusEffect(Base.Spell.HealPoisoning,    PlayerCondition.Poisoned));
        SpellEffectRegistry.Register(new HealHpEffect(Base.Spell.LightHealing, k: 25)); // 25 % of max LP at full mastery (RE'd K)

        // --- Frost damage line — K constants CONFIRMED from the per-spell handlers ---
        // ALL THREE freeze (RE batch 4): kind-1 buff base 3 (fcn.0004b8a1) — the target
        // skips max(1, margin·3/100)+1 rounds, margin = M − MagicResist from the gate.
        SpellEffectRegistry.Register(new DamageSpellEffect(Base.Spell.FrostSplinter,  k: 27, freezeBase: 3));
        SpellEffectRegistry.Register(new DamageSpellEffect(Base.Spell.FrostCrystal,   k: 18, freezeBase: 3));
        SpellEffectRegistry.Register(new DamageSpellEffect(Base.Spell.FrostAvalanche, k: 27, freezeBase: 3));

        // --- Blinding line: CONFIRMED Blind-only, NO damage component in the original ---
        SpellEffectRegistry.Register(new InflictStatusEffect(Base.Spell.BlindingSpark, PlayerCondition.Blind));
        SpellEffectRegistry.Register(new InflictStatusEffect(Base.Spell.BlindingRay,   PlayerCondition.Blind));
        SpellEffectRegistry.Register(new InflictStatusEffect(Base.Spell.BlindingStorm, PlayerCondition.Blind));

        // --- Status-inflict (mappings CONFIRMED) ---
        SpellEffectRegistry.Register(new InflictStatusEffect(Base.Spell.SleepSpores,  PlayerCondition.Asleep));
        SpellEffectRegistry.Register(new InflictStatusEffect(Base.Spell.ThornSnare,   PlayerCondition.Paralysed));

        // --- Buffs / traps / utility ---
        // Hurry doubles AP (the original "powered" flag, fcn.0004ef8b); duration base 10
        // (CONFIRMED): rounds = max(1, M·10/100) + 1.
        SpellEffectRegistry.Register(new BuffSpellEffect(Base.Spell.Hurry, CombatBuffs.BuffKind.Berserk, amount: 0, baseDuration: 10));
        SpellEffectRegistry.Register(new TrapSpellEffect(Base.Spell.ThornTrap, k: 24)); // CONFIRMED K
        SpellEffectRegistry.Register(new RemoveTrapEffect(Base.Spell.RemoveTrapDK));
        // Fungification (RE batch 4, handler 0x9fd5c): damage = max(1, margin·120/100);
        // a hit that meets/exceeds current LP kills (the mushroom transform — the kill
        // falls out of the damage path naturally; transform visual not modelled).
        SpellEffectRegistry.Register(new DamageSpellEffect(Base.Spell.Fungification, k: 120));
        // Light (RE 5C, handler 0x64359): merges {hours = M, pct = M} into the ambient
        // active-spell entry — re-casts ACCUMULATE duration and take the max percent;
        // the entry decays hourly and the dungeon light formula reads it
        // (min(100, max(spellPct, lightItems)) + Iskai bonus).
        SpellEffectRegistry.Register(new LightSpellEffect(Base.Spell.Light));
        SpellEffectRegistry.Register(new ViewOfLifeEffect(Base.Spell.ViewOfLife)); // monster LP shown on the combat grid for the battle
    }
}
