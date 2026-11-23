using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.Documents.TestSupport;
using static UnitTests.Extensions.Documents.Layout.Models;

namespace UnitTests.Extensions.Documents.Layout;

/// <summary>
/// Inset and outset borders, and solid borders of more than one colour, as the painter draws them
/// (<c>PagePainter.CutBorder</c>), each test on a case Chromium 159 printed: the sides in the order top, bottom, right,
/// left and the solid ones last, each over its whole edge, cut on the line from the outer to the inner corner where it
/// meets a side painted before it in another colour, the border box on whole pixels. A solid border of one colour stays
/// four rectangles. The page is 300 × 400 pt with 12 pt margins (16 px, so the page area starts on a whole pixel).
/// </summary>
public class CutBorderTests
{
    private static readonly Color Base = new(0xee, 0xee, 0xee);
    private static readonly Color Dark = new(0x9a, 0x9a, 0x9a);
    private static readonly Color Red = new(255, 0, 0);
    private static readonly Color Green = new(0, 0x80, 0);
    private static readonly Color Blue = new(0, 0, 255);
    private static readonly Color Orange = new(255, 0xa5, 0);

    /// <summary>A solid border of four colours as the <c>bs-solid-*</c> cases give them: top red, right green, bottom blue,
    /// left orange.</summary>
    private static Border Four(double top, double right, double bottom, double left) =>
        new(new BorderSide(top.Pt(), Red), new BorderSide(right.Pt(), Green), new BorderSide(bottom.Pt(), Blue), new BorderSide(left.Pt(), Orange));

    /// <summary>3 px, the width most cases use.</summary>
    private const double Wide = 2.25;

    private static BorderSide Inset(double width = Wide) => new(width.Pt(), Base, BorderStyle.Inset);

    private static StyledDocument Page(params Node[] children) =>
        new(new DocumentMetadata("Test", null), new PageStyle(300.Pt(), 400.Pt(), Edges.All(12.Pt())), Flow(0, [.. children]));

    private static Page[] Painted(StyledDocument document)
    {
        var pages = Paginator.Paginate(LayoutEnvironment.Create(TestFonts.Set), document);
        pages.IsSuccess.Should().BeTrue(pages.IsFailure ? pages.Error.ToString() : string.Empty);
        return [.. PagePainter.Paint(new DocumentMetadata(null, null), pages.Value).Pages];
    }

    private static DrawItem[] Fills(Page page) => [.. page.Items.Where(item => item is FillRectangle or FillPath)];

    /// <summary>A fill as its colour and its corners: a rectangle's four from the top left clockwise, a path's in order.</summary>
    private static (Color Color, Point[] Corners) Shape(DrawItem item) => item switch
    {
        FillRectangle fill => (fill.Color, [fill.Area.Origin, fill.Area.Origin + new Point(fill.Area.Size.Width, 0),
            fill.Area.Origin + new Point(fill.Area.Size.Width, fill.Area.Size.Height), fill.Area.Origin + new Point(0, fill.Area.Size.Height)]),
        FillPath path => (path.Color, [path.Contours.Single().Start, .. path.Contours.Single().Segments.Select(segment => ((PathLine)segment).End)]),
        _ => throw new System.InvalidOperationException(),
    };

    private static Point P(double x, double y) => new(x, y);

    [Fact]
    public void Inset_sides_are_painted_top_bottom_right_left_and_cut_at_the_corners_where_the_colours_differ()
    {
        // bs-widths (3 px, currentcolor: #eeeeee): top rect #9a9a9a, bottom rect #eeeeee, right a path #eeeeee cut at
        // the top right only, left a path #9a9a9a cut at the bottom left only. One line of 12 pt: the box is 16.5 pt high.
        var fills = Fills(Painted(Page(Paragraph(1, "One", BoxStyle.Initial with { Border = Border.All(Inset()) })))[0]);

        fills.Select(fill => fill.GetType()).Should().Equal(typeof(FillRectangle), typeof(FillRectangle), typeof(FillPath), typeof(FillPath));
        Shape(fills[0]).Should().BeEquivalentTo((Dark, new[] { P(12, 12), P(288, 12), P(288, 14.25), P(12, 14.25) }), options => options.WithStrictOrdering());
        Shape(fills[1]).Should().BeEquivalentTo((Base, new[] { P(12, 26.25), P(288, 26.25), P(288, 28.5), P(12, 28.5) }), options => options.WithStrictOrdering());
        Shape(fills[2]).Should().BeEquivalentTo((Base, new[] { P(288, 12), P(288, 28.5), P(285.75, 28.5), P(285.75, 14.25) }), options => options.WithStrictOrdering(),
            "the right side runs over both corners, cut from the outer to the inner top right corner, whole at the bottom beside its own colour");
        Shape(fills[3]).Should().BeEquivalentTo((Dark, new[] { P(12, 12), P(12, 28.5), P(14.25, 26.25), P(14.25, 12) }), options => options.WithStrictOrdering());
    }

