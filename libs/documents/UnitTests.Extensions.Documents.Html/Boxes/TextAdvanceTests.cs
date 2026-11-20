using System.IO;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;

namespace UnitTests.Extensions.Documents.Html.Boxes;

/// <summary>
/// Text at font sizes that are not a whole number of CSS pixels, through the HTML front-end, against Chromium's print
/// (<c>tools/reference/chromium</c>, cases <c>ta-*</c>; <see cref="FontSizeRounding.FlooredToPixelFractions"/>): every
/// word's start and end within 0.01 pt, not snapped to pixels. Under <see cref="FontSizeRounding.Exact"/> the same lines
/// are wider by a constant share of their width, which the last word of the 8 pt line shows.
/// </summary>
public class TextAdvanceTests
{
    private const double Bound = 0.01;

    private static PrintedPages Chromium(string name) =>
        PrintedPages.Parse(File.ReadAllText(Path.Combine(HtmlCases.Folder(), "..", "measured", name + ".print.txt")));

    private static PrintedPages Engine(string name, FontSizeRounding rounding)
    {
        var boxes = HtmlCases.Boxes(name).Value;
        var document = boxes.Document with { Profile = boxes.Document.Profile with { FontSizeRounding = rounding } };
        return PrintedPages.Engine(DocumentLayout.Run(document, boxes.Fonts).Value);
    }

    /// <summary>The words of both prints, in the engine's order, each with Chromium's word of the same text on the same
    /// baseline (a mark Chromium reads as a word of its own is not in these cases).</summary>
    private static IEnumerable<(PrintedWord Engine, PrintedWord Chromium)> Pairs(string name, FontSizeRounding rounding)
    {
        var (engine, chromium) = (Engine(name, rounding).Pages.Single().Words, Chromium(name).Pages.Single().Words);
        engine.Select(word => word.Text).Should().Equal(chromium.Select(word => word.Text));
        return engine.Zip(chromium);
    }

    /// <summary>
    /// Left-aligned lines, one font size each: seven sizes in points (<c>ta-sizes</c>: 8, 9, 10, 10.5, 11, 12, 13 pt), sizes
    /// given in pixels (<c>ta-px</c>: 10.667, 10.66, 10.65625, 14.667, 14.66, 11 px) and combining marks on bases without
    /// a precomposed form at 8 pt, 10.65625 px and 12 pt (<c>ta-marks</c>; each mark at its base's pen plus the anchor
    /// difference at the floored size).
    /// </summary>
    /// <param name="name">The case.</param>
    [Theory]
    [InlineData("ta-sizes")]
    [InlineData("ta-px")]
    [InlineData("ta-marks")]
    public void Every_word_starts_and_ends_where_Chromium_prints_it(string name)
    {
        Pairs(name, FontSizeRounding.FlooredToPixelFractions).Should().OnlyContain(
            pair => System.Math.Abs(pair.Engine.X0 - pair.Chromium.X0) <= Bound && System.Math.Abs(pair.Engine.X1 - pair.Chromium.X1) <= Bound);
    }

    /// <summary>
    /// Right-aligned lines (<c>ta-right</c>: 8, 10.5 and 11 pt against a content edge at 600 pt): the words are where
    /// Chromium prints them, so the start of each line moves with the rounding. Chromium ends the line at the content
    /// edge less its text item's width ceiled to a layout unit (1/64 px, 0.0117 pt), the engine at the edge: one layout
    /// unit more than the bound.
    /// </summary>
    [Fact]
    public void A_right_aligned_line_starts_where_Chromium_prints_it()
    {
        Pairs("ta-right", FontSizeRounding.FlooredToPixelFractions).Should().OnlyContain(
            pair => System.Math.Abs(pair.Engine.X0 - pair.Chromium.X0) <= ChromiumLayout.LayoutUnit + Bound && System.Math.Abs(pair.Engine.X1 - pair.Chromium.X1) <= ChromiumLayout.LayoutUnit + Bound);
    }

    /// <summary>At the font size as it is given the 8 pt line ends 0.27 pt after Chromium's (×1.000977 over 276 pt); the 9 pt
    /// line, a whole number of pixels, does not move.</summary>
    [Fact]
    public void At_the_exact_size_the_line_is_wider_than_Chromium_s_by_the_rounding()
    {
        var pairs = Pairs("ta-sizes", FontSizeRounding.Exact).ToList();
        var (eightPoint, ninePoint) = (pairs.First(pair => pair.Chromium.Text == "fox"), pairs.Where(pair => pair.Chromium.Text == "fox").ElementAt(1));

        (eightPoint.Engine.X1 - eightPoint.Chromium.X1).Should().BeInRange(0.25, 0.29);
        System.Math.Abs(ninePoint.Engine.X1 - ninePoint.Chromium.X1).Should().BeLessThanOrEqualTo(Bound);
    }
}
