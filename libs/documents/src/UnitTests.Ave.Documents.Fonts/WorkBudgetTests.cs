using Ave.Documents.Fonts;
using Ave.Documents.Fonts.OpenType;
using Ave.Documents.TestSupport;

namespace UnitTests.Ave.Documents.Fonts;

/// <summary>
/// The work budget of a layout table (eight units per byte, as HarfBuzz's sanitizer) must never refuse a valid font.
/// Work is charged per record read: a range record that covers thousands of glyphs is one record, as in the fonts of
/// large scripts (Malgun Gothic's GDEF is 30 bytes for 28 215 glyphs), and a coverage or class table that many
/// subtables point at is read once.
/// </summary>
public class WorkBudgetTests
{
    [Fact]
    public void A_glyph_class_range_over_28215_glyphs_in_a_22_byte_GDEF_is_read()
    {
        var gdef = new BigEndianBuilder()
            .UInt16(1).UInt16(0).UInt16(12).UInt16(0).UInt16(0).UInt16(0)   // version 1.0, glyphClassDef at 12
            .UInt16(2).UInt16(1).UInt16(0).UInt16(28214).UInt16(1)          // format 2, one range 0–28214, class 1
            .ToArray();

        var result = GlyphDefinitions.Parse(gdef);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        result.Value.GlyphClasses.ClassOf(new GlyphId(28000)).Should().Be(1);
        result.Value.GlyphClasses.ClassOf(new GlyphId(28215)).Should().Be(0);
    }

    [Fact]
    public void Twenty_lookups_whose_coverage_ranges_cover_every_glyph_are_read()
    {
        // 20 lookups of one single-substitution subtable (format 1, delta 0) whose coverage is ONE range over all
        // 3023 glyphs of Lato: 60 460 covered glyphs from a table of about 400 bytes.
        const int Lookups = 20;
        var glyphs = TestFonts.Regular.GlyphCount;
        var builder = new BigEndianBuilder().UInt16(1).UInt16(0).UInt16(10).UInt16(12).UInt16(14).UInt16(0).UInt16(0).UInt16(Lookups);
        var lookupStart = 2 + Lookups * 2;
        for (var index = 0; index < Lookups; index++)
        {
            builder.UInt16(lookupStart + index * 24);
        }

        for (var index = 0; index < Lookups; index++)
        {
            builder.UInt16(1).UInt16(0).UInt16(1).UInt16(8)                   // lookup: type 1, one subtable at +8
                .UInt16(1).UInt16(6).UInt16(0)                                // subtable: format 1, coverage at +6, delta 0
                .UInt16(2).UInt16(1).UInt16(0).UInt16(glyphs - 1).UInt16(0);  // coverage format 2, one range
        }

        var result = LayoutTableParser.Parse("GSUB", builder.ToArray(), glyphs);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        result.Value.Lookups.Should().HaveCount(Lookups);
        var single = (SingleSubstitution)result.Value.Lookups[19].Subtables[0];
        single.Replace(new GlyphId(3000)).Should().Be(new GlyphId(3000));
        single.Replace(new GlyphId((ushort)glyphs)).Should().BeNull("glyph 3023 is not covered");
    }
}
