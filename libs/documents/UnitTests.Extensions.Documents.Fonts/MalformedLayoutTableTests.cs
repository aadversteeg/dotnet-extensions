using Ave.Extensions.Documents.Fonts;
using Ave.Extensions.Documents.Fonts.OpenType;
using Ave.Extensions.Documents.TestSupport;

namespace UnitTests.Extensions.Documents.Fonts;

/// <summary>
/// Hand-made GSUB and GPOS tables that are malformed in ways a random byte change rarely finds: an offset near the
/// end of the 32-bit range, counts whose records take no bytes, and records that read nothing shared by many subtables.
/// A malformed font gives an error value, never an exception, and the parsed form of a table is bounded by its size:
/// every record created is charged to the work budget of <see cref="TableReader.WorkPerByte"/> units per byte.
/// </summary>
public class MalformedLayoutTableTests
{
    private static readonly byte[] Lato = TestFonts.Bytes("Lato-Regular.ttf");

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

    /// <summary>A single substitution, format 1, of one coverage range with a delta.</summary>
    private static byte[] SingleSubstitution(int first, int last, int delta) =>
        LayoutTableWithOneSubtable(1, new BigEndianBuilder()
            .UInt16(1).UInt16(6).UInt16(delta)               // format 1, coverage at +6, deltaGlyphID
            .UInt16(2).UInt16(1).UInt16(first).UInt16(last).UInt16(0)  // coverage format 2: one range
            .ToArray());

    [Fact]
    public void A_delta_that_wraps_below_zero_gives_glyphs_modulo_65536_and_is_checked_against_the_glyph_count()
    {
        // "Add to original glyph ID to get substitute glyph ID", modulo 65536: glyph 100 with delta -200 is 65 436,
        // beyond a font of 1 000 glyphs.
        var result = LayoutTableParser.Parse(LayoutTableParser.Gsub, SingleSubstitution(100, 100, -200), glyphCount: 1000);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(FontErrorCodes.Parse.InvalidValue);
    }

    [Theory]
    [InlineData(98, true)]
    [InlineData(99, false)]
    public void The_last_covered_glyph_plus_the_delta_must_be_a_glyph_of_the_font(int last, bool valid)
    {
        // A font of 100 glyphs, ids 0 to 99: 98 + 1 is its last glyph, 99 + 1 is none.
        var result = LayoutTableParser.Parse(LayoutTableParser.Gsub, SingleSubstitution(10, last, 1), glyphCount: 100);

        result.IsSuccess.Should().Be(valid, result.IsFailure ? result.Error.ToString() : string.Empty);
    }

    [Fact]
    public void A_GDEF_with_a_glyph_class_definition_has_glyph_classes()
    {
        // Theano Didot's GDEF has a glyph class definition (glyphClassDefOffset is not 0); Lato has no GDEF at all.
        TestFonts.Regular.Layout.Definitions.Should().BeNull();
        TestFonts.TheanoDidot.Layout.Definitions.Should().NotBeNull();
        TestFonts.TheanoDidot.Layout.Definitions!.HasGlyphClasses.Should().BeTrue();
    }

    /// <summary>
    /// A GPOS table of one pair-adjustment lookup with <paramref name="subtables"/> subtable offsets that all point at
    /// one glyph-pair subtable (format 1) whose one pair set holds <paramref name="pairs"/> records of a second glyph and
    /// a value record of only an X placement device offset (0x0010): four bytes a record, one value read (the second
    /// glyph), because device offsets are skipped. The coverage (format 1) has no glyphs.
    /// </summary>
    private static byte[] SharedDeviceOnlyGlyphPairs(int subtables, int pairs)
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

        builder.UInt16(1).UInt16(12).UInt16(0x0010).UInt16(0).UInt16(1).UInt16(16); // format 1, coverage, formats, one set
        builder.UInt16(1).UInt16(0);                                                  // the coverage: no glyphs
        return builder.UInt16(pairs).Bytes(new byte[pairs * 4]).ToArray();
    }

    [Theory]
    [InlineData(12, true)]
    [InlineData(24, false)]
    public void A_record_counted_by_its_array_costs_one_unit_on_top_of_what_it_reads(int subtables, bool parses)
    {
        // The table is about 8 KB, so its budget is about 64 500 units. Each subtable walks the 2 000 records of the
        // shared pair set: one read and one record each, 4 000 units. Twelve subtables (48 000) fit; twenty-four
        // (96 000) do not, and would fit (48 000) if only the reads were charged.
        var result = LayoutTableParser.Parse(LayoutTableParser.Gpos, SharedDeviceOnlyGlyphPairs(subtables, pairs: 2_000), glyphCount: 100);

        result.IsSuccess.Should().Be(parses, result.IsFailure ? result.Error.ToString() : "the table parsed");
        if (!parses)
        {
            result.Error.Code.Should().Be(FontErrorCodes.Parse.InvalidValue);
        }
    }

    [Theory]
    [InlineData(0x7FFFFFFEL)]
    [InlineData(0x7FFFFFE4L)]
    [InlineData(0xFFFFFFF0L)]
    public void An_extension_subtable_pointing_near_the_end_of_the_32_bit_range_is_an_error(long target)
    {
        var extension = new BigEndianBuilder().UInt16(1).UInt16(1).UInt32(target - 26).ToArray();
        var font = FontFiles.WithTable(Lato, "GSUB", LayoutTableWithOneSubtable(7, extension));

        var result = FontParser.Parse(font);

        result.IsFailure.Should().BeTrue();
        result.Error.Is(FontErrorCodes.Parse._).Should().BeTrue(result.Error.ToString());
    }

    [Theory]
    [InlineData(65535, 65535)]
    [InlineData(8000, 8000)]
    public void Class_pairs_whose_records_take_no_bytes_cost_no_memory(int class1Count, int class2Count)
    {
        var font = FontFiles.WithTable(Lato, "GPOS", LayoutTableWithOneSubtable(2, PairByClassWithEmptyRecords(class1Count, class2Count)));
        var allocated = GC.GetAllocatedBytesForCurrentThread();

        var result = FontParser.Parse(font);

        (GC.GetAllocatedBytesForCurrentThread() - allocated).Should().BeLessThan(64L * 1024 * 1024, "the 58-byte table describes no values");
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        var subtable = (PairAdjustmentByClass)result.Value.Layout.Positions.Lookups[0].Subtables[0];
        subtable.Values.Should().BeEmpty("value records of zero bytes adjust nothing");
    }
}
