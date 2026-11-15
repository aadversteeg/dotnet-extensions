using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Ave.Documents.PageModel;
using UglyToad.PdfPig;

namespace Ave.Documents.TestSupport;

/// <summary>
/// Reads a PDF back with PdfPig, an independent PDF reader, and compares what it finds with the page model the
/// PDF was written from: the same characters, at the same positions, in the same fonts.
/// </summary>
public static class PdfReadBack
{
    /// <summary>How far a glyph read back may be from where the page model put it: PDF numbers are written with
    /// four decimals, so anything beyond 0.01 pt is a real error.</summary>
    public const double PositionTolerance = 0.01;

    /// <summary>A glyph as PdfPig reads it, in PDF coordinates (origin bottom-left, y up).</summary>
    /// <param name="Value">The text the PDF's ToUnicode map gives for the glyph.</param>
    /// <param name="X">The glyph origin's x.</param>
    /// <param name="Y">The baseline's y.</param>
    /// <param name="FontName">The font's base name.</param>
    /// <param name="PointSize">The font size.</param>
    public sealed record ReadLetter(string Value, double X, double Y, string FontName, double PointSize);

    /// <summary>The letters on every page, in content-stream order.</summary>
    /// <param name="pdf">The PDF bytes.</param>
    /// <returns>One list of letters per page.</returns>
    public static ImmutableArray<ImmutableArray<ReadLetter>> Letters(ImmutableArray<byte> pdf)
    {
        using var document = PdfDocument.Open(pdf.ToArray());
        return [.. document.GetPages().Select(page => page.Letters
            .Select(letter => new ReadLetter(letter.Value, letter.StartBaseLine.X, letter.StartBaseLine.Y, letter.FontName ?? string.Empty, letter.PointSize))
            .ToImmutableArray())];
    }

    /// <summary>
    /// Compares the PDF with its page model. Returns one line per difference; an empty list means every glyph of
    /// the page model was read back with its text, font and size, within <see cref="PositionTolerance"/> of
    /// where the page model placed it.
    /// </summary>
    /// <param name="model">The page model.</param>
    /// <param name="pdf">The PDF written from it.</param>
    /// <returns>The differences.</returns>
    public static IReadOnlyList<string> Differences(PageDocument model, ImmutableArray<byte> pdf)
    {
        var pages = Letters(pdf);
        if (pages.Length != model.Pages.Count)
        {
            return [$"The PDF has {pages.Length} pages, the page model {model.Pages.Count}."];
        }

        return [.. model.Pages.SelectMany((page, index) => PageDifferences(page, pages[index], index + 1))];
    }

    private static IEnumerable<string> PageDifferences(Page page, ImmutableArray<ReadLetter> letters, int number)
    {
        var expected = page.Items.OfType<GlyphRun>()
            .SelectMany(run => run.Glyphs.Select(glyph => new ReadLetter(
                glyph.Text,
                run.Origin.X + glyph.X,
                page.Size.Height - run.Origin.Y,
                run.Font.Names.PostScriptName,
                run.FontSize)))
            .ToImmutableArray();
        if (expected.Length != letters.Length)
        {
            yield return $"Page {number}: {letters.Length} letters read back, {expected.Length} in the page model.";
            yield break;
        }

        foreach (var (want, got) in expected.Zip(letters))
        {
            var samePlace = Math.Abs(want.X - got.X) <= PositionTolerance && Math.Abs(want.Y - got.Y) <= PositionTolerance;
            var sameFont = got.FontName.EndsWith(want.FontName, StringComparison.Ordinal) && Math.Abs(want.PointSize - got.PointSize) < 1e-6;
            if (want.Value != got.Value || !samePlace || !sameFont)
            {
                yield return $"Page {number}: expected {want}, read {got}.";
            }
        }
    }
}
