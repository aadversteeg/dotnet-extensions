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

    /// <summary>The glyphs the word is drawn with, in drawing order (<see cref="PrintedGlyph"/>): set in the engine's pages
    /// (<see cref="PrintedPages.Engine"/>) and, for a print read back from its PDF, from its <c>glyphs.txt</c>
    /// (<see cref="PrintedPages.WithGlyphs"/>); empty where they are not known. Not part of <see cref="Equals(PrintedWord?)"/>.</summary>
    public ImmutableArray<PrintedGlyph> Glyphs { get; init; } = [];

    /// <summary>The place in its page's drawing sequence of the text that draws the word, among every item the page draws
    /// (words, fills, images): in the engine's pages the index of its glyph run among the page's items, in a print read
    /// back from its PDF the place <c>order.txt</c> gives (<see cref="PrintedPages.WithOrder"/>); null where it is not known.
    /// Not part of <see cref="Equals(PrintedWord?)"/>.</summary>
    public int? Place { get; init; }

    /// <summary>For a print read back from its PDF, the place of each of <see cref="Glyphs"/> in the drawing sequence
    /// (<c>order.txt</c>): one word can be drawn by several texts (case <c>ib-kerning</c>: "VAV", a letter in an inline box
    /// of its own). Empty for the engine's words, whose glyph run has one place (<see cref="Place"/>).</summary>
    public ImmutableArray<int> Places { get; init; } = [];

    /// <summary>The rectangle the clips open around the word's text leave (<see cref="PrintedPages.Visible"/>), or null where
    /// none is open: in the engine's pages from its clip items, in a print read back from its PDF from <c>clips.txt</c>
    /// (<see cref="PrintedPages.WithClips"/>), a clip taken as its box. Not part of <see cref="Equals(PrintedWord?)"/>.</summary>
    public PrintedBox? Clip { get; init; }

    /// <summary>What is painted over the word: the paints of <see cref="PrintedPage.Paints"/> after it in the drawing
    /// sequence whose box overlaps its box by more than a pixel each way, by colour (or <c>image</c>), in ordinal order, once
    /// each; set by the comparison of paint order, empty before. Not part of <see cref="Equals(PrintedWord?)"/>.</summary>
    public ImmutableArray<string> Over { get; init; } = [];

    /// <summary>The part of the word's box its clips leave (<see cref="Clip"/>), or null where they leave nothing of it;
    /// set by the comparison of clips, the whole box before. Not part of <see cref="Equals(PrintedWord?)"/>.</summary>
    public PrintedBox? Shown { get; init; }

    /// <summary>Whether every clip around the word is one rectangle (<see cref="Clip"/> is then exact); false where one of
    /// them is another path, which <see cref="Clip"/> holds as its box.</summary>
    public bool ClipIsRectangle { get; init; } = true;

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

/// <summary>A glyph of a printed word: the glyph id in its font program and its origin, in points, origin at the top left
/// of the page, y down.</summary>
/// <param name="Glyph">The glyph id.</param>
/// <param name="X">The x of its origin.</param>
/// <param name="Below">The distance of its origin below the word's baseline; negative above it (a raised mark).</param>
/// <param name="End">The x where its advance ends.</param>
public sealed record PrintedGlyph(int Glyph, double X, double Below, double End);

/// <summary>An item a page paints other than text, with its place in the page's drawing sequence: a fill (a rectangle, a
/// path's box; for a print read back from its PDF also a stroke's box) or an image, as the part its clips leave, in points,
/// origin top left, y down.</summary>
/// <param name="Place">Its place in the page's drawing sequence (<see cref="PrintedWord.Place"/>).</param>
/// <param name="X0">The left edge.</param>
/// <param name="Y0">The top edge.</param>
/// <param name="X1">The right edge.</param>
/// <param name="Y1">The bottom edge.</param>
/// <param name="Paint">The colour as <c>#rrggbb</c> (<c>#rrggbbaa</c> where it is not opaque), or <c>image</c>.</param>
public sealed record PrintedPaint(int Place, double X0, double Y0, double X1, double Y1, string Paint);

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

