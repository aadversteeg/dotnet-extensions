using System.Buffers.Binary;
using System.Text;
using Ave.Extensions.Documents.Fonts;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.Documents.Typesetting;

namespace UnitTests.Extensions.Documents.Typesetting;

/// <summary>
/// The old 'kern' table path against an oracle: values read from Theano Didot's 'kern' table by hand with fontTools
/// 4.68.0 (format 0, 6314 pairs: A V −284, T o −108, A T −180, Y o −204; hmtx A 1534, T 1564, Y 1462). The reference
/// HarfBuzz build cannot serve here: Theano also has GPOS kerning, which wins over the table in both shapers, so the
/// test removes GPOS from a copy of the font and the table is what is left.
/// </summary>
public class LegacyKerningOracleTests
{
    private static readonly TrueTypeFont WithoutGpos = FontParser.Parse(WithoutTable(TestFonts.Bytes("TheanoDidot-Regular.ttf"), "GPOS")).Value;

    /// <summary>A copy of a font file without one table (the directory rebuilt; checksums are not checked).</summary>
    private static byte[] WithoutTable(byte[] font, string tag)
    {
        int count = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(4));
        var tables = Enumerable.Range(0, count)
            .Select(index => 12 + index * 16)
            .Select(record => (Tag: Encoding.ASCII.GetString(font, record, 4), Offset: (int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(record + 8)), Length: (int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(record + 12))))
            .Where(table => table.Tag != tag)
            .ToList();
        var header = 12 + tables.Count * 16;
        var file = new byte[header + tables.Sum(table => (table.Length + 3) / 4 * 4)];
        BinaryPrimitives.WriteUInt32BigEndian(file, 0x00010000);
        BinaryPrimitives.WriteUInt16BigEndian(file.AsSpan(4), (ushort)tables.Count);
        var position = header;
        for (var index = 0; index < tables.Count; index++)
        {
            var record = 12 + index * 16;
            Encoding.ASCII.GetBytes(tables[index].Tag).CopyTo(file, record);
            BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(record + 8), (uint)position);
            BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(record + 12), (uint)tables[index].Length);
            font.AsSpan(tables[index].Offset, tables[index].Length).CopyTo(file.AsSpan(position));
            position += (tables[index].Length + 3) / 4 * 4;
        }

        return file;
    }

    [Theory]
    [InlineData("AV", 1534 - 284)]
    [InlineData("To", 1564 - 108)]
    [InlineData("AT", 1534 - 180)]
    [InlineData("Yo", 1462 - 204)]
    public void The_kern_table_value_is_added_to_the_first_glyph_s_advance(string pair, int expected)
    {
        WithoutGpos.Layout.Positions.Lookups.Should().BeEmpty("the copy has no GPOS, so the 'kern' table applies");

        var glyphs = Shaper.Shape(pair, WithoutGpos, fontSize: WithoutGpos.Metrics.UnitsPerEm).Value;

        glyphs[0].Advance.Should().Be(expected);
    }

    [Fact]
    public void A_pair_the_table_does_not_list_keeps_its_advances()
    {
        var glyphs = Shaper.Shape("AA", WithoutGpos, fontSize: WithoutGpos.Metrics.UnitsPerEm).Value;

        glyphs.Select(glyph => glyph.Advance).Should().Equal(1534, 1534);
    }
}
