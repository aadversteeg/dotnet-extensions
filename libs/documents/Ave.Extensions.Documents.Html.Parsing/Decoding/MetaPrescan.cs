using Ave.Extensions.Documents.Core;
using System;
using System.Collections.Generic;
using System.Text;

namespace Ave.Extensions.Documents.Html.Parsing.Decoding;

/// <summary>
/// "Prescan a byte stream to determine its encoding" (HTML Standard §13.2.3.2): looks for a <c>meta</c> element with a
/// <c>charset</c> attribute, or with <c>http-equiv="content-type"</c> and a <c>content</c> attribute that names a
/// charset, in the first 1024 bytes. The parser decodes only UTF-8 and UTF-16; it uses the result to refuse bytes that
/// say they are in another encoding, instead of guessing. Bytes past the end end the prescan without a result.
/// </summary>
internal static class MetaPrescan
{
    /// <summary>How many bytes are prescanned (§13.2.3.1 step 5: "the first 1024 bytes").</summary>
    public const int Length = 1024;

    /// <summary>What the prescan found.</summary>
    /// <param name="Label">The label as written in the meta element.</param>
    /// <param name="Encoding">The encoding the parser would use (<see cref="MetaEncodings.ForMeta"/>).</param>
    /// <param name="Offset">The byte offset of the meta element's <c>&lt;</c>.</param>
    public sealed record Found(string Label, string Encoding, int Offset);

    /// <summary>Runs the prescan.</summary>
    /// <param name="bytes">The bytes (without a byte order mark: with one, there is no prescan).</param>
    /// <returns>The meta element's encoding, or null.</returns>
    public static Found? Run(ReadOnlySpan<byte> bytes)
    {
        bytes = bytes[..Math.Min(bytes.Length, Length)];
        if (XmlDeclaration(bytes) is { } declared)
        {
            return declared;
        }

        var position = 0;
        while (position < bytes.Length)
        {
            var rest = bytes[position..];
            if (rest.StartsWith("<!--"u8))
            {
                // To the first > preceded by two hyphens, which may be those of "<!--" itself.
                var end = position + 2;
                while (end < bytes.Length && !(bytes[end] == '>' && bytes[end - 1] == '-' && bytes[end - 2] == '-'))
                {
                    end++;
                }

                position = end + 1;
                continue;
            }

            if (rest.Length >= 6 && rest[0] == '<' && IsWord(rest[1..5], "meta") && (InfraAscii.IsWhitespace(rest[5]) || rest[5] == '/'))
            {
                var start = position;
                position += 6;
                var found = Meta(bytes, ref position, start);
                if (found is not null)
                {
                    return found;
                }

                // "next byte": the position pointer was left on the byte after the attributes.
                position++;
                continue;
            }

            if (rest.Length >= 2 && rest[0] == '<' && (InfraAscii.IsAlpha(rest[1]) || (rest[1] == '/' && rest.Length >= 3 && InfraAscii.IsAlpha(rest[2]))))
            {
                // A tag: skip its name, then its attributes.
                while (position < bytes.Length && !InfraAscii.IsWhitespace(bytes[position]) && bytes[position] != '>')
                {
                    position++;
                }

                while (Attribute(bytes, ref position) is not null)
                {
                }

                position++;
                continue;
            }

            if (rest.StartsWith("<!"u8) || rest.StartsWith("</"u8) || rest.StartsWith("<?"u8))
            {
                var close = rest.IndexOf((byte)'>');
                if (close < 0)
                {
                    return null;
                }

                position += close + 1;
                continue;
            }

            position++;
        }

        return null;
    }

    /// <summary>An XML declaration that starts the bytes and names an encoding (<c>&lt;?xml version="1.0"
    /// encoding="iso-8859-2"?&gt;</c>): Chromium 159 decodes an HTML document by it (review 39 HD-13, measured), which
    /// the prescan of the standard does not; the parser refuses a non-UTF-8 one as it refuses a meta element.</summary>
    private static Found? XmlDeclaration(ReadOnlySpan<byte> bytes)
    {
        if (!bytes.StartsWith("<?xml"u8) || bytes.Length < 6 || !(InfraAscii.IsWhitespace(bytes[5])))
        {
            return null;
        }

        var end = bytes.IndexOf("?>"u8);
        var declaration = System.Text.Encoding.ASCII.GetString(end < 0 ? bytes : bytes[..end]);
        var at = declaration.IndexOf("encoding", StringComparison.Ordinal);
        if (at < 0)
        {
            return null;
        }

        var rest = declaration[(at + "encoding".Length)..].TrimStart(' ', '\t', '\n', '\r');
        if (!rest.StartsWith('=') || rest[1..].TrimStart(' ', '\t', '\n', '\r') is not { Length: > 0 } quoted || quoted[0] is not ('"' or '\''))
        {
            return null;
        }

        var close = quoted.IndexOf(quoted[0], 1);
        var label = close < 0 ? null : quoted[1..close];
        return label is not null && EncodingLabels.Resolve(label) is { } encoding ? new Found(label, MetaEncodings.ForMeta(encoding), 0) : null;
    }

