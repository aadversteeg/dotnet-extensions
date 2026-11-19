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

    /// <summary>The advance of the last glyph of each line of a left-aligned paragraph "AV AV" broken after the first space.</summary>
    private static List<double> LastAdvances(ValueList<TextDecoration> decorations)
    {
        var paragraph = new Box(new NodeId(1), SemanticRole.Paragraph, LayoutAlgorithmId.Inline, BoxStyle.Initial,
            [new TextRun(new NodeId(2), "AV AV", Text(20) with { Decorations = decorations }, At(0))], At(0));
        var laid = (LayoutResult.Placed)LayoutEnvironment.Create(TestFonts.Set).Layout(paragraph, new ConstraintSpace(40, double.PositiveInfinity, IsAtPageStart: true), null).Value;
        return [.. ((BoxFragment)laid.Fragment).Children.Select(line => ((LineFragment)line.Fragment).Children.Select(child => child.Fragment).OfType<TextFragment>().Single().Glyphs[^1].Advance)];
    }

    [Fact]
    public void A_line_that_ends_in_a_decorated_run_is_shaped_again_before_its_trailing_space()
    {
        // Lato kerns "V" with the space after it. Left-aligned text keeps the paragraph's glyphs at a line end at a space, so
        // the first line's "V" keeps that kerning; a decorated run needs its accurate end (Blink NeedsAccurateEndPosition), so
        // its first line ends with the "V" shaped without the space, as the last line's "V" is.
        var plain = LastAdvances([]);
        var decorated = LastAdvances([Line(TextDecorationLine.Underline, 20)]);

        plain.Should().HaveCount(2);
        decorated.Should().HaveCount(2);
        decorated[0].Should().BeApproximately(decorated[1], 1e-9, "both lines end with \"AV\" shaped alone");
        plain[0].Should().NotBeApproximately(plain[1], 1e-6, "the plain first line keeps the kerning of \"V\" with the space");
    }

    [Fact]
    public void A_piece_of_no_width_draws_no_line()
    {
        // A decorated run of only a zero width space has a piece on the line with no advance; Chromium paints nothing for a
        // line of no width (Blink DecorationLinePainter::Paint: "if (geometry.line.width() <= 0) return").
        var paragraph = new Box(new NodeId(1), SemanticRole.Paragraph, LayoutAlgorithmId.Inline, BoxStyle.Initial,
        [
            new TextRun(new NodeId(2), "ab", Text(20), At(0)),
            new TextRun(new NodeId(3), "\u200B", Text(20) with { Decorations = [Line(TextDecorationLine.Underline, 20)] }, At(0)),
            new TextRun(new NodeId(4), "cd", Text(20), At(0)),
        ], At(0));

        DocumentLayout.Run(Document(paragraph), TestFonts.Set).Value.Pages[0].Items.OfType<FillRectangle>().Should().BeEmpty();
    }
}
