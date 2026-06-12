using UAlbion.Formats.Assets.Sheets;

namespace UAlbion.Game.Combat.Spells;

/// <summary>
/// Zombie-magic spells (mob-only school used by undead-type monsters). Spell ids 151..154.
/// PoisonBreeze and PlagueBreeze likely combine damage + a status condition; for now the
/// damage component is registered and the status proc is deferred until the original's
/// proc-chance is RE'd.
/// </summary>
public static class ZombieMagicSpells
{
    public static void RegisterAll()
    {
        // CONFIRMED by RE: the breezes are condition-only, no damage component.
        SpellEffectRegistry.Register(new InflictStatusEffect(Base.Spell.ZombiePoisonBreeze, PlayerCondition.Poisoned));
        SpellEffectRegistry.Register(new InflictStatusEffect(Base.Spell.ZombiePlagueBreeze, PlayerCondition.Ill)); // INFERRED mapping (plague → Ill)

        SpellEffectRegistry.Register(new InflictStatusEffect(Base.Spell.ZombiePanic,      PlayerCondition.Panicking));
        SpellEffectRegistry.Register(new InflictStatusEffect(Base.Spell.ZombieIrritation, PlayerCondition.Irritated));
    }
}
