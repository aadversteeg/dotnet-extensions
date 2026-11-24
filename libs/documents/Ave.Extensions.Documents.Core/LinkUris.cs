using System;
using System.Collections.Frozen;
using System.Globalization;

namespace Ave.Extensions.Documents.Core;

/// <summary>
/// THE RULE FOR A LINK'S URI, in one place for every layer: which URIs a link may lead to. A link's target comes from
/// untrusted input, and a reader of the PDF opens it when the link is clicked, so only schemes that open a web page, a
/// mail program or a phone call are accepted: <c>http</c>, <c>https</c>, <c>mailto</c> and <c>tel</c>. Every other scheme
/// is refused, among them <c>javascript</c> and <c>vbscript</c> (script), <c>file</c> (a local path on the machine that
/// made the PDF, and a way to open local files), <c>data</c> (a document of its own inside the link); and so is a relative
/// URI, which has no scheme. The engine never fetches, resolves or opens a URI.
/// </summary>
/// <remarks>
/// The check is on the text that is written into the file: the PDF back-end writes the URI as it is given, without any
/// normalising, so the rule sees exactly what a reader will see. The URI must be printable ASCII (U+0021 to U+007E: no
/// space, no control character, nothing beyond ASCII), as a URI action's URI is "encoded in 7-bit ASCII" (ISO 32000-1
/// §12.6.4.7, Table 206); a front-end percent-encodes other characters first. Its scheme is the text before the first
/// colon, of the form RFC 3986 §3.1 gives (a letter, then letters, digits, <c>+</c>, <c>-</c> and <c>.</c>), compared
/// without regard to case ("schemes are case-insensitive", §3.1). An <c>http</c> or <c>https</c> URI must also name a
/// host (RFC 3986 §3.2.2). It is at most <see cref="MaxLength"/> characters long.
/// </remarks>
public static class LinkUris
{
    /// <summary>
    /// The longest URI a link may have: 32 767 characters, which as printable ASCII are as many bytes: the longest string
    /// a PDF/A file may contain (veraPDF rule 6.1.13-3, "A conforming file shall not contain any string longer than 32767
    /// bytes"), so a URI the rule accepts can be written under every profile. Longer is <c>Documents.Input.Invalid.Link</c>.
    /// </summary>
    public const int MaxLength = 32_767;

    /// <summary>The accepted schemes, in lower case.</summary>
    public static FrozenSet<string> Schemes { get; } = FrozenSet.Create(StringComparer.Ordinal, "http", "https", "mailto", "tel");

    /// <summary>The accepted schemes in words, for messages.</summary>
    public const string SchemesText = "http, https, mailto and tel";

    /// <summary>Why a URI is not accepted as a link's target, or null when it is. One pass over its characters.</summary>
    /// <param name="uri">The URI.</param>
    /// <returns>The reason, in words that name the scheme where there is one and always the accepted schemes, or null.</returns>
    public static string? Problem(string? uri)
    {
        if (string.IsNullOrEmpty(uri))
        {
            return $"a link needs a URI, an absolute URI of the schemes {SchemesText}";
        }

        if (uri.Length > MaxLength)
        {
            return $"the URI is {uri.Length.ToString(CultureInfo.InvariantCulture)} characters long; a link's URI, of the schemes {SchemesText}, is at most {MaxLength.ToString(CultureInfo.InvariantCulture)}";
        }

        for (var index = 0; index < uri.Length; index++)
        {
            if (uri[index] is < '!' or > '~')
            {
                return $"the URI holds U+{(int)uri[index]:X4} at position {index.ToString(CultureInfo.InvariantCulture)}; a link's URI is printable ASCII (no space, no control character), with other characters percent-encoded, of the schemes {SchemesText}";
            }
        }

        var colon = uri.IndexOf(':', StringComparison.Ordinal);
        if (colon <= 0 || !IsScheme(uri.AsSpan(0, colon)))
        {
            return $"the URI has no scheme (a relative URI); a link leads to an absolute URI of the schemes {SchemesText}";
        }

        var scheme = uri[..colon].ToLowerInvariant();
        if (!Schemes.Contains(scheme))
        {
            return $"the scheme '{scheme}' is not accepted; a link leads to a URI of the schemes {SchemesText}";
        }

        return scheme is "http" or "https" && !(System.Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.Host.Length > 0)
            ? $"the {scheme} URI names no host or does not parse; it needs the form {scheme}://host/path (the schemes accepted are {SchemesText})"
            : null;
    }

    /// <summary>RFC 3986 §3.1: <c>scheme = ALPHA *( ALPHA / DIGIT / "+" / "-" / "." )</c>.</summary>
    private static bool IsScheme(ReadOnlySpan<char> text)
    {
        if (!char.IsAsciiLetter(text[0]))
        {
            return false;
        }

        foreach (var character in text[1..])
        {
            if (!(char.IsAsciiLetterOrDigit(character) || character is '+' or '-' or '.'))
            {
                return false;
            }
        }

        return true;
    }
}
