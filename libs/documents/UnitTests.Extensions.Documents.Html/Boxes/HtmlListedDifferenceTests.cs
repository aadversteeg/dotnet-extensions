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
    /// <summary>Where the page area of the cases ends on the right: 13 px margin, 374 px wide (300 pt page, 10 pt margins).</summary>
    private const double AreaLeft = 9.75;
    private const double AreaWidth = 280.5;

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
    /// scale: it prints the word at its size and lets it stick out of the page area.
    /// </summary>
    [Theory]
    [InlineData("h-wide-word-little")]
    [InlineData("h-wide-word-much")]
    public void Content_wider_than_the_page_area_is_not_shrunk_as_Chromium_shrinks_the_page(string name)
    {
        var engine = Engine(name).Pages.Single().Words[0];
        engine.FontSize.Should().Be(12);
        engine.X1.Should().BeGreaterThan(AreaLeft + AreaWidth, "the engine lets the word stick out");

        var scale = System.Math.Min(1.5, (engine.X1 - engine.X0) / AreaWidth);
        Chromium(name).Pages.Single().Words.Should().OnlyContain(word => System.Math.Abs(word.FontSize - (12 / scale)) < 0.005,
            "Chromium prints every word of the page shrunk by one factor");
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
    public void A_ligature_across_the_border_of_an_inline_element_keeps_every_character_where_Chromium_keeps_the_first_run_s()
    {
        var (engine, chromium) = (Engine("sr-same-font"), Chromium("sr-same-font"));
        chromium.Pages.Single().Text.Should().Contain("fix fx office ofce");
        engine.Pages.Single().Text.Should().NotContain("fx").And.NotContain("ofce");
        HtmlCases.Difference(engine, Read(chromium, text => text switch { "fx" => "fix", "ofce" => "office", _ => text })).Should().BeNull();
    }

    /// <summary>
    /// A zero width non-joiner (U+200C) in a word: Chromium's print keeps it in the PDF's text, the engine's PDF does not
    /// (it draws no glyph for it; its PDF read back with <c>positions.py</c> reads "To", "T" "o", "T" "o" "T" for case
    /// <c>sr-zwnj</c>). With the non-joiners taken out of Chromium's words the texts agree.
    /// </summary>
    [Fact]
    public void A_zero_width_non_joiner_is_not_in_the_text_where_Chromium_keeps_it()
    {
        var (engine, chromium) = (Engine("sr-zwnj"), Chromium("sr-zwnj"));
        chromium.Pages.Single().Words.Select(word => word.Text).Should().Equal("T\u200Co", "T", "\u200Co", "T", "o\u200C", "T");
        engine.Pages.Single().Words.Select(word => word.Text).Should().Equal(
            Read(chromium, text => text.Replace("\u200C", string.Empty, StringComparison.Ordinal)).Pages.Single().Words.Select(word => word.Text));
    }

    /// <summary>Chromium's print with each word's text read through <paramref name="text"/>.</summary>
    private static PrintedPages Read(PrintedPages pages, Func<string, string> text) =>
        new([.. pages.Pages.Select(page => page with { Words = [.. page.Words.Select(word => word with { Text = text(word.Text) })] })]);

    /// <summary>Where each line of the only page ends: its last word's end, lines in order.</summary>
    private static double[] LineEnds(PrintedPages pages) =>
        [.. pages.Pages.Single().Words.GroupBy(word => word.Baseline).OrderBy(line => line.Key).Select(line => line.Max(word => word.X1))];
}
