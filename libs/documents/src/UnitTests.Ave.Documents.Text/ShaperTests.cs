using Ave.Documents.Core;
using Ave.Documents.Fonts;
using Ave.Documents.TestSupport;
using Ave.Documents.Text;

namespace UnitTests.Ave.Documents.Text;

/// <summary>
/// Glyph ids and advance widths from Lato Regular's cmap and hmtx (values read with fontTools 4.68.0):
/// H = glyph 15, 1527 units; i = glyph 98, 480 units; space = glyph 2, 512 units; 2000 units per em.
/// </summary>
public class ShaperTests
{
    [Fact]
    public void Each_character_becomes_its_cmap_glyph_with_its_hmtx_advance_scaled_to_the_size()
    {
        var glyphs = Shaper.Shape("Hi ", TestFonts.Regular, fontSize: 10).Value;

        glyphs.Select(glyph => glyph.Glyph).Should().Equal(new GlyphId(15), new GlyphId(98), new GlyphId(2));
        glyphs.Select(glyph => glyph.Advance).Should().Equal(1527 * 10 / 2000.0, 480 * 10 / 2000.0, 512 * 10 / 2000.0);
        glyphs.Select(glyph => glyph.Text).Should().Equal("H", "i", " ");
    }

    [Fact]
    public void Every_missing_character_is_reported_with_font_character_and_code_point()
    {
        var error = Shaper.Shape("a漢b漢", TestFonts.Regular, 10).Error;

        var parts = ResultCollections.Parts(error);
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
