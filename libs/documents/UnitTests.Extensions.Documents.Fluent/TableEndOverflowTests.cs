using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Fluent;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.Functional;
using static Ave.Extensions.Documents.Fluent.Elements;

namespace UnitTests.Extensions.Documents.Fluent;

/// <summary>
/// What passes the page end below a table's last row, where that row is placed whole at the top of a page under
/// <see cref="EmptyRowParts.Allow"/> (<c>TablePage.PassesOnlyByItsLineBelow</c>), under both values of
/// <see cref="TableEndOverflow"/>: under <see cref="TableEndOverflow.ContinuesOnNextPage"/> as Chromium prints it
/// (<c>tools/reference/chromium</c>, cases <c>a2-row-end*</c>), under <see cref="TableEndOverflow.EndsOnPage"/>, the
/// default, the table ends on the page (the limits of docs/architecture.md, "Known limits", pinned in <c>RowEndTests</c>).
/// Page area 276 × 180 pt (181.5 pt in <c>a2-row-end-6pt-part-after</c>), Lato, line height 15 pt.
/// </summary>
public class TableEndOverflowTests
{
    private static ParagraphElement Lines(string prefix, int count) =>
        Paragraph([.. Enumerable.Range(0, count).SelectMany(i => i == 0 ? new[] { Text($"{prefix}{i}") } : [LineBreak(), Text($"{prefix}{i}")])]);

    private static TableElement Grid(double line, CellElement cell) =>
        Table(Columns(Fill(1)), [TableRow([cell])]).GridLines(line.Pt(), Color.Black).SplitLinesAtBreaks();

    private static PageDocument Run(Element[] body, TableEndOverflow overflow, double fontSize, double pageHeight = 204)
    {
        var result = DocumentTranslator.ToModel(Document([.. body], Page(300.Pt(), pageHeight.Pt()).Margin(12.Pt()), title: "Title", language: "en")
                .FontFamily("Lato").FontSize(fontSize.Pt()).LineHeight(15.Pt())
                .EmptyRowParts(EmptyRowParts.Allow).MonolithicOverflow(MonolithicOverflow.Slice).FontMetricRounding(FontMetricRounding.WholePixels)
                .SplitLineHalves(SplitLineHalves.AtLeastOnePixel).TableEndOverflow(overflow))
            .OnSuccessBind(model => DocumentLayout.Run(model, TestFonts.SetWithItalic));
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        return result.Value;
    }

    private static PrintedPages Printed(Element[] body, TableEndOverflow overflow, double fontSize, double pageHeight = 204) =>
        PrintedPages.Engine(Run(body, overflow, fontSize, pageHeight));

    private static PrintedWord Word(PrintedPages pages, string text) => pages.Pages.SelectMany(page => page.Words).Single(word => word.Text == text);

    /// <summary>A table that starts the page: one row whose cell holds 11 lines below its top padding, so that the row up
    /// to the middle of its line below is the page area exactly and only the table's outer half of that line passes; then
    /// a paragraph "below", or nothing.</summary>
    private static Element[] OuterHalf(double line, double padding, bool below) =>
        [Grid(line, Cell([Lines("x", 11)]).PaddingTop(padding.Pt())), .. below ? new Element[] { Paragraph("below") } : []];

    [Theory]
    [InlineData("a2-row-end-first-row-half-top", 0.75, 13.875, 12, 24.375, 24)]
    [InlineData("a2-row-end-first-row-half-6pt-below", 6, 6, 10.5, 26.25, 23.25)]
    public void Where_only_the_outer_half_of_the_last_line_passes_the_next_page_takes_it_again_unpainted(
        string caseName, double line, double padding, double fontSize, double continues, double ends)
    {
        // Chromium draws the whole line on page 1 and starts page 2 below the outer half it takes again there, which it
        // does not paint: "below" at 12 + half the line + the baseline in its line. With a 0.75 pt line that is 24.375,
        // which Chromium prints at 24.75, the top of the line at the nearest whole pixel (12.375 pt is 16.5 px: 17 px);
        // with 6 pt, 26.25 = 12 + 3 + 11.25, as it prints.
        var chromium = PrintedPages.Chromium(caseName);
        var next = Printed(OuterHalf(line, padding, below: true), TableEndOverflow.ContinuesOnNextPage, fontSize);
        var end = Printed(OuterHalf(line, padding, below: true), TableEndOverflow.EndsOnPage, fontSize);

        chromium.Pages[1].Rectangles.Should().BeEmpty();
        Word(chromium, "below").Baseline.Should().Be(line > 1 ? continues : 24.75);
        next.PageTexts.Should().Equal(chromium.PageTexts);
        Word(next, "below").Baseline.Should().Be(continues);
        next.Pages[1].Rectangles.Should().BeEmpty();
        // The whole line ends page 1, its outer half past the page end at 192: to 192.375 (to 195 with 6 pt). Chromium
        // draws it to 192.75 (its outer edge on a whole pixel, 256.5 px: 257 px), or 195, under its clip to the page
        // area, so its print shows nothing past 192; the engine shows it here, under BodyClip.None (BodyClipTests).
        next.Pages[0].Rectangles.Max(box => box.Y1).Should().Be(192 + (line / 2));
        chromium.Pages[0].Rectangles.Max(box => box.Y1).Should().Be(192);
        end.PageTexts.Should().Equal(chromium.PageTexts);
        Word(end, "below").Baseline.Should().Be(ends);
    }

