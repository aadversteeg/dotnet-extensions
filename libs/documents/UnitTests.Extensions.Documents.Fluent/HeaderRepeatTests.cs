using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Fluent;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.Functional;
using static Ave.Extensions.Documents.Fluent.Elements;

namespace UnitTests.Extensions.Documents.Fluent;

/// <summary>
/// A header row is repeated on a later page only when it is at most a quarter of the page area, as Chromium prints
/// (<c>tools/reference/chromium</c>, cases <c>a6-header-quarter-*</c>): a header of one line with top padding above 60
/// one-line body rows, at two page areas (180 and 360 pt) and on both sides of the step there (45.0 and 45.75 pt; 90.0
/// and 90.75 pt). Page width 300 pt, margin 12 pt, Lato 12 pt, line height 15 pt, 0.75 pt grid lines. Also the header
/// cases <c>fr-old-header-quarter-*</c> (page areas of 150, 180 and 240 pt) and <c>fr-header-pad-box-20</c>.
/// </summary>
public class HeaderRepeatTests
{
    private static PrintedPages Printed(double area, double padding, MonolithicOverflow overflow)
    {
        TablePart[] rows = [HeaderRow([Cell([Paragraph("H0")]).PaddingTop(padding.Pt())]), .. Enumerable.Range(0, 60).Select(i => (TablePart)TableRow([Cell([Paragraph($"r{i}l0")])]))];
        var table = Table(Columns(Fill(1)), [.. rows]).GridLines(0.75.Pt(), Color.Black).SplitLinesAtBreaks();
        var document = Document([table], Page(300.Pt(), (area + 24).Pt()).Margin(12.Pt()), title: "Title", language: "en")
            .FontFamily("Lato").LineHeight(15.Pt()).MonolithicOverflow(overflow).FontMetricRounding(FontMetricRounding.WholePixels);
        var result = DocumentTranslator.ToModel(document).OnSuccessBind(model => DocumentLayout.Run(model, TestFonts.SetWithItalic)).OnSuccessMap(PrintedPages.Engine);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        return result.Value;
    }

    [Theory]
    [InlineData("a6-header-quarter-180-4500", 180, 29.25, 8, true)]
    [InlineData("a6-header-quarter-180-4575", 180, 30, 6, false)]
    [InlineData("a6-header-quarter-360-9000", 360, 74.25, 4, true)]
    [InlineData("a6-header-quarter-360-9075", 360, 75, 3, false)]
    public void A_header_row_is_repeated_only_when_it_is_at_most_a_quarter_of_the_page_area_as_in_Chromium(string caseName, double area, double padding, int pages, bool repeated)
    {
        var chromium = PrintedPages.Chromium(caseName);

        foreach (var overflow in new[] { MonolithicOverflow.Refuse, MonolithicOverflow.Slice })
        {
            var engine = Printed(area, padding, overflow);

            engine.PageTexts.Should().Equal(chromium.PageTexts, $"{caseName}, {overflow}");
            engine.Pages.Should().HaveCount(pages);
            engine.Pages.Skip(1).Select(page => page.Words.Any(word => word.Text == "H0")).Should().AllBeEquivalentTo(repeated);
        }
    }

    [Theory]
    [InlineData(150, 18.75, "at")]
    [InlineData(150, 19.5, "over")]
    [InlineData(180, 26.25, "at")]
    [InlineData(180, 27, "over")]
    [InlineData(240, 41.25, "at")]
    [InlineData(240, 42, "over")]
    public void A_header_with_bottom_padding_at_and_over_a_quarter_of_the_page_area_is_repeated_as_in_Chromium(int area, double padding, string side)
    {
        // Cases fr-old-header-quarter-*: a header row "h" with bottom padding above 14 one-line rows, 18 pt lines, on page
        // areas of 150, 180 and 240 pt; "at" is a header exactly a quarter of the page area high, "over" one 0.75 pt more.
        var table = Table([Fill()], [HeaderRow([Cell([Paragraph("h")]).PaddingBottom(padding.Pt())]), .. Enumerable.Range(0, 14).Select(row => (TablePart)TableRow([Cell([Paragraph($"r{row}")])]))])
            .GridLines(0.75.Pt(), Color.Black).SplitLinesAtBreaks().Orphans(2).Widows(2);
        var document = Document([table], Page(300.Pt(), (area + 24).Pt()).Margin(12.Pt()), title: "Title", language: "en").FontFamily("Lato").FontSize(12.Pt()).LineHeight(18.Pt());
        var engine = DocumentTranslator.ToModel(document).OnSuccessBind(model => DocumentLayout.Run(model, TestFonts.SetWithItalic)).OnSuccessMap(PrintedPages.Engine);
        var chromium = PrintedPages.Chromium($"fr-old-header-quarter-{area}-{side}");

        engine.IsSuccess.Should().BeTrue();
        engine.Value.PageTexts.Should().Equal(chromium.PageTexts);
        chromium.PageTexts.Skip(1).Should().OnlyContain(page => page.StartsWith("h ", StringComparison.Ordinal) == (side == "at"), "Chromium repeats it only up to a quarter of the page area");
    }

