using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Fluent;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.Functional;
using static Ave.Extensions.Documents.Fluent.Elements;

namespace UnitTests.Extensions.Documents.Fluent;

/// <summary>
/// Bottom padding and borders that do not fit below a box's content, and keep rules next to boxes that continue from
/// the page before, against Chromium's print (cases <c>fr-pad-*</c>, <c>fr-border-*</c> and <c>fr-keep-*</c> of
/// <c>tools/reference/chromium</c>). The rule is Chromium's
/// <c>FinishFragmentation</c> (SlicedBox): a box with a breakpoint inside breaks there and keeps that break, unless the
/// break lies inside a box or paragraph kept together and a better break lies before the box (<see cref="KeptBoxBreakTests"/>); any other box
/// breaks before its bottom padding and border, which start the next page; a box that continues from the page before
/// offers its parent no breakpoint inside it.
/// </summary>
public class BottomPaddingTests
{
    private static ParagraphElement Lines(params string[] lines) =>
        Paragraph([.. lines.SelectMany((line, index) => index == 0 ? new[] { Text(line) } : [LineBreak(), Text(line)])]);

    private static void ShouldPrintLikeChromium(string caseName, double width, double height, double? lineHeight, params Element[] body)
    {
        var document = Document([.. body], Page(width.Pt(), height.Pt()).Margin(12.Pt()), title: "Title", language: "en").FontFamily("Lato").FontSize(12.Pt());
        document = lineHeight is { } points ? document.LineHeight(points.Pt()) : document;
        var engine = DocumentTranslator.ToModel(document)
            .OnSuccessBind(model => DocumentLayout.Run(model, TestFonts.SetWithItalic))
            .OnSuccessMap(PrintedPages.Engine);

        engine.IsSuccess.Should().BeTrue(engine.IsFailure ? engine.Error.ToString() : string.Empty);
        engine.Value.PageTexts.Should().Equal(PrintedPages.Chromium(caseName).PageTexts, $"the words of each page as Chromium prints {caseName}");
    }

    [Fact]
    public void Bottom_padding_that_does_not_fit_below_a_line_at_the_page_start_starts_the_next_page()
    {
        // c | (the 51 pt padding) | AFTER.
        ShouldPrintLikeChromium("fr-pad-line", 300, 84, null, Paragraph("c").PaddingBottom(51.Pt()).Background(new Color(0xCC, 0xCC, 0xCC)), Paragraph("AFTER"));
    }

    [Fact]
    public void The_box_ends_right_below_its_content_and_its_padding_alone_fills_the_top_of_the_next_page()
    {
        var pages = DocumentTranslator.ToModel(Document([Paragraph("c").PaddingBottom(51.Pt()).Background(new Color(0xCC, 0xCC, 0xCC)), Paragraph("AFTER")],
                Page(300.Pt(), 84.Pt()).Margin(12.Pt()), title: "Title", language: "en").FontFamily("Lato").FontSize(12.Pt()))
            .OnSuccessBind(model => DocumentLayout.Run(model, TestFonts.SetWithItalic))
            .OnSuccessMap(PrintedPages.Engine).Value;
        var chromium = PrintedPages.Chromium("fr-pad-line");

        // The background: on page 1 the line only (Chromium 12..26.25 with its 14.25 pt line, the engine its 14.4 pt line),
        // on page 2 the padding only, from the top of the page area.
        pages.Pages[0].Rectangles.Single().Height.Should().BeApproximately(14.4, 1e-9);
        chromium.Pages[0].Rectangles.Single().Height.Should().Be(14.25);
        pages.Pages[1].Rectangles.Single().Should().Be(chromium.Pages[1].Rectangles.Single());
    }

    [Fact]
    public void The_same_inside_a_block_with_a_paragraph_after_it()
    {
        ShouldPrintLikeChromium("fr-pad-line-in-block", 300, 84, null, Block([Paragraph("c").PaddingBottom(51.Pt()), Paragraph("IN")]), Paragraph("AFTER"));
    }

    [Fact]
    public void Top_padding_stays_with_the_line_and_bottom_padding_moves_on()
    {
        ShouldPrintLikeChromium("fr-pad-top-and-bottom", 300, 84, null, Paragraph("c").PaddingTop(30.Pt()).PaddingBottom(30.Pt()), Paragraph("AFTER"));
        ShouldPrintLikeChromium("fr-border-top-and-bottom", 300, 84, null, Paragraph("c").BorderTop(24.Pt(), Color.Black).BorderBottom(24.Pt(), Color.Black), Paragraph("AFTER"));
    }

    [Fact]
    public void A_paragraph_that_continues_carries_its_padding_to_the_page_after_its_last_line()
    {
        ShouldPrintLikeChromium("fr-pad-three-lines", 300, 84, null, Lines("a", "b", "c").PaddingBottom(51.Pt()), Paragraph("AFTER"));
    }

