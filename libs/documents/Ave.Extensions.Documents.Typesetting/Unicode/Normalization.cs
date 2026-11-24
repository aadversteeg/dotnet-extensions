using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Ave.Extensions.Documents.Typesetting.Unicode;

/// <summary>
/// Unicode normalization before shaping, the way HarfBuzz's normalizer does it for the scripts this engine shapes
/// (hb-ot-shape-normalize.cc, mode <c>HB_OT_SHAPE_NORMALIZATION_MODE_COMPOSED_DIACRITICS</c>, which its <c>AUTO</c> mode
/// resolves to): "each grapheme is decomposed in a chain of 1:2 decompositions, marks reordered, and then recomposed if
/// desired, so far it's like Unicode Normalization. However, the decomposition and recomposition only happens if the font
/// supports the resulting characters." So canonically equivalent text — "ņ" written precomposed or as n and a combining
/// cedilla — shapes to the same glyphs where the font allows it: a base and a mark become the precomposed glyph when the
/// font has it, and a precomposed character the font lacks becomes its base and marks when the font has those. The data
/// (canonical decompositions, combining classes, composition exclusions) is the Unicode Character Database
/// <see cref="UnicodeProperties.UnicodeVersion"/>, generated as <c>UnicodeNormalizationData.g.cs</c>.
/// </summary>
/// <remarks>
/// The three rounds, on one shaping piece (a script run, cut after forced breaks):
/// <list type="number">
/// <item>Decompose. A cluster of one character the font has a glyph for stays as it is ("we simply don't touch
/// 1-character clusters that are supported by the font"). One the font lacks takes the shortest decomposition the font
/// can show; the characters of a cluster of a base and marks are each decomposed as far as the font has the parts. A 1:2
/// decomposition is taken when the font has the second part and either has the first or can decompose it in turn;
/// otherwise the character stays.</item>
/// <item>Reorder: within a run of characters whose combining class is not 0, by combining class, keeping the order of
/// equal ones (the Canonical Ordering Algorithm, the Unicode Standard §3.11.5, D108 and D109); a run longer than
/// <see cref="MaxReorderedMarks"/> is left as it is, as HarfBuzz leaves it.</item>
/// <item>Recompose: a mark is combined with the last starter before it when nothing between them blocks it (the character
/// just before it has a lower combining class, or is the starter), the pair has a canonical composition that is not
/// excluded (Full_Composition_Exclusion; the Canonical Composition Algorithm, the Unicode Standard §3.11.6, D114 to D117),
/// and the font has a glyph for the result. A character that is not a mark
/// is never combined with the starter before it.</item>
/// </list>
/// Rounds 2 and 3 run only when the piece holds a mark (HarfBuzz 15's "all simple" shortcut, which the reference build
/// shares). Clusters follow HarfBuzz too: the parts of a decomposition keep their character's cluster; a run of marks that
/// is reordered, and a composition with the characters between its parts, become one cluster. Each output character's text
/// is its cluster's characters in the source, the later characters of a cluster have none, so the texts joined are the
/// source text. BOUNDED WORK: every round looks at each character a fixed number of times, so a cluster of n marks costs
/// work in proportion to n (<see cref="Steps"/> counts it). Not done, as in this engine's shaper generally: enclosing marks (general category Me) count as no marks
/// here; the space fallback of HarfBuzz's normalizer (a missing space drawn as another space) is not made, and the
/// no-break hyphen drawn with the hyphen's glyph is the shaper's (<c>Shaper.StandIn</c>), after this.
/// </remarks>
internal static partial class Normalization
{
    /// <summary>A code point with a non-zero Canonical_Combining_Class.</summary>
    private readonly record struct CombiningClass(int CodePoint, int Class);

    /// <summary>A canonical decomposition: one or two code points (<see cref="Second"/> -1 for one); whether canonical
    /// composition makes the code point (it is not a Full_Composition_Exclusion).</summary>
    private readonly record struct Decomposition(int CodePoint, int First, int Second, bool Composes);

    /// <summary>The generated data as lookups. A nested class, so that they are built on first use, after the generated
    /// arrays (in the other part of this class) exist: the order of static initializers across the files of a partial
    /// class is not defined.</summary>
    private static class Tables
    {
        public static readonly FrozenDictionary<int, int> Classes = CombiningClasses.ToFrozenDictionary(entry => entry.CodePoint, entry => entry.Class);

        public static readonly FrozenDictionary<int, (int First, int Second)> Parts =
            Decompositions.ToFrozenDictionary(entry => entry.CodePoint, entry => (entry.First, entry.Second));

        public static readonly FrozenDictionary<(int, int), int> Compositions =
            Decompositions.Where(entry => entry.Composes && entry.Second >= 0).ToFrozenDictionary(entry => (entry.First, entry.Second), entry => entry.CodePoint);
    }

    // Hangul syllables, the Unicode Standard §3.12 (Conjoining Jamo Behavior): decomposed and composed by arithmetic.
    private const int SBase = 0xAC00, LBase = 0x1100, VBase = 0x1161, TBase = 0x11A7, LCount = 19, VCount = 21, TCount = 28;
    private const int NCount = VCount * TCount, SCount = LCount * NCount;

