using System;
using System.Collections.Immutable;
using Ave.Extensions.Documents.Css.Cascade;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Css.Media;
using Ave.Extensions.Documents.DocumentModel;

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
/// base URL a relative URL is passed as written. The front-end calls it at most once per URL in one render (a style
/// resolution and the box generation of the document it makes), synchronously, for at most 4 096 distinct URLs of at
/// most 32 767 characters each, and never opens a file or a socket itself. The URL comes from the document and is
/// untrusted: it can have any scheme (<c>file:</c>, a Windows share written as <c>file://host/share/x</c>, <c>ftp:</c>,
/// <c>javascript:</c>) and climb out of the base with <c>../</c>, but it reaches the resolver only when its scheme is one
/// the caller allows (<see cref="HtmlStyleOptions.AllowedResourceSchemes"/>, default <c>http</c> and <c>https</c>), or when
/// it is a reference without a scheme and without a host (<c>/images/x.png</c>, <c>../x.png</c>) and there is no base
/// URL; a reference that names a host without a scheme (<c>//host/x</c>, <c>\\host\share\x</c>) is never handed on as
/// written; a <c>base</c> element moves it only when the caller
/// allows it (<see cref="HtmlStyleOptions.AllowBaseElement"/>). The URL never has a fragment and is never empty: a
/// reference that is only a fragment, with no base URL, is not there. A <c>data:</c> URL never reaches the resolver,
/// with or without a fragment: the engine decodes it itself. So the resolver must not map a URL to a file path without confining it to a folder, nor fetch
/// one from a host it does not trust; null refuses it. What it gives for a style sheet is read as CSS: give null
/// for a resource that is not one, as Chromium applies only a resource served as text/css. An empty URL and the
/// document's own address are not asked for a style sheet: both are an empty one.
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
    private readonly MediaContext? _media;
    private readonly ValueContext? _values;
    private readonly ImmutableHashSet<string> _schemes = ResourceTable.DefaultSchemes;

    /// <summary>A4 with no margins, the default <see cref="Page"/> (as <see cref="MediaContext.PrintA4"/>).</summary>
    public static PageStyle A4Page { get; } = new(Length.FromMillimetres(210), Length.FromMillimetres(297), default);

    /// <summary>The default: print on A4 with no margins, no fonts for ex and ch, no resources, refuse what is
    /// unsupported.</summary>
    public static HtmlStyleOptions Default { get; } = new();

    /// <summary>The page without <c>@page</c> rules: the paper of the print call. Chromium prints a document without an
    /// <c>@page</c> rule on it, takes its size for <c>size: auto</c> and turns it for a bare <c>portrait</c> or
    /// <c>landscape</c> (<c>DesiredPageContainingBlockSize</c>, pagination_utils.cc); <see cref="Media"/> and
    /// <see cref="Values"/> follow it unless they are set. Its template is not used. Default: <see cref="A4Page"/>.</summary>
    public PageStyle Page { get; init; } = A4Page;

    /// <summary>The media @media rules and media attributes are evaluated for: unless set, print on the page area of
    /// <see cref="Page"/>. Chromium evaluates them against the paper of the print call also where an <c>@page</c> rule
    /// sets another size (measured: a 300 pt <c>@page</c> on US Letter matches <c>min-width: 600px</c>, not
    /// <c>max-width: 500px</c>; cases <c>h-media-page-size-*</c>).</summary>
    public MediaContext Media
    {
        get => _media ?? new MediaContext("print", Page.AreaWidth, Page.AreaHeight);
        init => _media = value;
    }

    /// <summary>The viewport and fonts relative units need: unless set, the page area of <see cref="Page"/> as the
    /// viewport and no fonts.</summary>
    public ValueContext Values
    {
        get => _values ?? new ValueContext(Page.AreaWidth, Page.AreaHeight);
        init => _values = value;
    }

    /// <summary>Gives the style sheets link elements and @import name; null for none (each one named is then
    /// refused).</summary>
    public ResourceResolver? Resources { get; init; }

    /// <summary>The document's URL, the base of its relative URLs; null when it has none.</summary>
    public string? BaseUrl { get; init; }

    /// <summary>Whether a <c>base</c> element in the document may replace <see cref="BaseUrl"/> as the base of its
    /// relative URLs (HTML Standard §2.4.1). Default false: the caller's base URL is the one the document's resources and
    /// links are resolved against, and a <c>base</c> element with an <c>href</c> is refused
    /// (<c>Documents.Html.Unsupported.Element</c>), or under <see cref="UnsupportedPolicy.Warn"/> reported and left out.</summary>
    public bool AllowBaseElement { get; init; }

    /// <summary>The URL schemes the resolver may be asked for (<see cref="Resources"/>), as names in lower case. Default
    /// <c>http</c> and <c>https</c>. The scheme of a URL (URL Standard §4.4, scheme state: an ASCII letter, then letters,
    /// digits, <c>+</c>, <c>-</c> and <c>.</c>, before the first colon) is compared ASCII case-insensitively, so
    /// <c>FILE:</c> is <c>file:</c>; names given in another case are lowered. It is read from the reference as the
    /// document writes it, before anything is resolved: a reference with a scheme is absolute (<c>x:</c> has the scheme
    /// <c>x</c>, <c>C:\x</c> the scheme <c>c</c>), and one without takes the scheme of the base URL (<see cref="BaseUrl"/>,
    /// or the style sheet that names it). A reference that names a host without a scheme (two slashes or backslashes) is
    /// refused without a base URL, and with one when it has a backslash or more than two slashes. A URL of a scheme not in the set
    /// is not there: it is refused (<c>Documents.Html.Resource.NotFound</c>, or for an image
    /// <c>Documents.Html.Unsupported.Rendering</c>), or under <see cref="UnsupportedPolicy.Warn"/> reported and left out,
    /// and the resolver is not asked. Without a base URL a relative reference has no scheme: it is handed on as the
    /// document writes it, the caller's own naming; an absolute URL in the document is checked all the same. With a base
    /// URL whose own scheme is not in the set, every relative reference resolves to that scheme and is not there: a
    /// caller that sets a <c>file:</c> base URL must allow <c>file</c> (and then confine every URL to a folder, as
    /// <see cref="ResourceResolver"/> says). A <c>data:</c> URL is read by the engine and never asked, whatever the set
    /// holds. Give <see cref="Boxes.HtmlBoxOptions.AllowedResourceSchemes"/> the same set.</summary>
    /// <exception cref="ArgumentNullException">The set is null.</exception>
    public ImmutableHashSet<string> AllowedResourceSchemes
    {
        get => _schemes;
        init => _schemes = ResourceTable.Schemes(value);
    }

    /// <summary>Refuse (the default) or only warn.</summary>
    public UnsupportedPolicy Unsupported { get; init; } = UnsupportedPolicy.Refuse;
}
