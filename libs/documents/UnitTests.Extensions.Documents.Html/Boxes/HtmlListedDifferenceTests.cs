using System.IO;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;

namespace UnitTests.Extensions.Documents.Html.Boxes;

/// <summary>
/// HTML cases that differ from Chromium by a limit of layout, each pinned with Chromium's print here and listed in the
/// status file (<see cref="HtmlCases.Listed"/>) and in docs/architecture.md, "Known limits".
/// </summary>
public class HtmlListedDifferenceTests
{
    private static PrintedPages Chromium(string name) =>
        PrintedPages.Parse(File.ReadAllText(Path.Combine(HtmlCases.Folder(), "..", "measured", name + ".print.txt")));

    private static PrintedPages Engine(string name)
    {
        var boxes = HtmlCases.Boxes(name).Value;
        return PrintedPages.Engine(DocumentLayout.Run(boxes.Document, boxes.Fonts).Value);
    }

    /// <summary>
    /// Chromium shrinks the whole print when content is wider than the page area: by the widest page's overflow, the
    /// right edge of its content over the page area's width, up to a factor of 1.5 (<c>CalculateOverflowShrinkForPrinting</c>
    /// in pagination_utils.cc, applied by <c>LocalFrameView::ForceLayoutForPagination</c>; the limit is the setting
    /// <c>printingMaximumShrinkFactor</c>, settings.json5). Past the limit the content is clipped. The engine has no page
    /// scale: it prints the content at its size and lets it stick out of the page area. Below the limit the scale is the
    /// whole difference: Chromium's print taken back through it about the page area's top left corner (each position and
    /// font size times the factor) is the engine's, compared as every case is (<see cref="HtmlCases.Difference"/>).
    /// </summary>
    /// <param name="name">The case.</param>
    /// <param name="left">The left edge of the page area, in points, at Chromium's whole-pixel margin.</param>
    /// <param name="top">The top edge of the page area.</param>
    /// <param name="width">The width of the page area.</param>
    [Theory]
    [InlineData("h-wide-word-little", 9.75, 9.75, 280.5)]
    [InlineData("h-wide-word-much", 9.75, 9.75, 280.5)]
    [InlineData("hw-nowrap", 12, 12, 276)]
    public void Content_wider_than_the_page_area_is_not_shrunk_as_Chromium_shrinks_the_page(string name, double left, double top, double width)
    {
        var (engine, chromium) = (Engine(name), Chromium(name));
        var right = engine.Pages.Single().Words.Max(word => word.X1);
        right.Should().BeGreaterThan(left + width, "the engine lets the content stick out");

        var scale = System.Math.Min(1.5, (right - left) / width);
        chromium.Pages.Single().Words.Zip(engine.Pages.Single().Words).Should().OnlyContain(pair => System.Math.Abs(pair.First.FontSize - (pair.Second.FontSize / scale)) < 0.005,
            "Chromium prints every word of the page shrunk by one factor");
        if (scale < 1.5)
        {
            var unscaled = new PrintedPages([.. chromium.Pages.Select(page => page with
            {
                Words = [.. page.Words.Select(word => word with
                {
                    X0 = left + ((word.X0 - left) * scale),
                    X1 = left + ((word.X1 - left) * scale),
                    Baseline = top + ((word.Baseline - top) * scale),
                    FontSize = word.FontSize * scale,
                })],
            })]);
            HtmlCases.Difference(engine, unscaled).Should().BeNull("the scale is the whole difference");
        }
    }

    /// <summary>
    /// A4 without margins (the default page): Chromium's page area is 794 x 1123 px (595.5 x 842.25 pt), the page size
    /// rounded up to whole pixels, and reaches past the sheet it prints, 594.96 x 841.92 pt; the model holds no page area
    /// larger than the sheet, so the engine's ends at the sheet. Text and backgrounds end 0.54 pt short on the right, and
    /// 64 lines of 1123/64 px, which fill Chromium's page, need two. A right margin of 0.71 px (0.53 pt) or more and a
    /// bottom margin of 0.52 px (0.39 pt) or more bring the area inside the sheet: then both agree (case
    /// <c>h-page-a4-margin-px</c>, 1 px margins, equal).
    /// </summary>
    [Fact]
    public void The_page_area_of_A4_without_margins_reaches_past_the_sheet_where_the_engine_ends_it_at_the_sheet()
    {
        var (engine, chromium) = (Engine("h-page-a4-zero-right").Pages.Single(), Chromium("h-page-a4-zero-right").Pages.Single());
        (chromium.Width, engine.Width).Should().Be((594.96, 594.96));
        chromium.Rectangles.Single().X1.Should().Be(595.2, "Chromium draws it to 595.5, its page area 794 px wide, under a clip that ends at 595.2, past the sheet");
        engine.Rectangles.Single().X1.Should().BeApproximately(594.96, 1e-6, "the engine's ends at the sheet");

        LineEnds(Chromium("h-page-a4-zero-justify"))[0].Should().BeApproximately(595.5, 0.01);
        LineEnds(Engine("h-page-a4-zero-justify"))[0].Should().BeApproximately(594.96, 0.01);

        Chromium("h-page-a4-zero-lines").Pages.Should().ContainSingle("64 lines of 1123/64 px fill 1123 px");
        Engine("h-page-a4-zero-lines").Pages.Should().HaveCount(2, "the engine's page area is 1122.56 px high");
    }

    /// <summary>
    /// A ligature across the border of an inline element in the same font: Chromium forms it and its print maps the glyph
    /// to the first run's characters only (<c>f&lt;span&gt;i&lt;/span&gt;x</c> reads "fx"); the engine's glyph keeps every
    /// character it shows, so the text can be copied and searched (docs/architecture.md, the rule for shaping across runs).
    /// With Chromium's two words read as the engine's, the case is equal: the text is the whole difference.
    /// </summary>
    [Fact]
    public void A_ligature_across_the_border_of_an_inline_element_keeps_every_character_where_Chromium_keeps_only_those_of_the_first_run()
    {
        var (engine, chromium) = (Engine("sr-same-font"), Chromium("sr-same-font"));
        chromium.Pages.Single().Text.Should().Contain("fix fx office ofce");
        engine.Pages.Single().Text.Should().NotContain("fx").And.NotContain("ofce");
        HtmlCases.Difference(engine, Read(chromium, text => text switch { "fx" => "fix", "ofce" => "office", _ => text })).Should().BeNull();
    }

