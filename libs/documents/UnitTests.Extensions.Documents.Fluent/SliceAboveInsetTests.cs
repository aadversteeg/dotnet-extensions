using System.IO;
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
/// A piece that fits the page area but not with the insets around it in its cell (padding, the grid line), under
/// <see cref="MonolithicOverflow.Slice"/>: the test for slicing is "the piece with its insets does not fit a page", so it
/// is sliced, not refused. Against Chromium's print (<c>tools/reference/chromium</c>, cases <c>a4-cell-*</c>), which does
/// not slice it: Chromium places the piece whole at the top of the next page and lets the cell's bottom padding and grid
/// line pass the page end. That is a listed difference (docs/architecture.md, "Known limits"), pinned here: the engine's
/// pages are asserted, Chromium's are stated. The same piece in a block outside a table is printed as Chromium prints it
/// (case <c>a4-image-177-block-padding-3</c>). Page area 276 × 180 pt, Lato 12 pt, line height 15 pt, 0.75 pt grid lines.
/// </summary>
public class SliceAboveInsetTests
{
    private static byte[] Tall => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "chromium", "tall.png"));

    private static Element[] Body(string caseName) => caseName switch
    {
        "a4-cell-pad3-image-177" => [Table(Cell([Image(Tall).Alt("tall").Height(177.Pt())])).CellPadding(3.Pt()), Paragraph("after")],
        "a4-cell-pad3-line-177" => [Table(Cell([Paragraph("W").LineHeight(177.Pt())])).CellPadding(3.Pt()), Paragraph("after")],
        "a4-cell-image-179-5" => [Table(Cell([Image(Tall).Alt("tall").Height(179.5.Pt())])), Paragraph("after")],
        "a4-image-177-block-padding-3" => [Block([Image(Tall).Alt("tall").Height(177.Pt())]).PaddingTop(3.Pt()).PaddingBottom(3.Pt()), Paragraph("after")],
        "after" => [Paragraph("after")],
        _ => throw new ArgumentException(caseName, nameof(caseName)),
    };

    private static TableElement Table(CellElement cell) =>
        Elements.Table(Columns(Fill(1)), [TableRow([cell])]).GridLines(0.75.Pt(), Color.Black).SplitLinesAtBreaks();

    private static Result<PageDocument, Ave.Extensions.ErrorPaths.Error> Laid(string caseName, MonolithicOverflow overflow)
    {
        var document = Document([.. Body(caseName)], Page(300.Pt(), 204.Pt()).Margin(12.Pt()), title: "Title", language: "en")
            .FontFamily("Lato").LineHeight(15.Pt()).MonolithicOverflow(overflow).FontMetricRounding(FontMetricRounding.WholePixels);
        return DocumentTranslator.ToModel(document).OnSuccessBind(model => DocumentLayout.Run(model, TestFonts.SetWithItalic));
    }

    private static PageDocument Sliced(string caseName)
    {
        var result = Laid(caseName, MonolithicOverflow.Slice);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        return result.Value;
    }

    /// <summary>The height of each page's clips: the slices it shows.</summary>
    private static List<List<double>> Slices(PageDocument document) =>
        [.. document.Pages.Select(page => page.Items.OfType<ClipStart>().Select(clip => clip.Area.Size.Height).ToList())];

    private static double Baseline(PrintedPages pages, string text) => pages.Pages.SelectMany(page => page.Words).Single(word => word.Text == text).Baseline;

    [Theory]
    [InlineData("a4-cell-pad3-image-177", 176.625, 0.375, 177)]
    [InlineData("a4-cell-image-179-5", 179.25, 0.25, 179.25)]
    public void An_image_that_fits_the_page_area_but_not_with_its_cell_s_insets_is_sliced_where_Chromium_moves_it_whole(string caseName, double firstSlice, double lastSlice, double wholeInChromium)
    {
        // Page 1 holds the cell's top padding and grid line; the image starts page 2 and is sliced there above the cell's
        // bottom padding and half grid line; its last slice, the bottom inset and "after" are on page 3. Chromium: the same
        // three pages and the same page for "after", but the image whole on page 2 (177 pt from 12 pt; the 179.5 pt image
        // drawn 179.25 pt high, Chromium's whole pixels) and the bottom inset past the page end, so "after" stands higher on
        // page 3.
        var document = Sliced(caseName);
        var engine = PrintedPages.Engine(document);
        var chromium = PrintedPages.Chromium(caseName);

        engine.PageTexts.Should().Equal(chromium.PageTexts);
        engine.PageTexts.Should().Equal("", "", "after");
        chromium.Pages[1].Images.Should().ContainSingle().Which.Height.Should().BeApproximately(wholeInChromium, 0.001);
        chromium.Pages[2].Images.Should().BeEmpty();
        Slices(document).Should().HaveCount(3);
        Slices(document)[0].Should().BeEmpty();
        Slices(document)[1].Should().ContainSingle().Which.Should().BeApproximately(firstSlice, 0.001);
        Slices(document)[2].Should().ContainSingle().Which.Should().BeApproximately(lastSlice, 0.001);
        Baseline(engine, "after").Should().BeGreaterThan(Baseline(chromium, "after"));
    }

    [Fact]
    public void A_line_that_fits_the_page_area_but_not_with_its_cell_s_insets_is_sliced_where_Chromium_moves_it_whole()
    {
        // The same for a 177 pt line: its glyph lies in the first slice (page 2), its last 0.375 pt and "after" on page 3.
        // Chromium: the line whole on page 2, "after" on page 3.
        var document = Sliced("a4-cell-pad3-line-177");
        var engine = PrintedPages.Engine(document);
        var chromium = PrintedPages.Chromium("a4-cell-pad3-line-177");

        chromium.PageTexts.Should().Equal("", "W", "after");
        engine.Pages.Should().HaveCount(3);
        engine.Pages[1].Text.Should().Be("W");
        Slices(document).Select(page => page.Sum()).Should().Equal(0, 176.625, 0.375);
        engine.Pages[2].Words.Select(word => word.Text).Should().EndWith("after");
        Baseline(engine, "after").Should().BeGreaterThan(Baseline(chromium, "after"));
    }

    [Theory]
    [InlineData(MonolithicOverflow.Refuse)]
    [InlineData(MonolithicOverflow.Slice)]
    public void A_block_outside_a_table_carries_its_bottom_padding_below_such_a_piece_to_the_next_page_as_Chromium_does(MonolithicOverflow overflow)
    {
        // A block with 3 pt padding above and below a 177 pt image, then "after": with the top padding the image is the
        // page area exactly, so the box breaks before its bottom padding (SlicedBox), the image stays whole on page 1, and
        // the padding starts page 2 with "after" below it, 3 pt lower than at the top of a page, in both.
        var result = Laid("a4-image-177-block-padding-3", overflow);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        var engine = PrintedPages.Engine(result.Value);
        var chromium = PrintedPages.Chromium("a4-image-177-block-padding-3");
        var engineTop = Baseline(PrintedPages.Engine(Laid("after", overflow).Value), "after");
        var chromiumTop = Baseline(PrintedPages.Chromium("a2-row-end-below-paragraph"), "i0");

        engine.PageTexts.Should().Equal(chromium.PageTexts);
        engine.PageTexts.Should().Equal("", "after");
        Slices(result.Value).Should().AllSatisfy(page => page.Should().BeEmpty());
        engine.Pages.SelectMany(page => page.Images).Should().Equal(chromium.Pages.SelectMany(page => page.Images));
        Baseline(engine, "after").Should().BeApproximately(engineTop + 3, 1e-9);
        Baseline(chromium, "after").Should().Be(chromiumTop + 3);
    }

    [Theory]
    [InlineData("a4-cell-pad3-image-177")]
    [InlineData("a4-cell-pad3-line-177")]
    [InlineData("a4-cell-image-179-5")]
    public void Under_Refuse_such_a_piece_is_still_refused(string caseName)
    {
        var result = Laid(caseName, MonolithicOverflow.Refuse);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(LayoutErrorCodes.DoesNotFit);
    }
}
