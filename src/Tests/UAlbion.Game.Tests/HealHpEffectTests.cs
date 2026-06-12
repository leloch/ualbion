using UAlbion.Config;
using UAlbion.Formats.Ids;
using UAlbion.Game.Combat;
using UAlbion.Game.Combat.Spells;
using Xunit;

namespace UAlbion.Game.Tests;

public class HealHpEffectTests
{
    public HealHpEffectTests()
    {
        AssetMapping.GlobalIsThreadLocal = true;
        AssetMapping.Global.Clear()
            .RegisterAssetType(typeof(Base.Spell), AssetType.Spell);
    }

    [Fact]
    public void Spell_Id_Matches_Constructor_Arg()
    {
        var effect = new HealHpEffect(Base.Spell.LightHealing, k: 25);
        Assert.Equal((SpellId)Base.Spell.LightHealing, effect.SpellId);
    }

    [Fact]
    public void Stores_K()
    {
        var effect = new HealHpEffect(Base.Spell.LightHealing, k: 40);
        Assert.Equal(40, effect.K);
    }

    [Fact]
    public void Returns_Failed_When_Context_Is_Null()
    {
        var effect = new HealHpEffect(Base.Spell.LightHealing, 25);
        Assert.Equal(SpellCastOutcome.Failed, effect.Apply(null));
    }

    [Fact]
    public void Returns_Failed_When_Context_Has_No_Target()
    {
        var effect = new HealHpEffect(Base.Spell.LightHealing, 25);
        Assert.Equal(SpellCastOutcome.Failed, effect.Apply(new SpellCastContext()));
    }
}
