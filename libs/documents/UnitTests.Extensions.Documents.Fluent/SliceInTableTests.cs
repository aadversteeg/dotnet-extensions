using System.IO;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Fluent;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.Functional;
using static Ave.Extensions.Documents.Fluent.Elements;

namespace UnitTests.Extensions.Documents.Fluent;

/// <summary>
/// Content taller than the page area inside a table (<see cref="MonolithicOverflow.Slice"/>) beside other content, in a
/// table inside a cell, in a cell that spans rows, and the row after it, against Chromium's print
/// (<c>tools/reference/chromium</c>, cases <c>a4-image-*</c>, <c>a4-line-beside-lines</c>). Where the engine's pages
/// are its own, the test states both its pages and Chromium's, and the rule that gives them. Page area 276 × 180 pt,
/// Lato 12 pt, line height 15 pt, fixed table layout, 0.75 pt grid lines, whole pixels throughout.
/// </summary>
public class SliceInTableTests
{
    private static byte[] Tall => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "chromium", "tall.png"));

    private static ImageElement TallImage() => Image(Tall).Alt("tall").Height(408.Pt());

    private static ParagraphElement Lines(string prefix, int count) =>
        Paragraph([.. Enumerable.Range(0, count).SelectMany(i => i == 0 ? new[] { Text($"{prefix}0") } : [LineBreak(), Text($"{prefix}{i}")])]);

    private static TableElement Table(int columns, params TablePart[] rows) =>
        Elements.Table(Columns([.. Enumerable.Range(0, columns).Select(_ => Fill(1))]), [.. rows]).GridLines(0.75.Pt(), Color.Black).SplitLinesAtBreaks();

    private static Element[] Body(string caseName) => caseName switch
    {
        "a4-image-beside-lines" => [Table(2, TableRow([Cell([TallImage()]), Cell([Lines("p", 30)])]), TableRow([Cell("after"), Cell("a")]))],
        "a4-line-beside-lines" => [Table(2, TableRow([Cell([Paragraph("W").FontSize(200.Pt()).LineHeight(240.Pt())]), Cell([Lines("p", 30)])]), TableRow([Cell("after"), Cell("a")]))],
        "a4-image-nested-table" => [Table(1, TableRow([Cell([Table(1, TableRow([Cell([TallImage()])]))])]), TableRow([Cell("after")])), Paragraph("below")],
        "a4-image-rowspan" => [Table(2, TableRow([Cell([TallImage()]).RowSpan(2), Cell("a")]), TableRow([Cell("b")]), TableRow([Cell("after"), Cell("z")]))],
        "a4-image-single-cell" => [Table(1, TableRow([Cell([TallImage()])]), TableRow([Cell("after")]))],
        _ => throw new ArgumentException(caseName, nameof(caseName)),
    };

    private static PrintedPages Printed(string caseName, CellsBesideSlices cells = CellsBesideSlices.GoOn)
    {
        var document = Document([.. Body(caseName)], Page(300.Pt(), 204.Pt()).Margin(12.Pt()), title: "Title", language: "en")
            .FontFamily("Lato").LineHeight(15.Pt()).MonolithicOverflow(MonolithicOverflow.Slice).FontMetricRounding(FontMetricRounding.WholePixels)
            .CellsBesideSlices(cells);
        var result = DocumentTranslator.ToModel(document).OnSuccessBind(model => DocumentLayout.Run(model, TestFonts.SetWithItalic)).OnSuccessMap(PrintedPages.Engine);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        return result.Value;
    }

    private static string Lines(int from, int to) => string.Join(" ", Enumerable.Range(from, to - from + 1).Select(i => $"p{i}"));

    private static List<List<PrintedBox>> Images(PrintedPages pages) => [.. pages.Pages.Select(page => page.Images.ToList())];

    private static double Baseline(PrintedPages pages, string text) => pages.Pages.SelectMany(page => page.Words).Single(word => word.Text == text).Baseline;

    [Fact]
    public void An_image_in_a_table_inside_a_cell_is_sliced_from_the_top_of_the_next_page_as_Chromium_slices_it()
    {
        // The image's cell lies below the grid lines of both tables, so it does not start page 1: page 1 is cut inside the
        // inner table as a last resort (the deepest place, as Chromium breaks), and on page 2 both tables and the cell go
        // on, so the image starts the page and is sliced from its top. The same pages and image rectangles as Chromium;
        // the row "after" comes below the last slice (Chromium draws it over the slice; see the last test).
        var engine = Printed("a4-image-nested-table");
        var chromium = PrintedPages.Chromium("a4-image-nested-table");

        engine.PageTexts.Should().Equal(chromium.PageTexts);
        Images(engine).Should().BeEquivalentTo(Images(chromium), options => options.WithStrictOrdering());
        engine.Pages.Should().HaveCount(4);
    }

    [Theory]
    [InlineData("a4-image-beside-lines")]
    [InlineData("a4-line-beside-lines")]
    public void Text_in_the_cell_beside_a_slice_runs_on_beside_the_slices_where_Chromium_holds_it_back(string caseName)
    {
        // A deliberate difference under the default CellsBesideSlices.GoOn, pinned (the next test: Below, as Chromium
        // prints it). Thirty lines beside the 408 pt image (or the 240 pt line): the engine cuts the
        // row at each page end and every cell goes on on the next page, so the text runs on beside the slices; the row
        // after follows below the longer of the two. Chromium stops the text cell while the slices run and goes on with it
        // below the end of the sliced piece, on the page of its last slice (image: p23 at 72 pt, under the 48 pt slice; the
        // line: p23 at 84 pt, under its 60 pt slice), a page more for the line.
        var engine = Printed(caseName);
        var chromium = PrintedPages.Chromium(caseName);

        var image = caseName == "a4-image-beside-lines";
        chromium.PageTexts.Should().Equal(image
            ? [Lines(0, 10), Lines(11, 22), string.Empty, Lines(23, 29) + " after a"]
            : [Lines(0, 10), Lines(11, 22) + " W", "W " + Lines(23, 29), "after a"]);
        engine.PageTexts.Should().Equal(image
            ? [Lines(0, 10), Lines(11, 22), Lines(23, 29), "after a"]
            : [Lines(0, 10), Lines(11, 22) + " W", "p23 W " + Lines(24, 29) + " after a"]);
        Baseline(chromium, "p23").Should().Be(image ? 72 : 84);
        Baseline(engine, "p23").Should().Be(24);
        if (image)
        {
            Images(engine).Should().BeEquivalentTo(Images(chromium), options => options.WithStrictOrdering(), "the slices themselves are Chromium's");
        }
    }

    [Theory]
    [InlineData("a4-image-beside-lines")]
    [InlineData("a4-line-beside-lines")]
    public void Under_CellsBesideSlices_Below_the_text_beside_a_slice_goes_on_below_it_as_Chromium_prints_it(string caseName)
    {
        // Chromium lays the piece out whole on the page where it starts; on each later page the text cell goes on below
        // what is left of the piece there: nothing beside a slice that covers the whole page, and p23 right below the last
        // slice (the image: 72 pt, under its 48 pt slice; the line: 84 pt, under its 60 pt slice). The row after follows
        // the text: below it under the image; under the line it starts page 4, where this test splits the grid line at
        // the break exactly (SplitLineHalves.Exact: "after" at 24.375 pt, Chromium's whole pixel gives 24.75). The
        // engine's pages and slices are Chromium's.
        var engine = Printed(caseName, CellsBesideSlices.Below);
        var chromium = PrintedPages.Chromium(caseName);

        var image = caseName == "a4-image-beside-lines";
        engine.PageTexts.Should().Equal(chromium.PageTexts);
        Baseline(engine, "p23").Should().Be(Baseline(chromium, "p23")).And.Be(image ? 72 : 84);
        Baseline(engine, "after").Should().Be(image ? Baseline(chromium, "after") : 24.375);
        Images(engine).Should().BeEquivalentTo(Images(chromium), options => options.WithStrictOrdering());
    }

    [Fact]
    public void Under_CellsBesideSlices_Below_a_row_without_a_slice_goes_on_as_under_GoOn()
    {
        // Only a page that goes on with a slice lowers the other cells: a table whose pieces all fit lays out the same.
        var below = Printed("a4-image-single-cell", CellsBesideSlices.Below);
        var goOn = Printed("a4-image-single-cell");

        below.PageTexts.Should().Equal(goOn.PageTexts);
        Baseline(below, "after").Should().Be(Baseline(goOn, "after"));
    }

    [Fact]
    public void An_image_in_a_cell_that_spans_two_rows_follows_the_engine_s_rules_for_rows_and_spanning_cells()
    {
        // A difference that follows from three stated rules, pinned. (1) The two rows the cell spans are one unit; it
        // does not fit, and of its breakpoints (between the rows, inside each row), all of the same appeal, the page breaks
        // at the latest (as everywhere, TablePage): after "b". (2) The image lies below the cell's half grid line, so it
        // goes on on the next page, where it starts the page and is sliced. (3) A row span's missing height goes to its
        // last row (a deliberate deviation from CSS, docs/architecture.md), so the second row goes on beside the slices,
        // and the next row starts below the last slice, as it does after every slice. Engine: "a b" | slice | slice | last slice and "after z".
        // Chromium breaks between the two rows, puts "b" beside the first slice, draws "after z" over the second slice and
        // ends with a page that holds only the last slice.
        var engine = Printed("a4-image-rowspan");
        var chromium = PrintedPages.Chromium("a4-image-rowspan");

        engine.PageTexts.Should().Equal("a b", string.Empty, string.Empty, "after z");
        chromium.PageTexts.Should().Equal("a", "b", "after z", string.Empty);
        Images(engine).Should().BeEquivalentTo(Images(chromium), options => options.WithStrictOrdering(), "the slices are the same");
        Baseline(engine, "after").Should().BeGreaterThan(engine.Pages[3].Images.Single().Y1, "the next row comes below the last slice");
        Baseline(chromium, "after").Should().BeLessThan(chromium.Pages[2].Images.Single().Y1, "Chromium draws it over the slice");
    }

    [Fact]
    public void In_a_one_column_table_the_row_after_a_sliced_image_comes_below_its_last_slice_as_Chromium_places_it()
    {
        // The next row starts below the last slice in every shape. Here Chromium too starts the next row below the last slice (the 48 pt slice ends at
        // 60 pt; "after" at 72.75 pt in both), as the engine does. In other shapes (a4-image-in-cell, two columns; a4-image-nested-table) Chromium draws the row at the top of the page, over the slice, which the engine does not copy.
        var engine = Printed("a4-image-single-cell");
        var chromium = PrintedPages.Chromium("a4-image-single-cell");

        engine.PageTexts.Should().Equal(chromium.PageTexts);
        Images(engine).Should().BeEquivalentTo(Images(chromium), options => options.WithStrictOrdering());
        Baseline(engine, "after").Should().Be(Baseline(chromium, "after")).And.Be(72.75);
    }
}
