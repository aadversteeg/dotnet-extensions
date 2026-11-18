using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Fluent;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.Functional;
using UnitTests.Extensions.Documents.Layout;
using static Ave.Extensions.Documents.Fluent.Elements;

namespace UnitTests.Extensions.Documents.Fluent;

/// <summary>
/// Cuts inside a table row whose part on one page would show nothing (<c>TableBreaks</c>, LeavesSomething and
/// CutShows), against Chromium's print of the same tables (<c>tools/reference/chromium</c>, cases <c>a2-*</c> and
/// <c>a3-*</c>). Under the default policy (<see cref="EmptyRowParts.Avoid"/>) such a cut is a last resort: the row moves to
/// the next page whole where it can, and is cut anyway where it cannot, so no table is refused. Chromium cuts a row
/// wherever its cells can break, also where a part shows nothing, and so does the engine under
/// <see cref="EmptyRowParts.Allow"/> (docs/architecture.md, "Empty row parts are a policy"). Page area 276 × 180 pt
/// (<c>@page { size: 300pt 204pt; margin: 12pt }</c>), 0.75 pt grid lines (one pixel), split at breaks as Chromium draws
/// them.
/// </summary>
public class RowPartTests
{
    private static PageSetup Area => Page(300.Pt(), 204.Pt()).Margin(12.Pt());

    private static Result<ValueList<PageFragment>, global::Ave.Extensions.ErrorPaths.Error> Paginate(params Element[] body) =>
        Paginate(Document([.. body], Area, title: "Title", language: "en").FontFamily("Lato"));

    private static Result<ValueList<PageFragment>, global::Ave.Extensions.ErrorPaths.Error> Paginate(Document document) =>
        DocumentTranslator.ToModel(document).OnSuccessBind(model => Paginator.Paginate(LayoutEnvironment.Create(TestFonts.SetWithItalic), model));

    private static Result<PrintedPages, global::Ave.Extensions.ErrorPaths.Error> Print(params Element[] body) =>
        DocumentTranslator.ToModel(Document([.. body], Area, title: "Title", language: "en").FontFamily("Lato"))
            .OnSuccessBind(model => DocumentLayout.Run(model, TestFonts.SetWithItalic))
            .OnSuccessMap(PrintedPages.Engine);

    /// <summary>The table of the a2 cases: "before", the row under test, "after".</summary>
    private static TableElement Between(TableRowElement row) =>
        Table(Columns(Fill(1), Fill(1)), [TableRow([Cell("before"), Cell("b")]), row, TableRow([Cell("after"), Cell("a")])])
            .GridLines(0.75.Pt(), Color.Black).SplitLinesAtBreaks();

    private static Span[] Breaks(int count) => [.. Enumerable.Range(0, count).Select(_ => LineBreak())];

    private static PrintedPages Printed(params Element[] body)
    {
        var result = Print(body);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        return result.Value;
    }

    [Fact]
    public void A_row_whose_middle_shows_nothing_over_a_whole_page_is_cut_there_as_a_last_resort_like_Chromium()
    {
        // "top", 30 blank lines and "end" beside "x". Page 1 is cut after "top"; page 2 can hold only blank lines of the
        // row, which show nothing, and the row cannot move from the top of the page, so it is cut there anyway: page 2 is
        // blank, page 3 holds "end". Chromium prints the same three pages. (Milestone 1 refused this document.)
        var engine = Printed(Between(TableRow([Cell("x"), Cell([Paragraph("top"), Paragraph([.. Breaks(30)]), Paragraph("end")])])));

        engine.PageTexts.Should().Equal(PrintedPages.Chromium("a2-blank-part").PageTexts);
        engine.PageTexts.Should().Equal("before b x top", string.Empty, "end after a");
    }