    /// <summary>
    /// A zero width non-joiner (U+200C) in a word: Chromium's print keeps it in the PDF's text, the engine's PDF does not
    /// (it draws no glyph for it; its PDF read back with <c>positions.py</c> reads "To", "T" "o", "T" "o" "T" for case
    /// <c>sr-zwnj</c>). With the non-joiners taken out of Chromium's words the case is equal, positions included, but for
    /// one fact of the read-back: the end of a word that ends in the non-joiner. Lato's own advance for U+200C is 0, but
    /// Chromium's PDF lists widths only for the glyphs of "T" and "o" and gives every other glyph its default width
    /// (<c>/DW 256</c>, 3.072 pt at 12 pt), so PyMuPDF reads the non-joiner as a character 3.072 pt wide at its own origin:
    /// one that leads a word starts where the "o" starts and ends inside it (the word is 19.090 to 25.894, as wide as
    /// "o"), and one that ends a word starts after the kerned "o" (22.947) and reaches 26.019, past the next "T" at 22.957.
    /// That end is left out; every other fact of the case is compared.
    /// </summary>
    [Fact]
    public void A_zero_width_non_joiner_is_not_in_the_text_where_Chromium_keeps_it()
    {
        var (engine, chromium) = (Engine("sr-zwnj"), Chromium("sr-zwnj"));
        chromium.Pages.Single().Words.Select(word => word.Text).Should().Equal("T\u200Co", "T", "\u200Co", "T", "o\u200C", "T");
        var trailing = chromium.Pages.Single().Words.Single(word => word.Text == "o\u200C");
        var mine = engine.Pages.Single().Words.Last(word => word.Text == "o");
        trailing.X1.Should().BeGreaterThan(chromium.Pages.Single().Words[^1].X0, "the read-back's non-joiner reaches past the next word's start");
        var read = new PrintedPages([.. chromium.Pages.Select(page => page with
        {
            Words = [.. page.Words.Select(word => word with
            {
                Text = word.Text.Replace("\u200C", string.Empty, StringComparison.Ordinal),
                X1 = word == trailing ? mine.X1 : word.X1,
            })],
        })]);
        HtmlCases.Difference(engine, read).Should().BeNull();
    }

    /// <summary>
    /// The fixed table layout at width 100% with columns without a width, the first holding a word wider than an even
    /// share of the page area: Chromium gives each column its share and lets the word overflow into the next column
    /// (CSS 2.1 §17.5.2.1: in the fixed layout the widths do not depend on the content). The engine's columns without a
    /// width are fill columns, which are never narrower than their content's min-content width: the first column is as
    /// wide as the word and its padding, and the others share what is left equally. Where every cell's content fits its
    /// share, both give the same columns. Cases: three columns in a 276 pt page area, 92 pt shares
    /// (<c>tl-fixed-fill-wide-word</c>); two columns, 137.625 pt each in Chromium (<c>boxes.txt</c>), the first holding a
    /// 200 pt "W" that ends at 219.945 pt, beside thirty lines over four pages (<c>a4-line-beside-lines</c>). The column
    /// widths are the whole difference: every word is on Chromium's page, on Chromium's baseline, and the words of each
    /// column lie one distance to the right of Chromium's (none for the first).
    /// </summary>
    /// <param name="name">The case.</param>
    /// <param name="wide">The word wider than its column's share.</param>
    /// <param name="next">The first word of the second column.</param>
    /// <param name="chromiumLines">The left edges of Chromium's column lines, painted at whole pixels.</param>
    [Theory]
    [InlineData("tl-fixed-fill-wide-word", "Pneumonoultramicroscopic", "two", new[] { 12, 103.5, 195.75, 287.25 })]
    [InlineData("a4-line-beside-lines", "W", "p0", new[] { 12, 150, 287.25 })]
    public void A_fill_column_of_the_fixed_layout_is_never_narrower_than_its_content_where_Chromium_lets_the_content_overflow(
        string name, string wide, string next, double[] chromiumLines)
    {
        var (engine, chromium) = (Engine(name), Chromium(name));
        static PrintedWord First(PrintedPages pages, string text) => pages.Pages.SelectMany(page => page.Words).First(word => word.Text == text);

        static double[] Lines(PrintedPages pages) =>
            [.. pages.Pages[0].Rectangles.Where(line => line.Height > line.X1 - line.X0).Select(line => line.X0).Distinct().Order()];

        var word = First(chromium, wide);
        Lines(chromium).Should().Equal(chromiumLines, "Chromium's columns are even shares, painted at whole pixels (a line per row)");
        word.X1.Should().BeGreaterThan(First(chromium, next).X0, "Chromium lets the word overflow into the second column");
        First(engine, next).X0.Should().BeGreaterThan(word.X1, "the engine's first column holds the word");
        var lines = Lines(engine);
        lines.Zip(lines.Skip(1)).Skip(1).Select(pair => pair.Second - pair.First).Should().OnlyContain(share => Math.Abs(share - (lines[2] - lines[1])) < 0.01,
            "the other columns share what is left equally");
        First(engine, wide).X0.Should().BeApproximately(word.X0, 0.01);

        engine.Pages.Select(page => page.Text).Should().Equal(chromium.Pages.Select(page => page.Text));
        var pairs = engine.Pages.SelectMany(page => page.Words).Zip(chromium.Pages.SelectMany(page => page.Words)).ToList();
        pairs.Should().OnlyContain(pair => pair.First.Page == pair.Second.Page && Math.Abs(ChromiumLayout.WholePixel(pair.First.Baseline) - pair.Second.Baseline) < HtmlCases.Bound,
            "every word is on Chromium's page and baseline, which Chromium paints at the nearest whole pixel");
        var column = (PrintedWord theirs) => chromiumLines.Count(line => line <= theirs.X0) - 1;
        var shifts = pairs.GroupBy(pair => column(pair.Second)).ToDictionary(group => group.Key, group => group.First().First.X0 - group.First().Second.X0);
        shifts[0].Should().BeApproximately(0, 0.01, "the first column starts where Chromium's does");
        pairs.Should().OnlyContain(pair => Math.Abs(pair.First.X0 - pair.Second.X0 - shifts[column(pair.Second)]) < 0.01, "each column's words move by one distance");
    }

