using System.IO;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;

namespace UnitTests.Extensions.Documents.Html.Boxes;

/// <summary>
/// HTML cases that differ from Chromium by a limit of layout, each pinned with Chromium's print here and listed in the
/// case's status file (<c>measured/&lt;case&gt;.status.txt</c>, <c>listed</c> with the test's name) and in docs/architecture.md, "Known limits".
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
    /// An inline box that ends on a justified line can end, in Chromium 159's layout, 1/64 px past the page area. Chromium
    /// then shrinks the whole print as for any content wider than the page area (see
    /// <see cref="Content_wider_than_the_page_area_is_not_shrunk_as_Chromium_shrinks_the_page"/>), by 112/112.015625 on a
    /// page area 112 px wide, lays the page out one pixel wider, 113 px, and clips it at the page area. Case
    /// <c>ib-break-justify</c>: in its second paragraph "kk ll" in a box with 5 pt padding and a 1 px border ends on the
    /// first line; every font size of the page prints at 10.499 pt, and the box's right border shows only 0.012 pt of its
    /// 0.75 pt. Taken back through the scale, each justified line on the page, also the first paragraph's, ends 0.75 pt
    /// past the engine's, each of its three spaces 0.25 pt wider. The engine justifies each line to the page area's end
    /// and has no page scale. With those spaces and the clipped border taken as the engine's, the print is the engine's.
    /// </summary>
    [Theory]
    [InlineData("ib-break-justify")]
    public void Where_an_inline_box_ends_on_a_justified_line_Chromium_may_shrink_the_page_and_justify_it_a_pixel_wider(string name)
    {
        const double left = 18, top = 18, end = 102;
        var (engine, chromium) = (Engine(name), Chromium(name).Pages.Single());
        var ours = engine.Pages.Single();

        var scale = (chromium.Words[0].X1 - left) / (ours.Words[0].X1 - left);
        scale.Should().BeApproximately(112 / (112 + (1 / 64.0)), 2e-5, "Chromium shrinks the page by an overflow of 1/64 px");
        chromium.Words.Should().OnlyContain(word => System.Math.Abs(word.FontSize - 10.499) < 0.0005, "every font size of the page is shrunk");
        double Back(double at, double origin) => origin + ((at - origin) / scale);
        var unscaled = chromium.Words.Select(word => word with { X0 = Back(word.X0, left), X1 = Back(word.X1, left), Baseline = Back(word.Baseline, top), FontSize = word.FontSize / scale }).ToList();

        // The paragraphs' last lines are not justified; the first line of each is.
        var lines = unscaled.GroupBy(word => System.Math.Round(word.Baseline, 1)).Where(line => line.First().Text is "Justified" or "Spread").ToList();
        lines.Select(line => string.Join(" ", line.Select(word => word.Text))).Should().Equal(["Justified aa bb cc", "Spread out kk ll"], "the two justified lines");
        (ours.Words.Single(word => word.Text == "cc").X1 - end).Should().BeApproximately(0, 0.01, "the engine justifies the first paragraph's first line to the page area's end");
        (unscaled.Single(word => word.Text == "cc").X1 - (left + (113 * 0.75))).Should().BeApproximately(0, 0.02, "Chromium justifies it to 113 px");

        var widened = new Dictionary<PrintedWord, double>();
        foreach (var line in lines)
        {
            var words = line.OrderBy(word => word.X0).ToList();
            var theirs = ours.Words.Where(word => System.Math.Abs(word.Baseline - words[0].Baseline) < HtmlCases.Bound).OrderBy(word => word.X0).ToList();
            theirs.Select(word => word.Text).Should().Equal(words.Select(word => word.Text));
            for (var space = 0; space < words.Count; space++)
            {
                (words[space].X0 - theirs[space].X0).Should().BeApproximately(0.25 * space, 0.02, "each space before '{0}' is 0.25 pt wider", words[space].Text);
                widened[words[space]] = 0.25 * space;
            }
        }

        var clipped = chromium.Rectangles.Single(box => box.Stroked && box.X1 - box.X0 < 0.1);
        (clipped.X0, clipped.X1).Should().Be((101.988, 102.0), "the box's right border, clipped at the page area");
        var back = new PrintedPages([chromium with
        {
            Words = [.. unscaled.Select(word => widened.TryGetValue(word, out var by) ? word with { X0 = word.X0 - by, X1 = word.X1 - by } : word)],
            Rectangles = [.. chromium.Rectangles.Select(box => box == clipped ? box with { X0 = end - 0.75 } : box)
                .Select(box => box with { X0 = Back(box.X0, left), Y0 = Back(box.Y0, top), X1 = System.Math.Min(end, Back(box.X1, left)), Y1 = Back(box.Y1, top) })],
        }]);
        HtmlCases.Difference(engine, back).Should().BeNull("the wider spaces, the clipped border and the scale are the whole difference");
    }

    /// <summary>
    /// A4 without margins (the default page): Chromium's page area is 794 x 1123 px (595.5 x 842.25 pt), the page size
    /// rounded up to whole pixels, and reaches past the sheet it prints, 594.96 x 841.92 pt; the model holds no page area
    /// larger than the sheet, so the engine's ends at the sheet. Text and backgrounds end 0.54 pt short on the right, and
    /// 64 lines of 1123/64 px, which fill Chromium's page, need two. A right margin of 0.71 px (0.53 pt) or more and a
    /// bottom margin of 0.52 px (0.39 pt) or more bring the area inside the sheet: then both agree (case
    /// <c>h-page-a4-margin-px</c>, 1 px margins, equal). Each case shows one of the three: the right edge, a justified
    /// line's end, the lines that fill the page.
    /// </summary>
    /// <param name="name">The case.</param>
    /// <param name="shows">What the case shows: <c>right</c>, <c>justify</c> or <c>lines</c>.</param>
    [Theory]
    [InlineData("h-page-a4-zero-right", "right")]
    [InlineData("h-page-a4-zero-justify", "justify")]
    [InlineData("h-page-a4-zero-lines", "lines")]
    public void The_page_area_of_A4_without_margins_reaches_past_the_sheet_where_the_engine_ends_it_at_the_sheet(string name, string shows)
    {
        var (engine, chromium) = (Engine(name), Chromium(name));
        switch (shows)
        {
            case "right":
                (chromium.Pages.Single().Width, engine.Pages.Single().Width).Should().Be((594.96, 594.96));
                chromium.Pages.Single().Rectangles.Single().X1.Should().Be(595.2, "Chromium draws it to 595.5, its page area 794 px wide, under a clip that ends at 595.2, past the sheet");
                engine.Pages.Single().Rectangles.Single().X1.Should().BeApproximately(594.96, 1e-6, "the engine's ends at the sheet");
                break;
            case "justify":
                LineEnds(chromium)[0].Should().BeApproximately(595.5, 0.01);
                LineEnds(engine)[0].Should().BeApproximately(594.96, 0.01);
                break;
            default:
                shows.Should().Be("lines");
                chromium.Pages.Should().ContainSingle("64 lines of 1123/64 px fill 1123 px");
                engine.Pages.Should().HaveCount(2, "the engine's page area is 1122.56 px high");
                break;
        }
    }

    /// <summary>
    /// A ligature across the border of an inline element in the same font: Chromium forms it and its print maps the glyph
    /// to the first run's characters only (<c>f&lt;span&gt;i&lt;/span&gt;x</c> reads "fx"); the engine's glyph keeps every
    /// character it shows, so the text can be copied and searched (docs/architecture.md, the rule for shaping across runs).
    /// With Chromium's two words read as the engine's, the case is equal: the text is the whole difference.
    /// </summary>
    [Theory]
    [InlineData("sr-same-font")]
    public void A_ligature_across_the_border_of_an_inline_element_keeps_every_character_where_Chromium_keeps_only_those_of_the_first_run(string name)
    {
        var (engine, chromium) = (Engine(name), Chromium(name));
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
    [Theory]
    [InlineData("sr-zwnj")]
    public void A_zero_width_non_joiner_is_not_in_the_text_where_Chromium_keeps_it(string name)
    {
        var (engine, chromium) = (Engine(name), Chromium(name));
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
    /// A link around generated text (case <c>mx-gen-link</c>: <c>a::before { content: "see " }</c> and
    /// <c>a::after { content: " more" }</c>; case <c>mx-link-gen-after</c>: the second alone; case <c>tt-groups</c>: <c>a::before { content: "xy" }</c> on a line whose top
    /// lies between pixels): Chromium's print writes two link annotations for each such link, one around the generated text
    /// and one around the element's own (33.75 to 52.5 pt and 52.5 to 74.25 pt for "see page"; 84 to 96 pt and 96 to 105 pt
    /// for "xyef"), where the engine writes one area for the link's part on the line, from its first piece to its last (THE
    /// RULE FOR A LINK'S AREA). A link area is not printed, so the print is the same, and each is a valid link annotation
    /// (ISO 32000-1 §12.5.6.5); the engine's area is the union of Chromium's two, edge for edge: the number of rectangles is
    /// the whole difference. Everything compared before the links is equal: the first difference is a link's.
    /// </summary>
    /// <param name="name">The case.</param>
    /// <param name="written">How many link rectangles Chromium writes.</param>
    /// <param name="areas">How many link areas the engine writes: one for each pair of Chromium's and each other link.</param>
    [Theory]
    [InlineData("mx-gen-link", 4, 2)]
    [InlineData("mx-link-gen-after", 2, 1)]
    [InlineData("tt-groups", 6, 5)]
    public void A_link_around_generated_text_has_one_area_where_Chromium_writes_one_for_the_generated_text_and_one_for_the_rest(string name, int written, int areas)
    {
        HtmlCases.Status(name).Should().StartWith("different link ", "the print is equal, its underlines cut around the ink, and so are its words and boxes");
        var boxes = HtmlCases.Boxes(name).Value;
        var pages = DocumentLayout.Run(boxes.Document, boxes.Fonts).Value;
        var chromium = HtmlCases.Links(HtmlCases.Cases, name);

        chromium.Should().HaveCount(written);
        HtmlCases.Links(pages).Should().HaveCount(areas);

        // Chromium's two rectangles of a link touch, on one line, to one target.
        var unions = new List<HtmlCases.PrintedLink>();
        foreach (var link in chromium)
        {
            if (unions.Count > 0 && unions[^1] is var last && last.Page == link.Page && last.X1 == link.X0 && last.Y0 == link.Y0 && last.Y1 == link.Y1 && last.Target == link.Target)
            {
                unions[^1] = last with { X1 = link.X1 };
                continue;
            }

            unions.Add(link);
        }

        unions.Should().HaveCount(areas);
        HtmlCases.LinkDifference(pages, unions, boxes.Document.Page.Margins.Top.Points).Should().BeNull();
    }

    /// <summary>
    /// A table whose given width is narrower than its content in the fixed layout, the same limit of the fill columns
    /// (above): case <c>twd-table-narrow</c>, four tables of <c>width: 40pt</c> holding "one" beside "text with more words",
    /// one word a line. In the automatic layout (tables a and c) Chromium and the engine both widen the table to its
    /// content's min-content, 63.773 and 79.523 pt. In the fixed layout (tables b and d) Chromium keeps the table 40 pt
    /// wide, its two columns 19.617 and 12.492 pt, and lets "one" overflow into the second column (in d it touches "text",
    /// which the print reads as "onetext"); the engine's fill columns hold "one" and its padding. The column widths are the
    /// whole difference: every word is on Chromium's baseline, the words of tables a and c and the first column's where
    /// Chromium prints them, and the second column's words of b and d one distance to the right of Chromium's.
    /// </summary>
    [Theory]
    [InlineData("twd-table-narrow")]
    public void A_table_of_a_given_width_in_the_fixed_layout_is_never_narrower_than_its_content_where_Chromium_lets_the_content_overflow(string name)
    {
        var (engine, chromium) = (Engine(name), Chromium(name));
        var ours = engine.Pages.Single().Words;
        var theirs = chromium.Pages.Single().Words;
        var starts = ours.Where(word => word.Text == "one").Select(word => word.Baseline).ToList();
        starts.Select(ChromiumLayout.WholePixel).Should().HaveCount(4).And.Equal(theirs.Where(word => word.Text.StartsWith("one", StringComparison.Ordinal)).Select(word => word.Baseline),
            "every table starts on Chromium's baseline, which Chromium paints at the nearest whole pixel");
        theirs.Single(word => word.Text == "onetext").Baseline.Should().Be(ChromiumLayout.WholePixel(starts[3]), "in table d Chromium's \"one\" overflows into \"text\"");

        // Each table's second column below its first line: "with", "more", "words".
        PrintedWord[] Column(IEnumerable<PrintedWord> words, int table) =>
            [.. words.Where(word => word.Baseline > starts[table] + 1 && word.Baseline < starts[table] + 40 && word.Text is "with" or "more" or "words")];
        for (var table = 0; table < 4; table++)
        {
            var (mine, chromiums) = (Column(ours, table), Column(theirs, table));
            mine.Select(word => word.Text).Should().Equal(chromiums.Select(word => word.Text));
            mine.Zip(chromiums).Should().OnlyContain(pair => Math.Abs(ChromiumLayout.WholePixel(pair.First.Baseline) - pair.Second.Baseline) < HtmlCases.Bound, "on Chromium's baselines");
            var shifts = mine.Zip(chromiums, (first, second) => first.X0 - second.X0).ToList();
            shifts.Should().OnlyContain(shift => Math.Abs(shift - shifts[0]) < 0.01, "the column's words move by one distance");
            if (table is 0 or 2)
            {
                shifts[0].Should().BeApproximately(0, HtmlCases.Bound, "the automatic layout widens the table to its content in both");
            }
            else
            {
                shifts[0].Should().BeGreaterThan(1, "the engine's first column holds \"one\" and its padding");
            }
        }

        ours.Where(word => word.Text == "one").Zip(theirs.Where(word => word.Text.StartsWith("one", StringComparison.Ordinal)))
            .Should().OnlyContain(pair => Math.Abs(pair.First.X0 - pair.Second.X0) < HtmlCases.Bound, "the first column starts where Chromium's does");
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
    /// A row whose cell's lines fill the page area and whose padding does not fit with them, at the top of the first page:
    /// Chromium leaves the first page blank and prints the row whole on the second, its bottom padding and line passing the
    /// page end; the engine cuts the row as a last resort before its last line, because a table at the top of a page
    /// cannot move to a better page (docs/architecture.md, "Known limits"; the code front-end's
    /// <c>TableBreakReferenceTests</c> pins the same pages). Cases: eight lines with 3 pt padding, 151.5 pt in a 150 pt
    /// page area, then "z" (<c>fr-f4-padding-page-start</c>); twelve lines with 12 pt bottom padding, 157.5 pt, the
    /// padding and line passing the page end of Chromium's second page by 7.5 pt (<c>a2-row-end-pad-past</c>), and the
    /// same followed by "z" (<c>a2-row-end-pad-past-after</c>). Where the page breaks is the whole difference: the
    /// engine's first page is Chromium's second without its last line, every word and line where Chromium prints it.
    /// </summary>
    /// <param name="name">The case.</param>
    /// <param name="engineTexts">The engine's pages, as their words.</param>
    /// <param name="chromiumTexts">Chromium's pages.</param>
    /// <param name="lastBaseline">The baseline of the row's last line on Chromium's second page.</param>
    [Theory]
    [InlineData("fr-f4-padding-page-start", new[] { "a0 a1 a2 a3 a4 a5 a6", "a7 z" }, new[] { "", "a0 a1 a2 a3 a4 a5 a6 a7", "z" }, 155.25)]
    [InlineData("a2-row-end-pad-past", new[] { "a0 a1 a2 a3 a4 a5 a6 a7 a8 a9 a10", "a11" }, new[] { "", "a0 a1 a2 a3 a4 a5 a6 a7 a8 a9 a10 a11", "" }, 154.5)]
    [InlineData("a2-row-end-pad-past-after", new[] { "a0 a1 a2 a3 a4 a5 a6 a7 a8 a9 a10", "a11 z" }, new[] { "", "a0 a1 a2 a3 a4 a5 a6 a7 a8 a9 a10 a11", "z" }, 154.5)]
    public void At_the_top_of_a_page_such_a_row_is_cut_where_Chromium_leaves_a_blank_page(string name, string[] engineTexts, string[] chromiumTexts, double lastBaseline)
    {
        var (engine, chromium) = (Engine(name), Chromium(name));

        engine.PageTexts.Should().Equal(engineTexts);
        chromium.PageTexts.Should().Equal(chromiumTexts);
        chromium.Pages[0].Words.Should().BeEmpty();
        chromium.Pages[0].Rectangles.Should().BeEmpty("Chromium's first page is blank");
        var second = chromium.Pages[1];
        second.Words[^1].Baseline.Should().Be(lastBaseline);
        var cut = second with { Number = 1, Words = [.. second.Words.SkipLast(1).Select(word => word with { Page = 1 })], Rectangles = [.. second.Rectangles.Select(line => line with { Page = 1 })] };
        HtmlCases.Difference(new PrintedPages([engine.Pages[0]]), new PrintedPages([cut])).Should().BeNull("the row is cut where the page ends, as Chromium lays it out");
    }

    /// <summary>
    /// A row at the top of a page whose tallest cell passes the page end only by its bottom padding, beside cells that
    /// end above the page end (12 pt top and bottom padding, 18 pt lines, page area 12 to 162; then "z"): both keep every
    /// line on page 2 and continue on page 3 with what passed of the tallest cell, its bottom padding past the page end
    /// and the line below (<c>TableEndOverflow.ContinuesOnNextPage</c>). Chromium also gives page 3 the whole bottom
    /// padding of the cell that ended above the page end, the largest of them where there are two, though it had its
    /// room on page 2: empty space printed a second time, which no writer wants; the engine continues only what passed
    /// (docs/architecture.md, "Known limits"). Chromium's line below the row lies, as printed, 13.125 pt below page 3's
    /// top beside 12 pt of padding (<c>fr-row-later-page-padding-overflows-two-cells</c>), 7.125 pt beside 6 pt
    /// (<c>-two-cells-pad-6</c>) and 19.125 pt beside 6 and 18 pt (<c>-three-cells</c>); the engine's 1.125 pt, as
    /// Chromium prints a row without such a cell (<c>fr-row-later-page-padding-overflows</c>, equal). That padding is the
    /// whole difference: with it put back into the engine's page 3 at the top of that line (the line and what follows
    /// lowered by it, the cells' sides lengthened), the engine's pages are Chromium's.
    /// </summary>
    /// <param name="name">The case.</param>
    /// <param name="added">The bottom padding Chromium gives page 3 again: the largest of the cells that ended.</param>
    /// <param name="chromiumLine">The centre of Chromium's line below the row on page 3, below the page's top, as printed.</param>
    [Theory]
    [InlineData("fr-row-later-page-padding-overflows-two-cells", 12, 13.125)]
    [InlineData("fr-row-later-page-padding-overflows-two-cells-pad-6", 6, 7.125)]
    [InlineData("fr-row-later-page-padding-overflows-three-cells", 18, 19.125)]
    public void Beside_a_cell_whose_bottom_padding_passes_the_page_end_the_cells_that_ended_give_the_next_page_no_padding_where_Chromium_gives_it_theirs_again(
        string name, double added, double chromiumLine)
    {
        var (engine, chromium) = (Engine(name), Chromium(name));
        static PrintedBox Line(PrintedPage page) => page.Rectangles.Where(box => box.X1 - box.X0 > box.Y1 - box.Y0).MaxBy(box => box.Y0)!;

        engine.PageTexts.Should().Equal(chromium.PageTexts);
        engine.PageTexts[2].Should().Be("z");
        var (theirs, ours) = (chromium.Pages[2], engine.Pages[2]);
        ((Line(theirs).Y0 + Line(theirs).Y1) / 2).Should().Be(12 + chromiumLine);
        ((ChromiumLayout.WholePixel(Line(ours).Y0) + ChromiumLayout.WholePixel(Line(ours).Y1)) / 2).Should().Be(13.125, "the engine's line, printed on whole pixels");
        HtmlCases.Difference(engine, chromium).Should().NotBeNull();

        var at = Line(ours).Y0;
        var lowered = ours with
        {
            Words = [.. ours.Words.Select(word => word.Baseline > at ? word with { Baseline = word.Baseline + added } : word)],
            Rectangles = [.. ours.Rectangles.Select(box => box.Y0 >= at ? box with { Y0 = box.Y0 + added, Y1 = box.Y1 + added } : box.Y1 > at ? box with { Y1 = box.Y1 + added } : box)],
        };
        HtmlCases.Difference(new PrintedPages([.. engine.Pages.Take(2), lowered]), chromium).Should().BeNull("the padding given again is the whole difference");
    }

    /// <summary>
    /// Bottom padding taller than a page area, then a paragraph (case <c>fr-continued-pad-past-taller-than-page</c>: 220 pt
    /// of padding below ten lines that end at 132 pt on page 2, 190.75 pt past the page end with the line below, in a page
    /// area of 150 pt): both paint the cell's sides over all of page 3 and down to 52.5 pt on page 4, with the table's bottom
    /// line there (<c>TableEndOverflow.ContinuesOnNextPage</c>). Chromium starts "z" 0.75 pt below page 4's top, its
    /// baseline at 22.5, drawn over the sides that still pass; the engine starts it below what passed, at 62.496 (52.746 +
    /// 9.75), as where what passed fits one page (docs/architecture.md, "Known limits"). Text drawn over a table is not what
    /// a writer wants, as with what follows a sliced image (above). Where "z" starts is the whole difference: with it moved
    /// to Chromium's baseline the engine's pages are Chromium's; with a row after the table, which Chromium starts below the
    /// padding, the case is equal (<c>-next-row</c>).
    /// </summary>
    [Theory]
    [InlineData("fr-continued-pad-past-taller-than-page")]
    public void What_follows_bottom_padding_taller_than_a_page_starts_below_it_where_Chromium_draws_it_over_the_padding(string name)
    {
        var (engine, chromium) = (Engine(name), Chromium(name));
        var (theirs, ours) = (chromium.Pages[3], engine.Pages[3]);

        engine.PageTexts.Should().Equal(chromium.PageTexts);
        engine.PageTexts[3].Should().Be("z");
        theirs.Words.Single().Baseline.Should().Be(22.5);
        theirs.Rectangles.Max(box => box.Y1).Should().Be(52.5, "Chromium draws z over the sides and above the bottom line");
        ours.Words.Single().Baseline.Should().BeApproximately(62.496, 0.0005);
        ours.Words.Single().Baseline.Should().BeGreaterThan(ours.Rectangles.Max(box => box.Y1), "the engine starts z below what passed");
        HtmlCases.Difference(engine, chromium).Should().NotBeNull();

        var moved = new PrintedPages([.. engine.Pages.Take(3), ours with { Words = [ours.Words.Single() with { Baseline = 22.5 }] }]);
        HtmlCases.Difference(moved, chromium).Should().BeNull("where z starts is the whole difference");
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
    [Theory]
    [InlineData("fr-old-row-taller-by-padding")]
    public void A_row_taller_than_the_page_only_through_its_cell_padding_starts_on_a_page_that_Chromium_leaves_blank(string name)
    {
        var (engine, chromium) = (Engine(name), Chromium(name));

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
    [Theory]
    [InlineData("fr-old-margin-at-row-cut")]
    public void A_margin_dropped_at_a_row_cut_does_not_stay_in_the_row_s_height(string name)
    {
        var (engine, chromium) = (Engine(name), Chromium(name));
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
    [Theory]
    [InlineData("a4-image-rowspan")]
    public void An_image_in_a_cell_that_spans_two_rows_follows_the_engine_s_rules_for_rows_and_spanning_cells(string name)
    {
        var (engine, chromium) = (Engine(name), Chromium(name));
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
    [Theory]
    [InlineData("tf-pages")]
    public void A_footer_group_is_drawn_once_after_the_body_where_Chromium_repeats_it_on_every_page(string name)
    {
        var (engine, chromium) = (Engine(name), Chromium(name));
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
    [Theory]
    [InlineData("pm-family-unused")]
    public void A_face_that_only_a_margin_box_uses_is_drawn_where_Chromium_prints_its_fallback_face(string name)
    {
        var (engine, chromium) = (Engine(name), Chromium(name));
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
    /// engine's, which starts it there (0.375 pt; the comparison paints a baseline on the whole pixel, and on page 7 of
    /// <c>a4-header-image-360-image</c> and <c>a4-header-two-image-rows</c> that half grid line crosses a pixel: "after" is
    /// laid out at 88.875 and painted at 89.25, Chromium's at 88.5, a difference their status files list).
    /// Cases: a 360 pt image, then a 408 pt image (<c>a4-header-image-360-image</c>, pages 3 and 4); a row, a 408 pt image,
    /// a row (<c>a4-header-row-image-row</c>, pages 4 and 5); a 408 pt image, a row, a 408 pt image
    /// (<c>a4-header-image-row-image</c>, pages 3 and 4); two 408 pt images (<c>a4-header-two-image-rows</c>, pages 3 and 4).
    /// The same where the header is repeated over the rest of a 228 pt image because a cell of its row beside it goes on
    /// (Lato 10.5 pt): text, then the empty part of a cell to the row's end (<c>a4-header-image-beside-lines</c>, page 3;
    /// Chromium's "p26" on page 3, the engine's on page 4, as it starts the text beside the rest that much lower), or only
    /// that empty part (<c>a4-header-image-228-beside-fit</c>, <c>-beside-line</c>, <c>-beside-two-pages</c>,
    /// <c>-background-beside</c>, page 3; <c>a4-header-row-image-228-beside-line</c>, page 4), and over the rest of a 228 pt
    /// image in a table nested in the cell, whose part goes on (<c>a4-header-nested-image-228</c>, page 3). There every word below the rest
    /// on the last such page is where Chromium prints it, moved down by the header's room (from the top of the page area to
    /// the centre of the header's line, 16.125 pt) and painted at Chromium's whole pixel.
    /// </summary>
    /// <param name="name">The case.</param>
    /// <param name="first">The first page where the header is repeated over a slice.</param>
    /// <param name="last">The last such page.</param>
    /// <param name="header">The baseline of the header's "H" in both prints.</param>
    /// <param name="below">The first word below the rest on the last such page that is compared so, or null.</param>
    [Theory]
    [InlineData("a4-header-image-360-image", 3, 4, 24.75, null)]
    [InlineData("a4-header-row-image-row", 4, 5, 24.75, null)]
    [InlineData("a4-header-image-row-image", 3, 4, 24.75, null)]
    [InlineData("a4-header-two-image-rows", 3, 4, 24.75, null)]
    [InlineData("a4-header-image-beside-lines", 3, 3, 24.0, "p20")]
    [InlineData("a4-header-image-228-beside-fit", 3, 3, 24.0, "after")]
    [InlineData("a4-header-image-228-beside-line", 3, 3, 24.0, "after")]
    [InlineData("a4-header-image-228-beside-two-pages", 3, 3, 24.0, "after")]
    [InlineData("a4-header-image-228-background-beside", 3, 3, 24.0, "after")]
    [InlineData("a4-header-row-image-228-beside-line", 4, 4, 24.0, "after")]
    [InlineData("a4-header-nested-image-228", 3, 3, 24.0, "after")]
    public void A_repeated_header_is_drawn_above_a_slice_where_Chromium_draws_it_over_the_slice(string name, int first, int last, double header, string? below)
    {
        var (engine, chromium) = (Engine(name), Chromium(name));
        var before = (PrintedPages pages) => new PrintedPages([.. pages.Pages.Take(first - 1)]);

        engine.Pages.Length.Should().Be(chromium.Pages.Length);
        HtmlCases.Difference(before(engine), before(chromium)).Should().BeNull("the pages before the slices are the same");
        for (var number = first; number <= last; number++)
        {
            var (ours, theirs) = (engine.Pages[number - 1], chromium.Pages[number - 1]);
            theirs.Words.Single(word => word.Text == "H").Baseline.Should().Be(header);
            theirs.Images.Single().Y0.Should().Be(12, "Chromium's slice starts at the top of the page area");
            theirs.Images.Single().Y1.Should().BeGreaterThanOrEqualTo(28.5, "and the header is drawn over it (the last slice of the 360 pt image lies wholly under it)");
            ours.Words.Single(word => word.Text == "H").Baseline.Should().Be(header);
            ours.Images.Single().Y0.Should().BeApproximately(28.125, 1e-6, "the engine's slice starts below the header, at the centre of its line");
        }

        if (below is not null)
        {
            var (ours, theirs) = (engine.Pages[last - 1], chromium.Pages[last - 1]);
            var after = (PrintedPage page) => page.Words.SkipWhile(word => word.Text != below).ToList();
            var (mine, chromiums) = (after(ours), after(theirs));
            mine.Select(word => word.Text).Should().Equal(chromiums.Take(mine.Count).Select(word => word.Text));
            mine.Select(word => ChromiumLayout.WholePixel(word.Baseline - 16.125))
                .Should().Equal(chromiums.Take(mine.Count).Select(word => word.Baseline), "below the rest only the header's room differs");
            var moved = chromiums.Skip(mine.Count).Select(word => word.Text).ToList();
            engine.Pages.Skip(last).Take(1).SelectMany(page => page.Words).Where(word => word.Text is not ("H" or "G")).Take(moved.Count).Select(word => word.Text)
                .Should().Equal(moved, "what that room pushes past the page end starts the engine's next page");
        }

        HtmlCases.Difference(engine, chromium).Should().NotBeNull();
    }

    /// <summary>
    /// What follows the row of an image sliced over pages in a table cell: Chromium lays the image out whole on the page
    /// where it starts, ends the row on the next page as tall as the rest of the image, and starts what follows there:
    /// in a row of two or more cells the next row, and after any table the content after it, below the row's end, or at
    /// the top of the next page, drawn over the slices still to come. The engine starts what follows below the last
    /// slice, so no text is drawn over an image (docs/architecture.md, "Known limits"); it centres the line that ends the
    /// row on the end of the last slice (<c>SliceRestEnd.AtLineCentre</c>), or half that line lower where the other cell of
    /// the row goes on with nothing left beside the slice and keeps its half, which prints where Chromium's is. That place is the whole
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
    /// <param name="rowEnd">How far below the slice's bottom edge the line that ends the sliced row is centred: half of it
    /// where a cell beside the slice keeps its half (<c>a4-image-in-cell</c>, <c>a4-slice4-cell-row-after</c>), else 0.</param>
    /// <param name="linesApart">How far below the line at the slice's bottom edge the engine draws the outer row's line,
    /// where Chromium draws both in one place; 0 where there is one line.</param>
    [Theory]
    [InlineData("a4-image-in-cell", "after", 4, 24.75, 4, 72.75, 0.375, 0)]
    [InlineData("a4-row-image-row", "after", 5, 24.75, 5, 72.375, 0, 0)]
    [InlineData("a4-two-image-rows", "after", 8, 24.75, 8, 72.375, 0, 0)]
    [InlineData("a4-slice4-cell-row-after", "after", 4, 23.25, 5, 71.25, 0.375, 0)]
    [InlineData("a4-image-nested-table", "after", 4, 24.75, 4, 73.125, 0, 0.75)]
    public void What_follows_the_row_of_an_image_sliced_in_a_table_starts_below_its_last_slice_where_Chromium_draws_it_over_the_slices(
        string name, string first, int chromiumPage, double chromiumBaseline, int enginePage, double engineBaseline, double rowEnd, double linesApart)
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
        var from = sliceEnd(engine, enginePage) + rowEnd;
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
    /// A line break beside an image next to a word joiner (U+2060) or a zero width joiner (U+200D) (case <c>ii-joiners</c>,
    /// a deliberate deviation from CSS Text 3 §5.5 that follows Chromium; the code front-end's <c>InlineImageTests</c> pins
    /// it): the lines are Chromium's, and the text layer differs. Chromium's print keeps the joiners in the PDF's text, the
    /// engine draws no glyph for them; and as for the non-joiner of <c>sr-zwnj</c> the read-back gives a joiner that ends
    /// a word the PDF's default width (<c>/DW 256</c>, 3.072 pt at 12 pt), so such a word reads that much longer. With the
    /// joiners taken out of Chromium's words and that width out of their ends, the case is equal.
    /// </summary>
    [Theory]
    [InlineData("ii-joiners")]
    public void A_line_breaks_beside_an_image_also_next_to_a_joiner_like_Chromium_whose_text_keeps_the_joiners(string name)
    {
        var (engine, chromium) = (Engine(name), HtmlCases.ChromiumPrint(name));
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
    [Theory]
    [InlineData("ws-border")]
    public void Where_an_element_that_wraps_differently_from_its_parent_ends_the_engine_follows_the_character_before_not_Chromium_in_the_HTML_case(string name)
    {
        var (engine, chromium) = (Engine(name), HtmlCases.ChromiumPrint(name));
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
    [Theory]
    [InlineData("ws-image-border")]
    public void Beside_an_image_at_the_end_of_an_element_that_wraps_differently_the_engine_follows_the_character_before_not_Chromium_in_the_HTML_case(string name)
    {
        var (engine, chromium) = (Engine(name), HtmlCases.ChromiumPrint(name));
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
    [Theory]
    [InlineData("ws-soft")]
    public void A_soft_hyphen_and_a_zero_width_space_break_by_the_white_space_mode_like_Chromium_whose_text_keeps_them(string name)
    {
        var (engine, chromium) = (Engine(name), HtmlCases.ChromiumPrint(name));
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
    /// A dash before a no-break space or a no-break hyphen, and a soft hyphen after a no-break space (case <c>lb-hyphens</c>,
    /// 30, 45 and 60 pt wide paragraphs): the lines are Chromium's, which break after the dash and after the soft hyphen
    /// where UAX #14 alone does not (LB12a, LB12, LB20a; the HTML front-end's line-break rules are Chromium's,
    /// <c>LineBreakRules.Chromium</c>). The text layer differs as in <c>ws-soft</c> (docs/architecture.md, a listed
    /// difference of the text layer): Chromium's PDF text keeps the soft hyphen that starts a word after the no-break space
    /// and adds U+2010 at a shown hyphen; the engine's keeps only a shown soft hyphen. With Chromium's words read as the
    /// engine's text, the case is equal.
    /// </summary>
    [Theory]
    [InlineData("lb-hyphens")]
    public void A_dash_and_a_soft_hyphen_beside_a_no_break_space_break_like_Chromium_whose_text_keeps_the_soft_hyphen(string name)
    {
        var (engine, chromium) = (Engine(name), HtmlCases.ChromiumPrint(name));
        chromium.Pages[0].Words.Select(word => word.Text).Should().ContainInOrder("10\u2013", "12", "a", "\u00ADlong", "e\u2011mail", "\u00AD\u2010", "address");
        engine.Pages[0].Words.Select(word => word.Text).Should().ContainInOrder("10\u2013", "12", "a", "long", "e\u2011mail", "\u00AD", "address");

        var read = new PrintedPages([.. chromium.Pages.Select(page => page with { Words = [.. page.Words.Select(word => word with { Text = EngineText(word.Text) })] })]);
        HtmlCases.Difference(engine, read).Should().BeNull("the text layer is the whole difference");
    }

    /// <summary>
    /// A dash after a space and a soft hyphen after a no-break space before a sign of class AI, and two hyphens before
    /// letters beyond ASCII (case <c>lb-dash-before-sign</c>, 30, 45 and 60 pt wide paragraphs): the lines are Chromium's
    /// (<c>LineBreakRules.Chromium</c>, a character of class AI read as AL), but for one. In the 30 pt paragraph Chromium
    /// ends a line after <c>x-</c> and starts the next with <c>-Ωmega</c>, as it finds the places of each line anew from
    /// its start, where UAX #14 LB20a keeps the letter after a hyphen at the start of a text; the engine finds them once for
    /// the whole paragraph and breaks again after that hyphen (docs/architecture.md, the limit of the line-break rules;
    /// <c>ChromiumLineBreakingTests.A_line_that_starts_at_a_break_is_decided_anew_by_chromium_and_once_by_the_rules</c>).
    /// The text layer differs as in <c>ws-soft</c>: Chromium's PDF text keeps the soft hyphen that starts a word after the
    /// no-break space and adds U+2010 at a shown hyphen. With Chromium's words read as the engine's text, the line of
    /// <c>-Ωmega</c> is the whole difference of the page's text (the comparison stops at a page whose text differs; the
    /// status file lists every difference of the case).
    /// </summary>
    [Theory]
    [InlineData("lb-dash-before-sign")]
    public void A_dash_and_a_soft_hyphen_before_a_sign_break_like_Chromium_but_a_hyphen_that_starts_a_line_breaks_again(string name)
    {
        var (engine, chromium) = (Engine(name), HtmlCases.ChromiumPrint(name));
        chromium.Pages[0].Words.Select(word => word.Text).Should().ContainInOrder("\u2013\u00A712", "\u00AD\u00A74", "a-", "-\u00E9lan,", "why?-", "\u00E9cole", "x-", "-\u03A9mega");
        engine.Pages[0].Words.Select(word => word.Text).Should().ContainInOrder("\u2013\u00A712", "\u00A74", "a-", "-\u00E9lan,", "why?-", "\u00E9cole", "x-", "-", "\u03A9mega");

        var read = new PrintedPages([.. chromium.Pages.Select(page => page with { Words = [.. page.Words.Select(word => word with { Text = EngineText(word.Text) })] })]);
        HtmlCases.Differences(engine, read).Should().Equal(
            "page 1 reads 'See rules \u2013\u00A712 and \u2012\u00BD hour, the cut \u00A74 and art \u00AD \u00D72, a- -\u00E9lan, why?- \u00E9cole and x- - \u03A9mega here. "
            + "See rules \u2013\u00A712 and \u2012\u00BD hour, the cut \u00AD \u00A74 and art \u00D72, a- -\u00E9lan, why?- \u00E9cole and x-- \u03A9mega here.', "
            + "Chromium 'See rules \u2013\u00A712 and \u2012\u00BD hour, the cut \u00A74 and art \u00AD \u00D72, a- -\u00E9lan, why?- \u00E9cole and x- -\u03A9mega here. "
            + "See rules \u2013\u00A712 and \u2012\u00BD hour, the cut \u00AD \u00A74 and art \u00D72, a- -\u00E9lan, why?- \u00E9cole and x-- \u03A9mega here.'; "
            + "the engine's page 1: digest 90951a3dedeab262 (print.txt)");
    }

    /// <summary>
    /// Tables 1 pt wide with automatic layout, whose first cell holds two hyphens (or <c>?-</c>) before a letter beyond ASCII
    /// (case <c>lb-line-start-width</c>): the lines are Chromium's, for example <c>a--</c> and <c>élanxxxx</c>, and the first
    /// column is narrower. Such a column is as wide as the widest piece of its cell between two places where a line may end.
    /// Chromium measures that piece with the second hyphen, <c>-élanxxxx</c>, as it finds the places of a line that starts
    /// at that hyphen anew from the line's start, where UAX #14 LB20a keeps the letter after a hyphen at the start of a text;
    /// the engine finds the places once for the cell's text and measures <c>élanxxxx</c> (docs/architecture.md, the limit
    /// of the line-break rules; case <c>lb-dash-before-sign</c> shows the same limit as an extra line). So the first cell's
    /// box is a hyphen's width (3.9 pt) narrower, and the cell after it starts that much further left. With ASCII letters
    /// after the pair (<c>a--elanxxxx</c>) both measure the word and the table is equal. With each word of a second cell
    /// moved by its table's difference, the print is equal; the status file lists the three cell widths besides.
    /// </summary>
    [Theory]
    [InlineData("lb-line-start-width")]
    public void A_column_after_two_hyphens_before_a_letter_beyond_ASCII_is_narrower_than_Chromium_s_by_the_second_hyphen(string name)
    {
        var (engine, chromium) = (Engine(name), HtmlCases.ChromiumPrint(name));
        string[] lines = ["a--", "M", "\u00E9lanxxxx", "why?-", "M", "\u00E9colexx", "x--", "M", "\u03A9megaxxx", "a--", "M", "elanxxxx"];
        engine.Pages.Single().Words.Select(word => word.Text).Should().Equal(lines);
        chromium.Pages.Single().Words.Select(word => word.Text).Should().Equal(lines);

        var shifts = chromium.Pages.Single().Words.Where(word => word.Text == "M")
            .Zip(engine.Pages.Single().Words.Where(word => word.Text == "M"), (theirs, mine) => theirs.X0 - mine.X0).ToList();
        shifts.Take(3).Should().AllSatisfy(shift => shift.Should().BeApproximately(3.9, 0.02, "the second hyphen's width"));
        shifts[3].Should().BeApproximately(0, 0.02, "with ASCII letters both measure the word");

        var moved = new PrintedPages([.. chromium.Pages.Select(page => page with
        {
            Words = [.. page.Words.Select((word, index) => word.Text == "M" ? word with { X0 = word.X0 - shifts[index / 3], X1 = word.X1 - shifts[index / 3] } : word)],
        })]);
        HtmlCases.Difference(engine, moved).Should().BeNull("the width of the first column is the whole difference of the print");
    }

    /// <summary>
    /// Underlines that skip the ink of descenders, cut beside an image, at a shown soft hyphen and in ligatures (case
    /// <c>sk-misc</c>; the code front-end's <c>SkipInkTests</c> pins the same pieces): the pieces are Chromium's, compared
    /// as the status compares them (<see cref="HtmlCases.ChromiumPrint"/>); the text layer differs at the shown soft
    /// hyphen, where Chromium's PDF text adds U+2010 after it and the engine's has the soft hyphen only. With that character
    /// taken out of Chromium's words, the case is equal.
    /// </summary>
    [Theory]
    [InlineData("sk-misc")]
    public void An_image_a_shown_soft_hyphen_and_ligatures_cut_the_underline_like_Chromium_whose_text_adds_a_hyphen(string name)
    {
        var (engine, chromium) = (Engine(name), HtmlCases.ChromiumPrint(name));
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

    /// <summary>Chromium's print with each word's text read through <paramref name="text"/>.</summary>
    private static PrintedPages Read(PrintedPages pages, Func<string, string> text) =>
        new([.. pages.Pages.Select(page => page with { Words = [.. page.Words.Select(word => word with { Text = text(word.Text) })] })]);

    /// <summary>Where each line of the only page ends: its last word's end, lines in order.</summary>
    private static double[] LineEnds(PrintedPages pages) =>
        [.. pages.Pages.Single().Words.GroupBy(word => word.Baseline).OrderBy(line => line.Key).Select(line => line.Max(word => word.X1))];

    /// <summary>
    /// A link in a table's header row that repeats on every page: the engine writes its annotation from the first copy only,
    /// and the later copies are artifacts, which carry no link (ISO 14289-1 §7.18.1: an annotation is tagged, and an artifact
    /// is no structure; docs/architecture.md, "Known limits"). Chromium 159 writes the link twice on every page that shows
    /// the header, at the same rectangle (case <c>lk-header-row</c>): an annotation is not printed, so Chromium's is a fact
    /// and not the rule. The print is the same, and the engine's one link is Chromium's first.
    /// </summary>
    [Theory]
    [InlineData("lk-header-row")]
    public void A_link_in_a_repeated_header_row_leads_from_its_first_copy_only_where_Chromium_writes_it_twice_on_every_page(string name)
    {
        var boxes = HtmlCases.Boxes(name).Value;
        var pages = DocumentLayout.Run(boxes.Document, boxes.Fonts).Value;
        var chromium = HtmlCases.Links(HtmlCases.Cases, name);

        HtmlCases.Difference(PrintedPages.Engine(pages), Chromium(name)).Should().BeNull("the print is the same");
        chromium.Select(link => link.Page).Should().Equal([1, 1, 2, 2], "Chromium writes the header's link twice on each page");
        var first = chromium[0];
        chromium.Select(link => (link.X0, link.Y0, link.X1, link.Y1, link.Target)).Should().AllBeEquivalentTo((first.X0, first.Y0, first.X1, first.Y1, first.Target), "every copy at the same rectangle, to the same target");
        HtmlCases.LinkDifference(pages, [chromium[0]]).Should().BeNull("the engine's one link is Chromium's first");
    }

    /// <summary>
    /// A link with a box inside it (a styled span with a background, padding or border) has one area per line part, over its
    /// text and the boxes inside it (THE RULE FOR A LINK'S AREA): the link is one thing to click, described once. Chromium 159
    /// writes three rectangles on such a line: one for the link's text before the box, one at the box's border box, one for
    /// the text after it (case <c>ib-link-span-inside</c>: 51.75..81.75, 81.75..109.5 and 109.5..140.25 pt on the first line;
    /// a box at the link's start leaves two). An annotation is not printed, so Chromium's split is a fact and not the rule:
    /// ISO 32000-1 §12.5.6.5 lets a link annotation's <c>/Rect</c> be any rectangle. The print is the same, and each of the
    /// engine's areas is the rectangle around Chromium's of that line, so no part of the link Chromium marks lies outside it.
    /// </summary>
    [Theory]
    [InlineData("ib-link-span-inside")]
    public void A_link_around_a_box_has_one_area_per_line_part_where_Chromium_writes_one_before_the_box_one_for_it_and_one_after_it(string name)
    {
        var boxes = HtmlCases.Boxes(name).Value;
        var pages = DocumentLayout.Run(boxes.Document, boxes.Fonts).Value;
        var chromium = HtmlCases.Links(HtmlCases.Cases, name);
        var around = chromium.GroupBy(link => link.Target)
            .Select(line => new HtmlCases.PrintedLink(line.First().Page, line.Min(link => link.X0), line.Min(link => link.Y0), line.Max(link => link.X1), line.Max(link => link.Y1), line.Key)).ToList();

        HtmlCases.Status(name).Should().EndWith("(links.txt)", "every fact of the print is the same, and the link areas are compared last");
        chromium.GroupBy(link => link.Target).Select(line => line.Count()).Should().Equal([3, 3, 2], "Chromium splits each line part at the box");
        HtmlCases.Links(pages).Should().HaveCount(3, "the engine writes one area per line part");
        HtmlCases.LinkDifference(pages, around).Should().BeNull("each area is the rectangle around Chromium's of its line");
        HtmlCases.LinkDifference(pages, chromium).Should().Be("link 1 on page 1: x1 140.18, Chromium 81.75 (links.txt)");
    }

    /// <summary>
    /// A list item's bullet against a rounded box in the item (case <c>br-boxes-inside</c>): Chromium paints the marker as
    /// content of the line, after the backgrounds and borders of every block box in the flow (CSS 2.1 Appendix E, step 4
    /// before step 7), so its two discs come after the four rounded paths of the page; the engine paints a list item's label
    /// before the item's body, inside the label's structure element. Paths are compared in drawing order, so that order is
    /// the whole difference: Chromium's print with each disc moved to where the engine paints it is the engine's.
    /// </summary>
    [Fact]
    public void A_list_bullet_is_painted_before_the_rounded_box_in_its_item_where_Chromium_paints_it_after_every_box()
    {
        var (engine, chromium) = (Engine("br-boxes-inside"), Chromium("br-boxes-inside"));
        var theirs = chromium.Pages.Single();
        theirs.Paths.Select(path => path.Colour).Should().Equal("#e0f0ff", "#2050a0", "#e0f0ff", "#2050a0", "#000000", "#000000");

        var moved = theirs with { Paths = [theirs.Paths[0], theirs.Paths[1], theirs.Paths[4], theirs.Paths[2], theirs.Paths[3], theirs.Paths[5]] };
        HtmlCases.Difference(engine, new PrintedPages([moved])).Should().BeNull("the order of the bullet is the whole difference");
        HtmlCases.Difference(engine, chromium).Should().StartWith("page 1 path at (30, 70.5) paints 2cfc2/cfffc/fffff/cfffc/2cfc2 #000000, Chromium at (42, 62.25) ");
    }

    /// <summary>
    /// The same order with rounded list items (case <c>bri-list</c>): Chromium paints the three rounded backgrounds (and the
    /// second item's ring) before any of the three discs, the engine each disc before its own item's background, inside the
    /// label's structure element. The pixels are the same: Chromium's print with each disc moved to where the engine paints it
    /// is the engine's.
    /// </summary>
    [Theory]
    [InlineData("bri-list")]
    public void A_list_bullet_is_painted_before_its_rounded_item_where_Chromium_paints_it_after_every_item(string name)
    {
        var (engine, chromium) = (Engine(name), Chromium(name));
        var theirs = chromium.Pages.Single();
        theirs.Paths.Select(path => path.Colour).Should().Equal("#f0e0a0", "#f0e0a0", "#c03020", "#f0e0a0", "#000000", "#000000", "#000000");

        var moved = theirs with { Paths = [theirs.Paths[0], theirs.Paths[4], theirs.Paths[1], theirs.Paths[2], theirs.Paths[5], theirs.Paths[3], theirs.Paths[6]] };
        HtmlCases.Difference(engine, new PrintedPages([moved])).Should().BeNull("the order of the bullets is the whole difference");
        HtmlCases.Difference(engine, chromium).Should().StartWith("page 1 path at (18, 40.5) paints 2cfc2/cfffc/fffff/cfffc/2cfc2 #000000, Chromium at (30, 54) ");
    }

    /// <summary>
    /// A dash's edge on a sample of the comparison (case <c>bd-lengths-thick</c>): the sixteenth dash of a 4 px side of
    /// 229 px starts 184.1667 px along the side, and its two edges lie on samples of the area comparison (2.5 of 15 into a
    /// pixel). The engine places it as Chromium writes it, 184.16667 px in single precision; Chromium's print, read back
    /// through its page transform of .23999999, puts the edge a few millionths of a pixel to the other side of the sample.
    /// So one sample at each edge, in each of the four rows of the side, is counted the other way; every other dash of the
    /// 47 measured sides is equal.
    /// </summary>
    [Theory]
    [InlineData("bd-lengths-thick")]
    public void A_dash_whose_edge_lies_on_a_sample_differs_by_that_sample(string name)
    {
        var (engine, chromium) = (Engine(name), Chromium(name));
        var (ours, theirs) = (engine.Pages.Single().Paths, chromium.Pages.Single().Paths);
        ours.Should().HaveSameCount(theirs);

        var differing = ours.Zip(theirs).Where(pair => !pair.First.Rows.SequenceEqual(pair.Second.Rows)).ToList();
        differing.Should().ContainSingle();
        var (side, measured) = differing[0];
        side.Rows.Should().HaveCount(4, "a side 4 px wide");
        foreach (var (row, other) in side.Rows.Zip(measured.Rows))
        {
            Enumerable.Range(0, row.Length).Where(column => row[column] != other[column]).Should().Equal([184, 192], "the dash's two edges, 184.17 px and 192.17 px along the side");
            new[] { row[184], row[192] }.Should().Equal('c', '3');
            new[] { other[184], other[192] }.Should().Equal('d', '2');
        }
    }
}
