using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Extensions.Documents.Pdf;

/// <summary>
/// Writes direct objects in PDF syntax (ISO 32000-1 §7.3). Pure: same object, same bytes. Every output byte is
/// ASCII, because names and literal strings are escaped and binary strings are written in hexadecimal.
/// </summary>
public static class PdfSerializer
{
    /// <summary>Decimals written for real numbers. 0.0001 pt is far below what any device can show.</summary>
    public const int RealDecimals = 4;

    /// <summary>The largest magnitude of a real number: veraPDF rule 6.1.13-2 refuses a real "outside the range of
    /// +/-3.403 x 10^38" in PDF/A, the range of a 32-bit float. Applied to every file, since a reader is free to read
    /// reals as such floats.</summary>
    public const double MaxReal = 3.403e38;

    /// <summary>
    /// The deepest an object may nest arrays and dictionaries, the object itself being level 1: a limit of the writer's
    /// own. Checking and writing an object recurse once per level, so the limit keeps both far from
    /// the stack limit whatever object a caller builds. The engine's own objects nest a few levels (a page's resources:
    /// page, /Resources, /Font, …), so 64 never refuses one of them.
    /// </summary>
    public const int MaxNestingDepth = 64;

    /// <summary>Writes an object, after checking it (<see cref="Problem"/>).</summary>
    /// <param name="value">The object.</param>
    /// <returns>Its PDF syntax, or a <see cref="PdfErrorCodes.Write.InvalidNumber"/> or
    /// <see cref="PdfErrorCodes.Write.InvalidObject"/> error.</returns>
    public static Result<string, Error> Serialize(PdfObject? value) =>
        Problem([value]) is { } problem
            ? Result<string, Error>.Failure(problem)
            : Result<string, Error>.Success(Write(value!));

    /// <summary>
    /// The one check every writer of PDF syntax makes before it writes: the first problem in some object trees, or
    /// null. A number must be finite (§7.3.3: PDF numbers are finite values), and a dictionary's keys unique (§7.3.7).
    /// Nothing may be missing: an object, a name's text, a dictionary key; a string has a form the serializer knows, and a
    /// reference an object number of 1 or more (the writer numbers objects from 1). Arrays and dictionaries nest at most
    /// <see cref="MaxNestingDepth"/> levels in each tree; the check stops at the first level past it, so it never
    /// recurses deeper than that itself.
    /// </summary>
    /// <param name="values">The object trees.</param>
    /// <returns>An <see cref="PdfErrorCodes.Write.InvalidNumber"/>, <see cref="PdfErrorCodes.Write.InvalidObject"/> or
    /// <see cref="PdfErrorCodes.Write.NestedTooDeep"/> error, or null.</returns>
    public static Error? Problem(IEnumerable<PdfObject?>? values) =>
        values is null ? Invalid("No objects were given.", "null") : FirstProblem(values.OrEmpty(), level: 1);

    private static Error? FirstProblem(IEnumerable<PdfObject?> values, int level) =>
        values.Select(value => ProblemIn(value, level)).FirstOrDefault(problem => problem is not null);

    /// <summary>The first problem in one object at <paramref name="level"/> (the top of a tree is level 1).</summary>
    private static Error? ProblemIn(PdfObject? value, int level) => value switch
    {
        null => Invalid("An object is missing.", "null"),
        PdfArray or PdfDictionary when level > MaxNestingDepth => new Error(
                PdfErrorCodes.Write.NestedTooDeep, $"Arrays and dictionaries may nest at most {MaxNestingDepth} levels deep.")
            .With(ErrorMetadataKeys.Value, MaxNestingDepth.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        PdfReal real when !double.IsFinite(real.Value) || Math.Abs(real.Value) > MaxReal => InvalidNumber(real.Value),
        PdfName { Value: null } => Invalid("A name has no text.", "null"),
        PdfString { Form: var form } when !Enum.IsDefined(form) => Invalid($"A string has the form {(int)form}, which is not a form.", ((int)form).ToString(System.Globalization.CultureInfo.InvariantCulture)),
        PdfReference { ObjectNumber: < 1 } reference => Invalid($"A reference names object {reference.ObjectNumber}; objects are numbered from 1.", reference.ObjectNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        PdfArray array => FirstProblem(array.Items, level + 1),
        PdfDictionary dictionary when dictionary.Entries.Any(entry => entry.Key?.Value is null) => Invalid("A dictionary has an entry without a key.", "null"),
        PdfDictionary { DuplicateKey: { } key } => new Error(PdfErrorCodes.Write.InvalidObject, $"A dictionary has two entries with the key /{key}.")
            .With(ErrorMetadataKeys.Value, key),
        PdfDictionary dictionary => FirstProblem(dictionary.Entries.Select(entry => entry.Value), level + 1),
        PdfNull or PdfBoolean or PdfInteger or PdfReal or PdfName or PdfString or PdfReference => null,

        // A record cannot hide its copy constructor, so another assembly can derive a case of its own from PdfObject;
        // the serializer writes only the cases of §7.3 and answers any other with an error value.
        _ => Invalid($"An object of type {value.GetType().Name} is not one of the PDF objects of ISO 32000-1 §7.3.", value.GetType().Name),
    };

    private static Error Invalid(string message, string value) =>
        new Error(PdfErrorCodes.Write.InvalidObject, message).With(ErrorMetadataKeys.Value, value);

    private static Error InvalidNumber(double value) =>
        new Error(PdfErrorCodes.Write.InvalidNumber, $"PDF numbers must be finite and within ±{MaxReal}, got {value}.")
            .With(ErrorMetadataKeys.Value, value.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>Writes an object that passed <see cref="Problem"/>: numbers finite, nesting within the limit.</summary>
    internal static string Write(PdfObject value) => value switch
    {
        PdfNull => "null",
        PdfBoolean boolean => boolean.Value ? "true" : "false",
        PdfInteger integer => integer.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        PdfReal real => WriteReal(real.Value),
        PdfName name => WriteName(name.Value),
        PdfString { Form: PdfStringForm.Hex } hex => WriteHexString(hex.Bytes),
        PdfString literal => WriteLiteralString(literal.Bytes),
        PdfArray array => "[" + string.Join(" ", array.Items.Select(Write)) + "]",
        PdfDictionary dictionary => WriteDictionary(dictionary),
        PdfReference reference => $"{reference.ObjectNumber} 0 R",
        _ => throw new InvalidOperationException($"Unknown PDF object type {value.GetType().Name}."),
    };

    /// <summary>
    /// §7.3.3: "A real value shall be written as one or more decimal digits with an optional sign and a leading,
    /// trailing, or embedded PERIOD (2Eh) (decimal point)." A real whose digits are whole at four decimals (5, 1e20) gets
    /// ".0", so that it stays a real for every reader: a reader that took 3000000000 for an integer would find it
    /// outside the integer range of Annex C.
    /// </summary>
    private static string WriteReal(double value)
    {
        var digits = InvariantNumber.Format(value, RealDecimals);
        return digits.Contains('.') ? digits : digits + ".0";
    }

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