    /// <summary>
    /// A row whose cell's lines fill the page area and whose padding does not fit with them, at the top of the first page
    /// (case <c>fr-f4-padding-page-start</c>, eight lines with 3 pt padding, 151.5 pt in a 150 pt page area): Chromium
    /// leaves the first page blank and prints the row whole on the second, its bottom padding and line passing the page
    /// end; the engine cuts the row as a last resort before its last line, because a table at the top of a page cannot
    /// move to a better page (docs/architecture.md, "Known limits"; the code front-end's
    /// <c>TableBreakReferenceTests</c> pins the same pages). Where the page breaks is the whole difference: the engine's
    /// first page is Chromium's second without its last line, every word and line where Chromium prints it.
    /// </summary>
    [Fact]
    public void At_the_top_of_a_page_such_a_row_is_cut_where_Chromium_leaves_a_blank_page()
    {
        var (engine, chromium) = (Engine("fr-f4-padding-page-start"), Chromium("fr-f4-padding-page-start"));

        engine.PageTexts.Should().Equal("a0 a1 a2 a3 a4 a5 a6", "a7 z");
        chromium.PageTexts.Should().Equal(string.Empty, "a0 a1 a2 a3 a4 a5 a6 a7", "z");
        var second = chromium.Pages[1];
        second.Words[^1].Baseline.Should().Be(155.25);
        var cut = second with { Number = 1, Words = [.. second.Words.SkipLast(1).Select(word => word with { Page = 1 })], Rectangles = [.. second.Rectangles.Select(line => line with { Page = 1 })] };
        HtmlCases.Difference(new PrintedPages([engine.Pages[0]]), new PrintedPages([cut])).Should().BeNull("the row is cut where the page ends, as Chromium lays it out");
    }

    /// <summary>
    /// A one-row table at the top of the first page whose row is taller than the page area only through its cell's bottom
    /// padding (case <c>fr-old-row-taller-by-padding</c>: "w" above 140 pt of padding, 159.5 pt in a 150 pt page area, then
    /// "z"): Chromium leaves the first page blank, prints the row on the second with its padding passing the page end, and
    /// starts the third with the rest of the padding and the bottom line. The engine cuts the row as a last resort on the
    /// first page, where nothing of it shows but the table's top line and the cell's sides, because a table at the top of
    /// a page cannot move to a better page; on the second page the row's rest keeps "w" and its padding passes the page
    /// end, as in Chromium (<c>TableEndOverflow.ContinuesOnNextPage</c>). Under the code front-end's defaults the row is
    /// refused (docs/architecture.md, "Known limits"; the code front-end's <c>ListedDifferenceTests</c>). Where the row
    /// starts is the whole difference: with the engine's first page empty and the top line drawn at the top of its second
    /// page instead, what lies below it lowered by the line's 0.75 pt, the engine's pages are Chromium's.
    /// </summary>
    [Fact]
    public void A_row_taller_than_the_page_only_through_its_cell_padding_starts_on_a_page_that_Chromium_leaves_blank()
    {
        var (engine, chromium) = (Engine("fr-old-row-taller-by-padding"), Chromium("fr-old-row-taller-by-padding"));

        engine.PageTexts.Should().Equal(string.Empty, "w", "z");
        chromium.PageTexts.Should().Equal(engine.PageTexts);
        chromium.Pages[0].Rectangles.Should().BeEmpty();
        var line = engine.Pages[0].Rectangles.Single(box => box.X1 - box.X0 > 200);
        line.Should().Be(new PrintedBox(1, 12, 12, 288, 12.75, line.Colour));
        engine.Pages[0].Rectangles.Except([line]).Should().HaveCount(2).And.OnlyContain(side => side.X1 - side.X0 == 0.75 && side.Y1 == 162, "the cell's sides, down to the page end");
        var second = engine.Pages[1];
        var started = second with
        {
            Words = [.. second.Words.Select(word => word with { Baseline = word.Baseline + line.Height })],
            Rectangles = [line with { Page = 2 }, .. second.Rectangles],
        };
        var blank = engine.Pages[0] with { Rectangles = [] };
        HtmlCases.Difference(new PrintedPages([blank, started, engine.Pages[2]]), chromium).Should().BeNull("where the row starts is the whole difference");
    }

    /// <summary>
    /// A margin dropped at a row cut (case <c>fr-old-margin-at-row-cut</c>: "a" with a 12 pt bottom margin ends the first
    /// page, "b0 b1" follow it in the same cell): the margin is truncated at the break (CSS Fragmentation 3 §5.2), and the
    /// engine's rest of the row is as tall as its content; Chromium's row keeps the height it has uncut, so its rest is
    /// 12 pt taller (docs/architecture.md, "Known limits"; the code front-end's <c>ListedDifferenceTests</c> pins the same).
    /// That height is the whole difference: the engine's second page with what lies below the row's rest moved 12 pt
    /// lower, and the cell's side lines 12 pt longer, is Chromium's.
    /// </summary>
    [Fact]
    public void A_margin_dropped_at_a_row_cut_does_not_stay_in_the_row_s_height()
    {
        var (engine, chromium) = (Engine("fr-old-margin-at-row-cut"), Chromium("fr-old-margin-at-row-cut"));
        double Z(PrintedPages pages) => pages.Pages[1].Words.Single(word => word.Text == "z").Baseline;

        engine.PageTexts.Should().Equal(chromium.PageTexts).And.Equal("i a", "b0 b1 z");
        (Z(engine), Z(chromium)).Should().Be((62.25, 74.25));
        var page = engine.Pages[1];
        var end = page.Rectangles.Where(line => line.X1 - line.X0 > line.Height).Min(line => line.Y0);
        end.Should().Be(48, "the rest of the row ends below b1");
        var taller = page with
        {
            Words = [.. page.Words.Select(word => word.Baseline > end ? word with { Baseline = word.Baseline + 12 } : word)],
            Rectangles = [.. page.Rectangles.Select(line => line.Y0 >= end ? line with { Y0 = line.Y0 + 12, Y1 = line.Y1 + 12 } : line.Y1 > end ? line with { Y1 = line.Y1 + 12 } : line)],
        };
        HtmlCases.Difference(new PrintedPages([engine.Pages[0], taller]), chromium).Should().BeNull("the row's height is the whole difference");
    }

