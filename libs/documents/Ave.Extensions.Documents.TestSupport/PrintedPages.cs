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
/// <param name="X1">The origin of its last glyph plus that glyph's advance: the word's extent is its advances. A combining
/// mark drawn after its base ends the word at its own origin plus its advance (0 where the font gives it none), as
/// <c>positions.py</c> reads Chromium's print (case <c>sk-extreme</c>).</param>
/// <param name="Baseline">The baseline's y.</param>
/// <param name="FontSize">The font size.</param>
/// <param name="Text">The characters.</param>
/// <param name="Colour">The text's fill colour as <c>#rrggbb</c> (<c>#rrggbbaa</c> where it is not opaque).</param>
/// <param name="Face">The PostScript name of the font the word is drawn with (without a subset tag): it names the font
/// file, and with it the weight and the style.</param>
public sealed record PrintedWord(int Page, double X0, double X1, double Baseline, double FontSize, string Text, string? Colour = null, string? Face = null)
{
    /// <summary>The word's place in its page's drawing order, where each white-space glyph a glyph run draws takes a place
    /// too: set in the engine's pages (<see cref="PrintedPages.Engine"/>), null in a print read back from its PDF, whose
    /// words are written sorted by place.</summary>
    public int? Drawn { get; init; }

    /// <summary>Whether two words print the same: their places in drawing order are not compared, so an engine word equals
    /// the word read back from Chromium's print.</summary>
    /// <param name="other">The other word.</param>
    /// <returns>Whether they are equal.</returns>
    public bool Equals(PrintedWord? other) =>
        other is not null && (Page, X0, X1, Baseline, FontSize, Text, Colour, Face) == (other.Page, other.X0, other.X1, other.Baseline, other.FontSize, other.Text, other.Colour, other.Face);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Page, X0, X1, Baseline, FontSize, Text, Colour, Face);

    /// <summary>
    /// Whether a reader of the PDF can take the word as the piece of text that comes right after <paramref name="before"/>:
    /// pdftotext and PyMuPDF join two pieces into one word only where the second is drawn right after the first, with
    /// nothing drawn between them, not even white space (case <c>ws-intrinsic</c>: a cell's last word and the next cell's
    /// first are one word where they share a baseline; the words of the two cells' second lines, with the next cell's first
    /// word drawn between them, are two; case <c>ws-table</c>: a cell's word followed by preserved spaces and the next
    /// cell's word are two). A word without a place (read back from a PDF) may follow any.
    /// </summary>
    /// <param name="before">The word before it on its baseline.</param>
    /// <returns>Whether it may continue <paramref name="before"/>.</returns>
    public bool IsDrawnRightAfter(PrintedWord before)
    {
        ArgumentNullException.ThrowIfNull(before);
        return before.Drawn is not { } first || Drawn is not { } second || second == first + 1;
    }
}

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

    /// <summary>The gap between two pieces of text on one baseline, as a share of the font size, below which poppler's
    /// pdftotext reads them as one word (<c>words.txt</c>) where no white space lies between them. Measured with Lato at
    /// 10.5 pt, a word in each of two cells side by side (cases <c>wg-cell-gap</c>, <c>wg-spacing-gap</c>): one word at a
    /// gap of 0.939 pt (0.089 of the size), two at 1.127 pt (0.107). A pair kerned across the border of two runs overlaps,
    /// and a space leaves a quarter of the size or more in the fonts of the cases.</summary>
    public const double PopplerWordGap = 0.1;

    /// <summary>The gap between two pieces of text on one baseline, as a share of the font size, below which PyMuPDF reads
    /// them as one word (<c>positions.py</c>, <c>print.txt</c>) where no white space lies between them and the font, the
    /// size and the colour stay the same. Measured as <see cref="PopplerWordGap"/>: one word at a gap of 1.502 pt (0.143 of
    /// the size), two at 1.690 pt (0.161). So a gap between the two gives one word in <c>print.txt</c> and two in
    /// <c>words.txt</c>.</summary>
    public const double PyMuPdfWordGap = 0.15;

    /// <summary>The words as poppler's pdftotext reads the page (<c>words.txt</c>): the pieces that PyMuPDF (and the engine,
    /// one glyph run per run) gives where a word's colour or font changes are joined again where nothing lies between them
    /// (<see cref="PopplerWordGap"/>) on one baseline at one font size, the second drawn right after the first
    /// (<see cref="PrintedWord.IsDrawnRightAfter"/>); pdftotext starts a new word at a change of size, not of face or
    /// colour. Words read back from a PDF have no place in drawing order, but PyMuPDF has joined every piece it could
    /// already: two of its words are joined only where the face or the colour changes between them (cases
    /// <c>ws-intrinsic</c>, <c>ws-table</c>: the words of two cells' second lines, apart in both files). The joined word
    /// keeps its first piece's colour and face and takes its last piece's place. Each word comes with the number of pieces
    /// it was made of.</summary>
    public ImmutableArray<(PrintedWord Word, int Pieces)> JoinedWords
    {
        get
        {
            var words = ImmutableArray.CreateBuilder<(PrintedWord Word, int Pieces)>();
            PrintedWord? previous = null;
            foreach (var piece in Words)
            {
                if (words.Count > 0 && words[^1] is var (last, pieces) && previous is not null && Math.Abs(last.Baseline - piece.Baseline) < 0.01 && last.FontSize == piece.FontSize
                    && piece.X0 - last.X1 < PopplerWordGap * piece.FontSize && piece.IsDrawnRightAfter(last)
                    && (piece.Drawn is not null || previous.Face != piece.Face || previous.Colour != piece.Colour))
                {
                    words[^1] = (last with { X1 = piece.X1, Text = last.Text + piece.Text, Drawn = piece.Drawn }, pieces + 1);
                    previous = piece;
                    continue;
                }

                words.Add((piece, 1));
                previous = piece;
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
    /// and face are its run's, and its <see cref="PrintedWord.Drawn"/> its place in drawing order. A rectangle's colour is its fill's. A rectangle and an image are the part the page shows,
    /// inside the clips open around them (<see cref="ClipStart"/>, <see cref="Visible"/>), and left out where they lie
    /// wholly outside them, as <c>positions.py</c> reads Chromium's print; a word is whole.
    /// </summary>
    /// <param name="document">The page model.</param>
    /// <returns>The pages.</returns>
    public static PrintedPages Engine(PageDocument document) =>
        new([.. document.Pages.Select((page, index) =>
        {
            var number = index + 1;
            var (rectangles, images) = (ImmutableArray.CreateBuilder<PrintedBox>(), ImmutableArray.CreateBuilder<PrintedBox>());
            var clips = new System.Collections.Generic.Stack<Core.Rect>();
            foreach (var item in page.Items)
            {
                switch (item)
                {
                    case ClipStart start:
                        clips.Push(clips.Count == 0 ? start.Area : Intersection(clips.Peek(), start.Area));
                        break;
                    case ClipEnd:
                        clips.Pop();
                        break;
                    case FillRectangle fill when Visible(Box(number, fill.Area) with { Colour = Hex(fill.Color) }, clips.Count == 0 ? null : clips.Peek()) is { } shown:
                        rectangles.Add(shown);
                        break;
                    case DrawImage image when Visible(Box(number, image.Area), clips.Count == 0 ? null : clips.Peek()) is { } shown:
                        images.Add(shown);
                        break;
                }
            }

            return new PrintedPage(number, page.Size.Width, page.Size.Height,
                Sorted(page.Items.OfType<GlyphRun>().SelectMany(run => Words(number, run)).Select((word, place) => word is null ? null : word with { Drawn = place }).OfType<PrintedWord>()), rectangles.ToImmutable(), images.ToImmutable());
        })]);

    /// <summary>
    /// The part of a painted rectangle or an image area that a clip lets show: the rectangle cut to the clip, or null
    /// where nothing of it lies inside (an edge on the clip's edge shows nothing beyond it). Without a clip, the rectangle
    /// as it is. A PDF paints a drawing only inside the clipping path around it (ISO 32000-1 §8.5.4); Chromium clips
    /// each page's body to the page area and draws what passes it all the same (<c>tools/reference/chromium/visible.py</c>,
    /// which reads its print back by this rule).
    /// </summary>
    /// <param name="box">The rectangle as drawn.</param>
    /// <param name="clip">The rectangle the clips around it leave, or null where no clip is open.</param>
    /// <returns>The part that shows, or null.</returns>
    public static PrintedBox? Visible(PrintedBox box, Core.Rect? clip)
    {
        ArgumentNullException.ThrowIfNull(box);
        if (clip is not { } area)
        {
            return box;
        }

        var (x0, y0) = (Math.Max(box.X0, area.Origin.X), Math.Max(box.Y0, area.Origin.Y));
        var (x1, y1) = (Math.Min(box.X1, area.Origin.X + area.Size.Width), Math.Min(box.Y1, area.Origin.Y + area.Size.Height));
        return x0 < x1 && y0 < y1 ? box with { X0 = x0, Y0 = y0, X1 = x1, Y1 = y1 } : null;
    }

    private static Core.Rect Intersection(Core.Rect outer, Core.Rect inner)
    {
        var (x0, y0) = (Math.Max(outer.Origin.X, inner.Origin.X), Math.Max(outer.Origin.Y, inner.Origin.Y));
        var (x1, y1) = (Math.Min(outer.Origin.X + outer.Size.Width, inner.Origin.X + inner.Size.Width), Math.Min(outer.Origin.Y + outer.Size.Height, inner.Origin.Y + inner.Size.Height));
        return new Core.Rect(new Core.Point(x0, y0), new Core.Size(Math.Max(0, x1 - x0), Math.Max(0, y1 - y0)));
    }

    private static string Hex(Core.Color color) => FormattableString.Invariant($"#{color.Red:x2}{color.Green:x2}{color.Blue:x2}");

    private static PrintedBox Box(int page, Core.Rect area) =>
        new(page, area.Origin.X, area.Origin.Y, area.Origin.X + area.Size.Width, area.Origin.Y + area.Size.Height);

    private static ImmutableArray<PrintedWord> Sorted(System.Collections.Generic.IEnumerable<PrintedWord> words) =>
        [.. words.OrderBy(word => Math.Round(word.Baseline, 3)).ThenBy(word => Math.Round(word.X0, 3))];

    // The words of a run in drawing order, with a null for each white-space glyph: a place in the page's drawing order.
    private static System.Collections.Generic.IEnumerable<PrintedWord?> Words(int page, GlyphRun run)
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

                yield return null;
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
