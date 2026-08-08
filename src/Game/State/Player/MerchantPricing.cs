using System;

namespace UAlbion.Game.State.Player;

/// <summary>
/// Merchant buy/sell pricing off an item's base <c>Value</c> (the "base resell value × 10",
/// stored in gold-tenths — the same unit party gold uses).
///
/// RE (docs/re/RE_MERCHANT.md, MAIN.EXE 0x68174 / 0x68328 / 0x2c10a): Albion shops have NO buy/sell
/// spread — both directions use the SAME per-shop percent and the SAME formula
/// <c>price = max(1, Value * percent / 100)</c> (truncating integer division, floored to 1).
/// The percent is the opening <c>PlaceActionEvent.Unk6</c> (≈38–130 %); 100 returns Value.
/// Pure + testable.
/// </summary>
public static class MerchantPricing
{
    public const int DefaultPercent = 100; // 100 % → price == Value (used until the shop percent is plumbed)

    public static int Price(int itemValue, int percent = DefaultPercent)
        => Math.Max(1, Math.Max(0, itemValue) * Math.Max(0, percent) / 100);
}
