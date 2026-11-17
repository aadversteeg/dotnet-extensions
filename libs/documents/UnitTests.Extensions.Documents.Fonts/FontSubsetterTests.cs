using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Ave.Extensions.Documents.Fonts;
using Ave.Extensions.Documents.Fonts.Tables;
using Ave.Extensions.Documents.TestSupport;

namespace UnitTests.Extensions.Documents.Fonts;

/// <summary>
/// TrueType subsetting. Glyph facts come from fontTools 4.68.0, not from this code: in Lato Regular, 'A' is glyph
/// 3, 'Aacute' is glyph 1836, a composite of glyphs 3 and 173; 'e' is 59 and the font has 3023 glyphs.
/// </summary>
public class FontSubsetterTests
{
    private static readonly GlyphId A = new(3);
    private static readonly GlyphId Aacute = new(1836);
    private static readonly GlyphId AcuteComponent = new(173);
    private static readonly GlyphId E = new(59);

    private static FontSubset Subset(params GlyphId[] glyphs)
    {
        var result = FontSubsetter.Subset(TestFonts.Regular, glyphs);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        return result.Value;
    }

    private static TableDirectory Directory(FontSubset subset) => TableDirectory.Parse(subset.FontFile.ToArray()).Value;

    /// <summary>The bytes of glyph <paramref name="glyph"/> in a font file's 'glyf', located through its long 'loca'.</summary>
    private static byte[] GlyphBytes(TableDirectory directory, GlyphId glyph)
    {
        var loca = directory.Tables["loca"].Span;
        var start = (int)BinaryPrimitives.ReadUInt32BigEndian(loca[(glyph.Value * 4)..]);
        var end = (int)BinaryPrimitives.ReadUInt32BigEndian(loca[((glyph.Value + 1) * 4)..]);
        return directory.Tables["glyf"].Span[start..end].ToArray();
    }

    [Fact]
    public void A_composite_whose_component_is_not_a_glyph_of_the_font_is_an_error()
    {
        // The first component record of Aacute starts after the 10-byte glyph header: flags, then glyphIndex.
        var bytes = TestFonts.Bytes("Lato-Regular.ttf");
        MemoryMarshal.TryGetArray(FontParser.Parse(bytes).Value.GlyphOutline(Aacute), out var outline).Should().BeTrue();
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(outline.Offset + 12), 0xFFFF);

        var result = FontSubsetter.Subset(FontParser.Parse(bytes).Value, [Aacute]);

