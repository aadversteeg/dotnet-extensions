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
}