    [Fact]
    public void A_row_that_shows_nothing_moves_whole_and_is_cut_at_the_top_of_the_next_page_where_Chromium_cuts_it_at_once()
    {
        // Twenty empty blocks with padding (200 pt) beside an empty cell: no part of the row shows anything. The engine
        // moves the row to page 2, where it cannot move again and is cut as a last resort; Chromium cuts it right after
        // "before" and leaves an empty strip at the end of page 1. Both are on a blank part of the row; the engine's page 2
        // is that part, Chromium's is the rest of it: the default policy, Avoid (see the class).
        var padded = Printed(Between(TableRow([Cell(""), Cell([.. Enumerable.Range(0, 20).Select(_ => (Element)Block([]).Padding(5.Pt()))])])));
        var breaks = Printed(Between(TableRow([Cell(""), Cell([Paragraph([.. Breaks(20)])])])));

        padded.PageTexts.Should().Equal("before b", string.Empty, "after a");
        PrintedPages.Chromium("a2-padded-blocks").PageTexts.Should().Equal("before b", "after a");
        breaks.PageTexts.Should().Equal("before b", string.Empty, "after a");
        PrintedPages.Chromium("a2-line-breaks").PageTexts.Should().Equal("before b", "after a");
    }

    [Fact]
    public void A_row_whose_rest_would_show_nothing_on_the_next_page_moves_whole()
    {
        // The mirror of the strip (review 36): each row is a line of text beside two empty blocks with 4 pt padding. The
        // page end falls below t1's text but inside its second block: Chromium cuts there, so page 2 starts with an empty
        // 9 pt part of t1. The engine moves t1 to page 2 whole: the default policy, Avoid (see the class).
        TableElement Rows() => Table(Columns(Fill(1), Fill(1)),
                [.. Enumerable.Range(0, 4).Select(i => (TablePart)TableRow([Cell($"t{i}"), Cell([Block([]).Padding(4.Pt()), Block([]).Padding(4.Pt())])]))])
            .GridLines(0.75.Pt(), Color.Black).SplitLinesAtBreaks();

        var engine = Printed(Paragraph("intro").MarginBottom(133.5.Pt()), Rows());

        engine.PageTexts.Should().Equal("intro t0", "t1 t2 t3");
        PrintedPages.Chromium("a3-mirror").PageTexts.Should().Equal("intro t0 t1", "t2 t3");
    }

    /// <summary>The documents where Chromium leaves a part of a row that shows nothing: the first measured ones, whose
    /// padded blocks of 10 and 8 pt are not whole pixels, and the same shapes with blocks of 9 pt (12 px), whose every
    /// length is whole pixels, so grid lines compare exactly.</summary>
    public static TheoryData<string> ChromiumStrips => ["a2-padded-blocks", "a2-line-breaks", "a3-mirror", "a2-padded-blocks-px", "a3-mirror-px"];

    /// <summary>The whole-pixel ones.</summary>
    public static TheoryData<string> WholePixelStrips => ["a2-padded-blocks-px", "a2-line-breaks", "a3-mirror-px"];

    private static Element[] StripBody(string caseName) => caseName switch
    {
        "a2-padded-blocks" => [Between(TableRow([Cell(""), Cell([.. Enumerable.Range(0, 20).Select(_ => (Element)Block([]).Padding(5.Pt()))])]))],
        "a2-padded-blocks-px" => [Between(TableRow([Cell(""), Cell([.. Enumerable.Range(0, 20).Select(_ => (Element)Block([]).Padding(4.5.Pt()))])]))],
        "a2-line-breaks" => [Between(TableRow([Cell(""), Cell([Paragraph([.. Breaks(20)])])]))],
        "a3-mirror" => Mirror(133.5, 4),
        "a3-mirror-px" => Mirror(129.75, 4.5),
        _ => throw new ArgumentException(caseName, nameof(caseName)),
    };

    /// <summary>"intro" with the given bottom margin, then four rows of "t" beside two empty blocks with the given padding.</summary>
    private static Element[] Mirror(double margin, double padding) =>
        [Paragraph("intro").MarginBottom(margin.Pt()), Table(Columns(Fill(1), Fill(1)),
                [.. Enumerable.Range(0, 4).Select(i => (TablePart)TableRow([Cell($"t{i}"), Cell([Block([]).Padding(padding.Pt()), Block([]).Padding(padding.Pt())])]))])
            .GridLines(0.75.Pt(), Color.Black).SplitLinesAtBreaks()];

    /// <summary>The top of every horizontal grid line on each page (a filled rectangle one pixel high and wider than
    /// one), in points, in page order.</summary>
    private static List<List<double>> HorizontalLines(PrintedPages pages) =>
        [.. pages.Pages.Select(page => page.Rectangles
            .Where(box => Math.Abs(box.Height - 0.75) < 1e-6 && box.X1 - box.X0 > 0.75 + 1e-6)
            .Select(box => box.Y0)
            .Distinct()
            .Order()
            .ToList())];