    /// <summary>The attributes of one meta element, and what they say (the steps after "Let attribute list be an
    /// empty list of strings").</summary>
    private static Found? Meta(ReadOnlySpan<byte> bytes, ref int position, int start)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var gotPragma = false;
        bool? needPragma = null;
        string? charset = null;
        string? label = null;
        while (Attribute(bytes, ref position) is { } attribute)
        {
            // A repeated attribute is skipped (§13.2.3.2), except charset: Chromium 159 takes the last one (review 39
            // HD-14, measured).
            if (!names.Add(attribute.Name) && attribute.Name != "charset")
            {
                continue;
            }

            switch (attribute.Name)
            {
                case "http-equiv":
                    gotPragma |= attribute.Value == "content-type";
                    break;
                case "content":
                    var fromContent = ContentCharset(attribute.Value);
                    if (fromContent is not null && charset is null && EncodingLabels.Resolve(fromContent) is { } contentEncoding)
                    {
                        (charset, label, needPragma) = (contentEncoding, fromContent, true);
                    }

                    break;
                case "charset":
                    (charset, label, needPragma) = (EncodingLabels.Resolve(attribute.Value), attribute.Value, false);
                    break;
            }
        }

        if (needPragma is null || (needPragma == true && !gotPragma) || charset is null)
        {
            return null;
        }

        return new Found(label!, MetaEncodings.ForMeta(charset), start);
    }

    /// <summary>"Get an attribute" (§13.2.3.2): one name and value, lowercased; null when there is none or the bytes
    /// end inside it.</summary>
    private static (string Name, string Value)? Attribute(ReadOnlySpan<byte> bytes, ref int position)
    {
        while (position < bytes.Length && (InfraAscii.IsWhitespace(bytes[position]) || bytes[position] == '/'))
        {
            position++;
        }

        if (position >= bytes.Length || bytes[position] == '>')
        {
            return null;
        }

        var name = new StringBuilder();
        while (true)
        {
            if (position >= bytes.Length)
            {
                return null;
            }

            var b = bytes[position];
            if (b == '=' && name.Length > 0)
            {
                position++;
                return AttributeValue(bytes, ref position) is { } value ? (name.ToString(), value) : null;
            }

            if (InfraAscii.IsWhitespace(b))
            {
                break;
            }

            if (b is (byte)'/' or (byte)'>')
            {
                return (name.ToString(), string.Empty);
            }

            name.Append((char)InfraAscii.ToLower(b));
            position++;
        }

        // Spaces after the name; anything but "=" ends the attribute without a value.
        while (position < bytes.Length && InfraAscii.IsWhitespace(bytes[position]))
        {
            position++;
        }

        if (position >= bytes.Length || bytes[position] != '=')
        {
            return (name.ToString(), string.Empty);
        }

        position++;
        return AttributeValue(bytes, ref position) is { } afterSpaces ? (name.ToString(), afterSpaces) : null;
    }

    /// <summary>The value part of "get an attribute": after the "=", quoted or not; null when the bytes end in it.</summary>
    private static string? AttributeValue(ReadOnlySpan<byte> bytes, ref int position)
    {
        while (position < bytes.Length && InfraAscii.IsWhitespace(bytes[position]))
        {
            position++;
        }

        if (position >= bytes.Length)
        {
            return null;
        }

        var value = new StringBuilder();
        var first = bytes[position];
        if (first is (byte)'"' or (byte)'\'')
        {
            position++;
            while (true)
            {
                if (position >= bytes.Length)
                {
                    return null;
                }

                if (bytes[position] == first)
                {
                    position++;
                    return value.ToString();
                }

                value.Append((char)InfraAscii.ToLower(bytes[position]));
                position++;
            }
        }

        if (first == '>')
        {
            return string.Empty;
        }

        while (true)
        {
            if (position >= bytes.Length)
            {
                return null;
            }

            var b = bytes[position];
            if (InfraAscii.IsWhitespace(b) || b == '>')
            {
                return value.ToString();
            }

            value.Append((char)InfraAscii.ToLower(b));
            position++;
        }
    }

    /// <summary>"Extracting character encodings from meta elements" (HTML Standard §2.5.5 / §4.2.5.5 content
    /// attribute): the label after <c>charset=</c> in a <c>content</c> value, or null.</summary>
    /// <param name="content">The attribute's value.</param>
    /// <returns>The label, or null.</returns>
    internal static string? ContentCharset(string content)
    {
        var position = 0;
        while (true)
        {
            var found = content.IndexOf("charset", position, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
            {
                return null;
            }

            position = found + "charset".Length;
            while (position < content.Length && InfraAscii.IsWhitespace(content[position]))
            {
                position++;
            }

            if (position >= content.Length || content[position] != '=')
            {
                continue;
            }

            position++;
            while (position < content.Length && InfraAscii.IsWhitespace(content[position]))
            {
                position++;
            }

            if (position >= content.Length)
            {
                return null;
            }

            var quote = content[position];
            if (quote is '"' or '\'')
            {
                var close = content.IndexOf(quote, position + 1);
                return close < 0 ? null : content[(position + 1)..close];
            }

            var end = position;
            while (end < content.Length && !InfraAscii.IsWhitespace(content[end]) && content[end] != ';')
            {
                end++;
            }

            return content[position..end];
        }
    }

    private static bool IsWord(ReadOnlySpan<byte> bytes, string lower)
    {
        for (var i = 0; i < lower.Length; i++)
        {
            if (InfraAscii.ToLower(bytes[i]) != lower[i])
            {
                return false;
            }
        }

        return true;
    }
}
