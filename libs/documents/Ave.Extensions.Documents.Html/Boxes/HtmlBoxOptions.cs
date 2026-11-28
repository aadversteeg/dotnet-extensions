using System;
using System.Collections.Immutable;
using System.Text;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Fonts;
using Ave.Extensions.ErrorPaths;

namespace Ave.Extensions.Documents.Html.Boxes;

/// <summary>How to turn a styled HTML document into the document model (<see cref="HtmlBoxGenerator"/>).</summary>
public sealed record HtmlBoxOptions
{
    private readonly ImmutableHashSet<string>? _schemes;

    /// <summary>No fonts, no resources, refuse what is not drawn.</summary>
    public static HtmlBoxOptions Default { get; } = new();

    /// <summary>The caller's fonts. A family the document names is taken from its <c>@font-face</c> rules first, then from
    /// these; there are no system fonts and no fallback per character.</summary>
    public FontSet Fonts { get; init; } = new([]);

    /// <summary>The families the generic families and the initial value of <c>font-family</c> stand for; a generic
    /// family they do not map is refused (<see cref="Boxes.GenericFamilies"/>).</summary>
    public GenericFamilies GenericFamilies { get; init; } = GenericFamilies.None;

    /// <summary>Gives the font files <c>@font-face</c> rules name; null for none (each one needed is then refused).</summary>
    public ResourceResolver? Resources { get; init; }

    /// <summary>The document's URL, the base of the font URLs of an <c>@font-face</c> rule without a base of its own
    /// (<see cref="Css.Rules.FontFaceRule.BaseUrl"/>); null when it has none (URLs are then passed as written).</summary>
    public string? BaseUrl { get; init; }

    /// <summary>Whether a <c>base</c> element may replace <see cref="BaseUrl"/> (<see cref="HtmlStyleOptions.AllowBaseElement"/>;
    /// give both the same value). Default false.</summary>
    public bool AllowBaseElement { get; init; }

    /// <summary>The URL schemes the resolver may be asked for (<see cref="HtmlStyleOptions.AllowedResourceSchemes"/>), as
    /// names in lower case. Null, the default, is the set the styled document was resolved with when its style resolution
    /// had a resolver, else <c>http</c> and <c>https</c>; so a caller sets it once, on the style options, and here only
    /// to differ.</summary>
    /// <exception cref="ArgumentException">A name is null or not a scheme by the URL Standard's syntax (§4.4, scheme
    /// state: an ASCII letter, then ASCII letters, digits, <c>+</c>, <c>-</c> or <c>.</c>, without the colon), such as
    /// <c>"file:"</c>, <c>" file "</c>, <c>"*"</c> or <c>""</c>.</exception>
    public ImmutableHashSet<string>? AllowedResourceSchemes
    {
        get => _schemes;
        init => _schemes = value is null ? null : ResourceTable.Schemes(value, nameof(AllowedResourceSchemes));
    }

    /// <summary>Refuse (the default) or only warn; what <see cref="UnsupportedPolicy.Warn"/> does with a value that is not
    /// drawn is said at <see cref="HtmlBoxGenerator"/>.</summary>
    public UnsupportedPolicy Unsupported { get; init; } = UnsupportedPolicy.Refuse;

    /// <summary>The characters of a text a face has no glyph for, each with the offset of its cluster: the shaper's own
    /// rule (<c>Shaper.MissingCharacters</c> of the typesetting package, which <c>HtmlRendering</c> gives; a front-end does
    /// not reference the shaper). With it, box generation refuses every such character of the document at its own place
    /// (<see cref="HtmlBoxGenerator"/>); without it (null, the default), layout refuses those of the first paragraph that
    /// has one, at the line of its text.</summary>
    public Func<string, TrueTypeFont, ImmutableArray<(int Offset, Rune Character)>>? MissingCharacters { get; init; }
}

/// <summary>The document model of an HTML document, with the fonts its text uses.</summary>
/// <param name="Document">The document, for <c>DocumentLayout.Run</c>.</param>
/// <param name="Fonts">The fonts layout needs: every face the text and the boxes use, from the <c>@font-face</c> rules and
/// the caller's set.</param>
/// <param name="Diagnostics">The style resolution's diagnostics and those of box generation (under
/// <see cref="UnsupportedPolicy.Warn"/>, also the refusals).</param>
public sealed record HtmlBoxes(StyledDocument Document, FontSet Fonts, ValueList<Error> Diagnostics);
