using System;
using System.Collections.Generic;
using System.Linq;
using Ave.Extensions.Documents.Core;

namespace Ave.Extensions.Documents.DocumentModel;

/// <summary>
/// Writes a document model as stable, human-readable text: one line per node, children indented by two spaces.
/// Used for golden-file tests and for reading what a front-end produced. Only properties that differ from
/// their CSS initial value are written, so the text shows what the author actually set; likewise the layout profile
/// (<see cref="StyledDocument.Profile"/>) only when it is not <see cref="LayoutProfile.Default"/>.
/// </summary>
/// <example>
/// <code>
/// document title="Letter"
/// page size=595.276x841.89 margin=56.693,56.693,56.693,56.693
/// box #0 flow generic @Letter.cs:12
///   box #1 inline heading-1 margin=0,0,6,0 @Letter.cs:13
///     text #2 "Invoice" Lato 24pt w700 #000000 @Letter.cs:13
/// </code>
/// </example>
public static class DocumentModelText
{
    /// <summary>Writes the document.</summary>
    /// <param name="document">The document.</param>
    /// <returns>The text, lines separated by <c>\n</c>, ending with a line feed.</returns>
    public static string Write(StyledDocument document)
    {
        IEnumerable<string> lines =
        [
            "document" + Attribute("title", document.Metadata.Title) + Attribute("author", document.Metadata.Author),
            $"page size={InvariantNumber.Format(document.Page.Width.Points)}x{InvariantNumber.Format(document.Page.Height.Points)}"
                + $" margin={document.Page.Margins}",
            .. document.Profile is { } profile && profile != LayoutProfile.Default
                ? new[] { $"profile monolithic-overflow={profile.MonolithicOverflow} empty-row-parts={profile.EmptyRowParts} font-metric-rounding={profile.FontMetricRounding}" }
                : [],
            .. TemplateLines("page-template", document.Page.Template),
            .. document.Page.FirstPage is { } first ? TemplateLines("first-page-template", first) : [],
            .. NodeLines(document.Root, depth: 0),
        ];
        return string.Join("\n", lines) + "\n";
    }

    private static IEnumerable<string> TemplateLines(string name, PageTemplate template) =>
        template == PageTemplate.Empty
            ? []
            : [name, .. AreaLines("header", template.Header), .. AreaLines("footer", template.Footer)];

    private static IEnumerable<string> AreaLines(string name, PageArea? area) =>
        area is null
            ? []
            : [$"  {name}" + (area.Height is { } height ? $" height={height}" : string.Empty), .. NodeLines(area.Content, depth: 2)];

    private static IEnumerable<string> NodeLines(Node node, int depth)
    {
        var indent = new string(' ', depth * 2);
        return node switch
        {
            Box box => [indent + BoxLine(box), .. box.Children.SelectMany(child => NodeLines(child, depth + 1))],
            TextRun text => [indent + $"text {text.Id} {QuotedText.Quote(text.Text)} {StyleText(text.Style)} @{text.Source}"],
            CounterRun counter => [indent + $"counter {counter.Id} {CounterName(counter.Counter)} {StyleText(counter.Style)} @{counter.Source}"],
            ImageNode image => [indent + ImageLine(image)],
            _ => [indent + $"unknown {node.GetType().Name} {node.Id} @{node.Source}"],
        };
    }

    private static string BoxLine(Box box)
    {
        var style = box.Style;
        var initial = BoxStyle.Initial;
        IEnumerable<string?> parts =
        [
            "box",
            box.Id.ToString(),
            box.Style.TableDisplay switch
            {
                TableDisplay.HeaderGroup => "table-header-group",
                TableDisplay.RowGroup => "table-row-group",
                TableDisplay.Row => "table-row",
                _ => box.Layout.ToString(),
            },
            box.Role.ToString(),
            style.Margin != initial.Margin ? $"margin={style.Margin}" : null,
            style.Padding != initial.Padding ? $"padding={style.Padding}" : null,
            style.Border != initial.Border ? $"border={style.Border}" : null,
            style.Background is { } background ? $"background={background}" : null,
            style.BreakBefore != initial.BreakBefore ? $"break-before={style.BreakBefore.ToString().ToLowerInvariant()}" : null,
            style.BreakAfter != initial.BreakAfter ? $"break-after={style.BreakAfter.ToString().ToLowerInvariant()}" : null,
            style.BreakInside != initial.BreakInside ? $"break-inside={style.BreakInside.ToString().ToLowerInvariant()}" : null,
            style.DecorationBreak != initial.DecorationBreak ? $"box-decoration-break={style.DecorationBreak.ToString().ToLowerInvariant()}" : null,
            style.TextAlign != initial.TextAlign ? $"text-align={style.TextAlign.ToString().ToLowerInvariant()}" : null,
            style.Orphans != initial.Orphans ? $"orphans={style.Orphans}" : null,
            style.Widows != initial.Widows ? $"widows={style.Widows}" : null,
            style.Text is { } strut ? $"strut=({StyleText(strut)})" : null,
            style.VerticalAlign != initial.VerticalAlign ? $"vertical-align={style.VerticalAlign.ToString().ToLowerInvariant()}" : null,
            style.Columns.Count > 0 ? $"columns={string.Join(",", style.Columns)}" : null,
            style.Span != initial.Span ? $"span={style.Span}" : null,
            $"@{box.Source}",
        ];
        return string.Join(" ", parts.OfType<string>());
    }

    private static string StyleText(TextStyle style) =>
        $"{style.FontFamily} {style.FontSize} w{style.FontWeight}{(style.FontStyle == FontStyle.Italic ? " italic" : string.Empty)} {style.Color}"
        + (style.LineHeight is LineHeight.Normal ? string.Empty : $" line-height={style.LineHeight}")
        + (style.WhiteSpaceCollapse == WhiteSpaceCollapse.Collapse ? string.Empty : $" white-space-collapse={Kebab(style.WhiteSpaceCollapse)}")
        + (style.TextWrapMode == TextWrapMode.Wrap ? string.Empty : $" text-wrap-mode={Kebab(style.TextWrapMode).Replace("-", string.Empty, StringComparison.Ordinal)}")
        + string.Concat(style.Decorations.Select(decoration =>
            $" text-decoration=({Kebab(decoration.Line)} {decoration.Color} {decoration.FontFamily} {decoration.FontSize} w{decoration.FontWeight}{(decoration.FontStyle == FontStyle.Italic ? " italic" : string.Empty)})"))
        + (style.DecorationSkipInk == TextDecorationSkipInk.Auto ? string.Empty : $" text-decoration-skip-ink={Kebab(style.DecorationSkipInk)}");

    /// <summary>A value's CSS keyword: <c>PreserveBreaks</c> is <c>preserve-breaks</c>.</summary>
    private static string Kebab<T>(T value)
        where T : struct, Enum =>
        string.Concat(value.ToString().Select((character, index) => char.IsUpper(character) && index > 0 ? $"-{char.ToLowerInvariant(character)}" : char.ToLowerInvariant(character).ToString()));

    private static string CounterName(PageCounter counter) => counter == PageCounter.PageNumber ? "page" : "pages";

    private static string ImageLine(ImageNode image) =>
        $"image {image.Id} {image.Image.Describe()}"
        + (image.Width is { } width ? $" width={width}" : string.Empty)
        + (image.Height is { } height ? $" height={height}" : string.Empty)
        + $" @{image.Source}";

    private static string Attribute(string name, string? value) =>
        value is null ? string.Empty : $" {name}={QuotedText.Quote(value)}";
}