    [Fact]
    public void Under_a_header_over_a_quarter_of_the_page_area_a_cell_whose_box_s_bottom_padding_moves_on_prints_Chromium_s_pages()
    {
        // A cell with a bordered box around "a", whose 20 pt bottom padding does not fit below it (page area 36 pt), under
        // an 18 pt header: more than a quarter of the page area, so the header is not repeated, and page 2 holds "a" (case
        // fr-header-pad-box-20). Without the header row both print a | (blank) | AFTER (fr-header-pad-none-36).
        static string Engine(TableElement table)
        {
            var document = Document([table.Orphans(1).Widows(1), Paragraph("AFTER").Orphans(1).Widows(1)], Page(300.Pt(), 60.Pt()).Margin(12.Pt()), title: "Title", language: "en")
                .FontFamily("Lato").FontSize(12.Pt()).LineHeight(18.Pt());
            var result = DocumentTranslator.ToModel(document).OnSuccessBind(model => DocumentLayout.Run(model, TestFonts.SetWithItalic)).OnSuccessMap(PrintedPages.Engine);
            result.IsSuccess.Should().BeTrue();
            return string.Join(" | ", result.Value.PageTexts);
        }

        static string Chromium(string caseName) => string.Join(" | ", PrintedPages.Chromium(caseName).PageTexts);

        var table = Table([Fill()], [HeaderRow([Cell("H")]), TableRow([Cell([Block([Paragraph("a").PaddingBottom(20.Pt())]).Border(1.5.Pt(), Color.Black)])])])
            .GridLines(0.Pt(), Color.Black).SplitLinesAtBreaks();
        Engine(table).Should().Be(Chromium("fr-header-pad-box-20")).And.Be("H | a |  | AFTER");
        var alone = Table([Fill()], [TableRow([Cell([Block([Paragraph("a").PaddingBottom(36.Pt())]).Border(1.5.Pt(), Color.Black)])])]).GridLines(0.Pt(), Color.Black).SplitLinesAtBreaks();
        Engine(alone).Should().Be(Chromium("fr-header-pad-none-36")).And.Be("a |  | AFTER");
    }

    /// <summary>A table of a header row "H" with top padding above <paramref name="rows"/> one-line rows (30 unless given),
    /// inside the one cell of an outer table whose cell has <paramref name="cellPadding"/> or which has a header row "O" of
    /// its own (cases <c>a6-nested-header-*</c>, <c>a6-nested-under-header-*</c>).</summary>
    private static PrintedPages Nested(
        double padding, double cellPadding, bool outerHeader, int rows = 30, MonolithicOverflow overflow = MonolithicOverflow.Refuse, EmptyRowParts parts = EmptyRowParts.Avoid)
    {
        TableElement Grid(TablePart[] rows) => Table(Columns(Fill(1)), [.. rows]).GridLines(0.75.Pt(), Color.Black).SplitLinesAtBreaks();
        var inner = Grid([HeaderRow([Cell([Paragraph("H")]).PaddingTop(padding.Pt())]), .. Enumerable.Range(0, rows).Select(i => (TablePart)TableRow([Cell([Paragraph($"r{i}")])]))]);
        TablePart body = TableRow([Cell([inner])]);
        var outer = outerHeader ? Grid([HeaderRow([Cell([Paragraph("O")])]), body]) : Grid([body]).CellPadding(cellPadding.Pt());
        var document = Document([outer], Page(300.Pt(), 204.Pt()).Margin(12.Pt()), title: "Title", language: "en")
            .FontFamily("Lato").LineHeight(15.Pt()).FontMetricRounding(FontMetricRounding.WholePixels).MonolithicOverflow(overflow).EmptyRowParts(parts);
        var result = DocumentTranslator.ToModel(document).OnSuccessBind(model => DocumentLayout.Run(model, TestFonts.SetWithItalic)).OnSuccessMap(PrintedPages.Engine);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        return result.Value;
    }

