using System;
using System.Collections.Generic;
using Ave.Extensions.Documents.Core;

namespace Ave.Extensions.Documents.Html;

/// <summary>
/// The body of a <c>data:</c> URL (Fetch §4.5, "data: URL processor"): what follows the first comma, percent-decoded,
/// and forgiving-base64 decoded (Infra §4.6) when the type before the comma ends with <c>;base64</c>. A browser needs no
/// fetch for such a URL, so the front-end reads it itself. The fragment (from the first <c>#</c>) is not part of the
/// body: the processor reads the URL serialized without it (Fetch §4.5), so Chromium draws <c>data:image/png;base64,…#x</c>
/// as the image without <c>#x</c>. The URL is read <see cref="Urls.Cleaned"/>, without its tabs and newlines. The work is
/// linear in the URL's length.
/// </summary>
internal static class DataUrl
{
    /// <summary>Whether the URL has the scheme <c>data</c>, in any case, once it is <see cref="Urls.Cleaned"/> (so also
    /// with a tab or newline inside the scheme, as a browser reads it).</summary>
    public static bool Is(string url)
    {
        var text = Urls.Cleaned(url);
        return text.Length >= 5 && InfraAscii.EqualsIgnoreCase(text[..5], "data:");
    }

    /// <summary>The body's bytes; null when the URL is not a <c>data:</c> URL or its body does not decode.</summary>
    public static byte[]? Body(string url)
    {
        if (!Is(url))
        {
            return null;
        }

        var text = Urls.WithoutFragment(Urls.Cleaned(url));

        var comma = text.IndexOf(',', 5);
        if (comma < 0)
        {
            return null;
        }

        var type = text[5..comma].TrimEnd(' ', '\t', '\n', '\f', '\r');
        var body = PercentDecode(text.AsSpan(comma + 1));
        var semicolon = type.LastIndexOf(';');
        var base64 = semicolon >= 0 && InfraAscii.EqualsIgnoreCase(type[(semicolon + 1)..].Trim(' ', '\t', '\n', '\f', '\r'), "base64");
        return base64 ? ForgivingBase64(body) : body;
    }

    /// <summary>Percent-decoding of URL §1.3: %XX with two hex digits is that byte; every other character is its UTF-8.</summary>
    private static byte[] PercentDecode(ReadOnlySpan<char> text)
    {
        var bytes = new List<byte>(text.Length);
        Span<byte> encoded = stackalloc byte[4];
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '%' && i + 2 < text.Length && char.IsAsciiHexDigit(text[i + 1]) && char.IsAsciiHexDigit(text[i + 2]))
            {
                bytes.Add((byte)Convert.ToInt32(text.Slice(i + 1, 2).ToString(), 16));
                i += 2;
                continue;
            }

            var length = char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]) ? 2 : 1;
            var written = System.Text.Encoding.UTF8.GetBytes(text.Slice(i, length), encoded);
            for (var j = 0; j < written; j++)
            {
                bytes.Add(encoded[j]);
            }

            i += length - 1;
        }

        return [.. bytes];
    }

    /// <summary>Forgiving-base64 decode (Infra §4.6): ASCII white space removed; one or two '=' at the end when the length
    /// is a multiple of 4; then a length that leaves 1 over 4, or a byte outside the alphabet, is a failure (null).</summary>
    private static byte[]? ForgivingBase64(byte[] data)
    {
        var characters = new List<char>(data.Length);
        foreach (var value in data)
        {
            if (value is not ((byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\f' or (byte)'\r'))
            {
                characters.Add((char)value);
            }
        }

        if (characters.Count % 4 == 0)
        {
            for (var k = 0; k < 2 && characters.Count > 0 && characters[^1] == '='; k++)
            {
                characters.RemoveAt(characters.Count - 1);
            }
        }

        if (characters.Count % 4 == 1 || characters.Exists(c => !(char.IsAsciiLetterOrDigit(c) || c is '+' or '/')))
        {
            return null;
        }

        while (characters.Count % 4 != 0)
        {
            characters.Add('=');
        }

        var output = new byte[characters.Count / 4 * 3];
        return Convert.TryFromBase64Chars(characters.ToArray(), output, out var written) ? output[..written] : null;
    }
}
