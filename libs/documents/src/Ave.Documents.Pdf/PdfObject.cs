using System;
using System.Collections.Generic;
using Ave.Documents.Core;
using System.Linq;
using System.Text;

namespace Ave.Documents.Pdf;

/// <summary>
/// A direct PDF object (ISO 32000-1 §7.3): boolean, number, string, name, array, dictionary, null, or a
/// reference to an indirect object. Streams are not direct objects (§7.3.8.1: "All streams shall be indirect
/// objects"), so they are a separate type, <see cref="PdfStream"/>, that only an <see cref="IndirectObject"/>
/// can hold. Immutable. Turned into bytes by <see cref="PdfSerializer"/>.
/// </summary>
public abstract record PdfObject
{
    // Only the cases in this assembly exist, so a switch over them is complete.
    private protected PdfObject()
    {
    }
}

/// <summary>The null object (§7.3.9).</summary>
public sealed record PdfNull : PdfObject
{
    /// <summary>The one null value.</summary>
    public static PdfNull Instance { get; } = new();
}

/// <summary>A boolean (§7.3.2).</summary>
/// <param name="Value">The value.</param>
public sealed record PdfBoolean(bool Value) : PdfObject;

/// <summary>An integer (§7.3.3).</summary>
/// <param name="Value">The value.</param>
public sealed record PdfInteger(long Value) : PdfObject;

/// <summary>A real number (§7.3.3). Must be finite; the writer refuses NaN and infinity.</summary>
/// <param name="Value">The value.</param>
public sealed record PdfReal(double Value) : PdfObject;

/// <summary>
/// A name (§7.3.5), written with a leading slash, for example <c>/Type</c>. The value is stored without the
/// slash; characters outside the regular printable range are escaped by the serializer.
/// </summary>
/// <param name="Value">The name, without the leading slash.</param>
public sealed record PdfName(string Value) : PdfObject
{
    /// <summary>Converts text to a name, so dictionary keys can be written as plain strings.</summary>
    /// <param name="value">The name without the slash.</param>
    public static implicit operator PdfName(string value) => new(value);
}

/// <summary>How the serializer writes a string's bytes.</summary>
public enum PdfStringForm
{
    /// <summary>A literal string in parentheses, <c>(Hello)</c> (§7.3.4.2): for readable text.</summary>
    Literal,

    /// <summary>A hexadecimal string, <c>&lt;0024&gt;</c> (§7.3.4.3): for binary data such as glyph codes.</summary>
    Hex,
}

/// <summary>A string (§7.3.4): a sequence of bytes.</summary>
/// <param name="Bytes">The bytes.</param>
/// <param name="Form">How to write them.</param>
public sealed record PdfString(ValueList<byte> Bytes, PdfStringForm Form) : PdfObject
{
    /// <summary>
    /// A text string for human-readable fields such as the document title (§7.9.2.2). Text made only of
    /// printable ASCII is written as those bytes (PDFDocEncoding equals ASCII in that range); anything else is
    /// written as UTF-16BE with the byte order mark FE FF, as the section prescribes.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The string.</returns>
    public static PdfString FromText(string text)
    {
        var isPrintableAscii = text.All(character => character is >= ' ' and <= '~');
        var bytes = isPrintableAscii
            ? Encoding.ASCII.GetBytes(text)
            : [0xFE, 0xFF, .. Encoding.BigEndianUnicode.GetBytes(text)];
        return new PdfString([.. bytes], PdfStringForm.Literal);
    }

    /// <summary>Binary data, written as a hexadecimal string.</summary>
    /// <param name="bytes">The bytes.</param>
    /// <returns>The string.</returns>
    public static PdfString FromBytes(IEnumerable<byte> bytes) => new([.. bytes], PdfStringForm.Hex);
}

/// <summary>An array (§7.3.6).</summary>
/// <param name="Items">The elements, in order.</param>
public sealed record PdfArray(ValueList<PdfObject> Items) : PdfObject
{
    /// <summary>An array of numbers; whole numbers become integers, others reals.</summary>
    /// <param name="numbers">The numbers.</param>
    /// <returns>The array.</returns>
    public static PdfArray OfNumbers(params IEnumerable<double> numbers) => new([.. numbers.Select(PdfNumber.From)]);
}

/// <summary>
/// A dictionary (§7.3.7). Entries keep the order they were given in, so output is the same on every run. Keys must be
/// unique ("Multiple entries in the same dictionary shall not have the same key"); the writer checks that, so a
/// dictionary with a duplicate key is an error value when it is written (<see cref="PdfWriter"/>), not an exception
/// when it is made.
/// </summary>
/// <param name="Entries">The entries, in output order.</param>
public sealed record PdfDictionary(ValueList<KeyValuePair<PdfName, PdfObject>> Entries) : PdfObject
{
    /// <summary>The first key that appears twice, or null.</summary>
    public string? DuplicateKey => Entries.GroupBy(entry => entry.Key.Value).FirstOrDefault(group => group.Count() > 1)?.Key;

    /// <summary>Creates a dictionary from (key, value) pairs, leaving out pairs whose value is null.</summary>
    /// <param name="entries">The entries; a null value means "this optional entry is absent".</param>
    /// <returns>The dictionary.</returns>
    public static PdfDictionary Of(params IEnumerable<(PdfName Key, PdfObject? Value)> entries) =>
        new([.. entries
            .Where(entry => entry.Value is not null)
            .Select(entry => KeyValuePair.Create(entry.Key, entry.Value!))]);

    /// <summary>True when the dictionary has an entry with this key.</summary>
    /// <param name="key">The key.</param>
    /// <returns>Whether the key is present.</returns>
    public bool ContainsKey(PdfName key) => Entries.Any(entry => entry.Key == key);
}

/// <summary>A reference to an indirect object, <c>12 0 R</c> (§7.3.10). Generation is always 0: the writer
/// only creates new files.</summary>
/// <param name="ObjectNumber">The object number, 1 or more.</param>
public sealed record PdfReference(int ObjectNumber) : PdfObject;
