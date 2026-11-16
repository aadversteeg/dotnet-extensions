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
    /// attachments. Null when the text reaches the operations limit of its <paramref name="budget"/>.</summary>
    /// <param name="buffer">The glyphs.</param>
    /// <param name="lookups">The lookups, in lookup-list order.</param>
    /// <param name="filter">Glyph classes and lookup-flag skipping.</param>
    /// <param name="budget">The limits of the text this buffer is part of.</param>
    /// <returns>The positioned glyphs, or null.</returns>
    public static ImmutableArray<GlyphItem>? Apply(ImmutableArray<GlyphItem> buffer, IEnumerable<Lookup> lookups, GlyphFilter filter, ShapingBudget budget)
    {
        var glyphs = buffer;
        var all = lookups.ToImmutableArray();
        var bases = all.Any(lookup => lookup.Subtables.Any(subtable => subtable is MarkToBase)) ? Bases(glyphs, budget) : [];
        if (bases is null)
        {
            return null;
        }

        foreach (var lookup in all)
        {
            if (ApplyLookup(glyphs, lookup, filter, budget, bases) is not { } next)
            {
                return null;
            }

            glyphs = next;
        }

        return ResolveAttachments(ZeroMarkAdvances(glyphs));
    }

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
    /// Kerning from the 'kern' table: each pair of adjacent glyphs, skipping marks and invisible characters
    /// (<see cref="IsSkippedByKerning"/>: neither separates a pair), gets the table's adjustment added to the first glyph's advance. The OpenType spec
    /// ('kern', format 0) only says "If this value is greater than zero, the characters will be moved apart. If this
    /// value is less than zero, the character will be moved closer together"; putting the whole value on the first
    /// glyph's advance is the engine's convention (the positions on the page are the same either way).
    /// </summary>
    /// <param name="buffer">The glyphs.</param>
    /// <param name="kerning">The 'kern' table.</param>
    /// <param name="budget">The limits of the text; each glyph looked at is an operation.</param>
    /// <returns>The kerned glyphs, or null when the budget is used up.</returns>
    public static ImmutableArray<GlyphItem>? ApplyLegacyKerning(ImmutableArray<GlyphItem> buffer, LegacyKerning kerning, ShapingBudget budget)
    {
        var glyphs = buffer.ToArray();
        for (var index = 0; index < glyphs.Length; index++)
        {
            if (!budget.Spend(1))
            {
                return null;
            }

            if (IsSkippedByKerning(glyphs[index]) || NextIndex(glyphs, index, IsSkippedByKerning, budget) is not { } next)
            {
                continue;
            }

            var adjustment = kerning.Adjustment(glyphs[index].Glyph, glyphs[next].Glyph);
            glyphs[index] = glyphs[index] with { XAdvance = glyphs[index].XAdvance + adjustment };
        }

        return budget.Exceeded is null ? [.. glyphs] : null;
    }

    /// <summary>A glyph the 'kern' table passes over: a mark, which sits on its base, and an invisible character (a
    /// default ignorable), which takes no space; as the GPOS path skips them (<see cref="GlyphFilter.SkipsInPositioning"/>).</summary>
    private static bool IsSkippedByKerning(GlyphItem item) => item.Class == GlyphClass.Mark || item.IsDefaultIgnorable;

    /// <summary>
    /// For each position, the base a mark there attaches to: the nearest glyph before it that is not a mark and not
    /// default ignorable, or -1. Found in one pass, one operation per glyph, when a lookup holds a mark-to-base subtable,
    /// and shared by every lookup: positioning changes no glyph and no class. So a mark finds its base in one step, and a long run of marks on one base costs
    /// work in proportion to its length, not to its square (each mark would otherwise step back over all the marks
    /// before it). Null when the budget is used up.
    /// </summary>
    private static int[]? Bases(ImmutableArray<GlyphItem> glyphs, ShapingBudget budget)
    {
        if (!budget.Spend(glyphs.Length))
        {
            return null;
        }

        var bases = new int[glyphs.Length];
        var last = -1;
        for (var index = 0; index < glyphs.Length; index++)
        {
            bases[index] = last;
            last = glyphs[index].Class == GlyphClass.Mark || glyphs[index].IsDefaultIgnorable ? last : index;
        }

        return bases;
    }

    /// <summary>One lookup over the whole buffer (lookups with a mark filtering set are skipped, as in GSUB); null once
    /// the budget is used up. Each position costs an operation, each subtable tried there another, and each glyph a
    /// search steps over another.</summary>
    private static ImmutableArray<GlyphItem>? ApplyLookup(ImmutableArray<GlyphItem> buffer, Lookup lookup, GlyphFilter filter, ShapingBudget budget, int[] bases)
    {
        if (lookup.Flags.UsesMarkFilteringSet)
        {
            return buffer;
        }

        var glyphs = buffer.ToArray();
        var index = 0;
        while (index < glyphs.Length)
        {
            if (!budget.Spend(1))
            {
                return null;
            }

            var next = filter.SkipsInPositioning(lookup.Flags, glyphs[index]) ? null : FirstApplying(glyphs, index, lookup, filter, budget, bases);
            index = next ?? index + 1;
        }

        return budget.Exceeded is null ? [.. glyphs] : null;
    }

    /// <summary>Where to continue after the first subtable of the lookup that applies at <paramref name="index"/>, or
    /// null when none does. Each subtable tried costs one operation, the lookup of its coverage, whether it covers the
    /// glyph or not: a lookup of many subtables pays for each of them.</summary>
    private static int? FirstApplying(GlyphItem[] glyphs, int index, Lookup lookup, GlyphFilter filter, ShapingBudget budget, int[] bases)
    {
        foreach (var subtable in lookup.Subtables)
        {
            if (!budget.Spend(1))
            {
                return null;
            }

            if (Position(glyphs, index, subtable, lookup.Flags, filter, budget, bases) is { } next)
            {
                return next;
            }
        }

        return null;
    }

    /// <summary>Tries one subtable at <paramref name="index"/>, changing <paramref name="glyphs"/>; returns where
    /// to continue, or null when the subtable does not apply there.</summary>
    private static int? Position(GlyphItem[] glyphs, int index, LookupSubtable subtable, LookupFlags flags, GlyphFilter filter, ShapingBudget budget, int[] bases) =>
        subtable switch
        {
            PairAdjustmentByGlyph pairs => AdjustPair(glyphs, index, flags, filter, pairs.SecondHasValues,
                (first, second) => pairs.Find(first.Glyph, second.Glyph), budget),
            PairAdjustmentByClass classes => AdjustPair(glyphs, index, flags, filter, classes.SecondHasValues,
                (first, second) => ClassPair(classes, first, second), budget),
            MarkToBase marks => Attach(glyphs, index, marks, bases, budget),

            // Every other type or format: not implemented in this version; skipped (documented on the Shaper).
            _ => null,
        };

    /// <summary>
    /// Type 2, pair adjustment. The second glyph of the pair is the next glyph the lookup does not skip. The first
    /// value record adjusts the first glyph, the second the second one. "If valueFormat2 is set to 0, then the second
    /// glyph of the pair is the “next” glyph for which a lookup should be performed."; otherwise the next pair starts after it.
    /// </summary>
    private static int? AdjustPair(
        GlyphItem[] glyphs, int index, LookupFlags flags, GlyphFilter filter, bool secondHasValues, System.Func<GlyphItem, GlyphItem, PairValues?> find, ShapingBudget budget)
    {
        if (NextIndex(glyphs, index, item => filter.SkipsInPositioning(flags, item), budget) is not { } second || find(glyphs[index], glyphs[second]) is not { } values)
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
    /// base), when the base is covered and has an anchor for the mark's class; "the mark anchor is aligned with the
    /// base anchor for the applicable mark class". The offset is recorded relative to the base and resolved at the end, when all
    /// advances are final. The base is looked up in <paramref name="bases"/> (see <see cref="Bases"/>): one operation.
    /// </summary>
    private static int? Attach(GlyphItem[] glyphs, int index, MarkToBase table, int[] bases, ShapingBudget budget)
    {
        if (table.MarkOf(glyphs[index].Glyph) is not { } mark)
        {
            return null;
        }

        var baseIndex = bases[index];
        if (!budget.Spend(1) || baseIndex < 0 || table.BaseAnchor(glyphs[baseIndex].Glyph, mark.MarkClass) is not { } baseAnchor)
        {
            return null;
        }

        glyphs[index] = glyphs[index] with { Attachment = new MarkAttachment(baseIndex, baseAnchor.X - mark.Anchor.X, baseAnchor.Y - mark.Anchor.Y) };
        return index + 1;
    }

    /// <summary>
    /// Turns each mark attachment into offsets from the pen position of the mark: the mark is drawn where the base
    /// is drawn plus the anchor difference, so the advances of the glyphs from the base up to the mark are taken
    /// back (text runs left to right). The advances between the two are a difference of running sums, so a long run of
    /// marks on one base costs no more than its length.
    /// </summary>
    private static ImmutableArray<GlyphItem> ResolveAttachments(ImmutableArray<GlyphItem> glyphs)
    {
        // pens[i]: the pen position before glyph i, the sum of the advances before it.
        var pens = new long[glyphs.Length + 1];
        for (var index = 0; index < glyphs.Length; index++)
        {
            pens[index + 1] = pens[index] + glyphs[index].XAdvance;
        }

        return [.. glyphs.Select((item, index) => item.Attachment is not { } attachment
            ? item
            : item with
            {
                XOffset = (int)(attachment.X + glyphs[attachment.BaseIndex].XOffset - (pens[index] - pens[attachment.BaseIndex])),
                YOffset = attachment.Y + glyphs[attachment.BaseIndex].YOffset,
                Attachment = null,
            })];
    }

    /// <summary>The first glyph after <paramref name="index"/> that is not skipped, or null; each glyph looked at is an
    /// operation of the budget, and the search gives up once it is used up.</summary>
    private static int? NextIndex(IReadOnlyList<GlyphItem> glyphs, int index, System.Func<GlyphItem, bool> skip, ShapingBudget budget)
    {
        for (var next = index + 1; next < glyphs.Count && budget.Spend(1); next++)
        {
            if (!skip(glyphs[next]))
            {
                return next;
            }
        }

        return null;
    }
}
