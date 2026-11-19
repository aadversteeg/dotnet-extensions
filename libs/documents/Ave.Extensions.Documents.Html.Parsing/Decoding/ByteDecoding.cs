using System;
using System.Collections.Generic;
using System.Text;

namespace Ave.Extensions.Documents.Html.Parsing.Decoding;

/// <summary>
/// Bytes to text, for the parser's byte input: the byte order mark decides between UTF-8, UTF-16LE and UTF-16BE
/// (Encoding Standard §6, "decode", BOM sniffing; HTML Standard §13.2.3.1 "encoding sniffing algorithm" step 1); without
/// one the bytes are UTF-8. Bytes that do not form a character are replaced by U+FFFD, one per maximal subpart (Encoding
/// Standard §8.1.1 "UTF-8 decoder", §14.2 "shared UTF-16 decoder"), as a browser decodes them; each replacement is
/// reported by the index of its U+FFFD in the text.
/// </summary>
internal static class ByteDecoding
{
    /// <summary>The text decoded from bytes.</summary>
    /// <param name="Text">The characters, without the byte order mark.</param>
    /// <param name="HasByteOrderMark">Whether a byte order mark chose the encoding; the encoding is then certain and
    /// no meta element is looked at (§13.2.3.1).</param>
    /// <param name="Replacements">The index in <paramref name="Text"/> of every U+FFFD that replaced bytes.</param>
    public sealed record Decoded(string Text, bool HasByteOrderMark, IReadOnlyList<int> Replacements);

    /// <summary>Decodes bytes as the byte order mark says, else as UTF-8.</summary>
    /// <param name="bytes">The bytes.</param>
    /// <returns>The text.</returns>
    public static Decoded Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            return Utf8(bytes[3..], hasByteOrderMark: true);
        }

        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]))
        {
            return Utf16(bytes[2..], bigEndian: true);
        }

        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]))
        {
            return Utf16(bytes[2..], bigEndian: false);
        }

        return Utf8(bytes, hasByteOrderMark: false);
    }

    /// <summary>The number of characters the first <paramref name="count"/> bytes decode to (without a byte order
    /// mark): maps a byte offset the prescan found to an index in the text.</summary>
    /// <param name="bytes">The bytes (no byte order mark).</param>
    /// <param name="count">How many bytes.</param>
    /// <returns>The number of UTF-16 code units.</returns>
    public static int Utf8Length(ReadOnlySpan<byte> bytes, int count) => Utf8(bytes[..count], hasByteOrderMark: false).Text.Length;

    private static Decoded Utf8(ReadOnlySpan<byte> bytes, bool hasByteOrderMark)
    {
        if (System.Text.Unicode.Utf8.IsValid(bytes))
        {
            return new Decoded(Encoding.UTF8.GetString(bytes), hasByteOrderMark, []);
        }

        // Rune.DecodeFromUtf8 reports an invalid sequence with the length of its maximal subpart (Unicode §3.9,
        // "U+FFFD substitution of maximal subparts"), which is the Encoding Standard's UTF-8 decoder.
        var text = new StringBuilder(bytes.Length);
        var replacements = new List<int>();
        Span<char> units = stackalloc char[2];
        while (!bytes.IsEmpty)
        {
            var status = Rune.DecodeFromUtf8(bytes, out var rune, out var consumed);
            if (status != System.Buffers.OperationStatus.Done)
            {
                replacements.Add(text.Length);
            }

            text.Append(units[..rune.EncodeToUtf16(units)]);
            bytes = bytes[consumed..];
        }

        return new Decoded(text.ToString(), hasByteOrderMark, replacements);
    }

    private static Decoded Utf16(ReadOnlySpan<byte> bytes, bool bigEndian)
    {
        // The shared UTF-16 decoder (§14.2): code units in pairs of bytes, a lone surrogate is U+FFFD. At the end of the
        // bytes Chromium 159 drops what does not complete a character, a lone last byte or a high surrogate without its
        // pair, where the decoder of the standard writes U+FFFD (measured).
        var text = new StringBuilder(bytes.Length / 2);
        var replacements = new List<int>();
        var index = 0;
        while (index + 1 < bytes.Length)
        {
            var unit = Unit(bytes, index, bigEndian);
            index += 2;
            if (char.IsHighSurrogate(unit) && index + 1 >= bytes.Length)
            {
                break;
            }

            if (char.IsHighSurrogate(unit) && index + 1 < bytes.Length && char.IsLowSurrogate(Unit(bytes, index, bigEndian)))
            {
                text.Append(unit).Append(Unit(bytes, index, bigEndian));
                index += 2;
            }
            else if (char.IsSurrogate(unit))
            {
                replacements.Add(text.Length);
                text.Append('�');
            }
            else
            {
                text.Append(unit);
            }
        }

        return new Decoded(text.ToString(), HasByteOrderMark: true, replacements);
    }

    private static char Unit(ReadOnlySpan<byte> bytes, int index, bool bigEndian) =>
        bigEndian ? (char)((bytes[index] << 8) | bytes[index + 1]) : (char)(bytes[index] | (bytes[index + 1] << 8));
}
