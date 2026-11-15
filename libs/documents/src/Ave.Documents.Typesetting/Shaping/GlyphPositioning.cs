using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Ave.Documents.Fonts.OpenType;

namespace Ave.Documents.Typesetting.Shaping;

/// <summary>
/// Applies GPOS lookups to the shaping buffer (OpenType spec, "GPOS — Glyph positioning table"), or the old
/// 'kern' table when the font has no GPOS kerning, zeroes the advances of marks, and finally resolves mark attachments
/// into offsets.
/// </summary>
internal static class GlyphPositioning
{
    /// <summary>Applies the lookups in order, then takes the advance of every mark away, then resolves mark
    /// attachments.</summary>
    public static ImmutableArray<GlyphItem> Apply(ImmutableArray<GlyphItem> buffer, IEnumerable<Lookup> lookups, GlyphFilter filter) =>
        ResolveAttachments(ZeroMarkAdvances(lookups.Aggregate(buffer, (glyphs, lookup) => ApplyLookup(glyphs, lookup, filter))));

    /// <summary>
    /// A mark takes no horizontal space: its advance becomes 0 once GPOS is applied, whatever 'hmtx' and the lookups
    /// gave it. The OpenType specification leaves advances alone (GPOS mark-to-base: "Placement of the base glyph and advances
    /// of both glyphs are not affected"; mark-to-mark: "Advance widths are not affected"); this is HarfBuzz's convention, which the engine follows where the specification is silent
    /// (hb-ot-shape.cc <c>zero_mark_widths_by_gdef</c>, mode <c>BY_GDEF_LATE</c> of the default shaper). A glyph is a
    /// mark by its GDEF class, or, in a font without GDEF, by its character (<see cref="GlyphFilter.ClassOf"/>, as
    /// HarfBuzz synthesises the classes). Without that, every accent of a font whose marks have an advance — Consolas,
    /// Courier New, Theano Didot's U+0314 — leaves a gap after it.
    /// </summary>
    private static ImmutableArray<GlyphItem> ZeroMarkAdvances(ImmutableArray<GlyphItem> glyphs) =>
        [.. glyphs.Select(item => item.Class == GlyphClass.Mark ? item with { XAdvance = 0 } : item)];

    /// <summary>
    /// Kerning from the 'kern' table: each pair of adjacent glyphs, skipping marks (a mark sits on its base and
    /// does not separate a pair), gets the table's adjustment added to the first glyph's advance. The OpenType spec
    /// ('kern', format 0) only says "If this value is greater than zero, the characters will be moved apart. If this
    /// value is less than zero, the character will be moved closer together"; putting the whole value on the first
    /// glyph's advance is the engine's convention (the positions on the page are the same either way).
    /// </summary>
    public static ImmutableArray<GlyphItem> ApplyLegacyKerning(ImmutableArray<GlyphItem> buffer, LegacyKerning kerning)
    {
        var glyphs = buffer.ToArray();
        for (var index = 0; index < glyphs.Length; index++)
        {
            if (glyphs[index].Class == GlyphClass.Mark || NextIndex(glyphs, index, item => item.Class == GlyphClass.Mark) is not { } next)
            {
                continue;
            }

            var adjustment = kerning.Adjustment(glyphs[index].Glyph, glyphs[next].Glyph);
            glyphs[index] = glyphs[index] with { XAdvance = glyphs[index].XAdvance + adjustment };
        }

        return [.. glyphs];
    }

    /// <summary>One lookup over the whole buffer (lookups with a mark filtering set are skipped, as in GSUB).</summary>
    private static ImmutableArray<GlyphItem> ApplyLookup(ImmutableArray<GlyphItem> buffer, Lookup lookup, GlyphFilter filter)
    {
        if (lookup.Flags.UsesMarkFilteringSet)
        {
            return buffer;
        }

        var glyphs = buffer.ToArray();
        var index = 0;
        while (index < glyphs.Length)
        {
            var next = filter.SkipsInPositioning(lookup.Flags, glyphs[index])
                ? null
                : lookup.Subtables.Select(subtable => Position(glyphs, index, subtable, lookup.Flags, filter)).FirstOrDefault(found => found is not null);
            index = next ?? index + 1;
        }

        return [.. glyphs];
    }

    /// <summary>Tries one subtable at <paramref name="index"/>, changing <paramref name="glyphs"/>; returns where
    /// to continue, or null when the subtable does not apply there.</summary>
    private static int? Position(GlyphItem[] glyphs, int index, LookupSubtable subtable, LookupFlags flags, GlyphFilter filter) =>
        subtable switch
        {
            PairAdjustmentByGlyph pairs => AdjustPair(glyphs, index, flags, filter, pairs.SecondHasValues,
                (first, second) => pairs.Find(first.Glyph, second.Glyph)),
            PairAdjustmentByClass classes => AdjustPair(glyphs, index, flags, filter, classes.SecondHasValues,
                (first, second) => ClassPair(classes, first, second)),
            MarkToBase marks => Attach(glyphs, index, marks),

            // Every other type or format: not implemented in this version; skipped (documented on the Shaper).
            _ => null,
        };

