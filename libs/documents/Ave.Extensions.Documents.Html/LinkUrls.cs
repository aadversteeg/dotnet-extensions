using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Ave.Extensions.Documents.Core;

namespace Ave.Extensions.Documents.Html;

/// <summary>
/// THE RULE FOR A LINK'S URL IN HTML: how the <c>href</c> of an <c>a</c> element becomes the URI its link leads to. The
/// HTML Standard resolves the attribute against the document base URL (§2.4.1, §2.4.2 "parse a URL"); this rule follows
/// the steps of the URL Standard that change where a link leads, in this order, and decides nothing about which URIs are
/// accepted: the caller applies the one rule of the model to the result (<see cref="LinkUris"/>), so the front-end adds no
/// second rule beside it.
/// <list type="number">
/// <item>White space: every leading and trailing C0 control and space is removed, and every tab, line feed and carriage
/// return inside (URL Standard §4.4, the basic URL parser's first steps). So <c>"  java\tscript:x"</c> is
/// <c>javascript:x</c>, which the model's rule refuses.</item>
/// <item>A URL that starts with <c>#</c> leads to a place inside the document, also under a <c>base</c> element, as Chromium 159
/// makes it (measured: a <c>/Dest</c>, under a <c>base</c> too): <see cref="Fragment"/> gives the part after the <c>#</c>, and
/// the front-end finds its element (<c>HtmlBoxGenerator</c>, THE RULE FOR A LINK INSIDE AN HTML DOCUMENT).</item>
/// <item>A URL with a scheme (URL Standard §4.4, scheme state: an ASCII letter, then letters, digits, <c>+</c>, <c>-</c> and
/// <c>.</c>, then a colon) is absolute: its scheme is written in lower case, as the URL Standard serializes it, and the rest
/// as written.</item>
/// <item>Any other URL is relative. It is resolved only against a base URL of the scheme <c>http</c> or <c>https</c>, by
/// RFC 3986 §5.2 (§5.2.2 transform references, §5.2.3 merge paths, §5.2.4 remove dot segments), with a backslash before
/// the query read as a slash, as the URL Standard reads it in an <c>http</c> or <c>https</c> URL. Without such a base it is
/// refused: a document read from a file has a <c>file:</c> base, and Chromium 159 writes the path of the machine that
/// printed it into the link (measured, case <c>lk-relative</c>: <c>file:///…/cases/other.html</c>).</item>
/// <item>Percent-encoding: every code point that the URL Standard percent-encodes in the query, the fragment, a path and
/// the user information alike (the intersection of those four percent-encode sets, URL Standard §1.3: the C0 controls,
/// space, <c>"</c>, <c>&lt;</c>, <c>&gt;</c>, U+007F and every code point above U+007E) is written as the percent-encoded
/// bytes of its UTF-8 form (§1.3, "UTF-8 percent-encode"; RFC 3987 §3.1 maps an IRI to a URI the same way). Every other
/// character stays as written, <c>%</c> too. So the URI is printable ASCII, as a PDF URI action needs (ISO 32000-1
/// §12.6.4.7). In an opaque path (<c>mailto:</c>, <c>tel:</c>) the URL Standard leaves a space inside as it is, which a URI
/// cannot hold (RFC 3986 §2): it is encoded too. A character beyond ASCII in the host of an <c>http</c> or <c>https</c> URL is
/// refused instead: the URL Standard writes such a host in its ASCII form (IDNA, UTS #46), which this version does not do.</item>
/// </list>
/// The engine never fetches, resolves or opens the URL. The work is one pass per step over a text of at most
/// <see cref="LinkUris.MaxLength"/> characters; a longer <c>href</c> is refused before any step.
/// </summary>
internal static partial class LinkUrls
{
    /// <summary>The URI a link's <c>href</c> leads to, or why it has none.</summary>
    /// <param name="href">The attribute's value.</param>
    /// <param name="baseUrl">The document base URL (<see cref="Urls.DocumentBase"/>), or null.</param>
    /// <returns>The URI, or the reason it is refused (to be checked by <see cref="LinkUris.Problem"/> when a URI).</returns>
    internal static (string? Uri, string? Problem) Resolve(string href, string? baseUrl)
    {
        if (href.Length > LinkUris.MaxLength)
        {
            return (null, $"the href is {href.Length.ToString(CultureInfo.InvariantCulture)} characters long; a link's URI is at most {LinkUris.MaxLength.ToString(CultureInfo.InvariantCulture)}");
        }

        var url = WithoutWhiteSpace(href);
        if (url.StartsWith('#'))
        {
            return (null, "it leads to a place inside the document (#…), which is not a URI");
        }

        if (Urls.HasScheme(url))
        {
            var colon = url.IndexOf(':', StringComparison.Ordinal);
            var absolute = url[..colon].ToLowerInvariant() + url[colon..];
            return HostProblem(absolute) is { } problem ? (null, problem) : (Encoded(absolute), null);
        }

        if (baseUrl is null || !(baseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
        {
            return (null, $"the URL '{url}' is relative, and the document has no base URL of the scheme http or https to resolve it against: add a base element with an http or https URL, or write the URL absolute");
        }

        var resolved = Resolved(Parts(baseUrl), Parts(Slashed(url)));
        return HostProblem(resolved) is { } hostProblem ? (null, hostProblem) : (Encoded(resolved), null);
    }

    /// <summary>The fragment of an <c>href</c> that leads to a place inside the document: the text after its <c>#</c>, once
    /// the white space is removed as for any URL (step 1); null for any other <c>href</c> and for one longer than
    /// <see cref="LinkUris.MaxLength"/> characters (which <see cref="Resolve"/> refuses).</summary>
    /// <param name="href">The attribute's value.</param>
    /// <returns>The fragment, possibly empty, or null.</returns>
    internal static string? Fragment(string href)
    {
        if (href.Length > LinkUris.MaxLength)
        {
            return null;
        }

        var url = WithoutWhiteSpace(href);
        return url.StartsWith('#') ? url[1..] : null;
    }

    /// <summary>A fragment percent-decoded and read as UTF-8 without a byte order mark, an invalid sequence read as
    /// U+FFFD (URL Standard §1.3, "percent-decode", and Encoding Standard "UTF-8 decode without BOM").</summary>
    /// <param name="fragment">The fragment.</param>
    /// <returns>The decoded text.</returns>
    internal static string PercentDecoded(string fragment)
    {
        var bytes = new System.Collections.Generic.List<byte>(fragment.Length);
        for (var i = 0; i < fragment.Length; i++)
        {
            if (fragment[i] == '%' && i + 2 < fragment.Length && Uri.IsHexDigit(fragment[i + 1]) && Uri.IsHexDigit(fragment[i + 2]))
            {
                bytes.Add((byte)((Uri.FromHex(fragment[i + 1]) << 4) | Uri.FromHex(fragment[i + 2])));
                i += 2;
            }
            else
            {
                var length = char.IsHighSurrogate(fragment[i]) && i + 1 < fragment.Length && char.IsLowSurrogate(fragment[i + 1]) ? 2 : 1;
                bytes.AddRange(Encoding.UTF8.GetBytes(fragment.Substring(i, length)));
                i += length - 1;
            }
        }

        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false).GetString([.. bytes]);
    }

    /// <summary>URL Standard §4.4, basic URL parser: leading and trailing C0 controls and spaces removed, and every ASCII
    /// tab or newline (tab, line feed, carriage return) inside.</summary>
    private static string WithoutWhiteSpace(string href)
    {
        var start = 0;
        var end = href.Length;
        while (start < end && href[start] <= ' ')
        {
            start++;
        }

        while (end > start && href[end - 1] <= ' ')
        {
            end--;
        }

        var builder = new StringBuilder(end - start);
        for (var index = start; index < end; index++)
        {
            if (href[index] is not ('\t' or '\n' or '\r'))
            {
                builder.Append(href[index]);
            }
        }

        return builder.ToString();
    }

    /// <summary>A backslash before the query or the fragment as a slash (URL Standard §4.4: in a special URL, the
    /// relative slash, path start and path states read <c>\</c> as <c>/</c>).</summary>
    private static string Slashed(string reference)
    {
        var end = reference.IndexOfAny(['?', '#']);
        return end < 0 ? reference.Replace('\\', '/') : reference[..end].Replace('\\', '/') + reference[end..];
    }

    /// <summary>The five parts of a URI reference by the regular expression of RFC 3986 Appendix B; a part that is not
    /// there is null.</summary>
    private static (string? Scheme, string? Authority, string Path, string? Query, string? Fragment) Parts(string reference)
    {
        var match = ReferenceParts().Match(reference);
        static string? Group(Match match, int index) => match.Groups[index].Success ? match.Groups[index].Value : null;
        return (Group(match, 2), Group(match, 4), match.Groups[5].Value, Group(match, 7), Group(match, 9));
    }

    [GeneratedRegex(@"^(([^:/?#]+):)?(//([^/?#]*))?([^?#]*)(\?([^#]*))?(#(.*))?$", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex ReferenceParts();

    /// <summary>RFC 3986 §5.2.2, a reference without a scheme against a base, and §5.3, the parts recomposed.</summary>
    private static string Resolved((string? Scheme, string? Authority, string Path, string? Query, string? Fragment) baseParts,
        (string? Scheme, string? Authority, string Path, string? Query, string? Fragment) reference)
    {
        string? authority;
        string path;
        string? query;
        if (reference.Authority is not null)
        {
            (authority, path, query) = (reference.Authority, WithoutDotSegments(reference.Path), reference.Query);
        }
        else if (reference.Path.Length == 0)
        {
            (authority, path, query) = (baseParts.Authority, baseParts.Path, reference.Query ?? baseParts.Query);
        }
        else if (reference.Path.StartsWith('/'))
        {
            (authority, path, query) = (baseParts.Authority, WithoutDotSegments(reference.Path), reference.Query);
        }
        else
        {
            // §5.2.3: merge with the base's path.
            var merged = baseParts.Authority is not null && baseParts.Path.Length == 0
                ? "/" + reference.Path
                : baseParts.Path[..(baseParts.Path.LastIndexOf('/') + 1)] + reference.Path;
            (authority, path, query) = (baseParts.Authority, WithoutDotSegments(merged), reference.Query);
        }

        var builder = new StringBuilder();
        builder.Append(baseParts.Scheme!.ToLowerInvariant()).Append(':');
        if (authority is not null)
        {
            builder.Append("//").Append(authority);
        }

        builder.Append(path);
        if (query is not null)
        {
            builder.Append('?').Append(query);
        }

        if (reference.Fragment is not null)
        {
            builder.Append('#').Append(reference.Fragment);
        }

        return builder.ToString();
    }

    /// <summary>RFC 3986 §5.2.4, remove dot segments: one pass over the input, each character moved or dropped once.</summary>
    private static string WithoutDotSegments(string path)
    {
        var output = new StringBuilder(path.Length);
        var input = path.AsSpan();
        while (!input.IsEmpty)
        {
            if (input.StartsWith("../"))
            {
                input = input[3..];
            }
            else if (input.StartsWith("./"))
            {
                input = input[2..];
            }
            else if (input.StartsWith("/./"))
            {
                input = input[2..];
            }
            else if (input.SequenceEqual("/."))
            {
                input = "/";
            }
            else if (input.StartsWith("/../"))
            {
                input = input[3..];
                DropLastSegment(output);
            }
            else if (input.SequenceEqual("/.."))
            {
                input = "/";
                DropLastSegment(output);
            }
            else if (input.SequenceEqual(".") || input.SequenceEqual(".."))
            {
                input = [];
            }
            else
            {
                var next = input[1..].IndexOf('/');
                var length = next < 0 ? input.Length : next + 1;
                output.Append(input[..length]);
                input = input[length..];
            }
        }

        return output.ToString();

        static void DropLastSegment(StringBuilder output)
        {
            var index = output.Length - 1;
            while (index >= 0 && output[index] != '/')
            {
                index--;
            }

            output.Length = Math.Max(index, 0);
        }
    }

    /// <summary>A character beyond ASCII in the host of an http or https URL, which the URL Standard would write in its
    /// ASCII form (IDNA); null when there is none.</summary>
    private static string? HostProblem(string uri)
    {
        var parts = Parts(uri);
        if (parts.Scheme is not ("http" or "https") || parts.Authority is not { } authority)
        {
            return null;
        }

        foreach (var character in authority)
        {
            if (character > '~')
            {
                return $"the host '{authority}' holds a character beyond ASCII; write it in its ASCII form (the IDNA form beginning xn--), which this version does not make";
            }
        }

        return null;
    }

    /// <summary>The URL with every code point of the shared percent-encode set (the C0 controls, space, <c>"</c>,
    /// <c>&lt;</c>, <c>&gt;</c>, U+007F and every code point above U+007E) written as the percent-encoded bytes of its UTF-8
    /// form; a lone surrogate as U+FFFD, as UTF-8 encoding does.</summary>
    private static string Encoded(string url)
    {
        var builder = new StringBuilder(url.Length);
        Span<byte> bytes = stackalloc byte[4];
        for (var index = 0; index < url.Length; index++)
        {
            var character = url[index];
            if (character is > ' ' and < '\u007f' and not ('"' or '<' or '>'))
            {
                builder.Append(character);
                continue;
            }

            var length = char.IsHighSurrogate(character) && index + 1 < url.Length && char.IsLowSurrogate(url[index + 1]) ? 2 : 1;
            var rune = length == 2 ? new Rune(character, url[index + 1]) : Rune.IsValid(character) ? new Rune(character) : Rune.ReplacementChar;
            var count = rune.EncodeToUtf8(bytes);
            for (var at = 0; at < count; at++)
            {
                builder.Append('%').Append(bytes[at].ToString("X2", CultureInfo.InvariantCulture));
            }

            index += length - 1;
        }

        return builder.ToString();
    }
}
