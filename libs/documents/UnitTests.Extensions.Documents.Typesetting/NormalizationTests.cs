using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.Documents.Typesetting;

namespace UnitTests.Extensions.Documents.Typesetting;

/// <summary>
/// Unicode normalization before shaping (<c>Unicode.Normalization</c>, after HarfBuzz's normalizer): canonically
/// equivalent text shapes to the same glyphs where the font allows it, and the glyphs' texts stay the text as written.
/// The glyph-for-glyph check against HarfBuzz is the corpus line "Normalization" (<c>HarfBuzzCorpusTests</c>).
/// </summary>
public class NormalizationTests
{
    private static readonly string Cedilla = char.ConvertFromUtf32(0x0327);
    private static readonly string Circumflex = char.ConvertFromUtf32(0x0302);
    private static readonly string DotBelow = char.ConvertFromUtf32(0x0323);
    private static readonly string Overline = char.ConvertFromUtf32(0x0305);

    private static int[] Glyphs(string text, Ave.Extensions.Documents.Fonts.TrueTypeFont font) =>
        [.. Shaper.Shape(text, font, 10).Value.Select(glyph => (int)glyph.Glyph.Value)];

    [Fact]
    public void A_base_and_a_mark_shape_as_the_precomposed_glyph_the_font_has()
    {
        // ņ written as n and a combining cedilla: Lato has U+0146, so one glyph, the same as for the precomposed character.
        Glyphs("n" + Cedilla, TestFonts.Regular).Should().Equal(Glyphs("ņ", TestFonts.Regular)).And.ContainSingle();
    }

    [Fact]
    public void Marks_in_another_order_shape_alike_after_canonical_reordering()
    {
        // a, circumflex (class 230), dot below (class 220) is canonically equivalent to a, dot below, circumflex: both
        // become ậ, which Lato has.
        Glyphs("a" + Circumflex + DotBelow, TestFonts.Regular).Should().Equal(Glyphs("a" + DotBelow + Circumflex, TestFonts.Regular))
            .And.Equal(Glyphs("ậ", TestFonts.Regular));
    }

    [Theory]
    [InlineData("\u1EAD")]
    [InlineData("\u1EAD x\u0302")]
    public void A_precomposed_character_the_font_lacks_is_shaped_from_its_parts_as_harfbuzz_shapes_it(string text)
    {
        // Theano Didot has no ậ (U+1EAD) and no ạ, but a, â and the combining marks. Alone, ậ becomes a, the dot below and
        // the circumflex: HarfBuzz 15 recomposes only in a run whose text holds a mark (its "all simple" shortcut). With a
        // mark elsewhere in the run, a and the circumflex recompose to â, and the dot below follows.
        var harfbuzz = Reference.HarfBuzzReference.Instance.Shape(TestFonts.Bytes("TheanoDidot-Regular.ttf"), text, "Latn", Reference.HarfBuzzCorpusTests.FeaturesNotImplemented);

        Glyphs(text, TestFonts.TheanoDidot).Should().Equal(harfbuzz.Select(glyph => glyph.Glyph));
    }

    [Fact]
    public void The_glyphs_texts_are_the_text_as_written()
    {
        // Composed, reordered or decomposed, the texts joined are the source text, so copying and the PDF's text give the
        // characters the author wrote.
        foreach (var text in new[] { "n" + Cedilla + "o", "a" + Circumflex + DotBelow + "b", "xậ y" })
        {
            string.Concat(Shaper.Shape(text, TestFonts.TheanoDidot, 10).Value.Select(glyph => glyph.Text)).Should().Be(text);
            string.Concat(Shaper.Shape(text, TestFonts.Regular, 10).Value.Select(glyph => glyph.Text)).Should().Be(text);
        }
    }

    [Fact]
    public void A_run_of_more_than_32_marks_is_not_reordered()
    {
        // HarfBuzz's HB_OT_SHAPE_MAX_COMBINING_MARKS: 33 overlines and a dot below stay in their order, so the dot below is
        // drawn last; with 31 overlines it moves before them (class 220 before 230).
        var dot = Glyphs(DotBelow, TestFonts.Regular).Single();
        Glyphs("x" + string.Concat(Enumerable.Repeat(Overline, 33)) + DotBelow, TestFonts.Regular)[^1].Should().Be(dot);
        Glyphs("x" + string.Concat(Enumerable.Repeat(Overline, 31)) + DotBelow, TestFonts.Regular)[1].Should().Be(dot);
    }
}
