using Ave.Documents.Core;
using Ave.Documents.Fonts;
using Ave.Documents.TestSupport;
using Ave.Documents.Typesetting;

namespace UnitTests.Ave.Documents.Typesetting;

/// <summary>
/// Glyph ids and advance widths from Lato Regular's cmap and hmtx (values read with fontTools 4.68.0):
/// H = glyph 15, 1527 units; i = glyph 98, 480 units; space = glyph 2, 512 units; 2000 units per em.
/// </summary>
public class ShaperTests
{
    [Fact]
    public void Each_character_becomes_its_cmap_glyph_with_its_hmtx_advance_scaled_to_the_size()
    {
        // "ab": a pair Lato does not kern (fontTools: PairPos format 2, classes 0 and 0, XAdvance 0).
        var glyphs = Shaper.Shape("ab ", TestFonts.Regular, fontSize: 10).Value;

        glyphs.Select(glyph => glyph.Text).Should().Equal("a", "b", " ");
        glyphs.Select(glyph => glyph.Advance).Should().Equal(
            TestFonts.Regular.AdvanceWidth(glyphs[0].Glyph) * 10 / 2000.0, TestFonts.Regular.AdvanceWidth(glyphs[1].Glyph) * 10 / 2000.0, 512 * 10 / 2000.0);
    }

    [Fact]
    public void A_kerning_pair_changes_the_advance_of_its_first_glyph()
    {
        // fontTools 4.68.0: Lato Regular GPOS lookup 0 (kern), PairPos format 2, H (class 7) + i (class 1): XAdvance -15.
        var glyphs = Shaper.Shape("Hi", TestFonts.Regular, fontSize: 10).Value;

        glyphs.Select(glyph => glyph.Glyph).Should().Equal(new GlyphId(15), new GlyphId(98));
        glyphs[0].Advance.Should().Be((1527 - 15) * 10 / 2000.0);
        glyphs[1].Advance.Should().Be(480 * 10 / 2000.0);
    }

    [Fact]
    public void Every_missing_character_is_reported_with_font_character_and_code_point()
    {
        var error = Shaper.Shape("a漢b漢", TestFonts.Regular, 10).Error;

        var parts = error.Parts();
        parts.Should().HaveCount(2);
        parts.Should().AllSatisfy(part =>
        {
            part.Code.Should().Be(DocumentErrorCodes.Font.GlyphMissing);
            part.Metadata![ErrorMetadataKeys.FontName].Should().Be("Lato-Regular");
            part.Metadata![ErrorMetadataKeys.Character].Should().Be("漢");
            part.Metadata![ErrorMetadataKeys.CodePoint].Should().Be("U+6F22");
        });
    }

    [Fact]
    public void A_surrogate_pair_is_one_character()
    {
        var error = Shaper.Shape("\U0001F600", TestFonts.Regular, 10).Error;

        error.Code.Should().Be(DocumentErrorCodes.Font.GlyphMissing);
        error.Metadata![ErrorMetadataKeys.CodePoint].Should().Be("U+1F600");
    }

    [Fact]
    public void An_unpaired_surrogate_is_invalid_text_not_a_replacement_character()
    {
        var error = Shaper.Shape("a\uD800b", TestFonts.Regular, 10).Error;

        error.Code.Should().Be(DocumentErrorCodes.Input.Invalid.Text);
        error.Metadata![ErrorMetadataKeys.Offset].Should().Be(1);
    }
}
