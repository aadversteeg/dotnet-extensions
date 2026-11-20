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
        chromium.Rectangles.Single().X1.Should().Be(595.5, "Chromium's page area is 794 px wide");
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
    /// The fixed table layout at width 100% with three columns without a width, the first holding a word wider than an
    /// even share of the 276 pt page area (case <c>tl-fixed-fill-wide-word</c>): Chromium gives each column its share,
    /// 92 pt, and lets the word overflow into the next column (CSS 2.1 §17.5.2.1: in the fixed layout the widths do not
    /// depend on the content). The engine's columns without a width are fill columns, which are never narrower than their
    /// content's min-content width: the first column is as wide as the word and its padding, and the other two share what
    /// is left equally. Where every cell's content fits its share, both give the same columns.
    /// </summary>
    [Fact]
    public void A_fill_column_of_the_fixed_layout_is_never_narrower_than_its_content_where_Chromium_lets_the_content_overflow()
    {
        var (engine, chromium) = (Engine("tl-fixed-fill-wide-word").Pages.Single(), Chromium("tl-fixed-fill-wide-word").Pages.Single());
        var word = chromium.Words.Single(word => word.Text == "Pneumonoultramicroscopic");
        static double[] Starts(PrintedPage page) => [.. page.Words.Where(word => word.Text is "two" or "three").Select(word => word.X0)];

        static double[] Lines(PrintedPage page) => [.. page.Rectangles.Where(line => line.Height > line.X1 - line.X0).Select(line => line.X0).Distinct().Order()];

        Lines(chromium).Should().Equal([12, 103.5, 195.75, 287.25], "Chromium's columns are even shares, painted at whole pixels (a line per row)");
        word.X1.Should().BeGreaterThan(Starts(chromium)[0], "Chromium lets the word overflow into the second column");
        Starts(engine)[0].Should().BeGreaterThan(word.X1, "the engine's first column holds the word");
        var lines = Lines(engine);
        (lines[2] - lines[1]).Should().BeApproximately(lines[3] - lines[2], 0.01, "the other two columns share what is left equally");
        engine.Words.Single(other => other.Text == word.Text).X0.Should().BeApproximately(word.X0, 0.01);
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

    /// <summary>Chromium's print with each word's text read through <paramref name="text"/>.</summary>
    private static PrintedPages Read(PrintedPages pages, Func<string, string> text) =>
        new([.. pages.Pages.Select(page => page with { Words = [.. page.Words.Select(word => word with { Text = text(word.Text) })] })]);

    /// <summary>Where each line of the only page ends: its last word's end, lines in order.</summary>
    private static double[] LineEnds(PrintedPages pages) =>
        [.. pages.Pages.Single().Words.GroupBy(word => word.Baseline).OrderBy(line => line.Key).Select(line => line.Max(word => word.X1))];
}
