using System.Text;
using Ave.Documents.Fonts;
using Ave.Documents.TestSupport;
using Ave.Documents.Typesetting;

namespace UnitTests.Ave.Documents.Typesetting;

/// <summary>
/// Shaping one text has two limits, both counted over the whole text: the glyphs its substitutions may make, and the
/// operations its lookups may spend (every glyph a lookup looks at). A font whose lookups feed each other or repeat
/// reaches one of them and the text is refused; splitting the text at forced line breaks does not multiply them.
/// Counted (glyphs, error codes, bytes allocated), not timed.
/// </summary>
public class ShapingBudgetTests
{
    private static readonly byte[] Lato = TestFonts.Bytes("Lato-Regular.ttf");

    private static readonly ushort A = TestFonts.Regular.GlyphFor(new Rune('a'))!.Value.Value;

    /// <summary>
    /// A GSUB with one script (DFLT) and one feature (liga) that applies, in order, <paramref name="doubling"/> lookups
    /// "a → a a" and then <paramref name="identity"/> lookups "a → a" (single substitution, delta 0). The identity lookups
    /// are as many offsets in the lookup list to ONE lookup table, as a malformed font may have. With
    /// <paramref name="withoutPositioning"/> the font's GPOS is replaced by one without lookups, so that the GSUB alone
    /// does the work.
    /// </summary>
    private static TrueTypeFont FontWith(int doubling, int identity, bool withoutPositioning = false)
    {
        var lookups = doubling + identity;
        const int ScriptList = 10, Script = 18, FeatureList = 30, Feature = 38;
        var lookupList = Feature + 4 + 2 * lookups;
        var firstTable = 2 + 2 * lookups;
        const int DoublingLength = 28;
        var gsub = new BigEndianBuilder()
            .UInt16(1).UInt16(0).UInt16(ScriptList).UInt16(FeatureList).UInt16(lookupList)
            .UInt16(1).Bytes("DFLT"u8.ToArray()).UInt16(Script - ScriptList)       // one script, DFLT
            .UInt16(4).UInt16(0)                                                   // its default language system at +4
            .UInt16(0).UInt16(0xFFFF).UInt16(1).UInt16(0)                          // no required feature; feature 0
            .UInt16(1).Bytes("liga"u8.ToArray()).UInt16(Feature - FeatureList)     // one feature, liga
            .UInt16(0).UInt16(lookups);                                            // every lookup, in order
        for (var index = 0; index < lookups; index++)
        {
            gsub.UInt16(index);
        }

        gsub.UInt16(lookups);
        for (var index = 0; index < lookups; index++)
        {
            gsub.UInt16(index < doubling ? firstTable + index * DoublingLength : firstTable + doubling * DoublingLength);
        }

        for (var index = 0; index < doubling; index++)
        {
            gsub.UInt16(2).UInt16(0).UInt16(1).UInt16(8)   // lookup: type 2, one subtable at +8
                .UInt16(1).UInt16(14).UInt16(1).UInt16(8)  // multiple substitution: coverage at +14, one sequence at +8
                .UInt16(2).UInt16(A).UInt16(A)             // the sequence: a a
                .UInt16(1).UInt16(1).UInt16(A);            // coverage: a
        }

        gsub.UInt16(1).UInt16(0).UInt16(1).UInt16(8)       // lookup: type 1, one subtable at +8
            .UInt16(1).UInt16(6).UInt16(0)                 // single substitution format 1: coverage at +6, delta 0
            .UInt16(1).UInt16(1).UInt16(A);                // coverage: a

        var file = FontFiles.WithTable(Lato, "GSUB", gsub.ToArray());
        var emptyGpos = new BigEndianBuilder().UInt16(1).UInt16(0).UInt16(10).UInt16(12).UInt16(14).UInt16(0).UInt16(0).UInt16(0).ToArray();
        var font = FontParser.Parse(withoutPositioning ? FontFiles.WithTable(file, "GPOS", emptyGpos) : file);
        font.IsSuccess.Should().BeTrue(font.IsFailure ? font.Error.ToString() : string.Empty);
        return font.Value;
    }

    [Fact]
    public void The_test_font_doubles_as_built()
    {
        var shaped = Shaper.Shape("a", FontWith(doubling: 3, identity: 2), 10);

        shaped.IsSuccess.Should().BeTrue(shaped.IsFailure ? shaped.Error.ToString() : string.Empty);
        shaped.Value.Should().HaveCount(8).And.OnlyContain(glyph => glyph.Glyph.Value == A);
    }

    [Fact]
    public void Repeated_lookups_over_a_grown_buffer_stop_at_the_operations_budget()
    {
        // 14 doubling lookups make 16 384 glyphs from one, inside the glyph limit; each of the 100 identity lookups after
        // them then walks all 16 384. That work, not the glyph count, is what the operations budget stops.
        var font = FontWith(doubling: 14, identity: 100);
        var allocated = GC.GetAllocatedBytesForCurrentThread();

        var shaped = Shaper.Shape("a", font, 10);

        (GC.GetAllocatedBytesForCurrentThread() - allocated).Should().BeLessThan(64L * 1024 * 1024);
        shaped.IsFailure.Should().BeTrue("one letter may not cost 1.6 million operations");
        shaped.Error.Code.Should().Be(TypesettingErrorCodes.TooMuchWork);
        shaped.Error.Metadata![global::Ave.Documents.Core.ErrorMetadataKeys.FontName].Should().Be(font.Names.PostScriptName);
    }

    [Fact]
    public void A_text_may_grow_to_exactly_its_glyph_limit()
    {
        // 14 doubling lookups make 16 384 glyphs of one: the limit of a short text itself, which is allowed.
        var shaped = Shaper.Shape("a", FontWith(doubling: 14, identity: 0, withoutPositioning: true), 10);

        shaped.IsSuccess.Should().BeTrue(shaped.IsFailure ? shaped.Error.ToString() : string.Empty);
        shaped.Value.Should().HaveCount(16_384);
    }

    [Fact]
    public void A_missing_glyph_names_its_code_point_with_four_hex_digits()
    {
        // U+0E01 THAI CHARACTER KO KAI, which Lato does not have.
        var shaped = Shaper.Shape("\u0E01", TestFonts.Regular, 10);

        shaped.Error.Code.Should().Be(FontErrorCodes.GlyphMissing);
        shaped.Error.Metadata![global::Ave.Documents.Core.ErrorMetadataKeys.CodePoint].Should().Be("U+0E01");
    }

    [Fact]
    public void The_glyph_limit_holds_for_the_whole_text_across_forced_line_breaks()
    {
        // 13 doubling lookups make 8 192 glyphs of one a. Twenty a's in a row (163 840 glyphs) are past the limit of the
        // text; the same twenty separated by line breaks are shaped piece by piece, and still past it. Without GPOS the
        // glyph limit, not the operations limit, is the one the text reaches.
        var font = FontWith(doubling: 13, identity: 0, withoutPositioning: true);

        Shaper.Shape(new string('a', 20), font, 10).Error.Code.Should().Be(TypesettingErrorCodes.TooManyGlyphs);
        Shaper.Shape(string.Join("\u2028", Enumerable.Repeat("a", 20)), font, 10).Error.Code.Should().Be(TypesettingErrorCodes.TooManyGlyphs);
        Shaper.Shape("a", font, 10).Value.Should().HaveCount(8192, "one piece alone is inside the limit");
    }
}
