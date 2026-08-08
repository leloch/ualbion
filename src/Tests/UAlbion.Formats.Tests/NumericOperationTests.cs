using UAlbion.Formats.Assets.Sheets;
using UAlbion.Formats.MapEvents;
using Xunit;

namespace UAlbion.Formats.Tests;

// DATA-01 (see docs/re/RE_DATA01.md): the original has two numeric-op appliers —
// bounded stats take percentages OF THE MAX (fcn.0003e2f1), unbounded quantities
// (XP/TP/gold/rations/max-stats) take percentages OF THE CURRENT value with a
// 0x7FFF cap (fcn.0003e0d5), and gold/rations reject out-of-range changes outright
// (fcn.0003e1f8) instead of clamping.
public class NumericOperationTests
{
    [Fact]
    public void Bounded_Percentage_Uses_Max_As_Base()
    {
        // 25% of max 40 = 10, added to current 20
        Assert.Equal(30, NumericOperationExtensions.Apply(NumericOperation.AddPercentage, 20, 25, 0, 40));
        Assert.Equal(10, NumericOperationExtensions.Apply(NumericOperation.SubtractPercentage, 20, 25, 0, 40));
        // clamps to [min, max]
        Assert.Equal(40, NumericOperationExtensions.Apply(NumericOperation.AddPercentage, 35, 50, 0, 40));
        Assert.Equal(0, NumericOperationExtensions.Apply(NumericOperation.SubtractPercentage, 5, 50, 0, 40));
    }

    [Fact]
    public void Unbounded_Percentage_Uses_Current_As_Base()
    {
        // 10% of current 200 = 20
        Assert.Equal(220, NumericOperationExtensions.ApplyUnbounded(NumericOperation.AddPercentage, 200, 10, 0x7FFF));
        Assert.Equal(180, NumericOperationExtensions.ApplyUnbounded(NumericOperation.SubtractPercentage, 200, 10, 0x7FFF));
        // truncating division: 10% of 15 = 1 (not 1.5)
        Assert.Equal(16, NumericOperationExtensions.ApplyUnbounded(NumericOperation.AddPercentage, 15, 10, 0x7FFF));
        // percentage of a zero base does nothing
        Assert.Equal(0, NumericOperationExtensions.ApplyUnbounded(NumericOperation.AddPercentage, 0, 50, 0x7FFF));
    }

    [Fact]
    public void Unbounded_Clamps_To_Cap()
    {
        Assert.Equal(0x7FFF, NumericOperationExtensions.ApplyUnbounded(NumericOperation.AddAmount, 0x7FF0, 100, 0x7FFF));
        Assert.Equal(0, NumericOperationExtensions.ApplyUnbounded(NumericOperation.SubtractAmount, 10, 100, 0x7FFF));
        // SetToMaximum for an unbounded quantity means "set to the cap"
        Assert.Equal(0x7FFF, NumericOperationExtensions.ApplyUnbounded(NumericOperation.SetToMaximum, 5, 0, 0x7FFF));
        // Toggle is a no-op for numeric quantities
        Assert.Equal(123, NumericOperationExtensions.ApplyUnbounded(NumericOperation.Toggle, 123, 55, 0x7FFF));
    }

    [Fact]
    public void Gold_Style_Validity_Rejects_Instead_Of_Clamping()
    {
        // paying more than held is rejected, NOT clamped to zero
        Assert.False(NumericOperationExtensions.IsValidUnbounded(NumericOperation.SubtractAmount, 5, 10, 0x7FFF));
        Assert.True(NumericOperationExtensions.IsValidUnbounded(NumericOperation.SubtractAmount, 10, 10, 0x7FFF));
        // overflowing the cap is rejected
        Assert.False(NumericOperationExtensions.IsValidUnbounded(NumericOperation.AddAmount, 0x7FF0, 100, 0x7FFF));
        Assert.True(NumericOperationExtensions.IsValidUnbounded(NumericOperation.AddAmount, 0, 0x7FFF, 0x7FFF));
        // percentage forms validate the computed delta
        Assert.False(NumericOperationExtensions.IsValidUnbounded(NumericOperation.SubtractPercentage, 10, 200, 0x7FFF)); // 200% of 10 = 20 > 10
        Assert.True(NumericOperationExtensions.IsValidUnbounded(NumericOperation.SubtractPercentage, 10, 50, 0x7FFF));   // 50% of 10 = 5
        // min/max/toggle are always valid
        Assert.True(NumericOperationExtensions.IsValidUnbounded(NumericOperation.SetToMinimum, 5, 999, 0x7FFF));
    }

    [Fact]
    public void ApplyToMax_Percentage_Uses_Current_Max_As_Base()
    {
        var attr = new CharacterAttribute { Current = 30, Max = 40 };
        attr.ApplyToMax(NumericOperation.AddPercentage, 10); // +10% of 40 = 4
        Assert.Equal(44, attr.Max);
        Assert.Equal(30, attr.Current);

        attr.ApplyToMax(NumericOperation.SubtractPercentage, 50); // -50% of 44 = 22
        Assert.Equal(22, attr.Max);
        Assert.Equal(22, attr.Current); // current clamps down to the new max
    }
}
