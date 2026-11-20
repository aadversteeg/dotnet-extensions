using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Fonts;
using Ave.Extensions.Documents.Typesetting;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace UnitTests.Extensions.Documents.Typesetting;

/// <summary>
/// The width of a text item rounded up to a step (<see cref="LineSegment.Step"/>; the line breaker's
/// <c>textItemStep</c>): Chromium's 1/64 CSS px, 0.01171875 pt, with glyphs whose advances are not on that step.
/// </summary>
public class TextItemWidthTests
{
    private const double Step = 0.75 / 64;

    private static ValueList<ShapedGlyph> Glyphs(string text, double advance) =>
        [.. text.Select(character => new ShapedGlyph(new GlyphId(1), advance, character.ToString()))];

    private static Result<ValueList<TextLine>, Error> Break(IReadOnlyList<ValueList<ShapedGlyph>> runs, double width, double step) =>
        GreedyLineBreaker.BreakLines(
            runs,
            [],
            width,
            (run, text, showsHyphen, budget) => Result<ValueList<ShapedGlyph>, Error>.Success(
                runs[run][0].IsAtomicInline ? runs[run] : Glyphs(text, runs[run][0].Advance)),
            [],
            step);

    private static IEnumerable<string> Texts(ValueList<TextLine> lines) =>
        lines.Select(line => string.Concat(line.Segments.SelectMany(segment => segment.Glyphs).Select(glyph => glyph.Text)));

    [Fact]
    public void A_segment_s_width_is_its_advances_rounded_up_to_its_step()
    {
        var glyphs = Glyphs("abc", 0.1);

        new LineSegment(0, glyphs).Width.Should().BeApproximately(0.3, 1e-12);
        // 0.3 pt is 25.6 steps: 26 steps.
        new LineSegment(0, glyphs, Step).Width.Should().Be(26 * Step);
    }

    [Fact]
    public void A_width_on_the_step_stays_there_also_with_the_last_bits_of_a_sum_above_it()
    {
        LineSegment.RoundedUp(3 * Step, Step).Should().Be(3 * Step);
        LineSegment.RoundedUp((3 * Step) + 1e-12, Step).Should().Be(3 * Step);
        LineSegment.RoundedUp((3 * Step) + 1e-7, Step).Should().Be(4 * Step);
        LineSegment.RoundedUp(0.3, 0).Should().Be(0.3);
    }

    /// <summary>
    /// Three items "ab ", "cd ", "ef" of glyphs 1.005 pt wide: 8.04 pt exact, 8.0625 pt with each item rounded up (258,
    /// 258 and 172 steps). In 8.05 pt the line holds them all only with exact widths; rounded, "ef" wraps (case
    /// <c>ta-items-fit</c> of <c>tools/reference/chromium</c>).
    /// </summary>
    [Fact]
    public void A_line_fits_by_the_sum_of_its_items_rounded_widths()
    {
        ValueList<ShapedGlyph>[] runs = [Glyphs("ab ", 1.005), Glyphs("cd ", 1.005), Glyphs("ef", 1.005)];

        Texts(Break(runs, 8.05, 0).Value).Should().Equal("ab cd ef");
        var rounded = Break(runs, 8.05, Step).Value;
        Texts(rounded).Should().Equal("ab cd", "ef");
        rounded[0].Segments.Should().OnlyContain(segment => segment.Step == Step);
        // The trailing space is not on the line: "cd" alone is rounded up, 172 steps.
        rounded[0].Width.Should().Be((258 + 172) * Step);
        Texts(Break(runs, 8.0625, Step).Value).Should().Equal("ab cd ef");
    }

    [Fact]
    public void An_atomic_inline_is_not_a_text_item_and_keeps_its_width()
    {
        ValueList<ShapedGlyph>[] runs = [Glyphs("a", 1.005), [ShapedGlyph.AtomicInline(2.005)]];

        var line = Break(runs, 100, Step).Value.Single();

        line.Segments[0].Step.Should().Be(Step);
        line.Segments[1].Step.Should().Be(0);
        line.Width.Should().Be((86 * Step) + 2.005);
    }
}
