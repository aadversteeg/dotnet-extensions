using System;

namespace Ave.Extensions.Documents.Pdf;

/// <summary>Creates the right number object for a value.</summary>
public static class PdfNumber
{
    /// <summary>
    /// An integer when the value, rounded to the <see cref="PdfSerializer.RealDecimals"/> decimals a real is written
    /// with, is a whole number in the integer range of ISO 32000-1 Annex C (Table C.1: -2^31 to 2^31 - 1, a C# int);
    /// otherwise a real. A whole number outside that range is therefore a real, which the serializer writes with a
    /// decimal point, so no file holds an integer outside the range (veraPDF rule 6.1.13-1). A value no real can hold
    /// either is refused when it is written (<see cref="PdfSerializer.MaxReal"/>).
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The number object.</returns>
    public static PdfObject From(double value)
    {
        var rounded = double.IsFinite(value) ? Math.Round(value, PdfSerializer.RealDecimals, MidpointRounding.AwayFromZero) : value;
        return rounded == Math.Floor(rounded) && rounded is >= int.MinValue and <= int.MaxValue
            ? new PdfInteger((int)rounded)
            : new PdfReal(value);
    }
}
