using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.Documents.Typesetting;

namespace UnitTests.Extensions.Documents.Layout;

/// <summary>
/// THE RULE FOR THE GAPS AROUND A GLYPH'S INK on outlines made for the purpose (design units, y down, as Skia's path): which
/// points make a glyph's gap, and how the gaps are joined; and on a real glyph, the band and the widening.
/// </summary>
public class InkGapsTests
{
    private static InkSegment Line(double x0, double y0, double x1, double y1) => new(x0, y0, x1, y1, 0, 0, IsCurve: false);

    private static InkSegment Curve(double x0, double y0, double cx, double cy, double x1, double y1) => new(x0, y0, x1, y1, cx, cy, IsCurve: true);

    private static (double Left, double Right)? Gap(double top, double bottom, params InkSegment[] outline) =>
        InkGaps.GapOf([.. outline], top, bottom, new InkOutlines());

    private static void ShouldBe((double Left, double Right)? gap, double left, double right)
    {
        gap.Should().NotBeNull();
        gap!.Value.Left.Should().BeApproximately(left, 1e-9);
        gap.Value.Right.Should().BeApproximately(right, 1e-9);
    }

    [Fact]
    public void A_slanted_side_gives_the_two_places_where_it_crosses_the_band_s_edges() =>
        ShouldBe(Gap(-10, 10, Line(0, -100, 200, 100)), 90, 110);

    [Fact]
    public void A_vertical_stem_alone_gives_one_x_and_so_no_gap() =>
        Gap(-10, 10, Line(100, -500, 100, 500)).Should().BeNull("Skia keeps an interval only where its left end lies before its right one");

    [Fact]
    public void A_horizontal_side_inside_the_band_counts_with_its_points() =>
        Gap(-10, 10, Line(10, 0, 30, 0)).Should().Be((10.0, 30.0));

    [Fact]
    public void A_side_meets_the_band_s_edge_with_its_start_but_not_with_its_end() =>
        Gap(-10, 10, Line(40, -20, 40, -10), Line(60, -10, 60, -30), Line(80, -20, 80, 20)).Should().Be((60.0, 80.0),
            "t in [0, 1): the side ending on the edge at x = 40 adds nothing, the one starting on it at x = 60 does");

    [Fact]
    public void A_curve_s_crossings_are_the_roots_of_the_quadratic()
    {
        // y(t) = -100 (1 - t)^2 + 2·200 t (1 - t) - 100 t^2 = -100 + 600 t - 600 t^2 crosses y = 0 at t = (1 ± 1/√3) / 2,
        // and x(t) = 200 t.
        var gap = Gap(-1e-9, 1e-9, Curve(0, -100, 100, 200, 200, -100))!.Value;
        var x = 100 * (1 - 1 / Math.Sqrt(3));

        gap.Left.Should().BeApproximately(x, 1e-6);
        gap.Right.Should().BeApproximately(200 - x, 1e-6);
    }

    [Fact]
    public void A_curve_s_control_point_inside_the_band_widens_the_gap_though_the_curve_does_not_reach_the_band()
    {
        // The curve peaks at y = -8.5, above the band from -8 to 8; its control point (50, 3) lies inside. With the stem at x = 0
        // the gap reaches to the control point, as Skia counts it; without the curve the stem alone gives no gap.
        Gap(-8, 8, Line(0, -20, 0, 20), Curve(0, -20, 50, 3, 100, -20)).Should().Be((0.0, 50.0));
        Gap(-8, 8, Line(0, -20, 0, 20)).Should().BeNull();
    }

    [Fact]
    public void A_segment_whose_points_all_lie_outside_the_band_adds_nothing() =>
        ShouldBe(Gap(-10, 10, Line(0, -100, 200, 100), Line(500, 20, 900, 40), Curve(300, -50, 400, -40, 450, -30)), 90, 110);

    [Fact]
    public void Gaps_are_joined_in_one_pass_after_sorting_and_leave_the_pieces_between_them()
    {
        var pieces = InkGaps.Visible(0, 100, [(60, 70), (10, 20), (15, 30), (95, 120), (-5, 2), (62, 65)]);

        pieces.Should().Equal([(2.0, 10.0), (30.0, 60.0), (70.0, 95.0)]);
    }

    [Fact]
    public void A_line_wholly_inside_one_gap_leaves_nothing() =>
        InkGaps.Visible(10, 20, [(0, 30)]).Should().BeEmpty();

