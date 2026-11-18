using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Ave.Extensions.Documents.Fonts.OpenType;

namespace Ave.Extensions.Documents.Typesetting.Shaping;

/// <summary>
/// Applies GSUB lookups to the shaping buffer (OpenType spec, "GSUB — Glyph substitution table"): each lookup in
/// turn walks the whole buffer, and at each glyph its subtables are tried in order; the first that applies
/// replaces the glyph (or glyph sequence) and the walk continues after what it replaced.
/// </summary>
internal static class GlyphSubstitution
{
    /// <summary>How many glyphs one input glyph may become, counting every lookup: a decomposition adds a few glyphs per
    /// character, so 64 is far beyond any real font. The number is HarfBuzz's HB_BUFFER_MAX_LEN_FACTOR from version 2.7.3
    /// to 11.4.1; since 11.4.2 HarfBuzz allows 256, so the pinned reference (15.0.0) would shape some texts this engine
    /// refuses.</summary>
    public const int MaxGrowthFactor = 64;

    /// <summary>The fewest glyphs any text may become, so a short text is not held to 64 times its length: HarfBuzz's
    /// HB_BUFFER_MAX_LEN_MIN from 2.7.3 to 11.4.1 (65 536 since 11.4.2).</summary>
    public const int MinGlyphLimit = 16384;

    /// <summary>The most glyphs the substitutions may produce from a text of <paramref name="inputLength"/> glyphs.
    /// One-to-many substitutions may feed each other (sixteen one-to-two lookups turn one glyph into 65 536); this bound
    /// keeps the buffer proportional to the text. The work of the lookups is bounded by
    /// <see cref="ShapingBudget.MaxOperations"/>.</summary>
    /// <param name="inputLength">The glyphs before substitution.</param>
    /// <returns>The limit.</returns>
    public static long MaxGlyphs(int inputLength) => Math.Max(MinGlyphLimit, (long)MaxGrowthFactor * inputLength);

    /// <summary>Applies the lookups in order; null when the text reaches a limit of its <paramref name="budget"/>.</summary>
    /// <param name="buffer">The glyphs.</param>
    /// <param name="lookups">The lookups, in lookup-list order.</param>
    /// <param name="filter">Glyph classes and lookup-flag skipping.</param>
    /// <param name="budget">The limits of the text this buffer is part of.</param>
    /// <returns>The glyphs after all substitutions, or null.</returns>
    public static ImmutableArray<GlyphItem>? Apply(ImmutableArray<GlyphItem> buffer, IEnumerable<Lookup> lookups, GlyphFilter filter, ShapingBudget budget)
    {
        var glyphs = buffer;
        foreach (var lookup in lookups)
        {
            if (ApplyLookup(glyphs, lookup, filter, budget) is not { } next)
            {
                return null;
            }

            glyphs = next;
        }

        return glyphs;
    }

    /// <summary>
    /// One lookup over the whole buffer. A glyph the lookup skips (its flags) is passed through unchanged. A
    /// lookup that uses a mark filtering set is not applied at all: mark filtering sets are not implemented in
    /// this version, and applying the lookup without its set could substitute the wrong glyphs.
    /// </summary>
    private static ImmutableArray<GlyphItem>? ApplyLookup(ImmutableArray<GlyphItem> glyphs, Lookup lookup, GlyphFilter filter, ShapingBudget budget)
    {
        if (lookup.Flags.UsesMarkFilteringSet)
        {
            return glyphs;
        }

        var output = ImmutableArray.CreateBuilder<GlyphItem>(glyphs.Length);
        var index = 0;
        while (index < glyphs.Length)
        {
            if (!budget.Spend(1))
            {
                return null;
            }

            if (filter.SkipsInSubstitution(lookup.Flags, glyphs[index]))
            {
                output.Add(glyphs[index++]);
                continue;
            }

            var applied = FirstApplying(glyphs, index, lookup, filter, budget);
            if (budget.Exceeded is not null)
            {
                return null;
            }

            if (applied is { } result)
            {
                if (!budget.Grow(result.Replacement.Length - (result.Next - index)))
                {
                    return null;
                }

                output.AddRange(result.Replacement);
                index = result.Next;
            }
            else
            {
                output.Add(glyphs[index++]);
            }
        }

        return output.ToImmutable();
    }

