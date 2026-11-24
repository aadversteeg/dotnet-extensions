using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.Documents.TestSupport;
using static UnitTests.Extensions.Documents.Layout.Models;

namespace UnitTests.Extensions.Documents.Layout;

/// <summary>Dashed borders (<see cref="BorderStyle.Dashed"/>, <see cref="DashedBorder"/>): the count, length and gap of
/// the dashes of a side, the paths a border paints, and what is refused; the reference cases <c>bd-*</c> hold Chromium's
/// prints.</summary>
public class DashedBorderTests
{
    private const double Pixel = 0.75;

    /// <summary>A page of 300 × 400 pt with margins of 12 pt: the page area's edges on whole CSS pixels (16 px).</summary>
    private static StyledDocument Document(params Node[] children) =>
        new(new DocumentMetadata("Test", null), new PageStyle(300.Pt(), 400.Pt(), Edges.All(12.Pt())), Flow(0, [.. children]));

    private static BorderSide Dashed(double pixels, Color color) => new((pixels * Pixel).Pt(), color, BorderStyle.Dashed);

    /// <summary>Every dashed side of the cases: its width and length in CSS pixels and the count of dashes Chromium prints.</summary>
    [Theory]
    // bd-lengths-thin, bd-lengths-thick: lone top sides.
    [InlineData(1, 11, 3)] [InlineData(1, 29, 6)] [InlineData(1, 83, 17)] [InlineData(1, 151, 31)] [InlineData(1, 232, 47)]
    [InlineData(2, 17, 2)] [InlineData(2, 41, 5)] [InlineData(2, 97, 10)] [InlineData(2, 163, 17)] [InlineData(2, 231, 24)]
    [InlineData(3, 19, 3)] [InlineData(3, 47, 6)] [InlineData(3, 89, 10)] [InlineData(3, 157, 18)] [InlineData(3, 233, 26)]
    [InlineData(4, 27, 3)] [InlineData(4, 53, 5)] [InlineData(4, 101, 9)] [InlineData(4, 167, 14)] [InlineData(4, 229, 19)]
    // bd-widths: four dashed sides of boxes 100 × 30 px.
    [InlineData(1, 102, 21)] [InlineData(1, 32, 7)] [InlineData(2, 104, 11)] [InlineData(2, 34, 4)]
    [InlineData(3, 106, 12)] [InlineData(3, 36, 4)] [InlineData(4, 108, 9)] [InlineData(4, 38, 4)]
    // bd-sides: one dashed side beside solid ones, one alone, two in a corner (the tie at 30 px), over a background.
    [InlineData(2, 136, 14)] [InlineData(3, 28, 4)] [InlineData(2, 134, 14)] [InlineData(2, 30, 4)] [InlineData(2, 32, 4)]
    [InlineData(1, 134, 27)] [InlineData(1, 30, 6)]
    // bd-fraction: widths of 1.5 and 2.6 px drawn as 1 and 2 px; a box between pixels.
    [InlineData(1, 26, 6)] [InlineData(2, 28, 3)] [InlineData(3, 84, 10)] [InlineData(3, 30, 4)]
    // bd-break: a box over a page break, each part dashed along its own length.
    [InlineData(1, 368, 74)] [InlineData(1, 340, 68)] [InlineData(1, 89, 18)]
    public void The_count_of_dashes_is_the_one_whose_gap_lies_nearer_the_ideal_gap(double width, double length, int count)
    {
        var dashes = DashedBorder.Dashes(length * Pixel, width * Pixel);

        dashes.Should().NotBeNull();
        dashes!.Value.Count.Should().Be(count);
        dashes.Value.Dash.Should().BeApproximately((width < 3 ? 3 * width : 2 * width) * Pixel, 1e-9);
        ((dashes.Value.Count * dashes.Value.Dash) + ((dashes.Value.Count - 1) * dashes.Value.Gap)).Should().BeApproximately(length * Pixel, 1e-9, "the dashes end flush with the side");
    }

    [Fact]
    public void A_side_of_length_0_has_no_dash_and_one_shorter_than_two_dashes_and_a_gap_none_at_all()
    {
        DashedBorder.Dashes(0, Pixel)!.Value.Count.Should().Be(0);
        DashedBorder.Dashes(7.9 * Pixel, Pixel).Should().BeNull("1 px: two dashes of 3 px and a gap of 2 px are 8 px");
        DashedBorder.Dashes(8 * Pixel, Pixel)!.Value.Count.Should().Be(2);
        DashedBorder.Dashes(19.9 * Pixel, 4 * Pixel).Should().BeNull("4 px: two dashes of 8 px and a gap of 4 px");
    }

    [Fact]
    public void A_side_has_at_most_as_many_dashes_as_one_path_holds()
    {
        DashedBorder.MaxDashes.Should().Be(1365);
        DashedBorder.Dashes(((5 * 1365) - 2) * Pixel, Pixel)!.Value.Count.Should().Be(1365);
        DashedBorder.Dashes(((5 * 1366) - 2) * Pixel, Pixel).Should().BeNull();
    }

    [Fact]
    public void Each_dashed_side_is_one_path_of_rectangles_in_the_order_top_bottom_right_left_and_the_solid_sides_follow()
    {
        var red = new Color(0xc0, 0x30, 0x20);
        var border = new Border(Dashed(2, red), new BorderSide((2 * Pixel).Pt(), red), new BorderSide((2 * Pixel).Pt(), red), Dashed(2, red));
        var items = DashedBorder.Paint(border, new Rect(new Point(12.3, 30), new Size(102 * Pixel, 32 * Pixel)), new Point(12, 12)).ToList();

        items.Should().HaveCount(4);
        var top = (FillPath)items[0];
        top.Contours.Should().HaveCount(11, "2 px over 102 px: 11 dashes of 6 px, gaps of 3.6 px");
        top.Contours[0].Start.Should().Be(new Point(12, 30), "the border box on whole pixels: 12.3 pt is 16.4 px, painted at 16 px");
        top.Contours[^1].Segments[0].Should().Be(new PathLine(new Point(12 + (102 * Pixel), 30)), "the last dash ends at the side's end");
        var left = (FillPath)items[1];
        left.Contours[0].Start.Should().Be(new Point(12, 30));
        items[2].Should().Be(new FillRectangle(new Rect(new Point(12, 30 + (30 * Pixel)), new Size(102 * Pixel, 2 * Pixel)), red), "the solid bottom along its whole edge");
        items[3].Should().Be(new FillRectangle(new Rect(new Point(12 + (100 * Pixel), 30), new Size(2 * Pixel, 32 * Pixel)), red), "then the right side, over both corners");
    }

    [Fact]
    public void A_dashed_side_too_short_to_dash_is_refused_with_its_length()
    {
        var box = Paragraph(1, "x", BoxStyle.Initial with { Border = Border.All(Dashed(1, Color.Black)), SpecifiedHeight = (4 * Pixel).Pt() });
        var error = DocumentLayout.Run(Document(box), TestFonts.Set).Error;

        error.Code.Should().Be(LayoutErrorCodes.DashedBorder);
        error.Message.Should().Contain("6 px long").And.Contain("solid line");
    }

    [Fact]
    public void The_model_refuses_dashed_sides_of_two_colours_and_on_an_inline_box()
    {
        var mixed = Paragraph(1, "x", BoxStyle.Initial with { Border = Border.All(Dashed(1, Color.Black)) with { Top = Dashed(1, Color.White) } });
        DocumentLayout.Run(Document(mixed), TestFonts.Set).Error.Code.Should().Be(DocumentErrorCodes.Input.Unsupported.BorderStyle);
    }
}
