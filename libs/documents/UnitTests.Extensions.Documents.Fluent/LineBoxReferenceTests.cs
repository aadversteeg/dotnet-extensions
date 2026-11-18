using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Fluent;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.Functional;
using static Ave.Extensions.Documents.Fluent.Elements;

namespace UnitTests.Extensions.Documents.Fluent;

/// <summary>
/// Line boxes against Chromium's print (cases <c>fr-empty-span*</c> and <c>fr-lh-*</c> of <c>tools/reference/chromium</c>,
/// the documents of review 40's line-height sub-review). Lato, margin 12 pt.
/// </summary>
public class LineBoxReferenceTests
{
    private static PrintedPages Print(double height, double fontSize, params Element[] body) =>
        DocumentTranslator.ToModel(Document([.. body], Page(300.Pt(), height.Pt()).Margin(12.Pt()), title: "Title", language: "en").FontFamily("Lato").FontSize(fontSize.Pt()))
            .OnSuccessBind(model => DocumentLayout.Run(model, TestFonts.SetWithItalic))
            .OnSuccessMap(PrintedPages.Engine)
            .Value;

    private static double Baseline(PrintedPages pages, string word) => pages.Pages.SelectMany(page => page.Words).Single(printed => printed.Text == word).Baseline;

    [Theory]
    [InlineData("fr-empty-span", "")]
    [InlineData("fr-empty-span-trailing-space", " ")]
    public void An_inline_box_without_a_glyph_on_the_line_counts_for_the_line_height(string caseName, string spanText)
    {
        // CSS 2.1 §10.8: empty inline boxes "still have ... a line height, and thus influence the line box height
        // calculation". A 40 pt line height on an empty span (or one whose space collapses at the line end) makes the
        // 18 pt line 40 pt high, in Chromium 53.33 px; the engine counted only pieces with glyphs (24 px).
        var span = Text(spanText).LineHeight(40.Pt());
        var body = spanText.Length == 0
            ? new Element[] { Paragraph([Text("a"), span, Text("b")]).LineHeight(18.Pt()), Paragraph("after").LineHeight(18.Pt()) }
            : [Paragraph([Text("a"), span]).LineHeight(18.Pt()), Paragraph("after").LineHeight(18.Pt())];
        var engine = Print(624, 12, body);
        var chromium = PrintedPages.Chromium(caseName);

        // Chromium rounds ascent and descent to whole pixels and floors the half-leading (a listed difference): within
        // one pixel (0.75 pt).
        Baseline(engine, "after").Should().BeApproximately(Baseline(chromium, "after"), 0.75);
    }

    [Fact]
    public void Two_font_sizes_on_a_line_with_a_line_height_length_make_the_engine_line_taller_than_Chromium_s()
    {
        // A LISTED difference (docs/architecture.md, "Against Chromium"): ten lines of 15 pt fill the 150 pt page area
        // exactly; the first two carry a 20 pt span with a 9 pt line height. Chromium rounds each box's ascent and descent
        // to whole pixels and floors its half-leading, so those lines stay 20 px; with the font's exact values they are
        // 20.128 px (15.096 pt), and the last two lines go to page 2. FontMetricRounding.WholePixels (pull request 8)
        // prints Chromium's page; this case is committed for it.
        Span Big(string text) => Text(text).FontSize(20.Pt()).LineHeight(9.Pt());
        var lines = new Span[] { Text("x0 "), Big("s0"), LineBreak(), Text("x1 "), Big("s1") }
            .Concat(Enumerable.Range(2, 8).SelectMany(index => new[] { LineBreak(), Text($"x{index} s{index}") }));
        var engine = Print(174, 12, Paragraph([.. lines]).LineHeight(15.Pt()));

        engine.Pages.Should().HaveCount(2, "the engine's mixed lines are 0.096 pt taller each");
        engine.PageTexts[1].Should().Be("x8 s8 x9 s9");
        PrintedPages.Chromium("fr-lh-mixed-sizes").Pages.Should().ContainSingle("Chromium's ten lines fill the page exactly");
    }

    [Fact]
    public void Under_an_explicit_line_height_the_first_baseline_is_within_the_measured_difference_of_Chromium_s()
    {
        // The font metrics move the baseline under an explicit line height too. Lato 10 pt at line-height 1.5: Chromium's
        // used height is 19.984375 px (the product floored to 1/64 px), ascent and descent rounded to 13 + 3 px, half-
        // leading floored to 1 px: the baseline 14 px below the line top. The engine's is 15.16 px: 1.16 px (0.87 pt) lower,
        // the largest difference the review measured. The test allows exactly that, and the pitch is the same in both.
        var engine = Print(624, 10, Paragraph([Text("a0"), LineBreak(), Text("a1"), LineBreak(), Text("a2"), LineBreak(), Text("a3")]).LineHeight(1.5), Paragraph("after"));
        var chromium = PrintedPages.Chromium("fr-lh-first-baseline");

        var difference = Baseline(engine, "a0") - Baseline(chromium, "a0");
        difference.Should().BeInRange(0, 0.87 + 0.001, "engine − Chromium, first baseline, in points");
        (Baseline(engine, "a3") - Baseline(engine, "a0")).Should().BeApproximately(Baseline(chromium, "a3") - Baseline(chromium, "a0"), 0.02, "three line pitches of 15 pt");
    }
}
