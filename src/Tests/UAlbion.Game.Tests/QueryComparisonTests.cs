using UAlbion.Formats;
using UAlbion.Formats.MapEvents;
using Xunit;

namespace UAlbion.Game.Tests;

/// <summary>
/// The comparator backing every comparison-style query opcode (gold, rations, hour, map,
/// schedule-tick 0x1E, facing 0x0C, NPC X/Y). RE: dispatcher 0x3ca53 → FormatUtil.Compare.
/// The unusual case is <see cref="QueryOperation.NonZero"/> (immediate ignored) and the
/// default arm, which the original treats as an unconditional pass.
/// </summary>
public class QueryComparisonTests
{
    [Theory]
    [InlineData(QueryOperation.LessThan, 4, 5, true)]
    [InlineData(QueryOperation.LessThan, 5, 5, false)]
    [InlineData(QueryOperation.LessThanOrEqual, 5, 5, true)]
    [InlineData(QueryOperation.LessThanOrEqual, 6, 5, false)]
    [InlineData(QueryOperation.Equals, 5, 5, true)]
    [InlineData(QueryOperation.Equals, 4, 5, false)]
    [InlineData(QueryOperation.GreaterThanOrEqual, 5, 5, true)]
    [InlineData(QueryOperation.GreaterThanOrEqual, 4, 5, false)]
    [InlineData(QueryOperation.GreaterThan, 6, 5, true)]
    [InlineData(QueryOperation.GreaterThan, 5, 5, false)]
    public void Compare_RelationalOps(QueryOperation op, int value, int immediate, bool expected)
        => Assert.Equal(expected, FormatUtil.Compare(op, value, immediate));

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(-3, true)]
    public void Compare_NonZero_IgnoresImmediate(int value, bool expected)
        => Assert.Equal(expected, FormatUtil.Compare(QueryOperation.NonZero, value, immediate: 999));

    [Fact]
    public void Compare_UnknownOperation_PassesUnconditionally()
        => Assert.True(FormatUtil.Compare((QueryOperation)99, value: 0, immediate: 12345));
}
