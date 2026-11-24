using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.Documents.TestSupport;

namespace UnitTests.Extensions.Documents.Html.Boxes;

/// <summary>
/// A shape that one side paints with a path and the other with rectangles, compared by the area they paint
/// (<see cref="HtmlCases.Difference"/>, <c>ShapesOfRectangles</c>; case <c>bs-solid-one-colour</c>, where Chromium fills a
/// border of one colour and unequal widths as one shape with a hole and the engine fills four rectangles): the
/// rectangles are sampled on the path's box by the path's rule (<see cref="PathSampler.Rows"/>), and what they paint
/// outside the box is compared as rectangles.
/// </summary>
public class ShapesOfRectanglesTests
{
    private static readonly FillPath L = TestPaths.Polygon(new Point(10.1, 20.2), new Point(14.3, 20.2), new Point(14.3, 21.9), new Point(11.6, 21.9), new Point(11.6, 24.05), new Point(10.1, 24.05));

    /// <summary>A frame: the box 12–48 × 12–36 pt with the box 15–45 × 18–33 pt cut out of it, even-odd, black.</summary>
    private static readonly FillPath Frame = new(
        [Contour(12, 12, 48, 36), Contour(15, 18, 45, 33)], PathFillRule.EvenOdd, Color.Black);

    /// <summary>The four sides of the frame as the engine paints a border: top and bottom over the full width, the left and
    /// right sides between them.</summary>
    private static readonly FillRectangle[] Ring =
    [
        Fill(12, 12, 48, 18), Fill(12, 33, 48, 36), Fill(12, 18, 15, 33), Fill(45, 18, 48, 33),
    ];

    private static PathContour Contour(double x0, double y0, double x1, double y1) =>
        new(new Point(x0, y0), [new PathLine(new Point(x1, y0)), new PathLine(new Point(x1, y1)), new PathLine(new Point(x0, y1))]);

    private static FillRectangle Fill(double x0, double y0, double x1, double y1, Color? color = null) =>
        new(new Rect(new Point(x0, y0), new Size(x1 - x0, y1 - y0)), color ?? Color.Black);

    private static PrintedPages Engine(params DrawItem[] items) =>
        PrintedPages.Engine(new PageDocument(new DocumentMetadata(null, null), [new Page(new Size(300, 420), [.. items])]));

    [Fact]
    public void A_ring_of_four_rectangles_paints_the_frame_a_path_paints_on_either_side()
    {
        var (rectangles, path) = (Engine(Ring), Engine(Frame));

        path.Pages[0].Paths.Should().ContainSingle();
        HtmlCases.Difference(rectangles, path).Should().BeNull("the rectangles paint the frame's pixels, and only those");
        HtmlCases.Difference(path, rectangles).Should().BeNull("the same the other way round");
    }

    [Fact]
    public void An_L_of_two_rectangles_paints_the_L_path_of_the_sampler_tests_off_the_pixel_grid()
    {
        // PathSamplerTests: the L paints 1111110/8fffff1/8fffff1/8f93330/8f70000/8f70000/1100000 from (9.75, 19.5).
        var rectangles = Engine(Fill(10.1, 20.2, 14.3, 21.9), Fill(10.1, 21.9, 11.6, 24.05));

        HtmlCases.Difference(rectangles, Engine(L)).Should().BeNull();
    }

    [Fact]
    public void A_rectangle_off_the_pixel_grid_paints_a_square_path_of_its_edges_and_one_moved_by_a_fifth_of_a_point_does_not()
    {
        var square = TestPaths.Polygon(new Point(10.1, 20.2), new Point(14.3, 20.2), new Point(14.3, 24.05), new Point(10.1, 24.05));
        var path = Engine(square);
        path.Pages[0].Paths.Should().ContainSingle().Which.Rows[0].Should().NotContain("f", "its top edge is off the pixel grid");

        HtmlCases.Difference(Engine(Fill(10.1, 20.2, 14.3, 24.05)), path).Should().BeNull();
        HtmlCases.Difference(Engine(Fill(10.3, 20.2, 14.5, 24.05)), path).Should().NotBeNull();
    }

    [Fact]
    public void A_square_does_not_paint_a_disc_of_the_same_box()
    {
        var disc = Engine(TestPaths.Disc(31.875, 201.375, 1.875));
        var box = disc.Pages[0].Paths.Should().ContainSingle().Subject;

        // The square's corners are not the disc's: the square stays a rectangle of its own, which the other side lacks.
        HtmlCases.Difference(Engine(Fill(box.X0, box.Y0, box.X1, box.Y1)), disc).Should().StartWith("page 1 painted area: the pixel at (30, 199.5): the engine paints it and Chromium does not");
        HtmlCases.Difference(disc, Engine(Fill(box.X0, box.Y0, box.X1, box.Y1))).Should().StartWith("page 1 painted area: the pixel at (30, 199.5): Chromium paints it and the engine does not");
    }

    [Fact]
    public void A_ring_with_a_side_short_a_side_in_another_colour_or_its_hole_filled_is_a_difference()
    {
        var path = Engine(Frame);

        HtmlCases.Difference(Engine(Ring[0], Ring[1], Ring[2], Fill(45, 18, 48, 32.25)), path).Should().NotBeNull("the right side is 1 px short");
        HtmlCases.Difference(Engine(Ring[0], Ring[1], Ring[2], Fill(45, 18, 48, 33, new Color(255, 0, 0))), path).Should().NotBeNull("the right side is red");
        HtmlCases.Difference(Engine([.. Ring, Fill(12, 12, 48, 36)]), path).Should().NotBeNull("the hole is filled");
    }

    [Fact]
    public void A_rectangle_reaching_past_the_path_s_box_is_compared_as_a_rectangle_outside_it()
    {
        // The top side runs 6 pt on to the right of the frame: in the box it is the frame's, past it a rectangle of its own,
        // which the other side must paint as a rectangle too.
        var longer = Engine([Fill(12, 12, 54, 18), .. Ring[1..]]);
        var frameAndBand = Engine(Frame, Fill(48, 12, 54, 18));

        HtmlCases.Difference(longer, frameAndBand).Should().BeNull();
        HtmlCases.Difference(longer, Engine(Frame)).Should().StartWith("page 1 painted area: the pixel at (48, 12): the engine paints it and Chromium does not");
        HtmlCases.Difference(Engine([.. Ring, Fill(48, 12, 54, 18)]), frameAndBand).Should().BeNull("a band beside the box that the engine paints apart counts once");
    }
}
