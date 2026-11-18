using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Fluent;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.Functional;
using static Ave.Extensions.Documents.Fluent.Elements;

namespace UnitTests.Extensions.Documents.Fluent;

/// <summary>
/// Where a page starts, against Chromium's print of the same content (<c>tools/reference/chromium</c>, cases
/// <c>a1-*</c>; README there). A box that does not fit below margins, empty boxes or padding moves to the next page,
/// where the margins above it are truncated (CSS Fragmentation 3 §5.2); only content that starts the page and does not
/// fit is refused. The page area is 276 × 150 pt, as in the cases (<c>@page { size: 300pt 174pt; margin: 12pt }</c>:
/// whole pixels, which Chromium keeps exact). Which words are on which page is compared exactly. Chromium's line is
/// 14.25 pt where the engine's is 14.4 (README there, "Print positions"), so the cases keep every decision clear of
/// that difference, and a position is compared with the same side's own reference: the first line of a page that has
/// nothing above it.
/// </summary>
public class PageStartTests
{
    private static PageSetup Area => Page(300.Pt(), 174.Pt()).Margin(12.Pt());

    private static Result<PrintedPages, global::Ave.Extensions.ErrorPaths.Error> Print(params Element[] body) =>
        DocumentTranslator.ToModel(Document([.. body], Area, title: "Title", language: "en").FontFamily("Lato"))
            .OnSuccessBind(model => DocumentLayout.Run(model, TestFonts.SetWithItalic))
            .OnSuccessMap(PrintedPages.Engine);

    private static TableElement OneRow() => Table(Columns(Fill(1)), [TableRow([Cell("row")])]);

    private static readonly Color Grey = new(0xCC, 0xCC, 0xCC);

    /// <summary>The engine puts the words on the pages Chromium puts them on; returns both prints.</summary>
    private static (PrintedPages Engine, PrintedPages Chromium) ShouldPrintLikeChromium(string caseName, params Element[] body)
    {
        var engine = Print(body);
        engine.IsSuccess.Should().BeTrue(engine.IsFailure ? engine.Error.ToString() : string.Empty);
        var chromium = PrintedPages.Chromium(caseName);
        engine.Value.PageTexts.Should().Equal(chromium.PageTexts, $"the words of each page as Chromium prints {caseName}");
        return (engine.Value, chromium);
    }

    /// <summary>
    /// The first line of <paramref name="page"/> has nothing above it: in both prints its baseline is where a page's
    /// first line is when nothing is above it (the first line of case <c>a1-text-before</c>, in the same font), so the
    /// margins before the break were truncated.
    /// </summary>
    private static void ShouldStartAtTheTopOfThePage((PrintedPages Engine, PrintedPages Chromium) prints, int page)
    {
        var engineTop = Print(Paragraph("intro")).Value.Pages[0].Words[0].Baseline;
        var chromiumTop = PrintedPages.Chromium("a1-text-before").Pages[0].Words[0].Baseline;
        prints.Engine.Pages[page - 1].Words[0].Baseline.Should().BeApproximately(engineTop, 1e-9);
        prints.Chromium.Pages[page - 1].Words[0].Baseline.Should().Be(chromiumTop);
    }

    /// <summary>The filled rectangles of a page from top to bottom, as (top, bottom), compared within n layout units.</summary>
    private static void ShouldFillLikeChromium((PrintedPages Engine, PrintedPages Chromium) prints, int page, int n, string lengths)
    {
        var engine = prints.Engine.Pages[page - 1].Rectangles;
        var chromium = prints.Chromium.Pages[page - 1].Rectangles;
        engine.Should().HaveSameCount(chromium);
        foreach (var (ours, theirs) in engine.Zip(chromium))
        {
            ChromiumLayout.DifferenceInLayoutUnits(ours.Y0, theirs.Y0).Should().BeLessThanOrEqualTo(n + ChromiumLayout.FloatSlack, $"top of {ours} ({lengths})");
            ChromiumLayout.DifferenceInLayoutUnits(ours.Y1, theirs.Y1).Should().BeLessThanOrEqualTo(n + ChromiumLayout.FloatSlack, $"bottom of {ours} ({lengths})");
        }
    }

    [Fact]
    public void A_table_after_an_empty_block_whose_margin_leaves_too_little_room_moves_to_the_next_page() =>
        ShouldStartAtTheTopOfThePage(ShouldPrintLikeChromium("a1-empty-block-then-table", Block([]).MarginBottom(140.Pt()), OneRow()), 2);

    [Fact]
    public void A_table_whose_own_top_margin_leaves_too_little_room_moves_to_the_next_page() =>
        ShouldStartAtTheTopOfThePage(ShouldPrintLikeChromium("a1-table-own-margin", OneRow().MarginTop(140.Pt())), 2);

    [Fact]
    public void A_paragraph_after_an_empty_block_whose_margin_leaves_too_little_room_moves_to_the_next_page() =>
        ShouldStartAtTheTopOfThePage(ShouldPrintLikeChromium("a1-empty-block-then-paragraph", Block([]).MarginBottom(140.Pt()), Paragraph("para")), 2);

    [Fact]
    public void A_paragraph_whose_own_top_margin_leaves_too_little_room_moves_to_the_next_page()
    {
        ShouldStartAtTheTopOfThePage(ShouldPrintLikeChromium("a1-paragraph-own-margin", Paragraph("para").MarginTop(140.Pt())), 2);

        // With room for its line it is laid out under its margin, on the first page.
        ShouldPrintLikeChromium("a1-paragraph-own-margin-fits", Paragraph("para").MarginTop(130.Pt()));
    }

