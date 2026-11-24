using System.Collections.Generic;
using System.Linq;
using Ave.Extensions.Documents.Core;

namespace Ave.Extensions.Documents.PageModel;

/// <summary>
/// Writes a page document as stable, human-readable text: one line per draw item, grouped by page. Used for
/// golden-file tests and for checking by eye (or by an agent) what will be drawn, without opening a PDF.
/// </summary>
/// <example>
/// <code>
/// document title="Letter"
/// page 1 size=595.276x841.89
///   start #1 paragraph
///   glyphs Lato-Regular 11pt #000000 at (56.693, 68.02) "Hi" g44@0 g78@7.645
///   end #1
/// </code>
/// </example>
public static class PageModelText
{
    /// <summary>Writes the document.</summary>
    /// <param name="document">The document.</param>
    /// <returns>The text, lines separated by <c>\n</c>, ending with a line feed.</returns>
    public static string Write(PageDocument document)
    {
        var metadata = document.Metadata;
        var header = "document"
            + (metadata.Title is { } title ? $" title={QuotedText.Quote(title)}" : string.Empty)
            + (metadata.Author is { } author ? $" author={QuotedText.Quote(author)}" : string.Empty)
            + (metadata.Description is { } description ? $" description={QuotedText.Quote(description)}" : string.Empty)
            + (metadata.Language is { } language ? $" language={language}" : string.Empty);
        IEnumerable<string> lines =
        [
            header,
            .. metadata.Attachments.Select(attachment =>
                $"attachment {QuotedText.Quote(attachment.FileName)} {attachment.MediaType} {attachment.Content.Count} bytes"
                + (attachment.Relationship is { } relationship ? $" relationship={relationship}" : string.Empty)),
            .. document.Pages.SelectMany((page, index) => PageLines(page, index + 1)),
        ];
        return string.Join("\n", lines) + "\n";
    }

    private static IEnumerable<string> PageLines(Page page, int number) =>
        [$"page {number} size={page.Size}", .. page.Items.Select(item => "  " + ItemLine(item))];

    private static string ItemLine(DrawItem item) => item switch
    {
        FillRectangle fill => $"fill {fill.Area} {fill.Color}",
        FillPath path => FilledPathLine(path),
        DrawImage image => $"image {image.Area} {image.Image.Describe()} id={image.Image.Id}",
        GlyphRun run => GlyphRunLine(run),
        StructureStart start => $"start {start.Node} {start.Role}" + (start.Span == CellSpan.Single ? string.Empty : $" span={start.Span}") + (start.Language is { } language ? $" lang={language}" : string.Empty),
        StructureEnd end => $"end {end.Node}",
        ArtifactStart artifact => $"artifact {artifact.Kind}",
        ArtifactEnd => "end artifact",
        ClipStart clip => $"clip {clip.Area}",
        ClipEnd => "end clip",
        LinkArea link => $"link-area {link.Link} {link.Area}",
        Anchor anchor => $"anchor {anchor.Node} {anchor.At}",
        _ => $"unknown {item.GetType().Name}",
    };

    /// <summary>A filled path: its rule and colour, then each contour as <c>M</c> and its start, each segment as <c>L</c>
    /// and its end or <c>C</c> and its two control points and end, and <c>Z</c> where the contour closes.</summary>
    private static string FilledPathLine(FillPath path) =>
        $"path {path.Rule} {path.Color} " + string.Join(" ", path.Contours.Select(contour =>
            $"M {contour.Start} " + string.Concat(contour.Segments.Select(segment => segment switch
            {
                PathCubic cubic => $"C {cubic.Control1} {cubic.Control2} {cubic.End} ",
                _ => $"L {segment.End} ",
            })) + "Z"));

    private static string GlyphRunLine(GlyphRun run)
    {
        var text = string.Concat(run.Glyphs.Select(glyph => glyph.Text));
        // A glyph's vertical offset (a mark raised onto its base) is written after its x when it is not 0.
        var glyphs = string.Join(" ", run.Glyphs.Select(glyph =>
            $"{glyph.Glyph}@{InvariantNumber.Format(glyph.X)}" + (glyph.Y == 0 ? string.Empty : $",{InvariantNumber.Format(glyph.Y)}")));
        return $"glyphs {run.Font} {InvariantNumber.Format(run.FontSize)}pt {run.Color} at {run.Origin} "
            + $"{QuotedText.Quote(text)} {glyphs}";
    }
}
