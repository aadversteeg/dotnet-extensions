using System.Collections.Immutable;
using System.Linq;
using Ave.Documents.Fonts;
using Ave.Documents.PageModel;
using Ave.Documents.Pdf;

namespace Ave.Documents.Render.Pdf;

/// <summary>A font the document uses, with the glyphs it uses and the text each glyph stands for.</summary>
/// <param name="Font">The font.</param>
/// <param name="ResourceName">Its name in the pages' /Font resources, for example <c>F1</c>.</param>
/// <param name="GlyphTexts">Every used glyph and its default text (the text its ToUnicode entry gives), ordered by
/// glyph id.</param>
internal sealed record UsedFont(TrueTypeFont Font, PdfName ResourceName, ImmutableSortedDictionary<ushort, string> GlyphTexts)
{
    /// <summary>
    /// Collects the fonts of a document in order of first use, so resource names and object numbers do not
    /// depend on anything but the document. One glyph can show different texts in different places (the space glyph
    /// for U+0020 and U+00A0, the hyphen glyph for U+002D and a soft hyphen at a break), but a ToUnicode map holds one
    /// text per glyph code. A glyph's default text is the text it shows most often, the first one used on a tie; every
    /// other occurrence carries its own text as replacement text where it is drawn (<see cref="NeedsReplacement"/>).
    /// </summary>
    public static ImmutableArray<UsedFont> Collect(PageDocument document) =>
        [.. document.Pages
            .SelectMany(page => page.Items.OfType<GlyphRun>())
            .GroupBy(run => run.Font)
            .Select((runs, index) => new UsedFont(
                runs.Key,
                new PdfName($"F{index + 1}"),
                runs.SelectMany(run => run.Glyphs)
                    .GroupBy(glyph => glyph.Glyph.Value)
                    .ToImmutableSortedDictionary(glyphs => glyphs.Key, DefaultText)))];

    /// <summary>Whether a glyph's text differs from its glyph's default text, so the ToUnicode map alone would give a
    /// reader the wrong text.</summary>
    /// <param name="glyph">A glyph drawn in this font.</param>
    /// <returns>True when the glyph needs its text as replacement text.</returns>
    public bool NeedsReplacement(PositionedGlyph glyph) => GlyphTexts[glyph.Glyph.Value] != glyph.Text;

    /// <summary>The most frequent text of one glyph's occurrences, the first one used on a tie: GroupBy keeps the order
    /// of first appearance, and OrderByDescending is a stable sort.</summary>
    private static string DefaultText(IGrouping<ushort, PositionedGlyph> occurrences) =>
        occurrences.GroupBy(glyph => glyph.Text, System.StringComparer.Ordinal)
            .OrderByDescending(texts => texts.Count())
            .First().Key;
}
