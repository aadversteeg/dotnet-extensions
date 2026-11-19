using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Fonts;
using Ave.Extensions.Documents.Typesetting;
using Ave.Extensions.Functional;

namespace UnitTests.Extensions.Documents.Typesetting;

/// <summary>Preserved spaces at a line's ends (CSS Text 3 §4.1.2): kept at the start, hanging at the end, whole at a soft wrap
/// and only as far as they do not fit before a forced break or at the paragraph's end.</summary>
public class PreservedSpaceBreakTests
{
    /// <summary>Glyphs with an advance of exactly 1 point per character (U+2028 a forced break of no width).</summary>
    private static ValueList<ShapedGlyph> Unit(string text) =>
        [.. text.Select(character => character == '\u2028'
            ? new ShapedGlyph(new GlyphId(0), 0, character.ToString()) { IsLineBreak = true, IsInvisible = true }
            : new ShapedGlyph(new GlyphId(1), 1, character.ToString()))];

    private static readonly RunWhiteSpace Preserved = new(WhiteSpaceCollapse.Preserve);

    private static ValueList<TextLine> Break(string text, double width, RunWhiteSpace? mode = null) =>
        GreedyLineBreaker.BreakLines([Unit(text)], [mode ?? Preserved], width, (_, piece, _, _) => Result<ValueList<ShapedGlyph>, Ave.Extensions.ErrorPaths.Error>.Success(Unit(piece)), LineEndShaping.KeepParagraphGlyphs).Value;

    private static IEnumerable<string> Texts(ValueList<TextLine> lines) =>
        lines.Select(line => string.Concat(line.Segments.SelectMany(segment => segment.Glyphs).Select(glyph => glyph.Text)));

    [Fact]
    public void Preserved_spaces_hang_whole_at_a_soft_wrap_and_are_not_measured_for_fit()
    {
        var lines = Break("ab    cd", 3);

        Texts(lines).Should().Equal("ab    ", "cd");
        lines[0].Width.Should().Be(2, "the four spaces hang past the line's end");
        lines[0].Segments[0].Glyphs.Skip(2).Should().OnlyContain(glyph => glyph.Hangs && glyph.Advance == 0);
    }

    [Fact]
    public void Collapsible_spaces_at_a_soft_wrap_are_removed_as_before() =>
        Texts(Break("ab    cd", 3, RunWhiteSpace.Initial)).Should().Equal("ab", "cd");

    [Fact]
    public void Preserved_spaces_at_the_paragraph_end_keep_the_room_that_is_left()
    {
        Break("ab    ", 10)[0].Width.Should().Be(6, "all four spaces fit");
        Break("ab    ", 4.5)[0].Width.Should().Be(4.5, "the spaces keep the 2.5 points that are left and hang past them");
        Break("ab    ", 0)[0].Width.Should().Be(2, "with no room they hang whole");
    }

    [Fact]
    public void Preserved_spaces_before_a_forced_break_keep_the_room_that_is_left_and_do_not_break_the_line_earlier()
    {
        var lines = Break("xy ab    \u2028cd", 6);

        Texts(lines).Should().Equal("xy ab    \u2028", "cd");
        lines[0].Width.Should().Be(6, "\"xy ab\" fits, its spaces keep the one point left");
    }

    [Fact]
    public void Preserved_spaces_at_the_start_of_a_line_stay_and_alone_before_a_soft_wrap_make_a_line()
    {
        Texts(Break("  ab", 10)).Should().Equal("  ab");
        Break("  ab", 10)[0].Width.Should().Be(4);
        Texts(Break("   ab", 1)).Should().Equal("   ", "ab");
    }

    [Fact]
    public void A_hanging_space_does_not_widen_in_a_justified_line_but_a_preserved_space_inside_it_does()
    {
        var line = Break("a  b c    dd", 7)[0];
        var (placed, _) = LineJustification.Place(line, 7, TextAlign.Justify, isLastLine: false);

        var glyphs = placed.Segments.SelectMany(segment => segment.Glyphs).ToList();
        glyphs.Take(6).Where(glyph => glyph.IsSpace).Should().HaveCount(3).And.OnlyContain(glyph => glyph.Advance == 4.0 / 3, "the free 2 points go to the three spaces inside the line");
        glyphs.Skip(6).Should().OnlyContain(glyph => glyph.Hangs && glyph.Advance == 0, "none go to the spaces that hang");
    }

    private static readonly RunWhiteSpace BreakingSpaces = new(WhiteSpaceCollapse.BreakSpaces);

    [Fact]
    public void Spaces_of_break_spaces_take_their_room_and_a_line_breaks_between_them()
    {
        var lines = Break("ab    cd", 4, BreakingSpaces);

        Texts(lines).Should().Equal("ab  ", "  cd");
        lines.Select(line => line.Width).Should().Equal(4, 4);
        lines.SelectMany(line => line.Segments).SelectMany(segment => segment.Glyphs).Should().OnlyContain(glyph => !glyph.Hangs);
    }

    [Fact]
    public void A_space_that_ends_a_justified_line_gets_no_share()
    {
        var line = Break("a b  cd", 5, BreakingSpaces)[0];
        Texts([line]).Should().Equal("a b  ");

        var (placed, _) = LineJustification.Place(line, 6, TextAlign.Justify, isLastLine: false);

        var glyphs = placed.Segments.SelectMany(segment => segment.Glyphs).ToList();
        glyphs.Select(glyph => glyph.Advance).Should().Equal(1, 1.5, 1, 1.5, 1);
    }
}
