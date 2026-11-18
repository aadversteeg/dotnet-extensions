using Ave.Extensions.Documents.Fonts;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.Documents.Typesetting;

namespace UnitTests.Extensions.Documents.Typesetting.Reference;

/// <summary>
/// Normalization against HarfBuzz on the clusters where one rule of the normalizer decides the glyphs (found by the
/// review of pull request 6 with planted defects that the corpus did not catch): a composition the Unicode data
/// excludes, a mark that blocks a composition, a decomposition whose second part the font lacks, and the shortest
/// decomposition of a single character the font lacks. Glyph for glyph, with positions, against HarfBuzz 15.0.0.
/// </summary>
public class NormalizationReferenceTests
{
    public static readonly TheoryData<string, string> Clusters = new()
    {
        // U+0344 COMBINING GREEK DIALYTIKA TONOS decomposes to U+0308 U+0301 and is a composition exclusion: the two
        // marks must not compose back into it.
        { "Lato-Regular.ttf", "a\u0308\u0301" },
        { "Lato-Regular.ttf", "\u0308\u0301" },
        // A circumflex between A and U+0344 (class 230 both): the second mark is blocked by the first.
        { "Lato-Regular.ttf", "A\u0302\u0344" },
        { "TheanoDidot-Regular.ttf", "A\u0302\u0344" },
        // U+01D5 (U with diaeresis and macron), which Theano Didot lacks: it takes the shortest decomposition the font
        // can show, not the full one.
        { "TheanoDidot-Regular.ttf", "\u01D5" },
    };

    private static TrueTypeFont Font(string file) => file.StartsWith("Lato", StringComparison.Ordinal) ? TestFonts.Regular : TestFonts.TheanoDidot;

    [Theory]
    [MemberData(nameof(Clusters))]
    public void The_cluster_has_harfbuzz_s_glyphs(string file, string text)
    {
        var font = Font(file);
        var ours = Shaper.Shape(text, font, font.Metrics.UnitsPerEm);
        var theirs = HarfBuzzReference.Instance.Shape(TestFonts.Bytes(file), text, "Latn", HarfBuzzCorpusTests.FeaturesNotImplemented);

        ours.IsSuccess.Should().BeTrue(ours.IsFailure ? ours.Error.ToString() : string.Empty);
        ours.Value.Select(glyph => (int)glyph.Glyph.Value).Should().Equal(theirs.Select(glyph => glyph.Glyph));
        ours.Value.Select(glyph => glyph.Advance).Should().Equal(theirs.Select(glyph => (double)glyph.XAdvance));
        ours.Value.Select(glyph => glyph.XOffset).Should().Equal(theirs.Select(glyph => (double)glyph.XOffset));
    }

    [Fact]
    public void A_character_whose_parts_the_font_cannot_all_show_is_reported_missing_once()
    {
        // U+1EDE in Theano Didot: the font lacks the character and the horn; decomposing it anyway would report the parts.
        var shaped = Shaper.Shape("\u1EDE", TestFonts.TheanoDidot, 10);

        shaped.IsFailure.Should().BeTrue();
        shaped.Error.Code.Should().Be(FontErrorCodes.GlyphMissing, "one character is missing, not several parts");
    }
}
