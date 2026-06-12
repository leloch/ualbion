using UAlbion.Config;
using UAlbion.Formats.Ids;
using UAlbion.Game.Combat;
using UAlbion.Game.Combat.Spells;
using Xunit;

namespace UAlbion.Game.Tests;

[Collection("SpellEffectRegistry")]
public class AllSpellSchoolsTests
{
    public AllSpellSchoolsTests()
    {
        AssetMapping.GlobalIsThreadLocal = true;
        AssetMapping.Global.Clear()
            .RegisterAssetType(typeof(Base.Spell), AssetType.Spell);
    }

    static void RegisterEverything()
    {
        SpellEffectRegistry.Clear();
        DjiKasSpells.RegisterAll();
        DjiKantosSpells.RegisterAll();
        DruidSpells.RegisterAll();
        OquloKamulosSpells.RegisterAll();
        ZombieMagicSpells.RegisterAll();
    }

    [Fact]
    public void All_Schools_Register_Without_Throwing()
    {
        RegisterEverything();
        Assert.True(SpellEffectRegistry.RegisteredCount > 0);
    }

    [Theory]
    // Dji-Kantos
    [InlineData(nameof(Base.Spell.HealingDC))]
    [InlineData(nameof(Base.Spell.Recuperation))]
    [InlineData(nameof(Base.Spell.Regeneration))]
    [InlineData(nameof(Base.Spell.Lifebringer))]
    [InlineData(nameof(Base.Spell.GoddessWrath))]
    [InlineData(nameof(Base.Spell.Irritation))]
    // Druid
    [InlineData(nameof(Base.Spell.SmallFireball))]
    [InlineData(nameof(Base.Spell.Shock))]
    [InlineData(nameof(Base.Spell.HealingD))]
    [InlineData(nameof(Base.Spell.Panic))]
    // Oqulo Kamulos
    [InlineData(nameof(Base.Spell.Fireball))]
    [InlineData(nameof(Base.Spell.FireRain))]
    [InlineData(nameof(Base.Spell.FireHail))]
    [InlineData(nameof(Base.Spell.LightningStrike))]
    [InlineData(nameof(Base.Spell.Thunderbolt))]
    [InlineData(nameof(Base.Spell.Thunderstorm))]
    // Zombie magic
    [InlineData(nameof(Base.Spell.ZombiePoisonBreeze))]
    [InlineData(nameof(Base.Spell.ZombiePlagueBreeze))]
    [InlineData(nameof(Base.Spell.ZombiePanic))]
    [InlineData(nameof(Base.Spell.ZombieIrritation))]
    public void Spell_Is_Registered(string spellName)
    {
        RegisterEverything();
        var spell = (Base.Spell)System.Enum.Parse(typeof(Base.Spell), spellName);
        Assert.True(SpellEffectRegistry.TryGet((SpellId)spell, out _),
            $"Expected {spellName} to be registered.");
    }

    [Theory]
    // Every named spell now has a registered handler — buffs, traps, drains, escape and
    // utility placeholders included. (Was a fail-through list before 2026-06-12.)
    [InlineData(nameof(Base.Spell.Teleporter))]
    [InlineData(nameof(Base.Spell.Levitation))]
    [InlineData(nameof(Base.Spell.MapView))]
    [InlineData(nameof(Base.Spell.QuickWithdrawal))]
    [InlineData(nameof(Base.Spell.Berserk))]
    [InlineData(nameof(Base.Spell.BanishDemon))]
    [InlineData(nameof(Base.Spell.MagicShield))]
    [InlineData(nameof(Base.Spell.StealLife))]
    [InlineData(nameof(Base.Spell.KamulosGaze))]
    [InlineData(nameof(Base.Spell.Hurry))]
    [InlineData(nameof(Base.Spell.ThornTrap))]
    [InlineData(nameof(Base.Spell.RemoveTrapDK))]
    [InlineData(nameof(Base.Spell.RemoveTrapKK))]
    [InlineData(nameof(Base.Spell.LightningTrap))]
    [InlineData(nameof(Base.Spell.BigLightningMine))]
    [InlineData(nameof(Base.Spell.StealMagic))]
    [InlineData(nameof(Base.Spell.PersonalProtection))]
    [InlineData(nameof(Base.Spell.Boasting))]
    [InlineData(nameof(Base.Spell.Fungification))]
    [InlineData(nameof(Base.Spell.Light))]
    [InlineData(nameof(Base.Spell.ViewOfLife))]
    public void Every_Named_Spell_Is_Registered(string spellName)
    {
        RegisterEverything();
        var spell = (Base.Spell)System.Enum.Parse(typeof(Base.Spell), spellName);
        Assert.True(SpellEffectRegistry.TryGet((SpellId)spell, out _),
            $"Expected {spellName} to have a registered effect handler.");
    }

    [Fact]
    public void Fire_Hail_Hits_Harder_Than_Fire_Ball()
    {
        RegisterEverything();
        Assert.True(SpellEffectRegistry.TryGet((SpellId)Base.Spell.Fireball, out var ball));
        Assert.True(SpellEffectRegistry.TryGet((SpellId)Base.Spell.FireHail, out var hail));
        Assert.True(((DamageSpellEffect)hail).BaseDamage > ((DamageSpellEffect)ball).BaseDamage);
    }

    [Fact]
    public void Thunderstorm_Hits_Harder_Than_Lightning_Strike()
    {
        RegisterEverything();
        Assert.True(SpellEffectRegistry.TryGet((SpellId)Base.Spell.LightningStrike, out var strike));
        Assert.True(SpellEffectRegistry.TryGet((SpellId)Base.Spell.Thunderstorm,    out var storm));
        Assert.True(((DamageSpellEffect)storm).BaseDamage > ((DamageSpellEffect)strike).BaseDamage);
    }
}
