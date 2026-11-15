using System.Collections.Frozen;
using System.Collections.Immutable;
using Ave.Documents.Fonts;
using Ave.Documents.Fonts.OpenType;
using Ave.Documents.TestSupport;
using Ave.Documents.Typesetting;
using Ave.Documents.Typesetting.Shaping;

namespace UnitTests.Ave.Documents.Typesetting;

/// <summary>
/// GSUB and GPOS on Lato Regular. Expected glyph ids and values come from fontTools 4.68.0, not from this code:
/// f = 61, i = 98, x = 138, U+0302 = 1579 (advance 0), ligature "fi" = 67, "ffi" = 76; mark-to-base lookup 4: mark
/// anchor of U+0302 (−400, 1120), base anchor of x (497, 1120), x advances 996 units; 2000 units per em.
/// </summary>
public class OpenTypeShapingTests
{
    private static ImmutableArray<ShapedGlyph> Shape(string text) => Shaper.Shape(text, TestFonts.Regular, fontSize: 10).Value;

    [Fact]
    public void Liga_replaces_f_and_i_with_the_fi_ligature_that_shows_both_characters()
    {
        var glyphs = Shape("fi");

        glyphs.Select(glyph => glyph.Glyph).Should().Equal(new GlyphId(67));
        glyphs.Single().Text.Should().Be("fi", "the ToUnicode map then gives back both characters");
    }

    [Fact]
    public void The_longest_ligature_is_preferred()
    {
        // "Ligatures with more components should be stored ahead of those with fewer components" (GSUB type 4).
        var glyphs = Shape("office");

        glyphs.Select(glyph => glyph.Text).Should().Equal("o", "ffi", "c", "e");
        glyphs[1].Glyph.Should().Be(new GlyphId(76));
    }

    [Fact]
    public void A_combining_mark_is_attached_to_its_base_by_the_anchors()
    {
        var glyphs = Shape("x" + char.ConvertFromUtf32(0x0302));

        glyphs.Select(glyph => glyph.Glyph).Should().Equal(new GlyphId(138), new GlyphId(1579));
        var mark = glyphs[1];
        mark.Advance.Should().Be(0);

        // Base anchor − mark anchor − the base's advance: 497 − (−400) − 996 = −99 units; y: 1120 − 1120 = 0.
        mark.XOffset.Should().Be(-99 * 10 / 2000.0);
        mark.YOffset.Should().Be(0);
    }

    [Fact]
    public void The_font_lists_the_lookups_per_script_and_feature()
    {
        var layout = TestFonts.Regular.Layout;

        layout.Positions.Lookups.Should().HaveCount(5);
        layout.Positions.LookupsFor("latn", Shaper.PositioningFeatures).Select(lookup => lookup.Index).Should().Equal(0, 1, 2, 3, 4);
        layout.Substitutions.LookupsFor("latn", new[] { "liga" }.ToFrozenSet()).Select(lookup => lookup.Index).Should().Equal(30, 31);
        layout.Substitutions.LookupsFor("grek", new[] { "liga" }.ToFrozenSet()).Should().BeEmpty("Lato has no liga for Greek");
        layout.Substitutions.LookupsFor("zzzz", new[] { "liga" }.ToFrozenSet()).Select(lookup => lookup.Index)
            .Should().Equal(new[] { 30, 31 }, "an unknown script falls back to DFLT");
    }

    [Fact]
    public void The_legacy_kern_table_adds_its_value_to_the_first_glyph_and_skips_marks()
    {
        var kerning = new LegacyKerning([new KerningSubtable(new Dictionary<(ushort, ushort), short> { [(1, 2)] = -50 }.ToFrozenDictionary(), false)]);
        ImmutableArray<GlyphItem> glyphs =
        [
            new(new GlyphId(1), "A", GlyphClass.Base, XAdvance: 600),
            new(new GlyphId(9), "́", GlyphClass.Mark, XAdvance: 0),
            new(new GlyphId(2), "V", GlyphClass.Base, XAdvance: 600),
        ];

        var kerned = GlyphPositioning.ApplyLegacyKerning(glyphs, kerning);

        kerned.Select(glyph => glyph.XAdvance).Should().Equal(550, 0, 600);
    }

    [Fact]
    public void Kern_table_subtables_add_up_unless_one_overrides()
    {
        static KerningSubtable Subtable(short value, bool overrides) =>
            new(new Dictionary<(ushort, ushort), short> { [(1, 2)] = value }.ToFrozenDictionary(), overrides);

        new LegacyKerning([Subtable(-10, false), Subtable(-20, false)]).Adjustment(new GlyphId(1), new GlyphId(2)).Should().Be(-30);
        new LegacyKerning([Subtable(-10, false), Subtable(-20, true)]).Adjustment(new GlyphId(1), new GlyphId(2)).Should().Be(-20);
    }

    [Fact]
    public void A_mark_with_an_advance_in_hmtx_takes_no_space()
    {
        // Theano Didot: U+0314 COMBINING REVERSED COMMA ABOVE is GDEF class 3 (mark) with an hmtx advance of 420 units;
        // e is 962 units. HarfBuzz zeroes a mark's advance after GPOS (hb-ot-shape.cc, zero_mark_widths_by_gdef).
        var font = TestFonts.TheanoDidot;

        var glyphs = Shaper.Shape("e" + char.ConvertFromUtf32(0x0314), font, fontSize: font.Metrics.UnitsPerEm).Value;

        glyphs.Should().HaveCount(2);
        glyphs[1].Advance.Should().Be(0);
        glyphs.Sum(glyph => glyph.Advance).Should().Be(962);
    }
}