    /// <summary>What the first subtable of the lookup that applies at <paramref name="index"/> does there, or null when
    /// none does. Each subtable tried costs one operation, the lookup of its coverage, whether it covers the glyph or
    /// not: a lookup of many subtables pays for each of them.</summary>
    private static Substitution? FirstApplying(ImmutableArray<GlyphItem> glyphs, int index, Lookup lookup, GlyphFilter filter, ShapingBudget budget)
    {
        foreach (var subtable in lookup.Subtables)
        {
            if (!budget.Spend(1))
            {
                return null;
            }

            if (Substitute(glyphs, index, subtable, lookup.Flags, filter, budget) is { } result)
            {
                return result;
            }
        }

        return null;
    }

    /// <summary>What a subtable did at one position: the glyphs that replace the input, and where to continue.</summary>
    private sealed record Substitution(ImmutableArray<GlyphItem> Replacement, int Next);

    /// <summary>Tries one subtable at <paramref name="index"/>; null when it does not apply there.</summary>
    private static Substitution? Substitute(ImmutableArray<GlyphItem> glyphs, int index, LookupSubtable subtable, LookupFlags flags, GlyphFilter filter, ShapingBudget budget)
    {
        var item = glyphs[index];
        switch (subtable)
        {
            // Type 1: "replace a single glyph with another".
            case SingleSubstitution single when single.Replace(item.Glyph) is { } replacement:
                return new Substitution([item with { Glyph = replacement, Class = filter.ClassAfterSubstitution(replacement, item.Class) }], index + 1);

            // Type 2 ("Lookup type 2 subtable: multiple substitution"): "A multiple substitution (MultipleSubst) subtable
            // replaces a single glyph with a sequence of glyphs". All of them show the same characters;
            // the text stays on the first, so copying the text gives it once.
            case MultipleSubstitution multiple when multiple.SequenceOf(item.Glyph) is { } sequence:
                return new Substitution(
                    [.. sequence.Select((glyph, position) => item with
                    {
                        Glyph = glyph,
                        Text = position == 0 ? item.Text : string.Empty,
                        Class = filter.ClassAfterSubstitution(glyph, item.Class),
                    })],
                    index + 1);

            case LigatureSubstitution ligatures:
                return ligatures.CandidatesFor(item.Glyph).Select(candidate => Ligate(glyphs, index, candidate, flags, filter, budget)).FirstOrDefault(found => found is not null);

            // Every other type or format: not implemented in this version; the subtable is skipped (documented on
            // the Shaper).
            default:
                return null;
        }
    }

    /// <summary>
    /// Type 4: "replace multiple glyphs with one glyph". The components must follow the first glyph in order;
    /// glyphs the lookup skips may sit between them and are kept, after the ligature, in their order. The ligature shows
    /// the text of everything from its first component to its last, the skipped glyphs' too, in the order of the text,
    /// and the skipped glyphs show none of their own: they join the ligature's cluster, as HarfBuzz merges the clusters of
    /// a ligature and the glyphs it skips (hb-ot-layout-gsubgpos.hh, <c>ligate_input</c>). So the glyphs' texts stay the
    /// text in its order, and a break between the components — at a soft hyphen the ligature skipped — falls inside one
    /// cluster, where the line breaker shapes both sides again. Each glyph looked at for a component, skipped or matched,
    /// costs one operation of the budget; no ligature is made once the budget is used up.
    /// </summary>
    private static Substitution? Ligate(ImmutableArray<GlyphItem> glyphs, int index, Ligature ligature, LookupFlags flags, GlyphFilter filter, ShapingBudget budget)
    {
        var matched = new List<GlyphItem> { glyphs[index] };
        var skipped = new List<GlyphItem>();
        var position = index + 1;
        foreach (var component in ligature.Components)
        {
            var start = position;
            while (position < glyphs.Length && filter.SkipsInSubstitution(flags, glyphs[position]))
            {
                skipped.Add(glyphs[position++]);
            }

            if (!budget.Spend(position - start + 1) || position >= glyphs.Length || glyphs[position].Glyph != component)
            {
                return null;
            }

            matched.Add(glyphs[position++]);
        }

        var first = matched[0];
        var result = first with
        {
            Glyph = ligature.Glyph,
            Text = string.Concat(glyphs.Skip(index).Take(position - index).Select(glyph => glyph.Text)),
            Class = filter.ClassAfterSubstitution(ligature.Glyph, GlyphClass.Ligature),
        };
        return new Substitution([result, .. skipped.Select(glyph => glyph with { Text = string.Empty })], position);
    }
}
