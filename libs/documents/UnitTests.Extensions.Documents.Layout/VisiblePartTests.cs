using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.Documents.TestSupport;

namespace UnitTests.Extensions.Documents.Layout;

/// <summary>
/// The part of a drawing that a clip lets show (<see cref="PrintedPages.Visible"/>), by which the case comparison reads the
/// engine's pages, as <c>tools/reference/chromium/visible.py</c> reads Chromium's print: a rectangle cut to the clip, or
/// nothing where it lies wholly outside. The clip here is a page area, (12, 12)–(288, 192).
/// </summary>
public class VisiblePartTests
{
    private static readonly Rect PageArea = new(new Point(12, 12), new Size(276, 180));

    private static PrintedBox Box(double x0, double y0, double x1, double y1) => new(1, x0, y0, x1, y1, "#000000");

    [Fact]
    public void A_rectangle_inside_the_clip_shows_whole()
    {
        PrintedPages.Visible(Box(12, 27, 288, 27.75), PageArea).Should().Be(Box(12, 27, 288, 27.75));
    }

    [Fact]
    public void A_rectangle_outside_the_clip_shows_nothing_also_where_it_touches_its_edge()
    {
        PrintedPages.Visible(Box(12, 192, 288, 192.75), PageArea).Should().BeNull("it starts where the clip ends");
        PrintedPages.Visible(Box(12, -168, 12.75, 0), PageArea).Should().BeNull();
        PrintedPages.Visible(Box(300, 50, 310, 60), PageArea).Should().BeNull();
    }

    [Fact]
    public void A_rectangle_across_one_edge_is_cut_at_that_edge()
    {
        PrintedPages.Visible(Box(12, -168, 12.75, 12.75), PageArea).Should().Be(Box(12, 12, 12.75, 12.75));
        PrintedPages.Visible(Box(66.75, 12, 67.5, 420), PageArea).Should().Be(Box(66.75, 12, 67.5, 192));
    }

    [Fact]
    public void A_rectangle_across_a_corner_is_cut_at_both_edges()
    {
        PrintedPages.Visible(Box(280, 180, 300, 200), PageArea).Should().Be(Box(280, 180, 288, 192));
        PrintedPages.Visible(Box(0, 0, 20, 20), PageArea).Should().Be(Box(12, 12, 20, 20));
    }

    [Fact]
    public void Without_a_clip_a_rectangle_shows_as_drawn()
    {
        PrintedPages.Visible(Box(12, -168, 12.75, 240), null).Should().Be(Box(12, -168, 12.75, 240));
    }

    [Fact]
    public void The_engine_s_pages_are_read_through_their_clips_also_nested()
    {
        // A fill and an image that pass the outer clip, a fill under a nested clip that leaves less, and one after both end.
        var image = TestImages.Load("rgb8.png");
        var page = new Page(new Size(300, 204),
        [
            new ClipStart(PageArea),
            new FillRectangle(new Rect(new Point(12, 180), new Size(276, 20)), Color.Black),
            new DrawImage(new Rect(new Point(50, -10), new Size(10, 30)), image),
            new ClipStart(new Rect(new Point(0, 100), new Size(300, 10))),
            new FillRectangle(new Rect(new Point(0, 0), new Size(300, 204)), Color.Black),
            ClipEnd.Instance,
            new FillRectangle(new Rect(new Point(12, 192), new Size(276, 0.75)), Color.Black),
            ClipEnd.Instance,
            new FillRectangle(new Rect(new Point(12, 192), new Size(276, 0.75)), Color.Black),
        ]);

        var printed = PrintedPages.Engine(new PageDocument(new DocumentMetadata(null, null), [page])).Pages[0];

        printed.Rectangles.Should().Equal(Box(12, 180, 288, 192), Box(12, 100, 288, 110), Box(12, 192, 288, 192.75));
        printed.Images.Should().Equal(new PrintedBox(1, 50, 12, 60, 20));
    }
}