    private static Result<PrintedPages, global::Ave.Extensions.ErrorPaths.Error> PrintAllowed(Element[] body) =>
        DocumentTranslator.ToModel(Document([.. body], Area, title: "Title", language: "en").FontFamily("Lato")
                .EmptyRowParts(EmptyRowParts.Allow).FontMetricRounding(FontMetricRounding.WholePixels))
            .OnSuccessBind(model => DocumentLayout.Run(model, TestFonts.SetWithItalic)).OnSuccessMap(PrintedPages.Engine);

    [Theory]
    [MemberData(nameof(ChromiumStrips))]
    public void With_empty_row_parts_allowed_rows_are_cut_where_Chromium_cuts_them(string caseName)
    {
        // EmptyRowParts.Allow, Chromium's policy: the row is cut wherever its cells can break, also where a part shows
        // nothing. With Chromium's whole-pixel font metrics too (FontMetricRounding.WholePixels: Lato 12 pt makes 19 px
        // lines), the engine prints the same pages and words as Chromium, and the cut leaves the same part of a row that
        // shows nothing.
        var document = Document([.. StripBody(caseName)], Area, title: "Title", language: "en").FontFamily("Lato")
            .EmptyRowParts(EmptyRowParts.Allow).FontMetricRounding(FontMetricRounding.WholePixels);
        var engine = PrintAllowed(StripBody(caseName));

        engine.IsSuccess.Should().BeTrue(engine.IsFailure ? engine.Error.ToString() : string.Empty);
        engine.Value.PageTexts.Should().Equal(PrintedPages.Chromium(caseName).PageTexts);
        RowParts.EmptyStrips(Paginate(document).Value).Should().NotBeEmpty("Chromium's cut leaves a part of a row that shows nothing");
    }

    [Theory]
    [MemberData(nameof(WholePixelStrips))]
    public void Where_every_length_is_whole_pixels_the_grid_lines_are_where_Chromium_paints_them(string caseName)
    {
        // The top of every horizontal grid line, page by page, within 0.1 pt and without rounding: every length in these
        // cases (page, margins, 19 px lines, 12 px blocks, 1 px grid lines) is a whole number of pixels, so Chromium's
        // painting at whole pixels moves nothing.
        var engine = PrintAllowed(StripBody(caseName)).Value;
        var (ours, theirs) = (HorizontalLines(engine), HorizontalLines(PrintedPages.Chromium(caseName)));

        ours.Select(page => page.Count).Should().Equal(theirs.Select(page => page.Count));
        ours.Zip(theirs).Should().AllSatisfy(pages => pages.First.Zip(pages.Second).Should().AllSatisfy(pair => pair.First.Should().BeApproximately(pair.Second, 0.1)));
    }

    [Fact]
    public void Where_lengths_are_not_whole_pixels_Chromium_paints_grid_lines_up_to_half_a_pixel_from_where_layout_puts_them()
    {
        // A difference, pinned: Chromium paints a grid line at a whole pixel, the engine where layout puts it. A cell over two
        // rows beside fourteen 10 pt blocks (13.33 px; case a2-rowspan-blocks): the same pages and words, and the line
        // between the two spanned rows is at 168.5 pt in the engine, at 168 pt in Chromium: 0.5 pt, the largest difference
        // measured (0.25 pt in a2-padded-blocks and a3-mirror).
        Element[] Blocks() => [.. Enumerable.Range(0, 14).Select(_ => (Element)Block([]).PaddingTop(5.Pt()).PaddingBottom(5.Pt()))];
        var body = Table(Columns(Fill(1), Fill(1)), [TableRow([Cell("before"), Cell("b")]), TableRow([Cell("s").RowSpan(2), Cell([.. Blocks()])]), TableRow([Cell([.. Blocks()])]), TableRow([Cell("after"), Cell("a")])])
            .GridLines(0.75.Pt(), Color.Black).SplitLinesAtBreaks();
        var result = DocumentTranslator.ToModel(Document([body], Area, title: "Title", language: "en").FontFamily("Lato").LineHeight(15.Pt()).EmptyRowParts(EmptyRowParts.Allow))
            .OnSuccessBind(model => DocumentLayout.Run(model, TestFonts.SetWithItalic)).OnSuccessMap(PrintedPages.Engine);
        var chromium = PrintedPages.Chromium("a2-rowspan-blocks");

        result.Value.PageTexts.Should().Equal(chromium.PageTexts);
        var (ours, theirs) = (HorizontalLines(result.Value), HorizontalLines(chromium));
        ours.Select(page => page.Count).Should().Equal(theirs.Select(page => page.Count));
        ours.Zip(theirs).SelectMany(pages => pages.First.Zip(pages.Second, (a, b) => Math.Abs(a - b))).Max().Should().BeApproximately(0.5, 1e-9);
    }

