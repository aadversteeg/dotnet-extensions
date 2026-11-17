using System;
using System.Globalization;

namespace Ave.Extensions.Documents.Pdf;

/// <summary>Formats dates the way PDF requires (ISO 32000-1 §7.9.4).</summary>
public static class PdfDate
{
    /// <summary>
    /// A date string <c>D:YYYYMMDDHHmmSSOHH'mm</c>: local date and time, then the offset from UT (O is
    /// <c>+</c>, <c>-</c> or <c>Z</c>), for example <c>D:20261115092200+01'00</c>.
    /// </summary>
    /// <param name="date">The date and its offset from UTC.</param>
    /// <returns>The date as a PDF text string.</returns>
    public static PdfString Format(DateTimeOffset date)
    {
        var local = date.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        var offset = date.Offset;
        var zone = offset == TimeSpan.Zero
            ? "Z"
            : $"{(offset < TimeSpan.Zero ? '-' : '+')}{Math.Abs(offset.Hours):00}'{Math.Abs(offset.Minutes):00}";
        return PdfString.FromText($"D:{local}{zone}");
    }
}
