using System.Collections.Immutable;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Fonts;
using Ave.Extensions.Documents.Fonts.OpenType;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.Documents.Typesetting.Shaping;

namespace UnitTests.Extensions.Documents.Typesetting;

/// <summary>
/// Malformed input never buys unbounded work: one-to-many substitutions that feed each other are stopped at
/// <see cref="GlyphSubstitution.MaxGlyphs"/>, and a language system that lists one feature many times walks that
/// feature's lookups once. Both are counted (glyphs, features), not timed.
/// </summary>
public class GlyphGrowthTests
{
    private const int Glyph = 5;

    /// <summary>A GSUB of <paramref name="count"/> lookups, each a multiple substitution "glyph 5 → glyph 5, glyph 5".</summary>
    private static LayoutTable Doubling(int count)
    {
        var builder = new BigEndianBuilder().UInt16(1).UInt16(0).UInt16(10).UInt16(12).UInt16(14).UInt16(0).UInt16(0).UInt16(count);
        var lookupStart = 2 + count * 2;
        for (var index = 0; index < count; index++)
        {
            builder.UInt16(lookupStart + index * 28);
        }

        for (var index = 0; index < count; index++)
        {
            builder.UInt16(2).UInt16(0).UInt16(1).UInt16(8)   // lookup: type 2, flags 0, one subtable at +8
                .UInt16(1).UInt16(14).UInt16(1).UInt16(8)     // subtable: format 1, coverage at +14, one sequence at +8
                .UInt16(2).UInt16(Glyph).UInt16(Glyph)        // the sequence: two glyphs
                .UInt16(1).UInt16(1).UInt16(Glyph);           // coverage format 1: glyph 5
        }

        var parsed = LayoutTableParser.Parse("GSUB", builder.ToArray(), glyphCount: 100);
        parsed.IsSuccess.Should().BeTrue(parsed.IsFailure ? parsed.Error.ToString() : string.Empty);
        return parsed.Value;
    }

    private static ImmutableArray<GlyphItem> One => [new GlyphItem(new GlyphId(Glyph), "x", GlyphClass.Base)];

    [Fact]
    public void Ten_doubling_lookups_give_1024_glyphs()
    {
        var result = GlyphSubstitution.Apply(One, Doubling(10).Lookups, new GlyphFilter(null), new ShapingBudget(1));

        result.Should().NotBeNull();
        result!.Value.Should().HaveCount(1024);
    }

    [Fact]
    public void Sixteen_doubling_lookups_stop_at_the_limit_instead_of_making_65536_glyphs()
    {
        GlyphSubstitution.MaxGlyphs(1).Should().Be(GlyphSubstitution.MinGlyphLimit);

        var budget = new ShapingBudget(1);

        var result = GlyphSubstitution.Apply(One, Doubling(16).Lookups, new GlyphFilter(null), budget);

        result.Should().BeNull("2^16 glyphs from one is past the limit of 16 384");
        budget.Exceeded.Should().Be(ShapingLimit.Glyphs);
    }

    [Fact]
    public void The_limit_grows_with_the_text()
    {
        GlyphSubstitution.MaxGlyphs(1000).Should().Be(64_000);
        GlyphSubstitution.MaxGlyphs(int.MaxValue).Should().Be(64L * int.MaxValue, "computed in long");
    }

    [Fact]
    public void A_feature_listed_8000_times_is_walked_once()
    {
        // One feature with 8000 lookups, listed 8000 times by the default language system: 64 million lookup indices
        // to walk if every listing is followed.
        ValueList<Lookup> lookups = [.. Enumerable.Range(0, 8000).Select(index => new Lookup(index, new LookupFlags(0), []))];
        var table = new LayoutTable(
            [new ScriptTable("DFLT", new LanguageSystem(null, [.. Enumerable.Repeat(0, 8000)]))],
            [new FeatureTable("liga", [.. Enumerable.Range(0, 8000)])],
            lookups);

        table.SelectedFeatures("latn", new HashSet<string> { "liga" }).Should().Equal([0]);
        table.LookupsFor("latn", new HashSet<string> { "liga" }).Should().HaveCount(8000);
    }
}
