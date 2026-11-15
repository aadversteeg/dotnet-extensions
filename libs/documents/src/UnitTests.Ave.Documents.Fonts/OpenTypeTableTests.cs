using Ave.Documents.Core;
using Ave.Documents.Fonts;
using Ave.Documents.Fonts.OpenType;
using Ave.Documents.TestSupport;

namespace UnitTests.Ave.Documents.Fonts;

/// <summary>Reading GSUB, GPOS and 'kern'. Lato facts from fontTools 4.68.0: GSUB has 41 lookups, GPOS 5; GPOS
/// lookup 0 (kern) holds six PairPos format 1 and six format 2 subtables behind extension lookups.</summary>
public class OpenTypeTableTests
{
    [Fact]
    public void Reads_the_lookups_of_lato_through_their_extension_subtables()
    {
        var layout = TestFonts.Regular.Layout;

        layout.Substitutions.Lookups.Should().HaveCount(41);
        var kern = layout.Positions.Lookups[0].Subtables;
        kern.OfType<PairAdjustmentByGlyph>().Should().HaveCount(6);
        kern.OfType<PairAdjustmentByClass>().Should().HaveCount(6);
        layout.Positions.Lookups.Skip(1).SelectMany(lookup => lookup.Subtables).Should().AllBeOfType<MarkToBase>();
        layout.Definitions.Should().BeNull("Lato has no GDEF table");
        layout.Kerning.Should().Be(LegacyKerning.None, "Lato has no 'kern' table");
    }

    [Fact]
    public void Contextual_lookups_are_kept_as_unsupported_subtables_not_errors()
    {
        // Lato's calt lookups 26 and 28 are chaining contextual substitutions (GSUB type 6, format 3).
        TestFonts.Regular.Layout.Substitutions.Lookups[26].Subtables.Should().NotBeEmpty()
            .And.AllBeEquivalentTo(new UnsupportedSubtable("GSUB", 6, 3));
    }

    [Fact]
    public void Reads_a_format_0_kern_table_and_keeps_only_horizontal_kerning()
    {
        var table = new BigEndianBuilder()
            .UInt16(0).UInt16(2)                                                    // version 0, two subtables
            .UInt16(0).UInt16(14 + 6).UInt16(0x0001)                                // horizontal, format 0
            .UInt16(1).UInt16(6).UInt16(0).UInt16(0).UInt16(5).UInt16(7).UInt16(unchecked((ushort)-40))
            .UInt16(0).UInt16(14 + 6).UInt16(0x0005)                                // cross-stream: not applied
            .UInt16(1).UInt16(6).UInt16(0).UInt16(0).UInt16(5).UInt16(7).UInt16(unchecked((ushort)-99))
            .ToArray();

        var kerning = LegacyKerning.Parse(table).Value;

        kerning.Subtables.Should().ContainSingle();
        kerning.Adjustment(new GlyphId(5), new GlyphId(7)).Should().Be(-40);
    }

    [Fact]
    public void A_truncated_layout_table_is_a_parse_error()
    {
        var gpos = TestFonts.Regular.Layout.Positions;
        var bytes = TestFonts.Bytes("Lato-Regular.ttf");
        var directory = global::Ave.Documents.Fonts.Tables.TableDirectory.Parse(bytes).Value;
        var truncated = directory.Tables["GPOS"][..200];

        var result = LayoutTableParser.Parse("GPOS", truncated, TestFonts.Regular.GlyphCount);

        gpos.Lookups.Should().NotBeEmpty();
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DocumentErrorCodes.Font.Parse.Truncated);
        result.Error.Metadata![ErrorMetadataKeys.Table].Should().Be("GPOS");
    }
}
