using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Ave.Extensions.Documents.Fonts.OpenType;

namespace Ave.Extensions.Documents.Typesetting.Shaping;

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

        var clusters = new Clusters(glyphs);
        foreach (var lookup in all)
        {
            if (ApplyLookup(glyphs, lookup, filter, budget, bases, clusters) is not { } next)
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
        var flags = new Clusters(buffer).Flags();
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
            if (adjustment != 0 && !flags.UnsafeToBreak(glyphs, index, next, budget))
            {
                return null;
            }
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
    private static ImmutableArray<GlyphItem>? ApplyLookup(ImmutableArray<GlyphItem> buffer, Lookup lookup, GlyphFilter filter, ShapingBudget budget, int[] bases, Clusters clusters)
    {
        if (lookup.Flags.UsesMarkFilteringSet)
        {
            return buffer;
        }

        var glyphs = buffer.ToArray();
        var flags = clusters.Flags();
        var index = 0;
        while (index < glyphs.Length)
        {
            if (!budget.Spend(1))
            {
                return null;
            }

            var next = filter.SkipsInPositioning(lookup.Flags, glyphs[index]) ? null : FirstApplying(glyphs, index, lookup, filter, budget, bases, flags);
            index = next ?? index + 1;
        }

        return budget.Exceeded is null ? [.. glyphs] : null;
    }

    /// <summary>Where to continue after the first subtable of the lookup that applies at <paramref name="index"/>, or
    /// null when none does. Each subtable tried costs one operation, the lookup of its coverage, whether it covers the
    /// glyph or not: a lookup of many subtables pays for each of them.</summary>
    private static int? FirstApplying(GlyphItem[] glyphs, int index, Lookup lookup, GlyphFilter filter, ShapingBudget budget, int[] bases, Clusters.Flagging flags)
    {
        foreach (var subtable in lookup.Subtables)
        {
            if (!budget.Spend(1))
            {
                return null;
            }

            if (Position(glyphs, index, subtable, lookup.Flags, filter, budget, bases, flags) is { } next)
            {
                return next;
            }
        }

        return null;
    }

    /// <summary>Tries one subtable at <paramref name="index"/>, changing <paramref name="glyphs"/>; returns where
    /// to continue, or null when the subtable does not apply there.</summary>
    private static int? Position(
        GlyphItem[] glyphs, int index, LookupSubtable subtable, LookupFlags flags, GlyphFilter filter, ShapingBudget budget, int[] bases, Clusters.Flagging unsafeFlags) =>
        subtable switch
        {
            PairAdjustmentByGlyph pairs => AdjustPair(glyphs, index, flags, filter, pairs.SecondHasValues,
                (first, second) => pairs.Find(first.Glyph, second.Glyph), budget, unsafeFlags),
            PairAdjustmentByClass classes => AdjustPair(glyphs, index, flags, filter, classes.SecondHasValues,
                (first, second) => ClassPair(classes, first, second), budget, unsafeFlags),
            MarkToBase marks => Attach(glyphs, index, marks, bases, budget, unsafeFlags),

            // Every other type or format: not implemented in this version; skipped (documented on the Shaper).
            _ => null,
        };

    /// <summary>
    /// Type 2, pair adjustment. The second glyph of the pair is the next glyph the lookup does not skip. The first
    /// value record adjusts the first glyph, the second the second one. "If valueFormat2 is set to 0, then the second
    /// glyph of the pair is the “next” glyph for which a lookup should be performed."; otherwise the next pair starts after it.
    /// The pair is unsafe to break only when it moved something: a record of zeros (a class pair that kerns nothing, which
    /// class kerning has for most pairs) changes no glyph, as in HarfBuzz, which marks the pair only when a value it
    /// applied is not zero (hb-ot-layout-gpos-table PairPos: <c>if (applied_first || applied_second) buffer-&gt;unsafe_to_break (...)</c>).
    /// </summary>
    private static int? AdjustPair(
        GlyphItem[] glyphs, int index, LookupFlags flags, GlyphFilter filter, bool secondHasValues, System.Func<GlyphItem, GlyphItem, PairValues?> find, ShapingBudget budget,
        Clusters.Flagging unsafeFlags)
    {
        if (NextIndex(glyphs, index, item => filter.SkipsInPositioning(flags, item), budget) is not { } second || find(glyphs[index], glyphs[second]) is not { } values)
        {
            return null;
        }

        glyphs[index] = Adjusted(glyphs[index], values.First);
        glyphs[second] = Adjusted(glyphs[second], values.Second);
        if ((values.First != default || values.Second != default) && !unsafeFlags.UnsafeToBreak(glyphs, index, second, budget))
        {
            return null;
        }

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
    /// advances are final. The base is looked up in <paramref name="bases"/> (see <see cref="Bases"/>), in one step that
    /// the subtable's own operation pays for (<see cref="FirstApplying"/>).
    /// </summary>
    private static int? Attach(GlyphItem[] glyphs, int index, MarkToBase table, int[] bases, ShapingBudget budget, Clusters.Flagging unsafeFlags)
    {
        if (table.MarkOf(glyphs[index].Glyph) is not { } mark)
        {
            return null;
        }

        var baseIndex = bases[index];
        if (baseIndex < 0 || table.BaseAnchor(glyphs[baseIndex].Glyph, mark.MarkClass) is not { } baseAnchor)
        {
            return null;
        }

        glyphs[index] = glyphs[index] with { Attachment = new MarkAttachment(baseIndex, baseAnchor.X - mark.Anchor.X, baseAnchor.Y - mark.Anchor.Y) };
        return unsafeFlags.UnsafeToBreak(glyphs, baseIndex, index, budget) ? index + 1 : null;
    }

    /// <summary>The clusters of a buffer, for marking glyphs unsafe to break: a cluster is a glyph with text and the glyphs
    /// after it that have none (the later glyphs of a substitution, the glyphs a ligature skipped). Positioning changes no
    /// glyph's text, so they are found once, in one pass, for all lookups.</summary>
    private sealed class Clusters
    {
        // _next[i]: the first glyph after i's cluster.
        private readonly int[] _next;

        public Clusters(ImmutableArray<GlyphItem> glyphs)
        {
            _next = new int[glyphs.Length];
            var next = glyphs.Length;
            for (var index = glyphs.Length - 1; index >= 0; index--)
            {
                _next[index] = next;
                if (glyphs[index].Text.Length > 0)
                {
                    next = index;
                }
            }
        }

        /// <summary>The marking of one walk over the buffer (one lookup).</summary>
        public Flagging Flags() => new(_next);

        /// <summary>
        /// Marks glyphs unsafe to break within one walk over the buffer. A lookup that joined <c>first</c> to
        /// <c>last</c> (a pair, a mark on its base) makes a break before any cluster after <c>first</c>'s, up to
        /// <c>last</c>, change how both sides are shaped: those glyphs are marked; the glyphs of <c>first</c>'s own
        /// cluster are not, as a break inside it is not a break between the two. This is HarfBuzz's
        /// <c>unsafe_to_break (start, end)</c> (hb-buffer.hh), which marks every glyph of the range whose cluster is not the
        /// range's first; the flag's meaning: "if input text is broken at the beginning of the cluster this glyph is part
        /// of, then both sides need to be re-shaped" (hb-buffer.h, <c>HB_GLYPH_FLAG_UNSAFE_TO_BREAK</c>).
        /// A walk joins ranges whose first glyph never moves back (the pairs and the marks' bases come in text order), so
        /// the glyphs up to the last one marked are not looked at again: a run of n marks on one base costs n steps, not n
        /// squared. Each glyph marked is an operation of the budget.
        /// </summary>
        public sealed class Flagging(int[] next)
        {
            private int _markedTo = -1;

            /// <summary>Marks the range; false when the budget is used up.</summary>
            public bool UnsafeToBreak(GlyphItem[] glyphs, int first, int last, ShapingBudget budget)
            {
                var from = Math.Max(next[first], _markedTo + 1);
                if (from > last)
                {
                    return true;
                }

                if (!budget.Spend(last - from + 1))
                {
                    return false;
                }

                for (var index = from; index <= last; index++)
                {
                    if (!glyphs[index].UnsafeToBreak)
                    {
                        glyphs[index] = glyphs[index] with { UnsafeToBreak = true };
                    }
                }

                _markedTo = last;
                return true;
            }
        }
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