    /// <summary>The longest run of marks that is reordered: HarfBuzz's <c>HB_OT_SHAPE_MAX_COMBINING_MARKS</c>, 32
    /// (hb-ot-shape-normalize.cc, second round), which also bounds the work of the sort.</summary>
    public const int MaxReorderedMarks = 32;

    /// <summary>One character of the normalized text.</summary>
    /// <param name="Character">The character.</param>
    /// <param name="Text">The source characters it shows: its cluster's for the first character of a cluster, none for
    /// the later ones.</param>
    public readonly record struct Normalized(Rune Character, string Text);

    /// <summary>The combining class of a code point (0 for a starter).</summary>
    public static int ClassOf(int codePoint) => Tables.Classes.GetValueOrDefault(codePoint);

    /// <summary>The work of normalizing: one step for each character a round looks at.</summary>
    public sealed class Steps
    {
        /// <summary>The steps so far.</summary>
        public long Count { get; private set; }

        /// <summary>Counts one step.</summary>
        public void Step() => Count++;
    }

    /// <summary>Normalizes one shaping piece for a font (see the remarks).</summary>
    /// <param name="characters">The piece's characters.</param>
    /// <param name="hasGlyph">Whether the font has a glyph for a code point ('cmap').</param>
    /// <returns>The characters to shape, each with its text.</returns>
    public static ImmutableArray<Normalized> ForFont(IReadOnlyList<Rune> characters, Func<int, bool> hasGlyph) =>
        ForFont(characters, hasGlyph, new Steps());

    /// <summary>As <see cref="ForFont(IReadOnlyList{Rune}, Func{int, bool})"/>, counting the work in <paramref name="steps"/>.</summary>
    public static ImmutableArray<Normalized> ForFont(IReadOnlyList<Rune> characters, Func<int, bool> hasGlyph, Steps steps)
    {
        // Each item: the code point and its cluster, an index into the source characters.
        var items = new List<(int CodePoint, int Cluster)>(characters.Count);
        for (var index = 0; index < characters.Count; index++)
        {
            var codePoint = characters[index].Value;
            var clusterEnd = index + 1;
            steps.Step();
            while (clusterEnd < characters.Count && IsMark(characters[clusterEnd].Value))
            {
                steps.Step();
                clusterEnd++;
            }

            var single = clusterEnd == index + 1;
            if (single && hasGlyph(codePoint))
            {
                items.Add((codePoint, index));
                continue;
            }

            for (var member = index; member < clusterEnd; member++)
            {
                var decomposed = new List<int>();
                var parts = Decompose(characters[member].Value, shortest: single, hasGlyph, decomposed) ? decomposed : [characters[member].Value];
                items.AddRange(parts.Select(part => (part, member)));
            }

            index = clusterEnd - 1;
        }

        if (characters.Any(character => IsMark(character.Value)))
        {
            Reorder(items, steps);
            Recompose(items, hasGlyph, steps);
        }

        return Texts(items, characters, steps);
    }

    /// <summary>A mark for the normalizer: general category Mn or Mc (HarfBuzz's <c>HB_UNICODE_GENERAL_CATEGORY_IS_MARK</c>
    /// counts Me as well, see the remarks).</summary>
    private static bool IsMark(int codePoint) => (UnicodeProperties.Of(codePoint).Traits & CharacterTraits.Mark) != 0;

    /// <summary>The canonical decomposition of a code point that the font can show (HarfBuzz's <c>decompose</c>): with
    /// <paramref name="shortest"/>, the first one whose parts the font has; otherwise as far as the font has the parts.
    /// False when there is none.</summary>
    private static bool Decompose(int codePoint, bool shortest, Func<int, bool> hasGlyph, List<int> output)
    {
        if (!PartsOf(codePoint, out var first, out var second) || (second >= 0 && !hasGlyph(second)))
        {
            return false;
        }

        var start = output.Count;
        if ((shortest && hasGlyph(first) && Add(output, first))
            || Decompose(first, shortest, hasGlyph, output)
            || (output.Count == start && hasGlyph(first) && Add(output, first)))
        {
            if (second >= 0)
            {
                output.Add(second);
            }

            return true;
        }

        output.RemoveRange(start, output.Count - start);
        return false;
    }

    private static bool Add(List<int> output, int codePoint)
    {
        output.Add(codePoint);
        return true;
    }

    private static bool PartsOf(int codePoint, out int first, out int second)
    {
        var index = codePoint - SBase;
        if (index is >= 0 and < SCount)
        {
            // An LV syllable decomposes into L and V, an LVT syllable into LV and T (§3.12, "Hangul Syllable Decomposition").
            var trailing = index % TCount;
            (first, second) = trailing == 0
                ? (LBase + index / NCount, VBase + index % NCount / TCount)
                : (codePoint - trailing, TBase + trailing);
            return true;
        }

        if (Tables.Parts.TryGetValue(codePoint, out var parts))
        {
            (first, second) = parts;
            return true;
        }

        (first, second) = (-1, -1);
        return false;
    }

