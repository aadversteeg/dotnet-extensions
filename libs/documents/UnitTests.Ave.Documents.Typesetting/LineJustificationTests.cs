using Ave.Documents.Core;
using Ave.Documents.Fonts;
using Ave.Documents.Typesetting;

namespace UnitTests.Ave.Documents.Typesetting;

/// <summary>text-align and inter-word justification (CSS Text 3 §6.1, §6.4) on lines of 1-point-wide glyphs.</summary>
public class LineJustificationTests
{
    private static TextLine Line(string text, bool forcedBreak = false) =>
        new([new LineSegment(0, [.. text.Select(character => new ShapedGlyph(new GlyphId(1), 1, character.ToString()))])], forcedBreak);

    [Theory]
    [InlineData(TextAlign.Left, 0)]
    [InlineData(TextAlign.Right, 6)]
    [InlineData(TextAlign.Center, 3)]
    public void Left_right_and_centre_move_the_line_without_changing_it(TextAlign align, double offset)
    {
        var line = Line("ab cd");

        var (placed, at) = LineJustification.Place(line, width: 11, align, isLastLine: false);

        at.Should().Be(offset);
        placed.Should().Be(line);
    }

    [Fact]
    public void Justify_shares_the_free_space_equally_among_the_word_separators()
    {
        var (placed, at) = LineJustification.Place(Line($"a b{char.ConvertFromUtf32(0x00A0)}c"), width: 11, TextAlign.Justify, isLastLine: false);

        at.Should().Be(0);
        placed.Width.Should().Be(11);
        placed.Segments[0].Glyphs.Select(glyph => glyph.Advance).Should().Equal(new double[] { 1, 4, 1, 4, 1 }, "6 points of free space over a space and a no-break space");
    }

    [Fact]
    public void The_last_line_and_a_line_before_a_forced_break_are_not_justified()
    {
        LineJustification.Place(Line("a b"), 10, TextAlign.Justify, isLastLine: true).Line.Width.Should().Be(3);
        LineJustification.Place(Line("a b", forcedBreak: true), 10, TextAlign.Justify, isLastLine: false).Line.Width.Should().Be(3);
    }

    [Fact]
    public void A_line_without_word_separators_cannot_be_justified_inter_word()
    {
        LineJustification.Place(Line("abc"), 10, TextAlign.Justify, isLastLine: false).Should().Be((Line("abc"), 0.0));
    }

    [Fact]
    public void A_line_wider_than_its_box_is_start_aligned()
    {
        LineJustification.Place(Line("abcdef"), 4, TextAlign.Right, isLastLine: false).Offset.Should().Be(0);
    }
}
