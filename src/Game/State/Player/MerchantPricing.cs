using System;

namespace UAlbion.Game.State.Player;

/// <summary>
/// Merchant buy/sell pricing off an item's base <c>Value</c> (the "base resell value × 10",
/// stored in gold-tenths — the same unit party gold uses). The original applies a per-shop
/// multiplier (≈38–130%) that is not yet decoded, so these use documented defaults; the
/// percent is a parameter so a per-shop value can be threaded in once RE'd. Pure + testable.
/// </summary>
public static class MerchantPricing
{
    public const int DefaultBuyPercent = 100;  // pay the base value (per-shop markup RE-pending)
    public const int DefaultSellPercent = 33;  // resale returns a fraction (exact rate RE-pending)

    public static int Price(int itemValue, int percent)
        => Math.Max(0, itemValue) * Math.Max(0, percent) / 100;

    public static int BuyPrice(int itemValue, int percent = DefaultBuyPercent) => Price(itemValue, percent);
    public static int SellPrice(int itemValue, int percent = DefaultSellPercent) => Price(itemValue, percent);
}
