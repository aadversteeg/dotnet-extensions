using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Css;
using Ave.Extensions.Documents.Fonts;
using Ave.Extensions.Documents.Html;
using Ave.Extensions.Documents.Html.Boxes;
using Ave.Extensions.Documents.Html.Parsing;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace UnitTests.Extensions.Documents.Html.Boxes;

/// <summary>
/// The Chromium reference cases (<c>tools/reference/chromium/cases</c>) through the whole HTML path, unchanged: parse,
/// resolve the styles (the resolver serves <c>common.css</c> and the font files from the repository), generate the
/// boxes, lay out; then the engine's pages beside every file measured for the case in <c>measured/</c> (README of
/// <c>tools/reference/chromium</c>): the print read back from Chromium's PDF (<c>&lt;case&gt;.print.txt</c>: pages,
/// words with their colour and face, painted rectangles with their colour, images), its filled rectangles read back by a
/// second script (<c>&lt;case&gt;.lines.txt</c>), its words read back by poppler (<c>&lt;case&gt;.words.txt</c>), and the
/// sizes of the boxes measured in the page (<c>&lt;case&gt;.boxes.txt</c>). Each comparison below names its file and its
/// bound.
/// </summary>
internal static class HtmlCases
{
    /// <summary>
    /// How far a position or a length may be from Chromium's, in points: 0.1 pt. Where it comes from: the whole-print
    /// tests of the code front-end compare baselines within one Chromium layout unit (1/64 px, 0.0117 pt,
    /// <c>FontMetricRoundingTests</c>), and x positions differ by Chromium's own font-size rounding, which scales the
    /// advances of a 10 pt line by 0.99961 (<c>ChromiumLayout.FontSizeFactor</c>): 0.08 pt at the end of a 210 pt line.
    /// The bound covers both and is no wider than 0.1 pt, the widest the reference rules of the family allow. Baselines and
    /// the edges of rectangles and images are compared after the one snapping rule of <see cref="WholePixel"/>, which the
    /// bound does not widen; the x positions of words, font sizes and box heights are compared as they are.
    /// </summary>
    public const double Bound = 0.1;

