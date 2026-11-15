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

    /// <summary>
    /// The letters on every page, in content-stream order, each with the text a reader gets for it: the ToUnicode text
    /// of its glyph or, inside a marked-content sequence with an <c>/ActualText</c> entry, that replacement text for
    /// the content the sequence encloses (ISO 32000-1 §14.9.4). The sequence's first letter carries the whole
    /// replacement, the others none.
    /// </summary>
    /// <param name="pdf">The PDF bytes.</param>
    /// <returns>One list of letters per page.</returns>
    public static ImmutableArray<ImmutableArray<ReadLetter>> Letters(ImmutableArray<byte> pdf)
    {
        using var document = PdfDocument.Open(pdf.ToArray());
        return [.. document.GetPages().Select(page =>
        {
            var replacements = Replacements(page.GetMarkedContents());
            return page.Letters
                .Select(letter => new ReadLetter(
                    replacements.TryGetValue(Key(letter), out var text) ? text : letter.Value,
                    letter.StartBaseLine.X,
                    letter.StartBaseLine.Y,
                    letter.FontName ?? string.Empty,
                    letter.PointSize))
                .ToImmutableArray();
        })];
    }

    /// <summary>The text of every page as a reader extracts it: the letters' texts (<see cref="Letters"/>) in
    /// content-stream order, nothing added between lines.</summary>
    /// <param name="pdf">The PDF bytes.</param>
    /// <returns>One string per page.</returns>
    public static ImmutableArray<string> Text(ImmutableArray<byte> pdf) =>
        [.. Letters(pdf).Select(letters => string.Concat(letters.Select(letter => letter.Value)))];

    /// <summary>Every <c>/ActualText</c> of every page's marked content, in content order.</summary>
    /// <param name="pdf">The PDF bytes.</param>
    /// <returns>One list of replacement texts per page.</returns>
    public static ImmutableArray<ImmutableArray<string>> ActualTexts(ImmutableArray<byte> pdf)
    {
        using var document = PdfDocument.Open(pdf.ToArray());
        return [.. document.GetPages().Select(page => Flatten(page.GetMarkedContents())
            .Select(element => element.ActualText)
            .OfType<string>()
            .ToImmutableArray())];
    }

    private static IEnumerable<UglyToad.PdfPig.Content.MarkedContentElement> Flatten(IEnumerable<UglyToad.PdfPig.Content.MarkedContentElement> elements) =>
        elements.SelectMany(element => Flatten(element.Children).Prepend(element));

    private static (double X, double Y, string Value) Key(UglyToad.PdfPig.Content.Letter letter) =>
        (Math.Round(letter.StartBaseLine.X, 3), Math.Round(letter.StartBaseLine.Y, 3), letter.Value);

    /// <summary>For each letter inside a sequence with ActualText, what it reads as.</summary>
    private static Dictionary<(double X, double Y, string Value), string> Replacements(IEnumerable<UglyToad.PdfPig.Content.MarkedContentElement> elements)
    {
        var replacements = new Dictionary<(double X, double Y, string Value), string>();
        var pending = new Stack<UglyToad.PdfPig.Content.MarkedContentElement>(elements);
        while (pending.Count > 0)
        {
            var element = pending.Pop();
            if (element.ActualText is { } text)
            {
                foreach (var (letter, index) in element.Letters.Select((letter, index) => (letter, index)))
                {
                    replacements[Key(letter)] = index == 0 ? text : string.Empty;
                }

                continue;
            }

            foreach (var child in element.Children)
            {
                pending.Push(child);
            }
        }

        return replacements;
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
                page.Size.Height - (run.Origin.Y + glyph.Y),
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

    /// <summary>A filled rectangle or an image as PdfPig reads it, in PDF coordinates (origin bottom-left, y up).</summary>
    /// <param name="Kind">"fill" or "image".</param>
    /// <param name="Left">The left edge.</param>
    /// <param name="Bottom">The bottom edge.</param>
    /// <param name="Width">The width.</param>
    /// <param name="Height">The height.</param>
    /// <param name="Rgb">For a fill, its colour as 8-bit red, green and blue; empty for an image.</param>
    public sealed record ReadArea(string Kind, double Left, double Bottom, double Width, double Height, string Rgb)
    {
        /// <summary>True when the two are the same kind, colour and place, within <see cref="PositionTolerance"/>.</summary>
        /// <param name="other">The other area.</param>
        /// <returns>Whether they match.</returns>
        public bool Matches(ReadArea other) =>
            Kind == other.Kind && Rgb == other.Rgb
            && Math.Abs(Left - other.Left) <= PositionTolerance && Math.Abs(Bottom - other.Bottom) <= PositionTolerance
            && Math.Abs(Width - other.Width) <= PositionTolerance && Math.Abs(Height - other.Height) <= PositionTolerance;
    }

    /// <summary>
    /// Compares the filled rectangles and the images of the PDF with its page model: the same number on every page, in
    /// painting order, each in the same place and size and — for a fill — the same colour, read back as RGB. Swapped
    /// colour channels, a rectangle moved or flipped, an image drawn at the wrong size all show up here. Returns one
    /// line per difference.
    /// </summary>
    /// <param name="model">The page model.</param>
    /// <param name="pdf">The PDF written from it.</param>
    /// <returns>The differences.</returns>
    public static IReadOnlyList<string> AreaDifferences(PageDocument model, ImmutableArray<byte> pdf)
    {
        using var document = PdfDocument.Open(pdf.ToArray());
        var pages = document.GetPages().ToList();
        if (pages.Count != model.Pages.Count)
        {
            return [$"The PDF has {pages.Count} pages, the page model {model.Pages.Count}."];
        }

        var differences = new List<string>();
        for (var index = 0; index < pages.Count; index++)
        {
            var page = model.Pages[index];
            var expectedFills = page.Items.OfType<FillRectangle>().Select(fill => Expected("fill", fill.Area, page, Rgb(fill.Color.Red, fill.Color.Green, fill.Color.Blue))).ToList();
            var expectedImages = page.Items.OfType<DrawImage>().Select(image => Expected("image", image.Area, page, string.Empty)).ToList();
            var readFills = pages[index].Paths.Where(path => path.IsFilled && path.GetBoundingRectangle() is not null)
                .Select(path => Read("fill", path.GetBoundingRectangle()!.Value, RgbOf(path.FillColor))).ToList();
            var readImages = pages[index].GetImages().Select(image => Read("image", image.BoundingBox, string.Empty)).ToList();
            foreach (var (kind, expected, read) in new[] { ("fill", expectedFills, readFills), ("image", expectedImages, readImages) })
            {
                if (expected.Count != read.Count)
                {
                    differences.Add($"Page {index + 1}: {read.Count} {kind}s read back, {expected.Count} in the page model.");
                    continue;
                }

                differences.AddRange(expected.Zip(read).Where(pair => !pair.First.Matches(pair.Second)).Select(pair => $"Page {index + 1}: expected {pair.First}, read {pair.Second}."));
            }
        }

        return differences;
    }

    private static ReadArea Expected(string kind, Ave.Documents.Core.Rect area, Page page, string rgb) =>
        new(kind, area.Origin.X, page.Size.Height - area.Origin.Y - area.Size.Height, area.Size.Width, area.Size.Height, rgb);

    private static ReadArea Read(string kind, UglyToad.PdfPig.Core.PdfRectangle box, string rgb) =>
        new(kind, box.Left, box.Bottom, box.Width, box.Height, rgb);

    private static string RgbOf(UglyToad.PdfPig.Graphics.Colors.IColor? color)
    {
        if (color is null)
        {
            return "none";
        }

        var (red, green, blue) = color.ToRGBValues();
        return Rgb((byte)Math.Round(red * 255), (byte)Math.Round(green * 255), (byte)Math.Round(blue * 255));
    }

    private static string Rgb(byte red, byte green, byte blue) => $"#{red:X2}{green:X2}{blue:X2}";
}
