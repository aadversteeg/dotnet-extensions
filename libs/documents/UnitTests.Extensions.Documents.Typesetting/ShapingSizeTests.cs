using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.Documents.Typesetting;

namespace UnitTests.Extensions.Documents.Typesetting;

/// <summary>
/// The two sizes text is shaped at (<see cref="ShapingSize"/>) and how the shaper uses them. Chromium's sizes as its print
/// shows them (<c>tools/reference/chromium</c>, cases <c>ta-sizes</c>, <c>ta-px</c>, <c>ta-kerning-scale</c>,
/// <c>ta-marks</c>): the size of the font in the PDF (<c>Tf</c>) is the floored one, the distance between two glyphs is
/// the font's advance at the truncated one. Lato Regular (2000 units per em): x = 996 units, i = 480, H = 1527 with
/// H + i kerned by −15, and U+0302 put on x by an anchor difference of 497 − (−400) = 897 units.
/// </summary>
public class ShapingSizeTests
{
    private const double PointsPerPixel = 0.75;

    [Theory]
    // 8 pt = 10.667 px: the PDF's font size is 10.66 px, an 'i' advances 480 × 10.65625 / 2000 px (ta-sizes).
    [InlineData(8.0, 10.66, 10.65625)]
    [InlineData(10.0, 13.33, 13.328125)]
    [InlineData(11.0, 14.66, 14.65625)]
    [InlineData(13.0, 17.33, 17.328125)]
    // 10.65625 px is a multiple of 1/64 px, but its float times 100 is floored first (ta-px: PDF size 7.987 pt).
    [InlineData(10.65625 * PointsPerPixel, 10.65, 10.640625)]
    // A size whose float lies below its decimal value: 8.4 px is the float 8.3999996.
    [InlineData(8.4 * PointsPerPixel, 8.39, 8.375)]
    public void A_size_that_is_not_a_whole_number_of_sixty_fourth_pixels_is_floored_then_truncated(double points, double positions, double advances)
    {
        var size = ShapingSize.FlooredToPixelFractions(points);

        (size.Positions / PointsPerPixel).Should().BeApproximately(positions, 1e-5);
        (size.Advances / PointsPerPixel).Should().Be(advances);
    }

    [Theory]
    [InlineData(9.0)]
    [InlineData(10.5)]
    [InlineData(12.0)]
    [InlineData(11 * PointsPerPixel)]
    [InlineData(24.0)]
    public void A_whole_pixel_size_and_eleven_pixels_are_kept(double points)
    {
        ShapingSize.FlooredToPixelFractions(points).Should().Be(ShapingSize.Exact(points));
    }

    [Fact]
    public void Exact_shapes_as_the_size_alone_does()
    {
        const string text = "Hi x̂ AVAWAY";

        Shaper.Shape(text, TestFonts.Regular, ShapingSize.Exact(8)).Value.Should().Equal(Shaper.Shape(text, TestFonts.Regular, 8).Value);
    }

    [Fact]
    public void The_font_s_advance_is_at_the_truncated_size_and_kerning_at_the_floored_size()
    {
        var size = ShapingSize.FlooredToPixelFractions(8);

        var glyphs = Shaper.Shape("Hi", TestFonts.Regular, size).Value;

        glyphs[0].Advance.Should().BeApproximately((1527 * size.Advances - 15 * size.Positions) / 2000, 1e-12);
        glyphs[1].Advance.Should().BeApproximately(480 * size.Advances / 2000, 1e-12);
        (glyphs[1].Advance / PointsPerPixel).Should().BeApproximately(2.5575, 1e-4, "Chromium's Td after an 'i' at 8 pt is 2.5575256 px");
    }

    [Fact]
    public void A_mark_keeps_no_advance_and_sits_on_its_base_by_the_anchor_difference_at_the_floored_size()
    {
        var size = ShapingSize.FlooredToPixelFractions(8);

        var glyphs = Shaper.Shape("x̂", TestFonts.Regular, size).Value;

        glyphs[0].Advance.Should().BeApproximately(996 * size.Advances / 2000, 1e-12);
        glyphs[1].Advance.Should().Be(0);
        // Drawn at the base's pen plus the anchor difference: the base's advance taken back as it was scaled.
        (glyphs[0].Advance + glyphs[1].XOffset).Should().BeApproximately(897 * size.Positions / 2000, 1e-12);
        // Chromium's Td from x to its mark at 8 pt is 4.7810059 px, in HarfBuzz's 16.16 positions (1/65536 px); at the
        // truncated size it would be 4.7793 px.
        ((glyphs[0].Advance + glyphs[1].XOffset) / PointsPerPixel).Should().BeApproximately(4.7810059, 2.0 / 65536);
    }

    [Fact]
    public void The_hyphen_at_a_soft_hyphen_advances_at_the_truncated_size()
    {
        var size = ShapingSize.FlooredToPixelFractions(8);
        var hyphen = Shaper.Shape("a­", TestFonts.Regular, size).Value[^1].BreakHyphen!;

        hyphen.Advance.Should().BeApproximately(TestFonts.Regular.AdvanceWidth(hyphen.Glyph) * size.Advances / 2000, 1e-12);
        Shaper.ShapeLinePiece("a­", TestFonts.Regular, size, showsHyphen: true, budget: null!).Value[^1].Should().Be(hyphen);
    }
}