    [Fact]
    public void A_header_of_a_quarter_of_the_page_area_in_a_table_inside_a_padded_cell_is_repeated_as_in_Chromium()
    {
        // The header is 45.0 pt, a quarter of the 180 pt page area, in a cell with 12 pt padding (case
        // a6-nested-header-pad12-h2925). A cell that continues on a later page gives its content the page's whole height
        // there, so the inner table's room is the page area, and the header is repeated on every page, as in Chromium.
        var engine = Nested(29.25, 12, outerHeader: false);
        var chromium = PrintedPages.Chromium("a6-nested-header-pad12-h2925");

        engine.PageTexts.Should().Equal(chromium.PageTexts);
        engine.Pages.Should().AllSatisfy(page => page.Words[0].Text.Should().Be("H"));
    }

    [Theory]
    [InlineData("a6-nested-under-header-h2400", 24, 30, true)]
    [InlineData("a6-nested-under-header-h2700", 27, 30, false)]
    [InlineData("a6-nested-under-header-h2700-n16", 27, 16, false)]
    public void Under_an_outer_repeated_header_the_inner_header_is_repeated_on_every_later_page_or_on_none_as_in_Chromium(string caseName, double padding, int rows, bool repeated)
    {
        // The inner table in the body cell of a table with a header row "O" of its own. Chromium reserves the outer
        // header's room in the page area for the rows below it, so the inner table compares its header with a quarter of
        // the page area less "O": its header of 39.75 pt is repeated on every later page, one of 42.75 pt on none. The
        // table decides on its first page, and the rest of the outer cell, laid out with unlimited height on the last
        // page, keeps that decision.
        var chromium = PrintedPages.Chromium(caseName);
        bool[] Repeats(PrintedPages pages) => [.. pages.Pages.Skip(1).Select(page => page.Words.Any(word => word.Text == "H"))];

        foreach (var overflow in Enum.GetValues<MonolithicOverflow>())
        {
            foreach (var parts in Enum.GetValues<EmptyRowParts>())
            {
                var engine = Nested(padding, 0, outerHeader: true, rows, overflow, parts);

                engine.PageTexts.Should().Equal(chromium.PageTexts, $"{caseName}, {overflow}/{parts}");
                engine.Pages.Should().AllSatisfy(page => page.Words[0].Text.Should().Be("O"));
            }
        }

        Repeats(chromium).Should().AllBeEquivalentTo(repeated);
    }

    [Theory]
    [InlineData("a6-nested-header-h4425-n14", 14, "r7 r8 r9 r10 r11 r12 r13")]
    [InlineData("a6-nested-header-h4425-n16", 16, "r7 r8 r9 r10 r11 r12 r13 r14 r15")]
    [InlineData("a6-nested-header-h4425-n20", 20, "r18 r19")]
    [InlineData("a6-nested-header-h4425-n26", 26, "r18 r19 r20 r21 r22 r23 r24 r25")]
    public void A_nested_header_over_a_quarter_of_the_page_area_is_shown_on_its_table_s_first_page_only_as_in_Chromium(string caseName, int rows, string lastPage)
    {
        // A header of 60 pt (44.25 pt top padding), a third of the 180 pt page area, in a table inside the one cell of an
        // outer table: one row count for each kind of last page. A page without the header holds 11 rows, a page with it 7.
        // The last page, where the rest of the outer cell is laid out with unlimited height, holds 7 rows (as many as fit
        // below the header), 9 (more than fit below it), 2, or 8 (more than fit below it again). The table decided on its
        // first page not to repeat the header, and the measurement keeps that decision, so no later page shows it.
        var chromium = PrintedPages.Chromium(caseName);

        foreach (var overflow in Enum.GetValues<MonolithicOverflow>())
        {
            foreach (var parts in Enum.GetValues<EmptyRowParts>())
            {
                var engine = Nested(44.25, 0, outerHeader: false, rows, overflow, parts);

                engine.PageTexts.Should().Equal(chromium.PageTexts, $"{caseName}, {overflow}/{parts}");
            }
        }

        chromium.PageTexts[0].Should().Be("H r0 r1 r2 r3 r4 r5 r6");
        chromium.PageTexts.Skip(1).Should().OnlyContain(page => !page.StartsWith("H ", StringComparison.Ordinal));
        chromium.PageTexts[^1].Should().Be(lastPage);
    }

