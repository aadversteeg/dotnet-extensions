using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Fluent;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.Functional;
using static Ave.Extensions.Documents.Fluent.Elements;

namespace UnitTests.Extensions.Documents.Fluent;

/// <summary>
/// <see cref="FontMetricRounding.WholePixels"/>: the fonts' ascent, descent and line gap each rounded to a whole CSS pixel
/// and the half-leading above floored to one, as Chromium does, against Chromium's print of 40-line paragraphs
/// (<c>tools/reference/chromium</c>, cases <c>f-*</c>; page area 276 × 180 pt, 240 px). Forty lines over several pages,
/// so a difference of 1/64 pixel per line would show at the end. Lato here; the Theano Didot cases are measured too and
/// are compared once the engine reads Theano's typographic metrics (USE_TYPO_METRICS) on this branch.
/// </summary>
public class FontMetricRoundingTests
{
    /// <summary>1/64 CSS pixel in points: the layout unit of Chromium.</summary>
    private const double LayoutUnit = 0.75 / 64;

    private static PageSetup Area => Page(300.Pt(), 204.Pt()).Margin(12.Pt());

    private static ValueList<Span> Lines(string prefix) =>
        [.. Enumerable.Range(0, 40).SelectMany(i => i == 0 ? new[] { Text($"{prefix}{i}") } : [LineBreak(), Text($"{prefix}{i}")])];

    private static PrintedPages Print(FontMetricRounding rounding, ParagraphElement paragraph)
    {
        var result = DocumentTranslator.ToModel(Document([paragraph], Area, title: "Title", language: "en").FontFamily("Lato").FontMetricRounding(rounding))
            .OnSuccessBind(model => DocumentLayout.Run(model, TestFonts.SetWithItalic))
            .OnSuccessMap(PrintedPages.Engine);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        return result.Value;
    }

    /// <summary>Every word as (page, text, baseline).</summary>
    private static List<(int Page, string Text, double Baseline)> Words(PrintedPages pages) =>
        [.. pages.Pages.SelectMany(page => page.Words.Select(word => (page.Number, word.Text, word.Baseline)))];

    [Theory]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(13)]
    public void With_line_height_normal_every_line_is_where_Chromium_puts_it(int size)
    {
        // At 9, 10, 11 and 13 pt Lato's ascent and descent fall on both sides of half a pixel (11.84, 13.16, 14.48, 17.11
        // px; 2.56, 2.84, 3.12, 3.69 px), so each rounding shows. Every line is on Chromium's page within 1/64 pixel; with
        // the exact metrics the lines drift off.
        var paragraph = Paragraph(Lines("l")).FontSize(size.Pt());
        var chromium = Words(PrintedPages.Chromium($"f-lato-{size}"));
        var engine = Words(Print(FontMetricRounding.WholePixels, paragraph));
        var exact = Words(Print(FontMetricRounding.Exact, paragraph));

        engine.Select(word => (word.Page, word.Text)).Should().Equal(chromium.Select(word => (word.Page, word.Text)));
        engine.Zip(chromium).Should().AllSatisfy(pair => pair.First.Baseline.Should().BeApproximately(pair.Second.Baseline, LayoutUnit, pair.First.Text));
        exact.Zip(chromium).Max(pair => Math.Abs(pair.First.Baseline - pair.Second.Baseline)).Should().BeGreaterThan(LayoutUnit, "the case tells the two apart");
    }

    [Theory]
    [InlineData("f-number")]
    [InlineData("f-length")]
    public void With_a_number_or_a_length_every_line_starts_where_Chromium_starts_it_and_Chromium_paints_its_baseline_at_a_whole_pixel(string caseName)
    {
        // Lato 11 pt with line-height 1.3 (19.07 px), and 10 pt with 13.3 pt (17.73 px): not whole pixels. The baseline's
        // place in its line is Chromium's: the rounded ascent plus the floored half-leading (15 and 13 px). Chromium keeps
        // the line height in units of 1/64 pixel (19.0625 and 17.734375 px) and paints each baseline at the nearest whole
        // pixel, which the engine does not do (docs/architecture.md, "Against Chromium"). So the comparison is: the same
        // pages, the same first baseline on every page, and every engine baseline rounded to a whole pixel is Chromium's;
        // before rounding they differ by at most the half pixel painting moves a baseline (pinned: 0.375 pt).
        var paragraph = caseName == "f-number"
            ? Paragraph(Lines("n")).FontSize(11.Pt()).LineHeight(1.3)
            : Paragraph(Lines("g")).FontSize(10.Pt()).LineHeight(13.3.Pt());
        var chromium = Words(PrintedPages.Chromium(caseName));
        var engine = Words(Print(FontMetricRounding.WholePixels, paragraph));

        static double Pixels(double baseline) => Math.Round((baseline - 12) / 0.75, MidpointRounding.AwayFromZero) * 0.75 + 12;
        engine.Select(word => (word.Page, word.Text)).Should().Equal(chromium.Select(word => (word.Page, word.Text)));
        engine.GroupBy(word => word.Page).Select(page => page.First().Baseline).Should().Equal(chromium.GroupBy(word => word.Page).Select(page => page.First().Baseline));
        engine.Select(word => Pixels(word.Baseline)).Should().Equal(chromium.Select(word => word.Baseline));
        engine.Zip(chromium).Max(pair => Math.Abs(pair.First.Baseline - pair.Second.Baseline)).Should().BeLessThanOrEqualTo(0.375);
    }
}
