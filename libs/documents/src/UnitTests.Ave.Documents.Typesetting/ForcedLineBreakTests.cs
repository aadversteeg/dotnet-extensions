using Ave.Documents.TestSupport;
using Ave.Documents.Typesetting;

namespace UnitTests.Ave.Documents.Typesetting;

/// <summary>
/// A forced line break (U+2028, what <c>LineBreak()</c> writes; and the other mandatory breaks of UAX #14) ends the line
/// as an ordinary end of line does: the collapsible space before it is removed (CSS Text 3 §4.1.2), and shaping does not
/// reach across it, so no kerning, ligature or mark attachment joins the glyphs of two lines.
/// </summary>
public class ForcedLineBreakTests
{
    /// <summary>U+2028 LINE SEPARATOR, written as "|" in the test data.</summary>
    private static readonly string Break = char.ConvertFromUtf32(0x2028);

    /// <summary>The test data with "|" as the line separator and "~" as the no-break space U+00A0.</summary>
    private static string WithBreaks(string text) => text.Replace("|", Break, StringComparison.Ordinal).Replace("~", "\u00A0", StringComparison.Ordinal);

    private static ShapedGlyph[] Shape(string text) => [.. Shaper.Shape(text, TestFonts.Regular, 10).Value];

    private static double Width(IEnumerable<ShapedGlyph> glyphs) => glyphs.Sum(glyph => glyph.Advance);

    [Theory]
    [InlineData("ab |cd")]
    [InlineData("ab  |cd")]
    [InlineData("ab~ |cd")]
    public void The_space_before_a_forced_break_is_removed_like_the_space_at_a_wrap(string text)
    {
        var lines = GreedyLineBreaker.BreakLines([[.. Shape(WithBreaks(text))]], availableWidth: 1000);

        lines.Should().HaveCount(2);
        var expected = text.StartsWith("ab~", StringComparison.Ordinal) ? Width(Shape(WithBreaks("ab~"))) : Width(Shape("ab"));
        lines[0].Width.Should().BeApproximately(expected, 1e-9, "a no-break space is not collapsible and stays; ordinary spaces go");
    }

    [Fact]
    public void Two_forced_breaks_still_make_an_empty_line()
    {
        var lines = GreedyLineBreaker.BreakLines([[.. Shape("a" + Break + Break + "b")]], availableWidth: 1000);

        lines.Should().HaveCount(3);
    }

    [Fact]
    public void Kerning_does_not_reach_across_a_forced_break()
    {
        // Lato kerns A and V; across a line break the A keeps its own advance.
        var alone = Shape("A")[0].Advance;
        Shape("AV")[0].Advance.Should().BeLessThan(alone, "the pair is kerned when the letters touch");

        Shape("A" + Break + "V")[0].Advance.Should().Be(alone);
    }

    [Fact]
    public void A_ligature_does_not_form_across_a_forced_break()
    {
        Shape("f" + Break + "i").Where(glyph => !glyph.IsInvisible).Select(glyph => glyph.Glyph.Value).Should().Equal(61, 98);
    }

    [Fact]
    public void A_mark_after_a_forced_break_does_not_attach_to_the_letter_before_it()
    {
        var mark = Shape("a" + Break + "́").Last();

        mark.XOffset.Should().Be(Shape("́").Single().XOffset);
        mark.YOffset.Should().Be(Shape("́").Single().YOffset);
    }
}
