using Ave.Documents.TestSupport;
using Ave.Documents.Typesetting;

namespace UnitTests.Ave.Documents.Typesetting.Reference;

/// <summary>
/// "f", an invisible character and "i" in Lato, shaped by HarfBuzz (the reference build) and by the shaper. HarfBuzz
/// skips every default-ignorable character but the zero width non-joiner while matching GSUB, so it forms the fi
/// ligature and puts the invisible glyph after it; the shaper does the same, with two documented exceptions: the soft
/// hyphen and the zero width space are line break opportunities, and the shaper does not shape again where a line
/// breaks, so it lets them stop the ligature (GlyphFilter.SkipsInSubstitution). Compared: the visible glyphs (the
/// invisible one is a zero-advance glyph in HarfBuzz and glyph 0 in the shaper).
/// </summary>
public class IgnorableShapingReferenceTests
{
    private const int F = 61;
    private const int I = 98;
    private const int Fi = 67;

    private static int[] HarfBuzz(string text) =>
        [.. HarfBuzzReference.Instance.Shape(TestFonts.Bytes("Lato-Regular.ttf"), text, "Latn", HarfBuzzCorpusTests.FeaturesNotImplemented)
            .Where(glyph => glyph.XAdvance != 0)
            .Select(glyph => glyph.Glyph)];

    private static int[] Ours(string text) =>
        [.. Shaper.Shape(text, TestFonts.Regular, 2000).Value.Where(glyph => !glyph.IsInvisible).Select(glyph => (int)glyph.Glyph.Value)];

    private static string Between(int codePoint) => "f" + char.ConvertFromUtf32(codePoint) + "i";

    [Theory]
    [InlineData(0x2060)] // word joiner
    [InlineData(0x034F)] // combining grapheme joiner
    [InlineData(0xFEFF)] // zero width no-break space
    [InlineData(0x200E)] // left-to-right mark
    [InlineData(0x200D)] // zero width joiner
    [InlineData(0x200C)] // zero width non-joiner: stops the ligature in both
    public void The_shaper_matches_harfbuzz_for_an_invisible_character_between_two_letters(int codePoint)
    {
        Ours(Between(codePoint)).Should().Equal(HarfBuzz(Between(codePoint)));
    }

    [Theory]
    [InlineData(0x00AD)] // soft hyphen
    [InlineData(0x200B)] // zero width space
    public void At_a_line_break_opportunity_the_shaper_keeps_the_letters_apart_where_harfbuzz_ligates(int codePoint)
    {
        HarfBuzz(Between(codePoint)).Should().Equal([Fi], "HarfBuzz skips the character and forms the ligature");
        Ours(Between(codePoint)).Should().Equal([F, I], "the shaper does not shape again at a line break, so the break point stops the ligature");
    }
}
