using System.Text;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.Documents.Typesetting;

namespace UnitTests.Extensions.Documents.Typesetting;

/// <summary>
/// The ends of a line as the layout breaks it (the reshaper passes the paragraph's budget on, as the layout's does), in
/// Lato Regular: the hyphen at a soft hyphen in each place it can be drawn from, a mark that starts a line, and a line
/// that ends inside a ligature. Font units: the font size is the units per em.
/// </summary>
public class LineEndTests
{
    private static readonly double Size = TestFonts.Regular.Metrics.UnitsPerEm;

    private static ValueList<ShapedGlyph> Run(string text) => [.. Shaper.Shape(text, TestFonts.Regular, Size).Value];

    private static ValueList<TextLine> Break(IReadOnlyList<ValueList<ShapedGlyph>> runs, double width) =>
        GreedyLineBreaker.BreakLines(runs, width, (_, text, showsHyphen, budget) => Shaper.ShapeLinePiece(text, TestFonts.Regular, Size, showsHyphen, budget)).Value;

    private static List<ShapedGlyph> Glyphs(TextLine line) => [.. line.Segments.SelectMany(segment => segment.Glyphs)];

    private static ushort GlyphOf(int codePoint) => TestFonts.Regular.GlyphFor(new Rune(codePoint)).Value.Value;

    /// <summary>The hyphen shaped alone: U+2010's glyph with its own advance.</summary>
    private static double HyphenAdvance => Run("\u2010").Single().Advance;

    [Fact]
    public void A_soft_hyphen_that_ends_a_text_run_shows_the_hyphen_where_the_line_breaks()
    {
        // "of" + U+00AD in one run, "fice" in the next: the soft hyphen ends its run, a safe place, so nothing is shaped
        // again there; the hyphen still shows.
        var lines = Break([Run("of\u00AD"), Run("fice")], width: 0);

        var first = Glyphs(lines[0]);
        first[^1].Glyph.Value.Should().Be(GlyphOf(0x2010), "the line breaks at the soft hyphen, which shows as a hyphen");
        first[^1].IsInvisible.Should().BeFalse();
        first[^1].Text.Should().Be("\u00AD", "the hyphen stands for the soft hyphen in the text");
        lines[0].Width.Should().BeApproximately(Run("of").Sum(glyph => glyph.Advance) + HyphenAdvance, 1e-9, "the hyphen is shaped alone");
    }

    [Fact]
    public void A_soft_hyphen_inside_a_ligature_shows_the_hyphen_after_the_half_shaped_again()
    {
        // "of\u00ADfice" in one run: the ffi ligature skips the soft hyphen, so the break falls inside its cluster; the end of
        // the line is shaped again ("f" + U+00AD, the path through Shaper.ShapeLinePiece) and shows the hyphen.
        var paragraph = Run("of\u00ADfice");
        paragraph.Should().Contain(glyph => glyph.Text == "f\u00ADfi", "the ligature is formed across the soft hyphen");

        var first = Glyphs(Break([paragraph], width: 0)[0]);

        first.Where(glyph => !glyph.IsInvisible).Select(glyph => (int)glyph.Glyph.Value).Should().Equal(GlyphOf('o'), GlyphOf('f'), GlyphOf(0x2010));
        first[^1].Text.Should().Be("\u00AD");
        first.Sum(glyph => glyph.Advance).Should().BeApproximately(Run("of").Sum(glyph => glyph.Advance) + HyphenAdvance, 1e-9);
    }

    [Fact]
    public void The_hyphen_is_not_kerned_with_the_letter_before_it()
    {
        // "Ver" + U+00AD + "lag": r and U+2010 kern in Lato (-41 units shaped together); Chromium shapes the hyphen alone.
        Run("Ver\u2010").Sum(glyph => glyph.Advance).Should().BeLessThan(Run("Ver").Sum(glyph => glyph.Advance) + HyphenAdvance, "the font kerns r with the hyphen");

        var line = Break([Run("Ver\u00ADlag")], width: 0)[0];

        line.Width.Should().BeApproximately(Run("Ver").Sum(glyph => glyph.Advance) + HyphenAdvance, 1e-9);
    }

    [Fact]
    public void A_mark_that_starts_a_line_after_a_zero_width_space_is_shaped_without_the_base_of_the_line_before()
    {
        // "o" U+200B U+0301 "x": in the paragraph the acute sits on the o (the zero width space is skipped), so its
        // cluster is unsafe to break; broken at the zero width space, the next line starts with the mark, shaped again on
        // its own: no base, no offset (HarfBuzz 15.0.0 on U+0301 "x": the mark at 0).
        var paragraph = Run("o\u200B\u0301x");
        paragraph[2].XOffset.Should().NotBe(0, "in the paragraph the mark is attached to the o");

        var lines = Break([paragraph], width: 0);

        lines.Should().HaveCount(2);
        var mark = Glyphs(lines[1])[0];
        mark.Glyph.Value.Should().Be(GlyphOf(0x0301));
        mark.XOffset.Should().Be(0);
    }

    [Fact]
    public void A_line_that_ends_inside_a_ligature_shapes_only_its_own_half_again()
    {
        // "ff" U+200B "fi": the ffi ligature forms from the second f across the zero width space ("f" + ligature). Broken
        // at the zero width space, line 1 is the paragraph's first f and the second f shaped again on its own: no ff
        // ligature, as Chromium prints it (1 402 units, f + f), although HarfBuzz gives "ff" alone the ligature.
        var paragraph = Run("ff\u200Bfi");
        paragraph.Select(glyph => glyph.Text).Should().StartWith(["f", "f\u200Bfi"]);
        Run("ff").Should().ContainSingle("the font has an ff ligature");

        var lines = Break([paragraph], width: 0);

        Glyphs(lines[0]).Where(glyph => !glyph.IsInvisible).Select(glyph => (int)glyph.Glyph.Value).Should().Equal(GlyphOf('f'), GlyphOf('f'));
        Glyphs(lines[1]).Where(glyph => !glyph.IsInvisible).Should().ContainSingle("the second line is the fi ligature");
    }
}