    /// <summary>
    /// An image in a cell that spans two rows (case <c>a4-image-rowspan</c>; the code front-end's <c>SliceInTableTests</c>
    /// pins the same pages): a difference that follows from two stated rules. A row span's missing height goes to its
    /// last row (docs/architecture.md, "Deliberate deviations from CSS"), so the engine keeps "b"'s row on the first page
    /// (baseline 40.5 pt) where Chromium, which spreads that height over the spanned rows, puts it beside the first slice
    /// (49.5 pt on page 2); and what follows the image's row starts below the last slice (72.375 pt on page 4, below the
    /// line centred on the slice's end, <c>SliceRestEnd.AtLineCentre</c>; printed at 72.75) where
    /// Chromium draws it over the second slice (73.5 pt on page 3; the limit of
    /// <see cref="What_follows_the_row_of_an_image_sliced_in_a_table_starts_below_its_last_slice_where_Chromium_draws_it_over_the_slices"/>).
    /// The slices are the same on every page.
    /// </summary>
    [Fact]
    public void An_image_in_a_cell_that_spans_two_rows_follows_the_engine_s_rules_for_rows_and_spanning_cells()
    {
        var (engine, chromium) = (Engine("a4-image-rowspan"), Chromium("a4-image-rowspan"));
        static (int Page, double Baseline) At(PrintedPages pages, string text) =>
            pages.Pages.SelectMany(page => page.Words).Where(word => word.Text == text).Select(word => (word.Page, word.Baseline)).Single();

        engine.PageTexts.Should().Equal("a b", string.Empty, string.Empty, "after z");
        chromium.PageTexts.Should().Equal("a", "b", "after z", string.Empty);
        engine.Pages.Select(page => page.Images).Should().BeEquivalentTo(chromium.Pages.Select(page => page.Images), options => options.WithStrictOrdering(), "the slices are the same");
        (At(engine, "a"), At(chromium, "a")).Should().Be(((1, 24.75), (1, 24.75)));
        (At(engine, "b"), At(chromium, "b")).Should().Be(((1, 40.5), (2, 49.5)));
        (At(engine, "after"), At(chromium, "after")).Should().Be(((4, 72.375), (3, 73.5)));
        At(engine, "after").Baseline.Should().BeGreaterThan(engine.Pages[3].Images.Single().Y1, "the next row comes below the last slice");
        At(chromium, "after").Baseline.Should().BeLessThan(chromium.Pages[2].Images.Single().Y1, "Chromium draws it over the slice");
    }

    /// <summary>
    /// A footer group that is at most a quarter of the page area and does not break inside (<c>break-inside: avoid</c>,
    /// which the user-agent style sheet gives <c>tfoot</c>) is repeated by Chromium at the end of every page its table
    /// is on, as it repeats a header group (CSS Tables 3, <c>#repeated-headers</c>; Chromium:
    /// <c>TableLayoutAlgorithm::Layout</c>, the repeatable header and footer check). The document model's table has
    /// no footer group, so the engine draws the footer's rows once, after the last body row. With
    /// <c>break-inside: auto</c> on the footer group Chromium does not repeat it either (case <c>tf-pages-auto</c>), and
    /// on one page both print the same (<c>tf-after</c>).
    /// </summary>
    [Fact]
    public void A_footer_group_is_drawn_once_after_the_body_where_Chromium_repeats_it_on_every_page()
    {
        var (engine, chromium) = (Engine("tf-pages"), Chromium("tf-pages"));
        static string[] Ends(PrintedPages pages) => [.. pages.Pages.Select(page => string.Join(" ", page.Words.Where(word => word.Text != "after").TakeLast(2).Select(word => word.Text)))];

        chromium.Pages.Should().HaveCount(4);
        Ends(chromium).Should().AllBe("total footer", "Chromium ends every page with the footer row");
        Ends(engine)[..^1].Should().NotContain("total footer", "the engine draws the footer only after the last body row");
        Ends(engine)[^1].Should().Be("total footer");
        engine.Pages.SelectMany(page => page.Words).Count(word => word.Text == "total").Should().Be(1);
    }

    /// <summary>
    /// A face of an <c>@font-face</c> family that only a page-margin box uses: Chromium has not loaded it when it prints
    /// the margin boxes and prints that box's text in its fallback serif face, the engine draws the face (case
    /// <c>pm-family-unused</c>: the body uses Lato Regular only, <c>@bottom-left</c> asks for Lato Bold; three prints of
    /// the case were the same). Once the body uses the face too, Chromium prints it (case <c>pm-inherit-family</c>,
    /// equal). With Chromium's word read as the engine's (face, end and baseline), the case is equal: the face is the
    /// whole difference.
    /// </summary>
    [Fact]
    public void A_face_that_only_a_margin_box_uses_is_drawn_where_Chromium_prints_its_fallback_face()
    {
        var (engine, chromium) = (Engine("pm-family-unused"), Chromium("pm-family-unused"));
        var theirs = chromium.Pages.Single().Words.Single(word => word.Text == "Cc");
        var mine = engine.Pages.Single().Words.Single(word => word.Text == "Cc");
        (theirs.Face, mine.Face).Should().Be(("LiberationSerif-Bold", "Lato-Bold"));
        chromium.Pages.Single().Words.Where(word => word.Text != "Cc").Should().OnlyContain(word => word.Face == "Lato-Regular");

        var read = new PrintedPages([.. chromium.Pages.Select(page => page with
        {
            Words = [.. page.Words.Select(word => word == theirs ? word with { Face = mine.Face, X1 = mine.X1, Baseline = mine.Baseline } : word)],
        })]);
        HtmlCases.Difference(engine, read).Should().BeNull();
    }

