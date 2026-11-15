using System.Runtime.CompilerServices;
using System.Text;
using Ave.Documents.Core;
using Ave.Documents.Fonts;
using Ave.Documents.TestSupport;

namespace UnitTests.Ave.Documents.Fonts;

/// <summary>
/// Reads the committed Lato fonts. The expected values were read independently with fontTools 4.68.0
/// (<c>TTFont('Lato-Regular.ttf')</c>: head, hhea, OS/2, post, name, getBestCmap, hmtx, loca), not with this code.
/// </summary>
public class FontParserTests
{
    [Fact]
    public void Reads_the_names()
    {
        var names = TestFonts.Regular.Names;

        names.Family.Should().Be("Lato");
        names.Subfamily.Should().Be("Regular");
        names.FullName.Should().Be("Lato Regular");
        names.PostScriptName.Should().Be("Lato-Regular");
        TestFonts.Bold.Names.PostScriptName.Should().Be("Lato-Bold");
    }

    [Fact]
    public void Reads_the_metrics()
    {
        var metrics = TestFonts.Regular.Metrics;

        metrics.UnitsPerEm.Should().Be(2000);
        metrics.Ascender.Should().Be(1974);
        metrics.Descender.Should().Be(-426);
        metrics.LineGap.Should().Be(0);
        metrics.CapHeight.Should().Be(1433);
        metrics.BoundingBox.Should().Be(new FontBoundingBox(-1093, -537, 2685, 2157));
        metrics.ItalicAngle.Should().Be(0);
        metrics.IsFixedPitch.Should().BeFalse();
        metrics.WeightClass.Should().Be(400);
        TestFonts.Bold.Metrics.WeightClass.Should().Be(700);
        TestFonts.Regular.GlyphCount.Should().Be(3023);
    }

    [Theory]
    [InlineData('A', 3, 1354)]
    [InlineData('a', 45, 994)]
    [InlineData('W', 41, 2071)]
    [InlineData(' ', 2, 512)]
    [InlineData(' ', 2, 512)]
    [InlineData('€', 360, 1160)]
    [InlineData('é', 2256, 1056)]
    public void Maps_characters_to_glyphs_and_advances(char character, int glyph, int advance)
    {
        var font = TestFonts.Regular;

        var found = font.GlyphFor(new Rune(character));

        found.HasValue.Should().BeTrue();
        found.Value.Should().Be(new GlyphId((ushort)glyph));
        font.AdvanceWidth(found.Value).Should().Be(advance);
    }

    [Fact]
    public void Has_no_glyph_for_a_character_outside_the_font() =>
        TestFonts.Regular.GlyphFor(new Rune('漢')).HasValue.Should().BeFalse();

    [Fact]
    public void Locates_glyph_outlines_through_loca()
    {
        var font = TestFonts.Regular;

        font.GlyphOutline(new GlyphId(3)).Length.Should().Be(136, "glyph 3 is 'A'");
        font.GlyphOutline(new GlyphId(2)).Length.Should().Be(0, "the space has no outline");
    }

    [Fact]
    public void Reads_a_glyph_s_bounding_box_from_its_glyf_header()
    {
        // fontTools 4.68.0: glyf['A'] xMin 6, yMin 0, xMax 1348, yMax 1433; glyf['g'] (glyph 93) 62, -365, 996, 1030.
        TestFonts.Regular.GlyphBounds(new GlyphId(3)).Value.Should().Be(new FontBoundingBox(6, 0, 1348, 1433));
        TestFonts.Regular.GlyphBounds(new GlyphId(93)).Value.Should().Be(new FontBoundingBox(62, -365, 996, 1030));
        TestFonts.Regular.GlyphBounds(new GlyphId(2)).HasValue.Should().BeFalse("the space has no outline, so no ink");
    }

    [Fact]
    public void A_glyph_shorter_than_its_glyf_header_is_an_invalid_value()
    {
        // OpenType "glyf": every glyph description starts with a 10-byte header (numberOfContours, xMin, yMin, xMax, yMax).
        // Lato's 'loca' is Offset32 at file offset 21256 (fontTools): moving loca[4] to loca[3] + 4 leaves glyph 3 with
        // 4 bytes, while the offsets stay ascending.
        var data = TestFonts.Bytes("Lato-Regular.ttf");
        new BigEndianBuilder().UInt32(252 + 4).ToArray().CopyTo(data, 21256 + 4 * 4);

        var error = FontParser.Parse(data).Error;

        error.Code.Should().Be(FontErrorCodes.Parse.InvalidValue);
        error.Metadata![ErrorMetadataKeys.Table].Should().Be("loca");
    }

    [Fact]
    public void Identifies_the_font_by_the_digest_of_its_file()
    {
        TestFonts.Regular.Id.Digest.Should().Be("d636e4683231f931eda222d588e944d082bfd3bdba02f928bee461c0f185b251");
        FontParser.Parse(TestFonts.Bytes("Lato-Regular.ttf")).Value.Should().Be(TestFonts.Regular, "the same bytes are the same font");
    }

    [Fact]
    public void Allows_embedding() => TestFonts.Regular.Embedding.AllowsOutlineEmbedding.Should().BeTrue();

    [Fact]
    public void Data_that_is_not_a_font_is_an_unsupported_format_with_the_callers_location()
    {
        var line = CurrentLine() + 1;
        var result = FontParser.Parse(Encoding.ASCII.GetBytes("This is not a font file at all."));

        result.Error.Code.Should().Be(FontErrorCodes.Parse.UnsupportedFormat);
        result.Error.Metadata![ErrorMetadataKeys.SourceFile].Should().Be(ThisFile());
        result.Error.Metadata![ErrorMetadataKeys.SourceLine].Should().Be(line);
    }

    [Theory]
    [InlineData("OTTO", "OpenType fonts with CFF outlines")]
    [InlineData("ttcf", "Font collections")]
    public void Cff_fonts_and_collections_are_reported_as_unsupported(string tag, string message)
    {
        var data = new BigEndianBuilder().Bytes(Encoding.ASCII.GetBytes(tag)).UInt16(0).UInt16(0).UInt16(0).UInt16(0).ToArray();

        var error = FontParser.Parse(data).Error;

        error.Code.Should().Be(FontErrorCodes.Parse.UnsupportedFormat);
        error.Message.Should().StartWith(message);
    }

    [Fact]
    public void A_truncated_file_is_reported_as_truncated()
    {
        var data = TestFonts.Bytes("Lato-Regular.ttf")[..1000];

        FontParser.Parse(data).Error.Code.Should().Be(FontErrorCodes.Parse.Truncated);
    }

    [Fact]
    public void A_missing_table_is_reported_by_its_tag()
    {
        var data = RenameTable(TestFonts.Bytes("Lato-Regular.ttf"), "cmap", "xxxx");

        var error = FontParser.Parse(data).Error;

        error.Code.Should().Be(FontErrorCodes.Parse.MissingTable);
        error.Metadata![ErrorMetadataKeys.Table].Should().Be("cmap");
    }

    private static byte[] RenameTable(byte[] font, string from, string to)
    {
        var copy = (byte[])font.Clone();
        int tableCount = (copy[4] << 8) | copy[5];
        for (var index = 0; index < tableCount; index++)
        {
            var record = 12 + index * 16;
            if (Encoding.ASCII.GetString(copy, record, 4) == from)
            {
                Encoding.ASCII.GetBytes(to).CopyTo(copy, record);
            }
        }

        return copy;
    }

    private static int CurrentLine([CallerLineNumber] int line = 0) => line;

    private static string ThisFile([CallerFilePath] string file = "") => file;
}
