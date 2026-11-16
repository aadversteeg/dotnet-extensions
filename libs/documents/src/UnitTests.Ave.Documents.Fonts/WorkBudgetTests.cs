using Ave.Documents.Fonts;
using Ave.Documents.Fonts.OpenType;
using Ave.Documents.TestSupport;

namespace UnitTests.Ave.Documents.Fonts;

/// <summary>
/// The work budget of a layout table (<see cref="TableReader.WorkPerByte"/> units per byte, at least
/// <see cref="TableReader.MinimumWork"/>) must never refuse a valid font, and must stop a malformed one. Work is
/// charged per record read: a range record that covers thousands of glyphs is one record, as in the fonts of large
/// scripts (Malgun Gothic's GDEF is 30 bytes for 28 215 glyphs), and a coverage or class table that many subtables
/// point at is read once; walking such a shared table again for every subtable is charged too.
/// </summary>
public class WorkBudgetTests
{
    /// <summary>
    /// <paramref name="count"/> lookups of one single-substitution subtable (format 1), all pointing at ONE coverage
    /// table of <paramref name="glyphs"/> separate glyphs (0, 2, 4, …: one range each).
    /// </summary>
    private static byte[] SharedCoverage(int count, int glyphs, int delta = 0)
    {
        var builder = new BigEndianBuilder().UInt16(1).UInt16(0).UInt16(10).UInt16(12).UInt16(14).UInt16(0).UInt16(0).UInt16(count);
        var lookupStart = 2 + count * 2;
        for (var index = 0; index < count; index++)
        {
            builder.UInt16(lookupStart + index * 14);
        }

        var coverage = lookupStart + count * 14;
        for (var index = 0; index < count; index++)
        {
            var subtable = lookupStart + index * 14 + 8;
            builder.UInt16(1).UInt16(0).UInt16(1).UInt16(8)          // lookup: type 1, one subtable at +8
                .UInt16(1).UInt16(coverage - subtable).UInt16(delta); // subtable: format 1, the shared coverage, the delta
        }

        builder.UInt16(1).UInt16(glyphs);
        for (var glyph = 0; glyph < glyphs; glyph++)
        {
            builder.UInt16(glyph * 2);
        }

        return builder.ToArray();
    }

    [Fact]
    public void Many_subtables_sharing_a_coverage_whose_outputs_are_in_range_are_read_without_walking_it()
    {
        // 3000 subtables × 3000 ranges would be 9 million steps from a table of about 54 KB (budget about 430 000). The
        // outputs lie between the first and the last covered glyph, so each subtable is checked in one step, and a valid
        // font of this shape is never refused.
        var table = SharedCoverage(3000, 3000);
        (3000L * 3000).Should().BeGreaterThan((long)TableReader.WorkPerByte * table.Length);

        var result = LayoutTableParser.Parse("GSUB", table, glyphCount: 65535);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        result.Value.Lookups.Should().HaveCount(3000);
    }

    [Fact]
    public void Walking_a_shared_coverage_range_by_range_is_charged_to_the_budget()
    {
        // A delta of -2 makes glyph 0 wrap around to 65534, which a font of 65535 glyphs has: the one-step bound does
        // not hold, so every range is checked, for every subtable. That walk is charged, and 3000 of them exceed the
        // table's budget.
        var result = LayoutTableParser.Parse("GSUB", SharedCoverage(3000, 3000, delta: -2), glyphCount: 65535);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(FontErrorCodes.Parse.InvalidValue);
        result.Error.Message.Should().Contain("far more work than its size allows");
    }

    [Fact]
    public void A_wrapping_delta_in_a_few_subtables_is_read()
    {
        var result = LayoutTableParser.Parse("GSUB", SharedCoverage(3, 3000, delta: -2), glyphCount: 65535);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        ((SingleSubstitution)result.Value.Lookups[0].Subtables[0]).Replace(new GlyphId(0)).Should().Be(new GlyphId(65534));
    }

    [Fact]
    public void One_subtable_with_an_output_past_the_glyph_count_is_found()
    {
        var result = LayoutTableParser.Parse("GSUB", SharedCoverage(1, 3000), glyphCount: 5000);

        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("beyond maxp.numGlyphs");
    }

    [Fact]
    public void The_budget_is_eight_units_per_byte_and_at_least_16384()
    {
        TableReader.WorkPerByte.Should().Be(8);
        TableReader.MinimumWork.Should().Be(16_384);
    }

    [Fact]
    public void A_glyph_class_range_over_28215_glyphs_in_a_22_byte_GDEF_is_read()
    {
        var gdef = new BigEndianBuilder()
            .UInt16(1).UInt16(0).UInt16(12).UInt16(0).UInt16(0).UInt16(0)   // version 1.0, glyphClassDef at 12
            .UInt16(2).UInt16(1).UInt16(0).UInt16(28214).UInt16(1)          // format 2, one range 0–28214, class 1
            .ToArray();

        var result = GlyphDefinitions.Parse(gdef);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        result.Value.GlyphClasses!.ClassOf(new GlyphId(28000)).Should().Be(1);
        result.Value.GlyphClasses!.ClassOf(new GlyphId(28215)).Should().Be(0);
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