/// <summary>The area a filled or stroked path that is not a rectangle paints (a curve, such as a list marker's disc or
/// circle), on the grid of whole pixels of 0.75 pt from the page's top left: the box of whole pixels that holds every
/// pixel it paints, and per pixel the share that is painted, in fifteenths (<c>tools/reference/chromium/paths.py</c>).
/// A stroke and a fill that paint the same ink are the same area; a disc and a square in one box are not.</summary>
/// <param name="Page">The page, from 1.</param>
/// <param name="X0">The box's left edge, a whole pixel.</param>
/// <param name="Y0">The box's top edge, a whole pixel.</param>
/// <param name="X1">The box's right edge, a whole pixel.</param>
/// <param name="Y1">The box's bottom edge, a whole pixel.</param>
/// <param name="Colour">The paint's colour as <c>#rrggbb</c> (<c>#rrggbbaa</c> where it is not opaque).</param>
/// <param name="Rows">The box's rows from the top, each one hexadecimal digit per pixel from the left: 0 for nothing
/// painted, f for the whole pixel.</param>
public sealed record PrintedPath(int Page, double X0, double Y0, double X1, double Y1, string Colour, ImmutableArray<string> Rows)
{
    /// <summary>Whether two paths paint the same area in the same colour (a record compares its array by reference).</summary>
    /// <param name="other">The other path.</param>
    /// <returns>Whether they are the same.</returns>
    public bool Paints(PrintedPath other) =>
        other is not null && (Page, X0, Y0, X1, Y1, Colour) == (other.Page, other.X0, other.Y0, other.X1, other.Y1, other.Colour) && Rows.SequenceEqual(other.Rows);
}