    /// <summary>
    /// A repeated header row over a slice: on a later page where a table repeats its header rows and goes on with the
    /// slices of a piece taller than the page area (a row still to come), Chromium still moves the piece up a whole page
    /// area, so the slice starts at the top of the page area, and draws the header over it, over 16.5 pt of the piece that
    /// are never shown. The engine draws the header, then the slice below it, so the whole piece shows once
    /// (docs/architecture.md, "Known limits"). That is the whole difference: on the pages before, both print the same; on
    /// the pages of the slices the header lies over the slice in Chromium and above it in the engine; what follows the
    /// slices in the table starts that much lower in the engine, and where Chromium starts the next row in an empty part
    /// below the header on the page of the last slice, that row goes on half a grid line higher on the next page than the
    /// engine's, which starts it there (0.375 pt, within the comparison's whole pixel until the end of the table).
    /// Cases: a 360 pt image, then a 408 pt image (<c>a4-header-image-360-image</c>, pages 3 and 4); a row, a 408 pt image,
    /// a row (<c>a4-header-row-image-row</c>, pages 4 and 5); a 408 pt image, a row, a 408 pt image
    /// (<c>a4-header-image-row-image</c>, pages 3 and 4); two 408 pt images (<c>a4-header-two-image-rows</c>, pages 3 and 4).
    /// </summary>
    /// <param name="name">The case.</param>
    /// <param name="first">The first page where the header is repeated over a slice.</param>
    /// <param name="last">The last such page.</param>
    [Theory]
    [InlineData("a4-header-image-360-image", 3, 4)]
    [InlineData("a4-header-row-image-row", 4, 5)]
    [InlineData("a4-header-image-row-image", 3, 4)]
    [InlineData("a4-header-two-image-rows", 3, 4)]
    public void A_repeated_header_is_drawn_above_a_slice_where_Chromium_draws_it_over_the_slice(string name, int first, int last)
    {
        var (engine, chromium) = (Engine(name), Chromium(name));
        var before = (PrintedPages pages) => new PrintedPages([.. pages.Pages.Take(first - 1)]);

        engine.Pages.Length.Should().Be(chromium.Pages.Length);
        HtmlCases.Difference(before(engine), before(chromium)).Should().BeNull("the pages before the slices are the same");
        for (var number = first; number <= last; number++)
        {
            var (ours, theirs) = (engine.Pages[number - 1], chromium.Pages[number - 1]);
            theirs.Words.Single(word => word.Text == "H").Baseline.Should().Be(24.75);
            theirs.Images.Single().Y0.Should().Be(12, "Chromium's slice starts at the top of the page area");
            theirs.Images.Single().Y1.Should().BeGreaterThanOrEqualTo(28.5, "and the header is drawn over it (the last slice of the 360 pt image lies wholly under it)");
            ours.Words.Single(word => word.Text == "H").Baseline.Should().Be(24.75);
            ours.Images.Single().Y0.Should().BeApproximately(28.125, 1e-6, "the engine's slice starts below the header, at the centre of its line");
        }

        HtmlCases.Difference(engine, chromium).Should().NotBeNull();
    }

