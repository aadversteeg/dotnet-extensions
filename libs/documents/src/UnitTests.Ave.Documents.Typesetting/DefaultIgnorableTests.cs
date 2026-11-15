using Ave.Documents.Fonts;
using Ave.Documents.TestSupport;
using Ave.Documents.Typesetting;

namespace UnitTests.Ave.Documents.Typesetting;

/// <summary>
/// Default-ignorable code points (Unicode <c>Default_Ignorable_Code_Point</c>, DerivedCoreProperties.txt): they take
/// no space and draw nothing, and need no glyph in the font (the Unicode Standard §5.21.6, "Characters Ignored for Display", and §5.3: such a character "should not be displayed with a visible fallback glyph").
/// The joiners act as HarfBuzz has them act: ZWJ is skipped while matching, so it does not stop a ligature; ZWNJ
/// stops one. A soft hyphen is a break opportunity that shows a hyphen only when the line breaks there (UAX #14 §5.4;
/// CSS Text 3 §5.3, <c>hyphens: manual</c>). Lato: f = 61, i = 98, "fi" = 67, U+2010 has a glyph.
/// </summary>
public class DefaultIgnorableTests
{
    private static string WithInvisible(string before, int codePoint, string after) => before + (char)codePoint + after;

    private static ShapedGlyph[] Shape(string text) => [.. Shaper.Shape(text, TestFonts.Regular, 10).Value];

    private static double Width(IEnumerable<ShapedGlyph> glyphs) => glyphs.Sum(glyph => glyph.Advance);

    [Theory]
    [InlineData(0x00AD)]
    [InlineData(0x200B)]
    [InlineData(0x2060)]
    [InlineData(0xFEFF)]
    [InlineData(0x034F)]
    public void A_default_ignorable_character_takes_no_space_draws_nothing_and_needs_no_glyph(int codePoint)
    {
        var shaped = Shaper.Shape(WithInvisible("co", codePoint, "operate"), TestFonts.Regular, 10);

        shaped.IsSuccess.Should().BeTrue(shaped.IsFailure ? shaped.Error.ToString() : string.Empty);
        var invisible = shaped.Value.Should().ContainSingle(glyph => glyph.IsInvisible).Which;
        invisible.Advance.Should().Be(0);
        Width(shaped.Value).Should().BeApproximately(Width(Shape("cooperate")), 1e-9);
    }

    [Fact]
    public void A_zero_width_joiner_does_not_stop_a_ligature()
    {
        var glyphs = Shape(WithInvisible("f", 0x200D, "i"));

        glyphs.Where(glyph => !glyph.IsInvisible).Select(glyph => glyph.Glyph).Should().Equal(new GlyphId(67));
    }

    [Fact]
    public void A_zero_width_non_joiner_stops_a_ligature()
    {
        var glyphs = Shape(WithInvisible("f", 0x200C, "i"));

        glyphs.Where(glyph => !glyph.IsInvisible).Select(glyph => glyph.Glyph).Should().Equal(new GlyphId(61), new GlyphId(98));
    }

    [Fact]
    public void A_soft_hyphen_shows_a_hyphen_only_where_the_line_breaks()
    {
        var glyphs = Shape(WithInvisible("co", 0x00AD, "operate"));
        var hyphen = TestFonts.Regular.GlyphFor(new System.Text.Rune(0x2010)).Value;

        var narrow = GreedyLineBreaker.BreakLines([[.. glyphs]], availableWidth: Width(Shape("co-")) + 0.5);
        var wide = GreedyLineBreaker.BreakLines([[.. glyphs]], availableWidth: 1000);

        narrow.Should().HaveCount(2);
        var first = narrow[0].Segments.SelectMany(segment => segment.Glyphs).ToList();
        first.Select(glyph => glyph.Text).Should().Equal(["c", "o", "\u2010"], "the hyphen shows, and copies as, the font's hyphen character");
        first[^1].Glyph.Should().Be(hyphen);
        first[^1].IsInvisible.Should().BeFalse();
        first[^1].Advance.Should().BeGreaterThan(0);
        wide.Should().ContainSingle().Which.Segments.SelectMany(segment => segment.Glyphs).Should().ContainSingle(glyph => glyph.IsInvisible);
    }

    [Fact]
    public void A_line_ending_at_a_soft_hyphen_makes_room_for_the_hyphen()
    {
        // "co" fits, "co-" does not: the break may not be taken at the soft hyphen, so "cooperate" moves on whole.
        var glyphs = Shape(WithInvisible("aa co", 0x00AD, "operate"));

        var lines = GreedyLineBreaker.BreakLines([[.. glyphs]], availableWidth: Width(Shape("aa co")) + 0.1);

        string.Concat(lines[0].Segments.SelectMany(segment => segment.Glyphs).Select(glyph => glyph.Text)).Should().Be("aa");
    }
}
