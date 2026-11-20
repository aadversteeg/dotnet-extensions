using System;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using Ave.Extensions.Documents.PageModel;

namespace Ave.Extensions.Documents.TestSupport;

/// <summary>A word on a printed page: a run of characters without white space on one baseline, in points, origin at the
/// top left of the page, y down.</summary>
/// <param name="Page">The page, from 1.</param>
/// <param name="X0">The origin of its first character.</param>
/// <param name="X1">The origin of its last character plus that character's advance: the word's extent is its advances.</param>
/// <param name="Baseline">The baseline's y.</param>
/// <param name="FontSize">The font size.</param>
/// <param name="Text">The characters.</param>
/// <param name="Colour">The text's fill colour as <c>#rrggbb</c> (<c>#rrggbbaa</c> where it is not opaque).</param>
/// <param name="Face">The PostScript name of the font the word is drawn with (without a subset tag): it names the font
/// file, and with it the weight and the style.</param>
public sealed record PrintedWord(int Page, double X0, double X1, double Baseline, double FontSize, string Text, string? Colour = null, string? Face = null);

/// <summary>A rectangle on a printed page: a painted rectangle (a background, a border, a grid line; filled, or one band
/// of a stroked rectangle) or the area an image is drawn into (which may reach past the page when the page shows only a
/// slice of it). Points, origin top left, y down.</summary>
/// <param name="Page">The page, from 1.</param>
/// <param name="X0">The left edge.</param>
/// <param name="Y0">The top edge.</param>
/// <param name="X1">The right edge.</param>
/// <param name="Y1">The bottom edge.</param>
/// <param name="Colour">A painted rectangle's colour as <c>#rrggbb</c> (<c>#rrggbbaa</c> where it is not opaque); null for
/// an image.</param>
/// <param name="Stroked">Whether a stroke paints it (a band of a stroked rectangle, read back as what the stroke paints),
/// not a fill.</param>
public sealed record PrintedBox(int Page, double X0, double Y0, double X1, double Y1, string? Colour = null, bool Stroked = false)
{
    /// <summary>The height.</summary>
    public double Height => Y1 - Y0;
}

/// <summary>One printed page: its size and what is on it.</summary>
/// <param name="Number">The page number, from 1.</param>
/// <param name="Width">The page width.</param>
/// <param name="Height">The page height.</param>
/// <param name="Words">The words, sorted by baseline and then by <see cref="PrintedWord.X0"/>.</param>
/// <param name="Rectangles">The painted rectangles, filled and stroked, in drawing order.</param>
/// <param name="Images">The images, in drawing order.</param>
public sealed record PrintedPage(int Number, double Width, double Height, ImmutableArray<PrintedWord> Words, ImmutableArray<PrintedBox> Rectangles, ImmutableArray<PrintedBox> Images)
{
    /// <summary>The words of the page joined by single spaces, in reading order (baseline, then x).</summary>
    public string Text => string.Join(" ", Words.Select(word => word.Text));

    /// <summary>The baselines on the page from the top, each with its words joined by spaces: one entry per baseline, so
    /// cells side by side on one baseline make one line.</summary>
    public ImmutableArray<(double Baseline, string Text)> Lines =>
        [.. Words.GroupBy(word => Math.Round(word.Baseline, 3))
            .Select(line => (line.First().Baseline, string.Join(" ", line.Select(word => word.Text))))];

    /// <summary>The widest gap between two pieces of text on one baseline that still counts as no white space between
    /// them, in points: a pair kerned across the border of two runs overlaps, and a space leaves 2.7 pt or more at the
    /// sizes of the cases. Both rules that join pieces into words use it (<see cref="JoinedWords"/>, and the HTML case
    /// comparison's join of PyMuPDF's words).</summary>
    public const double WordGap = 0.5;

    /// <summary>The words as poppler's pdftotext reads the page (<c>words.txt</c>): the pieces that PyMuPDF (and the engine,
    /// one glyph run per run) gives where a word's colour or font changes are joined again where nothing lies between them
    /// (<see cref="WordGap"/>) on one baseline at one font size; pdftotext starts a new word at a change of size, not of
    /// face or colour. The joined word keeps its first piece's colour and face. Each word comes with the number of pieces
    /// it was made of.</summary>
    public ImmutableArray<(PrintedWord Word, int Pieces)> JoinedWords
    {
        get
        {
            var words = ImmutableArray.CreateBuilder<(PrintedWord Word, int Pieces)>();
            foreach (var piece in Words)
            {
                if (words.Count > 0 && words[^1] is var (last, pieces) && Math.Abs(last.Baseline - piece.Baseline) < 0.01 && last.FontSize == piece.FontSize && piece.X0 - last.X1 < WordGap)
                {
                    words[^1] = (last with { X1 = piece.X1, Text = last.Text + piece.Text }, pieces + 1);
                    continue;
                }

                words.Add((piece, 1));
            }

            return words.ToImmutable();
        }
    }
}

/// <summary>
/// Where things are on printed pages, in one form for both sides of a comparison: Chromium's print of a reference case,
/// read back from its PDF (<c>tools/reference/chromium/positions.py</c>, the committed <c>measured/&lt;case&gt;.print.txt</c>
/// files), and the engine's own page model. A layout test compares the two page by page: which words are on which page,
/// and where (README of <c>tools/reference/chromium</c>, "Print positions"). Both sides describe the same thing in the
/// same way: words by their baseline and advances (not their ink), filled rectangles, image areas.
/// </summary>
/// <param name="Pages">The pages, in order.</param>
public sealed record PrintedPages(ImmutableArray<PrintedPage> Pages)
{
    /// <summary>The words of every page (<see cref="PrintedPage.Text"/>), one string per page.</summary>
    public ImmutableArray<string> PageTexts => [.. Pages.Select(page => page.Text)];