    [Fact]
    public void At_a_break_between_rows_Chromium_draws_a_one_pixel_line_whole_on_both_pages_where_the_engine_splits_it()
    {
        // A difference, pinned (case a2-split-line-1px; .SplitLinesAtBreaks()): the engine draws the upper half of the
        // 0.75 pt line at the end of page 1 and the lower half at the top of page 2, so page 2's first row starts at the
        // page area's top. Chromium cannot split a pixel: it draws the whole line on both pages, and page 2's first row
        // starts 0.375 pt lower (a 3 pt line, four pixels, it does split: cases mf4-line-*).
        var engine = PrintAllowed(Mirror(133.5, 4.5)).Value;
        var chromium = PrintedPages.Chromium("a2-split-line-1px");

        engine.PageTexts.Should().Equal(chromium.PageTexts);
        chromium.Pages[1].Rectangles.Where(box => box.X1 - box.X0 > 1).Min(box => box.Y0).Should().Be(12);
        chromium.Pages[1].Rectangles.Where(box => box.X1 - box.X0 > 1 && box.Y0 == 12).Should().AllSatisfy(box => box.Height.Should().Be(0.75));
        engine.Pages[1].Rectangles.Where(box => box.X1 - box.X0 > 1 && box.Y0 == 12).Should().AllSatisfy(box => box.Height.Should().BeApproximately(0.375, 1e-9));
        (chromium.Pages[1].Words[0].Baseline - engine.Pages[1].Words[0].Baseline).Should().BeApproximately(0.375, 1e-9);
    }

    /// <summary>The a2-cells-end cases: "intro" with a 99 pt bottom margin, then rows of "t" beside a cell of two 8 pt blocks
    /// (4 pt padding above and below, or a block and an 8 pt line "u"); line height 15 pt, so row t2's cells end at the
    /// page end (192 pt) and only the half of the line below the row passes it.</summary>
    private static Element[] CellsEnd(string caseName)
    {
        var text = caseName == "a2-cells-end-text";
        CellElement Second() => Cell([Block([]).PaddingTop(4.Pt()).PaddingBottom(4.Pt()),
            text ? Paragraph("u").FontSize(6.Pt()).LineHeight(8.Pt()) : Block([]).PaddingTop(4.Pt()).PaddingBottom(4.Pt())]);
        TablePart First() => caseName == "a2-cells-end-header" ? HeaderRow([Cell("H"), Cell("h")]) : TableRow([Cell("H"), Cell("h")]);
        return [Paragraph("intro").MarginBottom(99.Pt()), Table(Columns(Fill(1), Fill(1)),
                [First(), .. Enumerable.Range(0, 5).Select(i => (TablePart)TableRow([Cell($"t{i}"), Second()]))])
            .GridLines(0.75.Pt(), Color.Black).SplitLinesAtBreaks()];
    }

    private static PrintedPages PrintedWith(EmptyRowParts parts, Element[] body)
    {
        var document = Document([.. body], Area, title: "Title", language: "en").FontFamily("Lato").LineHeight(15.Pt()).EmptyRowParts(parts);
        var result = DocumentTranslator.ToModel(document).OnSuccessBind(model => DocumentLayout.Run(model, TestFonts.SetWithItalic)).OnSuccessMap(PrintedPages.Engine);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        return result.Value;
    }

