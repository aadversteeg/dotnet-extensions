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

    [Fact]
    public void A_kern_table_that_runs_out_of_its_work_budget_is_an_error()
    {
        // The sub-review's probe P7: 65535 subtables announced, ONE real one whose length is 0, so every
        // "next" subtable is the same one; the parse used to look only at reads past the end and accepted it.
        var table = new BigEndianBuilder()
            .UInt16(0).UInt16(65535)
            .UInt16(0).UInt16(0).UInt16(0x0001)
            .UInt16(1).UInt16(6).UInt16(0).UInt16(0).UInt16(5).UInt16(7).UInt16(unchecked((ushort)-40))
            .ToArray();

        var result = LegacyKerning.Parse(table);

        result.IsFailure.Should().BeTrue();
        result.Error.Is(DocumentErrorCodes.Font.Parse._).Should().BeTrue();
    }

    [Fact]
    public void A_null_class_definition_offset_in_a_pair_subtable_means_every_glyph_is_class_0()
    {
        // The sub-review's probe P6: classDef2Offset 0 is NULL ("every glyph class 0", as HarfBuzz reads it); it used
        // to be read as a class table at the start of the subtable itself.
        var subtable = new BigEndianBuilder()
            .UInt16(2).UInt16(20).UInt16(4).UInt16(0)
            .UInt16(26).UInt16(0)
            .UInt16(1).UInt16(1)
            .UInt16(unchecked((ushort)-50))
            .UInt16(0)
            .UInt16(1).UInt16(1).UInt16(45)
            .UInt16(1).UInt16(0).UInt16(0)
            .ToArray();

        var result = LayoutTableParser.Parse("GPOS", MalformedLayoutTableTests.LayoutTableWithOneSubtable(2, subtable), TestFonts.Regular.GlyphCount);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        var pairs = (PairAdjustmentByClass)result.Value.Lookups[0].Subtables[0];
        pairs.SecondClasses.Should().Be(ClassDefinition.Empty);
    }

    [Theory]
    [InlineData("grek", "latn", "grek")]
    [InlineData("grek", "latn", "DFLT")]
    [InlineData("grek", "latn", "dflt")]
    [InlineData("grek", "latn", "latn")]
    public void The_script_falls_back_to_DFLT_then_dflt_then_latn_as_in_HarfBuzz(string wanted, string _, string present)
    {
        // HarfBuzz, hb-ot-layout.cc hb_ot_layout_table_select_script: the script's own tags, then 'DFLT', then
        // 'dflt' ("try with 'dflt'; MS site has had typos and many fonts use it now"), then 'latn' ("try with
        // 'latn'; some old fonts put their features there even if they're really trying to support Thai").
        var language = new LanguageSystem(null, [0]);
        var table = new LayoutTable(
            [new ScriptTable(present, language)],
            [new FeatureTable("liga", [0])],
            [new Lookup(0, new LookupFlags(0), [])]);

        table.LookupsFor(wanted, new HashSet<string> { "liga" }).Should().ContainSingle();
    }

    [Fact]
    public void DFLT_is_preferred_to_latn_when_a_font_has_both()
    {
        var table = new LayoutTable(
            [new ScriptTable("latn", new LanguageSystem(null, [1])), new ScriptTable("DFLT", new LanguageSystem(null, [0]))],
            [new FeatureTable("liga", [0]), new FeatureTable("liga", [1])],
            [new Lookup(0, new LookupFlags(0), []), new Lookup(1, new LookupFlags(0), [])]);

        table.LookupsFor("cyrl", new HashSet<string> { "liga" }).Should().ContainSingle().Which.Index.Should().Be(0);
    }
}
