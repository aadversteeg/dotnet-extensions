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
/// An image or a line taller than the page area (<see cref="MonolithicOverflow"/>, layout's MonolithicSlices), against
/// Chromium's print of the same documents (<c>tools/reference/chromium</c>, cases <c>a4-*</c>). By default the document is
/// refused; with <see cref="MonolithicOverflow.Slice"/> the content is sliced as Chromium slices it. Page area 276 × 180
/// pt (<c>@page { size: 300pt 204pt; margin: 12pt }</c>); the image is 102 × 408 pt (136 × 544 px), so it shows 180,
/// 180 and 48 pt; the line is 200 pt Lato, 240 pt high, so it shows 180 and 60 pt.
/// </summary>
public class MonolithicSliceTests
{
    private const double AreaHeight = 180;

    private static PageSetup Area => Page(300.Pt(), 204.Pt()).Margin(12.Pt());

    private static byte[] Tall => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "chromium", "tall.png"));

    private static ImageElement TallImage() => Image(Tall).Alt("tall").Width(102.Pt()).Height(408.Pt());

    private static Document Doc(MonolithicOverflow overflow, params Element[] body) =>
        Document([.. body], Area, title: "Title", language: "en").FontFamily("Lato").MonolithicOverflow(overflow);

    private static Result<PageDocument, global::Ave.Extensions.ErrorPaths.Error> Run(Document document) =>
        DocumentTranslator.ToModel(document).OnSuccessBind(model => DocumentLayout.Run(model, TestFonts.SetWithItalic));

    private static PrintedPages Printed(params Element[] body)
    {
        var result = Run(Doc(MonolithicOverflow.Slice, body));
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        return PrintedPages.Engine(result.Value);
    }

    /// <summary>The documents of the four cases.</summary>
    public static TheoryData<string> Cases => ["a4-image-first", "a4-image-after-text", "a4-line-after-text", "a4-image-in-cell"];

    private static Element[] Body(string caseName) => caseName switch
    {
        "a4-image-first" => [TallImage(), Paragraph("after")],
        "a4-image-after-text" => [Paragraph("intro"), TallImage(), Paragraph("after")],
        "a4-line-after-text" => [Paragraph("intro"), Paragraph("W").FontSize(200.Pt()), Paragraph("after")],
        // The first column is 54.75 pt (line centre to line centre) because that is the width Chromium's automatic table
        // layout gives it in this case (the case sets no widths); the engine's automatic layout is not the subject here.
        "a4-image-in-cell" => [Table(Columns(54.75.Pt(), Fill(1)), [TableRow([Cell("x"), Cell([TallImage()])]), TableRow([Cell("after"), Cell("a")])])
            .GridLines(0.75.Pt(), Color.Black).SplitLinesAtBreaks()],
        _ => throw new ArgumentException(caseName, nameof(caseName)),
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void By_default_content_taller_than_the_page_area_is_refused(string caseName)
    {
        var result = Run(Document([.. Body(caseName)], Area, title: "Title", language: "en").FontFamily("Lato"));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(LayoutErrorCodes.DoesNotFit);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Sliced_content_gives_the_pages_and_words_Chromium_prints(string caseName)
    {
        Printed(Body(caseName)).PageTexts.Should().Equal(PrintedPages.Chromium(caseName).PageTexts);
    }

    [Theory]
    [InlineData("a4-image-first", 0)]
    [InlineData("a4-image-after-text", 1)]
    public void A_sliced_image_is_drawn_whole_on_each_page_shifted_by_the_page_area_as_Chromium_draws_it(string caseName, int firstPage)
    {
        // Chromium starts the image on the page it starts (moved to a fresh page first after "intro") and draws it there
        // and on each later page, one page-area height higher each time; the engine draws the same rectangles, clipped.
        var engine = Printed(Body(caseName));
        var chromium = PrintedPages.Chromium(caseName);

        var images = engine.Pages.SelectMany(page => page.Images).ToList();
        images.Should().Equal(chromium.Pages.SelectMany(page => page.Images));
        images.Select(box => box.Page).Should().Equal(firstPage + 1, firstPage + 2, firstPage + 3);
        images.Select(box => box.Y0).Should().Equal(12, 12 - AreaHeight, 12 - 2 * AreaHeight);
    }

    [Theory]
    [InlineData("a4-image-first")]
    [InlineData("a4-image-after-text")]
    [InlineData("a4-line-after-text")]
    public void What_follows_sliced_content_starts_right_below_its_last_slice(string caseName)
    {
        // "after" lies under the last slice: 48 pt of the image, or 60 pt of the line, below the page area's top. Measured
        // from the baseline a line at the top of a page has (the two differ in how they round the font's ascent).
        var engine = Printed(Body(caseName));
        var chromium = PrintedPages.Chromium(caseName);
        var engineTop = Printed(Paragraph("after")).Pages[0].Words[0].Baseline;
        var chromiumTop = PrintedPages.Chromium("a1-text-before").Pages[0].Words[0].Baseline;
        var shown = caseName.StartsWith("a4-line", StringComparison.Ordinal) ? 60 : 48;

        Baseline(engine, "after").Should().BeApproximately(engineTop + shown, 1e-9);
        Baseline(chromium, "after").Should().Be(chromiumTop + shown);
    }

    [Fact]
    public void A_sliced_line_is_drawn_on_each_page_where_Chromium_draws_it()
    {
        // With Chromium's whole-pixel font metrics the 200 pt line's ascent is 263 px (197.25 pt): its baseline is at
        // 209.25 pt on page 2 and one page area higher, 29.25 pt, on page 3, in both. The line height is whole pixels
        // (240 pt = 320 px), so the baselines compare exactly.
        var result = Run(Doc(MonolithicOverflow.Slice, Body("a4-line-after-text")).FontMetricRounding(FontMetricRounding.WholePixels));
        var engine = PrintedPages.Engine(result.Value);
        var chromium = PrintedPages.Chromium("a4-line-after-text");

        var engineW = engine.Pages.SelectMany(page => page.Words).Where(word => word.Text == "W").ToList();
        var chromiumW = chromium.Pages.SelectMany(page => page.Words).Where(word => word.Text == "W").ToList();
        engineW.Select(word => (word.Page, word.Baseline)).Should().Equal(chromiumW.Select(word => (word.Page, word.Baseline)));
        chromiumW.Select(word => (word.Page, word.Baseline)).Should().Equal((2, 209.25), (3, 209.25 - AreaHeight));
    }

    [Fact]
    public void A_sliced_image_in_a_cell_is_drawn_as_Chromium_draws_it_and_the_next_row_comes_below_it()
    {
        // The row is cut after "x" on page 1 (the image moves past the cell's top line), the image is sliced over pages 2
        // to 4, as Chromium prints it. On page 4 Chromium draws the next row at the top of the page, over the last 48 pt
        // of the image; the engine places the row below the image (a deliberate deviation, docs/architecture.md).
        var engine = Printed(Body("a4-image-in-cell"));
        var chromium = PrintedPages.Chromium("a4-image-in-cell");

        engine.Pages.SelectMany(page => page.Images).Should().Equal(chromium.Pages.SelectMany(page => page.Images));
        var imageBottom = chromium.Pages[3].Images.Single().Y1;
        Baseline(chromium, "after").Should().BeLessThan(imageBottom, "Chromium draws the row over the slice");
        (Baseline(engine, "after") - 12).Should().BeGreaterThan(imageBottom, "the engine's row lies below the slice");
    }

    [Fact]
    public void Each_slice_is_clipped_to_its_part_of_the_page_area_and_every_slice_of_an_image_is_content_of_its_figure()
    {
        var pages = Run(Doc(MonolithicOverflow.Slice, Body("a4-image-first"))).Value.Pages;

        pages.Select(page => page.Items.OfType<ClipStart>().Single().Area).Should().Equal(
            new Rect(new Point(0, 12), new Size(300, 180)), new Rect(new Point(0, 12), new Size(300, 180)), new Rect(new Point(0, 12), new Size(300, 48)));
        pages.Should().AllSatisfy(page => page.Items.OfType<ArtifactStart>().Should().BeEmpty("each slice is content of the figure"));
        pages.Select(page => page.Items.OfType<StructureStart>().Count(start => start.Role is SemanticRole.FigureRole)).Should().Equal(1, 1, 1);
    }

    [Fact]
    public void Every_slice_of_a_line_after_the_first_is_an_artifact_so_its_text_is_read_once()
    {
        var pages = Run(Doc(MonolithicOverflow.Slice, Body("a4-line-after-text"))).Value.Pages;

        pages.Select(page => page.Items.OfType<ArtifactStart>().Count(start => start.Kind == ArtifactKind.SliceContinuation)).Should().Equal(0, 0, 1);
    }

    private static double Baseline(PrintedPages pages, string text) =>
        pages.Pages.SelectMany(page => page.Words).Single(word => word.Text == text).Baseline;
}
