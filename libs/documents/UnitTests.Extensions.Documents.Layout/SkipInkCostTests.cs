using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.Documents.Typesetting;
using Xunit.Abstractions;

namespace UnitTests.Extensions.Documents.Layout;

/// <summary>
/// Skipping ink is linear in the glyphs of a decorated piece: per underline or overline one pass over each glyph's outline, each
/// outline decoded once per font and glyph (THE RULE FOR THE GAPS AROUND A GLYPH'S INK, "Bounded work"). Counted: the outline
/// segments visited and the outlines decoded, at two sizes, so a pass per gap over all gaps or a decoding per use would show.
/// </summary>
public class SkipInkCostTests(ITestOutputHelper output)
{
    private static (long Visited, int Decoded, int Pieces) Cost(int words, int decorations)
    {
        var font = TestFonts.Regular;
        var glyphs = Shaper.Shape(string.Concat(Enumerable.Repeat("gjpqy ", words)), font, 24).Value;
        var line = new TextDecoration(TextDecorationLine.Underline, Color.Black, "Lato", 24.Pt(), FontWeight.Regular);
        var resolved = Enumerable.Range(0, decorations).Select(index => new TextDecorations.Resolved(
            line with { Line = index % 2 == 0 ? TextDecorationLine.Underline : TextDecorationLine.Overline }, font)).ToList();
        var outlines = new InkOutlines();
        var drawn = TextDecorations.Of(resolved, 0, new LineSegment(0, [.. glyphs]), 30, 22, FontMetricRounding.WholePixels, new TextDecorations.Ink(font, 24, TextDecorationSkipInk.Auto, outlines));
        drawn.IsSuccess.Should().BeTrue();
        return (outlines.Visited, outlines.Decoded, drawn.Value.Before.Count);
    }

    [Fact]
    public void Skipping_ink_visits_each_outline_once_per_line_and_decodes_it_once()
    {
        var (small, large) = (Cost(500, 16), Cost(1000, 16));
        output.WriteLine($"500 words: {small}; 1000 words: {large}");

        large.Visited.Should().Be(2 * small.Visited, "one pass over each glyph's outline per decoration: exactly linear in the glyphs");
        small.Decoded.Should().Be(6, "g, j, p, q, y and the space, each once, though 16 lines and 3,000 glyphs use them");
        large.Decoded.Should().Be(6);
        Cost(500, 1).Visited.Should().Be(small.Visited / 16, "one pass per decoration");
        large.Pieces.Should().BeLessThanOrEqualTo(16 * (6 * 1000 + 1), "a line's pieces are at most one more than its gaps");
    }

    private static (long Cuts, int Rectangles) PartHeight(int words)
    {
        // Case sk-part row b: a 24 pt underline, then an 8 pt one that the 24 pt clips reach over its lower half.
        var font = TestFonts.Regular;
        var glyphs = Shaper.Shape(string.Concat(Enumerable.Repeat("gjpqy ", words)), font, 24).Value;
        TextDecorations.Resolved Underline(double size) => new(new TextDecoration(TextDecorationLine.Underline, Color.Black, "Lato", size.Pt(), FontWeight.Regular), font);
        var outlines = new InkOutlines();
        var drawn = TextDecorations.Of([Underline(24), Underline(8)], 0, new LineSegment(0, [.. glyphs]), 30, 22, FontMetricRounding.WholePixels, new TextDecorations.Ink(font, 24, TextDecorationSkipInk.Auto, outlines));
        drawn.IsSuccess.Should().BeTrue();
        return (outlines.Cuts, drawn.Value.Before.Count);
    }

    [Fact]
    public void Cutting_a_line_over_part_of_its_height_costs_work_linear_in_the_gaps()
    {
        var (small, large) = (PartHeight(500), PartHeight(1000));
        output.WriteLine($"part height: 500 words {small}; 1000 words {large}");

        small.Rectangles.Should().BeGreaterThan(2 * 500, "the 8 pt line keeps rows between the 24 pt gaps");
        large.Rectangles.Should().BeLessThanOrEqualTo(2 * (2 * 2 * 6 * 1000 + 1), "columns at most twice the gaps plus one, two lines");
        large.Cuts.Should().BeLessThanOrEqualTo(2 * small.Cuts + 64, "one pass over the gap ends: linear in the gaps, not quadratic");
        large.Cuts.Should().BeGreaterThanOrEqualTo(2 * small.Cuts - 64);
    }
}