    /// <summary>
    /// The cases whose difference is a listed limit of layout, pinned by name in the code front-end's tests
    /// (<c>ListedDifferenceTests</c> of <c>UnitTests.Extensions.Documents.Fluent</c>; docs/architecture.md, "Known limits"):
    /// the status file says <c>listed</c>, the test name and the first difference, so the case must still differ, and
    /// differ first in the same fact by the same amount.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Listed = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["fr-old-forced-after-empty"] = "A_forced_break_after_only_empty_boxes_is_not_taken",
        ["fr-old-forced-in-keep-together"] = "A_forced_break_inside_a_box_kept_together_moves_the_whole_box_first",
        ["fr-old-empty-box-past-end"] = "An_empty_box_whose_margin_carries_it_past_the_page_end_stays_on_the_page",
        ["fr-old-blank-last-page"] = "An_empty_box_past_the_end_of_the_last_page_adds_no_page",
    };

    /// <summary>The statuses the status test accepts, as the second field of a line, compared whole: <c>equal</c>,
    /// <c>listed</c> (a listed limit, <see cref="Listed"/>), <c>refused</c> (the first refusal, with its line and column)
    /// and <c>open</c> (a known difference the committed file names; while one is open the branch is not ready for a pull
    /// request). <c>different</c> and <c>failed</c> are never accepted.</summary>
    public static readonly IReadOnlySet<string> Accepted = new HashSet<string>(StringComparer.Ordinal) { "equal", "listed", "refused", "open" };

    /// <summary>The codes that are a refusal of the HTML path, by their exact code: what the CSS stage and the HTML
    /// front-end refuse to draw (<see cref="CssErrorCodes.Unsupported"/>, <see cref="HtmlStyleErrorCodes.Unsupported"/>).
    /// Any other error, also another code under the same parent, is a failure of the case.</summary>
    private static readonly HashSet<string> Refusals = new(StringComparer.Ordinal)
    {
        CssErrorCodes.Unsupported.Property.ToString(),
        CssErrorCodes.Unsupported.Value.ToString(),
        CssErrorCodes.Unsupported.Selector.ToString(),
        CssErrorCodes.Unsupported.AtRule.ToString(),
        CssErrorCodes.Unsupported.MediaFeature.ToString(),
        CssErrorCodes.Unsupported.Calc.ToString(),
        CssErrorCodes.Unsupported.Encoding.ToString(),
        CssErrorCodes.Unsupported.ColorFunction.ToString(),
        HtmlStyleErrorCodes.Unsupported.Attribute.ToString(),
        HtmlStyleErrorCodes.Unsupported.Element.ToString(),
        HtmlStyleErrorCodes.Unsupported.Script.ToString(),
        HtmlStyleErrorCodes.Unsupported.QuirksMode.ToString(),
        HtmlStyleErrorCodes.Unsupported.Value.ToString(),
        HtmlStyleErrorCodes.Unsupported.Rendering.ToString(),
        HtmlStyleErrorCodes.Unsupported.BlockInInline.ToString(),
        HtmlStyleErrorCodes.Unsupported.CanvasBackground.ToString(),
        HtmlStyleErrorCodes.Unsupported.PageRule.ToString(),
        HtmlStyleErrorCodes.Unsupported.Font.ToString(),
        HtmlStyleErrorCodes.Unsupported.TooDeep.ToString(),
    };

    /// <summary>The cases folder in the source tree.</summary>
    public static string Folder([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", "tools", "reference", "chromium", "cases"));

    /// <summary>The committed status file: one line per case, <c>name status rest</c> (<see cref="Accepted"/>).</summary>
    public static string StatusFile => Path.Combine(Folder(), "..", "html-status.txt");

    /// <summary>Every case, by name, in ordinal order.</summary>
    public static IReadOnlyList<string> Names =>
        [.. Directory.GetFiles(Folder(), "*.html").Select(Path.GetFileNameWithoutExtension).Order(StringComparer.Ordinal)!];

    /// <summary>The family folder: what the cases' relative URLs may reach.</summary>
    private static string Family => Path.GetFullPath(Path.Combine(Folder(), "..", "..", "..", ".."));

    /// <summary>The measured files of a case, without their extension.</summary>
    private static string Measured(string name) => Path.Combine(Folder(), "..", "measured", name);

    /// <summary>A status line by its fields: the case's name, its status (the second field) and the rest.</summary>
    /// <param name="line">The line.</param>
    /// <returns>The fields; an empty status where the line has none.</returns>
    public static (string Name, string Status, string Text) Fields(string line)
    {
        var fields = line.Split(' ', 3);
        return (fields[0], fields.Length > 1 ? fields[1] : string.Empty, fields.Length > 2 ? fields[2] : string.Empty);
    }

    /// <summary>The resources of a case: files under the family folder, by file URL.</summary>
    public static HtmlResource? Resource(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !uri.IsFile)
        {
            return null;
        }

        var path = Path.GetFullPath(uri.LocalPath);
        return path.StartsWith(Family, StringComparison.Ordinal) && File.Exists(path) ? new HtmlResource(File.ReadAllBytes(path)) : null;
    }

    /// <summary>The parsed HTML of a case.</summary>
    private static Result<HtmlParseResult, Error> Parsed(string name) =>
        HtmlParser.Parse(File.ReadAllText(Path.Combine(Folder(), name + ".html")), new HtmlParseOptions(name + ".html"));

    /// <summary>The boxes of a case.</summary>
    public static Result<HtmlBoxes, Error> Boxes(string name, UnsupportedPolicy policy = UnsupportedPolicy.Refuse)
    {
        var baseUrl = new Uri(Path.Combine(Folder(), name + ".html")).AbsoluteUri;
        return Parsed(name)
            .OnSuccessBind(parsed => HtmlStyleResolver.Resolve(parsed, new HtmlStyleOptions { Resources = Resource, BaseUrl = baseUrl, Unsupported = policy }))
            .OnSuccessBind(styled => HtmlBoxGenerator.Generate(styled, new HtmlBoxOptions { Resources = Resource, BaseUrl = baseUrl, Unsupported = policy }));
    }

    /// <summary>
    /// The status of a case: <c>equal</c>; <c>listed test: difference</c>; <c>refused code what at line:column</c>;
    /// <c>open difference</c> where the committed status of the case is <c>open</c>; otherwise <c>different difference</c>
    /// or <c>failed …</c>, which the status test never accepts. Only the committed file makes a case <c>open</c>, so writing
    /// the file anew never turns a new difference into an accepted one.
    /// </summary>
    /// <param name="name">The case.</param>
    /// <param name="committed">The status the committed file gives the case (its second field), or null.</param>
    /// <returns>The status, without the name.</returns>
    public static string Status(string name, string? committed = null)
    {
        var boxes = Boxes(name);
        if (boxes.IsFailure)
        {
            var first = boxes.Error.Parts().First();
            var metadata = first.Metadata;
            var what = metadata is { } named && named.TryGetValue(ErrorMetadataKeys.Property, out var property)
                ? $"{property}: {named[ErrorMetadataKeys.Value]}"
                : metadata is { } other && other.TryGetValue(ErrorMetadataKeys.Value, out var value) ? $"{value}" : first.Message;
            if (!Refusals.Contains(first.Code.ToString()))
            {
                return $"failed {first.Code} {first.Message}";
            }

            return metadata is { } placed && placed.TryGetValue(ErrorMetadataKeys.SourceLine, out var line) && placed.TryGetValue(ErrorMetadataKeys.SourceColumn, out var column)
                ? $"refused {first.Code} {what} at {line}:{column}"
                : $"failed {first.Code} {what}: a refusal without its line and column";
        }

        var pages = DocumentLayout.Run(boxes.Value.Document, boxes.Value.Fonts);
        if (pages.IsFailure)
        {
            return $"failed {pages.Error.Code} {pages.Error.Message}";
        }

        if (!File.Exists(Measured(name) + ".print.txt"))
        {
            return "failed no print of the case";
        }

        var engine = PrintedPages.Engine(pages.Value);
        var difference = Difference(engine, PrintedPages.Parse(File.ReadAllText(Measured(name) + ".print.txt")))
            ?? RectangleDifference(engine, PrintedPages.Parse(File.ReadAllText(Measured(name) + ".print.txt")), Rows(name, "lines"))
            ?? PopplerDifference(pages.Value, engine, Rows(name, "words"))
            ?? BoxDifference(name, boxes.Value, Rows(name, "boxes"));
        return (difference, Listed.TryGetValue(name, out var listed)) switch
        {
            (null, false) => "equal",
            (null, true) => "different equal to Chromium, but listed as a difference",
            ({ } found, true) => $"listed {listed}: {found}",
            ({ } found, false) => committed == "open" ? $"open {found}" : $"different {found}",
        };
    }

    /// <summary>The non-empty lines of a measured file of a case, split at spaces.</summary>
    private static List<string[]> Rows(string name, string kind) =>
        [.. File.ReadAllLines($"{Measured(name)}.{kind}.txt").Where(line => line.Length > 0).Select(line => line.Split(' '))];

    private static double Number(string value) => double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static string Of(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>
    /// The first difference between the engine's pages and Chromium's print as <c>print.txt</c> holds it, or null: the same
    /// number of pages of the same size (within 0.001 pt, the file's precision); on each page the same words in the same
    /// order, each with its start and end (<see cref="PrintedWord.X0"/>, <see cref="PrintedWord.X1"/>) and its font size
    /// within <see cref="Bound"/>, its baseline after <see cref="WholePixel"/> within <see cref="Bound"/>, and the same
    /// colour and face (<see cref="PrintedWord.Colour"/>, <see cref="PrintedWord.Face"/>: the PostScript name, which names
    /// the font file and with it the weight and the style); the same area painted in each colour by the rectangles,
    /// filled or stroked (<see cref="PaintedDifference"/>); the same image areas in drawing order, each edge after
    /// <see cref="WholePixel"/> within <see cref="Bound"/>.
    /// </summary>
    public static string? Difference(PrintedPages engine, PrintedPages chromium)
    {
        if (engine.Pages.Length != chromium.Pages.Length)
        {
            return $"{engine.Pages.Length} pages, Chromium {chromium.Pages.Length} (print.txt)";
        }

        foreach (var (ours, theirs) in engine.Pages.Zip(chromium.Pages))
        {
            if (Math.Abs(ours.Width - theirs.Width) > 0.001 || Math.Abs(ours.Height - theirs.Height) > 0.001)
            {
                return $"page {ours.Number} is {Of(ours.Width)}x{Of(ours.Height)}, Chromium {Of(theirs.Width)}x{Of(theirs.Height)} (print.txt)";
            }

            var words = Joined(ours.Words);
            var text = string.Join(" ", words.Select(word => word.Text));
            if (text != theirs.Text)
            {
                return $"page {ours.Number} reads '{text}', Chromium '{theirs.Text}' (print.txt)";
            }

            foreach (var (word, measured) in words.Zip(theirs.Words))
            {
                var at = $"page {ours.Number} '{word.Text}'";
                var found = Far(at, "x0", word.X0, measured.X0) ?? Far(at, "x1", word.X1, measured.X1)
                    ?? Snapped(at, "baseline", word.Baseline, measured.Baseline) ?? Far(at, "font size", word.FontSize, measured.FontSize)
                    ?? Same(at, "colour", word.Colour, measured.Colour) ?? Same(at, "face", word.Face, measured.Face);
                if (found is not null)
                {
                    return found + " (print.txt)";
                }
            }

            if ((PaintedDifference(ours, ours.Rectangles, theirs.Rectangles) ?? Areas($"page {ours.Number} image", ours.Images, theirs.Images)) is { } area)
            {
                return area + " (print.txt)";
            }
        }

        return null;
    }

    /// <summary>The first difference between the area the engine's rectangles paint and the area of the rectangles
    /// <c>lines.txt</c> holds (<c>page x0 y0 x1 y1 height</c>, read back by <c>rectangles.py</c>; the height is y1 less y0
    /// of the same line, nothing of its own), page by page as <see cref="PaintedDifference"/> compares them. The file
    /// holds no colour, so the area is one for every colour; and it holds fills only, so the bands that Chromium's strokes
    /// paint (<c>print.txt</c>, <see cref="PrintedBox.Stroked"/>: Chromium strokes a border of one colour and style on
    /// all four sides as one rectangle) are added to it.</summary>
    private static string? RectangleDifference(PrintedPages engine, PrintedPages chromium, List<string[]> rows)
    {
        var measured = rows.Select(row => new PrintedBox(int.Parse(row[0], CultureInfo.InvariantCulture), Number(row[1]), Number(row[2]), Number(row[3]), Number(row[4]))).ToList();
        if (measured.FirstOrDefault(box => box.Page < 1 || box.Page > engine.Pages.Length) is { } beyond)
        {
            return $"a rectangle on page {beyond.Page}, the engine has {engine.Pages.Length} pages (lines.txt)";
        }

        static PrintedBox Colourless(PrintedBox box) => box with { Colour = null, Stroked = false };
        foreach (var (page, print) in engine.Pages.Zip(chromium.Pages))
        {
            var filled = measured.Where(box => box.Page == page.Number).Concat(print.Rectangles.Where(box => box.Stroked).Select(Colourless));
            if (PaintedDifference(page, [.. page.Rectangles.Select(Colourless)], [.. filled]) is { } found)
            {
                return found + " (lines.txt)";
            }
        }

        return null;
    }

    /// <summary>
    /// The first difference between what two sets of painted rectangles show on a page: for each whole pixel of the page
    /// (0.75 pt), the colour of the last rectangle in drawing order that covers it, each edge after
    /// <see cref="WholePixel"/>, so an edge within <see cref="Bound"/> of Chromium's after snapping covers the same pixels.
    /// The area of each colour is compared, not each rectangle: Chromium cuts a border into other rectangles than the
    /// engine, which paint the same pixels (case <c>fr-f9-padded-box-orphans3</c>: Chromium's side borders run over the
    /// top border, from 93 pt down, the engine's start below it, at 93.75 pt), and strokes a border of one colour on all
    /// four sides as one rectangle where the engine fills four. The number of the rectangles is not compared; their order
    /// only where two colours overlap.
    /// </summary>
    private static string? PaintedDifference(PrintedPage page, IReadOnlyList<PrintedBox> ours, IReadOnlyList<PrintedBox> theirs)
    {
        var (engine, chromium) = (Painted(page, ours), Painted(page, theirs));
        var columns = (int)Math.Round(page.Width / 0.75, MidpointRounding.AwayFromZero);
        for (var cell = 0; cell < engine.Length; cell++)
        {
            if (engine[cell] != chromium[cell])
            {
                var (x, y) = (cell % columns * 0.75, cell / columns * 0.75);
                var who = (engine[cell], chromium[cell]) switch
                {
                    (null, _) => "Chromium paints it and the engine does not",
                    (_, null) => "the engine paints it and Chromium does not",
                    _ => $"the engine paints it {engine[cell]}, Chromium {chromium[cell]}",
                };
                var colour = engine[cell] ?? chromium[cell];
                var area = colour is null or "" ? string.Empty : $" {colour}";
                return $"page {page.Number} painted area: the pixel at ({Of(x)}, {Of(y)}): {who}; the engine paints {engine.Count(painted => painted == colour)} px{area}, " +
                    $"Chromium {chromium.Count(painted => painted == colour)} px";
            }
        }

        return null;
    }

    /// <summary>The whole pixels of a page, row by row from the top left: the colour of the last rectangle that covers it
    /// (an empty colour for a rectangle without one), or null; each edge after <see cref="WholePixel"/> and cut at the
    /// page's edges.</summary>
    private static string?[] Painted(PrintedPage page, IReadOnlyList<PrintedBox> boxes)
    {
        static int Pixel(double length) => (int)Math.Round(length / 0.75, MidpointRounding.AwayFromZero);
        var (columns, rows) = (Pixel(page.Width), Pixel(page.Height));
        var cells = new string?[columns * rows];
        foreach (var box in boxes)
        {
            for (var y = Math.Max(0, Pixel(box.Y0)); y < Math.Min(rows, Pixel(box.Y1)); y++)
            {
                for (var x = Math.Max(0, Pixel(box.X0)); x < Math.Min(columns, Pixel(box.X1)); x++)
                {
                    cells[y * columns + x] = box.Colour ?? string.Empty;
                }
            }
        }

        return cells;
    }

    /// <summary>
    /// The first difference between the engine's words and the words poppler reads back (<c>words.txt</c>: <c>page xMin
    /// yMin xMax yMax text</c>, the text as HTML). Poppler finds the words inside the page (the baseline after
    /// <see cref="WholePixel"/> on the page, and a font size above it), as <c>ReadBackAgreementTests</c> finds for
    /// Chromium's: their number per page is the engine's. Each is an engine word on the same page with the same text
    /// (matched by text and nearest start, since poppler writes them in its own reading order) whose start and end are
    /// within <see cref="Bound"/> of <c>xMin</c> and <c>xMax</c>, and whose line box, the snapped baseline less the
    /// ascender and plus the descender of the face's line metrics (<see cref="FontMetrics.Line"/>: the typographic ones
    /// where the face sets USE_TYPO_METRICS, as Chromium writes them into the PDF's font descriptor; measured with Theano
    /// Didot, case <c>f-theano-13-ab</c>) at the word's size, is within <see cref="Bound"/> of <c>yMin</c> and <c>yMax</c>.
    /// </summary>
    private static string? PopplerDifference(PageDocument document, PrintedPages engine, List<string[]> rows)
    {
        foreach (var page in engine.Pages)
        {
            var inside = Joined(page.Words).Where(word => WholePixel(word.Baseline) <= page.Height && WholePixel(word.Baseline) - word.FontSize >= 0).ToList();
            var poppler = rows.Where(row => int.Parse(row[0], CultureInfo.InvariantCulture) == page.Number).ToList();
            if (inside.Count != poppler.Count)
            {
                return $"page {page.Number} has {inside.Count} words inside the page, Chromium {poppler.Count} (words.txt)";
            }

            foreach (var row in poppler)
            {
                var text = System.Net.WebUtility.HtmlDecode(row[5]);
                var word = inside.Where(word => word.Text == text).OrderBy(word => Math.Abs(word.X0 - Number(row[1]))).FirstOrDefault();
                if (word is null)
                {
                    return $"page {page.Number} has no word '{text}' (words.txt)";
                }

                inside.Remove(word);
                var at = $"page {page.Number} '{text}'";
                var metrics = Face(document.Pages[page.Number - 1], word);
                var baseline = WholePixel(word.Baseline);
                var found = Far(at, "xMin", word.X0, Number(row[1])) ?? Far(at, "xMax", word.X1, Number(row[3]))
                    ?? Far(at, "yMin", baseline - metrics.ToPoints(metrics.Line.Ascender, word.FontSize), Number(row[2]))
                    ?? Far(at, "yMax", baseline - metrics.ToPoints(metrics.Line.Descender, word.FontSize), Number(row[4]));
                if (found is not null)
                {
                    return found + " (words.txt)";
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The first difference between the engine's boxes and the sizes measured in the page (<c>boxes.txt</c>: <c>tag width
    /// height label</c>, the label the element's id where it has one): the height of the box of the element with that id,
    /// within <see cref="Bound"/>, unsnapped (Chromium's <c>getBoundingClientRect</c>). The width is not compared:
    /// <c>measure.mjs</c> measures the boxes in the browser window (1280 px wide) with print media, not in the printed
    /// page, so a block is as wide as the window (960 pt in <c>fr-empty-span</c>, whose page is 300 pt wide). The box is
    /// found by its source line: the first box written on the line of the element, which must be the first element that
    /// starts on that line (an element later on the line, a span inside it, has no box before the element's own). A box
    /// the file names without an id, one whose element does not start its line, and one on more than one page are
    /// differences: the comparison does not skip them.
    /// </summary>
    private static string? BoxDifference(string name, HtmlBoxes boxes, List<string[]> rows)
    {
        if (rows.Count == 0)
        {
            return null;
        }

        var elements = Elements(Parsed(name).Value.Document).ToList();
        var pages = Paginator.Paginate(LayoutEnvironment.Create(boxes.Fonts), boxes.Document);
        if (pages.IsFailure)
        {
            return $"the pages for the boxes failed: {pages.Error.Code} (boxes.txt)";
        }

        foreach (var row in rows)
        {
            var label = row.Length > 3 ? string.Join(' ', row[3..]) : string.Empty;
            var element = elements.Where(element => element.Attribute("id") == label).ToList();
            if (element.Count != 1 || elements.First(other => other.Position.Line == element[0].Position.Line) != element[0])
            {
                return $"box {row[0]} '{label}' is not one element with that id that starts its line (boxes.txt)";
            }

            var line = element[0].Position.Line;
            var fragments = pages.Value.Select(page => First(page.Root.Fragment, line)).Where(fragment => fragment is not null).ToList();
            if (fragments.Count != 1)
            {
                return $"box '{label}' is on {fragments.Count} pages (boxes.txt)";
            }

            if (Far($"box '{label}'", "height", fragments[0]!.Size.Height, Number(row[2])) is { } found)
            {
                return found + " (boxes.txt)";
            }
        }

        return null;
    }

    /// <summary>Every element of a document, in document order, with an explicit stack (no recursion on input depth).</summary>
    private static IEnumerable<HtmlElement> Elements(HtmlDocument document)
    {
        var stack = new Stack<HtmlNode>(document.Children.Reverse());
        while (stack.Count > 0)
        {
            if (stack.Pop() is HtmlElement element)
            {
                yield return element;
                foreach (var child in element.Children.Reverse())
                {
                    stack.Push(child);
                }
            }
        }
    }

    /// <summary>The first box fragment in document order whose box was written on the line: the element's own box comes
    /// before the anonymous boxes inside it, which carry the same line.</summary>
    private static BoxFragment? First(Fragment root, int line)
    {
        var stack = new Stack<Fragment>([root]);
        while (stack.Count > 0)
        {
            if (stack.Pop() is BoxFragment box)
            {
                if (box.Box.Source.Line == line)
                {
                    return box;
                }

                foreach (var child in box.Children.Reverse())
                {
                    stack.Push(child.Fragment);
                }
            }
        }

        return null;
    }

    /// <summary>The first image of a page whose edges after <see cref="WholePixel"/> are not within <see cref="Bound"/> of
    /// Chromium's, in drawing order; or a different count.</summary>
    private static string? Areas(string at, IReadOnlyList<PrintedBox> ours, IReadOnlyList<PrintedBox> theirs)
    {
        if (ours.Count != theirs.Count)
        {
            return $"{at}s: {ours.Count}, Chromium {theirs.Count}";
        }

        foreach (var (box, (measured, index)) in ours.Zip(theirs.Select((box, index) => (box, index + 1))))
        {
            var where = $"{at} {index}";
            var found = Snapped(where, "x0", box.X0, measured.X0) ?? Snapped(where, "y0", box.Y0, measured.Y0)
                ?? Snapped(where, "x1", box.X1, measured.X1) ?? Snapped(where, "y1", box.Y1, measured.Y1);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static string? Same(string at, string what, string? ours, string? chromium) =>
        ours != chromium ? $"{at} {what} {ours}, Chromium {chromium}" : null;

    private static string? Far(string at, string what, double ours, double chromium) =>
        Math.Abs(ours - chromium) > Bound ? $"{at} {what} {Of(ours)}, Chromium {Of(chromium)}" : null;

    /// <summary>A length compared after <see cref="WholePixel"/>; the difference names the engine's value painted and as
    /// laid out.</summary>
    private static string? Snapped(string at, string what, double ours, double chromium) =>
        Math.Abs(WholePixel(ours) - chromium) > Bound ? $"{at} {what} {Of(WholePixel(ours))} (laid out at {Of(ours)}), Chromium {Of(chromium)}" : null;

    /// <summary>The metrics of the face a word is drawn in: of the glyph run on the word's baseline that starts last at or
    /// before the word.</summary>
    private static FontMetrics Face(Page page, PrintedWord word) =>
        page.Items.OfType<GlyphRun>().Where(run => run.Origin.Y == word.Baseline && run.Origin.X <= word.X0 + 1e-6)
            .MaxBy(run => run.Origin.X)!.Font.Metrics;

    /// <summary>
    /// A length down or across the page as Chromium paints it: at the nearest whole CSS pixel of the page (0.75 pt).
    /// Chromium paints a baseline at a whole pixel (docs/architecture.md, "Against Chromium"; <c>FontMetricRoundingTests</c>
    /// compares the same way), and the edges of a filled rectangle and of an image area too. Measured: all 6,104 rectangle
    /// edges and all 83 image edges of the 294 committed prints are whole pixels, and all but 10 of the 4,349 baselines (the
    /// 10 are in case <c>font-sizes</c>, at 8.2 and 9.1 pt, which the HTML path refuses). The rule moves a value by up to
    /// 0.375 pt, so a difference that small in a baseline or an edge can stay inside one pixel; the x positions of words
    /// and the box heights of <c>boxes.txt</c> are not snapped in Chromium's files and are compared as they are.
    /// </summary>
    private static double WholePixel(double length) => Math.Round(length / 0.75, MidpointRounding.AwayFromZero) * 0.75;

    /// <summary>
    /// The engine's words as the read-backs of Chromium's PDF find them: two runs of glyphs that touch on one baseline at
    /// the same font size, in the same face and colour, with no white space between (text on both sides of an inline
    /// element's edge), are one word; PyMuPDF starts a new span, and <c>positions.py</c> a new word, where the font, the
    /// size or the colour changes.
    /// </summary>
    private static List<PrintedWord> Joined(IEnumerable<PrintedWord> words)
    {
        var joined = new List<PrintedWord>();
        foreach (var word in words)
        {
            if (joined.Count > 0 && joined[^1] is var last && last.Baseline == word.Baseline && Math.Abs(last.X1 - word.X0) < 1e-6
                && last.FontSize == word.FontSize && last.Face == word.Face && last.Colour == word.Colour)
            {
                joined[^1] = last with { X1 = word.X1, Text = last.Text + word.Text };
                continue;
            }

            joined.Add(word);
        }

        return joined;
    }
}
