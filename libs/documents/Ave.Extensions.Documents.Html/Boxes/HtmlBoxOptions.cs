using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Fonts;
using Ave.Extensions.ErrorPaths;

namespace Ave.Extensions.Documents.Html.Boxes;

/// <summary>How to turn a styled HTML document into the document model (<see cref="HtmlBoxGenerator"/>).</summary>
public sealed record HtmlBoxOptions
{
    /// <summary>No fonts, no resources, refuse what is not drawn.</summary>
    public static HtmlBoxOptions Default { get; } = new();

    /// <summary>The caller's fonts. A family the document names is taken from its <c>@font-face</c> rules first, then from
    /// these; there are no system fonts and no fallback per character.</summary>
    public FontSet Fonts { get; init; } = new([]);

    /// <summary>Gives the font files <c>@font-face</c> rules name; null for none (each one needed is then refused).</summary>
    public ResourceResolver? Resources { get; init; }

    /// <summary>The document's URL, the base of the font URLs; null when it has none (URLs are then passed as written).</summary>
    public string? BaseUrl { get; init; }

    /// <summary>Refuse (the default) or only warn; what <see cref="UnsupportedPolicy.Warn"/> does with a value that is not
    /// drawn is said at <see cref="HtmlBoxGenerator"/>.</summary>
    public UnsupportedPolicy Unsupported { get; init; } = UnsupportedPolicy.Refuse;
}

/// <summary>The document model of an HTML document, with the fonts its text uses.</summary>
/// <param name="Document">The document, for <c>DocumentLayout.Run</c>.</param>
/// <param name="Fonts">The fonts layout needs: every face the text and the boxes use, from the <c>@font-face</c> rules and
/// the caller's set.</param>
/// <param name="Diagnostics">The style resolution's diagnostics and those of box generation (under
/// <see cref="UnsupportedPolicy.Warn"/>, also the refusals).</param>
public sealed record HtmlBoxes(StyledDocument Document, FontSet Fonts, ValueList<Error> Diagnostics);
