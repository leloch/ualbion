using UAlbion.Config;
using UAlbion.Formats.Ids;
using UAlbion.Game.Combat;
using UAlbion.Game.Combat.Spells;
using Xunit;

namespace UAlbion.Game.Tests;

[Collection("SpellEffectRegistry")] // CombatBuffs is also process-global static state
public class CombatBuffsTests
{
    static SheetId Sheet(int n) => new(AssetType.PartySheet, n);

    [Fact]
    public void Bonus_Defaults_To_Zero()
    {
        CombatBuffs.Clear();
        Assert.Equal(0, CombatBuffs.Bonus(Sheet(1), CombatBuffs.BuffKind.Attack));
        Assert.False(CombatBuffs.IsBerserk(Sheet(1)));
    }

    [Fact]
    public void Add_And_Query_Bonus()
    {
        CombatBuffs.Clear();
        CombatBuffs.Add(Sheet(1), CombatBuffs.BuffKind.Defense, 8, 2);
        Assert.Equal(8, CombatBuffs.Bonus(Sheet(1), CombatBuffs.BuffKind.Defense));
        Assert.Equal(0, CombatBuffs.Bonus(Sheet(2), CombatBuffs.BuffKind.Defense)); // other sheet unaffected
    }

    [Fact]
    public void Buffs_Expire_After_Duration()
    {
        CombatBuffs.Clear();
        CombatBuffs.Add(Sheet(1), CombatBuffs.BuffKind.Speed, 10, 2);
        CombatBuffs.TickRound();
        Assert.Equal(10, CombatBuffs.Bonus(Sheet(1), CombatBuffs.BuffKind.Speed));
        CombatBuffs.TickRound();
        Assert.Equal(0, CombatBuffs.Bonus(Sheet(1), CombatBuffs.BuffKind.Speed));
    }

    [Fact]
    public void Recasting_Refreshes_Rather_Than_Stacks()
    {
        CombatBuffs.Clear();
        CombatBuffs.Add(Sheet(1), CombatBuffs.BuffKind.Attack, 6, 1);
        CombatBuffs.Add(Sheet(1), CombatBuffs.BuffKind.Attack, 6, 3);
        Assert.Equal(6, CombatBuffs.Bonus(Sheet(1), CombatBuffs.BuffKind.Attack)); // not 12
        CombatBuffs.TickRound();
        CombatBuffs.TickRound();
        Assert.Equal(6, CombatBuffs.Bonus(Sheet(1), CombatBuffs.BuffKind.Attack)); // refreshed duration survives
    }

    [Fact]
    public void Berserk_Flag_Set_And_Expires()
    {
        CombatBuffs.Clear();
        CombatBuffs.Add(Sheet(3), CombatBuffs.BuffKind.Berserk, 0, 1);
        Assert.True(CombatBuffs.IsBerserk(Sheet(3)));
        CombatBuffs.TickRound();
        Assert.False(CombatBuffs.IsBerserk(Sheet(3)));
    }

    [Fact]
    public void BuffSpellEffect_Applies_To_Target()
    {
        CombatBuffs.Clear();
        var effect = new BuffSpellEffect(new SpellId(61), CombatBuffs.BuffKind.Berserk, 0, 3);
        var target = new FakeParticipant(Sheet(2), null);
        var result = effect.Apply(new SpellCastContext { Caster = new FakeParticipant(Sheet(1), null), Target = target });
        Assert.Equal(SpellCastOutcome.Hit, result);
        Assert.True(CombatBuffs.IsBerserk(Sheet(2)));
    }

    [Fact]
    public void TrapEffect_Places_And_RemoveTrap_Removes()
    {
        var traps = new System.Collections.Generic.Dictionary<int, int>();
        var trap = new TrapSpellEffect(new SpellId(97), k: 10);
        var remove = new RemoveTrapEffect(new SpellId(105));

        var placeCtx = new SpellCastContext
        {
            CombatTargetPosition = 7,
            MasteryMultiplier = 100, // full mastery → trap damage = K
            PlaceTrap = (tile, dmg) => traps[tile] = dmg,
            RemoveTrap = tile => traps.Remove(tile)
        };

        Assert.Equal(SpellCastOutcome.Hit, trap.Apply(placeCtx));
        Assert.Equal(10, traps[7]); // max(1, M*K/100) = 100*10/100

        Assert.Equal(SpellCastOutcome.Hit, remove.Apply(placeCtx));
        Assert.Empty(traps);
    }

    [Fact]
    public void StealLife_Drains_Percent_Of_Target_Max_Lp()
    {
        int damaged = 0, healed = 0;
        var effect = new StealLifeEffect(new SpellId(101)); // K = 30 % of target max LP
        var target = new UAlbion.Formats.Assets.Sheets.EffectiveCharacterSheet(Sheet(2))
        {
            Combat = { LifePoints = new UAlbion.Formats.Assets.Sheets.CharacterAttribute { Max = 50 } }
        };
        var ctx = new SpellCastContext
        {
            Caster = new FakeParticipant(Sheet(1), null),
            Target = new FakeParticipant(Sheet(2), target),
            MasteryMultiplier = 100, // full mastery → drains the whole 30 %
            ApplyDamage = (_, amt) => damaged = amt,
            ApplyHeal = (_, amt) => healed = amt
        };

        Assert.Equal(SpellCastOutcome.Hit, effect.Apply(ctx));
        Assert.Equal(15, damaged); // 50 * 30 % = 15
        Assert.Equal(15, healed);
    }

    sealed class FakeParticipant : UAlbion.Game.State.ICombatParticipant
    {
        public FakeParticipant(SheetId id, UAlbion.Formats.Assets.Sheets.IEffectiveCharacterSheet effective)
        {
            SheetId = id;
            Effective = effective;
        }

        public int CombatPosition => 0;
        public SheetId SheetId { get; }
        public UAlbion.Formats.Assets.Sheets.IEffectiveCharacterSheet Effective { get; }
    }
}
