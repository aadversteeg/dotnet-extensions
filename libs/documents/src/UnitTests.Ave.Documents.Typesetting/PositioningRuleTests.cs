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
            SecondHasValues: false);
        ImmutableArray<GlyphItem> glyphs =
        [
            new(new GlyphId(1), "A", GlyphClass.Base, XAdvance: 600),
            new(new GlyphId(2), "V", GlyphClass.Base, XAdvance: 600),
        ];

        var positioned = GlyphPositioning.Apply(glyphs, [new Lookup(0, new LookupFlags(0), [pairs])], new GlyphFilter(null));

        positioned.Select(glyph => (glyph.XOffset, glyph.YOffset, glyph.XAdvance)).Should().Equal((15, 120, 570), (0, 0, 600));
    }
}
