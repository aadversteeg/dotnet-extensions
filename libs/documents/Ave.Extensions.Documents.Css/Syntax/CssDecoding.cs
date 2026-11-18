using System;
using System.Text;

namespace Ave.Extensions.Documents.Css.Syntax;

/// <summary>
/// Turns a style sheet's bytes into text (CSS Syntax 3 §3.2, "decode bytes"; Encoding Standard §6 "decode"). The
/// encoding is chosen as the standard says: a byte order mark; else the protocol's label, an <c>@charset</c> rule of
/// the exact form <c>@charset "…";</c> at the start, the referring document's encoding, UTF-8. Of the encodings, UTF-8
/// and UTF-16 are decoded; the replacement encoding gives one U+FFFD, as the standard says; any other encoding decodes
/// bytes that are all ASCII to the same text as ASCII does (ISO-2022-JP only without the escape byte), and anything
/// else is not decoded.
/// </summary>
internal static class CssDecoding
{
    /// <summary>The encoding the bytes are in, by name of the Encoding Standard.</summary>
    public static string EncodingOf(ReadOnlySpan<byte> bytes, string? protocolEncoding, string? environmentEncoding)
    {
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            return "UTF-8";
        }

        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]))
        {
            return "UTF-16BE";
        }

        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]))
        {
            return "UTF-16LE";
        }

        if (EncodingLabels.Resolve(protocolEncoding) is { } protocol)
        {
            return protocol;
        }

        if (CharsetRule(bytes) is { } charset && EncodingLabels.Resolve(charset) is { } named)
        {
            // §3.2 step 3: a UTF-16 label in @charset means UTF-8 (the text was readable as ASCII, so it is not UTF-16).
            return named is "UTF-16BE" or "UTF-16LE" ? "UTF-8" : named;
        }

        return EncodingLabels.Resolve(environmentEncoding) ?? "UTF-8";
    }

    /// <summary>The text of the bytes in an encoding, or null when the engine cannot decode them.</summary>
    public static string? Decode(ReadOnlySpan<byte> bytes, string encoding)
    {
        switch (encoding)
        {
            case "UTF-8":
                return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false)
                    .GetString(bytes.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]) ? bytes[3..] : bytes);
            case "UTF-16LE":
                return new UnicodeEncoding(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: false)
                    .GetString(bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]) ? bytes[2..] : bytes);
            case "UTF-16BE":
                return new UnicodeEncoding(bigEndian: true, byteOrderMark: false, throwOnInvalidBytes: false)
                    .GetString(bytes.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]) ? bytes[2..] : bytes);
            case "replacement":
                // Encoding Standard §14.1: the replacement decoder gives one U+FFFD for any input that is not empty.
                return bytes.IsEmpty ? string.Empty : "�";
            case "ISO-2022-JP" when bytes.Contains((byte)0x1B):
                return null;
            default:
                return bytes.ContainsAnyExceptInRange((byte)0x00, (byte)0x7F) ? null : Encoding.ASCII.GetString(bytes);
        }
    }

    /// <summary>The label of an <c>@charset "…";</c> rule at the very start of the bytes, in exactly that form (§3.2
    /// step 3), or null.</summary>
    private static string? CharsetRule(ReadOnlySpan<byte> bytes)
    {
        ReadOnlySpan<byte> start = "@charset \""u8;
        if (!bytes.StartsWith(start))
        {
            return null;
        }

        var rest = bytes[start.Length..];
        var end = rest.IndexOf("\";"u8);
        return end < 0 || rest[..end].ContainsAnyExceptInRange((byte)0x00, (byte)0x7F) ? null : Encoding.ASCII.GetString(rest[..end]);
    }
}
