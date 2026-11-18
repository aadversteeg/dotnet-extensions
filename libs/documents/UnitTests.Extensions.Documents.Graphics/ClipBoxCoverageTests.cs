using Ave.Extensions.Documents.Graphics;
using Path = Ave.Extensions.Documents.Graphics.Path;

namespace UnitTests.Extensions.Documents.Graphics;

/// <summary>
/// Coverage through a clip box whose edges lie inside pixels, as the raster back-end's clips give it: the clip cuts the
/// geometry (Sutherland–Hodgman, <c>Clipping</c>) before the area is computed, so every pixel must get the exact area of
/// the shape inside the box, also where the clip leaves edges along its side that lie on top of each other (a shape
/// with a counter, a concave shape cut in two stretches), for both fill rules. The expected areas come from
/// <see cref="Geometry"/>: the area of each contour inside the pixel's square intersected with the box.
/// </summary>
public class ClipBoxCoverageTests
{
    /// <summary>Half an 8-bit step, plus room for the rounding of doubles.</summary>
    private const double OneRounding = 0.5 + 1e-9;

    private static Path Polygons(params IReadOnlyList<(double X, double Y)>[] contours)
    {
        var builder = new PathBuilder();
        foreach (var points in contours)
        {
            var subpath = builder.MoveTo(points[0].X, points[0].Y);
            foreach (var point in points.Skip(1))
            {
                subpath.LineTo(point.X, point.Y);
            }

            subpath.Close();
        }

        return builder.ToPath();
    }

    private static (double X, double Y)[] Rectangle(double x0, double y0, double x1, double y1, bool reversed = false) =>
        reversed ? [(x0, y0), (x0, y1), (x1, y1), (x1, y0)] : [(x0, y0), (x1, y0), (x1, y1), (x0, y1)];

    /// <summary>Every pixel of the mask (and every pixel of the box) against the expected area in it, which the caller
    /// computes for the pixel's square intersected with the box.</summary>
    private static void ShouldMatch(CoverageMask mask, PixelBox box, Func<double, double, double, double, double> expectedArea)
    {
        for (var y = (int)Math.Floor(box.Top); y < (int)Math.Ceiling(box.Bottom); y++)
        {
            for (var x = (int)Math.Floor(box.Left); x < (int)Math.Ceiling(box.Right); x++)
            {
                var (left, top, right, bottom) = (Math.Max(x, box.Left), Math.Max(y, box.Top), Math.Min(x + 1, box.Right), Math.Min(y + 1, box.Bottom));
                var expected = 255 * expectedArea(left, top, right, bottom);
                var actual = x >= mask.Left && x < mask.Left + mask.Width && y >= mask.Top && y < mask.Top + mask.Height ? mask.At(x, y) : 0;
                Math.Abs(actual - expected).Should().BeLessThanOrEqualTo(OneRounding, $"pixel ({x}, {y})");
            }
        }
    }

    public static TheoryData<FillRule> Rules => [FillRule.NonZero, FillRule.EvenOdd];

    private static readonly (double X, double Y)[] Outer = Rectangle(10.3, 10.2, 30.7, 28.9);

    private static readonly (double X, double Y)[] Counter = Rectangle(16.1, 15.4, 24.9, 23.6, reversed: true);

    [Theory]
    [MemberData(nameof(Rules))]
    public void A_clip_line_through_a_counter_horizontally_cuts_the_shape_exactly(FillRule rule)
    {
        var box = new PixelBox(5.4, 19.35, 40.6, 40.2);
        var mask = Canvas.Coverage(Polygons(Outer, Counter), Affine.Identity, rule, box).Value;

        ShouldMatch(mask, box, (l, t, r, b) => Geometry.AreaInBox(Outer, l, t, r, b) - Geometry.AreaInBox(Counter, l, t, r, b));
    }

    [Theory]
    [MemberData(nameof(Rules))]
    public void A_clip_line_through_a_counter_vertically_cuts_the_shape_exactly(FillRule rule)
    {
        var box = new PixelBox(20.65, 3.3, 45.2, 40.15);
        var mask = Canvas.Coverage(Polygons(Outer, Counter), Affine.Identity, rule, box).Value;

        ShouldMatch(mask, box, (l, t, r, b) => Geometry.AreaInBox(Outer, l, t, r, b) - Geometry.AreaInBox(Counter, l, t, r, b));
    }

    [Theory]
    [MemberData(nameof(Rules))]
    public void A_concave_shape_cut_by_the_clip_in_two_stretches_is_covered_exactly(FillRule rule)
    {
        // A U: the clip's top edge cuts both arms, so the clipped polygon runs along that edge twice, with the edge between
        // the arms traced there and back.
        (double X, double Y)[] u = [(10.2, 10.3), (14.6, 10.3), (14.6, 24.7), (25.4, 24.7), (25.4, 10.3), (29.8, 10.3), (29.8, 30.1), (10.2, 30.1)];
        var box = new PixelBox(4.75, 16.45, 36.3, 35.5);
        var mask = Canvas.Coverage(Polygons(u), Affine.Identity, rule, box).Value;

        ShouldMatch(mask, box, (l, t, r, b) => Geometry.AreaInBox(u, l, t, r, b));
    }