    /// <summary>A table of a header row "H" with top padding above 20 one-line rows, in the last body row of an outer table
    /// with a header row "O" and <paramref name="rowsBefore"/> one-line rows "a" before it, so that the inner table starts
    /// on the outer table's second page (on its first when there are none). With <paramref name="middle"/> a table with a
    /// header row "M" and the rows before it, "b", lies between the two. Grid lines of <paramref name="line"/> pt (cases
    /// <c>a6-nested-late-*</c>, <c>a6-nested-late6-*</c>, <c>a6-nested-first-*</c>); with <paramref name="clone"/> the
    /// outer table repeats its grid lines at a break (<c>box-decoration-break: clone</c>).</summary>
    private static PrintedPages Late(
        double padding, int rowsBefore, double line, bool middle, bool clone, MonolithicOverflow overflow, EmptyRowParts parts)
    {
        TableElement Grid(TablePart[] rows) => Table(Columns(Fill(1)), [.. rows]).GridLines(line.Pt(), Color.Black).SplitLinesAtBreaks();
        TablePart[] Rows(string prefix, int count) => [.. Enumerable.Range(0, count).Select(i => (TablePart)TableRow([Cell([Paragraph($"{prefix}{i}")])]))];
        var inner = Grid([HeaderRow([Cell([Paragraph("H")]).PaddingTop(padding.Pt())]), .. Rows("r", 20)]);
        var outer = middle
            ? Grid([HeaderRow([Cell([Paragraph("O")])]), TableRow([Cell([Grid([HeaderRow([Cell([Paragraph("M")])]), .. Rows("b", rowsBefore), TableRow([Cell([inner])])])])])])
            : Grid([HeaderRow([Cell([Paragraph("O")])]), .. Rows("a", rowsBefore), TableRow([Cell([inner])])]);
        var document = Document([clone ? outer.RepeatLinesAtBreaks() : outer], Page(300.Pt(), 204.Pt()).Margin(12.Pt()), title: "Title", language: "en")
            .FontFamily("Lato").LineHeight(15.Pt()).FontMetricRounding(FontMetricRounding.WholePixels).MonolithicOverflow(overflow).EmptyRowParts(parts);
        var result = DocumentTranslator.ToModel(document).OnSuccessBind(model => DocumentLayout.Run(model, TestFonts.SetWithItalic)).OnSuccessMap(PrintedPages.Engine);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        return result.Value;
    }

    [Theory]
    [InlineData("a6-nested-late-h2521875", 25.21875, 14, 0.75, false, false, true)]
    [InlineData("a6-nested-late-h2525", 25.25, 14, 0.75, false, false, false)]
    [InlineData("a6-nested-late-h253125", 25.3125, 14, 0.75, false, false, false)]
    [InlineData("a6-nested-late-h2700", 27, 14, 0.75, false, false, false)]
    [InlineData("a6-nested-late6-h1800", 18, 10, 6, false, false, true)]
    [InlineData("a6-nested-late6-h1850", 18.5, 10, 6, false, false, false)]
    [InlineData("a6-nested-late-d3-h2100", 21, 12, 0.75, true, false, true)]
    [InlineData("a6-nested-late-d3-h2125", 21.25, 12, 0.75, true, false, false)]
    [InlineData("a6-nested-late-clone-h2525", 25.25, 14, 0.75, false, true, false)]
    [InlineData("a6-nested-first-h2525", 25.25, 0, 0.75, false, false, true)]
    [InlineData("a6-nested-first-h253125", 25.3125, 0, 0.75, false, false, true)]
    public void A_nested_table_under_an_outer_repeated_header_has_the_room_Chromium_reserves_on_that_page_for_the_header(
        string caseName, double padding, int rowsBefore, double line, bool middle, bool clone, bool repeated)
    {
        // The nested header compares with a quarter of the page area less the room the outer headers take. On the outer
        // table's first page that is the header; on a later page the repeated header also draws the outer half of the
        // table's top grid line. Below "O" of 15.75 pt on 180 pt the limit is 41.0625 pt on the first page and 40.96875 pt
        // on the second: a header of 41.0 pt (top padding 25.25 pt) is repeated in a table that starts on the first page
        // and not in one that starts on the second. With 6 pt lines the second-page limit is 39.0 pt, with a middle table
        // of a one-line header "M" between the two it is 36.9375 pt. An outer table that clones its lines at a break
        // reserves the same.
        var chromium = PrintedPages.Chromium(caseName);
        foreach (var overflow in Enum.GetValues<MonolithicOverflow>())
        {
            foreach (var parts in Enum.GetValues<EmptyRowParts>())
            {
                Late(padding, rowsBefore, line, middle, clone, overflow, parts).PageTexts.Should().Equal(chromium.PageTexts, $"{caseName}, {overflow}/{parts}");
            }
        }

        chromium.PageTexts.Skip(2).Select(page => page.Contains(" H ", StringComparison.Ordinal)).Should().AllBeEquivalentTo(repeated);
    }
}
