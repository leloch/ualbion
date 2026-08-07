using UAlbion.Config;
using UAlbion.Formats.Assets.Sheets;
using UAlbion.Formats.Ids;
using UAlbion.Game.Combat;
using UAlbion.Game.Combat.Spells;
using Xunit;

namespace UAlbion.Game.Tests;

// All tests that touch the static SpellEffectRegistry must share this collection so xUnit
// serialises them — otherwise interleaved Clear/Register calls race across test classes.
[Collection("SpellEffectRegistry")]
public class DjiKasSpellsTests
{
    public DjiKasSpellsTests()
    {
        AssetMapping.GlobalIsThreadLocal = true;
        AssetMapping.Global.Clear()
            .RegisterAssetType(typeof(Base.Spell), AssetType.Spell);
    }

    [Fact]
    public void RegisterAll_Covers_Implemented_DjiKas_Spells()
    {
        SpellEffectRegistry.Clear();
        DjiKasSpells.RegisterAll();

        // 5 heal-school + 3 frost + 3 blinding + 2 status-inflict
        // + Hurry/ThornTrap/RemoveTrapDK/Fungification/Light/ViewOfLife = 19
        Assert.Equal(19, SpellEffectRegistry.RegisteredCount);
    }

    [Theory]
    [InlineData(nameof(Base.Spell.HealParalysis),    typeof(UtilitySpellEffect))] // NULL handler in the original - does nothing
    [InlineData(nameof(Base.Spell.HealIntoxication), typeof(HealStatusEffect))]
    [InlineData(nameof(Base.Spell.HealBlindness),    typeof(HealStatusEffect))]
    [InlineData(nameof(Base.Spell.HealPoisoning),    typeof(HealStatusEffect))]
    [InlineData(nameof(Base.Spell.LightHealing),     typeof(HealHpEffect))]
    [InlineData(nameof(Base.Spell.FrostSplinter),    typeof(DamageSpellEffect))]
    [InlineData(nameof(Base.Spell.FrostCrystal),     typeof(DamageSpellEffect))]
    [InlineData(nameof(Base.Spell.FrostAvalanche),   typeof(DamageSpellEffect))]
    [InlineData(nameof(Base.Spell.BlindingSpark),    typeof(InflictStatusEffect))] // Blind-only, no damage (RE'd)
    [InlineData(nameof(Base.Spell.BlindingRay),      typeof(InflictStatusEffect))]
    [InlineData(nameof(Base.Spell.BlindingStorm),    typeof(InflictStatusEffect))]
    [InlineData(nameof(Base.Spell.SleepSpores),      typeof(InflictStatusEffect))]
    [InlineData(nameof(Base.Spell.ThornSnare),       typeof(InflictStatusEffect))]
    public void RegisterAll_Wires_Each_Spell_To_Expected_Handler(string spellName, System.Type expected)
    {
        SpellEffectRegistry.Clear();
        DjiKasSpells.RegisterAll();
        var spell = (Base.Spell)System.Enum.Parse(typeof(Base.Spell), spellName);
        Assert.True(SpellEffectRegistry.TryGet((SpellId)spell, out var effect));
        Assert.IsType(expected, effect);
    }

    [Fact]
    public void Hurry_With_Empty_Context_Falls_Through_To_Failed()
    {
        SpellEffectRegistry.Clear();
        DjiKasSpells.RegisterAll();
        // Hurry is registered (AP-double buff) but an empty context has no target/caster.
        var outcome = SpellEffectRegistry.Cast((SpellId)Base.Spell.Hurry, new SpellCastContext());
        Assert.Equal(SpellCastOutcome.Failed, outcome);
    }

    [Fact]
    public void Frost_Line_Uses_The_REd_K_Constants()
    {
        // RE'd per-spell K constants: Splinter 27, Crystal 18, Avalanche 27. Splinter and
        // Avalanche share K — the higher tiers differ in targeting (single vs row/all),
        // not in per-target damage.
        SpellEffectRegistry.Clear();
        DjiKasSpells.RegisterAll();
        Assert.True(SpellEffectRegistry.TryGet((SpellId)Base.Spell.FrostSplinter,  out var splinter));
        Assert.True(SpellEffectRegistry.TryGet((SpellId)Base.Spell.FrostCrystal,   out var crystal));
        Assert.True(SpellEffectRegistry.TryGet((SpellId)Base.Spell.FrostAvalanche, out var avalanche));
        Assert.Equal(27, ((DamageSpellEffect)splinter).K);
        Assert.Equal(18, ((DamageSpellEffect)crystal).K);
        Assert.Equal(27, ((DamageSpellEffect)avalanche).K);
    }

    [Fact]
    public void Inflict_Status_Effect_Stores_Spell_Id_And_Condition()
    {
        var effect = new InflictStatusEffect(Base.Spell.SleepSpores, PlayerCondition.Asleep);
        Assert.Equal((SpellId)Base.Spell.SleepSpores, effect.SpellId);
        Assert.Equal(PlayerCondition.Asleep, effect.Condition);
    }

    [Fact]
    public void Inflict_Status_Effect_Returns_Failed_On_Null_Context()
        => Assert.Equal(SpellCastOutcome.Failed,
            new InflictStatusEffect(Base.Spell.SleepSpores, PlayerCondition.Asleep).Apply(null));

    [Fact]
    public void Damage_Spell_Effect_Stores_Constructor_Args()
    {
        var effect = new DamageSpellEffect(Base.Spell.FrostSplinter, k: 27);
        Assert.Equal((SpellId)Base.Spell.FrostSplinter, effect.SpellId);
        Assert.Equal(27, effect.K);
    }

    [Fact]
    public void Damage_Spell_Effect_Returns_Failed_On_Null_Context()
        => Assert.Equal(SpellCastOutcome.Failed,
            new DamageSpellEffect(Base.Spell.FrostSplinter, 4).Apply(null));
}

