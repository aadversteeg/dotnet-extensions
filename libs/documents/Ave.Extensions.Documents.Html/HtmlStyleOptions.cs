using System;
using Ave.Extensions.Documents.Css.Cascade;
using Ave.Extensions.Documents.Css.Media;

namespace Ave.Extensions.Documents.Html;

/// <summary>A resource the caller gives for a URL: its bytes, and the charset its protocol named, if any.</summary>
/// <param name="Bytes">The bytes.</param>
public sealed record HtmlResource(ReadOnlyMemory<byte> Bytes)
{
    /// <summary>The charset the protocol gave (Content-Type), or null (CSS Syntax 3 §3.2, "protocol encoding").</summary>
    public string? Charset { get; init; }
}

/// <summary>
/// Gives the resource at a URL, or null when there is none. The URL is absolute: resolved against the document's base
/// URL (<see cref="HtmlStyleOptions.BaseUrl"/>, or a <c>base</c> element) or the style sheet that names it; without a
/// base URL a relative URL is passed as written. The front-end calls it at most once per URL during one resolution,
/// synchronously, and never opens a file or a socket itself.
/// </summary>
/// <param name="url">The URL.</param>
/// <returns>The resource, or null.</returns>
public delegate HtmlResource? ResourceResolver(string url);

/// <summary>What to do with valid HTML and CSS the engine does not implement.</summary>
public enum UnsupportedPolicy
{
    /// <summary>Refuse the document: resolution fails with every refusal (the default; the result would differ from a
    /// browser's).</summary>
    Refuse,

    /// <summary>Report the refusals as diagnostics and resolve anyway, leaving the refused declarations out.</summary>
    Warn,
}

/// <summary>How to resolve the styles of an HTML document.</summary>
public sealed record HtmlStyleOptions
{
    /// <summary>The default: print on A4, no fonts for ex and ch, no resources, refuse what is unsupported.</summary>
    public static HtmlStyleOptions Default { get; } = new();

    /// <summary>The media @media rules and media attributes are evaluated for: print on A4 by default (in print,
    /// Chromium evaluates them against the page area).</summary>
    public MediaContext Media { get; init; } = MediaContext.PrintA4;

    /// <summary>The viewport and fonts relative units need.</summary>
    public ValueContext Values { get; init; } = ValueContext.A4;

    /// <summary>Gives the style sheets link elements and @import name; null for none (each one named is then
    /// refused).</summary>
    public ResourceResolver? Resources { get; init; }

    /// <summary>The document's URL, the base of its relative URLs; null when it has none.</summary>
    public string? BaseUrl { get; init; }

    /// <summary>Refuse (the default) or only warn.</summary>
    public UnsupportedPolicy Unsupported { get; init; } = UnsupportedPolicy.Refuse;
}
