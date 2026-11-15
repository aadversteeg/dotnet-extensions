using System.Collections.Immutable;
using System.Linq;
using Ave.Documents.Fonts;
using Ave.Documents.Page;
using Ave.Documents.Pdf;

namespace Ave.Documents.Render.Pdf;

/// <summary>A font the document uses, with the glyphs it uses and the text each glyph stands for.</summary>
/// <param name="Font">The font.</param>
/// <param name="ResourceName">Its name in the pages' /Font resources, for example <c>F1</c>.</param>
/// <param name="GlyphTexts">Every used glyph and the text it shows, ordered by glyph id.</param>
internal sealed record UsedFont(TrueTypeFont Font, PdfName ResourceName, ImmutableSortedDictionary<ushort, string> GlyphTexts)
{
    /// <summary>
    /// Collects the fonts of a document in order of first use, so resource names and object numbers do not
    /// depend on anything but the document. When one glyph shows different texts in different places (for
    /// example the space glyph for both U+0020 and U+00A0), the first one wins: a ToUnicode map can hold only
    /// one text per glyph code.
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
                    .ToImmutableSortedDictionary(glyphs => glyphs.Key, glyphs => glyphs.First().Text)))];
}
