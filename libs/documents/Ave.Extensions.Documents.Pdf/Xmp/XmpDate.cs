using System;
using System.Globalization;

namespace Ave.Extensions.Documents.Pdf.Xmp;

/// <summary>Formats dates as XMP Date values (ISO 16684-1 §8.2.1.2, "a subset of the formats as defined in Date and Time Formats", the W3C profile of ISO 8601).</summary>
public static class XmpDate
{
    /// <summary>
    /// <c>YYYY-MM-DDThh:mm:ssTZD</c>, the time zone written as <c>Z</c> for UTC or <c>+hh:mm</c> / <c>-hh:mm</c>: the
    /// same instant and offset that <see cref="PdfDate.Format"/> writes in the document information, so the two
    /// stay "equivalent" as PDF/A asks for metadata kept in both places.
    /// </summary>
    /// <param name="date">The date and its offset from UTC.</param>
    /// <returns>The XMP text.</returns>
    public static XmpText Format(DateTimeOffset date)
    {
        var local = date.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
        var offset = date.Offset;
        var zone = offset == TimeSpan.Zero
            ? "Z"
            : $"{(offset < TimeSpan.Zero ? '-' : '+')}{Math.Abs(offset.Hours):00}:{Math.Abs(offset.Minutes):00}";
        return new XmpText(local + zone);
    }
}