        result.IsFailure.Should().BeTrue("glyph 65535 does not exist in a font of 3023 glyphs");
        result.Error.Code.Should().Be(FontErrorCodes.Parse.InvalidValue);
    }

    [Fact]
    public void Keeps_the_used_glyphs_glyph_zero_and_the_components_of_composites()
    {
        Subset(Aacute, E).Glyphs.Should().Equal(new GlyphId(0), A, E, AcuteComponent, Aacute);
    }

    [Fact]
    public void Kept_glyphs_keep_their_ids_and_bytes_and_all_others_are_empty()
    {
        var directory = Directory(Subset(Aacute));

        foreach (var kept in new[] { new GlyphId(0), A, AcuteComponent, Aacute })
        {
            var original = TestFonts.Regular.GlyphOutline(kept).ToArray();
            GlyphBytes(directory, kept).Take(original.Length).Should().Equal(original, $"glyph {kept} is copied unchanged");
        }

        GlyphBytes(directory, E).Should().BeEmpty("glyph 'e' is not used");
        directory.Tables["loca"].Length.Should().Be((3023 + 1) * 4, "every glyph id still exists (long offsets)");
    }

    [Fact]
    public void Has_only_the_tables_a_pdf_consumer_needs()
    {
        Directory(Subset(A)).Tables.Keys.Order(StringComparer.Ordinal).Should().Equal("cvt ", "fpgm", "glyf", "head", "hhea", "hmtx", "loca", "maxp", "prep");
    }

    [Fact]
    public void Every_table_checksum_and_the_whole_file_checksum_are_valid()
    {
        var file = Subset(A, E).FontFile.ToArray();
        var count = BinaryPrimitives.ReadUInt16BigEndian(file.AsSpan(4));
        for (var index = 0; index < count; index++)
        {
            var record = 12 + index * 16;
            var tag = System.Text.Encoding.ASCII.GetString(file, record, 4);
            var checksum = BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(record + 4));
            var offset = (int)BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(record + 8));
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(record + 12));
            var table = file.AsSpan(offset, length).ToArray();
            if (tag == "head")
            {
                // The head checksum is computed with checkSumAdjustment set to zero (OpenType spec, "head").
                BinaryPrimitives.WriteUInt32BigEndian(table.AsSpan(8), 0);
            }

            offset.Should().Be(offset / 4 * 4, $"table {tag} starts on a 4-byte boundary");
            FontSubsetter.Checksum(table).Should().Be(checksum, $"checksum of {tag}");
        }

        FontSubsetter.Checksum(file).Should().Be(0xB1B0AFBA, "head.checkSumAdjustment makes the whole file sum to the magic value");
    }

    [Fact]
    public void Head_switches_to_long_loca_offsets()
    {
        // Theano Didot has short 'loca' offsets (head.indexToLocFormat 0, fontTools 4.68.0); Lato's are long already,
        // so only a short-offset font shows that the subsetter writes the format it uses.
        var theano = TestFonts.TheanoDidot;
        BinaryPrimitives.ReadInt16BigEndian(TableDirectory.Parse(theano.FileData).Value.Tables["head"].Span[50..]).Should().Be(0);

        var subset = FontSubsetter.Subset(theano, [new GlyphId(36)]).Value;

        BinaryPrimitives.ReadInt16BigEndian(Directory(subset).Tables["head"].Span[50..]).Should().Be(1);
        Directory(subset).Tables["loca"].Length.Should().Be((theano.GlyphCount + 1) * 4, "one 4-byte offset per glyph and one at the end");
    }

    [Fact]
    public void Is_deterministic_and_the_tag_is_six_uppercase_letters_that_differ_per_subset()
    {
        var first = Subset(A, E);
        var again = Subset(E, A);
        var other = Subset(A);

        first.FontFile.SequenceEqual(again.FontFile).Should().BeTrue();
        first.Tag.Should().Be(again.Tag).And.MatchRegex("^[A-Z]{6}$");
        other.Tag.Should().NotBe(first.Tag);
        first.PdfName(TestFonts.Regular).Should().Be($"{first.Tag}+Lato-Regular");
    }

    [Fact]
    public void Is_far_smaller_than_the_whole_font()
    {
        Subset(A, E).FontFile.Length.Should().BeLessThan(TestFonts.Regular.FileData.Length / 10);
    }

    /// <summary>Lato with its OS/2 fsType set to <paramref name="fsType"/> (OS/2 offset 8).</summary>
    private static TrueTypeFont WithFsType(ushort fsType)
    {
        var bytes = TestFonts.Bytes("Lato-Regular.ttf");
        var os2 = TableDirectory.Parse(bytes).Value.Tables["OS/2"];
        MemoryMarshal.TryGetArray(os2, out var table).Should().BeTrue();
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(table.Offset + 8), fsType);
        return FontParser.Parse(bytes).Value;
    }

    [Fact]
    public void A_font_whose_licence_forbids_subsetting_is_embedded_whole_without_a_subset_tag()
    {
        // OS/2 fsType bit 8, 0x0100 "No subsetting": "When this bit is set, the font must not be subsetted prior to
        // embedding."
        var font = WithFsType(0x0100);

        var embedded = FontSubsetter.Subset(font, [A, E]).Value;

        embedded.IsSubset.Should().BeFalse();
        embedded.FontFile.Should().Equal(font.FileData.ToArray());
        embedded.PdfName(font).Should().Be("Lato-Regular", "only a subset carries a tag (ISO 32000-1 §9.6.4)");
    }
}
