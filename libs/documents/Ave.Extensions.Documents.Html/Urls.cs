using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Html.Parsing;

namespace Ave.Extensions.Documents.Html;

/// <summary>URL resolution, one rule for every URL the HTML front-end follows: style sheets (link, @import), images, and
/// the font files of @font-face rules (against the base of the sheet that declares them).</summary>
internal static class Urls
{
    /// <summary>
    /// What a reference (a URL as a document writes it) names, by one rule for every place a resource URL comes from: the
    /// URL the resolver may be asked for, or why there is none, as the end of a sentence. The reference is
    /// <see cref="Cleaned"/> first, and its scheme read from it by the URL Standard's syntax (<see cref="Scheme"/>) before
    /// anything is resolved, so what <see cref="Uri"/> makes of it does not decide whether it is absolute.
    /// <list type="bullet">
    /// <item>A reference with a scheme is absolute: it is not resolved against the base URL, and keeps its scheme (in the
    /// form <see cref="Uri.AbsoluteUri"/> gives when that has the same scheme, else as written). Whether its scheme is
    /// allowed is the caller's set (<see cref="ResourceTable"/>); <c>C:\x</c> has the scheme <c>c</c>.</item>
    /// <item>A reference that names a host without a scheme starts, past every character that is white space or a
    /// control character (<see cref="char.IsWhiteSpace(char)"/>, <see cref="char.IsControl(char)"/>), with two slashes
    /// or backslashes in any mix. Without a base URL it is refused. With one, only <c>//host/…</c> written with forward
    /// slashes and nothing before them resolves (URL Standard §4.4, relative slash and special authority slashes states:
    /// it takes the base URL's scheme); three or more slashes name no host and are refused.</item>
    /// <item>With a base URL, a reference with a backslash before its first <c>?</c> or <c>#</c> is refused: a browser
    /// reads that backslash as a slash in a URL of a special scheme (URL Standard §4.4, path start and path states), and
    /// <see cref="Uri"/> does not always (<c>\\host\x</c> becomes a <c>file:</c> URL, <c>/\host/x</c> a path on the
    /// base URL's host).</item>
    /// <item>Any other reference is resolved against the base URL, and is not there when it cannot be, or when the result
    /// does not keep the base URL's scheme. Without a base URL it is handed on as written: a path-absolute
    /// (<c>/images/x.png</c>) or relative (<c>../x.png</c>) reference is the caller's own naming, which the resolver
    /// confines (<see cref="ResourceResolver"/>).</item>
    /// </list>
    /// One pass over the reference for each test; no recursion.
    /// </summary>
    /// <param name="reference">The reference as the document writes it.</param>
    /// <param name="baseUrl">The base URL it is resolved against, or null.</param>
    /// <returns>The URL (with its fragment), or null and the reason.</returns>
    internal static (string? Url, string? Refusal) Resolved(string reference, string? baseUrl)
    {
        var url = Cleaned(reference);
        if (Scheme(url) is { } scheme)
        {
            return (Uri.TryCreate(url, UriKind.Absolute, out var absolute) && InfraAscii.ToLower(absolute.Scheme) == scheme ? absolute.AbsoluteUri : url, null);
        }

        var start = 0;
        while (start < url.Length && (char.IsWhiteSpace(url[start]) || char.IsControl(url[start])))
        {
            start++;
        }

        var slashes = 0;
        while (start + slashes < url.Length && url[start + slashes] is '/' or '\\')
        {
            slashes++;
        }

        if (slashes >= 2 && baseUrl is null)
        {
            return (null, "names a host (it starts with two slashes or backslashes), which cannot be resolved without a base URL, so the resolver is not asked: give a BaseUrl, or name the resource with a path");
        }

        if (baseUrl is null)
        {
            return (url, null);
        }

        if (url.AsSpan(0, url.AsSpan().IndexOfAny('?', '#') is var end and >= 0 ? end : url.Length).Contains('\\'))
        {
            return (null, "has a backslash before its query or fragment, which a browser reads as a slash and the engine does not, so the resolver is not asked: write a slash");
        }

        if (slashes >= 3)
        {
            return (null, "starts with three or more slashes, so it names no host, and the resolver is not asked: write one slash for a path on the host of the base URL, or two before a host");
        }

        if (slashes == 2 && start > 0)
        {
            return (null, "has white space or a control character before the two slashes that name a host, which a browser keeps as part of a path and the engine does not, so the resolver is not asked: remove it");
        }

        return Scheme(baseUrl) is not null && Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) && Uri.TryCreate(baseUri, url, out var resolved) && resolved.Scheme == baseUri.Scheme
            ? (resolved.AbsoluteUri, null)
            : (null, $"cannot be resolved against the base URL '{InputQuote.Of(baseUrl)}', so it is not there: write it as a valid URL");
    }

    /// <summary>A URL as the URL Standard's basic URL parser reads it before any other step (§4.4): every leading and
    /// trailing C0 control and space removed, and every ASCII tab or newline (tab, line feed, carriage return) inside. So
    /// <c>"da\nta:image/png;…"</c> is a <c>data:</c> URL, and <c>"java\tscript:x"</c> has the scheme <c>javascript</c>.
    /// Every test of a URL (its scheme, <c>data:</c>, its fragment, its length) is made on this text.</summary>
    internal static string Cleaned(string url)
    {
        var start = 0;
        var end = url.Length;
        while (start < end && url[start] <= ' ')
        {
            start++;
        }

        while (end > start && url[end - 1] <= ' ')
        {
            end--;
        }

        if (url.AsSpan(start, end - start).IndexOfAny('\t', '\n', '\r') < 0)
        {
            return start == 0 && end == url.Length ? url : url[start..end];
        }

        var builder = new StringBuilder(end - start);
        for (var index = start; index < end; index++)
        {
            if (url[index] is not ('\t' or '\n' or '\r'))
            {
                builder.Append(url[index]);
            }
        }

        return builder.ToString();
    }

    /// <summary>A URL without its fragment (URL Standard §4.1: the part from the first <c>#</c>): a fragment names a
    /// place inside a resource, never another resource, so it is not part of what the resolver is asked.</summary>
    internal static string WithoutFragment(string url) => url.IndexOf('#') is var hash and >= 0 ? url[..hash] : url;

    /// <summary>The document base URL (HTML Standard §2.4.1): the href of the first base element in tree order that has
    /// one, resolved against the document's URL, when the caller allows a base element
    /// (<see cref="HtmlStyleOptions.AllowBaseElement"/>); else, and when that does not give an absolute URL, the
    /// document's URL, the caller's (HTML Standard §4.2.3, the frozen base URL: the fallback base URL when parsing fails). One rule for the style
    /// resolver and box generation. The tree is walked with an explicit stack.</summary>
    internal static string? DocumentBase(HtmlDocument document, string? documentUrl, bool allowBaseElement) =>
        allowBaseElement && BaseElement(document)?.Attribute("href") is { } href && Resolved(href, documentUrl).Url is { } url && HasScheme(url) ? url : documentUrl;

    /// <summary>The first <c>base</c> element with an <c>href</c> in tree order, or null.</summary>
    internal static HtmlElement? BaseElement(HtmlDocument document)
    {
        var pending = new Stack<HtmlNode>(document.Children.Reverse());
        while (pending.Count > 0)
        {
            if (pending.Pop() is HtmlElement element)
            {
                if (element.IsHtml("base") && element.Attribute("href") is not null)
                {
                    return element;
                }

                for (var i = element.Children.Count - 1; i >= 0; i--)
                {
                    pending.Push(element.Children[i]);
                }
            }
        }

        return null;
    }

    /// <summary>Whether a URL starts with a scheme (<see cref="Scheme"/>).</summary>
    internal static bool HasScheme(string url) => Scheme(url) is not null;

    /// <summary>The scheme a URL starts with, in ASCII lower case, or null: by the URL Standard's syntax (§4.4, scheme
    /// start and scheme states), an ASCII letter, then ASCII letters, digits, <c>+</c>, <c>-</c> or <c>.</c>, then a
    /// colon. Read on the URL as it is given, so <c>x:</c> is a URL of the scheme <c>x</c> and <c>C:\x</c> one of the
    /// scheme <c>c</c>.</summary>
    internal static string? Scheme(string url)
    {
        if (url.Length == 0 || !char.IsAsciiLetter(url[0]))
        {
            return null;
        }

        for (var index = 1; index < url.Length; index++)
        {
            var character = url[index];
            if (character == ':')
            {
                return InfraAscii.ToLower(url[..index]);
            }

            if (!(char.IsAsciiLetterOrDigit(character) || character is '+' or '-' or '.'))
            {
                return null;
            }
        }

        return null;
    }
}
