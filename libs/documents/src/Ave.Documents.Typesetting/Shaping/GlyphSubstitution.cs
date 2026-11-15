using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Ave.Documents.Fonts.OpenType;

namespace Ave.Documents.Typesetting.Shaping;

/// <summary>
/// Applies GSUB lookups to the shaping buffer (OpenType spec, "GSUB — Glyph substitution table"): each lookup in
/// turn walks the whole buffer, and at each glyph its subtables are tried in order; the first that applies
/// replaces the glyph (or glyph sequence) and the walk continues after what it replaced.
/// </summary>
internal static class GlyphSubstitution
{
    /// <summary>Applies the lookups in order.</summary>
    /// <param name="buffer">The glyphs.</param>
    /// <param name="lookups">The lookups, in lookup-list order.</param>
    /// <param name="filter">Glyph classes and lookup-flag skipping.</param>
    /// <returns>The glyphs after all substitutions.</returns>
    public static ImmutableArray<GlyphItem> Apply(ImmutableArray<GlyphItem> buffer, IEnumerable<Lookup> lookups, GlyphFilter filter) =>
        lookups.Aggregate(buffer, (glyphs, lookup) => ApplyLookup(glyphs, lookup, filter));

    /// <summary>
    /// One lookup over the whole buffer. A glyph the lookup skips (its flags) is passed through unchanged. A
    /// lookup that uses a mark filtering set is not applied at all: mark filtering sets are not implemented in
    /// this version, and applying the lookup without its set could substitute the wrong glyphs.
    /// </summary>
    private static ImmutableArray<GlyphItem> ApplyLookup(ImmutableArray<GlyphItem> glyphs, Lookup lookup, GlyphFilter filter)
    {
        if (lookup.Flags.UsesMarkFilteringSet)
        {
            return glyphs;
        }

        var output = ImmutableArray.CreateBuilder<GlyphItem>(glyphs.Length);
        var index = 0;
        while (index < glyphs.Length)
        {
            if (filter.SkipsInSubstitution(lookup.Flags, glyphs[index]))
            {
                output.Add(glyphs[index++]);
                continue;
            }

            var applied = lookup.Subtables
                .Select(subtable => Substitute(glyphs, index, subtable, lookup.Flags, filter))
                .FirstOrDefault(result => result is not null);
            if (applied is { } result)
            {
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

    /// <summary>What a subtable did at one position: the glyphs that replace the input, and where to continue.</summary>
    private sealed record Substitution(ImmutableArray<GlyphItem> Replacement, int Next);

    /// <summary>Tries one subtable at <paramref name="index"/>; null when it does not apply there.</summary>
    private static Substitution? Substitute(ImmutableArray<GlyphItem> glyphs, int index, LookupSubtable subtable, LookupFlags flags, GlyphFilter filter)
    {
        var item = glyphs[index];
        switch (subtable)
        {
            // Type 1: "replace a single glyph with another".
            case SingleSubstitution single when single.Replace(item.Glyph) is { } replacement:
                return new Substitution([item with { Glyph = replacement, Class = filter.ClassAfterSubstitution(replacement, item.Class) }], index + 1);

            // Type 2: "replace a single glyph with more than one glyph". All of them show the same characters;
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
                return ligatures.CandidatesFor(item.Glyph).Select(candidate => Ligate(glyphs, index, candidate, flags, filter)).FirstOrDefault(found => found is not null);

            // Every other type or format: not implemented in this version; the subtable is skipped (documented on
            // the Shaper).
            default:
                return null;
        }
    }

    /// <summary>
    /// Type 4: "replace multiple glyphs with one glyph". The components must follow the first glyph in order;
    /// glyphs the lookup skips may sit between them and are kept, after the ligature, in their order. The
    /// ligature shows the text of all its components and takes the first one's cluster.
    /// </summary>
    private static Substitution? Ligate(ImmutableArray<GlyphItem> glyphs, int index, Ligature ligature, LookupFlags flags, GlyphFilter filter)
    {
        var matched = new List<GlyphItem> { glyphs[index] };
        var skipped = new List<GlyphItem>();
        var position = index + 1;
        foreach (var component in ligature.Components)
        {
            while (position < glyphs.Length && filter.SkipsInSubstitution(flags, glyphs[position]))
            {
                skipped.Add(glyphs[position++]);
            }

            if (position >= glyphs.Length || glyphs[position].Glyph != component)
            {
                return null;
            }

            matched.Add(glyphs[position++]);
        }

        var first = matched[0];
        var result = first with
        {
            Glyph = ligature.Glyph,
            Text = string.Concat(matched.Select(glyph => glyph.Text)),
            Class = filter.ClassAfterSubstitution(ligature.Glyph, GlyphClass.Ligature),
        };
        return new Substitution([result, .. skipped], position);
    }
}
