using System.IO;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;

namespace UnitTests.Extensions.Documents.Html.Boxes;

/// <summary>
/// Where the part of a table cell ends that holds nothing but the rest of a piece sliced on an earlier page
/// (<see cref="SliceRestEnd"/>), against Chromium's print (<c>tools/reference/chromium</c>). The rest of each piece starts
/// half a pixel below a whole pixel (below the centre of a 0.75 pt or 2.25 pt grid line under a repeated header row), so
/// it ends there too, and the place of the line below it shows in the print: under
/// <see cref="SliceRestEnd.AtLineCentre"/>, the HTML front-end's value, the line is centred on the rest's end and lies
/// exactly where Chromium paints it, and the text after the table starts where Chromium's does; these positions are
/// compared as laid out, not at whole pixels.
/// </summary>
public class SliceRestEndTests
{
    private static PrintedPages Chromium(string name) =>
        PrintedPages.Parse(File.ReadAllText(Path.Combine(HtmlCases.Folder(), "..", "measured", name + ".print.txt")));

    private static PrintedPages Engine(string name, SliceRestEnd? end = null)
    {
        var boxes = HtmlCases.Boxes(name).Value;
        var document = end is { } value ? boxes.Document with { Profile = boxes.Document.Profile with { SliceRestEnd = value } } : boxes.Document;
        return PrintedPages.Engine(DocumentLayout.Run(document, boxes.Fonts).Value);
    }

    /// <summary>The lowest grid line across the table on a page: the line below the table's last row.</summary>
    private static PrintedBox LineBelow(PrintedPage page) => page.Rectangles.Where(box => box.X1 - box.X0 > 200).MaxBy(box => box.Y0)!;

    /// <summary>
    /// Below the rest of a 228 pt image (<c>a4-header-image-228</c>, and with a 2.25 pt line below the header row
    /// <c>a4-header-line2-25-image-228</c>), of a 408 pt image (<c>a4-header-image-row</c>), of a 180 pt image
    /// (<c>a4-header-image-180</c>) and of a line 240 pt tall (<c>a4-header-tall-line</c>) the 0.75 pt line, and below
    /// the rest of the 228 pt image a 2.25 pt line (<c>a4-header-image-228-line3-below</c>), lie where Chromium paints
    /// them, centred on the rest's end, and the paragraph after the table starts where Chromium's does.
    /// </summary>
    /// <param name="name">The case.</param>
    /// <param name="page">The page that shows the rest.</param>
    /// <param name="restEnd">Where the rest ends as laid out: the top of the image's first slice plus its height, less
    /// the page areas before.</param>
    [Theory]
    [InlineData("a4-header-image-228", 3, 76.125)]
    [InlineData("a4-header-line2-25-image-228", 3, 76.875)]
    [InlineData("a4-header-image-row", 4, 76.125)]
    [InlineData("a4-header-image-180", 3, 28.125)]
    [InlineData("a4-header-tall-line", 3, 88.125)]
    [InlineData("a4-header-image-228-line3-below", 3, 76.125)]
    public void The_line_below_the_rest_of_a_sliced_piece_is_centred_on_the_rests_end_where_Chromium_paints_it(string name, int page, double restEnd)
    {
        var (engine, chromium) = (Engine(name).Pages[page - 1], Chromium(name).Pages[page - 1]);
        var (ours, theirs) = (LineBelow(engine), LineBelow(chromium));

        ours.Y0.Should().BeApproximately(theirs.Y0, 1e-6);
        ours.Y1.Should().BeApproximately(theirs.Y1, 1e-6);
        ((ours.Y0 + ours.Y1) / 2).Should().BeApproximately(restEnd, 1e-6, "the line is centred on the rest's end");
        engine.Words.Where(word => word.Baseline > ours.Y1).Select(word => word.Baseline)
            .Should().Equal(chromium.Words.Where(word => word.Baseline > theirs.Y1).Select(word => word.Baseline), "what follows starts where Chromium's does");
    }

    /// <summary>Under <see cref="SliceRestEnd.InsideCell"/>, the code front-end's default, the cell holds its half of the
    /// line below the rest as every cell does: the line starts where the rest ends, half the line lower than Chromium's,
    /// and so does what follows.</summary>
    [Fact]
    public void Under_InsideCell_the_line_below_the_rest_starts_where_the_rest_ends()
    {
        var (inside, centre) = (Engine("a4-header-image-228-line3-below", SliceRestEnd.InsideCell).Pages[2], Engine("a4-header-image-228-line3-below").Pages[2]);

        LineBelow(inside).Y0.Should().BeApproximately(76.125, 1e-6);
        LineBelow(centre).Y0.Should().BeApproximately(76.125 - 1.125, 1e-6);
        inside.Words.Single(word => word.Text == "after").Baseline.Should().BeApproximately(centre.Words.Single(word => word.Text == "after").Baseline + 1.125, 1e-6);
    }

    /// <summary>A part that lays content out on the page holds its half of the line below it under either value: the
    /// rest of a row cut inside its text, below a repeated header row with a 0.75 pt and a 2.25 pt line
    /// (<c>a4-header-cut-text-row</c>, <c>a4-header-line2-25-cut-text-row</c>, both equal to Chromium).</summary>
    /// <param name="name">The case.</param>
    [Theory]
    [InlineData("a4-header-cut-text-row")]
    [InlineData("a4-header-line2-25-cut-text-row")]
    public void A_part_that_lays_content_out_holds_its_half_of_the_line_below_it_under_either_value(string name) =>
        Engine(name, SliceRestEnd.InsideCell).Should().BeEquivalentTo(Engine(name, SliceRestEnd.AtLineCentre));
}
