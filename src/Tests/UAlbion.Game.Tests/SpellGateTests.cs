using UAlbion.Config;
using UAlbion.Formats.Assets.Sheets;
using UAlbion.Formats.Ids;
using UAlbion.Game.Combat;
using UAlbion.Game.Combat.Spells;
using UAlbion.Game.State;
using Xunit;

namespace UAlbion.Game.Tests;

/// <summary>
/// Regression guards for the spell success gate and margin-scaled damage — the heart of
/// all spell resolution (RE batch 4/5B). Previously had zero unit coverage.
/// </summary>
public class SpellGateTests
{
    static SheetId Monster(int n) => new(AssetType.MonsterSheet, n);
    static SheetId Party(int n) => new(AssetType.PartySheet, n);

    static EffectiveCharacterSheet Sheet(SheetId id, int magicResist, int unknownE = 0, int lp = 100)
        => new(id)
        {
            UnknownE = (byte)unknownE,
            Attributes = { MagicResistance = new CharacterAttribute { Current = (ushort)magicResist } },
            Combat = { LifePoints = new CharacterAttribute { Current = (ushort)lp, Max = (ushort)lp } }
        };

    sealed class P : ICombatParticipant
    {
        public P(SheetId id, IEffectiveCharacterSheet eff) { SheetId = id; Effective = eff; }
        public int CombatPosition => 0;
        public SheetId SheetId { get; }
        public IEffectiveCharacterSheet Effective { get; }
    }

    static SpellCastContext Ctx(int m, ICombatParticipant target, System.Action<ICombatParticipant, int> applyDamage = null) =>
        new() { MasteryMultiplier = m, Target = target, ApplyDamage = applyDamage };

    [Fact]
    public void Margin_IsMasteryMinusResist_ForMonster()
    {
        var t = new P(Monster(1), Sheet(Monster(1), magicResist: 30));
        Assert.Equal(70, SpellSuccessGate.Margin(Ctx(100, t), t));
        Assert.True(SpellSuccessGate.Lands(Ctx(100, t), t));
    }

    [Fact]
    public void Gate_FailsWhenResistMeetsOrExceedsMastery()
    {
        var t = new P(Monster(1), Sheet(Monster(1), magicResist: 100));
        Assert.True(SpellSuccessGate.Margin(Ctx(100, t), t) <= 0);
        Assert.False(SpellSuccessGate.Lands(Ctx(100, t), t));
    }

    [Fact]
    public void ExclusionMaskBeatsInclusion_CritImmuneGazeTarget()
    {
        // KamulosGaze excludes class bit 0x80; a 0x80 creature is immune regardless of resist.
        var t = new P(Monster(1), Sheet(Monster(1), magicResist: 0, unknownE: 0x80));
        Assert.Equal(0, SpellSuccessGate.Margin(Ctx(100, t), t, exclMask: SpellSuccessGate.GazeImmuneMask));
    }

    [Fact]
    public void InclusionMask_UnaffectedCreatureType()
    {
        // Banish needs demon class (mask 0x44); a non-demon monster is unaffected.
        var nonDemon = new P(Monster(1), Sheet(Monster(1), magicResist: 0, unknownE: 0x01));
        Assert.Equal(0, SpellSuccessGate.Margin(Ctx(100, nonDemon), nonDemon, SpellSuccessGate.DemonClassMask));
        var demon = new P(Monster(2), Sheet(Monster(2), magicResist: 0, unknownE: 0x44));
        Assert.True(SpellSuccessGate.Margin(Ctx(100, demon), demon, SpellSuccessGate.DemonClassMask) > 0);
    }

    [Fact]
    public void DamageSpell_ScalesOnMargin_NotRawMastery()
    {
        // Fireball K=22: damage = max(1, (M - resist) * K / 100).
        int dealt = 0;
        var t = new P(Monster(1), Sheet(Monster(1), magicResist: 50));
        var ctx = Ctx(100, t, (_, amt) => dealt = amt);
        var fireball = new DamageSpellEffect(new SpellId(91), k: 22);
        Assert.Equal(SpellCastOutcome.Hit, fireball.Apply(ctx));
        Assert.Equal(11, dealt); // margin 50 * 22 / 100 = 11 (NOT 22 from raw M=100)
    }

    [Fact]
    public void DamageSpell_ResistedWhenMarginNonPositive()
    {
        int dealt = 0;
        var t = new P(Monster(1), Sheet(Monster(1), magicResist: 100));
        var ctx = Ctx(100, t, (_, amt) => dealt = amt);
        var fireball = new DamageSpellEffect(new SpellId(91), k: 22);
        Assert.Equal(SpellCastOutcome.Resisted, fireball.Apply(ctx));
        Assert.Equal(0, dealt);
    }

    [Fact]
    public void LightningStrike_IsUngated_IgnoresResist()
    {
        // The only damage spell that skips the gate: damage = max(1, raw M * 33 / 100).
        int dealt = 0;
        var t = new P(Monster(1), Sheet(Monster(1), magicResist: 999));
        var ctx = Ctx(100, t, (_, amt) => dealt = amt);
        var strike = new DamageSpellEffect(new SpellId(92), k: 33, gated: false);
        Assert.Equal(SpellCastOutcome.Hit, strike.Apply(ctx));
        Assert.Equal(33, dealt); // ignores the 999 resist entirely
    }
}