    [Fact]
    public void Lower_on_a_page_the_box_moves_whole_since_the_break_before_it_is_better()
    {
        ShouldPrintLikeChromium("fr-pad-after-content", 300, 84, null, Paragraph("x"), Paragraph("c").PaddingBottom(51.Pt()), Paragraph("AFTER"));
    }

    [Fact]
    public void A_bottom_margin_inside_the_box_is_truncated_at_the_break_before_its_padding()
    {
        ShouldPrintLikeChromium("fr-pad-margin-inside", 120, 120, null, Block([Paragraph("a").MarginBottom(40.Pt())]).PaddingBottom(60.Pt()), Paragraph("AFTER"));
    }

    [Fact]
    public void A_box_with_a_breakpoint_inside_breaks_there_although_the_break_before_it_is_better()
    {
        // The break before p1 breaks orphans and widows 2, or a keep rule, and a perfect
        // break before the box exists; Chromium breaks inside the box all the same, and the engine keeps that break.
        ShouldPrintLikeChromium("fr-pad-own-break", 300, 78, 18, Paragraph("ia0"), Block([Lines("p0", "p1")]).PaddingBottom(3.Pt()), Paragraph("z0"));
        ShouldPrintLikeChromium("fr-pad-own-break-paragraph", 300, 78, 18, Paragraph("ia0"), Lines("p0", "p1").PaddingBottom(3.Pt()), Paragraph("z0"));
        ShouldPrintLikeChromium("fr-pad-against-keep", 300, 78, 18, Paragraph("ia0"), Block([Paragraph("p0").KeepWithNext(), Paragraph("q0")]).PaddingBottom(3.Pt()), Paragraph("z0"));
    }

    [Fact]
    public void Where_a_line_does_not_fit_the_box_moves_whole()
    {
        ShouldPrintLikeChromium("fr-pad-line-does-not-fit", 300, 78, 18, Paragraph("ia0"), Paragraph("ib0"), Block([Lines("p0", "p1")]).PaddingBottom(3.Pt()), Paragraph("z0"));
    }

    [Fact]
    public void A_box_that_continues_keeps_its_lines_and_moves_only_its_bottom_border()
    {
        // The box continues from page 1; its content fits page 2, its border does
        // not; it has no breakpoint to offer (its paragraph continues too), so the border alone starts page 3.
        ShouldPrintLikeChromium("fr-border-continued", 300, 68, null,
            Block([Lines("p0", "p1", "p2", "p3", "p4", "p5").Orphans(1).Widows(1)]).BorderBottom(1.5.Pt(), Color.Black), Paragraph("z0"));
    }

    [Fact]
    public void A_framed_figure_that_fits_the_page_only_with_its_top_padding_keeps_it()
    {
        // The figure (800 pt) and its top padding fill page 2, the bottom padding starts page 3.
        ShouldPrintLikeChromium("fr-pad-figure", 595.5, 842.25, null,
            Paragraph("intro"), Block([Image(TestImages.Bytes("rgb8-all-filters.png")).Alt("chart").Height(800.Pt())]).PaddingTop(12.Pt()).PaddingBottom(12.Pt()), Paragraph("AFTER"));
    }

    [Fact]
    public void A_keep_rule_never_reaches_back_into_a_paragraph_that_continues_from_the_page_before()
    {
        // Chromium breaks the keep after h0 rather than break inside the continued paragraph.
        ShouldPrintLikeChromium("fr-keep-continued-paragraph", 300, 69, null,
            Lines("a0", "a1", "a2", "a3", "a4").Orphans(1).Widows(1).KeepWithNext(), Paragraph("h0").Orphans(1).Widows(1).KeepWithNext(), Paragraph("q0").Orphans(1).Widows(1));
    }

    [Fact]
    public void Nor_into_a_box_that_continues_from_the_page_before()
    {
        ShouldPrintLikeChromium("fr-keep-continued-box", 300, 69, null,
            Block([Lines("a0", "a1", "a2", "a3").Orphans(1).Widows(1), Paragraph("x0"), Paragraph("y0")]).KeepWithNext(), Paragraph("q0"));
    }

    [Fact]
    public void A_keep_rule_inside_a_box_at_a_page_end_splits_the_paragraph_before_it()
    {
        // The box breaks before d1 (against orphans and widows 2) rather than before e (against
        // the keep), and keeps that break, although a perfect break before the box exists.
        ShouldPrintLikeChromium("fr-keep-inside-box", 300, 114, 18,
            Paragraph("a0"), Paragraph("b0"), Paragraph("c0"), Block([Lines("d0", "d1").KeepWithNext(), Lines("e0", "e1")]));
    }
}
