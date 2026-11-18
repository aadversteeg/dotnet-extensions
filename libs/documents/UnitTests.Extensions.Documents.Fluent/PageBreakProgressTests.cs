using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Fluent;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.Functional;
using static Ave.Extensions.Documents.Fluent.Elements;

namespace UnitTests.Extensions.Documents.Fluent;

/// <summary>
/// Documents that made <c>DocumentLayout.Run</c> throw "a layout algorithm made no progress" (review 40, blocker B2), each
/// with the outcome it has now. A margin after a forced break takes space on the page right after the break only
/// (CSS Fragmentation 3 §5.2; Chromium truncates it after the unforced break that follows), and a box that continues
/// under a table's repeated header with no room for its content has no room there, instead of a break that leaves
/// nothing behind. Against Chromium's print where the engine's rules give its pages (cases <c>fr-*</c> of
/// <c>tools/reference/chromium</c>).
/// </summary>
public class PageBreakProgressTests
{
    private static Result<PrintedPages, Ave.Extensions.ErrorPaths.Error> Print(double width, double height, params Element[] body) =>
        DocumentTranslator.ToModel(Document([.. body], Page(width.Pt(), height.Pt()).Margin(12.Pt()), title: "Title", language: "en").FontFamily("Lato"))
            .OnSuccessBind(model => DocumentLayout.Run(model, TestFonts.SetWithItalic))
            .OnSuccessMap(PrintedPages.Engine);

    private static void ShouldPrintLikeChromium(string caseName, double width, double height, params Element[] body)
    {
        var engine = Print(width, height, body);

        engine.IsSuccess.Should().BeTrue(engine.IsFailure ? engine.Error.ToString() : string.Empty);
        engine.Value.PageTexts.Should().Equal(PrintedPages.Chromium(caseName).PageTexts, $"the words of each page as Chromium prints {caseName}");
    }

    [Fact]
    public void A_margin_taller_than_the_page_after_a_forced_break_leaves_one_blank_page()
    {
        // Review case m49 (page area 36 pt): the margin is kept after the forced break and leaves no room for b0, the page
        // ends before b0 as a last resort, and on the next page the margin is truncated.
        ShouldPrintLikeChromium("fr-forced-margin", 300, 60, Paragraph("a0"), PageBreak(), Paragraph("b0").MarginTop(37.5.Pt()));
    }

    [Fact]
    public void The_same_on_an_A4_page()
    {
        // Review case m50: page area 818 pt, margin 830 pt.
        ShouldPrintLikeChromium("fr-forced-margin-a4", 595, 842, Paragraph("a0"), PageBreak(), Paragraph("b0").MarginTop(830.Pt()));
    }

    [Fact]
    public void A_table_whose_margin_after_a_forced_break_leaves_no_room_for_it_starts_the_page_after()
    {
        // Review case m46 (page area 150 pt).
        ShouldPrintLikeChromium("fr-forced-table", 300, 174, Paragraph("i"), PageBreak(), Table([Fill()], [TableRow([Cell("w")])]).MarginTop(140.Pt()), Paragraph("z"));
    }

    [Fact]
    public void A_padded_block_after_a_forced_break_keeps_its_line_and_moves_its_bottom_padding()
    {
        // Review case m47: below its 12 pt margin the block's line fits, its 130 pt bottom padding does not, and the block
        // has no breakpoint inside: it breaks before its bottom padding, which starts the next page with z below it.
        ShouldPrintLikeChromium("fr-forced-padded", 300, 174, Paragraph("i"), PageBreak(), Block([Paragraph("w")]).PaddingBottom(130.Pt()).MarginTop(12.Pt()), Paragraph("z"));
    }

    [Theory]
    [InlineData(2.5, false)]
    [InlineData(null, false)]
    [InlineData(null, true)]
    public void A_cell_under_a_repeated_header_whose_content_never_fits_below_it_is_refused(double? lineHeight, bool inBlock)
    {
        // Review cases m34 (font size 24) and m21 (line height 2.5): the cell's paragraph has a 40 pt top margin and a
        // border; it never fits under the header, and without the header its 12 pt bottom margin, which stays inside the
        // cell, does not fit. A clean refusal: the limit "a cell whose content and bottom margin are taller than the page"
        // in docs/architecture.md (Chromium prints it, case fr-header-cell-margin).
        var paragraph = Paragraph("w").MarginTop(40.Pt()).MarginBottom(12.Pt()).Border(1.5.Pt(), Color.Black);
        paragraph = lineHeight is { } factor ? paragraph.LineHeight(factor) : paragraph.FontSize(24.Pt());

        // In a block, the block continues around the continued paragraph, and it has no room either.
        Element content = inBlock ? Block([paragraph]) : paragraph;

        var result = Print(300, 60, Table([Fill()], [HeaderRow([Cell("H")]), TableRow([Cell([content])])]));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(LayoutErrorCodes.DoesNotFit);
    }

