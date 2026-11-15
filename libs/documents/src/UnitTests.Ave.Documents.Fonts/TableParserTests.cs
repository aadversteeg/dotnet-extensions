using Ave.Documents.Core;
using Ave.Documents.Fonts;
using Ave.Documents.Fonts.Tables;

namespace UnitTests.Ave.Documents.Fonts;

/// <summary>
/// Table parsers on hand-made tables, for the cases the Lato fonts do not contain (cmap format 12, a cmap
/// format 4 segment that uses idRangeOffset, hmtx with fewer records than glyphs). Layouts from the OpenType
/// specification.
/// </summary>
public class TableParserTests
{
    [Fact]
    public void Cmap_format_4_maps_delta_segments_and_glyph_array_segments()
    {
        // Segment 0: 'A'..'C' with idDelta = 10 - 0x41, so A → 10, B → 11, C → 12.
        // Segment 1: 'a'..'b' through glyphIdArray [20, 0]: a → 20, b → 0 (= missing).
        // Segment 2: the required final segment 0xFFFF → glyph 0.
        // idRangeOffset of segment 1 sits at byte 36 of the subtable, glyphIdArray starts at 40: offset 4.
        var subtable = new BigEndianBuilder()
            .UInt16(4).UInt16(44).UInt16(0).UInt16(6).UInt16(0).UInt16(0).UInt16(0)
            .UInt16(0x43).UInt16(0x62).UInt16(0xFFFF)
            .UInt16(0)
            .UInt16(0x41).UInt16(0x61).UInt16(0xFFFF)
            .UInt16(10 - 0x41).UInt16(0).UInt16(1)
            .UInt16(0).UInt16(4).UInt16(0)
            .UInt16(20).UInt16(0)
            .ToArray();

        var map = CmapTable.Parse(Cmap(3, 1, subtable), glyphCount: 30).Value;

        map.Should().BeEquivalentTo(new Dictionary<int, GlyphId>
        {
            ['A'] = new(10), ['B'] = new(11), ['C'] = new(12), ['a'] = new(20),
        });
    }

    [Fact]
    public void Cmap_format_12_maps_groups_beyond_the_basic_multilingual_plane()
    {
        var subtable = Format12((0x41, 0x43, 5), (0x1F600, 0x1F601, 8));

        var map = CmapTable.Parse(Cmap(3, 10, subtable), glyphCount: 30).Value;

        map.Should().BeEquivalentTo(new Dictionary<int, GlyphId>
        {
            [0x41] = new(5), [0x42] = new(6), [0x43] = new(7), [0x1F600] = new(8), [0x1F601] = new(9),
        });
    }

    [Fact]
    public void Cmap_prefers_the_full_repertoire_subtable()
    {
        var bmp = Format12((0x41, 0x41, 1));
        var full = Format12((0x41, 0x41, 2));
        var cmap = new BigEndianBuilder()
            .UInt16(0).UInt16(2)
            .UInt16(3).UInt16(1).UInt32(20)
            .UInt16(3).UInt16(10).UInt32(20 + bmp.Length)
            .Bytes(bmp).Bytes(full)
            .ToArray();

        CmapTable.Parse(cmap, glyphCount: 30).Value[0x41].Should().Be(new GlyphId(2));
    }

    [Fact]
    public void Cmap_format_12_groups_out_of_order_are_invalid()
    {
        var subtable = Format12((0x50, 0x51, 1), (0x41, 0x42, 3));

        CmapTable.Parse(Cmap(3, 10, subtable), glyphCount: 30).Error.Code.Should().Be(DocumentErrorCodes.Font.Parse.InvalidValue);
    }

    [Fact]
    public void Cmap_mapping_to_a_glyph_the_font_does_not_have_is_invalid()
    {
        var subtable = Format12((0x41, 0x41, 99));

        CmapTable.Parse(Cmap(3, 10, subtable), glyphCount: 30).Error.Code.Should().Be(DocumentErrorCodes.Font.Parse.InvalidValue);
    }

    [Fact]
    public void Hmtx_glyphs_after_the_last_record_reuse_its_advance()
    {
        var table = new BigEndianBuilder().UInt16(500).UInt16(0).UInt16(600).UInt16(0).UInt16(7).UInt16(8).ToArray();

        HmtxTable.Parse(table, numberOfHMetrics: 2, glyphCount: 4).Value.AdvanceWidths.Should().Equal(500, 600, 600, 600);
    }

    [Fact]
    public void Loca_short_offsets_are_stored_halved_and_must_ascend()
    {
        var ascending = new BigEndianBuilder().UInt16(0).UInt16(5).UInt16(5).UInt16(9).ToArray();
        var descending = new BigEndianBuilder().UInt16(0).UInt16(5).UInt16(4).UInt16(9).ToArray();

        LocaTable.Parse(ascending, longOffsets: false, glyphCount: 3, glyfLength: 18).Value.Offsets.Should().Equal(0, 10, 10, 18);
        LocaTable.Parse(descending, longOffsets: false, glyphCount: 3, glyfLength: 18).Error.Code.Should().Be(DocumentErrorCodes.Font.Parse.InvalidValue);
    }

    [Theory]
    [InlineData(0x0000, true)]
    [InlineData(0x0002, false)]
    [InlineData(0x0004, true)]
    [InlineData(0x0006, true)]
    [InlineData(0x0008, true)]
    [InlineData(0x0200, false)]
    [InlineData(0x0208, false)]
    public void Embedding_permissions_follow_the_fsType_bits(int fsType, bool allowed) =>
        new EmbeddingPermissions((ushort)fsType).AllowsOutlineEmbedding.Should().Be(allowed);

    private static byte[] Cmap(int platform, int encoding, byte[] subtable) =>
        new BigEndianBuilder().UInt16(0).UInt16(1).UInt16(platform).UInt16(encoding).UInt32(12).Bytes(subtable).ToArray();

    private static byte[] Format12(params (long Start, long End, long Glyph)[] groups)
    {
        var builder = new BigEndianBuilder().UInt16(12).UInt16(0).UInt32(16 + 12 * groups.Length).UInt32(0).UInt32(groups.Length);
        foreach (var (start, end, glyph) in groups)
        {
            builder.UInt32(start).UInt32(end).UInt32(glyph);
        }

        return builder.ToArray();
    }
}
