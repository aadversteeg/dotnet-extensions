using System;
using System.Globalization;

namespace Ave.Extensions.Documents.Core;

/// <summary>
/// Formats numbers the same way on every machine. Used by every text output of the engine: the PDF writer and
/// the text serialisations of the intermediate models. Same input therefore gives the same bytes everywhere.
/// </summary>
public static class InvariantNumber
{
    /// <summary>
    /// Formats a number with at most <paramref name="maxDecimals"/> decimals, no thousands separator, no exponent
    /// and a dot as decimal separator, for example <c>12.5</c>, <c>-3</c>, <c>0.333</c>.
    /// </summary>
    /// <remarks>
    /// Never writes an exponent because PDF has no exponent notation for numbers (ISO 32000-1 §7.3.3).
    /// Rounds half away from zero and writes <c>0</c>, never <c>-0</c>: .NET formats negative zero as "-0",
    /// which would make two equal layouts serialise differently. A value that is not finite is written as
    /// <c>NaN</c>, <c>Infinity</c> or <c>-Infinity</c> so that diagnostics can show it; such text is not valid
    /// PDF, which is why the PDF writer refuses non-finite numbers before it formats anything.
    /// </remarks>
    /// <param name="value">The number.</param>
    /// <param name="maxDecimals">The largest number of decimals to write (0–15).</param>
    /// <returns>The formatted number.</returns>
    public static string Format(double value, int maxDecimals = 3)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxDecimals);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxDecimals, 15);
        if (!double.IsFinite(value))
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        var rounded = Math.Round(value, maxDecimals, MidpointRounding.AwayFromZero);
        var withoutNegativeZero = rounded == 0 ? 0 : rounded;
        var pattern = maxDecimals == 0 ? "0" : "0." + new string('#', maxDecimals);
        return withoutNegativeZero.ToString(pattern, CultureInfo.InvariantCulture);
    }
}
