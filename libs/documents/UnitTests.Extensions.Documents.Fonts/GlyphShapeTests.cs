using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Fonts;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.ErrorPaths;
using static UnitTests.Extensions.Documents.Fonts.GlyphBytes;

namespace UnitTests.Extensions.Documents.Fonts;

/// <summary>
/// The outline decoder on hand-made glyphs (<see cref="GlyphBytes"/>): every encoding of a simple glyph, every kind of
/// component placement, the limits, and damaged data. The expected points are worked out by hand from the OpenType
/// specification ("glyf — Glyph data"); <see cref="GlyphShapeReferenceTests"/> compares real fonts with fontTools.
/// </summary>
public class GlyphShapeTests
{
    private static readonly byte[] Lato = TestFonts.Bytes("Lato-Regular.ttf");

    private static readonly (int X, int Y, bool On)[] Square = [(0, 0, true), (100, 0, true), (100, 100, true), (0, 100, true)];

    private static TrueTypeFont FontWith(params byte[][] glyphs)
    {
        var result = FontParser.Parse(FontFiles.WithGlyphs(Lato, glyphs));
        return result.IsSuccess ? result.Value : throw new InvalidOperationException(result.Error.ToString());
    }

    private static GlyphShape ShapeOf(TrueTypeFont font, int glyph)
    {
        var result = font.Shape(new GlyphId((ushort)glyph));
        return result.IsSuccess ? result.Value : throw new InvalidOperationException(result.Error.ToString());
    }

    private static Error ErrorOf(TrueTypeFont font, int glyph)
    {
        var result = font.Shape(new GlyphId((ushort)glyph));
        result.IsFailure.Should().BeTrue();
        return result.Error;
    }

