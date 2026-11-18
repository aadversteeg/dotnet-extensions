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

    /// <summary>The three documents where Chromium leaves a part of a row that shows nothing.</summary>
    public static TheoryData<string> ChromiumStrips => ["a2-padded-blocks", "a2-line-breaks", "a3-mirror"];

    private static Element[] StripBody(string caseName) => caseName switch
    {
        "a2-padded-blocks" => [Between(TableRow([Cell(""), Cell([.. Enumerable.Range(0, 20).Select(_ => (Element)Block([]).Padding(5.Pt()))])]))],
        "a2-line-breaks" => [Between(TableRow([Cell(""), Cell([Paragraph([.. Breaks(20)])])]))],
        "a3-mirror" => [Paragraph("intro").MarginBottom(133.5.Pt()), Table(Columns(Fill(1), Fill(1)),
                [.. Enumerable.Range(0, 4).Select(i => (TablePart)TableRow([Cell($"t{i}"), Cell([Block([]).Padding(4.Pt()), Block([]).Padding(4.Pt())])]))])
            .GridLines(0.75.Pt(), Color.Black).SplitLinesAtBreaks()],
        _ => throw new ArgumentException(caseName, nameof(caseName)),
    };

    /// <summary>The top of every horizontal grid line on each page (a filled rectangle one pixel high and wider than
    /// one), in points, in page order.</summary>
    private static List<List<double>> HorizontalLines(PrintedPages pages) =>
        [.. pages.Pages.Select(page => page.Rectangles
            .Where(box => Math.Abs(box.Height - 0.75) < 1e-6 && box.X1 - box.X0 > 0.75 + 1e-6)
            .Select(box => box.Y0)
            .Distinct()
            .Order()
            .ToList())];

    /// <summary>A length in points rounded to a whole CSS pixel of 0.75 pt, as Chromium paints a grid line.</summary>
    private static double WholePixel(double points) => Math.Round(points / 0.75, MidpointRounding.AwayFromZero) * 0.75;

    [Theory]
    [MemberData(nameof(ChromiumStrips))]
    public void With_empty_row_parts_allowed_rows_are_cut_where_Chromium_cuts_them(string caseName)
    {
        // EmptyRowParts.Allow, Chromium's policy: the row is cut wherever its cells can break, also where a part shows
        // nothing. With Chromium's whole-pixel font metrics too (FontMetricRounding.WholePixels: Lato 12 pt makes 19 px
        // lines, 14.25 pt, where the exact metrics give 14.4 pt), the engine prints the same pages and words as Chromium,
        // and its grid lines where Chromium's are. What is compared for the lines: the top of every horizontal grid line,
        // page by page. Chromium paints a grid line at a whole pixel, the engine where layout puts it (the centre of a
        // 0.75 pt line can fall on a third of a pixel); so the engine's tops rounded to whole pixels must be Chromium's,
        // and before rounding they may differ by no more than 0.25 pt, the largest difference these cases show (pinned).
        var document = Document([.. StripBody(caseName)], Area, title: "Title", language: "en").FontFamily("Lato")
            .EmptyRowParts(EmptyRowParts.Allow).FontMetricRounding(FontMetricRounding.WholePixels);
        var engine = DocumentTranslator.ToModel(document).OnSuccessBind(model => DocumentLayout.Run(model, TestFonts.SetWithItalic)).OnSuccessMap(PrintedPages.Engine);
        var chromium = PrintedPages.Chromium(caseName);

        engine.IsSuccess.Should().BeTrue(engine.IsFailure ? engine.Error.ToString() : string.Empty);
        engine.Value.PageTexts.Should().Equal(chromium.PageTexts);
        var (ours, theirs) = (HorizontalLines(engine.Value), HorizontalLines(chromium));
        ours.Select(page => page.Select(WholePixel).ToList()).Should().BeEquivalentTo(theirs, options => options.WithStrictOrdering());
        ours.Zip(theirs).SelectMany(pages => pages.First.Zip(pages.Second, (a, b) => Math.Abs(a - b))).Max().Should().BeLessThanOrEqualTo(0.25 + 1e-9);
        RowParts.EmptyStrips(Paginate(document).Value).Should().NotBeEmpty("Chromium's cut leaves a part of a row that shows nothing");
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
