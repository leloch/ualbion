using UAlbion.Config;
using UAlbion.Formats.Assets.Sheets;
using UAlbion.Formats.Ids;
using UAlbion.Game.Combat;
using UAlbion.Game.Combat.Spells;
using Xunit;

namespace UAlbion.Game.Tests;

[Collection("SpellEffectRegistry")]
public class HealStatusEffectTests
{
    public HealStatusEffectTests()
    {
        AssetMapping.GlobalIsThreadLocal = true;
        AssetMapping.Global.Clear()
            .RegisterAssetType(typeof(Base.Spell), AssetType.Spell);
    }

    [Fact]
    public void Spell_Id_Matches_Constructor_Arg()
    {
        var effect = new HealStatusEffect(Base.Spell.HealParalysis, PlayerCondition.Paralysed);
        Assert.Equal((SpellId)Base.Spell.HealParalysis, effect.SpellId);
    }

    [Fact]
    public void Returns_Failed_When_Context_Empty()
    {
        var effect = new HealStatusEffect(Base.Spell.HealParalysis, PlayerCondition.Paralysed);
        Assert.Equal(SpellCastOutcome.Failed, effect.Apply(null));
        Assert.Equal(SpellCastOutcome.Failed, effect.Apply(new SpellCastContext()));
    }

    [Fact]
    public void Register_DjiKas_Heal_Spells_Includes_Status_And_Hp_Handlers()
    {
        SpellEffectRegistry.Clear();
        HealStatusEffect.RegisterDjiKasHealSpells();
        Assert.Equal(5, SpellEffectRegistry.RegisteredCount);

        Assert.True(SpellEffectRegistry.TryGet((SpellId)Base.Spell.HealParalysis,    out var e1));
        Assert.True(SpellEffectRegistry.TryGet((SpellId)Base.Spell.HealIntoxication, out var e2));
        Assert.True(SpellEffectRegistry.TryGet((SpellId)Base.Spell.HealBlindness,    out var e3));
        Assert.True(SpellEffectRegistry.TryGet((SpellId)Base.Spell.HealPoisoning,    out var e4));
        Assert.True(SpellEffectRegistry.TryGet((SpellId)Base.Spell.LightHealing,     out var e5));

        Assert.Equal(PlayerCondition.Paralysed,    ((HealStatusEffect)e1).Condition);
        Assert.Equal(PlayerCondition.Intoxicated,  ((HealStatusEffect)e2).Condition);
        Assert.Equal(PlayerCondition.Blind,        ((HealStatusEffect)e3).Condition);
        Assert.Equal(PlayerCondition.Poisoned,     ((HealStatusEffect)e4).Condition);
        Assert.IsType<HealHpEffect>(e5);
    }
}
