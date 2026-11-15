using Ave.Documents.Core;
using Ave.Documents.Fonts;
using Ave.Documents.TestSupport;
using Ave.Documents.Typesetting;

namespace UnitTests.Ave.Documents.Typesetting;

public class GreedyLineBreakerTests
{
    /// <summary>Glyphs with an advance of exactly 1 point per character, so widths are easy to count.</summary>
    private static ValueList<ShapedGlyph> Unit(string text) =>
        [.. text.Select(character => new ShapedGlyph(new GlyphId(1), 1, character.ToString()))];

    private static IEnumerable<string> Texts(ValueList<TextLine> lines) =>
        lines.Select(line => string.Concat(line.Segments.SelectMany(segment => segment.Glyphs).Select(glyph => glyph.Text)));

    [Fact]
    public void Fills_each_line_with_as_many_words_as_fit()
    {
        var lines = GreedyLineBreaker.BreakLines([Unit("aaa bb c dddd")], availableWidth: 6);

        Texts(lines).Should().Equal("aaa bb", "c dddd");
        lines.Select(line => line.Width).Should().Equal(6, 6);
    }

    [Fact]
    public void Spaces_at_the_start_and_end_of_lines_are_removed()
    {
        var lines = GreedyLineBreaker.BreakLines([Unit(" ab cd ")], availableWidth: 3);

        Texts(lines).Should().Equal("ab", "cd");
    }

    [Fact]
    public void A_word_wider_than_the_line_gets_its_own_line_and_overflows()
    {
        var lines = GreedyLineBreaker.BreakLines([Unit("abcdefg hi")], availableWidth: 3);

        Texts(lines).Should().Equal("abcdefg", "hi");
        lines[0].Width.Should().Be(7);
    }

    [Fact]
    public void A_word_can_span_runs_and_lines_keep_one_segment_per_run_stretch()
    {
        var lines = GreedyLineBreaker.BreakLines([Unit("ab"), Unit("cd ef")], availableWidth: 4);

        Texts(lines).Should().Equal("abcd", "ef");
        lines[0].Segments.Select(segment => segment.Run).Should().Equal(0, 1);
        lines[1].Segments.Select(segment => segment.Run).Should().Equal(1);
    }

    [Fact]
    public void No_visible_text_gives_no_lines() =>
        GreedyLineBreaker.BreakLines([Unit("   "), Unit("")], availableWidth: 10).Should().BeEmpty();

    /// <summary>
    /// Hand-computed from Lato Regular's hmtx (2000 units/em) at 10pt: a = 994, n = 1116, space = 512 units.
    /// "an" = 2110 units = 10.55pt; "an an" = 10.55 + 2.56 + 10.55 = 23.66pt.
    /// </summary>
    [Theory]
    [InlineData(23.66, 23.66, new[] { "an an", "an" })]
    [InlineData(23.65, 10.55, new[] { "an", "an", "an" })]
    [InlineData(36.77, 36.77, new[] { "an an an" })]
    public void Breaks_real_text_where_hand_computed_widths_say(double width, double firstLineWidth, string[] expected)
    {
        var glyphs = Shaper.Shape("an an an", TestFonts.Regular, fontSize: 10).Value;

        var lines = GreedyLineBreaker.BreakLines([glyphs], width);

        Texts(lines).Should().Equal(expected);
        lines[0].Width.Should().BeApproximately(firstLineWidth, 1e-9);
    }
}
