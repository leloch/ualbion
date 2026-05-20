using UAlbion.Formats.Ids;
using UAlbion.Game.Combat;
using Xunit;

namespace UAlbion.Game.Tests;

[Collection("SpellEffectRegistry")]
public class SpellEffectRegistryTests
{
    sealed class StubEffect : ISpellEffect
    {
        public StubEffect(SpellId id, SpellCastOutcome outcome) { SpellId = id; _outcome = outcome; }
        public SpellId SpellId { get; }
        readonly SpellCastOutcome _outcome;
        public int CallCount { get; private set; }
        public SpellCastOutcome Apply(SpellCastContext ctx) { CallCount++; return _outcome; }
    }

    [Fact]
    public void Unregistered_Spell_Returns_Failed()
    {
        SpellEffectRegistry.Clear();
        var result = SpellEffectRegistry.Cast(new SpellId(0), new SpellCastContext());
        Assert.Equal(SpellCastOutcome.Failed, result);
    }

    [Fact]
    public void Registered_Effect_Is_Invoked()
    {
        SpellEffectRegistry.Clear();
        var stub = new StubEffect(new SpellId(42), SpellCastOutcome.Hit);
        SpellEffectRegistry.Register(stub);
        var result = SpellEffectRegistry.Cast(new SpellId(42), new SpellCastContext());
        Assert.Equal(SpellCastOutcome.Hit, result);
        Assert.Equal(1, stub.CallCount);
    }

    [Fact]
    public void Re_Registering_Replaces_Handler()
    {
        SpellEffectRegistry.Clear();
        var first  = new StubEffect(new SpellId(7), SpellCastOutcome.Hit);
        var second = new StubEffect(new SpellId(7), SpellCastOutcome.Resisted);
        SpellEffectRegistry.Register(first);
        SpellEffectRegistry.Register(second);

        var result = SpellEffectRegistry.Cast(new SpellId(7), new SpellCastContext());
        Assert.Equal(SpellCastOutcome.Resisted, result);
        Assert.Equal(0, first.CallCount);
        Assert.Equal(1, second.CallCount);
    }

    [Fact]
    public void Different_Spell_Ids_Have_Separate_Handlers()
    {
        SpellEffectRegistry.Clear();
        SpellEffectRegistry.Register(new StubEffect(new SpellId(1), SpellCastOutcome.Hit));
        SpellEffectRegistry.Register(new StubEffect(new SpellId(2), SpellCastOutcome.Resisted));

        Assert.Equal(SpellCastOutcome.Hit,      SpellEffectRegistry.Cast(new SpellId(1), new SpellCastContext()));
        Assert.Equal(SpellCastOutcome.Resisted, SpellEffectRegistry.Cast(new SpellId(2), new SpellCastContext()));
        Assert.Equal(SpellCastOutcome.Failed,   SpellEffectRegistry.Cast(new SpellId(3), new SpellCastContext()));
        Assert.Equal(2, SpellEffectRegistry.RegisteredCount);
    }

    [Fact]
    public void TryGet_Returns_True_For_Registered()
    {
        SpellEffectRegistry.Clear();
        var stub = new StubEffect(new SpellId(9), SpellCastOutcome.Hit);
        SpellEffectRegistry.Register(stub);

        Assert.True(SpellEffectRegistry.TryGet(new SpellId(9), out var got));
        Assert.Same(stub, got);

        Assert.False(SpellEffectRegistry.TryGet(new SpellId(99), out _));
    }

    [Fact]
    public void Register_Null_Is_Ignored()
    {
        SpellEffectRegistry.Clear();
        SpellEffectRegistry.Register(null);
        Assert.Equal(0, SpellEffectRegistry.RegisteredCount);
    }
}
