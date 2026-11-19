using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Fonts;
using Ave.Extensions.Documents.Typesetting;

namespace UnitTests.Extensions.Documents.Typesetting;

/// <summary>
/// The stand-in of an atomic inline in the line breaker (<see cref="ShapedGlyph.AtomicInline"/>): the object replacement
/// character U+FFFC with a fixed advance, and a break opportunity before and after it whatever is beside it (CSS Text 3
/// §5.1), also where UAX #14 alone has none.
/// </summary>
public class AtomicInlineBreakTests
{
    private static ValueList<ShapedGlyph> Unit(string text) =>
        [.. text.Select(character => new ShapedGlyph(new GlyphId(1), 1, character.ToString()))];

    private static ValueList<ShapedGlyph> Atomic(double advance) => [ShapedGlyph.AtomicInline(advance)];

    private static IEnumerable<string> Texts(ValueList<TextLine> lines) =>
        lines.Select(line => string.Concat(line.Segments.SelectMany(segment => segment.Glyphs).Select(glyph => glyph.Text)));

    [Theory]
    [InlineData("abc", "def")]
    [InlineData("abc ", " def")]
    [InlineData("(a", ")")]
    [InlineData("a", ".")]
    public void A_line_may_break_before_and_after_an_atomic_inline_beside_any_character(string before, string after)
    {
        var lines = GreedyLineBreaker.BreakLines([Unit(before), Atomic(4), Unit(after)], availableWidth: 1);

        Texts(lines).Should().Equal(before, "￼", after);
        lines[1].Width.Should().Be(4, "the advance is the atomic inline's width");
    }

    [Fact]
    public void Without_the_atomic_inline_mark_the_same_character_follows_UAX_14()
    {
        // U+FFFC as text is class CB: LB12 "GL ×" keeps it with the no-break space before it, LB13 "× CL" with the ")".
        var lines = GreedyLineBreaker.BreakLines([Unit("abc "), Unit("￼"), Unit(")")], availableWidth: 1);

        Texts(lines).Should().Equal("abc ￼)");
    }

    [Fact]
    public void Atomic_inlines_side_by_side_break_between_each_other()
    {
        var lines = GreedyLineBreaker.BreakLines([Atomic(3), Atomic(3), Atomic(3)], availableWidth: 7);

        lines.Select(line => line.Width).Should().Equal(6, 3);
    }

    [Fact]
    public void An_atomic_inline_is_no_expansion_opportunity_of_a_justified_line()
    {
        var line = GreedyLineBreaker.BreakLines([Unit("a"), Atomic(3), Unit("b c")], availableWidth: 20)[0];

        var (justified, _) = LineJustification.Place(line, 20, TextAlign.Justify, isLastLine: false);

        justified.Segments[1].Glyphs.Single().Advance.Should().Be(3);
        justified.Width.Should().Be(20, "the space widens alone");
    }
}
