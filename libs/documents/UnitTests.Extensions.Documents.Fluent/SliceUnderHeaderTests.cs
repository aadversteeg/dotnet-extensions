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
/// Content taller than the page area in a table with a repeated header row (<see cref="MonolithicOverflow.Slice"/>),
/// against Chromium's print (<c>tools/reference/chromium</c>, cases <c>a4-header-*</c>, <c>a4-intro-header-image-row</c>,
/// <c>a4-two-image-rows</c>, <c>a4-row-image-row</c>). THE RULE: under a repeated header row, content taller than the page
/// area is sliced right below the header, on a page after one that showed no body row other than slices of a piece
/// that was itself sliced; after a page that did show a body row, the page holds only the header and the row moves on
/// once. That is where Chromium starts the first slice, page and top, in every measured case. Page area 276 × 180 pt,
/// Lato 12 pt, line height 15 pt, fixed table layout, 0.75 pt grid lines, all whole pixels, so positions compare exactly.
/// </summary>
public class SliceUnderHeaderTests
{
    private static byte[] Tall => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "chromium", "tall.png"));

    private static ImageElement TallImage(double height = 408) => Image(Tall).Alt("tall").Height(height.Pt());

    private static TableRowElement Row(Element content) => TableRow([Cell([content])]);

    private static TableElement Table(params TablePart[] rows) =>
        Elements.Table(Columns(Fill(1)), [.. rows]).GridLines(0.75.Pt(), Color.Black).SplitLinesAtBreaks();

    private static TablePart[] Rows(string prefix, int count) => [.. Enumerable.Range(0, count).Select(i => (TablePart)TableRow([Cell($"{prefix}{i}")]))];

    private static Element[] Body(string caseName)
    {
        var header = HeaderRow([Cell("H")]);
        var line = Paragraph("W").FontSize(200.Pt()).LineHeight(240.Pt());
        return caseName switch
        {
            "a4-header-image-row" => [Table(header, Row(TallImage())), Paragraph("after")],
            "a4-header-row-image-row" => [Table(header, TableRow([Cell("r1")]), Row(TallImage()), TableRow([Cell("r3")])), Paragraph("after")],
            "a4-header-tall-line" => [Table(header, Row(line)), Paragraph("after")],
            "a4-header-rows-image-row" => [Table([header, .. Rows("r", 12), Row(TallImage())]), Paragraph("after")],
            "a4-header-rows-fill-image-row" => [Table([header, .. Rows("r", 10), Row(TallImage())]), Paragraph("after")],
            "a4-header-two-image-rows" => [Table(header, Row(TallImage()), Row(TallImage())), Paragraph("after")],
            "a4-intro-header-image-row" => [Paragraph("intro"), Table(header, Row(TallImage())), Paragraph("after")],
            "a4-header-cut-row-image-row" => [Table(header, TableRow([Cell([Paragraph([.. Enumerable.Range(0, 15).SelectMany(i => i == 0 ? new[] { Text("c0") } : [LineBreak(), Text($"c{i}")])])])]), Row(TallImage())), Paragraph("after")],
            "a4-header-image-row-image" => [Table(header, Row(TallImage()), TableRow([Cell("r")]), Row(TallImage())), Paragraph("after")],
            "a4-header-image-360-image" => [Table(header, Row(TallImage(360)), Row(TallImage())), Paragraph("after")],
            "a4-header-image-150-image" => [Table(header, Row(TallImage(150)), Row(TallImage())), Paragraph("after")],
            "a4-two-image-rows" => [Table(Row(TallImage()), Row(TallImage())), Paragraph("after")],
            "a4-row-image-row" => [Table(TableRow([Cell("r1")]), Row(TallImage())), Paragraph("after")],
            _ => throw new ArgumentException(caseName, nameof(caseName)),
        };
    }

    private static Document Doc(string caseName, MonolithicOverflow overflow = MonolithicOverflow.Slice, EmptyRowParts parts = EmptyRowParts.Avoid) =>
        Document([.. Body(caseName)], Page(300.Pt(), 204.Pt()).Margin(12.Pt()), title: "Title", language: "en")
            .FontFamily("Lato").LineHeight(15.Pt()).MonolithicOverflow(overflow).EmptyRowParts(parts).FontMetricRounding(FontMetricRounding.WholePixels);

    private static PageDocument Run(Document document)
    {
        var result = DocumentTranslator.ToModel(document).OnSuccessBind(model => DocumentLayout.Run(model, TestFonts.SetWithItalic));
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        return result.Value;
    }

    /// <summary>Where each image or the 200 pt line starts: the page and top of its first slice, or of the whole image
    /// when it fits (an image's top edge; for the line, the baseline of its first "W").</summary>
    private static List<(int Page, double Top)> FirstSlices(PrintedPages pages) =>
        [.. pages.Pages.SelectMany(page => page.Images.Where(image => image.Y0 >= 0).Select(image => (page.Number, image.Y0))),
         .. pages.Pages.SelectMany(page => page.Words.Where(word => word.Text == "W").Select(word => (page.Number, word.Baseline))).Take(1)];

    /// <summary>The cases with a header row, with the pages Chromium starts their pieces on.</summary>
    public static TheoryData<string, int[]> HeaderCases => new()
    {
        { "a4-header-image-row", [2] },
        { "a4-header-row-image-row", [3] },
        { "a4-header-tall-line", [2] },
        { "a4-header-rows-image-row", [4] },
        { "a4-header-rows-fill-image-row", [3] },
        { "a4-intro-header-image-row", [3] },
        { "a4-header-cut-row-image-row", [4] },
        { "a4-header-image-150-image", [1, 3] }, // the 150 pt image fits whole on page 1, below the header
        { "a4-header-image-row-image", [2, 6] },
        { "a4-header-two-image-rows", [2, 5] },
        { "a4-header-image-360-image", [2, 5] },
    };

    [Theory]
    [MemberData(nameof(HeaderCases))]
    public void Under_a_repeated_header_row_the_first_slice_starts_where_Chromium_starts_it(string caseName, int[] pages)
    {
        // Page and top of every piece's first slice: right below the header (an image at 28.5 pt, the line's baseline at
        // 225.75 pt), on the page after the one that showed only the header, or only slices of the piece before. The
        // pages up to the first slice hold the same words as Chromium's.
        var engine = PrintedPages.Engine(Run(Doc(caseName)));
        var chromium = PrintedPages.Chromium(caseName);

        FirstSlices(engine).Should().Equal(FirstSlices(chromium));
        FirstSlices(engine).Select(slice => slice.Page).Should().Equal(pages);
        engine.PageTexts.Take(pages[0]).Should().Equal(chromium.PageTexts.Take(pages[0]));
    }

    [Theory]
    [InlineData("a4-header-image-row")]
    [InlineData("a4-header-row-image-row")]
    [InlineData("a4-header-rows-image-row")]
    public void Under_a_repeated_header_row_every_later_slice_lies_below_the_header_and_the_slices_show_the_image_once(string caseName)
    {
        // A deliberate difference from Chromium, pinned. The engine repeats the header above every later slice, as on every
        // other page of the table, and each slice is as tall as the room below it, so the slices show the 408 pt image
        // whole and once: 163.5 pt on the first page (below the header and the line under it), then 163.875 pt (a
        // continued cell has no half line above it), then the rest. Chromium moves the image up a whole page area (180 pt)
        // on each later page although the header takes 16.5 pt there: in a4-header-row-image-row it repeats the header,
        // and 16.5 pt of the image under it are never shown; in a4-header-image-row and a4-header-rows-image-row it leaves
        // the header out on those pages instead.
        var document = Run(Doc(caseName));
        var engine = PrintedPages.Engine(document);
        var chromium = PrintedPages.Chromium(caseName);
        var first = FirstSlices(engine)[0].Page;

        var clips = document.Pages.Skip(first - 1).SelectMany(page => page.Items.OfType<ClipStart>()).Select(clip => clip.Area).ToList();
        clips.Sum(area => area.Size.Height).Should().BeApproximately(408, 1e-9);
        clips.Select(area => area.Origin.Y).Should().Equal([28.5, .. Enumerable.Repeat(28.125, clips.Count - 1)]);
        engine.Pages.Skip(first - 1).Take(clips.Count).Should().AllSatisfy(page => page.Words.Select(word => word.Text).Should().Contain("H"));
        chromium.Pages.Skip(first).Take(clips.Count - 1).SelectMany(page => page.Images).Select(image => image.Y0)
            .Should().Equal(Enumerable.Range(1, clips.Count - 1).Select(k => 28.5 - (180.0 * k)), "Chromium moves the image a whole page area per page");
        chromium.Pages.Skip(first).Take(clips.Count - 1).Select(page => page.Words.Any(word => word.Text == "H"))
            .Should().AllBeEquivalentTo(caseName == "a4-header-row-image-row");
    }

    /// <summary>A table with a one-line repeated header (15 pt) and one cell holding <paramref name="piece"/>, then
    /// "after", on a page area of 144 pt: 129 pt of room below the header.</summary>
    private static Document UnderOneLineHeader(Element piece, EmptyRowParts parts) =>
        Document(
            [Elements.Table(Columns(Fill(1)), [HeaderRow([Cell("H")]), Row(piece)]), Paragraph("after")],
            Page(300.Pt(), 204.Pt()).Margins(new Edges(12.Pt(), 12.Pt(), 48.Pt(), 12.Pt())), title: "Title", language: "en")
            .FontFamily("Lato").LineHeight(15.Pt()).MonolithicOverflow(MonolithicOverflow.Slice).EmptyRowParts(parts);

    private static Element Piece(string kind, double height) =>
        kind == "line" ? Paragraph("W").FontSize(12.Pt()).LineHeight(height.Pt()) : TallImage(height);

    [Theory]
    [InlineData("line", EmptyRowParts.Avoid)]
    [InlineData("line", EmptyRowParts.Allow)]
    [InlineData("image", EmptyRowParts.Avoid)]
    [InlineData("image", EmptyRowParts.Allow)]
    public void Under_a_repeated_header_row_a_last_slice_taller_than_the_room_below_the_header_still_lies_below_the_header(string kind, EmptyRowParts parts)
    {
        // 388 pt in slices of 129 pt (the room below the header) leaves 130 pt for the last one: more than the room below
        // the header, less than the page area. That slice too lies below the header, so the piece takes four slices, each
        // at 27 pt under "H"; a last slice of 1 pt follows on the page with "after". A line's later slices draw no ink
        // (its glyph sits in the middle of the line box), so under EmptyRowParts.Avoid the cut between them is a last
        // resort; the header stays above them all the same.
        var document = Run(UnderOneLineHeader(Piece(kind, 388), parts));
        var engine = PrintedPages.Engine(document);
        var clips = document.Pages.Select((page, index) => (Page: index + 1, Clips: page.Items.OfType<ClipStart>().Select(clip => clip.Area).ToList()))
            .Where(page => page.Clips.Count > 0).ToList();

        clips.Select(page => page.Clips.Count).Should().AllBeEquivalentTo(1);
        clips.Sum(page => page.Clips[0].Size.Height).Should().BeApproximately(388, 1e-9);
        clips.Select(page => page.Clips[0].Size.Height).Should().Equal(129, 129, 129, 1);
        clips.Select(page => page.Clips[0].Origin.Y).Should().AllBeEquivalentTo(27.0);
        clips.Select(page => engine.Pages[page.Page - 1].Words.Select(word => word.Text)).Should().AllSatisfy(words => words.Should().Contain("H"));
        engine.Pages[^1].Words.Select(word => word.Text).Should().Contain("after");
    }

    [Fact]
    public void Under_a_repeated_header_row_the_slice_limit_is_met_alike_under_both_values_of_EmptyRowParts()
    {
        // A 12,901 pt line in slices of 129 pt needs 101 slices: one more than MonolithicSlices.MaxSlices, whatever the
        // value of EmptyRowParts, since every slice lies below the header under both.
        foreach (var parts in new[] { EmptyRowParts.Avoid, EmptyRowParts.Allow })
        {
            var result = DocumentTranslator.ToModel(UnderOneLineHeader(Piece("line", 12901), parts))
                .OnSuccessBind(model => DocumentLayout.Run(model, TestFonts.SetWithItalic));

            result.IsFailure.Should().BeTrue(parts.ToString());
            result.Error.Code.Should().Be(LayoutErrorCodes.TooManySlices, parts.ToString());
        }
    }

    [Theory]
    [InlineData("a4-header-two-image-rows")]
    [InlineData("a4-header-image-360-image")]
    public void A_second_image_under_the_header_is_sliced_on_the_page_after_the_first_image_s_last_slice(string caseName)
    {
        // The page before the second image showed only slices of the first, so no page with only the header comes between
        // them: the second image's first slice is on the page right after the first image's last slice, below the header.
        // Chromium: the first image on pages 2 to 4, the second from page 5 at 28.5 pt; the engine the same pages here,
        // though its later pages are its own (the header above every slice).
        var document = Run(Doc(caseName));
        var engine = PrintedPages.Engine(document);
        var firsts = FirstSlices(engine);
        var lastOfFirst = document.Pages.Select((page, index) => (page, index)).Last(pair => pair.index < firsts[1].Page - 1
            && pair.page.Items.OfType<ClipStart>().Any()).index + 1;

        firsts[1].Page.Should().Be(lastOfFirst + 1);
        firsts[1].Top.Should().Be(28.5);
        FirstSlices(PrintedPages.Chromium(caseName)).Should().Equal([(2, 28.5), (5, 28.5)]);
    }

    [Theory]
    [InlineData("a4-two-image-rows")]
    [InlineData("a4-row-image-row")]
    public void Without_a_header_row_an_image_row_moves_to_a_blank_page_first_as_in_Chromium(string caseName)
    {
        // Without a header the image's cell starts under the grid line above it, so it does not start the page: the row is
        // cut before the image as a last resort, a blank page, and the image is sliced from the top of the next. Chromium
        // prints the same pages and image rectangles.
        var engine = PrintedPages.Engine(Run(Doc(caseName)));
        var chromium = PrintedPages.Chromium(caseName);

        engine.PageTexts.Should().Equal(chromium.PageTexts);
        engine.Pages.Select(page => page.Images).Should().BeEquivalentTo(chromium.Pages.Select(page => page.Images), options => options.WithStrictOrdering());
    }

    [Theory]
    [InlineData("a4-header-image-row")]
    [InlineData("a4-header-tall-line")]
    public void With_the_default_overflow_a_piece_taller_than_the_page_under_a_header_is_refused(string caseName)
    {
        var result = DocumentTranslator.ToModel(Doc(caseName, MonolithicOverflow.Refuse)).OnSuccessBind(model => DocumentLayout.Run(model, TestFonts.SetWithItalic));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(LayoutErrorCodes.DoesNotFit);
    }
}
