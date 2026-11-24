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
            "a4-header-image-228" => [Table(header, Row(TallImage(228)))],
            "a4-header-row-image-228" => [Table(header, TableRow([Cell("r1")]), Row(TallImage(228)))],
            "a4-header-image-170" => [Table(header, Row(TallImage(170))), Paragraph("after")],
            "a4-header-image-174" => [Table(header, Row(TallImage(174))), Paragraph("after")],
            "a4-header-image-180" => [Table(header, Row(TallImage(180))), Paragraph("after")],
            "a4-header2-image-228" => [Table(HeaderRow([Cell("H0")]), HeaderRow([Cell("H1")]), Row(TallImage(228)))],
            "a4-header-unrepeated-image-228" => [Table(HeaderRow([Cell([Paragraph([Text("H0"), LineBreak(), Text("H1"), LineBreak(), Text("H2"), LineBreak(), Text("H3")])])]), Row(TallImage(228)))],
            _ => throw new ArgumentException(caseName, nameof(caseName)),
        };
    }

    private static Document Doc(
        string caseName, MonolithicOverflow overflow = MonolithicOverflow.Slice, EmptyRowParts parts = EmptyRowParts.Avoid, RepeatedHeaderRows rows = RepeatedHeaderRows.AboveEverySlice) =>
        Document([.. Body(caseName)], Page(300.Pt(), 204.Pt()).Margin(12.Pt()), title: "Title", language: "en")
            .FontFamily("Lato").LineHeight(15.Pt()).MonolithicOverflow(overflow).EmptyRowParts(parts).RepeatedHeaderRows(rows)
            .FontMetricRounding(FontMetricRounding.WholePixels);

    private static PageDocument Run(Document document)
    {
        var result = DocumentTranslator.ToModel(document).OnSuccessBind(model => DocumentLayout.Run(model, TestFonts.SetWithItalic));
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        return result.Value;
    }

    /// <summary>Where each image or the 200 pt line starts in the engine's pages: the page and top of its first slice, or
    /// of the whole image when it fits (an image's top edge as drawn, before its clip: a later slice is drawn higher than
    /// the page; for the line, the baseline of its first "W").</summary>
    private static List<(int Page, double Top)> FirstSlices(PageDocument document) =>
        [.. document.Pages.SelectMany((page, index) => page.Items.OfType<DrawImage>().Where(image => image.Area.Origin.Y >= 0).Select(image => (index + 1, image.Area.Origin.Y))),
         .. Words(PrintedPages.Engine(document))];

    /// <summary>The same in Chromium's print, from what it shows: Chromium draws a later slice moved up by the page area's
    /// height under its clip to the page area, so a later slice shows from the page area's top (12 pt), and a first slice,
    /// which starts below the header, from lower.</summary>
    private static List<(int Page, double Top)> FirstSlices(PrintedPages chromium) =>
        [.. chromium.Pages.SelectMany(page => page.Images.Where(image => image.Y0 > 12).Select(image => (page.Number, image.Y0))), .. Words(chromium)];

    private static IEnumerable<(int Page, double Top)> Words(PrintedPages pages) =>
        pages.Pages.SelectMany(page => page.Words.Where(word => word.Text == "W").Select(word => (page.Number, word.Baseline))).Take(1);

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
        var document = Run(Doc(caseName));
        var engine = PrintedPages.Engine(document);
        var chromium = PrintedPages.Chromium(caseName);

        FirstSlices(document).Should().Equal(FirstSlices(chromium));
        FirstSlices(document).Select(slice => slice.Page).Should().Equal(pages);
        engine.PageTexts.Take(pages[0]).Should().Equal(chromium.PageTexts.Take(pages[0]));
    }

    /// <summary>The cases whose page before the first slice the engine ends right below the header under
    /// <see cref="EmptyRowParts.Avoid"/>, with that page: on the table's first page at the top of the page, after a page
    /// that showed a body row, and on the page after an intro paragraph; and the bottom of the header rows there.</summary>
    public static TheoryData<string, int, double> PagesBelowTheHeader => new()
    {
        { "a4-header-image-row", 1, 28.5 },
        { "a4-header-image-228", 1, 28.5 },
        { "a4-header2-image-228", 1, 44.25 },
        { "a4-header-tall-line", 1, 28.5 },
        { "a4-header-two-image-rows", 1, 28.5 },
        { "a4-header-row-image-row", 2, 28.5 },
        { "a4-header-rows-image-row", 3, 28.5 },
        { "a4-intro-header-image-row", 2, 28.5 },
    };

    [Theory]
    [MemberData(nameof(PagesBelowTheHeader))]
    public void Under_EmptyRowParts_Allow_the_page_before_the_first_slice_ends_with_the_row_cut_empty_below_the_header(string caseName, int page, double header)
    {
        // Chromium never ends a page right below a header that repeats: it cuts the first body row below it, draws the
        // row's cells empty down to the page end (their side lines from the header to 192 pt, no line below them) and starts the
        // image or the 200 pt line in their part on the next page. Under Allow the engine paints that page as Chromium
        // does; under Avoid the page holds the header alone. The first slice is on Chromium's page, in the cells' part
        // there, which starts 0.375 pt higher than Chromium's, as every part of a cell that continues below a repeated
        // header does in the engine (a4-header-cut-row-image-row: "c10" on page 2 at 40.125 pt, Chromium 40.5 pt); a
        // second image (a4-header-two-image-rows) starts a row of its own, at Chromium's place.
        var allow = Run(Doc(caseName, parts: EmptyRowParts.Allow));
        var avoid = PrintedPages.Engine(Run(Doc(caseName)));
        var chromium = PrintedPages.Chromium(caseName);

        Painted(PrintedPages.Engine(allow).Pages[page - 1]).Should().Equal(Painted(chromium.Pages[page - 1]));
        Painted(chromium.Pages[page - 1]).Should().Contain(Cells(12, header, 12.75, 192)).And.Contain(Cells(287.25, header, 288, 192))
            .And.NotContain(Cells(12.75, 191.625, 287.25, 192));
        Painted(avoid.Pages[page - 1]).Should().NotContain(Cells(12, header, 12.75, 192));
        var slices = FirstSlices(chromium);
        FirstSlices(allow).Should().Equal([(slices[0].Page, slices[0].Top - 0.375), .. slices.Skip(1)]);
    }

    /// <summary>The cases of <see cref="RepeatedHeaderRows"/>: a piece in a body row below a repeated header row, sliced
    /// over pages that only show its rest (the first nine), or with a row still to come below it (the last two).</summary>
    public static TheoryData<string> RestPages => new()
    {
        "a4-header-image-228",
        "a4-header2-image-228",
        "a4-header-row-image-228",
        "a4-header-image-row",
        "a4-header-tall-line",
        "a4-header-image-170",
        "a4-header-image-174",
        "a4-header-image-180",
        "a4-header-rows-image-row",
        "a4-header-image-360-image",
        "a4-header-two-image-rows",
    };

    [Theory]
    [MemberData(nameof(RestPages))]
    public void Under_RepeatedHeaderRows_WhereContentIsLaidOut_a_page_that_only_shows_the_rest_of_a_piece_draws_no_header_rows(string caseName)
    {
        // Chromium lays the piece out whole below the header row of the page where it starts, also an image of 170, 174 or
        // 180 pt, no taller than the page area but taller than the 163.5 pt below the header, and lets it pass the page
        // end. It repeats the header row on every later page where the table still lays content out (in
        // a4-header-image-360-image and a4-header-two-image-rows: the pages of the first image, with the second row still
        // to come), and leaves it out on a page that only shows the rest of a piece laid out before: there the rest
        // starts at the top of the page area, 12 pt. Under WhereContentIsLaidOut the engine draws the header row on
        // Chromium's pages, and the rest on a page without it from 12 pt; under AboveEverySlice it draws the header row
        // above every slice, and a piece no taller than the page area goes on without it.
        var document = Run(Doc(caseName, parts: EmptyRowParts.Allow, rows: RepeatedHeaderRows.WhereContentIsLaidOut));
        var chromium = PrintedPages.Chromium(caseName);
        var above = PrintedPages.Engine(Run(Doc(caseName, parts: EmptyRowParts.Allow)));

        HeaderPages(PrintedPages.Engine(document)).Should().Equal(HeaderPages(chromium));
        HeaderPages(chromium).Should().Contain(false);
        HeaderPages(above).Should().NotEqual(HeaderPages(chromium));
        var rest = HeaderPages(chromium).Select((header, index) => (header, index)).Where(page => !page.header).Select(page => page.index).ToList();
        rest.SelectMany(index => document.Pages[index].Items.OfType<ClipStart>()).Select(clip => clip.Area.Origin.Y).Should().AllBeEquivalentTo(12.0);
        rest.SelectMany(index => chromium.Pages[index].Images).Select(image => image.Y0).Should().AllBeEquivalentTo(12.0);
    }

    /// <summary>Per page, whether it shows a header row ("H", "H0").</summary>
    private static List<bool> HeaderPages(PrintedPages pages) =>
        [.. pages.Pages.Select(page => page.Words.Any(word => word.Text is "H" or "H0"))];

    [Fact]
    public void Under_EmptyRowParts_Allow_a_header_that_does_not_repeat_still_ends_its_page_alone()
    {
        // Case a4-header-unrepeated-image-228: a header of four lines (61.5 pt) is more than a quarter of the page area and
        // is shown on page 1 only. Chromium ends page 1 with the header alone and starts the row on page 2, cut before the
        // image below the grid line above it (a blank strip, as without a header: a4-noheader-image-228), with the image
        // from page 3. The engine does the same under both values: nothing below the header on page 1, the strip from the
        // top of page 2 to its end, the image where Chromium draws it.
        const string caseName = "a4-header-unrepeated-image-228";
        var chromium = PrintedPages.Chromium(caseName);
        foreach (var parts in new[] { EmptyRowParts.Avoid, EmptyRowParts.Allow })
        {
            var engine = PrintedPages.Engine(Run(Doc(caseName, parts: parts)));

            Painted(engine.Pages[0]).Should().NotContain(Cells(12, 73.5, 12.75, 74.25), parts.ToString());
            Painted(engine.Pages[1]).Should().Contain(Cells(12, 12, 12.75, 192), parts.ToString());
            engine.Pages.Select(page => page.Images).Should().BeEquivalentTo(chromium.Pages.Select(page => page.Images), options => options.WithStrictOrdering(), parts.ToString());
        }

        Painted(chromium.Pages[0]).Should().NotContain(Cells(12, 73.5, 12.75, 74.25));
        Painted(chromium.Pages[1]).Should().Contain(Cells(12, 12, 12.75, 192));
    }

    /// <summary>The area a page's rectangles paint, as the cells of a grid of 0.375 pt that they cover (every edge in
    /// these cases lies on it: a 0.75 pt line and its centre).</summary>
    private static List<(int X, int Y)> Painted(PrintedPage page) =>
        [.. page.Rectangles.SelectMany(box => Cells(box.X0, box.Y0, box.X1, box.Y1)).Distinct().Order()];

    private static IEnumerable<(int X, int Y)> Cells(double x0, double y0, double x1, double y1) =>
        Enumerable.Range(Step(x0), Step(x1) - Step(x0)).SelectMany(x => Enumerable.Range(Step(y0), Step(y1) - Step(y0)).Select(y => (x, y)));

    private static int Step(double value) => (int)Math.Round(value / 0.375);

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
        var first = FirstSlices(document)[0].Page;

        var clips = document.Pages.Skip(first - 1).SelectMany(page => page.Items.OfType<ClipStart>()).Select(clip => clip.Area).ToList();
        clips.Sum(area => area.Size.Height).Should().BeApproximately(408, 1e-9);
        clips.Select(area => area.Origin.Y).Should().Equal([28.5, .. Enumerable.Repeat(28.125, clips.Count - 1)]);
        engine.Pages.Skip(first - 1).Take(clips.Count).Should().AllSatisfy(page => page.Words.Select(word => word.Text).Should().Contain("H"));
        chromium.Pages.Skip(first).Take(clips.Count - 1).SelectMany(page => page.Images).Select(image => (image.Y0, image.Y1))
            .Should().Equal(Enumerable.Range(1, clips.Count - 1).Select(k => (12.0, Math.Min(192, 28.5 - (180.0 * k) + 408))),
                "Chromium moves the image a whole page area per page (drawn from 28.5 − 180 k), and its clip shows it inside the page area");
        chromium.Pages.Skip(first).Take(clips.Count - 1).Select(page => page.Words.Any(word => word.Text == "H"))
            .Should().AllBeEquivalentTo(caseName == "a4-header-row-image-row");
    }

    [Fact]
    public void A_line_spanning_two_rows_beside_an_image_under_an_empty_header_is_printed_whole_on_the_first_page()
    {
        // Case a4-header-empty-rowspan-line-image: two columns, an empty repeated header row, then "t20" in a cell over two
        // rows beside an empty cell, and a 345 pt image in the second row. "t20" is printed whole on page 1, page 2 holds
        // only the header and the grid, and the image is sliced from page 3, as in Chromium: the same pages and words, "t20"
        // at Chromium's baseline, the image on page 3 at Chromium's height (0.375 pt further left). On page 4 the engine
        // shows the rest of the image, 166.5 pt, and Chromium moves the image up a whole page area (the listed deviation
        // above). This line was once sliced 0.375 + 14.625 pt over pages 2 and 3.
        var table = Elements.Table(Columns(Fill(1), Fill(1)), [HeaderRow([Cell(""), Cell("")]), TableRow([Cell("t20").RowSpan(2), Cell("")]), TableRow([Cell([TallImage(345)])])])
            .GridLines(0.75.Pt(), Color.Black).SplitLinesAtBreaks();
        var document = Run(Document([table], Page(300.Pt(), 204.Pt()).Margin(12.Pt()), title: "Title", language: "en")
            .FontFamily("Lato").LineHeight(15.Pt()).MonolithicOverflow(MonolithicOverflow.Slice).FontMetricRounding(FontMetricRounding.WholePixels));
        var engine = PrintedPages.Engine(document);
        var chromium = PrintedPages.Chromium("a4-header-empty-rowspan-line-image");

        engine.PageTexts.Should().Equal(chromium.PageTexts);
        engine.Pages[0].Words.Single().Should().Be(chromium.Pages[0].Words.Single());
        var (image, chromiumImage) = (engine.Pages[2].Images.Single(), chromium.Pages[2].Images.Single());
        (image.Y0, image.Y1).Should().Be((chromiumImage.Y0, chromiumImage.Y1));
        image.X0.Should().Be(chromiumImage.X0 - 0.375, "the engine starts the second column's content half the 0.75 pt grid line earlier");
        document.Pages[0].Items.OfType<ClipStart>().Should().BeEmpty("the line is not sliced");
        var clips = document.Pages.Skip(2).SelectMany(page => page.Items.OfType<ClipStart>()).Select(clip => clip.Area.Size.Height).ToList();
        clips.Should().Equal(178.5, 166.5);
        (chromium.Pages[3].Images.Single().Y0, chromium.Pages[3].Images.Single().Y1).Should().Be((12, chromium.Pages[2].Images.Single().Y0 - 180 + 345),
            "Chromium moves the image up a whole page area and shows it inside the page area");
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
        var firsts = FirstSlices(document);
        var lastOfFirst = document.Pages.Select((page, index) => (page, index)).Last(pair => pair.index < firsts[1].Page - 1
            && pair.page.Items.OfType<ClipStart>().Any()).index + 1;

        firsts[1].Page.Should().Be(lastOfFirst + 1);
        firsts[1].Top.Should().Be(28.5);
        FirstSlices(PrintedPages.Chromium(caseName)).Should().Equal([(2, 28.5), (5, 28.5)]);
    }

    /// <summary>The cases of a row whose other cell draws something below a repeated header row ("H", "G"): a 228 pt
    /// image in its first cell, beside it six, fourteen or thirty lines, one line, or an empty cell with a background, the
    /// row as the second body row, the image's own cell with a background in a table of one column, and the image in a
    /// table nested in the cell.</summary>
    private static Element[] Beside(string caseName)
    {
        var header = HeaderRow([Cell("H"), Cell("G")]);
        var lines = (string prefix, int count) => Cell([Paragraph([.. Enumerable.Range(0, count).SelectMany(i => i == 0 ? new[] { Text($"{prefix}0") } : [LineBreak(), Text($"{prefix}{i}")])])]);
        var image = Cell([TallImage(228)]);
        var table = (TablePart[] rows) => Elements.Table(Columns(Fill(1), Fill(1)), [.. rows]).GridLines(0.75.Pt(), Color.Black).SplitLinesAtBreaks();
        return caseName switch
        {
            "a4-header-image-228-beside-fit" => [table([header, TableRow([image, lines("b", 6)])]), Paragraph("after")],
            "a4-header-image-228-beside-two-pages" => [table([header, TableRow([image, lines("b", 14)])]), Paragraph("after")],
            "a4-header-image-beside-lines" => [table([header, TableRow([image, lines("p", 30)])])],
            "a4-header-image-228-beside-line" => [table([header, TableRow([image, Cell("beside")])]), Paragraph("after")],
            "a4-header-image-228-background-beside" => [table([header, TableRow([image, Cell("").Background(new Color(0xC0, 0xC0, 0xC0))])]), Paragraph("after")],
            "a4-header-row-image-228-beside-line" => [table([header, TableRow([Cell("r1"), Cell("s1")]), TableRow([image, Cell("beside")])]), Paragraph("after")],
            "a4-header-image-228-background" => [Table(HeaderRow([Cell("H")]), TableRow([Cell([TallImage(228)]).Background(new Color(0xC0, 0xC0, 0xC0))])), Paragraph("after")],
            "a4-header-nested-image-228" => [Table(HeaderRow([Cell("H")]), Row(Table(Row(TallImage(228))))), Paragraph("after")],
            _ => throw new ArgumentException(caseName, nameof(caseName)),
        };
    }

    /// <summary>A case of <see cref="Beside"/> under the HTML front-end's values of the policies, or under
    /// <paramref name="parts"/>.</summary>
    private static PageDocument BesideRun(string caseName, EmptyRowParts parts = EmptyRowParts.Allow) =>
        Run(Document([.. Beside(caseName)], Page(300.Pt(), 204.Pt()).Margin(12.Pt()), title: "Title", language: "en")
            .FontFamily("Lato").FontSize(10.5.Pt()).LineHeight(15.Pt()).MonolithicOverflow(MonolithicOverflow.Slice).EmptyRowParts(parts)
            .RepeatedHeaderRows(RepeatedHeaderRows.WhereContentIsLaidOut).CellsBesideSlices(CellsBesideSlices.Below).SliceRestEnd(SliceRestEnd.AtLineCentre)
            .FontMetricRounding(FontMetricRounding.WholePixels));

    /// <summary>The cases of <see cref="Beside"/>, with the pages on which both print the same words (in
    /// <c>a4-header-image-beside-lines</c> the header over the rest on page 3, the listed limit, moves a line beside it on).</summary>
    public static TheoryData<string, int> BesideCases => new()
    {
        { "a4-header-image-228-beside-fit", 3 },
        { "a4-header-image-228-beside-two-pages", 3 },
        { "a4-header-image-beside-lines", 2 },
        { "a4-header-image-228-beside-line", 3 },
        { "a4-header-image-228-background-beside", 3 },
        { "a4-header-row-image-228-beside-line", 4 },
        { "a4-header-image-228-background", 3 },
        { "a4-header-nested-image-228", 3 },
    };

    [Theory]
    [MemberData(nameof(BesideCases))]
    public void Under_EmptyRowParts_Allow_a_row_cut_below_a_repeated_header_draws_its_other_cells_and_starts_the_image_on_the_next_page(string caseName, int same)
    {
        // Chromium never ends a page with a repeated header alone, also where another cell of the first body row draws
        // something there (text, a background): it starts the row below the header, draws what the other cells hold as far
        // as the page reaches, the image's cell empty down to the page end, and starts the image in that cell's part on the
        // next page, below the repeated header, beside the rest of the text; the image is not moved on again. On the page
        // of the image's rest it repeats the header where the row has a cell beside the image, which goes on, empty, to the
        // row's end, and not in a table of one column. One level down the same: a table nested in the cut cell starts on the
        // page of the cut, its top line and its row cut empty below it, and its image starts on the next page
        // (a4-header-nested-image-228, three pages). The engine prints the same pages and words, the image's first slice
        // on Chromium's page, 0.375 pt higher (the centre of the line below the header), and the header on Chromium's pages;
        // under Avoid it ends the page before the image with the header alone, without the side lines below it.
        var document = BesideRun(caseName);
        var engine = PrintedPages.Engine(document);
        var chromium = PrintedPages.Chromium(caseName);

        engine.Pages.Length.Should().Be(chromium.Pages.Length);
        engine.PageTexts.Take(same).Should().Equal(chromium.PageTexts.Take(same));
        var slices = FirstSlices(chromium);
        FirstSlices(document).Should().Equal([(slices[0].Page, slices[0].Top - 0.375)]);
        HeaderPages(engine).Should().Equal(HeaderPages(chromium));
        var before = slices[0].Page - 2;
        var strip = Cells(12, 28.5, 12.75, 192);
        Painted(chromium.Pages[before]).Should().Contain(strip, "Chromium starts the row on the page before the image");
        Painted(engine.Pages[before]).Should().Contain(strip);
        Painted(PrintedPages.Engine(BesideRun(caseName, EmptyRowParts.Avoid)).Pages[before]).Should().NotContain(strip);
    }

    [Theory]
    [InlineData("a4-header-image-228-beside-fit", 0.375)]
    [InlineData("a4-header-image-228-beside-line", 0.375)]
    [InlineData("a4-header-image-228-background-beside", 0.375)]
    [InlineData("a4-header-image-228-background", 0)]
    public void Under_SliceRestEnd_AtLineCentre_a_cell_beside_the_rest_that_has_nothing_left_keeps_its_half_of_the_line_below(string caseName, double centre)
    {
        // On the page of the image's rest, the line below the row is centred on the rest's end in a table of one column
        // (75.75 to 76.5 pt below a rest that ends at 76.125 pt), and half its width lower where a cell beside the image
        // goes on with nothing left: that cell's part keeps its half (76.5 to 77.25 pt in Chromium's print, where the rest
        // is drawn to 76.5 pt). In the engine the line's centre lies that far below the rest's end, which Chromium's
        // print shows on its whole pixels.
        var engine = PrintedPages.Engine(BesideRun(caseName));
        var chromium = PrintedPages.Chromium(caseName);
        var line = (PrintedPage page) => page.Rectangles.Where(box => box.Colour == "#000000" && box.X1 - box.X0 > 100 && box.Y0 > page.Images.Single().Y1 - 1).MinBy(box => box.Y0)!;
        var (ours, theirs) = (engine.Pages[^1], chromium.Pages[^1]);

        ((line(ours).Y0 + line(ours).Y1) / 2).Should().BeApproximately(ours.Images.Single().Y1 + centre, 1e-6);
        line(theirs).Y0.Should().Be(ChromiumLayout.WholePixel(76.125 + centre - 0.375));
        theirs.Images.Single().Y1.Should().Be(76.5);
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