    [Theory]
    [InlineData("a2-cells-end")]
    [InlineData("a2-cells-end-header")]
    [InlineData("a2-cells-end-text")]
    public void With_empty_row_parts_allowed_a_row_whose_cells_end_on_the_page_but_not_its_line_moves_whole_as_in_Chromium(string caseName)
    {
        // Every cell of row t2 ends on page 1; only the half of the grid line below the row, which each cell's box holds,
        // passes the page end. Chromium does not cut such a row: it moves to page 2 whole, with or without a repeated header
        // row, and also when the rest would be a line of text. Under Allow the engine does the same.
        var engine = PrintedWith(EmptyRowParts.Allow, CellsEnd(caseName));
        var chromium = PrintedPages.Chromium(caseName);

        engine.PageTexts.Should().Equal(chromium.PageTexts);
        engine.Pages[1].Words.Select(word => word.Text).Should().StartWith(caseName == "a2-cells-end-header" ? ["H", "h", "t2"] : ["t2"]);
    }

    [Fact]
    public void With_empty_row_parts_avoided_the_cells_keep_their_line_on_the_page_and_the_row_is_cut()
    {
        // The default policy keeps each cell's half of the line below on the page: the cell breaks before its last block, so
        // row t2 is cut, its text on page 1 and the line "u" on page 2, where Chromium moves the row whole (Allow follows
        // Chromium, above).
        var engine = PrintedWith(EmptyRowParts.Avoid, CellsEnd("a2-cells-end-text"));

        engine.Pages[0].Words.Select(word => word.Text).Should().Contain("t2");
        engine.Pages[1].Words.Select(word => word.Text).Should().StartWith(["u"]);
        PrintedPages.Chromium("a2-cells-end-text").Pages[1].Words.Select(word => word.Text).Should().StartWith(["t2", "u"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Rows_beside_two_padded_empty_blocks_never_leave_a_part_that_shows_nothing_at_a_page_end_or_start(bool clone)
    {
        // The mirror, swept (review 36 found it in 237 of 2 400 documents): every page end over two rows.
        TableElement Rows()
        {
            var table = Table(Columns(Fill(1), Fill(1)),
                [.. Enumerable.Range(0, 12).Select(i => (TablePart)TableRow([Cell($"t{i}"), Cell([Block([]).Padding(4.Pt()), Block([]).Padding(4.Pt())])]))])
                .GridLines(0.75.Pt(), Color.Black);
            return clone ? table.RepeatLinesAtBreaks() : table.SplitLinesAtBreaks();
        }

        foreach (var offset in Enumerable.Range(0, 80).Select(step => step * 0.25))
        {
            var pages = Paginate(Paragraph("intro").MarginBottom((120 + offset).Pt()), Rows());

            pages.IsSuccess.Should().BeTrue(pages.IsFailure ? pages.Error.ToString() : string.Empty);
            RowParts.EmptyStrips(pages.Value).Should().BeEmpty($"offset {offset}");
        }
    }

    [Theory]
    [InlineData(8.0, 0.0)]
    [InlineData(0.0, 8.0)]
    public void Text_hidden_under_a_thick_line_does_not_count_as_shown_in_the_row_below_it(double first, double second)
    {
        // Review 36: units of two rows beside a five-word cell that spans them, an 8 pt line under one row of each unit,
        // clone; with 5 pt top padding in the spanning cell a page ended with the 8 pt line and the closing line under it,
        // because the cell's text reached 1 pt into the row below, where the thick line covers it. The visible part of a
        // row starts under the line above it, so that row is not cut there: no part of a row that shows nothing, for every
        // padding from 0 to 14 pt.
        CellElement Lined(CellElement cell, double width) => width > 0 ? cell.BorderBottom(width.Pt(), Color.Black) : cell;
        foreach (var padding in Enumerable.Range(0, 15))
        {
            var table = Table(Columns(80.Pt(), Fill(1)), [.. Enumerable.Range(0, 6).SelectMany(unit => new TablePart[]
            {
                TableRow([Cell($"s{unit}x000 s{unit}x001 s{unit}x002 s{unit}x003 s{unit}x004").RowSpan(2).PaddingTop(padding.Pt()), Lined(Cell($"a{unit}x000 a{unit}x001"), first)]),
                TableRow([Lined(Cell($"b{unit}x000 b{unit}x001"), second)]),
            })]).GridLines(1.Pt(), Color.Black);

            var pages = Paginate(Paragraph("intro"), table);

            pages.IsSuccess.Should().BeTrue(pages.IsFailure ? pages.Error.ToString() : string.Empty);
            RowParts.EmptyStrips(pages.Value).Should().BeEmpty($"top padding {padding}");
        }
    }
}
