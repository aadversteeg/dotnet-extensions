using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Fonts;
using Ave.Extensions.Documents.Typesetting;
using Ave.Extensions.Functional;

namespace UnitTests.Extensions.Documents.Typesetting;

/// <summary>Text that does not wrap (<c>text-wrap-mode: nowrap</c>, CSS Text 4 §5.1): no soft wrap opportunity is taken after
/// a character of such a run; forced breaks still break.</summary>
public class NoWrapBreakTests
{
    /// <summary>Glyphs with an advance of exactly 1 point per character (U+2028 a forced break of no width).</summary>
    private static ValueList<ShapedGlyph> Unit(string text) =>
        [.. text.Select(character => character == '\u2028'
            ? new ShapedGlyph(new GlyphId(0), 0, character.ToString()) { IsLineBreak = true, IsInvisible = true }
            : new ShapedGlyph(new GlyphId(1), 1, character.ToString()))];

    private static readonly RunWhiteSpace Wrapping = RunWhiteSpace.Initial;
    private static readonly RunWhiteSpace NotWrapping = new(WhiteSpaceCollapse.Collapse, TextWrapMode.NoWrap);

    private static IEnumerable<string> Lines(double width, params (string Text, RunWhiteSpace Mode)[] runs) =>
        GreedyLineBreaker.BreakLines([.. runs.Select(run => Unit(run.Text))], [.. runs.Select(run => run.Mode)], width,
                (index, piece, _, _) => Result<ValueList<ShapedGlyph>, Ave.Extensions.ErrorPaths.Error>.Success(Unit(piece)), LineEndShaping.KeepParagraphGlyphs).Value
            .Select(line => string.Concat(line.Segments.SelectMany(segment => segment.Glyphs).Select(glyph => glyph.Text)));

    [Fact]
    public void Nowrap_text_stays_on_one_line_and_overflows() =>
        Lines(3, ("aa bb cc", NotWrapping)).Should().Equal("aa bb cc");

    [Fact]
    public void A_forced_break_still_breaks_nowrap_text() =>
        Lines(3, ("aa bb\u2028cc dd", NotWrapping)).Should().Equal("aa bb\u2028", "cc dd");

    [Fact]
    public void The_opportunity_after_a_space_follows_the_space_s_run()
    {
        Lines(1, ("aa", Wrapping), (" bb", NotWrapping)).Should().Equal("aa bb");
        Lines(1, ("aa", NotWrapping), (" bb", Wrapping)).Should().Equal("aa", "bb");
        Lines(1, ("aa ", NotWrapping), ("bb", Wrapping)).Should().Equal("aa bb");
        Lines(1, ("aa ", Wrapping), ("bb", NotWrapping)).Should().Equal("aa", "bb");
    }

    [Fact]
    public void The_opportunity_after_a_hyphen_follows_the_hyphen_s_run()
    {
        Lines(1, ("aa-", Wrapping), ("bb", NotWrapping)).Should().Equal("aa-", "bb");
        Lines(1, ("aa-", NotWrapping), ("bb", Wrapping)).Should().Equal("aa-bb");
    }

    [Fact]
    public void An_atomic_inline_breaks_by_the_mode_it_is_given()
    {
        static IEnumerable<string> WithImage(RunWhiteSpace mode) =>
            GreedyLineBreaker.BreakLines([Unit("ab"), [ShapedGlyph.AtomicInline(5)], Unit("cd")], [mode, mode, mode], 1,
                    (index, piece, _, _) => Result<ValueList<ShapedGlyph>, Ave.Extensions.ErrorPaths.Error>.Success(Unit(piece)), LineEndShaping.KeepParagraphGlyphs).Value
                .Select(line => string.Concat(line.Segments.SelectMany(segment => segment.Glyphs).Select(glyph => glyph.Text)));

        WithImage(NotWrapping).Should().HaveCount(1);
        WithImage(Wrapping).Should().HaveCount(3);
    }
}
