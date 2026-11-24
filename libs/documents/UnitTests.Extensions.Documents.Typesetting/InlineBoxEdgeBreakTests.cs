using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Fonts;
using Ave.Extensions.Documents.Typesetting;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace UnitTests.Extensions.Documents.Typesetting;

/// <summary>
/// The stand-in of an inline box's edge in the line breaker (<see cref="ShapedGlyph.BoxEdge"/>, THE RULE FOR AN INLINE BOX'S
/// EDGES IN A LINE): a glyph without text whose advance is the edge's room; the start edge stays with the box's first
/// character and the end edge with its last, an edge is no break opportunity, no space and no expansion opportunity.
/// </summary>
public class InlineBoxEdgeBreakTests
{
    private static ValueList<ShapedGlyph> Unit(string text) =>
        [.. text.Select(character => new ShapedGlyph(new GlyphId(1), 1, character.ToString()))];

    private static ValueList<ShapedGlyph> Start(double advance) => [ShapedGlyph.BoxEdge(advance, InlineBoxEdge.Start)];

    private static ValueList<ShapedGlyph> End(double advance) => [ShapedGlyph.BoxEdge(advance, InlineBoxEdge.End)];

    /// <summary>Each line's glyphs as text, a start edge as "[" and an end edge as "]".</summary>
    private static IEnumerable<string> Texts(ValueList<TextLine> lines) =>
        lines.Select(line => string.Concat(line.Segments.SelectMany(segment => segment.Glyphs).Select(glyph => glyph.Edge switch
        {
            InlineBoxEdge.Start => "[",
            InlineBoxEdge.End => "]",
            _ => glyph.Text,
        })));

    [Fact]
    public void The_start_edge_goes_to_the_next_line_with_the_box_s_first_character()
    {
        // "aa " and the box "bb" with 3 of room on each side: 2 + 3 + 2 + 3 = 10 do not fit in 9.
        var lines = GreedyLineBreaker.BreakLines([Unit("aa "), Start(3), Unit("bb"), End(3)], availableWidth: 9);

        Texts(lines).Should().Equal("aa", "[bb]");
        lines[1].Width.Should().Be(8);
    }

    [Fact]
    public void The_end_edge_stays_on_the_line_with_the_box_s_last_character_and_the_space_after_it_goes()
    {
        var lines = GreedyLineBreaker.BreakLines([Start(1), Unit("aa"), End(3), Unit(" bb")], availableWidth: 7);

        Texts(lines).Should().Equal("[aa]", "bb");
        lines[0].Width.Should().Be(6, "the end edge counts, the space removed at the line's end does not");
    }

    [Fact]
    public void An_edge_is_no_break_opportunity()
    {
        // "aa" + edge + "bb" has no opportunity of UAX #14 inside it: one line that overflows.
        var lines = GreedyLineBreaker.BreakLines([Unit("aa"), Start(3), Unit("bb"), End(3), Unit("cc")], availableWidth: 4);

        Texts(lines).Should().Equal("aa[bb]cc");
    }

    [Fact]
    public void An_empty_box_stays_where_it_stands_and_keeps_its_room()
    {
        var lines = GreedyLineBreaker.BreakLines([Unit("aa "), Start(4), End(4), Unit("bb")], availableWidth: 100);

        Texts(lines).Should().Equal("aa []bb");
        lines[0].Width.Should().Be(13);
    }

    [Fact]
    public void An_empty_box_at_a_break_stays_on_the_line_before_with_the_space_before_it_removed()
    {
        var lines = GreedyLineBreaker.BreakLines([Unit("aa "), Start(4), End(4), Unit("bb")], availableWidth: 10);

        Texts(lines).Should().Equal("aa[]", "bb");
    }

    [Fact]
    public void A_box_whose_first_character_is_a_space_keeps_its_start_edge_with_that_space_at_a_break_after_it()
    {
        // The break opportunity lies after the box's space: its start edge stays on the line before, the space goes, and
        // the box lies on two lines (which layout refuses in this version).
        var lines = GreedyLineBreaker.BreakLines([Unit("aaaa"), Start(2), Unit(" bb"), End(2)], availableWidth: 6);

        Texts(lines).Should().Equal("aaaa[", "bb]");
    }

    [Fact]
    public void An_edge_is_no_expansion_opportunity_of_a_justified_line()
    {
        var line = GreedyLineBreaker.BreakLines([Unit("a "), Start(3), Unit("b"), End(3), Unit(" c")], availableWidth: 20)[0];

        var (justified, _) = LineJustification.Place(line, 20, TextAlign.Justify, isLastLine: false);

        justified.Segments.Where(segment => segment.Glyphs.Any(glyph => glyph.Edge != InlineBoxEdge.None))
            .Select(segment => segment.Glyphs.Single().Advance).Should().Equal(3, 3);
        justified.Width.Should().Be(20, "the two spaces widen alone");
    }

    [Fact]
    public void An_edge_takes_its_room_beside_text_items_rounded_to_their_step()
    {
        var lines = GreedyLineBreaker.BreakLines(
            [Unit("aa"), Start(0.5), Unit("b"), End(0.5)], [], 100, (_, text, _, _) => Result<ValueList<ShapedGlyph>, Error>.Success(Unit(text)), [], textItemStep: 2);

        lines.Value[0].Segments.Select(segment => segment.Width).Should().Equal(2, 0.5, 2, 0.5);
    }

    [Fact]
    public void A_line_end_shaped_again_keeps_the_start_edge_of_a_box_that_the_reshaped_stretch_begins_with()
    {
        // The box's "b" is marked unsafe to break, so a line end after it is shaped again from the run's start: the start
        // edge, which no text carries, must stay on the line.
        ValueList<ShapedGlyph> unsafeText = [new ShapedGlyph(new GlyphId(1), 1, "b") { UnsafeToBreak = true }, new ShapedGlyph(new GlyphId(1), 1, "b") { UnsafeToBreak = true }];
        ValueList<ShapedGlyph> after = [new ShapedGlyph(new GlyphId(1), 1, " ") { UnsafeToBreak = true }, .. Unit("cc")];
        var lines = GreedyLineBreaker.BreakLines(
            [Unit("aa "), Start(2), unsafeText, after], [], 7, (_, text, _, _) => Result<ValueList<ShapedGlyph>, Error>.Success(Unit(text)), [LineEndShaping.ShapeAgainBeforeSpace, LineEndShaping.ShapeAgainBeforeSpace, LineEndShaping.ShapeAgainBeforeSpace, LineEndShaping.ShapeAgainBeforeSpace]);

        Texts(lines.Value).Should().Equal("aa [bb", "cc");
    }
}