    [Fact]
    public void Where_only_part_of_the_outer_half_of_the_last_line_passes_the_next_page_takes_the_whole_half()
    {
        // Chromium: a one-row table at the top of the page whose 6 pt bottom line lies at 156.75 to 162.75, past the page
        // end at 162 by 0.75 pt; its outer half, 3 pt, is what the next page takes: "z" at 24.75, 3 pt below where a
        // first line starts a page ("i" at 21.75). Likewise a 0.75 pt line that passes by 0.25 pt of its 0.375 pt half
        // (a4-cell-image-179-5: "after" at 24.375, printed at 24.75 where 24.25 would print at 24).
        var chromium = PrintedPages.Chromium("a2-row-end-thick-less-after");
        chromium.Pages[0].Rectangles.Should().Contain(new PrintedBox(1, 12, 156.75, 288, 162, "#000000"));
        Word(chromium, "z").Baseline.Should().Be(Word(PrintedPages.Chromium("fr-row-later-padding-beside-line"), "i").Baseline + 3);

        // The same with 6 pt grid lines: 4.5 pt of top padding put the middle of the line below at 190.5, so 1.5 pt of
        // its outer half pass the page end at 192; the next page takes all 3 pt, as where the whole half passes (6 pt).
        var part = Printed(OuterHalf(6, 4.5, below: true), TableEndOverflow.ContinuesOnNextPage, 10.5);
        var whole = Printed(OuterHalf(6, 6, below: true), TableEndOverflow.ContinuesOnNextPage, 10.5);
        part.Pages[0].Rectangles.Max(box => box.Y1).Should().Be(193.5);
        Word(part, "below").Baseline.Should().Be(26.25).And.Be(Word(whole, "below").Baseline);
        part.Pages[1].Rectangles.Should().BeEmpty();
        Word(Printed(OuterHalf(6, 4.5, below: true), TableEndOverflow.EndsOnPage, 10.5), "below").Baseline.Should().Be(23.25, "the default ends the table on its page");
    }

    [Theory]
    [InlineData("a2-row-end-first-row-half-alone", 0.75, 13.875, 12)]
    [InlineData("a2-row-end-first-row-half-6pt-alone", 6, 6, 10.5)]
    public void Where_such_a_table_ends_the_document_the_outer_half_is_on_a_page_of_its_own_that_shows_nothing(string caseName, double line, double padding, double fontSize)
    {
        var chromium = PrintedPages.Chromium(caseName);
        var next = Printed(OuterHalf(line, padding, below: false), TableEndOverflow.ContinuesOnNextPage, fontSize);
        var end = Printed(OuterHalf(line, padding, below: false), TableEndOverflow.EndsOnPage, fontSize);

        chromium.PageTexts.Should().Equal("x0 x1 x2 x3 x4 x5 x6 x7 x8 x9 x10", "");
        chromium.Pages[1].Rectangles.Should().BeEmpty();
        next.PageTexts.Should().Equal(chromium.PageTexts);
        next.Pages[1].Rectangles.Should().BeEmpty();
        end.PageTexts.Should().Equal(["x0 x1 x2 x3 x4 x5 x6 x7 x8 x9 x10"]);
    }

    /// <summary>A paragraph, then a one-column table of a row of 22 lines that continues on page 2 with 12 lines, which
    /// end at or above the page end, so that the cell's own half of the line below passes too; then "after", or
    /// nothing.</summary>
    private static Element[] CellPasses(double line, bool after) =>
        [Lines("a", 1), Grid(line, Cell([Lines("b", 22)])), .. after ? new Element[] { Paragraph("after") } : []];

