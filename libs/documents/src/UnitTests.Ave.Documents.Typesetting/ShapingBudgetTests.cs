using System.Text;
using Ave.Documents.Fonts;
using Ave.Documents.TestSupport;
using Ave.Documents.Typesetting;
using Ave.Documents.Typesetting.Shaping;

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

    /// <summary>
    /// Lato with a GPOS of one script (DFLT) and one feature (kern) whose one lookup holds <paramref name="subtables"/>
    /// offsets to ONE pair-adjustment subtable (format 1) that covers no glyph. Each subtable first looks for the second
    /// glyph of a pair, stepping over the invisible characters after the first, and only then finds no pair.
    /// </summary>
    private static TrueTypeFont FontWithRepeatedPairSubtables(int subtables)
    {
        const int ScriptList = 10, Script = 18, FeatureList = 30, Feature = 38, LookupList = 44, Lookup = 48;
        var gpos = new BigEndianBuilder()
            .UInt16(1).UInt16(0).UInt16(ScriptList).UInt16(FeatureList).UInt16(LookupList)
            .UInt16(1).Bytes("DFLT"u8.ToArray()).UInt16(Script - ScriptList)       // one script, DFLT
            .UInt16(4).UInt16(0)                                                   // its default language system at +4
            .UInt16(0).UInt16(0xFFFF).UInt16(1).UInt16(0)                          // no required feature; feature 0
            .UInt16(1).Bytes("kern"u8.ToArray()).UInt16(Feature - FeatureList)     // one feature, kern
            .UInt16(0).UInt16(1).UInt16(0)                                         // lookup 0
            .UInt16(1).UInt16(Lookup - LookupList)                                 // one lookup
            .UInt16(2).UInt16(0).UInt16(subtables);                                // pair adjustment, no flags
        for (var index = 0; index < subtables; index++)
        {
            gpos.UInt16(6 + 2 * subtables);
        }

        gpos.UInt16(1).UInt16(10).UInt16(0x0004).UInt16(0).UInt16(0)               // format 1, coverage at +10, no sets
            .UInt16(1).UInt16(0);                                                  // coverage: no glyphs

        var font = FontParser.Parse(FontFiles.WithTable(Lato, "GPOS", gpos.ToArray()));
        font.IsSuccess.Should().BeTrue(font.IsFailure ? font.Error.ToString() : string.Empty);
        return font.Value;
    }

    [Fact]
    public void Each_glyph_a_search_for_a_pair_s_second_glyph_steps_over_is_an_operation()
    {
        // One a and 1 000 word joiners (U+2060, default ignorable, skipped in positioning): a budget of 1 024 × 1 001
        // operations. Each of the 2 000 subtables steps over the 1 000 joiners after the a before it finds no pair: two
        // million operations, though the walk over the buffer itself costs only 1 001.
        var text = "a" + new string((char)0x2060, 1_000);

        Shaper.Shape(text, FontWithRepeatedPairSubtables(1), 10).IsSuccess.Should().BeTrue("one subtable steps over 1 000");
        var shaped = Shaper.Shape(text, FontWithRepeatedPairSubtables(2_000), 10);

        shaped.IsFailure.Should().BeTrue("the searches cost two million operations");
        shaped.Error.Code.Should().Be(TypesettingErrorCodes.TooMuchWork);
    }

    /// <summary>
    /// Lato with a GSUB of one script (DFLT) and one feature (liga) whose one lookup holds <paramref name="subtables"/>
    /// offsets to ONE single substitution (format 1) that covers no glyph, and a GPOS without lookups. Every subtable
    /// looks up its coverage at every position and finds nothing.
    /// </summary>
    private static TrueTypeFont FontWithUncoveringSubtables(int subtables)
    {
        const int ScriptList = 10, Script = 18, FeatureList = 30, Feature = 38, LookupList = 44, Lookup = 48;
        var gsub = new BigEndianBuilder()
            .UInt16(1).UInt16(0).UInt16(ScriptList).UInt16(FeatureList).UInt16(LookupList)
            .UInt16(1).Bytes("DFLT"u8.ToArray()).UInt16(Script - ScriptList)       // one script, DFLT
            .UInt16(4).UInt16(0)                                                   // its default language system at +4
            .UInt16(0).UInt16(0xFFFF).UInt16(1).UInt16(0)                          // no required feature; feature 0
            .UInt16(1).Bytes("liga"u8.ToArray()).UInt16(Feature - FeatureList)     // one feature, liga
            .UInt16(0).UInt16(1).UInt16(0)                                         // lookup 0
            .UInt16(1).UInt16(Lookup - LookupList)                                 // one lookup
            .UInt16(1).UInt16(0).UInt16(subtables);                                // single substitution, no flags
        for (var index = 0; index < subtables; index++)
        {
            gsub.UInt16(6 + 2 * subtables);
        }

        gsub.UInt16(1).UInt16(6).UInt16(0)                                         // format 1: coverage at +6, delta 0
            .UInt16(1).UInt16(0);                                                  // coverage: no glyphs

        var emptyGpos = new BigEndianBuilder().UInt16(1).UInt16(0).UInt16(10).UInt16(12).UInt16(14).UInt16(0).UInt16(0).UInt16(0).ToArray();
        var font = FontParser.Parse(FontFiles.WithTable(FontFiles.WithTable(Lato, "GSUB", gsub.ToArray()), "GPOS", emptyGpos));
        font.IsSuccess.Should().BeTrue(font.IsFailure ? font.Error.ToString() : string.Empty);
        return font.Value;
    }

    /// <summary>
    /// Lato with its GSUB and a GPOS of one script (DFLT) and one feature (mark) whose one lookup holds
    /// <paramref name="subtables"/> offsets to ONE mark-to-base subtable that covers no mark and no base.
    /// </summary>
    private static TrueTypeFont FontWithUncoveringPositioningSubtables(int subtables)
    {
        const int ScriptList = 10, Script = 18, FeatureList = 30, Feature = 38, LookupList = 44, Lookup = 48;
        var gpos = new BigEndianBuilder()
            .UInt16(1).UInt16(0).UInt16(ScriptList).UInt16(FeatureList).UInt16(LookupList)
            .UInt16(1).Bytes("DFLT"u8.ToArray()).UInt16(Script - ScriptList)       // one script, DFLT
            .UInt16(4).UInt16(0)                                                   // its default language system at +4
            .UInt16(0).UInt16(0xFFFF).UInt16(1).UInt16(0)                          // no required feature; feature 0
            .UInt16(1).Bytes("mark"u8.ToArray()).UInt16(Feature - FeatureList)     // one feature, mark
            .UInt16(0).UInt16(1).UInt16(0)                                         // lookup 0
            .UInt16(1).UInt16(Lookup - LookupList)                                 // one lookup
            .UInt16(4).UInt16(0).UInt16(subtables);                                // mark-to-base, no flags
        for (var index = 0; index < subtables; index++)
        {
            gpos.UInt16(6 + 2 * subtables);
        }

        gpos.UInt16(1).UInt16(12).UInt16(16).UInt16(1).UInt16(20).UInt16(22)     // format 1: coverages, 1 class, arrays
            .UInt16(1).UInt16(0)                                                   // mark coverage: no glyphs
            .UInt16(1).UInt16(0)                                                   // base coverage: no glyphs
            .UInt16(0)                                                             // mark array: no marks
            .UInt16(0);                                                            // base array: no bases

        var font = FontParser.Parse(FontFiles.WithTable(Lato, "GPOS", gpos.ToArray()));
        font.IsSuccess.Should().BeTrue(font.IsFailure ? font.Error.ToString() : string.Empty);
        return font.Value;
    }

    [Fact]
    public void Each_positioning_subtable_a_lookup_tries_is_an_operation_whether_it_covers_the_glyph_or_not()
    {
        // As for substitution: 100 a's, 2 000 mark-to-base subtables that cover nothing, 200 000 coverage lookups.
        var text = new string('a', 100);

        Shaper.Shape(text, FontWithUncoveringPositioningSubtables(1), 10).IsSuccess.Should().BeTrue();
        var shaped = Shaper.Shape(text, FontWithUncoveringPositioningSubtables(2_000), 10);

        shaped.IsFailure.Should().BeTrue("2 000 subtables at 100 positions cost 200 000 operations");
        shaped.Error.Code.Should().Be(TypesettingErrorCodes.TooMuchWork);
    }

    [Fact]
    public void Each_subtable_a_lookup_tries_is_an_operation_whether_it_covers_the_glyph_or_not()
    {
        // 100 a's: a budget of 102 400 operations. One subtable costs 100 positions and 100 coverage lookups; 2 000
        // subtables cost 100 positions and 200 000 coverage lookups, although none of them covers a glyph.
        var text = new string('a', 100);

        Shaper.Shape(text, FontWithUncoveringSubtables(1), 10).IsSuccess.Should().BeTrue();
        var shaped = Shaper.Shape(text, FontWithUncoveringSubtables(2_000), 10);

        shaped.IsFailure.Should().BeTrue("2 000 subtables at 100 positions cost 200 000 operations");
        shaped.Error.Code.Should().Be(TypesettingErrorCodes.TooMuchWork);
    }

    /// <summary>
    /// Lato with a GSUB of one script (DFLT) and one feature (liga) whose one lookup, ignoring marks, holds
    /// <paramref name="subtables"/> offsets to ONE ligature substitution "a b → a". Every subtable looks for the b after
    /// an a, stepping over the marks between them.
    /// </summary>
    private static TrueTypeFont FontWithRepeatedLigatureSubtables(int subtables)
    {
        const int ScriptList = 10, Script = 18, FeatureList = 30, Feature = 38, LookupList = 44, Lookup = 48;
        var b = TestFonts.Regular.GlyphFor(new Rune('b'))!.Value.Value;
        var gsub = new BigEndianBuilder()
            .UInt16(1).UInt16(0).UInt16(ScriptList).UInt16(FeatureList).UInt16(LookupList)
            .UInt16(1).Bytes("DFLT"u8.ToArray()).UInt16(Script - ScriptList)       // one script, DFLT
            .UInt16(4).UInt16(0)                                                   // its default language system at +4
            .UInt16(0).UInt16(0xFFFF).UInt16(1).UInt16(0)                          // no required feature; feature 0
            .UInt16(1).Bytes("liga"u8.ToArray()).UInt16(Feature - FeatureList)     // one feature, liga
            .UInt16(0).UInt16(1).UInt16(0)                                         // lookup 0
            .UInt16(1).UInt16(Lookup - LookupList)                                 // one lookup
            .UInt16(4).UInt16(0x0008).UInt16(subtables);                           // ligature substitution, ignore marks
        for (var index = 0; index < subtables; index++)
        {
            gsub.UInt16(6 + 2 * subtables);
        }

        gsub.UInt16(1).UInt16(18).UInt16(1).UInt16(8)                              // format 1: coverage at +18, one set at +8
            .UInt16(1).UInt16(4)                                                   // the set: one ligature at +4
            .UInt16(A).UInt16(2).UInt16(b)                                         // the ligature: a, from a b
            .UInt16(1).UInt16(1).UInt16(A);                                        // coverage: a

        var emptyGpos = new BigEndianBuilder().UInt16(1).UInt16(0).UInt16(10).UInt16(12).UInt16(14).UInt16(0).UInt16(0).UInt16(0).ToArray();
        var font = FontParser.Parse(FontFiles.WithTable(FontFiles.WithTable(Lato, "GSUB", gsub.ToArray()), "GPOS", emptyGpos));
        font.IsSuccess.Should().BeTrue(font.IsFailure ? font.Error.ToString() : string.Empty);
        return font.Value;
    }

    [Fact]
    public void Each_glyph_a_search_for_a_ligature_s_next_component_steps_over_is_an_operation()
    {
        // An a, 1 000 combining marks and another a: a budget of 1 024 x 1 002 operations. Each of the 1 100 subtables
        // steps over the 1 000 marks after the first a before it finds an a, not the b of its ligature: 1.1 million
        // operations, though the walk and the subtables tried cost only some 3 000.
        var text = "a" + string.Concat(Enumerable.Repeat(char.ConvertFromUtf32(0x0302), 1_000)) + "a";

        Shaper.Shape(text, FontWithRepeatedLigatureSubtables(1), 10).IsSuccess.Should().BeTrue("one subtable steps over 1 000");
        var shaped = Shaper.Shape(text, FontWithRepeatedLigatureSubtables(1_100), 10);

        shaped.IsFailure.Should().BeTrue("the searches cost 1.1 million operations");
        shaped.Error.Code.Should().Be(TypesettingErrorCodes.TooMuchWork);
    }

    [Fact]
    public void The_operations_error_says_what_was_exceeded_and_what_spent_it()
    {
        // Spot check of fix round 3, SC-4: the message names the limit, its rate per glyph and the text's length, and
        // says the font's lookups spent the operations (the text's own shape costs a fixed few per glyph per lookup).
        var shaped = Shaper.Shape(new string('a', 100), FontWithUncoveringSubtables(2_000), 10);

        shaped.Error.Message.Should().Contain("102400 operations").And.Contain("100 glyphs").And.Contain($"{ShapingBudget.OperationsPerGlyph} per glyph")
            .And.Contain("lookups").And.NotContain("without end");
    }

    [Theory]
    [InlineData(2_000)]
    [InlineData(6_000)]
    public void A_long_run_of_combining_marks_on_one_base_is_shaped_in_work_linear_in_its_length(int marks)
    {
        // Spot check of fix round 3, SC-4: one base letter and thousands of combining marks is legitimate text. Each
        // mark finds its base in one step (the bases are found in one pass over the buffer), so the work grows with the
        // number of marks, not with its square, and every mark sits on the base's anchor as a single mark does.
        var single = Shaper.Shape("x" + char.ConvertFromUtf32(0x0302), TestFonts.Regular, 10).Value[1];

        var shaped = Shaper.Shape("x" + string.Concat(Enumerable.Repeat(char.ConvertFromUtf32(0x0302), marks)), TestFonts.Regular, 10);

        shaped.IsSuccess.Should().BeTrue(shaped.IsFailure ? shaped.Error.Message : string.Empty);
        shaped.Value.Should().HaveCount(marks + 1);
        shaped.Value.Skip(1).Should().OnlyContain(glyph => glyph.XOffset == single.XOffset && glyph.YOffset == single.YOffset);
    }

    [Fact]
    public void A_text_may_spend_exactly_its_operations_limit()
    {
        var budget = new ShapingBudget(100);

        budget.Spend(budget.OperationLimit).Should().BeTrue("the limit itself may be spent");
        budget.Exceeded.Should().BeNull();
        budget.Spend(1).Should().BeFalse("one operation more is refused");
        budget.Exceeded.Should().Be(ShapingLimit.Operations);
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
