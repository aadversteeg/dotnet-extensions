using System.IO;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;

namespace UnitTests.Extensions.Documents.Html.Boxes;

/// <summary>
/// Text at font sizes that are not a whole number of CSS pixels, through the HTML front-end, against Chromium's print
/// (<c>tools/reference/chromium</c>, cases <c>ta-*</c>; <see cref="FontSizeRounding.FlooredToPixelFractions"/> and
/// <see cref="LengthRounding.SixtyFourthPixels"/>, as the HTML front-end sets them): every word's start and end within
/// 0.01 pt, not snapped to pixels. Under <see cref="FontSizeRounding.Exact"/> the same lines are wider by a constant share
/// of their width, which the last word of the 8 pt line shows; under <see cref="LengthRounding.Exact"/> a line of many
/// text items ends early by the sum of the steps its items' widths are not rounded up by.
/// </summary>
public class TextAdvanceTests
{
    private const double Bound = 0.01;

    private static PrintedPages Chromium(string name) =>
        PrintedPages.Parse(File.ReadAllText(Path.Combine(HtmlCases.Folder(), "..", "measured", name + ".print.txt")));

    private static PrintedPages Engine(string name, FontSizeRounding rounding, LengthRounding lengths)
    {
        var boxes = HtmlCases.Boxes(name).Value;
        var document = boxes.Document with { Profile = boxes.Document.Profile with { FontSizeRounding = rounding, LengthRounding = lengths } };
        return PrintedPages.Engine(DocumentLayout.Run(document, boxes.Fonts).Value);
    }

    /// <summary>The words of both prints, in the engine's order, each with Chromium's word of the same text on the same
    /// baseline (a mark Chromium reads as a word of its own is not in these cases).</summary>
    private static IEnumerable<(PrintedWord Engine, PrintedWord Chromium)> Pairs(
        string name, FontSizeRounding rounding = FontSizeRounding.FlooredToPixelFractions, LengthRounding lengths = LengthRounding.SixtyFourthPixels)
    {
        var (engine, chromium) = (Engine(name, rounding, lengths).Pages.Single().Words, Chromium(name).Pages.Single().Words);
        engine.Select(word => word.Text).Should().Equal(chromium.Select(word => word.Text));
        return engine.Zip(chromium);
    }

    private static bool Near(PrintedWord engine, PrintedWord chromium) =>
        System.Math.Abs(engine.X0 - chromium.X0) <= Bound && System.Math.Abs(engine.X1 - chromium.X1) <= Bound;

    /// <summary>
    /// Left-aligned lines, one font size each: seven sizes in points (<c>ta-sizes</c>: 8, 9, 10, 10.5, 11, 12, 13 pt), sizes
    /// given in pixels (<c>ta-px</c>: 10.667, 10.66, 10.65625, 14.667, 14.66, 11 px) and combining marks on bases without
    /// a precomposed form at 8 pt, 10.65625 px and 12 pt (<c>ta-marks</c>; each mark at its base's pen plus the anchor
    /// difference at the floored size); and 30 inline elements of two one-letter words each at 8 and 10.5 pt
    /// (<c>ta-items</c>: each element's text a text item of its own, its width rounded up to 1/64 px).
    /// </summary>
    /// <param name="name">The case.</param>
    [Theory]
    [InlineData("ta-sizes")]
    [InlineData("ta-px")]
    [InlineData("ta-marks")]
    [InlineData("ta-items")]
    public void Every_word_starts_and_ends_where_Chromium_prints_it(string name)
    {
        Pairs(name).Should().OnlyContain(pair => Near(pair.Engine, pair.Chromium));
    }

    /// <summary>
    /// Right-aligned lines against a content edge at 600 pt (<c>ta-right</c>: one text item at 8, 10.5 and 11 pt;
    /// <c>ta-items-right</c>: 30 items at 8 and 10.5 pt): the words are where Chromium prints them, so the start of each
    /// line moves with both roundings: the line ends at the edge with its items' widths rounded up to 1/64 px.
    /// </summary>
    /// <param name="name">The case.</param>
    [Theory]
    [InlineData("ta-right")]
    [InlineData("ta-items-right")]
    public void A_right_aligned_line_starts_where_Chromium_prints_it(string name)
    {
        Pairs(name).Should().OnlyContain(pair => Near(pair.Engine, pair.Chromium));
    }

    /// <summary>
    /// A block wider than the sum of its 30 text items' unrounded widths and narrower than the sum rounded up per item
    /// (<c>ta-items-fit</c>, 8 and 10.5 pt): as in Chromium the last word wraps, and every word is where Chromium prints it,
    /// on its baseline. With the widths exact (<see cref="LengthRounding.Exact"/>) the line holds the last word.
    /// </summary>
    [Fact]
    public void A_line_that_fits_only_by_the_unrounded_item_widths_wraps_its_last_word()
    {
        Pairs("ta-items-fit").Should().OnlyContain(pair => Near(pair.Engine, pair.Chromium) && System.Math.Abs(pair.Engine.Baseline - pair.Chromium.Baseline) <= Bound);

        var exact = Engine("ta-items-fit", FontSizeRounding.FlooredToPixelFractions, LengthRounding.Exact).Pages.Single().Lines;
        exact.Should().HaveCount(2);
        exact.Should().OnlyContain(line => line.Text.EndsWith(" g h", System.StringComparison.Ordinal));
    }

    /// <summary>With every text item's width exact (<see cref="LengthRounding.Exact"/>) the last word of <c>ta-items</c> starts
    /// early by the sum of the steps: 0.176 pt at 8 pt and 0.119 pt at 10.5 pt.</summary>
    [Fact]
    public void With_exact_item_widths_a_line_of_many_items_ends_early()
    {
        // Each line holds three "h"; the third ends the line.
        var h = Pairs("ta-items", lengths: LengthRounding.Exact).Where(pair => pair.Chromium.Text == "h").ToList();

        (h[2].Engine.X0 - h[2].Chromium.X0).Should().BeInRange(-0.19, -0.16);
        (h[5].Engine.X0 - h[5].Chromium.X0).Should().BeInRange(-0.13, -0.10);
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