    [Fact]
    public void Outset_paints_the_top_and_left_sides_light_and_the_bottom_and_right_sides_dark()
    {
        // bs-widths, the outset boxes.
        var outset = new BorderSide(Wide.Pt(), Base, BorderStyle.Outset);
        var fills = Fills(Painted(Page(Paragraph(1, "One", BoxStyle.Initial with { Border = Border.All(outset) })))[0]);

        fills.Select(fill => Shape(fill).Color).Should().Equal(Base, Dark, Dark, Base);
    }

    [Fact]
    public void A_side_beside_its_own_colour_or_beside_no_side_is_a_rectangle_over_the_corner()
    {
        // bs-sides: a top and a left outset side are both light, a bottom and a right inset side both light; no path.
        var outset = new BorderSide(Wide.Pt(), Base, BorderStyle.Outset);
        var topLeft = Paragraph(1, "tl", BoxStyle.Initial with { Border = new Border(outset, BorderSide.None, BorderSide.None, outset) });
        var bottomRight = Paragraph(2, "br", BoxStyle.Initial with { Border = new Border(BorderSide.None, Inset(), Inset(), BorderSide.None) });

        var fills = Fills(Painted(Page(topLeft, bottomRight))[0]);

        fills.Should().AllBeOfType<FillRectangle>();
        Shape(fills[1]).Corners.Should().Equal(P(12, 12), P(14.25, 12), P(14.25, 26.25), P(12, 26.25));
        Shape(fills[3]).Corners.Should().Equal(P(285.75, 26.25), P(288, 26.25), P(288, 40.5), P(285.75, 40.5));
    }

    [Fact]
    public void A_solid_side_beside_inset_sides_is_painted_last_and_cut_at_both_corners()
    {
        // bs-corners, box m6: a red solid top over a 6 px inset border. Chromium paints bottom, right (whole: the bottom is
        // its colour and the red top comes later), left (cut at the bottom), then the top, cut at both ends.
        var six = Inset(4.5);
        var top = new BorderSide(4.5.Pt(), Red);
        var fills = Fills(Painted(Page(Paragraph(1, "m6", BoxStyle.Initial with { Border = new Border(top, six, six, six) })))[0]);

        fills.Select(fill => Shape(fill).Color).Should().Equal(Base, Base, Dark, Red);
        fills.Select(fill => fill.GetType()).Should().Equal(typeof(FillRectangle), typeof(FillRectangle), typeof(FillPath), typeof(FillPath));
        Shape(fills[3]).Corners.Should().Equal(P(12, 12), P(288, 12), P(283.5, 16.5), P(16.5, 16.5));
    }

    [Fact]
    public void A_corner_of_two_widths_is_cut_on_the_line_from_the_outer_to_the_inner_corner()
    {
        // bs-corners, box t6: a 6 px top beside 2 px sides; the right side's cut ends 1.5 pt in and 4.5 pt down.
        var border = new Border(Inset(4.5), Inset(1.5), Inset(4.5), Inset(1.5));
        var fills = Fills(Painted(Page(Paragraph(1, "t6", BoxStyle.Initial with { Border = border })))[0]);

        Shape(fills[2]).Corners.Should().Equal(P(288, 12), P(288, 33), P(286.5, 33), P(286.5, 16.5));
    }

    [Fact]
    public void The_border_box_is_painted_on_whole_pixels()
    {
        // bs-colours: a box 28⅔ px high is painted 29 px high. Here 12 + 2 × 1 + 2 × 2.25 = 18.5 pt (24⅔ px) is painted as
        // 25 px, 18.75 pt: the bottom side and the cut sides end at 30.75.
        var style = BoxStyle.Initial with { Border = Border.All(Inset()), Padding = new Edges(1.Pt(), 0.Pt(), 1.Pt(), 0.Pt()) };
        var fills = Fills(Painted(Page(Paragraph(1, "One", style)))[0]);

        Shape(fills[1]).Corners.Should().Equal(P(12, 28.5), P(288, 28.5), P(288, 30.75), P(12, 30.75));
        Shape(fills[2]).Corners.Should().Equal(P(288, 12), P(288, 30.75), P(285.75, 30.75), P(285.75, 14.25));
    }