    /// <summary>
    /// What follows the row of an image sliced over pages in a table cell: Chromium lays the image out whole on the page
    /// where it starts, ends the row on the next page as tall as the rest of the image, and starts what follows there:
    /// in a row of two or more cells the next row, and after any table the content after it, below the row's end, or at
    /// the top of the next page, drawn over the slices still to come. The engine starts what follows below the last
    /// slice, so no text is drawn over an image (docs/architecture.md, "Known limits"); it centres the line that ends the
    /// row on the end of the last slice (<c>SliceRestEnd.AtLineCentre</c>), which prints where Chromium's is. That place is the whole
    /// difference: the engine's print with what follows the slice moved up to Chromium's place (from the engine's page
    /// to Chromium's, by the difference of the first word's baseline) is Chromium's, compared as every case is
    /// (<see cref="HtmlCases.Difference"/>). The engine draws a column's grid line as one rectangle down through the
    /// next row; its part below the line that ends the sliced row moves with the row. Cases: a 408 pt image in the second
    /// of two cells, then a row (<c>a4-image-in-cell</c>); in the last row of a one-column table, then a paragraph
    /// (<c>a4-row-image-row</c>);
    /// two such rows, then a paragraph (<c>a4-two-image-rows</c>); a 588 pt image in the second of two cells, then a row,
    /// which Chromium draws over the third of four slices, a page before the engine's (<c>a4-slice4-cell-row-after</c>); a
    /// 408 pt image in a table inside a cell, then a row (<c>a4-image-nested-table</c>). There Chromium ends the outer row
    /// as tall as the rest of the image too, so its line lies on the inner table's bottom line (both 60 to 60.75 pt);
    /// the engine stacks the outer cell's bottom inset below the inner table, its line 0.75 pt lower. That distance is
    /// stated and taken out before the comparison: Chromium's coinciding lines come with drawing over the slices, not with
    /// a geometry the engine follows.
    /// </summary>
    /// <param name="name">The case.</param>
    /// <param name="first">The first word after the sliced row.</param>
    /// <param name="chromiumPage">The page Chromium draws it on.</param>
    /// <param name="chromiumBaseline">Its baseline there, over the slice.</param>
    /// <param name="enginePage">The page the engine draws it on.</param>
    /// <param name="engineBaseline">Its baseline there, below the last slice.</param>
    /// <param name="linesApart">How far below the line at the slice's bottom edge the engine draws the outer row's line,
    /// where Chromium draws both in one place; 0 where there is one line.</param>
    [Theory]
    [InlineData("a4-image-in-cell", "after", 4, 24.75, 4, 72.375, 0)]
    [InlineData("a4-row-image-row", "after", 5, 24.75, 5, 72.375, 0)]
    [InlineData("a4-two-image-rows", "after", 8, 24.75, 8, 72.375, 0)]
    [InlineData("a4-slice4-cell-row-after", "after", 4, 23.25, 5, 70.875, 0)]
    [InlineData("a4-image-nested-table", "after", 4, 24.75, 4, 73.125, 0.75)]
    public void What_follows_the_row_of_an_image_sliced_in_a_table_starts_below_its_last_slice_where_Chromium_draws_it_over_the_slices(
        string name, string first, int chromiumPage, double chromiumBaseline, int enginePage, double engineBaseline, double linesApart)
    {
        var (engine, chromium) = (Engine(name), Chromium(name));
        var word = (PrintedPages pages, int page) => pages.Pages[page - 1].Words.First(candidate => candidate.Text == first);
        var sliceEnd = (PrintedPages pages, int page) => pages.Pages[page - 1].Images.Max(image => image.Y1);

        word(chromium, chromiumPage).Baseline.Should().Be(chromiumBaseline);
        chromiumBaseline.Should().BeLessThan(sliceEnd(chromium, chromiumPage), "Chromium draws it over a slice");
        word(engine, enginePage).Baseline.Should().Be(engineBaseline);
        engineBaseline.Should().BeGreaterThan(sliceEnd(engine, enginePage), "the engine starts it below the last slice");
        HtmlCases.Difference(engine, chromium).Should().NotBeNull();

        // Every word below the last slice moves, and every rectangle below the line centred on the slice's bottom edge,
        // which both draw (it ends the sliced row, SliceRestEnd.AtLineCentre); a column's line that runs on through it is
        // split there. An outer row's line drawn below it is first lifted onto it, as Chromium draws it.
        var from = sliceEnd(engine, enginePage);
        var source = engine.Pages[enginePage - 1];
        var across = (PrintedBox line, double y) => Math.Abs(((line.Y0 + line.Y1) / 2) - y) < 0.01 && line.X1 - line.X0 > line.Height;
        var lifted = source.Rectangles.Where(line => across(line, from + linesApart)).ToList();
        lifted.Should().NotBeEmpty("the line that ends the sliced row");
        if (linesApart > 0)
        {
            source.Rectangles.Should().Contain(line => across(line, from), "the inner table's bottom line");
            chromium.Pages[enginePage - 1].Rectangles.Where(line => Math.Abs(line.Y0 - ChromiumLayout.WholePixel(from - (line.Height / 2))) < 0.01 && line.X1 - line.X0 > line.Height)
                .Should().HaveCountGreaterThanOrEqualTo(2, "Chromium draws the inner and the outer line in one place");
        }

        var cut = lifted.Max(line => line.Y1);
        var shift = engineBaseline - chromiumBaseline;
        var stays = source.Rectangles.Where(item => lifted.Contains(item) || item.Y0 < cut - linesApart)
            .Select(item => lifted.Contains(item) ? item with { Y0 = item.Y0 - linesApart, Y1 = item.Y1 - linesApart } : item with { Y1 = Math.Min(item.Y1, cut - linesApart) });
        var goes = source.Rectangles.Where(item => item.Y1 > cut).Select(item => item with { Page = chromiumPage, Y0 = Math.Max(item.Y0, cut) - shift, Y1 = item.Y1 - shift });
        var moved = new PrintedPages([.. engine.Pages.Select(page =>
        {
            var words = page.Words.Where(item => page.Number != enginePage || item.Baseline <= from).ToList();
            var rectangles = page.Number == enginePage ? [.. stays] : page.Rectangles.ToList();
            if (page.Number == chromiumPage)
            {
                words.AddRange(source.Words.Where(item => item.Baseline > from).Select(item => item with { Page = chromiumPage, Baseline = item.Baseline - shift }));
                rectangles.AddRange(goes);
            }

            return page with { Words = [.. words.OrderBy(item => item.Baseline).ThenBy(item => item.X0)], Rectangles = [.. rectangles] };
        })]);
        HtmlCases.Difference(moved, chromium).Should().BeNull("where what follows the sliced row starts is the whole difference");
    }

    /// <summary>
    /// A hyphen after an image or a space, before a letter (case <c>ii-hyphen</c>, 6 pt wide paragraphs, 18 pt lines):
    /// UAX #14 LB20a allows no break between the hyphen and the letter there, so the engine keeps "-b" on one line;
    /// Chromium breaks after the hyphen all the same, after an image as after a space, and prints "-" and "b" on lines of
    /// their own (the code front-end's <c>InlineImageTests</c> pins the same lines). That break is the whole difference:
    /// with each such "b" set after its hyphen on the hyphen's line and what follows moved up one line, Chromium's print
    /// is the engine's.
    /// </summary>
    [Fact]
    public void A_hyphen_after_an_image_keeps_its_letter_as_after_a_space_where_Chromium_breaks_after_it_in_the_HTML_case()
    {
        var (engine, chromium) = (Engine("ii-hyphen"), HtmlCases.ChromiumPrint("ii-hyphen"));
        engine.Pages[0].Lines.Select(line => line.Text).Should().Equal("a", "-b", "a", "-b", "a-", "b", "x");
        chromium.Pages[0].Lines.Select(line => line.Text).Should().Equal("a", "-", "b", "a", "-", "b", "a-", "b", "x");
        chromium.Pages[0].Images.Single().Y1.Should().Be(43.5, "the image is on a line of its own in both");

        var joined = JoinedWithNextLine(JoinedWithNextLine(chromium, 115.5, 18), 61.5, 18);
        HtmlCases.Difference(engine, joined).Should().BeNull("the break after the hyphen is the whole difference");
    }

    /// <summary>
    /// A line break beside an image next to a word joiner (U+2060) or a zero width joiner (U+200D) (case <c>ii-joiners</c>,
    /// a deliberate deviation from CSS Text 3 §5.5 that follows Chromium; the code front-end's <c>InlineImageTests</c> pins
    /// it): the lines are Chromium's, and the text layer differs. Chromium's print keeps the joiners in the PDF's text, the
    /// engine draws no glyph for them; and as for the non-joiner of <c>sr-zwnj</c> the read-back gives a joiner that ends
    /// a word the PDF's default width (<c>/DW 256</c>, 3.072 pt at 12 pt), so such a word reads that much longer. With the
    /// joiners taken out of Chromium's words and that width out of their ends, the case is equal.
    /// </summary>
    [Fact]
    public void A_line_breaks_beside_an_image_also_next_to_a_joiner_like_Chromium_whose_text_keeps_the_joiners()
    {
        var (engine, chromium) = (Engine("ii-joiners"), HtmlCases.ChromiumPrint("ii-joiners"));
        static bool Joiner(char character) => character is '⁠' or '‍';
        var ending = chromium.Pages[0].Words.Where(word => Joiner(word.Text[^1])).ToList();
        ending.Select(word => word.Text).Should().Equal("abc⁠", "abc‍");
        engine.Pages[0].Words.Where(word => word.Text == "abc").Take(2).Select(word => word.X1)
            .Should().Equal(ending.Select(word => word.X1 - (DefaultWidth * word.FontSize)), (mine, theirs) => Math.Abs(mine - theirs) < 0.01);

        var read = new PrintedPages([.. chromium.Pages.Select(page => page with
        {
            Words = [.. page.Words.Select(word => word with
            {
                Text = string.Concat(word.Text.Where(character => !Joiner(character))),
                X1 = ending.Contains(word) ? word.X1 - (DefaultWidth * word.FontSize) : word.X1,
            })],
        })]);
        HtmlCases.Difference(engine, read).Should().BeNull("the joiners in the text are the whole difference");
    }

