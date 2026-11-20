using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Css.Cascade;
using Ave.Extensions.Documents.Css.Rules;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Html.Parsing;
using Ave.Extensions.ErrorPaths;

namespace Ave.Extensions.Documents.Html;

/// <summary>A node of the styled tree: an element or a text.</summary>
public abstract record StyledNode;

/// <summary>An element with its computed style.</summary>
/// <param name="Element">The element as parsed (with its position).</param>
/// <param name="Style">Its computed style.</param>
/// <param name="Language">Its language (HTML Standard §3.2.6.2): the nearest lang or xml:lang attribute, else the
/// document's default language; the empty string when the attribute says it is unknown; null when nothing says.</param>
/// <param name="Children">Its element and text children, in order (comments and processing instructions left out).</param>
public sealed record StyledElement(HtmlElement Element, ComputedStyle Style, string? Language, ValueList<StyledNode> Children) : StyledNode;

/// <summary>A text, styled as its parent element (CSS 2.1 §9.2.2.1: text inherits from its parent box).</summary>
/// <param name="Text">The text as parsed.</param>
/// <param name="Style">The parent element's computed style.</param>
public sealed record StyledText(HtmlText Text, ComputedStyle Style) : StyledNode;

/// <summary>An HTML document with the computed style of every element: what stage 4 turns into boxes.</summary>
/// <param name="Document">The parsed document.</param>
/// <param name="Root">The root element, styled.</param>
/// <param name="FontFaces">The <c>@font-face</c> rules that apply, in order.</param>
/// <param name="PageRules">The <c>@page</c> rules that apply, in order.</param>
/// <param name="Diagnostics">Every diagnostic: the parser's, the style sheets', the cascade's.</param>
/// <param name="SourceName">The document's name.</param>
public sealed record StyledHtmlDocument(
    HtmlDocument Document,
    StyledElement Root,
    ValueList<FontFaceRule> FontFaces,
    ValueList<CascadedPageRule> PageRules,
    ValueList<Error> Diagnostics,
    string SourceName)
{
    /// <summary>The page without <c>@page</c> rules that styles were resolved for (<see cref="HtmlStyleOptions.Page"/>):
    /// box generation makes the document's page from it.</summary>
    public PageStyle Page { get; init; } = HtmlStyleOptions.A4Page;

    /// <summary>The page contexts of every page and of page 1 with their generated margin boxes, and the diagnostics of
    /// their cascade (CSS Paged Media 3 §5.3): what box generation makes the margin boxes from. Those diagnostics are not
    /// in <see cref="Diagnostics"/>: box generation reports them when it draws margin boxes.</summary>
    public PageContexts PageContexts { get; init; } = PageContexts.None;
}