    [Fact]
    public void A_box_sliced_by_a_page_break_cuts_only_the_corners_its_page_shows()
    {
        // bs-break: page 1 has the top, a right side cut at the top and a whole left side; page 2 the bottom, a whole right
        // side and a left side cut at the bottom. 40 one-word lines in a 20 pt wide box do not fit one page.
        var style = BoxStyle.Initial with { Margin = new Edges(0.Pt(), 236.Pt(), 0.Pt(), 0.Pt()), Border = Border.All(Inset()) };
        var pages = Painted(Page(Paragraph(1, Words(40), style)));

        pages.Should().HaveCount(2);
        Fills(pages[0]).Select(fill => (fill.GetType(), Shape(fill).Color)).Should().Equal((typeof(FillRectangle), Dark), (typeof(FillPath), Base), (typeof(FillRectangle), Dark));
        Fills(pages[1]).Select(fill => (fill.GetType(), Shape(fill).Color)).Should().Equal((typeof(FillRectangle), Base), (typeof(FillRectangle), Base), (typeof(FillPath), Dark));
    }

    [Fact]
    public void A_solid_bottom_or_a_solid_top_and_bottom_beside_inset_sides_is_painted_after_them()
    {
        // bs-solid-order, box sb: top, right (cut at the top), left (whole: beside the top's colour), then the red bottom,
        // cut at both ends. Box tb: right and left whole (nothing painted before them), then the red top and the blue
        // bottom, each cut at both ends.
        var six = Inset(4.5);
        var bottom = Fills(Painted(Page(Paragraph(1, "sb", BoxStyle.Initial with { Border = new Border(six, six, new BorderSide(4.5.Pt(), Red), six) })))[0]);
        var both = Fills(Painted(Page(Paragraph(1, "tb", BoxStyle.Initial with { Border = new Border(new BorderSide(4.5.Pt(), Red), six, new BorderSide(4.5.Pt(), Blue), six) })))[0]);

        bottom.Select(fill => (fill.GetType(), Shape(fill).Color)).Should().Equal(
            (typeof(FillRectangle), Dark), (typeof(FillPath), Base), (typeof(FillRectangle), Dark), (typeof(FillPath), Red));
        Shape(bottom[3]).Corners.Should().Equal(P(12, 33), P(288, 33), P(283.5, 28.5), P(16.5, 28.5));
        both.Select(fill => (fill.GetType(), Shape(fill).Color)).Should().Equal(
            (typeof(FillRectangle), Base), (typeof(FillRectangle), Dark), (typeof(FillPath), Red), (typeof(FillPath), Blue));
    }

    [Fact]
    public void Solid_sides_of_four_colours_are_cut_at_every_corner_the_right_and_left_sides_after_the_top_and_bottom()
    {
        // bs-solid-colours, box c3 (3 px): top red and bottom blue over the full width, then the right side green and the
        // left side orange, each cut at both ends from the outer to the inner corner. One line of 12 pt: 16.5 pt high.
        var fills = Fills(Painted(Page(Paragraph(1, "c3", BoxStyle.Initial with { Border = Four(Wide, Wide, Wide, Wide) })))[0]);

        fills.Select(fill => (fill.GetType(), Shape(fill).Color)).Should().Equal(
            (typeof(FillRectangle), Red), (typeof(FillRectangle), Blue), (typeof(FillPath), Green), (typeof(FillPath), Orange));
        Shape(fills[2]).Corners.Should().Equal(P(288, 12), P(288, 28.5), P(285.75, 26.25), P(285.75, 14.25));
        Shape(fills[3]).Corners.Should().Equal(P(12, 12), P(12, 28.5), P(14.25, 26.25), P(14.25, 14.25));
    }

    [Fact]
    public void Solid_sides_of_unequal_widths_are_cut_on_the_line_from_the_outer_to_the_inner_corner()
    {
        // bs-solid-widths, box wt: a red top of 8 px beside sides of 2 px; the right side's cut ends 1.5 pt in and 6 pt down.
        var fills = Fills(Painted(Page(Paragraph(1, "wt", BoxStyle.Initial with { Border = Four(6, 1.5, 1.5, 1.5) })))[0]);

        Shape(fills[2]).Corners.Should().Equal(P(288, 12), P(288, 31.5), P(286.5, 30), P(286.5, 18));
    }

