namespace UAlbion.Game.State.Player;

/// <summary>
/// Carry-weight (encumbrance) gate. The original Albion halts party movement while a member is
/// over their carry weight (MaxWeight = Strength * CarryWeightPerStrength) and shows
/// "&lt;name&gt; is carrying too much". The weight numbers were already computed on the Effective
/// sheet (EffectiveSheetCalculator); this just exposes the over-weight test for the 2D/3D movers.
/// </summary>
public static class PartyEncumbrance
{
    /// <summary>The first party member that is over their carry weight, or null if none.</summary>
    public static IPlayer FirstOverloaded(IParty party)
    {
        if (party == null)
            return null;

        foreach (var member in party.StatusBarOrder)
        {
            var sheet = member?.Apparent;
            // Guard MaxWeight > 0: a zero/uncomputed max must never strand the party (robustness).
            if (sheet != null && sheet.MaxWeight > 0 && sheet.TotalWeight > sheet.MaxWeight)
                return member;
        }

        return null;
    }
}
