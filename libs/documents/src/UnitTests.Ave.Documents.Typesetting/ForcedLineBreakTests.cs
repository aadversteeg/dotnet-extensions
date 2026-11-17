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

    /// <summary>The test data with "|" as the line separator, "~" as the no-break space U+00A0, and "{SHY}", "{ZWSP}",
    /// "{WJ}" and "{ZWNJ}" as the invisible characters soft hyphen U+00AD, zero width space U+200B, word joiner U+2060
    /// and zero width non-joiner U+200C.</summary>
    private static string WithBreaks(string text) => text.Replace("|", Break, StringComparison.Ordinal).Replace("~", "\u00A0", StringComparison.Ordinal)
        .Replace("{SHY}", "\u00AD", StringComparison.Ordinal).Replace("{ZWSP}", "\u200B", StringComparison.Ordinal).Replace("{WJ}", "\u2060", StringComparison.Ordinal)
        .Replace("{ZWNJ}", "\u200C", StringComparison.Ordinal);

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

    [Theory]
    [InlineData("xx ab |cd")]
    [InlineData("xx ab  |cd")]
    [InlineData("xx ab {SHY}|cd")]
    [InlineData("xx ab {ZWSP}|cd")]
    [InlineData("xx ab {WJ}|cd")]
    [InlineData("xx ab {SHY}{ZWSP} |cd")]
    public void The_space_before_a_forced_break_does_not_count_in_the_fit_test(string text)
    {
        // The line is as wide as "xx ab" and half a space: "xx ab" fits and the space before the break is removed, so
        // the line holds "xx ab" and the break; counting the space would wrap "ab" onto a line of its own.
        var width = Width(Shape("xx ab")) + Width(Shape(" ")) / 2;

        var lines = GreedyLineBreaker.BreakLines([[.. Shape(WithBreaks(text))]], availableWidth: width);

        lines.Should().HaveCount(2);
        lines[0].Width.Should().BeApproximately(Width(Shape("xx ab")), 1e-9);
        lines[0].EndsWithForcedBreak.Should().BeTrue();
    }

    /// <summary>
    /// Spot check of fix round 3, SC-5. Invisible characters (soft hyphen, zero width space, word joiner) between the
    /// spaces and a forced break, or after the last spaces of a paragraph, change nothing: the lines are the same as
    /// without them, as wide and as many, and no line holds only invisible characters. At a line end the spaces and the
    /// invisible characters among them are removed together; the invisible characters stay on the line as text, without
    /// width.
    /// </summary>
    [Theory]
    [InlineData("xx ab |cd")]
    [InlineData("xx ab  |cd")]
    [InlineData("xx ab ")]
    [InlineData("xx ab |")]
    [InlineData("a | |b")]
    [InlineData("xx ab cd |ef")]
    [InlineData("a | ")]
    [InlineData("wwwwwwwwwwww |cd")]
    [InlineData("wwwwwwwwwwww  |cd")]
    public void Invisible_characters_before_a_forced_break_or_the_paragraph_end_change_no_line(string text)
    {
        // "wwwwwwwwwwww" is wider than the line (review 35, SC-5 nit): it overflows a line of its own either way, and the
        // invisible character after its space must not end that line and make one of its own before the forced break.
        var width = Width(Shape("xx ab")) + Width(Shape(" ")) / 2;
        var plain = GreedyLineBreaker.BreakLines([[.. Shape(WithBreaks(text))]], availableWidth: width);
        foreach (var invisible in new[] { "{SHY}", "{ZWSP}", "{WJ}", "{ZWNJ}", "{SHY}{WJ}" })
        {
            // Before every forced break, and at the end when the text ends in a space.
            var marked = text.Replace(" |", " " + invisible + "|", StringComparison.Ordinal);
            marked = marked.EndsWith(' ') ? marked + invisible : marked;

            var lines = GreedyLineBreaker.BreakLines([[.. Shape(WithBreaks(marked))]], availableWidth: width);

            var because = $"'{marked}' against '{text}'";
            lines.Select(line => (line.Width, line.EndsWithForcedBreak)).Should().Equal(plain.Select(line => (line.Width, line.EndsWithForcedBreak)), because);
            lines.Select(Visible).Should().Equal(plain.Select(Visible), because + ": the same text on each line");
        }
    }

    /// <summary>
    /// Review 35, SC-5 nit: invisible characters before the spaces at the start of a line change nothing either. The
    /// spaces at a line start are removed (CSS Text 3 §4.1.2) also when an invisible character stands before or among
    /// them; the invisible characters stay as text, without width. Lines start with spaces at the paragraph start,
    /// after a forced break, and where a line wraps before a zero width space (UAX #14 LB8 keeps the spaces after it
    /// with it).
    /// </summary>
    [Theory]
    [InlineData(" b", "{ZWSP} b")]
    [InlineData(" b", "{WJ}  b")]
    [InlineData(" b", "{SHY} {ZWSP} b")]
    [InlineData("a| b", "a|{ZWSP} b")]
    [InlineData("xx ab cd", "xx ab{ZWSP} cd")]
    [InlineData("xx ab cd", "xx ab {ZWSP} cd")]
    public void Invisible_characters_before_the_spaces_at_a_line_start_change_no_line(string plainText, string text)
    {
        var width = Width(Shape("xx ab")) + Width(Shape(" ")) / 2;
        var plain = GreedyLineBreaker.BreakLines([[.. Shape(WithBreaks(plainText))]], availableWidth: width);

        var lines = GreedyLineBreaker.BreakLines([[.. Shape(WithBreaks(text))]], availableWidth: width);

        lines.Select(line => (line.Width, line.EndsWithForcedBreak)).Should().Equal(plain.Select(line => (line.Width, line.EndsWithForcedBreak)), text);
        lines.Select(Visible).Should().Equal(plain.Select(Visible), text + ": the same text on each line");
        lines.SelectMany(line => line.Segments).SelectMany(segment => segment.Glyphs).Count(glyph => glyph.IsInvisible && !glyph.IsLineBreak)
            .Should().Be(Shape(WithBreaks(text)).Count(glyph => glyph.IsInvisible && !glyph.IsLineBreak), text + ": the invisible characters stay as text");
    }

    /// <summary>
    /// Mutant R5-15 of review 35: a piece of soft hyphens after a space ("xx ab", a space, two soft hyphens, "cd ef")
    /// shows a hyphen where the line breaks after it, so it goes on the line only when it fits with that hyphen; where
    /// it does not, the line ends after "xx ab". With room for the space and the hyphen it stays, and the line shows
    /// "xx ab" and the hyphen (the spaces before a shown soft hyphen stay).
    /// </summary>
    [Fact]
    public void Soft_hyphens_after_a_space_stay_on_the_line_only_when_their_hyphen_fits()
    {
        var (word, space, hyphen) = (Width(Shape("xx ab")), Width(Shape(" ")), Width(Shape("-")));
        var text = WithBreaks("xx ab {SHY}{SHY}cd ef");
        foreach (var extra in new[] { 0, hyphen / 2, hyphen + 0.01 })
        {
            var lines = GreedyLineBreaker.BreakLines([[.. Shape(text)]], availableWidth: word + extra);

            lines.Select(Visible).Should().Equal(["xx ab", "cd ef"], $"extra {extra}");
            lines.Should().OnlyContain(line => line.Width <= word + extra + 1e-9, $"extra {extra}: no line wider than the line box");
        }

        var roomy = GreedyLineBreaker.BreakLines([[.. Shape(text)]], availableWidth: word + space + hyphen + 0.01);
        roomy.Select(Visible).Should().Equal(["xx ab \u00AD", "cd ef"], "with room for the space and the hyphen the soft hyphens stay");
        roomy[0].Segments[^1].Glyphs[^1].IsInvisible.Should().BeFalse("the last one shows as a hyphen (which keeps the soft hyphen's text)");
    }

    /// <summary>The text of a line without its invisible characters and spaces at either end.</summary>
    private static string Visible(TextLine line) =>
        string.Concat(line.Segments.SelectMany(segment => segment.Glyphs).Where(glyph => !glyph.IsInvisible).Select(glyph => glyph.Text)).Trim();

    [Theory]
    [InlineData(0x000B)]
    [InlineData(0x000C)]
    [InlineData(0x0085)]
    [InlineData(0x2028)]
    [InlineData(0x2029)]
    public void Every_mandatory_break_that_white_space_collapsing_keeps_forces_a_line_break(int codePoint)
    {
        // U+000B LINE TABULATION, U+000C FORM FEED (UAX #14 class BK), U+0085 NEXT LINE (NL), U+2028 and U+2029 (BK)
        // are not collapsible white space, so they reach line breaking and end the line. (Line feed and carriage
        // return collapse to a space first.)
        var text = WhiteSpaceCollapsing.Collapse(["ab" + char.ConvertFromUtf32(codePoint) + "cd"])[0];

        GreedyLineBreaker.BreakLines([[.. Shape(text)]], availableWidth: 1000).Should().HaveCount(2);
    }

    [Theory]
    [InlineData(0x000A)]
    [InlineData(0x000D)]
    public void A_line_feed_or_carriage_return_collapses_to_a_space_and_forces_nothing(int codePoint)
    {
        var text = WhiteSpaceCollapsing.Collapse(["ab" + char.ConvertFromUtf32(codePoint) + "cd"])[0];

        text.Should().Be("ab cd");
        GreedyLineBreaker.BreakLines([[.. Shape(text)]], availableWidth: 1000).Should().ContainSingle();
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
        var mark = Shape("a" + Break + "\u0301").Last();

        mark.XOffset.Should().Be(Shape("\u0301").Single().XOffset);
        mark.YOffset.Should().Be(Shape("\u0301").Single().YOffset);
    }
}
