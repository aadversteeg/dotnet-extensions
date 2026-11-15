using System.Collections.Generic;
using System.Linq;
using Ave.Documents.Core;

namespace Ave.Documents.DocumentModel;

/// <summary>
/// Writes a document model as stable, human-readable text: one line per node, children indented by two spaces.
/// Used for golden-file tests and for reading what a front-end produced. Only properties that differ from
/// their CSS initial value are written, so the text shows what the author actually set.
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
            .. NodeLines(document.Root, depth: 0),
        ];
        return string.Join("\n", lines) + "\n";
    }

    private static IEnumerable<string> NodeLines(Node node, int depth)
    {
        var indent = new string(' ', depth * 2);
        return node switch
        {
            Box box => [indent + BoxLine(box), .. box.Children.SelectMany(child => NodeLines(child, depth + 1))],
            TextRun text => [indent + TextLine(text)],
            _ => [indent + $"unknown {node.GetType().Name} {node.Id} @{node.Source}"],
        };
    }

    private static string BoxLine(Box box)
    {
        var style = box.Style;
        var parts = new List<string> { "box", box.Id.ToString(), box.Layout.ToString(), box.Role.ToString() };
        if (style.Margin != Edges.Zero)
        {
            parts.Add($"margin={style.Margin}");
        }

        if (style.Padding != Edges.Zero)
        {
            parts.Add($"padding={style.Padding}");
        }

        if (style.Background is { } background)
        {
            parts.Add($"background={background}");
        }

        if (style.BreakBefore != BreakBefore.Auto)
        {
            parts.Add("break-before=page");
        }

        if (style.TextAlign != TextAlign.Left)
        {
            parts.Add($"text-align={style.TextAlign.ToString().ToLowerInvariant()}");
        }

        parts.Add($"@{box.Source}");
        return string.Join(" ", parts);
    }

    private static string TextLine(TextRun text) =>
        $"text {text.Id} {QuotedText.Quote(text.Text)} {text.Style.FontFamily} {text.Style.FontSize} "
        + $"w{text.Style.FontWeight} {text.Style.Color} @{text.Source}";

    private static string Attribute(string name, string? value) =>
        value is null ? string.Empty : $" {name}={QuotedText.Quote(value)}";
}