    [Fact]
    public void The_band_is_the_line_inset_by_half_a_pixel_and_each_gap_is_widened_by_the_thickness_up_to_13_pixels()
    {
        // A "j" of Lato at 24 pt, its pen at 0 and its baseline at 0: the underline of case dl-skip, 1.5 pt below the baseline
        // and 3.2 px thick, crosses the descender. The gap is the outline's own ends in the band (computed here the same way)
        // widened by min(T, 13 px) on each side; a band half a pixel larger moves them.
        var font = TestFonts.Regular;
        var glyph = Shaper.Shape("j", font, 24).Value.Single();
        var outlines = new InkOutlines();
        var (top, thickness) = (1.5, 3.2 * 0.75);
        var gaps = new List<(double Left, double Right)>();
        InkGaps.Add(gaps, outlines, font, 24, 0, [glyph], 0, top, thickness).IsSuccess.Should().BeTrue();
        var scale = 24.0 / font.Metrics.UnitsPerEm;
        var own = InkGaps.GapOf(outlines.Of(font, glyph.Glyph).Value, (top + 0.375) / scale, (top + thickness - 0.375) / scale, outlines)!.Value;

        gaps.Should().ContainSingle().Which.Should().Be((own.Left * scale - thickness, own.Right * scale + thickness));
        var wider = InkGaps.GapOf(outlines.Of(font, glyph.Glyph).Value, top / scale, (top + thickness) / scale, outlines)!.Value;
        (wider.Left < own.Left || wider.Right > own.Right).Should().BeTrue("the inset moves at least one end");

        var thick = new List<(double Left, double Right)>();
        InkGaps.Add(thick, outlines, font, 24, 0, [glyph], 0, top, 20).IsSuccess.Should().BeTrue();
        var thickOwn = InkGaps.GapOf(outlines.Of(font, glyph.Glyph).Value, (top + 0.375) / scale, (top + 20 - 0.375) / scale, outlines)!.Value;
        thick.Single().Left.Should().BeApproximately(thickOwn.Left * scale - 13 * 0.75, 1e-9, "the widening stops at 13 px");
    }

    [Fact]
    public void A_glyph_s_offset_from_shaping_moves_its_gap_and_its_outline_is_decoded_once()
    {
        var font = TestFonts.Regular;
        var glyph = Shaper.Shape("j", font, 24).Value.Single();
        var outlines = new InkOutlines();
        var (plain, moved) = (new List<(double Left, double Right)>(), new List<(double Left, double Right)>());
        InkGaps.Add(plain, outlines, font, 24, 0, [glyph, glyph], 0, 1.5, 2.4).IsSuccess.Should().BeTrue();
        InkGaps.Add(moved, outlines, font, 24, 0, [glyph with { XOffset = 1 }, glyph], 0, 1.5, 2.4).IsSuccess.Should().BeTrue();

        moved[0].Left.Should().BeApproximately(plain[0].Left + 1, 1e-9);
        moved[1].Should().Be(plain[1]);
        outlines.Decoded.Should().Be(1, "one glyph of one font, used four times");
    }

    [Fact]
    public void A_glyph_s_vertical_offset_from_shaping_moves_the_band_against_its_outline()
    {
        // The band is taken from the glyph's own origin: a glyph drawn 0.5 pt higher meets the band as a line 0.5 pt lower would.
        var font = TestFonts.Regular;
        var glyph = Shaper.Shape("j", font, 24).Value.Single();
        var outlines = new InkOutlines();
        var (raised, lower) = (new List<(double Left, double Right)>(), new List<(double Left, double Right)>());
        InkGaps.Add(raised, outlines, font, 24, 0, [glyph with { YOffset = 0.5 }], 0, 1.5, 2.4).IsSuccess.Should().BeTrue();
        InkGaps.Add(lower, outlines, font, 24, 0, [glyph], 0, 2.0, 2.4).IsSuccess.Should().BeTrue();
        var plain = new List<(double Left, double Right)>();
        InkGaps.Add(plain, outlines, font, 24, 0, [glyph], 0, 1.5, 2.4).IsSuccess.Should().BeTrue();

        raised.Single().Left.Should().BeApproximately(lower.Single().Left, 1e-9);
        raised.Single().Right.Should().BeApproximately(lower.Single().Right, 1e-9);
        raised.Single().Should().NotBe(plain.Single(), "the offset changes where the descender meets the band");
    }

    [Fact]
    public void A_later_line_is_cut_by_the_gaps_of_an_earlier_line_whose_clips_reach_it()
    {
        // Two underlines on one piece, a 24 pt and a 22 pt decorating box: both lie two pixels below the baseline and overlap,
        // and their gaps differ. The second is painted under the clips of both (Blink keeps the first one's clips).
        var font = TestFonts.Regular;
        var glyphs = Shaper.Shape("gjpqy fy", font, 24).Value;
        TextDecorations.Resolved Underline(double size) => new(new TextDecoration(TextDecorationLine.Underline, Color.Black, "Lato", size.Pt(), FontWeight.Regular), font);
        var outlines = new InkOutlines();
        var drawn = TextDecorations.Of([Underline(24), Underline(22)], 0, glyphs, 30, 22, FontMetricRounding.WholePixels,
            new TextDecorations.Ink(font, 24, TextDecorationSkipInk.Auto, outlines)).Value.Before;
        var width = glyphs.Sum(glyph => glyph.Advance);
        var (first, second) = (new List<(double Left, double Right)>(), new List<(double Left, double Right)>());
        var top = drawn[0].Offset.Y;
        InkGaps.Add(first, outlines, font, 24, 0, glyphs, 30, top, 2.4).IsSuccess.Should().BeTrue();
        InkGaps.Add(second, outlines, font, 24, 0, glyphs, 30, top, 2.2).IsSuccess.Should().BeTrue();
        var alone = InkGaps.Visible(0, width, [.. second]);
        var both = InkGaps.Visible(0, width, [.. first, .. second]);

        both.Should().NotEqual(alone, "the earlier line's gaps are wider than the later one's");
        drawn.Skip(InkGaps.Visible(0, width, [.. first]).Count).Select(piece => (piece.Offset.X, piece.Offset.X + ((RuleFragment)piece.Fragment).Size.Width))
            .Should().Equal(both);
    }
}
