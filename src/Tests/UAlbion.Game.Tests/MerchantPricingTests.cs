using UAlbion.Game.State.Player;
using Xunit;

namespace UAlbion.Game.Tests;

/// <summary>Merchant buy/sell pricing off item Value (gold-tenths). Per-shop multiplier is a
/// parameter pending RE; defaults are buy 100% / sell 33%.</summary>
public class MerchantPricingTests
{
    [Theory]
    [InlineData(1000, 100, 1000)]
    [InlineData(1000, 50, 500)]
    [InlineData(1000, 130, 1300)]
    [InlineData(0, 100, 0)]
    [InlineData(1000, 0, 0)]
    [InlineData(-50, 100, 0)]   // negative value clamps to 0
    [InlineData(1000, -10, 0)]  // negative percent clamps to 0
    public void Price_ValueTimesPercent(int value, int percent, int expected)
        => Assert.Equal(expected, MerchantPricing.Price(value, percent));

    [Fact]
    public void BuyPrice_DefaultsToFullValue()
        => Assert.Equal(1000, MerchantPricing.BuyPrice(1000));

    [Fact]
    public void SellPrice_DefaultsToAThirdAndIsLessThanBuy()
    {
        Assert.Equal(330, MerchantPricing.SellPrice(1000));
        Assert.True(MerchantPricing.SellPrice(1000) < MerchantPricing.BuyPrice(1000));
    }
}
