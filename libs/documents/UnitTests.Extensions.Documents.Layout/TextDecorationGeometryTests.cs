using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.Documents.TestSupport;
using static UnitTests.Extensions.Documents.Layout.Models;

namespace UnitTests.Extensions.Documents.Layout;

/// <summary>
/// THE RULE FOR TEXT DECORATION LINES under <see cref="FontMetricRounding.Exact"/>, the code front-end's default profile:
/// Chromium's formulas with nothing rounded to whole pixels (the whole-pixel form is checked against Chromium's print in
/// TextDecorationTests).
/// </summary>
public class TextDecorationGeometryTests
{
    private static TextDecoration Line(TextDecorationLine line, double size) => new(line, Color.Black, "Lato", size.Pt(), FontWeight.Regular);

    private static (Page Page, double Baseline) Laid(TextStyle style)
    {
        var paragraph = new Box(new NodeId(1), SemanticRole.Paragraph, LayoutAlgorithmId.Inline, BoxStyle.Initial,
            [new TextRun(new NodeId(2), "Hxg", style, At(0))], At(0));
        var page = DocumentLayout.Run(Document(paragraph), TestFonts.Set).Value.Pages[0];
        return (page, page.Items.OfType<GlyphRun>().Single().Origin.Y);
    }

    [Fact]
    public void Each_line_lies_where_the_formulas_put_it_unrounded()
    {
        // Lato 20 pt: thickness 2 pt; the underline's gap is half of it rounded up to whole pixels, 2 px = 1.5 pt.
        var style = Text(20) with { Decorations = [Line(TextDecorationLine.Underline, 20), Line(TextDecorationLine.Overline, 20), Line(TextDecorationLine.LineThrough, 20)] };
        var (page, baseline) = Laid(style);
        var ascent = TestFonts.Regular.Metrics.ToPoints(TestFonts.Regular.Metrics.Line.Ascender, 20);

        var lines = page.Items.OfType<FillRectangle>().Select(fill => fill.Area).ToList();
        lines.Should().HaveCount(3);
        lines.Select(area => area.Size.Height).Should().AllSatisfy(height => height.Should().BeApproximately(2, 1e-9));
        lines[0].Origin.Y.Should().BeApproximately(baseline + 1.5, 1e-9, "the underline");
        lines[1].Origin.Y.Should().BeApproximately(baseline - ascent - 2, 1e-9, "the overline, its thickness above the text's top");
        lines[2].Origin.Y.Should().BeApproximately(baseline - ascent + 2 * ascent / 3 - 1, 1e-9, "the line-through");
    }

    [Fact]
    public void A_line_is_at_least_one_pixel_thick_and_its_gap_at_least_one_pixel()
    {
        // Lato 5 pt: a tenth is 0.5 pt, below one pixel (0.75 pt).
        var (page, baseline) = Laid(Text(5) with { Decorations = [Line(TextDecorationLine.Underline, 5)] });

        var line = page.Items.OfType<FillRectangle>().Single().Area;
        line.Size.Height.Should().BeApproximately(0.75, 1e-9);
        line.Origin.Y.Should().BeApproximately(baseline + 0.75, 1e-9);
    }
}