    /// <summary>Chromium's print of a reference case, as measured and committed (copied next to the test assembly).</summary>
    /// <param name="caseName">The case's name, for example <c>a1-empty-block-margin</c>.</param>
    /// <returns>The pages.</returns>
    public static PrintedPages Chromium(string caseName) =>
        Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "chromium", caseName + ".print.txt")));

    /// <summary>
    /// Reads the text form <c>positions.py</c> writes: one line per item, <c>page n width height</c>, <c>word page x0 x1
    /// baseline size text colour face</c>, <c>rect page x0 y0 x1 y1 colour</c>, <c>stroke page x0 y0 x1 y1 colour</c> (a
    /// band a stroke paints), <c>image page x0 y0 x1 y1</c>.
    /// </summary>
    /// <param name="text">The file's text.</param>
    /// <returns>The pages.</returns>
    /// <exception cref="FormatException">A line is not one of the five forms, with exactly the fields of its form.</exception>
    public static PrintedPages Parse(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split(' '))
            .ToImmutableArray();
        static double Number(string value) => double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
        static PrintedBox Box(string[] fields) =>
            new(int.Parse(fields[1], CultureInfo.InvariantCulture), Number(fields[2]), Number(fields[3]), Number(fields[4]), Number(fields[5]),
                fields.Length > 6 ? fields[6] : null, fields[0] == "stroke");
        foreach (var fields in lines.Where(fields => fields.Length != fields[0] switch { "page" => 4, "word" => 9, "rect" or "stroke" => 7, "image" => 6, _ => -1 }))
        {
            throw new FormatException($"Not a line of positions.py: '{string.Join(' ', fields)}'.");
        }

        var words = lines.Where(fields => fields[0] == "word")
            .Select(fields => new PrintedWord(int.Parse(fields[1], CultureInfo.InvariantCulture), Number(fields[2]), Number(fields[3]), Number(fields[4]), Number(fields[5]), fields[6],
                fields[7], fields[8]))
            .ToImmutableArray();
        var rectangles = lines.Where(fields => fields[0] is "rect" or "stroke").Select(Box).ToImmutableArray();
        var images = lines.Where(fields => fields[0] == "image").Select(Box).ToImmutableArray();
        return new([.. lines.Where(fields => fields[0] == "page").Select(fields =>
        {
            var number = int.Parse(fields[1], CultureInfo.InvariantCulture);
            return new PrintedPage(number, Number(fields[2]), Number(fields[3]),
                Sorted(words.Where(word => word.Page == number)),
                [.. rectangles.Where(box => box.Page == number)],
                [.. images.Where(box => box.Page == number)]);
        })]);
    }

    /// <summary>
    /// The engine's page model in the same form. A word is a run of glyphs whose text has no white space; a glyph with no
    /// text of its own continues the word before it (the later glyphs of a one-to-many substitution). Its baseline is its
    /// run's, its extent runs from its first glyph's origin to its last glyph's origin plus that glyph's advance; its colour
    /// and face are its run's. A rectangle's colour is its fill's.
    /// </summary>
    /// <param name="document">The page model.</param>
    /// <returns>The pages.</returns>
    public static PrintedPages Engine(PageDocument document) =>
        new([.. document.Pages.Select((page, index) =>
        {
            var number = index + 1;
            return new PrintedPage(number, page.Size.Width, page.Size.Height,
                Sorted(page.Items.OfType<GlyphRun>().SelectMany(run => Words(number, run))),
                [.. page.Items.OfType<FillRectangle>().Select(fill => Box(number, fill.Area) with { Colour = Hex(fill.Color) })],
                [.. page.Items.OfType<DrawImage>().Select(image => Box(number, image.Area))]);
        })]);

    private static string Hex(Core.Color color) => FormattableString.Invariant($"#{color.Red:x2}{color.Green:x2}{color.Blue:x2}");

    private static PrintedBox Box(int page, Core.Rect area) =>
        new(page, area.Origin.X, area.Origin.Y, area.Origin.X + area.Size.Width, area.Origin.Y + area.Size.Height);

    private static ImmutableArray<PrintedWord> Sorted(System.Collections.Generic.IEnumerable<PrintedWord> words) =>
        [.. words.OrderBy(word => Math.Round(word.Baseline, 3)).ThenBy(word => Math.Round(word.X0, 3))];

    private static System.Collections.Generic.IEnumerable<PrintedWord> Words(int page, GlyphRun run)
    {
        var word = ImmutableArray.CreateBuilder<PositionedGlyph>();
        foreach (var glyph in run.Glyphs)
        {
            if (glyph.Text.Length > 0 && glyph.Text.All(char.IsWhiteSpace))
            {
                if (word.Count > 0)
                {
                    yield return Word(page, run, word.ToImmutable());
                    word.Clear();
                }

                continue;
            }

            word.Add(glyph);
        }

        if (word.Count > 0)
        {
            yield return Word(page, run, word.ToImmutable());
        }
    }

    private static PrintedWord Word(int page, GlyphRun run, ImmutableArray<PositionedGlyph> glyphs)
    {
        var advance = run.Font.Metrics.ToPoints(run.Font.AdvanceWidth(glyphs[^1].Glyph), run.FontSize);
        return new PrintedWord(page, run.Origin.X + glyphs[0].X, run.Origin.X + glyphs[^1].X + advance, run.Origin.Y, run.FontSize,
            string.Concat(glyphs.Select(glyph => glyph.Text)), Hex(run.Color), run.Font.Names.PostScriptName);
    }
}