    [Fact]
    public void A_top_margin_taller_than_the_page_area_is_truncated_on_the_next_page() =>
        ShouldStartAtTheTopOfThePage(ShouldPrintLikeChromium("a1-margin-taller-than-page", Paragraph("para").MarginTop(200.Pt())), 2);

    [Fact]
    public void After_a_forced_page_break_the_margin_stays_and_a_table_that_does_not_fit_below_it_moves_on() =>
        // The forced break keeps the margin after it (§5.2: "margins after the break are preserved"), so the table does
        // not fit on page 2; the break before it is unforced, so on page 3 that margin is gone.
        ShouldStartAtTheTopOfThePage(
            ShouldPrintLikeChromium("a1-after-forced-break", Paragraph("first"), PageBreak(), Block([]).MarginBottom(140.Pt()), OneRow()), 3);

    [Fact]
    public void A_forced_page_break_keeps_the_margin_after_it_and_one_with_only_margins_before_it_has_no_effect()
    {
        var kept = ShouldPrintLikeChromium("a1-forced-break-keeps-margin", Paragraph("a"), PageBreak(), Block([]).MarginTop(20.Pt()), Paragraph("b"));
        var ignored = ShouldPrintLikeChromium("a1-forced-break-without-content", Block([]).MarginBottom(20.Pt()), PageBreak(), Block([]), Paragraph("b"));

        // In both, "b" lies 20 pt under the top of its page.
        var engineTop = Print(Paragraph("intro")).Value.Pages[0].Words[0].Baseline;
        kept.Engine.Pages[1].Words[0].Baseline.Should().BeApproximately(engineTop + 20, 1e-9);
        ignored.Engine.Pages[0].Words[0].Baseline.Should().BeApproximately(engineTop + 20, 1e-9);
        kept.Chromium.Pages[1].Words[0].Baseline.Should().Be(ignored.Chromium.Pages[0].Words[0].Baseline);
    }

    [Fact]
    public void A_first_row_kept_together_after_an_empty_block_moves_to_the_next_page_whole()
    {
        // The break before the table, after the empty block, is perfect; the cut inside the kept row violates its keep.
        TableElement Table9() => Table(Columns(Fill(1)), [TableRow([Cell([.. Enumerable.Range(0, 9).Select(i => (Element)Paragraph($"l{i}"))])]).KeepTogether(), TableRow([Cell("next")])]);

        ShouldPrintLikeChromium("a1-kept-row", Block([]).MarginBottom(40.Pt()), Table9());
    }

    [Fact]
    public void A_header_row_after_an_empty_block_does_not_stand_alone_at_the_foot_of_the_page()
    {
        TableElement Headed() => Table(Columns(Fill(1)), [HeaderRow([Cell("head")]), TableRow([Cell("r0")]), TableRow([Cell("r1")])]);

        ShouldPrintLikeChromium("a1-header-row", Block([]).MarginBottom(125.Pt()), Headed());
    }

    [Fact]
    public void With_text_before_it_the_table_moves_to_the_next_page() =>
        ShouldPrintLikeChromium("a1-text-before", Paragraph("intro").MarginBottom(124.Pt()), OneRow());

    [Fact]
    public void A_first_child_whose_margin_collapses_through_its_box_breaks_inside_the_box_as_a_last_resort()
    {
        // The paragraph's margin collapses through the grey block (no padding or border between them), so the block
        // starts 138 pt down and the line does not fit under it. There is no breakpoint before the line (it is the first
        // child, nothing but its margin separates it from the block's top) nor before the block (the first child of the
        // page), so the block breaks before the line as a last resort: page 1 shows the block's background from 138 pt to
        // the page end, page 2 the line at the top.
        var prints = ShouldPrintLikeChromium("a1-margin-through-box", Block([Paragraph("line").MarginTop(138.Pt())]).Background(Grey));

        ShouldStartAtTheTopOfThePage(prints, 2);
        // 138 pt is 184 px, the page area's end 150 pt is 200 px: whole pixels, so Chromium's numbers are exact (n = 0).
        ShouldFillLikeChromium(prints, 1, 0, "a margin of whole pixels and the page area's end");
    }

    [Fact]
    public void A_box_at_the_top_of_a_page_whose_first_line_does_not_fit_under_its_padding_leaves_the_padding_on_that_page()
    {
        var prints = ShouldPrintLikeChromium("a1-padding-at-page-start", Paragraph("W").FontSize(118.Pt()).PaddingTop(20.Pt()).Background(Grey));

        // Page 1 holds the paragraph's background from the top of the page area to its end (the padding, and the rest of
        // the page a broken box fills, §5.3): both are the page area's edges, whole pixels (n = 0).
        ShouldFillLikeChromium(prints, 1, 0, "the page area's edges");
    }

    [Fact]
    public void A_box_lower_on_the_page_whose_first_line_does_not_fit_under_its_padding_moves_whole()
    {
        var prints = ShouldPrintLikeChromium("a1-padding-lower", Paragraph("intro"), Block([Paragraph("W").FontSize(118.Pt())]).PaddingTop(3.Pt()).Background(Grey));

        // Nothing of the block is on page 1: the break before it is better than the last resort inside it.
        prints.Engine.Pages[0].Rectangles.Should().BeEmpty();
        prints.Chromium.Pages[0].Rectangles.Should().BeEmpty();
    }

    [Fact]
    public void A_line_taller_than_the_page_area_at_the_top_of_a_page_is_refused()
    {
        // Content that cannot be broken and starts the page: it does not fit on any page.
        var result = Print(Paragraph("W").FontSize(200.Pt()));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(LayoutErrorCodes.DoesNotFit);
        result.Error.Message.Should().StartWith("The paragraph made at PageStartTests.cs:")
            .And.EndWith(" does not fit in the 150pt left for it on the page and cannot be broken; nothing before it on the page can move to a later page.");
    }
}