    /// <summary>
    /// Type 2, pair adjustment. The second glyph of the pair is the next glyph the lookup does not skip. The first
    /// value record adjusts the first glyph, the second the second one. "If valueFormat2 is set to 0, then the second
    /// glyph of the pair is the 'first' glyph of the next pair"; otherwise the next pair starts after it.
    /// </summary>
    private static int? AdjustPair(
        GlyphItem[] glyphs, int index, LookupFlags flags, GlyphFilter filter, bool secondHasValues, System.Func<GlyphItem, GlyphItem, PairValues?> find)
    {
        if (NextIndex(glyphs, index, item => filter.SkipsInPositioning(flags, item)) is not { } second || find(glyphs[index], glyphs[second]) is not { } values)
        {
            return null;
        }

        glyphs[index] = Adjusted(glyphs[index], values.First);
        glyphs[second] = Adjusted(glyphs[second], values.Second);
        return secondHasValues ? second + 1 : second;
    }

    /// <summary>Format 2: the first glyph must be covered; then the value records of (class of first, class of
    /// second) apply, if both classes are inside the class counts.</summary>
    private static PairValues? ClassPair(PairAdjustmentByClass table, GlyphItem first, GlyphItem second)
    {
        if (table.FirstGlyphs.IndexOf(first.Glyph) is null)
        {
            return null;
        }

        var (class1, class2) = (table.FirstClasses.ClassOf(first.Glyph), table.SecondClasses.ClassOf(second.Glyph));
        return class1 < table.FirstClassCount && class2 < table.SecondClassCount ? table.Values[class1 * table.SecondClassCount + class2] : null;
    }

    /// <summary>A value record applied to a glyph: placement moves the glyph, advance changes the pen movement.</summary>
    private static GlyphItem Adjusted(GlyphItem item, ValueRecord value) => item with
    {
        XOffset = item.XOffset + value.XPlacement,
        YOffset = item.YOffset + value.YPlacement,
        XAdvance = item.XAdvance + value.XAdvance,
    };

    /// <summary>
    /// Type 4, mark-to-base: a covered mark is attached to the nearest glyph before it that is not a mark (its
    /// base), when the base is covered and has an anchor for the mark's class; "the mark's anchor point is aligned
    /// with the base's anchor point". The offset is recorded relative to the base and resolved at the end, when all
    /// advances are final.
    /// </summary>
    private static int? Attach(GlyphItem[] glyphs, int index, MarkToBase table)
    {
        if (table.MarkOf(glyphs[index].Glyph) is not { } mark)
        {
            return null;
        }

        var baseIndex = index - 1;
        while (baseIndex >= 0 && (glyphs[baseIndex].Class == GlyphClass.Mark || glyphs[baseIndex].IsDefaultIgnorable))
        {
            baseIndex--;
        }

        if (baseIndex < 0 || table.BaseAnchor(glyphs[baseIndex].Glyph, mark.MarkClass) is not { } baseAnchor)
        {
            return null;
        }

        glyphs[index] = glyphs[index] with { Attachment = new MarkAttachment(baseIndex, baseAnchor.X - mark.Anchor.X, baseAnchor.Y - mark.Anchor.Y) };
        return index + 1;
    }

    /// <summary>
    /// Turns each mark attachment into offsets from the pen position of the mark: the mark is drawn where the base
    /// is drawn plus the anchor difference, so the advances of the glyphs from the base up to the mark are taken
    /// back (text runs left to right).
    /// </summary>
    private static ImmutableArray<GlyphItem> ResolveAttachments(ImmutableArray<GlyphItem> glyphs) =>
        [.. glyphs.Select((item, index) => item.Attachment is not { } attachment
            ? item
            : item with
            {
                XOffset = attachment.X + glyphs[attachment.BaseIndex].XOffset - glyphs.Skip(attachment.BaseIndex).Take(index - attachment.BaseIndex).Sum(glyph => glyph.XAdvance),
                YOffset = attachment.Y + glyphs[attachment.BaseIndex].YOffset,
                Attachment = null,
            })];

    private static int? NextIndex(IReadOnlyList<GlyphItem> glyphs, int index, System.Func<GlyphItem, bool> skip)
    {
        for (var next = index + 1; next < glyphs.Count; next++)
        {
            if (!skip(glyphs[next]))
            {
                return next;
            }
        }

        return null;
    }
}
