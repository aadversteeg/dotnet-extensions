using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Ave.Documents.Core;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Documents.Pdf;

/// <summary>
/// Writes direct objects in PDF syntax (ISO 32000-1 §7.3). Pure: same object, same bytes. Every output byte is
/// ASCII, because names and literal strings are escaped and binary strings are written in hexadecimal.
/// </summary>
public static class PdfSerializer
{
    /// <summary>Decimals written for real numbers. 0.0001 pt is far below what any device can show.</summary>
    public const int RealDecimals = 4;

    /// <summary>Writes an object, after checking that every number in it is finite.</summary>
    /// <param name="value">The object.</param>
    /// <returns>Its PDF syntax, or a <see cref="PdfErrorCodes.Write.InvalidNumber"/> error.</returns>
    public static Result<string, Error> Serialize(PdfObject value) =>
        FindNonFinite(value) is { } invalid
            ? Result<string, Error>.Failure(InvalidNumber(invalid))
            : Result<string, Error>.Success(Write(value));

    /// <summary>The first real number in an object tree that is NaN or infinite, if any.</summary>
    /// <param name="value">The object tree.</param>
    /// <returns>The invalid value, or null when all numbers are finite.</returns>
    public static double? FindNonFinite(PdfObject value) => value switch
    {
        PdfReal real when !double.IsFinite(real.Value) => real.Value,
        PdfArray array => array.Items.Select(FindNonFinite).FirstOrDefault(found => found is not null),
        PdfDictionary dictionary => dictionary.Entries.Select(entry => FindNonFinite(entry.Value)).FirstOrDefault(found => found is not null),
        _ => null,
    };

    /// <summary>The error for a non-finite number.</summary>
    /// <param name="value">The number.</param>
    /// <returns>The error.</returns>
    public static Error InvalidNumber(double value) =>
        new Error(PdfErrorCodes.Write.InvalidNumber, $"PDF numbers must be finite, got {value}.")
            .With(ErrorMetadataKeys.Value, value.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>Writes an object whose numbers are known to be finite.</summary>
    internal static string Write(PdfObject value) => value switch
    {
        PdfNull => "null",
        PdfBoolean boolean => boolean.Value ? "true" : "false",
        PdfInteger integer => integer.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        PdfReal real => InvariantNumber.Format(real.Value, RealDecimals),
        PdfName name => WriteName(name.Value),
        PdfString { Form: PdfStringForm.Hex } hex => WriteHexString(hex.Bytes),
        PdfString literal => WriteLiteralString(literal.Bytes),
        PdfArray array => "[" + string.Join(" ", array.Items.Select(Write)) + "]",
        PdfDictionary dictionary => WriteDictionary(dictionary),
        PdfReference reference => $"{reference.ObjectNumber} 0 R",
        _ => throw new InvalidOperationException($"Unknown PDF object type {value.GetType().Name}."),
    };

    private static string WriteDictionary(PdfDictionary dictionary) =>
        "<<" + string.Concat(dictionary.Entries.Select(entry => $" {WriteName(entry.Key.Value)} {Write(entry.Value)}")) + " >>";

    /// <summary>
    /// §7.3.5: a name is a slash followed by bytes (UTF-8 for non-ASCII text). A byte outside the printable
    /// range 0x21–0x7E, the number sign itself, and the delimiters <c>( ) &lt; &gt; [ ] { } / %</c> are written
    /// as <c>#</c> followed by two hexadecimal digits.
    /// </summary>
    private static string WriteName(string name)
    {
        var builder = new StringBuilder("/");
        foreach (var value in Encoding.UTF8.GetBytes(name))
        {
            var isRegular = value is >= 0x21 and <= 0x7E && !"#()<>[]{}/%".Contains((char)value);
            builder.Append(isRegular ? ((char)value).ToString() : $"#{value:X2}");
        }

        return builder.ToString();
    }

    /// <summary>
    /// §7.3.4.2: a literal string between parentheses. Backslash and both parentheses are escaped with a
    /// backslash (balanced parentheses need no escape, but escaping always is simpler and also valid); bytes
    /// outside printable ASCII are written as three-digit octal escapes <c>\ddd</c>.
    /// </summary>
    private static string WriteLiteralString(IEnumerable<byte> bytes)
    {
        var builder = new StringBuilder("(");
        foreach (var value in bytes)
        {
            builder.Append(value switch
            {
                (byte)'(' or (byte)')' or (byte)'\\' => "\\" + (char)value,
                >= 0x20 and <= 0x7E => ((char)value).ToString(),
                _ => "\\" + Convert.ToString(value, 8).PadLeft(3, '0'),
            });
        }

        return builder.Append(')').ToString();
    }

    /// <summary>§7.3.4.3: a hexadecimal string, two digits per byte between angle brackets.</summary>
    private static string WriteHexString(IEnumerable<byte> bytes) => "<" + Convert.ToHexString([.. bytes]) + ">";
}