    [Theory]
    [InlineData("a2-row-end-after", 0.75, 12, 204, 24.75, 12.75)]
    [InlineData("a2-row-end-6pt-after", 6, 12, 204, 30, 18)]
    [InlineData("a2-row-end-6pt-part-after", 6, 10.5, 205.5, 27.75, 16.5)]
    public void Where_part_of_a_cell_passes_the_next_page_draws_what_passed_again_and_takes_its_height(
        string caseName, double line, double fontSize, double pageHeight, double continues, double passedBottom)
    {
        // Page 2's 12 lines end at 192; the line below them, 192 to 192 + its width, passes the page end, the cell's half
        // with it. Chromium draws what passed again at the top of page 3: the bottom line from the page's top (12) to
        // 12 + what passed, and "after" below it. In -part-after the page area ends at 193.5, so 4.5 pt of the 6 pt line
        // passes: 1.5 pt of the cell's half and the table's outer half (Chromium: the line from 10.5, offset up by the page
        // area, clipped at 12; "after" at 12 + 4.5 + 11.25).
        var chromium = PrintedPages.Chromium(caseName);
        var next = Printed(CellPasses(line, after: true), TableEndOverflow.ContinuesOnNextPage, fontSize, pageHeight);
        var end = Printed(CellPasses(line, after: true), TableEndOverflow.EndsOnPage, fontSize, pageHeight);
        static PrintedBox BottomLine(PrintedPage page) => page.Rectangles.Single(box => box.X1 - box.X0 > 200);

        Word(chromium, "after").Page.Should().Be(3);
        Word(chromium, "after").Baseline.Should().Be(continues);
        BottomLine(chromium.Pages[2]).Y1.Should().Be(passedBottom);
        next.PageTexts.Should().Equal(chromium.PageTexts);
        Word(next, "after").Baseline.Should().Be(continues);
        BottomLine(next.Pages[2]).Should().Be(new PrintedBox(3, 12, 12, 288, passedBottom, BottomLine(next.Pages[2]).Colour));
        next.Pages[2].Rectangles.Should().OnlyContain(box => box.Y0 >= 12 && box.Y1 <= passedBottom, "only what passed is drawn, from the page's top");
        next.Pages[1].Rectangles.Max(box => box.Y1).Should().Be(192 + line, "the whole line ends page 2");
        chromium.Pages[1].Rectangles.Max(box => box.Y1).Should().Be(pageHeight - 12, "Chromium draws it whole too, and its print shows it to the page area's end");
        end.PageTexts.Should().Equal(chromium.PageTexts);
        Word(end, "after").Baseline.Should().Be(fontSize == 12 ? 24 : 23.25);
        end.Pages[2].Rectangles.Should().BeEmpty();
    }

    [Theory]
    [InlineData("a2-row-end", 0.75, 12.75)]
    [InlineData("a2-row-end-6pt", 6, 18)]
    public void Where_such_a_table_ends_the_document_what_passed_is_drawn_on_a_page_of_its_own(string caseName, double line, double passedBottom)
    {
        var chromium = PrintedPages.Chromium(caseName);
        var next = Printed(CellPasses(line, after: false), TableEndOverflow.ContinuesOnNextPage, 12);
        var end = Printed(CellPasses(line, after: false), TableEndOverflow.EndsOnPage, 12);

        chromium.Pages.Should().HaveCount(3);
        chromium.Pages[2].Words.Should().BeEmpty();
        chromium.Pages[2].Rectangles.Max(box => box.Y1).Should().Be(passedBottom);
        next.PageTexts.Should().Equal(chromium.PageTexts);
        next.Pages[2].Rectangles.Should().NotBeEmpty();
        next.Pages[2].Rectangles.Min(box => box.Y0).Should().Be(12);
        next.Pages[2].Rectangles.Max(box => box.Y1).Should().Be(passedBottom);
        end.Pages.Should().HaveCount(2);
    }