    private static readonly (double X, double Y)[] Stem = Rectangle(20.4, 6.2, 26.3, 38.7);

    private static readonly (double X, double Y)[] Bar = Rectangle(13.1, 18.9, 33.6, 23.4);

    private static readonly (double X, double Y)[] Overlap = Rectangle(20.4, 18.9, 26.3, 23.4);

    /// <summary>The stem and the bar of 'Ɨ' (two contours wound the same way, overlapping): the non-zero rule fills their
    /// union, even-odd leaves the overlap empty.</summary>
    private static double StemAndBar(FillRule rule, double l, double t, double r, double b)
    {
        var both = Geometry.AreaInBox(Overlap, l, t, r, b);
        return Geometry.AreaInBox(Stem, l, t, r, b) + Geometry.AreaInBox(Bar, l, t, r, b) - (rule == FillRule.NonZero ? both : 2 * both);
    }

    public static TheoryData<FillRule, double> ClipThroughTheOverlap => new()
    {
        { FillRule.NonZero, 20.55 }, { FillRule.NonZero, 21.5 }, { FillRule.NonZero, 22.9 },
        { FillRule.EvenOdd, 20.55 }, { FillRule.EvenOdd, 21.5 }, { FillRule.EvenOdd, 22.9 },
    };

    [Theory]
    [MemberData(nameof(ClipThroughTheOverlap))]
    public void A_clip_edge_through_the_inside_of_two_overlapping_contours_cuts_them_exactly(FillRule rule, double top)
    {
        // The clip's top edge runs through the overlap of the stem and the bar, where the winding number is 2: the fill rule
        // is taken over the part of each cell inside the clip, so the row it crosses is exact. Exact everywhere except in
        // the two cells where an edge of each contour crosses inside the clip, the stem's sides and the bar's bottom,
        // (20, 23) and (26, 23): the limit of overlapping contours, the same with and without the clip (pinned).
        var box = new PixelBox(11.35, top, 29.85, 34.65);
        var path = Polygons(Stem, Bar);
        var mask = Canvas.Coverage(path, Affine.Identity, rule, box).Value;
        var whole = Canvas.Coverage(path, Affine.Identity, rule, new PixelBox(0, 0, 48, 48)).Value;
        (int X, int Y)[] limit = [(20, 23), (26, 23)];

        ShouldMatch(mask, box, (l, t, r, b) => limit.Contains(((int)Math.Floor(l), (int)Math.Floor(t))) ? mask.At((int)Math.Floor(l), (int)Math.Floor(t)) / 255.0 : StemAndBar(rule, l, t, r, b));
        limit.Select(cell => mask.At(cell.X, cell.Y)).Should().Equal(limit.Select(cell => whole.At(cell.X, cell.Y)));
        limit.Select(cell => mask.At(cell.X, cell.Y)).Should().Equal((byte)255, (byte)178);
        limit.Select(cell => Math.Round(255 * StemAndBar(rule, cell.X, cell.Y, cell.X + 1, cell.Y + 1), 1)).Should().Equal(rule == FillRule.NonZero ? [193.8, 147.9] : [132.6, 117.3]);
    }

    [Theory]
    [MemberData(nameof(Rules))]
    public void Overlapping_contours_inside_the_clip_are_covered_as_without_the_clip(FillRule rule)
    {
        // Pixel (20, 18), crossed by an edge of each contour and wholly inside the clip, keeps the value it has without the
        // clip; so does every pixel wholly inside the clip.
        var box = new PixelBox(11.35, 10.55, 29.85, 34.65);
        var path = Polygons(Stem, Bar);
        var mask = Canvas.Coverage(path, Affine.Identity, rule, box).Value;
        var whole = Canvas.Coverage(path, Affine.Identity, rule, new PixelBox(0, 0, 48, 48)).Value;

        for (var y = 11; y < 34; y++)
        {
            for (var x = 12; x < 29; x++)
            {
                mask.At(x, y).Should().Be(whole.At(x, y), $"pixel ({x}, {y}) lies wholly inside the clip");
            }
        }
    }