    [Fact]
    public void A_solid_side_beside_a_side_of_its_own_colour_or_of_width_0_covers_that_corner()
    {
        // bs-solid-pairs, box pa: top and right red, bottom and left blue; the right side is cut only at the bottom, the
        // left only at the top. Box pz: no right side, so only the left side is cut, at both ends.
        var (red, blue) = (new BorderSide(4.5.Pt(), Red), new BorderSide(4.5.Pt(), Blue));
        var pairs = Fills(Painted(Page(Paragraph(1, "pa", BoxStyle.Initial with { Border = new Border(red, red, blue, blue) })))[0]);
        var noRight = Fills(Painted(Page(Paragraph(1, "pz", BoxStyle.Initial with { Border = Four(4.5, 0, 4.5, 4.5) })))[0]);

        Shape(pairs[2]).Should().BeEquivalentTo((Red, new[] { P(288, 12), P(288, 33), P(283.5, 28.5), P(283.5, 12) }), options => options.WithStrictOrdering());
        Shape(pairs[3]).Should().BeEquivalentTo((Blue, new[] { P(12, 12), P(12, 33), P(16.5, 33), P(16.5, 16.5) }), options => options.WithStrictOrdering());
        noRight.Select(fill => (fill.GetType(), Shape(fill).Color)).Should().Equal(
            (typeof(FillRectangle), Red), (typeof(FillRectangle), Blue), (typeof(FillPath), Orange));
    }

    [Fact]
    public void Solid_sides_of_four_colours_are_painted_on_whole_pixels()
    {
        // bs-solid-colours, box cx: a left edge at 16.53 px is painted at 17 px, a bottom at 202.53 px at 203 px. Here a
        // box 0.4 pt right of the page area's edge, 12 + 2 × 1 + 2 × 2.25 = 18.5 pt high (24⅔ px), is painted from 12.75 pt
        // (17 px) and 25 px, 18.75 pt, high.
        var style = BoxStyle.Initial with { Border = Four(Wide, Wide, Wide, Wide), Padding = new Edges(1.Pt(), 0.Pt(), 1.Pt(), 0.Pt()), Margin = new Edges(0.Pt(), 0.Pt(), 0.Pt(), 0.4.Pt()) };
        var fills = Fills(Painted(Page(Paragraph(1, "cx", style)))[0]);

        Shape(fills[1]).Corners.Should().Equal(P(12.75, 28.5), P(288, 28.5), P(288, 30.75), P(12.75, 30.75));
        Shape(fills[3]).Corners.Should().Equal(P(12.75, 12), P(12.75, 30.75), P(15, 28.5), P(15, 14.25));
    }

    [Fact]
    public void A_box_of_four_solid_colours_sliced_by_a_page_break_cuts_only_the_corners_its_page_shows()
    {
        // bs-solid-break: page 1 has the top and the right and left sides cut at the top only; page 2 the bottom and the
        // two sides cut at the bottom only.
        var style = BoxStyle.Initial with { Margin = new Edges(0.Pt(), 236.Pt(), 0.Pt(), 0.Pt()), Border = Four(3, 3, 3, 3) };
        var pages = Painted(Page(Paragraph(1, Words(40), style)));

        pages.Should().HaveCount(2);
        var (first, second) = (Fills(pages[0]), Fills(pages[1]));
        first.Select(fill => (fill.GetType(), Shape(fill).Color)).Should().Equal((typeof(FillRectangle), Red), (typeof(FillPath), Green), (typeof(FillPath), Orange));
        var left = Shape(first[2]).Corners;
        (left[0], left[3], left[2].X, left[2].Y).Should().Be((P(12, 12), P(15, 15), 15.0, left[1].Y), "the left side is cut at the top and runs on to the page's end");
        second.Select(fill => (fill.GetType(), Shape(fill).Color)).Should().Equal((typeof(FillRectangle), Blue), (typeof(FillPath), Green), (typeof(FillPath), Orange));
    }

    [Fact]
    public void A_solid_border_of_one_colour_and_unequal_widths_stays_four_rectangles()
    {
        // bs-solid-one-colour: Chromium fills one shape of the same pixels (listed in the HTML cases); the engine keeps the
        // rectangles it paints for every border of one colour.
        var fills = Fills(Painted(Page(Paragraph(1, "wo", BoxStyle.Initial with { Border = new Border(new BorderSide(6.Pt(), Blue), new BorderSide(1.5.Pt(), Blue), new BorderSide(1.5.Pt(), Blue), new BorderSide(3.Pt(), Blue)) })))[0]);

        fills.Should().HaveCount(4).And.AllBeOfType<FillRectangle>();
        Shape(fills[2]).Corners.Should().Equal(P(12, 18), P(15, 18), P(15, 30), P(12, 30));
    }

    [Fact]
    public void A_border_of_solid_sides_is_painted_as_before_the_left_and_right_sides_between_the_top_and_the_bottom()
    {
        var solid = new BorderSide(Wide.Pt(), Red);
        var fills = Fills(Painted(Page(Paragraph(1, "One", BoxStyle.Initial with { Border = Border.All(solid) })))[0]);

        fills.Should().AllBeOfType<FillRectangle>();
        Shape(fills[2]).Corners.Should().Equal([P(12, 14.25), P(14.25, 14.25), P(14.25, 26.25), P(12, 26.25)], "the left side, third, between the top and the bottom");
    }
}