    /// <summary>
    /// Where an element that wraps differently from its parent ends (case <c>ws-border</c>, 18 pt lines): Chromium decides a
    /// break at the end tag by the element (Blink <c>LineBreaker::HandleCloseTag</c>), the engine by the wrap mode of the
    /// character before the opportunity (docs/architecture.md, the end-tag rule; the code front-end's
    /// <c>WhiteSpaceTests</c> pins the same lines). So the engine keeps three lines whole that Chromium breaks: a nowrap
    /// "aa-" before a wrapping "bb" (paragraph 8), a wrapping "aa" before the nowrap " bb" of its parent (paragraph 14), and
    /// a wrapping "bb cc" before the parent's nowrap " dd" (paragraph 17). Those three breaks are the whole difference:
    /// with each taken in the engine's print (the rest of the line moved to the start of the next, what follows one line
    /// lower), the engine's print is Chromium's.
    /// </summary>
    [Fact]
    public void Where_an_element_that_wraps_differently_from_its_parent_ends_the_engine_follows_the_character_before_not_Chromium_in_the_HTML_case()
    {
        var (engine, chromium) = (Engine("ws-border"), HtmlCases.ChromiumPrint("ws-border"));
        string Line(PrintedPages pages, double baseline) => string.Join(" ", pages.Pages[0].Words.Where(word => On(word.Baseline, baseline)).Select(word => word.Text));
        (Line(engine, 187.5), Line(chromium, 187.5), Line(chromium, 205.5)).Should().Be(("aa- bb", "aa-", "bb"));
        (Line(engine, 367.5), Line(chromium, 385.5), Line(chromium, 403.5)).Should().Be(("aa bb", "aa", "bb"));
        (Line(engine, 457.5), Line(chromium, 493.5), Line(chromium, 511.5)).Should().Be(("cc dd", "cc", "dd"));

        var broken = BrokenBefore(BrokenBefore(BrokenBefore(engine, 457.5, 26.427, 18), 367.5, 27.002, 18), 187.5, 28.395, 18);
        HtmlCases.Difference(broken, chromium).Should().BeNull("the three breaks at the end tags are the whole difference");
    }

    /// <summary>
    /// The end-tag rule beside an image (case <c>ws-image-border</c>, 18 pt lines; the code front-end's <c>WhiteSpaceTests</c>
    /// pins the same lines): Chromium puts "ab", the image and "cd" on three lines in the first two paragraphs. The engine
    /// keeps the image on the line of a nowrap "ab" before it (paragraph 1), and "cd" on the line of an image inside a
    /// nowrap element (paragraph 2), as the character before each opportunity does not wrap. Those two breaks are the
    /// whole difference: with each taken in the engine's print, the engine's print is Chromium's.
    /// </summary>
    [Fact]
    public void Beside_an_image_at_the_end_of_an_element_that_wraps_differently_the_engine_follows_the_character_before_not_Chromium_in_the_HTML_case()
    {
        var (engine, chromium) = (Engine("ws-image-border"), HtmlCases.ChromiumPrint("ws-image-border"));
        engine.Pages[0].Images[0].X0.Should().BeApproximately(24.691, 1e-3, "the image follows the nowrap \"ab\" on its line");
        engine.Pages[0].Images[0].Y1.Should().BeApproximately(25.5, 1e-3);
        (chromium.Pages[0].Images[0].X0, chromium.Pages[0].Images[0].Y1).Should().Be((12, 43.5), "Chromium breaks before it");
        engine.Pages[0].Words.Single(word => On(word.Baseline, 79.5)).X0.Should().BeApproximately(20.4, 1e-3, "\"cd\" follows the nowrap image on its line");

        var broken = BrokenBefore(BrokenBefore(engine, 79.5, 20.4, 18), 25.5, engine.Pages[0].Images[0].X0, 18);
        HtmlCases.Difference(broken, chromium).Should().BeNull("the two breaks at the end tags are the whole difference");
    }

    /// <summary>
    /// A soft hyphen and a zero width space in text of each white space mode (case <c>ws-soft</c>; the code front-end's
    /// <c>WhiteSpaceTests</c> pins the same lines): the lines are Chromium's, the text layer differs (docs/architecture.md,
    /// a listed difference of the text layer). Chromium's PDF text keeps every soft hyphen and zero width space and adds
    /// U+2010 at a shown hyphen; the engine's keeps only a shown soft hyphen. And the read-back gives a zero width space
    /// that ends a word the PDF's default width (<c>/DW 256</c>, 3.072 pt at 12 pt). With Chromium's words read as the
    /// engine's text and that width out of their ends, the case is equal.
    /// </summary>
    [Fact]
    public void A_soft_hyphen_and_a_zero_width_space_break_by_the_white_space_mode_like_Chromium_whose_text_keeps_them()
    {
        var (engine, chromium) = (Engine("ws-soft"), HtmlCases.ChromiumPrint("ws-soft"));
        chromium.Pages[0].Words.Select(word => word.Text).Should().StartWith(["aa­bb​cc", "aa­‐", "bb​", "cc"]);
        engine.Pages[0].Words.Select(word => word.Text).Should().StartWith(["aabbcc", "aa­", "bb", "cc"]);

        var read = new PrintedPages([.. chromium.Pages.Select(page => page with
        {
            Words = [.. page.Words.Select(word => word with
            {
                Text = EngineText(word.Text),
                X1 = word.Text.EndsWith('​') ? word.X1 - (DefaultWidth * word.FontSize) : word.X1,
            })],
        })]);
        HtmlCases.Difference(engine, read).Should().BeNull("the text layer is the whole difference");
    }