    private static int? Compose(int first, int second)
    {
        var lIndex = first - LBase;
        if (lIndex is >= 0 and < LCount && second - VBase is >= 0 and < VCount)
        {
            return SBase + (lIndex * VCount + second - VBase) * TCount;
        }

        var sIndex = first - SBase;
        if (sIndex is >= 0 and < SCount && sIndex % TCount == 0 && second - TBase is > 0 and < TCount)
        {
            return first + second - TBase;
        }

        return Tables.Compositions.TryGetValue((first, second), out var composed) ? composed : null;
    }

    /// <summary>The canonical ordering: each run of characters with a non-zero combining class sorted by it, stably; a run
    /// whose order changes becomes one cluster. A run longer than <see cref="MaxReorderedMarks"/> is passed over without
    /// being sorted, so the sort works on at most that many characters.</summary>
    private static void Reorder(List<(int CodePoint, int Cluster)> items, Steps steps)
    {
        var start = 0;
        while (start < items.Count)
        {
            steps.Step();
            if (ClassOf(items[start].CodePoint) == 0)
            {
                start++;
                continue;
            }

            var end = start;
            while (end < items.Count && ClassOf(items[end].CodePoint) != 0)
            {
                steps.Step();
                end++;
            }

            if (end - start > MaxReorderedMarks)
            {
                start = end;
                continue;
            }

            var run = items.GetRange(start, end - start);
            var sorted = run.OrderBy(item => ClassOf(item.CodePoint)).ToList();
            if (!sorted.SequenceEqual(run))
            {
                var cluster = run.Min(item => item.Cluster);
                for (var index = 0; index < sorted.Count; index++)
                {
                    items[start + index] = (sorted[index].CodePoint, cluster);
                }
            }

            start = end;
        }
    }

    /// <summary>The recomposition round (see the remarks); a composition and what lies between its parts become one
    /// cluster.</summary>
    private static void Recompose(List<(int CodePoint, int Cluster)> items, Func<int, bool> hasGlyph, Steps steps)
    {
        // The first character is the first starter, whatever its class, as in HarfBuzz.
        var output = new List<(int CodePoint, int Cluster)>(items.Count);
        var starter = 0;

        // Each composition joins the characters after the starter into its cluster. A starter composes only a few times:
        // each composition makes a character whose full canonical decomposition is one character longer, and those are
        // short; so this is a fixed number of passes over the characters after it.
        foreach (var item in items)
        {
            steps.Step();
            if (output.Count > 0 && IsMark(item.CodePoint)
                && (starter == output.Count - 1 || ClassOf(output[^1].CodePoint) < ClassOf(item.CodePoint))
                && Compose(output[starter].CodePoint, item.CodePoint) is { } composed && hasGlyph(composed))
            {
                var cluster = Math.Min(output[starter].Cluster, item.Cluster);
                output[starter] = (composed, cluster);
                for (var index = starter + 1; index < output.Count; index++)
                {
                    steps.Step();
                    output[index] = (output[index].CodePoint, cluster);
                }

                continue;
            }

            output.Add(item);
            if (ClassOf(item.CodePoint) == 0 && output.Count > 1)
            {
                starter = output.Count - 1;
            }
        }

        items.Clear();
        items.AddRange(output);
    }

    /// <summary>Each item's text from the clusters, which never decrease: the first item of a cluster shows the source
    /// characters from its cluster up to the next cluster, the others none. The next cluster of each item is found in one
    /// pass from the end, so a long cluster is walked once, not once per item.</summary>
    private static ImmutableArray<Normalized> Texts(List<(int CodePoint, int Cluster)> items, IReadOnlyList<Rune> characters, Steps steps)
    {
        // nextCluster[i]: the cluster of the first item after i whose cluster differs, or the end of the text.
        var nextCluster = new int[items.Count];
        var following = characters.Count;
        for (var index = items.Count - 1; index >= 0; index--)
        {
            steps.Step();
            nextCluster[index] = following;
            if (index > 0 && items[index - 1].Cluster != items[index].Cluster)
            {
                following = items[index].Cluster;
            }
        }

        var result = ImmutableArray.CreateBuilder<Normalized>(items.Count);
        for (var index = 0; index < items.Count; index++)
        {
            steps.Step();
            var cluster = items[index].Cluster;
            var sharesCluster = index > 0 && items[index - 1].Cluster == cluster;
            var text = sharesCluster ? string.Empty : Source(characters, cluster, nextCluster[index], steps);
            result.Add(new Normalized(new Rune(items[index].CodePoint), text));
        }

        return result.MoveToImmutable();
    }

    private static string Source(IReadOnlyList<Rune> characters, int from, int to, Steps steps)
    {
        var text = new StringBuilder();
        for (var position = from; position < to; position++)
        {
            steps.Step();
            text.Append(characters[position].ToString());
        }

        return text.ToString();
    }
}
