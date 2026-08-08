using System;

namespace UAlbion.Formats.MapEvents;

public enum NumericOperation : byte
{
    SetToMinimum = 0,
    SetToMaximum = 1,
    Toggle = 2,
    SetAmount = 3,
    AddAmount = 4,
    SubtractAmount = 5,
    AddPercentage = 6,
    SubtractPercentage = 7
}

// The original engine has TWO numeric-op appliers with different percentage semantics
// (RE'd in docs/re/RE_DATA01.md):
// - fcn.0003e2f1 for BOUNDED stats (attribute/skill/health/mana): percentage ops take a
//   percentage OF THE MAX, result clamped to [0, max]. That is Apply/Apply16 below with an
//   explicit max.
// - fcn.0003e0d5 for UNBOUNDED quantities (experience/training points/gold/rations and the
//   max-HP/SP values themselves): percentage ops take a percentage OF THE CURRENT VALUE,
//   result clamped to [0, cap] where cap is 0x7FFF (0x7FFFFFFF for experience). That is
//   ApplyUnbounded below.
public static class NumericOperationExtensions
{
    public static ushort Apply16(this NumericOperation operation, ushort existing, ushort immediate, ushort min = 0, ushort max = ushort.MaxValue)
        => (ushort)operation.Apply(existing, immediate, min, max);

    public static int Apply(this NumericOperation operation, int existing, int immediate, int min = 0, int max = int.MaxValue)
    {
        var result = operation switch
        {
            NumericOperation.SetToMinimum => min,
            NumericOperation.SetToMaximum => max,
            NumericOperation.Toggle => existing,
            NumericOperation.SetAmount => immediate,
            NumericOperation.AddAmount => existing + immediate,
            NumericOperation.SubtractAmount => existing - immediate,
            NumericOperation.AddPercentage => (int)(existing + (long)immediate * (max - min) / 100),
            NumericOperation.SubtractPercentage => (int)(existing - (long)immediate * (max - min) / 100),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
        };

        return Math.Clamp(result, min, max);
    }

    /// <summary>
    /// Applier for quantities without a natural maximum (fcn.0003e0d5): percentage ops
    /// use the CURRENT value as the base; SetToMaximum means "set to the cap".
    /// Truncating integer division, result clamped to [0, cap].
    /// </summary>
    public static int ApplyUnbounded(this NumericOperation operation, int existing, int immediate, int cap)
    {
        long result = operation switch
        {
            NumericOperation.SetToMinimum => 0,
            NumericOperation.SetToMaximum => cap,
            NumericOperation.Toggle => existing, // no-op for numeric quantities
            NumericOperation.SetAmount => immediate,
            NumericOperation.AddAmount => (long)existing + immediate,
            NumericOperation.SubtractAmount => (long)existing - immediate,
            NumericOperation.AddPercentage => existing + (long)immediate * existing / 100,
            NumericOperation.SubtractPercentage => existing - (long)immediate * existing / 100,
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
        };

        return (int)Math.Clamp(result, 0, cap);
    }

    /// <summary>
    /// The gold/rations validity pre-check (fcn.0003e1f8): the original REJECTS a change
    /// that would leave [0, cap] — nothing is written and the event is flagged failed —
    /// rather than clamping. Paying a cost you can't afford must fail the chain branch,
    /// not silently zero your gold.
    /// </summary>
    public static bool IsValidUnbounded(this NumericOperation operation, int existing, int immediate, int cap)
        => operation switch
        {
            NumericOperation.SetAmount => immediate <= cap,
            NumericOperation.AddAmount => (long)existing + immediate <= cap,
            NumericOperation.SubtractAmount => existing >= immediate,
            NumericOperation.AddPercentage => existing + (long)immediate * existing / 100 <= cap,
            NumericOperation.SubtractPercentage => existing >= (long)immediate * existing / 100,
            _ => true // SetToMinimum / SetToMaximum / Toggle are always in range
        };
}