    private static IEnumerable<(double X, double Y, bool On)> PointsOf(GlyphShape shape) =>
        shape.Contours.SelectMany(contour => contour.Points).Select(point => (point.X, point.Y, point.OnCurve));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(65535)]
    public void A_glyph_id_the_font_does_not_have_is_an_error_value(int beyond)
    {
        var font = TestFonts.Regular;
        var glyph = font.GlyphCount + beyond;
        if (glyph > ushort.MaxValue)
        {
            glyph = ushort.MaxValue;
        }

        var error = ErrorOf(font, glyph);

        error.Code.Should().Be(FontErrorCodes.GlyphOutOfRange);
        error.Metadata![ErrorMetadataKeys.Value].Should().Be(glyph.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void A_glyph_without_an_outline_has_no_contours()
    {
        var font = FontWith([], Simple(Square));

        ShapeOf(font, 0).Should().Be(GlyphShape.Empty);
        ShapeOf(font, 5).Should().Be(GlyphShape.Empty, "glyphs past the hand-made ones have no outline");
    }

    [Fact]
    public void A_simple_glyph_keeps_its_points_and_contours()
    {
        var font = FontWith([], Simple(Square, [(30, 30, true), (60, 30, false), (60, 60, true)]));

        var shape = ShapeOf(font, 1);

        shape.Contours.Should().HaveCount(2);
        shape.Contours[0].Points.Select(point => (point.X, point.Y, point.OnCurve)).Should().Equal((0d, 0d, true), (100d, 0d, true), (100d, 100d, true), (0d, 100d, true));
        shape.Contours[1].Points.Select(point => (point.X, point.Y, point.OnCurve)).Should().Equal((30d, 30d, true), (60d, 30d, false), (60d, 60d, true));
    }

    [Fact]
    public void Short_vectors_repeated_flags_and_same_values_are_decoded()
    {
        // Point 0: on, x short positive 10, y same (0). Points 1 and 2: one flag repeated once: on, x short negative
        // (5 each), y words. Point 3: off, x and y words.
        byte[] body =
        [
            0x00, 0x00,                // instructionLength 0
            0x33, 0x0B, 0x01, 0x00,    // flags: 0x33; 0x0B repeated 1 more time; 0x00
            10, 5, 5, 0x01, 0x2C,      // x: +10, -5, -5, +300 (word)
            0x00, 0x64, 0xFF, 0xCE, 0x00, 0x07, // y: (same), +100, -50, +7 (words)
        ];
        var font = FontWith([], Raw([3], body));

        PointsOf(ShapeOf(font, 1)).Should().Equal((10d, 0d, true), (5d, 100d, true), (0d, 50d, true), (300d, 57d, false));
    }

    [Fact]
    public void Instructions_are_skipped()
    {
        // instructionLength 3 and three instruction bytes, then one point: flag 0x37 (on, x and y short and positive),
        // x +1, y +0x33.
        var font = FontWith([], Raw([0], [0x00, 0x03, 0xB0, 0x01, 0x2C, 0x37, 0x01, 0x33]));

        PointsOf(ShapeOf(font, 1)).Should().Equal((1d, 51d, true));
    }

    [Fact]
    public void A_repeated_flag_that_runs_past_the_points_is_invalid()
    {
        var font = FontWith([], Raw([1], [0x00, 0x00, 0x09, 0x02, 0, 0, 0, 0]));

        var error = ErrorOf(font, 1);

        error.Code.Should().Be(FontErrorCodes.Parse.InvalidValue);
        error.Message.Should().Contain("glyph 1").And.Contain("repeated flag");
        error.Metadata!.Should().Contain(ErrorMetadataKeys.FontName, "Lato-Regular");
    }

    [Fact]
    public void Reserved_point_flag_bit_7_is_refused()
    {
        var font = FontWith([], Raw([0], [0x00, 0x00, 0x81, 0x00, 0x00, 0x00, 0x00]));

        ErrorOf(font, 1).Message.Should().Contain("bit 7");
    }

    [Fact]
    public void Contour_ends_must_increase()
    {
        var font = FontWith([], Raw([2, 2], [0x00, 0x00, 0x31, 0x31, 0x31, 0, 0, 0]));

        ErrorOf(font, 1).Message.Should().Contain("contour 1");
    }

    [Fact]
    public void Every_truncation_of_a_glyph_is_an_error_and_never_throws()
    {
        var simple = Simple(Square, [(30, 30, true), (60, 30, false), (60, 60, true)]);
        var composite = Composite(new Component(1, 10, 20, ArgsAreXyValues | WeHaveATwoByTwo, F2Dot14(0.5), F2Dot14(-0.25), 0, F2Dot14(1)), new Component(1, 0, 0, ArgsAreXyValues));
        foreach (var glyph in new[] { simple, composite })
        {
            for (var length = 10; length < glyph.Length; length++)
            {
                var font = FontWith([], simple, glyph[..length]);

                var result = font.Shape(new GlyphId(2));

                result.IsFailure.Should().BeTrue($"a glyph cut to {length} of {glyph.Length} bytes is incomplete");
                result.Error.Code.Should().Be(FontErrorCodes.Parse.Truncated);
            }
        }
    }

    [Fact]
    public void Implied_on_curve_points_become_explicit_segments()
    {
        var contour = new GlyphContour([new(0, 0, true), new(100, 0, false), new(100, 100, false), new(0, 100, true)]);

        var segments = contour.Segments();

        segments.Start.Should().Be(new OutlinePoint(0, 0, true));
        segments.Segments.Should().Equal(
            new OutlineSegment(new(100, 50, true), new(100, 0, false)),
            new OutlineSegment(new(0, 100, true), new(100, 100, false)),
            new OutlineSegment(new(0, 0, true), null));
    }

    [Fact]
    public void A_contour_of_off_curve_points_starts_between_its_last_and_first_point()
    {
        var contour = new GlyphContour([new(0, 0, false), new(100, 0, false), new(100, 100, false), new(0, 100, false)]);

        var segments = contour.Segments();

        segments.Start.Should().Be(new OutlinePoint(0, 50, true));
        segments.Segments.Should().Equal(
            new OutlineSegment(new(50, 0, true), new(0, 0, false)),
            new OutlineSegment(new(100, 50, true), new(100, 0, false)),
            new OutlineSegment(new(50, 100, true), new(100, 100, false)),
            new OutlineSegment(new(0, 50, true), new(0, 100, false)));
    }

    [Fact]
    public void A_contour_that_starts_off_curve_starts_at_its_first_on_curve_point()
    {
        var contour = new GlyphContour([new(0, 0, false), new(100, 0, true), new(100, 100, true)]);

        var segments = contour.Segments();

        segments.Start.Should().Be(new OutlinePoint(100, 0, true));
        segments.Segments.Should().Equal(
            new OutlineSegment(new(100, 100, true), null),
            new OutlineSegment(new(100, 0, true), new(0, 0, false)));
    }

    [Fact]
    public void A_contour_of_one_point_has_no_segments()
    {
        new GlyphContour([new(5, 7, false)]).Segments().Should().Be(new ContourSegments(new(5, 7, true), []));
    }

    [Fact]
    public void An_empty_contour_has_no_segments()
    {
        var origin = new ContourSegments(new(0, 0, true), []);

        new GlyphContour([]).Segments().Should().Be(origin);
        new GlyphContour(default).Segments().Should().Be(origin);
    }

    [Fact]
    public void A_component_with_an_offset_is_moved()
    {
        var font = FontWith([], Simple(Square), Composite(new Component(1, 10, -20, ArgsAreXyValues), new Component(1, 200, 0, ArgsAreXyValues)));

        var shape = ShapeOf(font, 2);

        shape.Contours.Should().HaveCount(2);
        PointsOf(shape).Should().Equal(
            (10d, -20d, true), (110d, -20d, true), (110d, 80d, true), (10d, 80d, true),
            (200d, 0d, true), (300d, 0d, true), (300d, 100d, true), (200d, 100d, true));
    }

    [Fact]
    public void A_scale_applies_to_the_points_and_by_default_not_to_the_offset()
    {
        var unscaled = Composite(new Component(1, -7, 3, ArgsAreXyValues | WeHaveAScale, F2Dot14(0.5)));
        var explicitUnscaled = Composite(new Component(1, -7, 3, ArgsAreXyValues | WeHaveAScale | UnscaledComponentOffset, F2Dot14(0.5)));
        var font = FontWith([], Simple(Square), unscaled, explicitUnscaled);

        // x' = 0.5·x − 7, y' = 0.5·y + 3
        var expected = new[] { (-7d, 3d, true), (43d, 3d, true), (43d, 53d, true), (-7d, 53d, true) };
        PointsOf(ShapeOf(font, 2)).Should().Equal(expected);
        PointsOf(ShapeOf(font, 3)).Should().Equal(expected);
    }

    [Fact]
    public void A_scaled_component_offset_is_transformed_with_the_points()
    {
        var scaled = Composite(new Component(1, -8, 4, ArgsAreXyValues | WeHaveAScale | ScaledComponentOffset, F2Dot14(0.5)));
        var both = Composite(new Component(1, -8, 4, ArgsAreXyValues | WeHaveAScale | ScaledComponentOffset | UnscaledComponentOffset, F2Dot14(0.5)));
        var font = FontWith([], Simple(Square), scaled, both);

        // x' = 0.5·(x − 8), y' = 0.5·(y + 4); with both flags the offset is not scaled.
        PointsOf(ShapeOf(font, 2)).Should().Equal((-4d, 2d, true), (46d, 2d, true), (46d, 52d, true), (-4d, 52d, true));
        PointsOf(ShapeOf(font, 3)).Should().Equal((-8d, 4d, true), (42d, 4d, true), (42d, 54d, true), (-8d, 54d, true));
    }

    [Fact]
    public void An_x_and_y_scale_and_a_two_by_two_matrix_transform_the_points()
    {
        var xy = Composite(new Component(1, 0, 0, ArgsAreXyValues | WeHaveAnXAndYScale, F2Dot14(1.5), F2Dot14(-0.25)));

        // Read as a, b, c, d: x' = a·x + c·y, y' = b·x + d·y. With a = 0, b = 1, c = −1, d = 0 a quarter turn.
        var turn = Composite(new Component(1, 0, 0, ArgsAreXyValues | WeHaveATwoByTwo, 0, F2Dot14(1), F2Dot14(-1), 0));
        var font = FontWith([], Simple(Square), xy, turn);

        PointsOf(ShapeOf(font, 2)).Should().Equal((0d, 0d, true), (150d, 0d, true), (150d, -25d, true), (0d, -25d, true));
        PointsOf(ShapeOf(font, 3)).Should().Equal((0d, 0d, true), (0d, 100d, true), (-100d, 100d, true), (-100d, 0d, true));
    }

    [Fact]
    public void A_component_matched_by_points_lands_its_point_on_the_glyph_s_point()
    {
        // First the square as it is; then a half-size square whose point 0 lands on point 2 of the glyph so far (100, 100).
        // The transform comes first: the matched point is the transformed one.
        var matched = Composite(
            new Component(1, 0, 0, ArgsAreXyValues),
            new Component(1, 2, 1, WeHaveAScale, F2Dot14(0.5)));
        var font = FontWith([], Simple(Square), matched);

        // Point 1 of the half square is (50, 0) after the scale, so it moves by (50, 100).
        PointsOf(ShapeOf(font, 2)).Should().Equal(
            (0d, 0d, true), (100d, 0d, true), (100d, 100d, true), (0d, 100d, true),
            (50d, 100d, true), (100d, 100d, true), (100d, 150d, true), (50d, 150d, true));
    }

    [Fact]
    public void A_point_number_outside_the_points_is_invalid()
    {
        var font = FontWith([], Simple(Square), Composite(new Component(1, 0, 0, ArgsAreXyValues), new Component(1, 4, 0, 0)), Composite(new Component(1, 0, 4, 0)));

        ErrorOf(font, 2).Message.Should().Contain("points 4 and 0");
        ErrorOf(font, 3).Message.Should().Contain("points 0 and 4");
    }

    [Fact]
    public void Nested_composites_are_resolved()
    {
        var font = FontWith([], Simple(Square), Composite(new Component(1, 10, 0, ArgsAreXyValues)), Composite(new Component(2, 0, 5, ArgsAreXyValues | WeHaveAScale, F2Dot14(0.5))));

        PointsOf(ShapeOf(font, 3)).Should().Equal((5d, 5d, true), (55d, 5d, true), (55d, 55d, true), (5d, 55d, true));
    }

    [Fact]
    public void A_glyph_that_contains_itself_is_refused_at_the_nesting_limit()
    {
        var font = FontWith([], Composite(new Component(2, 0, 0, ArgsAreXyValues)), Composite(new Component(1, 0, 0, ArgsAreXyValues)));

        var error = ErrorOf(font, 1);

        error.Code.Should().Be(FontErrorCodes.Parse.InvalidValue);
        error.Message.Should().Contain("64 levels");
    }

    [Fact]
    public void A_component_that_is_not_a_glyph_of_the_font_is_invalid()
    {
        var font = FontWith([], Composite(new Component(60000, 0, 0, ArgsAreXyValues)));

        ErrorOf(font, 1).Message.Should().Contain("component glyph 60000");
    }

    [Fact]
    public void Components_used_many_times_are_bounded_by_the_component_limit()
    {
        // 300 references to a glyph of 300 references to an empty glyph: 90 300 component references and no points.
        var inner = Composite([.. Enumerable.Repeat(new Component(0, 0, 0, ArgsAreXyValues), 300)]);
        var outer = Composite([.. Enumerable.Repeat(new Component(1, 0, 0, ArgsAreXyValues), 300)]);
        var font = FontWith([], inner, outer);

        ShapeOf(font, 1).Should().Be(GlyphShape.Empty, "300 references to an empty glyph are within the limit");
        ErrorOf(font, 2).Message.Should().Contain("65536 component references");
    }

    [Fact]
    public void The_points_of_all_components_together_are_bounded()
    {
        var many = Simple([.. Enumerable.Range(0, 1000).Select(index => (index, index % 7, true))]);
        var font = FontWith([], many, Composite([.. Enumerable.Repeat(new Component(1, 0, 0, ArgsAreXyValues), 65)]), Composite([.. Enumerable.Repeat(new Component(1, 0, 0, ArgsAreXyValues), 66)]));

        ShapeOf(font, 2).Contours.Should().HaveCount(65, "65 000 points are within the limit");
        ErrorOf(font, 3).Message.Should().Contain("65536 points");
    }
}
