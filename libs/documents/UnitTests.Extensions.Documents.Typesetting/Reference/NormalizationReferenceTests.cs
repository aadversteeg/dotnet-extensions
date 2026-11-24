using Ave.Extensions.Documents.Fonts;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.Documents.Typesetting;

namespace UnitTests.Extensions.Documents.Typesetting.Reference;

/// <summary>
/// Normalization against HarfBuzz on the clusters where one rule of the normalizer decides the glyphs (where
/// a change to that rule leaves the corpus green): a composition the Unicode data
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

    /// <summary>
    /// The exceptions <c>docs/architecture.md</c> lists ("Text") that the family's fonts can show, each against HarfBuzz:
    /// a cluster with a variation selector is normalized, where HarfBuzz leaves it alone; and the fallback for a missing
    /// space, which HarfBuzz makes, is not made: the text is refused. The fallback for U+2011 NON-BREAKING HYPHEN is made
    /// as HarfBuzz makes it, with the glyph of U+2010.
    /// </summary>
    [Fact]
    public void A_cluster_with_a_variation_selector_is_normalized_where_HarfBuzz_leaves_it_alone()
    {
        // U+01D5, which Theano Didot lacks, before U+FE00: the engine takes its shortest decomposition, HarfBuzz keeps the
        // character and draws the missing glyph.
        var ours = Shaper.Shape("\u01D5\uFE00", TestFonts.TheanoDidot, TestFonts.TheanoDidot.Metrics.UnitsPerEm);
        var theirs = HarfBuzzReference.Instance.Shape(TestFonts.Bytes("TheanoDidot-Regular.ttf"), "\u01D5\uFE00", "Latn", HarfBuzzCorpusTests.FeaturesNotImplemented);

        ours.IsSuccess.Should().BeTrue(ours.IsFailure ? ours.Error.ToString() : string.Empty);
        ours.Value.Select(glyph => (int)glyph.Glyph.Value).Take(2).Should().Equal(The("TheanoDidot-Regular.ttf", "\u00DC")[0], The("TheanoDidot-Regular.ttf", "\u0304")[0]);
        theirs[0].Glyph.Should().Be(0, "HarfBuzz leaves the cluster alone, and the font has no glyph for U+01D5");
    }

    [Fact]
    public void A_missing_non_breaking_hyphen_is_drawn_with_the_glyph_HarfBuzz_takes()
    {
        var ours = Shaper.Shape("a\u2011b", TestFonts.Regular, TestFonts.Regular.Metrics.UnitsPerEm);
        var theirs = HarfBuzzReference.Instance.Shape(TestFonts.Bytes("Lato-Regular.ttf"), "a\u2011b", "Latn", HarfBuzzCorpusTests.FeaturesNotImplemented);

        ours.IsSuccess.Should().BeTrue(ours.IsFailure ? ours.Error.ToString() : string.Empty);
        ((int)ours.Value[1].Glyph.Value).Should().Be(theirs[1].Glyph);
        ((int)ours.Value[1].Glyph.Value).Should().Be(The("Lato-Regular.ttf", "\u2010")[0]);
    }

    [Theory]
    [InlineData("Lato-Regular.ttf", "\u3000", " ")]
    [InlineData("TheanoDidot-Regular.ttf", "\u205F", " ")]
    public void A_missing_space_is_refused_where_HarfBuzz_falls_back(string file, string missing, string fallback)
    {
        var font = Font(file);
        var ours = Shaper.Shape($"a{missing}b", font, font.Metrics.UnitsPerEm);
        var theirs = HarfBuzzReference.Instance.Shape(TestFonts.Bytes(file), $"a{missing}b", "Latn", HarfBuzzCorpusTests.FeaturesNotImplemented);

        ours.IsFailure.Should().BeTrue();
        ours.Error.Code.Should().Be(FontErrorCodes.GlyphMissing);
        theirs[1].Glyph.Should().Be(The(file, fallback)[0], "HarfBuzz draws the font's ordinary space");
    }

    private static int[] The(string file, string text) =>
        [.. HarfBuzzReference.Instance.Shape(TestFonts.Bytes(file), text, "Latn", HarfBuzzCorpusTests.FeaturesNotImplemented).Select(glyph => glyph.Glyph)];

    [Fact]
    public void A_character_whose_parts_the_font_cannot_all_show_is_reported_missing_once()
    {
        // U+1EDE in Theano Didot: the font lacks the character and the horn; decomposing it anyway would report the parts.
        var shaped = Shaper.Shape("\u1EDE", TestFonts.TheanoDidot, 10);

        shaped.IsFailure.Should().BeTrue();
        shaped.Error.Code.Should().Be(FontErrorCodes.GlyphMissing, "one character is missing, not several parts");
    }
}