    [Theory]
    [InlineData(FillRule.NonZero, 2)]
    [InlineData(FillRule.NonZero, 3)]
    [InlineData(FillRule.EvenOdd, 2)]
    [InlineData(FillRule.EvenOdd, 3)]
    public void A_winding_number_of_two_or_three_over_a_clipped_cell_follows_the_rule_over_that_part(FillRule rule, int copies)
    {
        // The same square two or three times, wound the same way: non-zero covers it, even-odd covers it for three copies and
        // leaves it empty for two; a clip box with edges inside pixels cuts it. Every cell inside the square that the clip's
        // edges cross is exact. (The cells the square's own edges cross hold the coinciding edges of the copies, contours
        // that meet inside a cell: the documented limit, with or without a clip; they are left out here.)
        var square = Rectangle(6.3, 5.2, 21.7, 19.6);
        var box = new PixelBox(9.45, 8.65, 16.25, 30.4);
        var mask = Canvas.Coverage(Polygons([.. Enumerable.Repeat(square, copies)]), Affine.Identity, rule, box).Value;
        var covered = rule == FillRule.NonZero || copies % 2 == 1;
        var checkedCells = 0;

        for (var y = 8; y < 19; y++)
        {
            for (var x = 9; x < 17; x++)
            {
                var (l, t, r, b) = (Math.Max(x, box.Left), Math.Max(y, box.Top), Math.Min(x + 1, box.Right), Math.Min(y + 1, box.Bottom));
                var expected = covered ? 255 * (r - l) * (b - t) : 0;
                Math.Abs(mask.At(x, y) - expected).Should().BeLessThanOrEqualTo(OneRounding, $"pixel ({x}, {y})");
                checkedCells += (r - l) * (b - t) < 1 ? 1 : 0;
            }
        }

        checkedCells.Should().Be((2 * 11) + 6, "the cells along the clip's left and right edges (rows 8 to 18) and its top (columns 10 to 15)");
    }

    [Theory]
    [MemberData(nameof(Rules))]
    public void Contours_of_opposite_direction_cut_by_the_clip_are_covered_exactly_where_their_edges_do_not_meet(FillRule rule)
    {
        // Two overlapping rectangles wound in opposite directions: the overlap has winding 0 under both rules. The clip's
        // edges cut the overlap inside pixels; exact except where an edge of each contour crosses inside the clipped part
        // of a cell, where the parts cancel (the documented limit): (12, 12) and (18, 12) here.
        var first = Rectangle(5.3, 6.2, 18.7, 21.4);
        var second = Rectangle(12.6, 12.8, 27.1, 28.3, reversed: true);
        var both = Rectangle(12.6, 12.8, 18.7, 21.4);
        var box = new PixelBox(8.35, 15.45, 24.65, 26.75);
        var mask = Canvas.Coverage(Polygons(first, second), Affine.Identity, rule, box).Value;
        (int X, int Y)[] limit = [(12, 21), (18, 21)];

        ShouldMatch(mask, box, (l, t, r, b) => limit.Contains(((int)Math.Floor(l), (int)Math.Floor(t)))
            ? mask.At((int)Math.Floor(l), (int)Math.Floor(t)) / 255.0
            : Geometry.AreaInBox(first, l, t, r, b) + Geometry.AreaInBox(second, l, t, r, b) - (2 * Geometry.AreaInBox(both, l, t, r, b)));
    }

    [Theory]
    [MemberData(nameof(Rules))]
    public void A_clip_sliver_through_the_overlap_gives_its_area_and_no_more(FillRule rule)
    {
        // A box 1e-9 pixel high through the overlap of the stem and the bar: every cell's part inside it is about 1e-9, its
        // winding 2 there; the coverage is that tiny area (non-zero) or nothing (even-odd), never a blown-up value.
        var box = new PixelBox(11.35, 21.3, 29.85, 21.3 + 1e-9);
        var mask = Canvas.Coverage(Polygons(Stem, Bar), Affine.Identity, rule, box).Value;

        mask.Values.Should().AllSatisfy(value => value.Should().Be(0));
    }

    [Theory]
    [MemberData(nameof(Rules))]
    public void A_clip_smaller_than_a_pixel_inside_one_pixel_lets_its_area_through(FillRule rule)
    {
        var box = new PixelBox(12.2, 7.3, 12.7, 7.65);
        var mask = Canvas.Coverage(Polygons(Rectangle(2.5, 2.5, 30.5, 20.5)), Affine.Identity, rule, box).Value;

        mask.Width.Should().Be(1);
        mask.Height.Should().Be(1);
        Math.Abs(mask.At(12, 7) - (255 * 0.5 * 0.35)).Should().BeLessThanOrEqualTo(OneRounding);
    }

    [Theory]
    [MemberData(nameof(Rules))]
    public void A_clip_edge_on_a_pixel_boundary_cuts_exactly(FillRule rule)
    {
        (double X, double Y)[] triangle = [(3.3, 30.4), (18.7, 2.15), (35.1, 27.8)];
        var box = new PixelBox(10, 5, 30, 20);
        var mask = Canvas.Coverage(Polygons(triangle), Affine.Identity, rule, box).Value;

        ShouldMatch(mask, box, (l, t, r, b) => Geometry.AreaInBox(triangle, l, t, r, b));
    }

    [Theory]
    [MemberData(nameof(Rules))]
    public void A_clip_edge_through_a_vertex_cuts_exactly(FillRule rule)
    {
        // A diamond whose top vertex and side vertices lie exactly on the clip's edges, inside pixels.
        (double X, double Y)[] diamond = [(20.35, 8.45), (31.65, 19.2), (20.35, 30.75), (9.15, 19.2)];
        var box = new PixelBox(9.15, 8.45, 26.4, 19.2);
        var mask = Canvas.Coverage(Polygons(diamond), Affine.Identity, rule, box).Value;

        ShouldMatch(mask, box, (l, t, r, b) => Geometry.AreaInBox(diamond, l, t, r, b));
    }
}
