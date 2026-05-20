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
        SpellEffectRegistry.Register(new DamageSpellEffect(Base.Spell.ZombiePoisonBreeze, baseDamage: 6,  strengthScale: 1));
        SpellEffectRegistry.Register(new DamageSpellEffect(Base.Spell.ZombiePlagueBreeze, baseDamage: 10, strengthScale: 1));

        SpellEffectRegistry.Register(new InflictStatusEffect(Base.Spell.ZombiePanic,      PlayerCondition.Panicking));
        SpellEffectRegistry.Register(new InflictStatusEffect(Base.Spell.ZombieIrritation, PlayerCondition.Irritated));
    }
}
