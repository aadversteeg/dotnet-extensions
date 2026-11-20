using System;
using System.Collections.Generic;
using System.Linq;
using Ave.Extensions.Documents.Html.Parsing;

namespace Ave.Extensions.Documents.Html;

/// <summary>URL resolution, one rule for every URL the HTML front-end follows: style sheets (link, @import), images, and
/// the font files of @font-face rules (against the base of the sheet that declares them).</summary>
internal static class Urls
{
    /// <summary>A URL resolved against a base; null when it does not parse. Without a base, an absolute URL stays as
    /// it is and a relative one is passed on as written.</summary>
    internal static string? Absolute(string url, string? baseUrl)
    {
        // Only a URL with a scheme is absolute: on Unix, Uri takes "/other.css" for an absolute file path.
        if (HasScheme(url) && Uri.TryCreate(url, UriKind.Absolute, out var absolute))
        {
            return absolute.AbsoluteUri;
        }

        if (baseUrl is null)
        {
            return url;
        }

        return HasScheme(baseUrl) && Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) && Uri.TryCreate(baseUri, url, out var resolved) ? resolved.AbsoluteUri : null;
    }

    /// <summary>The document base URL (HTML Standard §2.4.1): the href of the first base element in tree order that has
    /// one, resolved against the document's URL; else the document's URL. One rule for the style resolver and box
    /// generation. The tree is walked with an explicit stack.</summary>
    internal static string? DocumentBase(HtmlDocument document, string? documentUrl)
    {
        var pending = new Stack<HtmlNode>(document.Children.Reverse());
        while (pending.Count > 0)
        {
            if (pending.Pop() is HtmlElement element)
            {
                if (element.IsHtml("base") && element.Attribute("href") is { } href)
                {
                    return Absolute(href.Trim(), documentUrl) ?? documentUrl;
                }

                for (var i = element.Children.Count - 1; i >= 0; i--)
                {
                    pending.Push(element.Children[i]);
                }
            }
        }

        return documentUrl;
    }

    /// <summary>Whether a URL starts with a scheme (URL Standard §4.4: an ASCII alpha, then alphanumerics, +, - or .,
    /// then a colon).</summary>
    internal static bool HasScheme(string url)
    {
        var colon = url.IndexOf(':', StringComparison.Ordinal);
        return colon > 0 && char.IsAsciiLetter(url[0]) && url[..colon].All(c => char.IsAsciiLetterOrDigit(c) || c is '+' or '-' or '.');
    }
}
