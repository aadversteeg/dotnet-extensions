using System.Collections.Frozen;
using System.Collections.Immutable;
using Ave.Documents.Fonts;
using Ave.Documents.Fonts.OpenType;
using Ave.Documents.Typesetting.Shaping;

namespace UnitTests.Ave.Documents.Typesetting;

/// <summary>
/// GPOS rules that Lato never exercises (its pair kerning only changes advances), so no test of a real font would
/// notice a change to them. The lookups are built here.
/// </summary>
public class PositioningRuleTests
{
    [Fact]
    public void A_pair_adjustment_moves_the_glyph_vertically_by_its_y_placement()
    {
        // OpenType GPOS, ValueRecord: "yPlacement: vertical adjustment for placement, in design units"; it moves the
        // glyph without changing the pen movement. Pair (1, 2) gives the first glyph yPlacement 120 and xAdvance −30.
        var pairs = new PairAdjustmentByGlyph(
            new Coverage([new Coverage.Range(1, 1, 0)]),
            [new Dictionary<ushort, PairValues> { [2] = new PairValues(new ValueRecord(15, 120, -30, 0), default) }.ToFrozenDictionary()],
            secondHasValues: false);
        ImmutableArray<GlyphItem> glyphs =
        [
            new(new GlyphId(1), "A", GlyphClass.Base, XAdvance: 600),
            new(new GlyphId(2), "V", GlyphClass.Base, XAdvance: 600),
        ];

        var positioned = GlyphPositioning.Apply(glyphs, [new Lookup(0, new LookupFlags(0), [pairs])], new GlyphFilter(null), new ShapingBudget(glyphs.Length))!.Value;

        positioned.Select(glyph => (glyph.XOffset, glyph.YOffset, glyph.XAdvance)).Should().Equal((15, 120, 570), (0, 0, 600));
    }

    /// <summary>A zero width joiner: default-ignorable, drawn as nothing, on glyph 3.</summary>
    private static GlyphItem Joiner => new(new GlyphId(3), GlyphItem.ZeroWidthJoiner, GlyphClass.Base) { IsDefaultIgnorable = true };

    [Fact]
    public void A_default_ignorable_between_a_pair_does_not_stop_its_adjustment()
    {
        // HarfBuzz skips default-ignorables when it matches GPOS input (hb-ot-layout-gsubgpos.hh, the skipping
        // iterator), so "A", ZWJ, "V" is kerned as "AV": the pair's second glyph is the next one that is not skipped.
        var pairs = new PairAdjustmentByGlyph(
            new Coverage([new Coverage.Range(1, 1, 0)]),
            [new Dictionary<ushort, PairValues> { [2] = new PairValues(new ValueRecord(0, 0, -30, 0), default) }.ToFrozenDictionary()],
            secondHasValues: false);
        ImmutableArray<GlyphItem> glyphs =
        [
            new(new GlyphId(1), "A", GlyphClass.Base, XAdvance: 600),
            Joiner,
            new(new GlyphId(2), "V", GlyphClass.Base, XAdvance: 600),
        ];

        var positioned = GlyphPositioning.Apply(glyphs, [new Lookup(0, new LookupFlags(0), [pairs])], new GlyphFilter(null), new ShapingBudget(glyphs.Length))!.Value;

        positioned.Select(glyph => glyph.XAdvance).Should().Equal(570, 0, 600);
    }

    [Fact]
    public void A_mark_after_a_default_ignorable_attaches_to_the_base_before_it()
    {
        // Base glyph 1 has an anchor at (100, 500) for mark class 0; the mark, glyph 5, has its anchor at (0, 0). A ZWJ
        // between them is not a base: the mark goes on glyph 1, 100 units right and 500 up of glyph 1's origin.
        var marks = new MarkToBase(
            new Coverage([new Coverage.Range(5, 5, 0)]),
            [(0, new Anchor(0, 0))],
            new Coverage([new Coverage.Range(1, 1, 0)]),
            [[new Anchor(100, 500)]]);
        ImmutableArray<GlyphItem> glyphs =
        [
            new(new GlyphId(1), "a", GlyphClass.Base, XAdvance: 600),
            Joiner,
            new(new GlyphId(5), "\u0301", GlyphClass.Mark),
        ];

        var positioned = GlyphPositioning.Apply(glyphs, [new Lookup(0, new LookupFlags(0), [marks])], new GlyphFilter(null), new ShapingBudget(glyphs.Length))!.Value;

        positioned[2].YOffset.Should().Be(500, "the mark sits on the base's anchor");
    }
}
