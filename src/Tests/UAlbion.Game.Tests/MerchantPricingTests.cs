using UAlbion.Game.State.Player;
using Xunit;

namespace UAlbion.Game.Tests;

/// <summary>Merchant pricing off item Value (gold-tenths). RE (_RE_MERCHANT.md): buy and sell use
/// the SAME per-shop percent and formula price = max(1, Value * percent / 100).</summary>
public class MerchantPricingTests
{
    [Theory]
    [InlineData(1000, 100, 1000)]
    [InlineData(1000, 50, 500)]
    [InlineData(1000, 130, 1300)]
    [InlineData(1000, 38, 380)]
    [InlineData(0, 100, 1)]      // floored to 1
    [InlineData(1000, 0, 1)]     // floored to 1
    [InlineData(-50, 100, 1)]    // negative value clamps to 0 then floors to 1
    [InlineData(1000, -10, 1)]   // negative percent clamps to 0 then floors to 1
    [InlineData(5, 100, 5)]
    public void Price_ValueTimesPercentFlooredAtOne(int value, int percent, int expected)
        => Assert.Equal(expected, MerchantPricing.Price(value, percent));

    [Fact]
    public void Price_DefaultPercentIsFullValue()
        => Assert.Equal(1000, MerchantPricing.Price(1000));
}