    /// <summary>A table with a header row "H" and one body cell, then "AFTER", on a page 300 pt wide with 18 pt lines,
    /// <c>orphans</c> and <c>widows</c> 1 (cases <c>fr-header-pad-*</c>): the pages, separated by <c> | </c>, or the error
    /// code.</summary>
    private static string UnderHeader(double height, Element cell, double gridLines = 0)
    {
        var table = Table([Fill()], [HeaderRow([Cell("H")]), TableRow([Cell([cell])])]).GridLines(gridLines.Pt(), Color.Black).SplitLinesAtBreaks().Orphans(1).Widows(1);
        var document = Document([table, Paragraph("AFTER").Orphans(1).Widows(1)], Page(300.Pt(), height.Pt()).Margin(12.Pt()), title: "Title", language: "en")
            .FontFamily("Lato").FontSize(12.Pt()).LineHeight(18.Pt());
        var result = DocumentTranslator.ToModel(document).OnSuccessBind(model => DocumentLayout.Run(model, TestFonts.SetWithItalic)).OnSuccessMap(PrintedPages.Engine);
        return result.IsFailure ? result.Error.Code.ToString() : string.Join(" | ", result.Value.PageTexts);
    }

    private static BlockElement Bordered(params Element[] content) => Block([.. content]).Border(1.5.Pt(), Color.Black);

    private static string Chromium(string caseName) => string.Join(" | ", PrintedPages.Chromium(caseName).PageTexts);

    [Fact]
    public void A_cell_whose_box_has_bottom_padding_as_tall_as_the_page_area_under_a_repeated_header_is_printed()
    {
        // The smallest such document: the header row, then a cell with a bordered box around "a", whose bottom padding is
        // the page area's height (36 pt). The box went on to the next page with only that padding left; under the
        // repeated header it had no room, and the box broke before its first child as a last resort, which resumed where
        // it resumed: the table returned the token it was given, and the layout ended in Documents.Layout.EngineDefect.
        // A box that continues has no top border or padding on that page, so such a break leaves nothing of it there: it
        // has no room instead, and the page goes on without the header (TablePage). Chromium prints H | a | (blank) |
        // AFTER (case fr-header-pad-box-36): it does not repeat a header taller than a quarter of the page area, and it
        // does not leave a page with only the header and the box's top border (both listed in docs/architecture.md).
        UnderHeader(60, Bordered(Paragraph("a").PaddingBottom(36.Pt()))).Should().Be("H | H | H a |  | H | AFTER");
        Chromium("fr-header-pad-box-36").Should().Be("H | a |  | AFTER");
    }

    [Theory]
    [InlineData("top padding", "H | H |  | a | AFTER", "H |  |  | a | AFTER")]
    [InlineData("grid lines", "H | H | a |  | H | AFTER", "H | a |  | AFTER")]
    [InlineData("inner box", "H | H | H a |  | H | AFTER", null)]
    public void The_same_with_top_padding_with_grid_lines_and_on_an_inner_box_is_printed(string shape, string engine, string? chromium)
    {
        // Top padding as tall as the page area (fr-header-pad-top-36; Chromium's pages differ by the header rule again),
        // the same box in a table with grid lines (fr-header-pad-grid-36), and the padding on a box inside the bordered
        // one (not measured).
        var (cell, gridLines, caseName) = shape switch
        {
            "top padding" => (Bordered(Paragraph("a").PaddingTop(36.Pt())), 0.0, "fr-header-pad-top-36"),
            "grid lines" => (Bordered(Paragraph("a").PaddingBottom(36.Pt())), 0.75, "fr-header-pad-grid-36"),
            _ => (Bordered(Block([Paragraph("a")]).PaddingBottom(36.Pt())), 0.0, null),
        };

        UnderHeader(60, cell, gridLines).Should().Be(engine);
        if (caseName is not null)
        {
            Chromium(caseName).Should().Be(chromium);
        }
    }

    [Fact]
    public void The_same_on_a_page_where_Chromium_repeats_the_header_is_printed()
    {
        // Page area 72 pt: the 18 pt header is a quarter of it, so Chromium repeats it as well (fr-header-pad-box-72,
        // H a | H | H AFTER). The engine leaves the header alone on page 1, where Chromium cuts the row below it (a listed
        // difference in docs/architecture.md).
        UnderHeader(96, Bordered(Paragraph("a").PaddingBottom(72.Pt()))).Should().Be("H | H a |  | H AFTER");
        Chromium("fr-header-pad-box-72").Should().Be("H a | H | H AFTER");
    }

    [Fact]
    public void Padding_taller_than_the_page_area_under_a_repeated_header_with_content_after_it_is_refused()
    {
        // 50 pt of bottom padding on a page area of 36 pt, with AFTER after the table: the listed limit for bottom padding
        // taller than the page area with content after it (docs/architecture.md); Chromium prints it (fr-header-pad-box-50).
        UnderHeader(60, Bordered(Paragraph("a").PaddingBottom(50.Pt()))).Should().Be(LayoutErrorCodes.DoesNotFit.ToString());
        Chromium("fr-header-pad-box-50").Should().Be("H | a |  | AFTER");
    }
}