    [Fact]
    public void At_a_break_between_rows_below_a_row_placed_whole_the_next_page_lays_its_rows_out_below_what_passed()
    {
        // A cell that spans two rows beside a row of 23 lines, then an empty row: the first row continues on page 2 with
        // 12 lines, which end at the page end, and the page breaks before the empty row, so the upper half of the line
        // between the rows passes the page end. Page 3 takes it again, above the line's lower half (unpainted: the
        // spanning cell continues), the empty row and the table's bottom line: "after" 1.5 pt below the page's top, as in
        // Chromium, which draws the whole line between the rows from 12 to 12.75 in the second column.
        Element[] Body(bool after) =>
        [
            Table(Columns(Fill(1), Fill(1)), [TableRow([Cell("").RowSpan(2), Cell([Lines("b", 23)])]), TableRow([Cell("")])])
                .GridLines(0.75.Pt(), Color.Black).SplitLinesAtBreaks(),
            .. after ? new Element[] { Paragraph("after") } : [],
        ];
        var chromium = PrintedPages.Chromium("a2-row-end-span-after");
        var next = Printed(Body(after: true), TableEndOverflow.ContinuesOnNextPage, 12);
        var end = Printed(Body(after: true), TableEndOverflow.EndsOnPage, 12);

        Word(chromium, "after").Baseline.Should().Be(25.5);
        chromium.Pages[2].Rectangles.Should().Contain(new PrintedBox(3, 150.75, 12, 288, 12.75, "#000000"));
        next.PageTexts.Should().Equal(chromium.PageTexts);
        Word(next, "after").Baseline.Should().Be(25.5);
        next.Pages[2].Rectangles.Should().ContainSingle(box => box.Y0 == 12 && box.Y1 == 12.75 && box.X1 == 288 && box.X0 > 140, "the passed part of the line between the rows");
        next.Pages[2].Rectangles.Max(box => box.Y1).Should().Be(13.5);
        Printed(Body(after: false), TableEndOverflow.ContinuesOnNextPage, 12).PageTexts.Should().Equal(PrintedPages.Chromium("a2-row-end-span").PageTexts);
        Word(end, "after").Baseline.Should().Be(25.125);
    }

    [Fact]
    public void A_table_at_the_top_of_a_table_cell_takes_the_outer_half_that_passed_again_as_in_Chromium()
    {
        // The inner table starts below only the outer cell's top inset (half the outer grid line): its row is placed whole
        // on page 1 with its line below passing by its outer half, as Chromium prints it, and page 2 takes that half again
        // before "below". Laid out at 12.375 + 11.25; Chromium prints 24, the line's top at the nearest whole pixel.
        var chromium = PrintedPages.Chromium("a2-row-end-first-row-half-in-cell");
        Element[] body = [Table(Columns(Fill(1)), [TableRow([Cell([Grid(0.75, Cell([Lines("x", 11)]).PaddingTop(13.125.Pt())), Paragraph("below")])])])
            .GridLines(0.75.Pt(), Color.Black).SplitLinesAtBreaks()];
        var next = Printed(body, TableEndOverflow.ContinuesOnNextPage, 10.5);
        var end = Printed(body, TableEndOverflow.EndsOnPage, 10.5);

        chromium.PageTexts.Should().Equal("x0 x1 x2 x3 x4 x5 x6 x7 x8 x9 x10", "below");
        Word(chromium, "below").Baseline.Should().Be(24);
        next.PageTexts.Should().Equal(chromium.PageTexts);
        Word(next, "below").Baseline.Should().Be(23.625);
        end.PageTexts.Should().Equal(chromium.PageTexts);
        Word(end, "below").Baseline.Should().Be(23.25);
    }

    [Fact]
    public void The_code_front_end_s_default_ends_the_table_on_the_page_and_the_modifier_sets_the_other_value()
    {
        var document = Document([Paragraph("a")], title: "Title", language: "en").FontFamily("Lato");

        LayoutProfile.Default.TableEndOverflow.Should().Be(TableEndOverflow.EndsOnPage);
        DocumentTranslator.ToModel(document).Value.Profile.TableEndOverflow.Should().Be(TableEndOverflow.EndsOnPage);
        DocumentTranslator.ToModel(document.TableEndOverflow(TableEndOverflow.ContinuesOnNextPage)).Value.Profile
            .Should().Be(LayoutProfile.Default with { TableEndOverflow = TableEndOverflow.ContinuesOnNextPage });
    }

    [Fact]
    public void A_profile_that_names_no_table_end_overflow_value_is_refused()
    {
        var result = DocumentTranslator.ToModel(Document([Paragraph("a")], title: "Title", language: "en").FontFamily("Lato").TableEndOverflow((TableEndOverflow)7));

        result.Error.Code.Should().Be(DocumentErrorCodes.Input.Invalid.LayoutProfile);
        result.Error.Message.Should().StartWith("The table end overflow policy must be EndsOnPage or ContinuesOnNextPage.");
    }
}
