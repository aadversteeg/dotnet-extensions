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

    [Fact]
    public void A_line_may_break_after_a_hyphen_as_unicode_line_breaking_allows()
    {
        // UAX #14 LB21 "× HY" forbids a break before the hyphen; LB31 allows one after it.
        Texts(GreedyLineBreaker.BreakLines([Unit("self-contained")], availableWidth: 8)).Should().Equal("self-", "contained");
    }

    private static readonly string NoBreakSpace = char.ConvertFromUtf32(0x00A0);
    private static readonly string ZeroWidthSpace = char.ConvertFromUtf32(0x200B);
    private static readonly string LineSeparator = char.ConvertFromUtf32(0x2028);

    [Fact]
    public void A_no_break_space_glues_its_neighbours()
    {
        // U+00A0 has class GL: LB12 "GL ×" and LB12a "[^SP HY HH] × GL".
        Texts(GreedyLineBreaker.BreakLines([Unit($"a 10{NoBreakSpace}000")], availableWidth: 4)).Should().Equal("a", $"10{NoBreakSpace}000");
    }

    [Fact]
    public void A_zero_width_space_is_a_break_opportunity()
    {
        // U+200B has class ZW: LB8 "ZW SP* ÷".
        Texts(GreedyLineBreaker.BreakLines([Unit($"abc{ZeroWidthSpace}def")], availableWidth: 4)).Should().Equal($"abc{ZeroWidthSpace}", "def");
    }

    [Fact]
    public void A_line_separator_forces_a_break()
    {
        // U+2028 has class BK: LB4 "BK !".
        Texts(GreedyLineBreaker.BreakLines([Unit($"ab{LineSeparator}cd")], availableWidth: 100)).Should().Equal($"ab{LineSeparator}", "cd");
    }

    [Fact]
    public void An_opportunity_inside_a_ligature_cluster_is_not_used()
    {
        // One glyph shows "-b" (a cluster): the opportunity after "-" lies inside it.
        ValueList<ShapedGlyph> glyphs = [new(new GlyphId(1), 1, "a"), new(new GlyphId(2), 1, "-b"), new(new GlyphId(1), 1, "c")];

        Texts(GreedyLineBreaker.BreakLines([glyphs], availableWidth: 1)).Should().Equal("a-bc");
    }

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

    [Fact]
    public void The_glyphs_of_one_character_are_never_split_over_two_lines()
    {
        // The sub-review's probe T3: a one-to-many substitution gives the later glyphs of a character an empty text.
        // "xä—y" may break before the em dash; the two glyphs of the decomposed ä must stay on one line.
        ValueList<ShapedGlyph> glyphs =
        [
            new(new GlyphId(1), 1, "x"), new(new GlyphId(2), 1, "ä"), new(new GlyphId(3), 1, string.Empty), new(new GlyphId(4), 1, "—"), new(new GlyphId(5), 1, "y"),
        ];

        var lines = GreedyLineBreaker.BreakLines([glyphs], availableWidth: 2);

        lines[0].Segments.SelectMany(segment => segment.Glyphs).Select(glyph => glyph.Glyph.Value).Should().Equal(1, 2, 3);
    }
}
