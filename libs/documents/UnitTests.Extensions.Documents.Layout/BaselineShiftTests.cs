using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Fonts;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;

namespace UnitTests.Extensions.Documents.Layout;

/// <summary>
/// THE RULE FOR A SHIFTED RUN ON THE LINE (<c>LineBoxes.Build</c>): a run's <see cref="TextStyle.BaselineShift"/> moves its
/// baseline and its inline box, and the line box holds the shifted box. Lato 10.5 pt (14 px) under whole-pixel metrics:
/// ascent 14 px, descent 3 px, no line gap; the shifts are Chromium's super and sub at that size, 362/64 px and 243/64 px
/// (cases <c>va-shift</c>, <c>va-elements</c> of tools/reference/chromium, whose prints these numbers are).
/// </summary>
public class BaselineShiftTests
{
    private const double Px = 0.75;
    private const double Super = 362.0 / 64;
    private const double Sub = 243.0 / 64;

    private static TextStyle Text(LineHeight lineHeight) =>
        new TextStyle(TestFonts.Regular.Names.Family, 10.5.Pt(), FontWeight.Regular, Color.Black) { LineHeight = lineHeight };

    /// <summary>One line: "x ", a run shifted by <paramref name="shiftPixels"/>, " y"; its line fragment.</summary>
    private static LineFragment Line(LineHeight lineHeight, double shiftPixels, TextStyle? shifted = null)
    {
        var style = Text(lineHeight);
        var paragraph = new Box(new NodeId(1), SemanticRole.Paragraph, LayoutAlgorithmId.Inline, BoxStyle.Initial with { Text = style },
            [
                new TextRun(new NodeId(2), "x ", style, Models.At(0)),
                new TextRun(new NodeId(3), "2", (shifted ?? style) with { BaselineShift = (shiftPixels * Px).Pt() }, Models.At(0)),
                new TextRun(new NodeId(4), " y", style, Models.At(0)),
            ], Models.At(0));
        var document = new StyledDocument(new DocumentMetadata("Test", null), new PageStyle(200.Pt(), 100.Pt(), Edges.All(0.Pt())), Models.Flow(0, [paragraph]))
        {
            Profile = new LayoutProfile { FontMetricRounding = FontMetricRounding.WholePixels },
        };

        var page = Paginator.Paginate(LayoutEnvironment.Create(new FontSet([TestFonts.Regular])), document).Value.Single();
        return (LineFragment)((BoxFragment)((BoxFragment)page.Root.Fragment).Children[0].Fragment).Children[0].Fragment;
    }

    private static PlacedFragment Piece(LineFragment line, string text) =>
        line.Children.Single(child => child.Fragment is TextFragment fragment && fragment.Run is TextRun { Text: var own } && own == text);

    [Theory]
    // Under 18pt (24 px): the strut is 3 + 14 px above the baseline and 3 + 4 px below (the 7 px of leading, 3 above).
    // Raised: 17 + 5.656 px above, 7 below, 22.242 pt (va-shift: the next baseline 18.258 pt lower, painted at whole pixels).
    [InlineData(Super, 17 + Super, 7)]
    // Lowered: 17 px above, 7 + 3.797 px below.
    [InlineData(-Sub, 17, 7 + Sub)]
    [InlineData(0, 17, 7)]
    public void A_shifted_run_s_box_moves_with_its_baseline_and_the_line_holds_it_under_a_fixed_line_height(double shift, double above, double below)
    {
        var line = Line(new LineHeight.Fixed(18.Pt()), shift);

        line.Size.Height.Should().BeApproximately((above + below) * Px, 1e-9);
        var word = Piece(line, "x ");
        (word.Offset.Y + ((TextFragment)word.Fragment).Ascent).Should().BeApproximately(above * Px, 1e-9, "the line's baseline lies the highest box top below the line's top");
        var raised = Piece(line, "2");
        (raised.Offset.Y + ((TextFragment)raised.Fragment).Ascent).Should().BeApproximately((above - shift) * Px, 1e-9, "the run's own baseline lies its shift above the line's");
    }

    [Fact]
    public void Under_a_normal_line_height_the_shifted_box_is_its_ascent_and_descent()
    {
        var line = Line(LineHeight.Normal.Instance, Super);

        // va-shift, line height normal: 14 + 5.656 px above, 3 below.
        line.Size.Height.Should().BeApproximately((14 + Super + 3) * Px, 1e-9);
    }

    [Fact]
    public void A_smaller_raised_run_counts_with_its_own_box()
    {
        // A sup at 8.75 pt (11.667 px: ascent 12, descent 2 px) raised by its parent's 5.656 px reaches 17.656 px above
        // the baseline, past the strut's 14 (va-elements: the baseline 13.242 pt below the line's top).
        var line = Line(LineHeight.Normal.Instance, Super, Text(LineHeight.Normal.Instance) with { FontSize = 8.75.Pt() });

        line.Size.Height.Should().BeApproximately((12 + Super + 3) * Px, 1e-9);
    }

    [Fact]
    public void A_shifted_run_s_underline_lies_under_its_own_baseline()
    {
        // The underline of a sup (va-decorated): a whole pixel below the run's own baseline, which is 3 px of half-leading
        // and 14 px of ascent below the line's top; the line's baseline is 5.656 px lower.
        var decorated = Text(new LineHeight.Fixed(18.Pt())) with
        {
            Decorations = [new TextDecoration(TextDecorationLine.Underline, Color.Black, TestFonts.Regular.Names.Family, 10.5.Pt(), FontWeight.Regular)],
            DecorationSkipInk = TextDecorationSkipInk.None,
        };

        var line = Line(new LineHeight.Fixed(18.Pt()), Super, decorated);

        var underline = line.Children.Single(child => child.Fragment is RuleFragment);
        underline.Offset.Y.Should().BeApproximately((3 + 14 + 1) * Px, 1e-9);
    }
}