    /// <summary>
    /// Underlines that skip the ink of descenders, cut beside an image, at a shown soft hyphen and in ligatures (case
    /// <c>sk-misc</c>; the code front-end's <c>SkipInkTests</c> pins the same pieces): the pieces are Chromium's, compared
    /// as the status compares them (<see cref="HtmlCases.ChromiumPrint"/>); the text layer differs at the shown soft
    /// hyphen, where Chromium's PDF text adds U+2010 after it and the engine's has the soft hyphen only. With that character
    /// taken out of Chromium's words, the case is equal.
    /// </summary>
    [Fact]
    public void An_image_a_shown_soft_hyphen_and_ligatures_cut_the_underline_like_Chromium_whose_text_adds_a_hyphen()
    {
        var (engine, chromium) = (Engine("sk-misc"), HtmlCases.ChromiumPrint("sk-misc"));
        chromium.Pages[0].Words.Should().ContainSingle(word => word.Text.Contains('‐')).Which.Text.Should().Be("jump­‐");
        engine.Pages[0].Words.Should().Contain(word => word.Text == "jump­");

        HtmlCases.Difference(engine, Read(chromium, EngineText)).Should().BeNull("the hyphen in the text is the whole difference");
    }

    /// <summary>The width the read-back gives a glyph that Chromium's PDF lists no width for, per point of font size: the
    /// font's default width, <c>/DW 256</c> in units of 1/1000 (case <c>sr-zwnj</c>).</summary>
    private const double DefaultWidth = 0.256;

    /// <summary>A word of Chromium's PDF text as the engine's text has it: without zero width spaces and without the U+2010
    /// that Chromium adds at a shown soft hyphen; a soft hyphen only where it is shown, at the end of the word.</summary>
    private static string EngineText(string text)
    {
        var kept = string.Concat(text.Where(character => character is not ('​' or '‐')));
        return kept.EndsWith('­') ? kept[..^1].Replace("­", string.Empty, StringComparison.Ordinal) + "­" : kept.Replace("­", string.Empty, StringComparison.Ordinal);
    }

    /// <summary>Whether two positions are one, within the print files' precision.</summary>
    private static bool On(double position, double other) => Math.Abs(position - other) < 0.01;

    /// <summary>The only page with the line at <paramref name="baseline"/> broken before <paramref name="x"/>: its words and
    /// images from there on start the next line at the line's left edge (that of its first word or image), one line of
    /// <paramref name="lineHeight"/> lower, and everything below the line moves down by one line.</summary>
    private static PrintedPages BrokenBefore(PrintedPages pages, double baseline, double x, double lineHeight)
    {
        var page = pages.Pages.Single();
        var left = page.Words.Where(word => On(word.Baseline, baseline)).Select(word => word.X0).Concat(page.Images.Where(image => On(image.Y1, baseline)).Select(image => image.X0)).Min();
        var moved = (double from) => from >= x - 0.001;
        return new([page with
        {
            Words = [.. page.Words.Select(word => word.Baseline > baseline + 0.01 ? word with { Baseline = word.Baseline + lineHeight }
                : On(word.Baseline, baseline) && moved(word.X0) ? word with { X0 = word.X0 - x + left, X1 = word.X1 - x + left, Baseline = word.Baseline + lineHeight } : word)
                .OrderBy(word => word.Baseline).ThenBy(word => word.X0)],
            Images = [.. page.Images.Select(image => image.Y0 >= baseline ? image with { Y0 = image.Y0 + lineHeight, Y1 = image.Y1 + lineHeight }
                : On(image.Y1, baseline) && moved(image.X0) ? image with { X0 = image.X0 - x + left, X1 = image.X1 - x + left, Y0 = image.Y0 + lineHeight, Y1 = image.Y1 + lineHeight } : image)],
            Rectangles = [.. page.Rectangles.Select(box => box.Y0 >= baseline ? box with { Y0 = box.Y0 + lineHeight, Y1 = box.Y1 + lineHeight } : box)],
        }]);
    }

    /// <summary>The only page with the one word on the line below <paramref name="baseline"/> set after the line's one word,
    /// as one word, and everything below moved up by one line of <paramref name="lineHeight"/>.</summary>
    private static PrintedPages JoinedWithNextLine(PrintedPages pages, double baseline, double lineHeight)
    {
        var page = pages.Pages.Single();
        var (first, next) = (page.Words.Single(word => On(word.Baseline, baseline)), page.Words.Single(word => On(word.Baseline, baseline + lineHeight)));
        return new([page with
        {
            Words = [.. page.Words.Where(word => word != next).Select(word => word == first ? word with { Text = first.Text + next.Text, X1 = first.X1 + next.X1 - next.X0 }
                : word.Baseline > baseline + 0.01 ? word with { Baseline = word.Baseline - lineHeight } : word)],
            Images = [.. page.Images.Select(image => image.Y0 > baseline + lineHeight ? image with { Y0 = image.Y0 - lineHeight, Y1 = image.Y1 - lineHeight } : image)],
            Rectangles = [.. page.Rectangles.Select(box => box.Y0 > baseline + lineHeight ? box with { Y0 = box.Y0 - lineHeight, Y1 = box.Y1 - lineHeight } : box)],
        }]);
    }

    /// <summary>Chromium's print with each word's text read through <paramref name="text"/>.</summary>
    private static PrintedPages Read(PrintedPages pages, Func<string, string> text) =>
        new([.. pages.Pages.Select(page => page with { Words = [.. page.Words.Select(word => word with { Text = text(word.Text) })] })]);

    /// <summary>Where each line of the only page ends: its last word's end, lines in order.</summary>
    private static double[] LineEnds(PrintedPages pages) =>
        [.. pages.Pages.Single().Words.GroupBy(word => word.Baseline).OrderBy(line => line.Key).Select(line => line.Max(word => word.X1))];
}
