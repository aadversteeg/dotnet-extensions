using System.Buffers.Binary;
using System.Text;
using Ave.Documents.Fonts;
using Ave.Documents.Fonts.OpenType;
using Ave.Documents.TestSupport;

namespace UnitTests.Ave.Documents.Fonts;

/// <summary>
/// Hand-made GSUB and GPOS tables that are malformed in ways a random byte change rarely finds: an offset near the
/// end of the 32-bit range, counts whose records take no bytes, and records that read nothing shared by many subtables.
/// A malformed font gives an error value, never an exception, and the parsed form of a table is bounded by its size:
/// every record created is charged to the work budget of <see cref="TableReader.WorkPerByte"/> units per byte.
/// </summary>
public class MalformedLayoutTableTests
{
    private static readonly byte[] Lato = TestFonts.Bytes("Lato-Regular.ttf");

    /// <summary>Rebuilds a font with one table replaced or added (checksums are not checked by the parser).</summary>
    internal static byte[] ReplaceTable(byte[] font, string tag, byte[] data)
    {
        int count = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(4));
        var tables = new List<(string Tag, byte[] Data)>();
        for (var index = 0; index < count; index++)
        {
            var record = 12 + index * 16;
            var name = Encoding.ASCII.GetString(font, record, 4);
            var offset = (int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(record + 8));
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(record + 12));
            tables.Add((name, name == tag ? data : font.AsSpan(offset, length).ToArray()));
        }

        if (tables.All(table => table.Tag != tag))
        {
            tables.Add((tag, data));
            tables.Sort((left, right) => string.CompareOrdinal(left.Tag, right.Tag));
        }

        var file = new BigEndianBuilder().UInt32(0x00010000).UInt16(tables.Count).UInt16(0).UInt16(0).UInt16(0);
        var position = 12 + tables.Count * 16;
        foreach (var (name, bytes) in tables)
        {
            file.Bytes(Encoding.ASCII.GetBytes(name)).UInt32(0).UInt32(position).UInt32(bytes.Length);
            position += (bytes.Length + 3) / 4 * 4;
        }

        foreach (var (_, bytes) in tables)
        {
            file.Bytes(bytes).Bytes(new byte[(4 - bytes.Length % 4) % 4]);
        }

        return file.ToArray();
    }

    /// <summary>A GSUB or GPOS table with no scripts, no features and one lookup of one subtable (at offset 26).</summary>
    internal static byte[] LayoutTableWithOneSubtable(int lookupType, byte[] subtable) =>
        new BigEndianBuilder()
            .UInt16(1).UInt16(0).UInt16(10).UInt16(12).UInt16(14)
            .UInt16(0)
            .UInt16(0)
            .UInt16(1).UInt16(4)
            .UInt16(lookupType).UInt16(0).UInt16(1).UInt16(8)
            .Bytes(subtable)
            .ToArray();

    /// <summary>Pair adjustment format 2 with both value formats 0 (so every class record is zero bytes long), an
    /// empty coverage and empty class definitions.</summary>
    private static byte[] PairByClassWithEmptyRecords(int class1Count, int class2Count) =>
        new BigEndianBuilder()
            .UInt16(2).UInt16(16).UInt16(0).UInt16(0)
            .UInt16(20).UInt16(26)
            .UInt16(class1Count).UInt16(class2Count)
            .UInt16(1).UInt16(0)
            .UInt16(1).UInt16(0).UInt16(0)
            .UInt16(1).UInt16(0).UInt16(0)
            .ToArray();

    /// <summary>
    /// A GPOS table of one pair-adjustment lookup with <paramref name="subtables"/> subtable offsets that all point at
    /// one class-pair subtable (format 2) of <paramref name="classes"/> × <paramref name="classes"/> records whose
    /// valueFormat1 holds only an X placement device offset (0x0010): two bytes a record, and no value read, because
    /// device offsets are skipped. The coverage (format 1, no glyphs) is read from the first record's bytes.
    /// </summary>
    private static byte[] SharedDeviceOnlyClassPairs(int subtables, int classes)
    {
        const int Lookup = 18;
        var subtable = Lookup + 6 + subtables * 2;
        var builder = new BigEndianBuilder()
            .UInt16(1).UInt16(0).UInt16(10).UInt16(12).UInt16(14) // version 1.0, script, feature and lookup lists
            .UInt16(0)                                              // no scripts
            .UInt16(0)                                              // no features
            .UInt16(1).UInt16(Lookup - 14)                          // one lookup
            .UInt16(2).UInt16(0).UInt16(subtables);                 // pair adjustment, no flags, the subtables
        for (var index = 0; index < subtables; index++)
        {
            builder.UInt16(subtable - Lookup);
        }

        builder.UInt16(2).UInt16(16).UInt16(0x0010).UInt16(0).UInt16(0).UInt16(0).UInt16(classes).UInt16(classes);
        return builder.UInt16(1).Bytes(new byte[classes * classes * 2 - 2]).ToArray();
    }

    [Theory]
    [InlineData(50)]
    [InlineData(200)]
    public void Shared_class_pair_records_that_read_nothing_are_charged_as_records(int subtables)
    {
        // Each record makes a PairValues whatever it reads, so its creation is charged to the work budget: 50 offsets to
        // one subtable of 10 000 records would otherwise keep 500 000 records parsed from a 20 KB table.
        var table = SharedDeviceOnlyClassPairs(subtables, classes: 100);
        var allocated = GC.GetAllocatedBytesForCurrentThread();

        var result = LayoutTableParser.Parse(LayoutTableParser.Gpos, table, glyphCount: 100);

        (GC.GetAllocatedBytesForCurrentThread() - allocated).Should().BeLessThan(16L * 1024 * 1024);
        result.IsFailure.Should().BeTrue("the table asks for far more work than its size allows");
        result.Error.Code.Should().Be(FontErrorCodes.Parse.InvalidValue);
    }

    [Fact]
    public void One_subtable_of_class_pair_records_that_read_nothing_is_parsed()
    {
        var result = LayoutTableParser.Parse(LayoutTableParser.Gpos, SharedDeviceOnlyClassPairs(1, classes: 100), glyphCount: 100);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        ((PairAdjustmentByClass)result.Value.Lookups[0].Subtables[0]).Values.Should().HaveCount(100 * 100);
    }

    [Theory]
    [InlineData(0x7FFFFFFEL)]
    [InlineData(0x7FFFFFE4L)]
    [InlineData(0xFFFFFFF0L)]
    public void An_extension_subtable_pointing_near_the_end_of_the_32_bit_range_is_an_error(long target)
    {
        var extension = new BigEndianBuilder().UInt16(1).UInt16(1).UInt32(target - 26).ToArray();
        var font = ReplaceTable(Lato, "GSUB", LayoutTableWithOneSubtable(7, extension));

        var result = FontParser.Parse(font);

        result.IsFailure.Should().BeTrue();
        result.Error.Is(FontErrorCodes.Parse._).Should().BeTrue(result.Error.ToString());
    }

    [Theory]
    [InlineData(65535, 65535)]
    [InlineData(8000, 8000)]
    public void Class_pairs_whose_records_take_no_bytes_cost_no_memory(int class1Count, int class2Count)
    {
        var font = ReplaceTable(Lato, "GPOS", LayoutTableWithOneSubtable(2, PairByClassWithEmptyRecords(class1Count, class2Count)));
        var allocated = GC.GetAllocatedBytesForCurrentThread();

        var result = FontParser.Parse(font);

        (GC.GetAllocatedBytesForCurrentThread() - allocated).Should().BeLessThan(64L * 1024 * 1024, "the 58-byte table describes no values");
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        var subtable = (PairAdjustmentByClass)result.Value.Layout.Positions.Lookups[0].Subtables[0];
        subtable.Values.Should().BeEmpty("value records of zero bytes adjust nothing");
    }
}