/// <summary>One printed page: its size and what is on it.</summary>
/// <param name="Number">The page number, from 1.</param>
/// <param name="Width">The page width.</param>
/// <param name="Height">The page height.</param>
/// <param name="Words">The words, sorted by baseline and then by <see cref="PrintedWord.X0"/>.</param>
/// <param name="Rectangles">The painted rectangles, filled and stroked, in drawing order.</param>
/// <param name="Images">The images, in drawing order.</param>
/// <param name="Paths">The areas painted by paths that are not rectangles (curves), in drawing order; empty where there
/// are none.</param>
public sealed record PrintedPage(int Number, double Width, double Height, ImmutableArray<PrintedWord> Words, ImmutableArray<PrintedBox> Rectangles, ImmutableArray<PrintedBox> Images,
    ImmutableArray<PrintedPath> Paths = default)
{
    /// <summary>The areas painted by paths that are not rectangles, in drawing order (empty, never default).</summary>
    public ImmutableArray<PrintedPath> Paths { get; init; } = Paths.IsDefault ? [] : Paths;

    /// <summary>What the page paints other than text, each with its place in the drawing sequence, in drawing order: set in
    /// the engine's pages and, for a print read back from its PDF, from its <c>order.txt</c>; empty where not known.</summary>
    public ImmutableArray<PrintedPaint> Paints { get; init; } = [];


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
                    words[^1] = (last with { X1 = piece.X1, Text = last.Text + piece.Text, Drawn = piece.Drawn, Glyphs = last.Glyphs.AddRange(piece.Glyphs) }, pieces + 1);
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
    /// band a stroke paints), <c>image page x0 y0 x1 y1</c>, <c>path page x0 y0 x1 y1 colour rows</c> (the area a curve
    /// paints, <see cref="PrintedPath"/>; the rows joined by <c>/</c>).
    /// </summary>
    /// <param name="text">The file's text.</param>
    /// <returns>The pages.</returns>
    /// <exception cref="FormatException">A line is not one of the six forms, with exactly the fields of its form.</exception>
    public static PrintedPages Parse(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split(' '))
            .ToImmutableArray();
        static double Number(string value) => double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
        static PrintedBox Box(string[] fields) =>
            new(int.Parse(fields[1], CultureInfo.InvariantCulture), Number(fields[2]), Number(fields[3]), Number(fields[4]), Number(fields[5]),
                fields.Length > 6 ? fields[6] : null, fields[0] == "stroke");
        foreach (var fields in lines.Where(fields => fields.Length != fields[0] switch { "page" => 4, "word" => 9, "rect" or "stroke" => 7, "image" => 6, "path" => 8, _ => -1 }))
        {
            throw new FormatException($"Not a line of positions.py: '{string.Join(' ', fields)}'.");
        }

        var words = lines.Where(fields => fields[0] == "word")
            .Select(fields => new PrintedWord(int.Parse(fields[1], CultureInfo.InvariantCulture), Number(fields[2]), Number(fields[3]), Number(fields[4]), Number(fields[5]), fields[6],
                fields[7], fields[8]))
            .ToImmutableArray();
        var rectangles = lines.Where(fields => fields[0] is "rect" or "stroke").Select(Box).ToImmutableArray();
        var images = lines.Where(fields => fields[0] == "image").Select(Box).ToImmutableArray();
        var paths = lines.Where(fields => fields[0] == "path")
            .Select(fields => new PrintedPath(int.Parse(fields[1], CultureInfo.InvariantCulture), Number(fields[2]), Number(fields[3]), Number(fields[4]), Number(fields[5]), fields[6],
                [.. fields[7].Split('/')]))
            .ToImmutableArray();
        return new([.. lines.Where(fields => fields[0] == "page").Select(fields =>
        {
            var number = int.Parse(fields[1], CultureInfo.InvariantCulture);
            return new PrintedPage(number, Number(fields[2]), Number(fields[3]),
                Sorted(words.Where(word => word.Page == number)),
                [.. rectangles.Where(box => box.Page == number)],
                [.. images.Where(box => box.Page == number)],
                [.. paths.Where(path => path.Page == number)]);
        })]);
    }

    /// <summary>
    /// The print with the glyphs of each word (<see cref="PrintedWord.Glyphs"/>) from the text <c>glyphs.py</c> writes
    /// (<c>glyphs.txt</c>): one line per word, <c>page baseline text</c> and <c>glyph x below end</c> per glyph, the words
    /// in the order of <c>print.txt</c>. A line belongs to the word of its place on its page, and must have its baseline and
    /// its text.
    /// </summary>
    /// <param name="text">The file's text.</param>
    /// <returns>The pages, each word with its glyphs.</returns>
    /// <exception cref="FormatException">A line is not of that form, or does not belong to the word of its place.</exception>
    public PrintedPages WithGlyphs(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(line => line.Split(' ')).ToList();
        static double Number(string value) => double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
        var pages = ImmutableArray.CreateBuilder<PrintedPage>();
        foreach (var page in Pages)
        {
            var rows = lines.Where(fields => fields[0] == page.Number.ToString(CultureInfo.InvariantCulture)).ToList();
            if (rows.Count != page.Words.Length)
            {
                throw new FormatException($"Page {page.Number} has {page.Words.Length} words and {rows.Count} lines of glyphs.");
            }

            pages.Add(page with
            {
                Words = [.. page.Words.Zip(rows).Select(pair =>
                {
                    var (word, fields) = pair;
                    if (fields.Length < 7 || (fields.Length - 3) % 4 != 0 || fields[2] != word.Text || Math.Abs(Number(fields[1]) - word.Baseline) > 0.0005)
                    {
                        throw new FormatException($"Not the glyphs of page {page.Number}'s word '{word.Text}' at {word.Baseline}: '{string.Join(' ', fields)}'.");
                    }

                    return word with
                    {
                        Glyphs = [.. Enumerable.Range(0, (fields.Length - 3) / 4).Select(index =>
                            new PrintedGlyph(int.Parse(fields[3 + (4 * index)], CultureInfo.InvariantCulture), Number(fields[4 + (4 * index)]), Number(fields[5 + (4 * index)]),
                                Number(fields[6 + (4 * index)])))],
                    };
                })],
            });
        }

        return new(pages.ToImmutable());
    }

    /// <summary>
    /// The print with the place in the drawing sequence of each word (<see cref="PrintedWord.Place"/>) and what each page
    /// paints besides text (<see cref="PrintedPage.Paints"/>), from the text <c>order.py</c> writes (<c>order.txt</c>):
    /// <c>word page text place…</c> per word of <c>print.txt</c> in its order (a place per glyph, <see cref="PrintedWord.Places"/>), <c>paint page place x0 y0 x1 y1 colour</c> per fill
    /// or stroke and <c>image page place x0 y0 x1 y1</c> per image.
    /// </summary>
    /// <param name="text">The file's text.</param>
    /// <returns>The pages with their places.</returns>
    /// <exception cref="FormatException">A line is not of those forms, or a word line does not belong to the word of its place.</exception>
    public PrintedPages WithOrder(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(line => line.Split(' ')).ToList();
        foreach (var fields in lines.Where(fields => fields[0] == "word" ? fields.Length < 3 : fields.Length != fields[0] switch { "paint" => 8, "image" => 7, _ => -1 }))
        {
            throw new FormatException($"Not a line of order.py: '{string.Join(' ', fields)}'.");
        }

        static double Number(string value) => double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
        static int Whole(string value) => int.Parse(value, CultureInfo.InvariantCulture);
        return new([.. Pages.Select(page =>
        {
            var on = lines.Where(fields => Whole(fields[1]) == page.Number).ToList();
            var words = on.Where(fields => fields[0] == "word").ToList();
            if (words.Count != page.Words.Length)
            {
                throw new FormatException($"Page {page.Number} has {page.Words.Length} words and {words.Count} word lines of order.txt.");
            }

            return page with
            {
                Words = [.. page.Words.Zip(words).Select(pair => pair.Second[2] == pair.First.Text && (pair.First.Glyphs.IsEmpty || pair.First.Glyphs.Length == pair.Second.Length - 3)
                    ? pair.First with { Places = [.. pair.Second.Skip(3).Select(Whole)], Place = pair.Second.Length > 3 ? Whole(pair.Second[3]) : null }
                    : throw new FormatException($"Not the places of page {page.Number}'s word '{pair.First.Text}': '{string.Join(' ', pair.Second)}'."))],
                Paints = [.. on.Where(fields => fields[0] != "word").Select(fields =>
                    new PrintedPaint(Whole(fields[2]), Number(fields[3]), Number(fields[4]), Number(fields[5]), Number(fields[6]), fields[0] == "image" ? "image" : fields[7]))],
            };
        })]);
    }

    /// <summary>
    /// The print with the clips around each word (<see cref="PrintedWord.Clip"/>, <see cref="PrintedWord.ClipIsRectangle"/>),
    /// from the text <c>clips.py</c> writes (<c>clips.txt</c>): per word of <c>print.txt</c> in its order <c>page text x0 y0 x1
    /// y1 rect|path</c>, or <c>page text none</c> where no clip is open around it.
    /// </summary>
    /// <param name="text">The file's text.</param>
    /// <returns>The pages with the clips of their words.</returns>
    /// <exception cref="FormatException">A line is not of that form, or does not belong to the word of its place.</exception>
    public PrintedPages WithClips(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(line => line.Split(' ')).ToList();
        static double Number(string value) => double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
        return new([.. Pages.Select(page =>
        {
            var rows = lines.Where(fields => fields[0] == page.Number.ToString(CultureInfo.InvariantCulture)).ToList();
            if (rows.Count != page.Words.Length)
            {
                throw new FormatException($"Page {page.Number} has {page.Words.Length} words and {rows.Count} lines of clips.txt.");
            }

            return page with
            {
                Words = [.. page.Words.Zip(rows).Select(pair => pair.Second switch
                {
                    [_, var word, "none"] when word == pair.First.Text => pair.First with { Clip = null },
                    [_, var word, var x0, var y0, var x1, var y1, "rect" or "path"] when word == pair.First.Text =>
                        pair.First with { Clip = new PrintedBox(page.Number, Number(x0), Number(y0), Number(x1), Number(y1)), ClipIsRectangle = pair.Second[6] == "rect" },
                    _ => throw new FormatException($"Not the clips of page {page.Number}'s word '{pair.First.Text}': '{string.Join(' ', pair.Second)}'."),
                })],
            };
        })]);
    }

    /// <summary>
    /// The engine's page model in the same form. A word is a run of glyphs whose text has no white space; a glyph with no
    /// text of its own continues the word before it (the later glyphs of a one-to-many substitution). Its baseline is its
    /// run's, its extent runs from its first glyph's origin to its last glyph's origin plus that glyph's advance; its colour
    /// and face are its run's, and its <see cref="PrintedWord.Drawn"/> its place in drawing order. A rectangle's colour is its fill's. A rectangle and an image are the part the page shows,
    /// inside the clips open around them (<see cref="ClipStart"/>, <see cref="Visible"/>), and left out where they lie
    /// wholly outside them, as <c>positions.py</c> reads Chromium's print; a word is whole. A filled path is the area it
    /// paints inside those clips, sampled by the rule <c>paths.py</c> reads Chromium's curves with (<see cref="PathSampler"/>),
    /// and left out where it paints no pixel.
    /// </summary>
    /// <param name="document">The page model.</param>
    /// <returns>The pages.</returns>
    public static PrintedPages Engine(PageDocument document) =>
        new([.. document.Pages.Select((page, index) =>
        {
            var number = index + 1;
            var (rectangles, images) = (ImmutableArray.CreateBuilder<PrintedBox>(), ImmutableArray.CreateBuilder<PrintedBox>());
            var paths = ImmutableArray.CreateBuilder<PrintedPath>();
            var paints = ImmutableArray.CreateBuilder<PrintedPaint>();
            var runs = new System.Collections.Generic.List<(GlyphRun Run, int Place, PrintedBox? Clip)>();
            var clips = new System.Collections.Generic.Stack<Core.Rect>();
            foreach (var (item, place) in page.Items.Select((item, place) => (item, place)))
            {
                var clip = clips.Count == 0 ? (Core.Rect?)null : clips.Peek();
                switch (item)
                {
                    case GlyphRun run:
                        runs.Add((run, place, clip is { } open ? Box(number, open) : null));
                        break;
                    case FillRectangle fill when Visible(Box(number, fill.Area), clip) is { } shown:
                        paints.Add(new PrintedPaint(place, shown.X0, shown.Y0, shown.X1, shown.Y1, Hex(fill.Color)));
                        break;
                    case DrawImage image when Visible(Box(number, image.Area), clip) is { } shown:
                        paints.Add(new PrintedPaint(place, shown.X0, shown.Y0, shown.X1, shown.Y1, "image"));
                        break;
                    case FillPath path when Visible(Bounds(number, path), clip) is { } shown:
                        paints.Add(new PrintedPaint(place, shown.X0, shown.Y0, shown.X1, shown.Y1, Hex(path.Color)));
                        break;
                }

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
                    case FillPath path when PathSampler.Sample(number, path, clips.Count == 0 ? null : clips.Peek()) is { } painted:
                        paths.Add(painted);
                        break;
                }
            }

            return new PrintedPage(number, page.Size.Width, page.Size.Height,
                Sorted(runs.SelectMany(run => Words(number, run.Run).Select(word => word is null ? null : word with { Place = run.Place, Clip = run.Clip }))
                    .Select((word, place) => word is null ? null : word with { Drawn = place }).OfType<PrintedWord>()), rectangles.ToImmutable(), images.ToImmutable(), paths.ToImmutable())
            {
                Paints = paints.ToImmutable(),
            };
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

    // The box of a path's contours: of their points, the control points of their curves included, as a PDF reader bounds a
    // path (PyMuPDF's drawing rectangle).
    private static PrintedBox Bounds(int page, FillPath path)
    {
        var points = path.Contours.SelectMany(contour => new[] { contour.Start }.Concat(contour.Segments.SelectMany(segment => segment switch
        {
            PathCubic cubic => new[] { cubic.Control1, cubic.Control2, cubic.End },
            _ => new[] { segment.End },
        }))).ToList();
        return new PrintedBox(page, points.Min(point => point.X), points.Min(point => point.Y), points.Max(point => point.X), points.Max(point => point.Y));
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
            string.Concat(glyphs.Select(glyph => glyph.Text)), Hex(run.Color), run.Font.Names.PostScriptName)
        {
            Glyphs = [.. glyphs.Select(glyph => new PrintedGlyph(glyph.Glyph.Value, run.Origin.X + glyph.X, glyph.Y,
                run.Origin.X + glyph.X + run.Font.Metrics.ToPoints(run.Font.AdvanceWidth(glyph.Glyph), run.FontSize)))],
        };
    }
}
