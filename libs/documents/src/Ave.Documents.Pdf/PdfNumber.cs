using System;

namespace Ave.Documents.Pdf;

/// <summary>Creates the right number object for a value.</summary>
public static class PdfNumber
{
    /// <summary>An integer when the value is a whole number that fits a long, otherwise a real.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The number object.</returns>
    public static PdfObject From(double value) =>
        double.IsFinite(value) && Math.Abs(value) < 1e15 && value == Math.Floor(value)
            ? new PdfInteger((long)value)
            : new PdfReal(value);
}
