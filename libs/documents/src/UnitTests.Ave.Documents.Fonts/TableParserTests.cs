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

    [Fact]
    public void The_sub_review_s_8000_overlapping_format_4_segments_are_refused_quickly()
    {
        // Probe P4: 8000 segments of 0x0000–0xFFFE (64 KB) took 4.4 s. Their endCodes do not increase ("The segments
        // are sorted in order of increasing endCode values"), so the table is malformed.
        const int Segments = 8000;
        var builder = new BigEndianBuilder().UInt16(4).UInt16(16 + Segments * 8).UInt16(0).UInt16(Segments * 2).UInt16(0).UInt16(0).UInt16(0);
        for (var index = 0; index < Segments; index++) { builder.UInt16(index == Segments - 1 ? 0xFFFF : 0xFFFE); }
        builder.UInt16(0);
        for (var index = 0; index < Segments; index++) { builder.UInt16(index == Segments - 1 ? 0xFFFF : 0); }
        for (var index = 0; index < Segments; index++) { builder.UInt16(index == Segments - 1 ? 1 : 0); }
        for (var index = 0; index < Segments; index++) { builder.UInt16(0); }
        var cmap = new BigEndianBuilder().UInt16(0).UInt16(1).UInt16(3).UInt16(1).UInt32(12).Bytes(builder.ToArray()).ToArray();
        var watch = System.Diagnostics.Stopwatch.StartNew();

        var result = CmapTable.Parse(cmap, glyphCount: 65535);

        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DocumentErrorCodes.Font.Parse.InvalidValue);
    }

    [Fact]
    public void In_a_format_4_cmap_a_code_belongs_to_the_first_segment_whose_end_code_covers_it()
    {
        // A lookup finds the first segment whose endCode is at or above the code, then checks its startCode. Segment
        // 1 is 0x41–0x50 (delta 1), segment 2 starts lower, 0x30–0x60 (delta 7): U+0041 is segment 1's; U+0035 finds
        // segment 1 too, whose startCode is above it, so it is not mapped; U+0055 is segment 2's.
        var subtable = new BigEndianBuilder()
            .UInt16(4).UInt16(16 + 3 * 8).UInt16(0).UInt16(6).UInt16(0).UInt16(0).UInt16(0)
            .UInt16(0x50).UInt16(0x60).UInt16(0xFFFF)
            .UInt16(0)
            .UInt16(0x41).UInt16(0x30).UInt16(0xFFFF)
            .UInt16(1).UInt16(7).UInt16(1)
            .UInt16(0).UInt16(0).UInt16(0)
            .ToArray();
        var cmap = new BigEndianBuilder().UInt16(0).UInt16(1).UInt16(3).UInt16(1).UInt32(12).Bytes(subtable).ToArray();

        var map = CmapTable.Parse(cmap, glyphCount: 200).Value;

        map[0x41].Value.Should().Be(0x42);
        map.Should().NotContainKey(0x35);
        map[0x55].Value.Should().Be(0x5C);
    }

    [Fact]
    public void A_format_4_cmap_whose_end_codes_decrease_is_invalid()
    {
        var subtable = new BigEndianBuilder()
            .UInt16(4).UInt16(16 + 3 * 8).UInt16(0).UInt16(6).UInt16(0).UInt16(0).UInt16(0)
            .UInt16(0x60).UInt16(0x50).UInt16(0xFFFF)
            .UInt16(0)
            .UInt16(0x41).UInt16(0x41).UInt16(0xFFFF)
            .UInt16(1).UInt16(1).UInt16(1)
            .UInt16(0).UInt16(0).UInt16(0)
            .ToArray();
        var cmap = new BigEndianBuilder().UInt16(0).UInt16(1).UInt16(3).UInt16(1).UInt32(12).Bytes(subtable).ToArray();

        var result = CmapTable.Parse(cmap, glyphCount: 200);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DocumentErrorCodes.Font.Parse.InvalidValue);
    }
}
