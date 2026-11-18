using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Fluent;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.Functional;
using static Ave.Extensions.Documents.Fluent.Elements;

namespace UnitTests.Extensions.Documents.Fluent;

/// <summary>
/// <c>line-height</c> and the strut (CSS 2.1 §10.8.1; CSS Inline 3 §5.1, §5.3), against Chromium's print of the same
/// paragraphs (<c>tools/reference/chromium</c>, cases <c>d-*</c>). The line heights are whole pixels (18 pt = 24 px,
/// 36 pt = 48 px), which Chromium keeps exact, so the distance between two baselines is compared exactly on both sides,
/// and which words are on which page exactly. Page area 276 × 150 pt. The vertical place of the first baseline in its
/// line is not compared: Chromium rounds the font's ascent and descent to whole pixels and floors the half-leading above
/// (<c>font_metrics.cc</c>, <c>line_utils.cc</c>), the engine keeps the font's exact values (docs/architecture.md,
/// "Against Chromium").
/// </summary>
public class LineHeightTests
{
    private static PageSetup Area => Page(300.Pt(), 174.Pt()).Margin(12.Pt());

    private static ValueList<Span> Lines(int count, string prefix) =>
        [.. Enumerable.Range(0, count).SelectMany(i => i == 0 ? new[] { Text($"{prefix}{i}") } : [LineBreak(), Text($"{prefix}{i}")])];

    private static Result<StyledDocument, global::Ave.Extensions.ErrorPaths.Error> Model(params Element[] body) =>
        DocumentTranslator.ToModel(Document([.. body], Area, title: "Title", language: "en").FontFamily("Lato"));

    /// <summary>The engine prints the words on Chromium's pages, and on page 1 both put consecutive baselines
    /// <paramref name="pitch"/> apart (null: not compared).</summary>
    private static void ShouldPrintLikeChromium(string caseName, double? pitch, params Element[] body)
    {
        var engine = Model(body).OnSuccessBind(model => DocumentLayout.Run(model, TestFonts.SetWithItalic)).OnSuccessMap(PrintedPages.Engine);

        engine.IsSuccess.Should().BeTrue(engine.IsFailure ? engine.Error.ToString() : string.Empty);
        var chromium = PrintedPages.Chromium(caseName);
        engine.Value.PageTexts.Should().Equal(chromium.PageTexts, $"the words of each page as Chromium prints {caseName}");
        if (pitch is { } expected)
        {
            Pitches(engine.Value).Should().AllSatisfy(distance => distance.Should().BeApproximately(expected, 1e-9, "the engine's line height"));
            Pitches(chromium).Should().AllSatisfy(distance => distance.Should().Be(expected, "Chromium's line height, whole pixels"));
        }
    }

    private static IEnumerable<double> Pitches(PrintedPages pages)
    {
        var baselines = pages.Pages[0].Lines.Select(line => line.Baseline).ToList();
        return baselines.Zip(baselines.Skip(1), (upper, lower) => lower - upper);
    }

    [Fact]
    public void A_line_height_length_sets_the_distance_between_baselines() =>
        ShouldPrintLikeChromium("d-length", 18, Paragraph(Lines(12, "l")).LineHeight(18.Pt()));

    [Fact]
    public void A_line_height_number_is_a_multiple_of_the_font_size() =>
        ShouldPrintLikeChromium("d-number", 18, Paragraph(Lines(12, "n")).LineHeight(1.5));

    [Fact]
    public void A_line_height_percentage_is_a_share_of_the_font_size() =>
        ShouldPrintLikeChromium("d-percent", 18, Paragraph(Lines(12, "c")).LineHeightPercent(150));

    [Fact]
    public void A_number_inherits_as_the_number_and_scales_with_a_descendant_s_font_size() =>
        // 1.5 on the block, 24 pt on the paragraph: 36 pt lines.
        ShouldPrintLikeChromium("d-number-inherits", 36, Block([Paragraph(Lines(8, "n")).FontSize(24.Pt())]).LineHeight(1.5));

    [Fact]
    public void A_percentage_inherits_as_the_length_it_computes_to() =>
        // 150 % of the block's 12 pt is 18 pt, kept by the 24 pt paragraph: its lines are 18 pt apart.
        ShouldPrintLikeChromium("d-percent-inherits", 18, Block([Paragraph(Lines(12, "c")).FontSize(24.Pt())]).LineHeightPercent(150));

    [Fact]
    public void The_paragraph_s_strut_keeps_its_lines_at_its_own_line_height_whatever_its_runs_are()
    {
        // The text is 6 pt with a 9 pt line height, the paragraph 12 pt with an 18 pt one: every line starts with the
        // paragraph's strut, so the lines are 18 pt apart, not 9.
        Span[] small = [.. Lines(12, "s").Select(span => span.FontSize(6.Pt()).LineHeight(9.Pt()))];
        ShouldPrintLikeChromium("d-strut", 18, Paragraph([.. small]).LineHeight(18.Pt()));
    }

    [Fact]
    public void With_line_height_normal_the_strut_is_the_paragraph_s_font_s_line_spacing()
    {
        // A 24 pt paragraph of 12 pt text: each line is as high as the paragraph's own font makes it (28.8 pt in the
        // engine, 29.25 pt in Chromium, which rounds the font's metrics to whole pixels), so five lines fit page 1.
        Span[] text = [.. Lines(8, "s").Select(span => span.FontSize(12.Pt()))];
        ShouldPrintLikeChromium("d-strut-normal", null, Paragraph([.. text]).FontSize(24.Pt()));
    }

    [Fact]
    public void The_line_height_is_a_computed_value_of_every_run_and_the_paragraph_s_strut()
    {
        var model = Model(Block([Paragraph([Text("a"), Text("b").FontSize(24.Pt())]).FontSize(10.Pt())]).LineHeightPercent(150));

        model.IsSuccess.Should().BeTrue();
        var paragraph = (Box)((Box)model.Value.Root.Children[0]).Children[0];
        // 150 % of the block's own 12 pt: 18 pt everywhere below it.
        paragraph.Style.Text!.LineHeight.Should().Be(new LineHeight.Fixed(18.Pt()));
        paragraph.Children.Cast<TextRun>().Select(run => run.Style.LineHeight).Should().AllBeEquivalentTo(new LineHeight.Fixed(18.Pt()));
        paragraph.Style.Text.FontSize.Should().Be(10.Pt());
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void A_negative_or_not_finite_line_height_is_refused_at_the_author_s_line(double value)
    {
        var result = Model(Paragraph("p").LineHeight(value), Paragraph("q").LineHeight(value.Pt()), Paragraph("r").LineHeightPercent(value));

        result.IsFailure.Should().BeTrue();
        result.Error.Parts().Select(part => part.Code).Should().Equal(Enumerable.Repeat(DocumentErrorCodes.Input.Invalid.LineHeight, 3));
    }

    [Fact]
    public void A_line_height_of_zero_is_allowed_and_normal_resets_an_inherited_one()
    {
        var model = Model(Block([Paragraph("a"), Paragraph("b").LineHeightNormal()]).LineHeight(0));

        model.IsSuccess.Should().BeTrue();
        var block = (Box)model.Value.Root.Children[0];
        ((Box)block.Children[0]).Style.Text!.LineHeight.Should().Be(new LineHeight.Factor(0));
        ((Box)block.Children[1]).Style.Text!.LineHeight.Should().Be(LineHeight.Normal.Instance);
    }
}
