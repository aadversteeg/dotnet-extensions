using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Css;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Fonts;
using Ave.Extensions.Documents.Html;
using Ave.Extensions.Documents.Html.Boxes;
using Ave.Extensions.Documents.Html.Parsing;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.PageModel;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.Documents.Typesetting;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace UnitTests.Extensions.Documents.Html.Boxes;

/// <summary>
/// The Chromium reference cases (<c>tools/reference/chromium/cases</c>) through the whole HTML path, unchanged: parse,
/// resolve the styles (the resolver serves <c>common.css</c> and the font files from the repository), generate the
/// boxes, lay out; then the engine's pages beside every file measured for the case in <c>measured/</c> (README of
/// <c>tools/reference/chromium</c>): the print read back from Chromium's PDF (<c>&lt;case&gt;.print.txt</c>: pages,
/// words with their colour and face, painted rectangles with their colour, images), its filled rectangles read back by a
/// second script (<c>&lt;case&gt;.lines.txt</c>), the lines it clips around the glyphs' ink (<c>&lt;case&gt;.ink.txt</c>,
/// <see cref="Clips"/>: both files hold such a line whole), its words read back by poppler (<c>&lt;case&gt;.words.txt</c>), and the
/// sizes of the boxes measured in the page (<c>&lt;case&gt;.boxes.txt</c>), and its link annotations
/// (<c>&lt;case&gt;.links.txt</c>, <see cref="LinkDifference"/>; a case without the file has none). Each comparison below
/// names its file and its bound.
/// </summary>
internal static class HtmlCases
{
    /// <summary>
    /// How far a position or a length may be from Chromium's, in points: 0.1 pt. Where it comes from: the whole-print
    /// tests of the code front-end compare baselines within one Chromium layout unit (1/64 px, 0.0117 pt,
    /// <c>FontMetricRoundingTests</c>), and x positions differ by Chromium's own font-size rounding, which scales the
    /// advances of a 10 pt line by 0.99961 (<c>ChromiumLayout.FontSizeFactor</c>): 0.08 pt at the end of a 210 pt line.
    /// The bound covers both and is no wider than 0.1 pt, the widest the reference rules of the family allow. Baselines are
    /// compared where Chromium paints them (<see cref="BaselineDifference"/>) and the edges of rectangles and images after
    /// the snapping rule of <see cref="WholePixel"/>, which the bound does not widen; the x positions of words, font sizes and box heights are compared as they are.
    /// </summary>
    public const double Bound = 0.1;

    /// <summary>The statuses the status test accepts, as the second field of a line, compared whole: <c>equal</c>,
    /// <c>listed</c> (a listed limit, <see cref="CommittedStatus"/>), <c>refused</c> (the first refusal, with its line and column)
    /// and <c>open</c> (a known difference the committed status line names; while one is open the branch is not ready for a pull
    /// request). <c>different</c> and <c>failed</c> are never accepted.</summary>
    public static readonly IReadOnlySet<string> Accepted = new HashSet<string>(StringComparer.Ordinal) { "equal", "listed", "refused", "open" };

    /// <summary>The codes that are a refusal of the HTML path, by their exact code: what the CSS stage and the HTML
    /// front-end refuse to draw (<see cref="CssErrorCodes.Unsupported"/>, <see cref="HtmlStyleErrorCodes.Unsupported"/>),
    /// and content layout cannot place on any page (<see cref="LayoutErrorCodes.DoesNotFit"/>: it names the room it had
    /// and the source line of the box, where Chromium prints the content past the page end) or that is wider than its
    /// page-margin box (<see cref="LayoutErrorCodes.MarginBoxOverflow"/>, at the margin box's <c>content</c> declaration,
    /// where Chromium draws it over its box), or a table whose columns do not fit the width it is given
    /// (<see cref="LayoutErrorCodes.ColumnsDoNotFit"/>, at the table's source line, where Chromium lets the table pass its
    /// container: case <c>tw-pref</c>, a table that needs 157.805 pt in a box of 150 pt), or a box with a height whose
    /// content goes on past a page break after the height ended (<see cref="LayoutErrorCodes.OverflowAcrossPages"/>, at the
    /// box's source line, where Chromium prints that content on the next page: cases <c>sz-height-overflow-page</c>,
    /// <c>sz-height-forced</c>), or a list item's label that would start left of the sheet
    /// (<see cref="LayoutErrorCodes.MarkerOutsideSheet"/>, at the item's source line, where Chromium cuts the label at the
    /// sheet's edge or leaves it out), or a table with separated borders that would break over pages
    /// (<see cref="LayoutErrorCodes.TableBreak"/>: the room it had, at the table's source line), or an inline box layout
    /// does not draw where it stands (<see cref="DocumentErrorCodes.Input.Unsupported.InlineBox"/>, at the line and column
    /// of the element that made it; the front-end's own <see cref="HtmlStyleErrorCodes.Unsupported.InlineBox"/> is in the
    /// list as one of its codes). Any other error, also another code under the same parent, is a failure of the case.</summary>
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
        HtmlStyleErrorCodes.Unsupported.Character.ToString(),
        HtmlStyleErrorCodes.Unsupported.Table.ToString(),
        HtmlStyleErrorCodes.Unsupported.List.ToString(),
        FontErrorCodes.GlyphMissing.ToString(),
        HtmlStyleErrorCodes.Unsupported.Link.ToString(),
        HtmlStyleErrorCodes.Unsupported.Language.ToString(),
        HtmlStyleErrorCodes.Unsupported.Metadata.ToString(),
        HtmlStyleErrorCodes.Unsupported.InlineBox.ToString(),
        HtmlStyleErrorCodes.Unsupported.Counter.ToString(),
        CssErrorCodes.Unsupported.PageRule.ToString(),
        CssErrorCodes.Unsupported.GeneratedContent.ToString(),
        LayoutErrorCodes.DoesNotFit.ToString(),
        LayoutErrorCodes.MarginBoxOverflow.ToString(),
        LayoutErrorCodes.ColumnsDoNotFit.ToString(),
        LayoutErrorCodes.OverflowAcrossPages.ToString(),
        LayoutErrorCodes.MarkerOutsideSheet.ToString(),
        LayoutErrorCodes.TableBreak.ToString(),
        LayoutErrorCodes.RoundedBorderLineBreak.ToString(),
        DocumentErrorCodes.Input.Unsupported.InlineBox.ToString(),
    };

    /// <summary>The cases folder in the source tree.</summary>
    public static string Folder([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", "tools", "reference", "chromium", "cases"));

    /// <summary>The reference cases: <c>cases</c>, and their measured files and status files in <c>measured</c>, beside
    /// the folder.</summary>
    public static CaseFolder Cases { get; } = new(Folder(), Path.Combine(Folder(), "..", "measured"));

    /// <summary>The whole documents (<c>tools/reference/chromium/documents</c>: the timesheets and the invoice), compared as
    /// the cases are: their measured files and status files in the folder's <c>measured</c>.</summary>
    public static CaseFolder Documents { get; } = new(Path.Combine(Folder(), "..", "documents"), Path.Combine(Folder(), "..", "documents", "measured"));

    /// <summary>Every case, by name, in ordinal order.</summary>
    public static IReadOnlyList<string> Names => Cases.Names;

    /// <summary>The family folder: what the cases' relative URLs may reach.</summary>
    private static string Family => Path.GetFullPath(Path.Combine(Folder(), "..", "..", "..", ".."));

    /// <summary>The measured files of a case, without their extension.</summary>
    private static string Measured(CaseFolder folder, string name) => Path.Combine(folder.Measured, name);

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
    private static Result<HtmlParseResult, Error> Parsed(CaseFolder folder, string name) =>
        HtmlParser.Parse(File.ReadAllText(Path.Combine(folder.Html, name + ".html")), new HtmlParseOptions(name + ".html"));

    /// <summary>The paper the reference tool prints on (Playwright's default for a PDF): US Letter, no margins.</summary>
    public static PageStyle Letter { get; } = new(Length.FromPoints(612), Length.FromPoints(792), default);

    /// <summary>The boxes of a case, on the reference tool's paper (<see cref="Letter"/>).</summary>
    public static Result<HtmlBoxes, Error> Boxes(string name, UnsupportedPolicy policy = UnsupportedPolicy.Refuse) => Boxes(Cases, name, policy);

    /// <summary>The boxes of an HTML file of a folder, on the reference tool's paper (<see cref="Letter"/>).</summary>
    public static Result<HtmlBoxes, Error> Boxes(CaseFolder folder, string name, UnsupportedPolicy policy = UnsupportedPolicy.Refuse)
    {
        var baseUrl = new Uri(Path.GetFullPath(Path.Combine(folder.Html, name + ".html"))).AbsoluteUri;
        return Parsed(folder, name)
            .OnSuccessBind(parsed => HtmlStyleResolver.Resolve(parsed, new HtmlStyleOptions { Page = Letter, Resources = Resource, BaseUrl = baseUrl, Unsupported = policy }))
            .OnSuccessBind(styled => HtmlBoxGenerator.Generate(styled, new HtmlBoxOptions { Resources = Resource, BaseUrl = baseUrl, Unsupported = policy, MissingCharacters = Shaper.MissingCharacters }));
    }

    /// <summary>
    /// The status a case's committed status line names (<see cref="CaseFolder.Committed"/>, its second field) and, where it
    /// is <c>listed</c>, the test that pins the difference: a listed limit of layout (docs/architecture.md, "Known limits"),
    /// pinned by a test of this project that lays out the case (<see cref="HtmlListedDifferenceTests"/>,
    /// <see cref="HtmlListedBreakTests"/>; <c>CaseStatusTests</c> checks that it names the case). The first line says
    /// <c>listed</c>, the test name and the first difference, and the lines after it every difference
    /// (<see cref="DifferenceList"/>), so the case must still differ, in the same facts by the same amounts.
    /// </summary>
    /// <param name="committed">The committed status line without the case's name, or null.</param>
    /// <returns>The committed status (empty where there is none) and the test of a listed case, or null.</returns>
    private static (string Status, string? Listed) CommittedStatus(string? committed)
    {
        var fields = (committed ?? string.Empty).Split('\n', 2)[0].Split(' ', 2);
        return (fields[0], fields[0] == "listed" ? (fields.Length > 1 ? fields[1] : string.Empty).Split(": ", 2)[0] : null);
    }

    /// <summary>
    /// The status of a case: <c>equal</c>; <c>listed test: difference</c>; <c>refused code what at line:column</c>;
    /// <c>open difference</c> where the committed status of the case is <c>open</c>; otherwise <c>different difference</c>
    /// or <c>failed …</c>, which the status test never accepts. Only the committed status line makes a case <c>open</c> or
    /// <c>listed</c>, so writing the status files anew never turns a new difference into an accepted one.
    /// </summary>
    /// <param name="name">The case.</param>
    /// <param name="committed">The case's committed status line without its name (<see cref="CaseFolder.Committed"/>), or
    /// null.</param>
    /// <returns>The status, without the name.</returns>
    public static string Status(string name, string? committed = null) => Status(Cases, name, committed);

    /// <summary>The status of an HTML file of a folder, by the same rules as a case's (<see cref="Status(string, string?)"/>).</summary>
    /// <param name="folder">The folder of the file, and of its measured files and its status file.</param>
    /// <param name="name">The file, without its extension.</param>
    /// <param name="committed">Its committed status line without its name, or null.</param>
    /// <returns>The status, without the name.</returns>
    public static string Status(CaseFolder folder, string name, string? committed = null)
    {
        var boxes = Boxes(folder, name);
        if (boxes.IsFailure)
        {
            return Refusal(boxes.Error);
        }

        var pages = DocumentLayout.Run(boxes.Value.Document, boxes.Value.Fonts);
        if (pages.IsFailure)
        {
            return Refusal(pages.Error);
        }

        if (!File.Exists(Measured(folder, name) + ".print.txt"))
        {
            return "failed no print of the case";
        }

        var engine = PrintedPages.Engine(pages.Value);
        var clips = Clips(folder, name);
        var chromium = PrintedPages.Parse(File.ReadAllText(Measured(folder, name) + ".print.txt")).WithGlyphs(File.ReadAllText(Measured(folder, name) + ".glyphs.txt"));
        var unused = clips.ToList();
        chromium = new([.. chromium.Pages.Select(page => page with { Rectangles = [.. Cut(page.Rectangles, unused)] })]);
        var lines = Cut(Rows(folder, name, "lines").Select(row => new PrintedBox(int.Parse(row[0], CultureInfo.InvariantCulture), Number(row[1]), Number(row[2]), Number(row[3]), Number(row[4]))), clips.ToList());
        var lineTops = LineTops(boxes.Value);
        var differences = unused.Select(clip => $"page {clip.Line.Page} the line clipped at y {Of(clip.Line.Y0)} is no rectangle of the print (ink.txt)")
            .Concat(Differences(engine, chromium, lineTops))
            .Concat(RectangleDifferences(engine, chromium, lines))
            .Concat(PopplerDifferences(pages.Value, engine, Rows(folder, name, "words"), lineTops))
            .Concat(BoxDifferences(folder, name, boxes.Value, Rows(folder, name, "boxes")))
            .Concat(LinkDifferences(pages.Value, Links(folder, name), boxes.Value.Document.Page.Margins.Top.Points))
            .Concat(GlyphDifferences(engine, chromium))
            .Take(DifferenceBound + 1).ToList();
        var (status, listed) = CommittedStatus(committed);
        if (differences.Count == 0)
        {
            return listed is null ? "equal" : "different equal to Chromium, but listed as a difference";
        }

        if (listed is null && status != "open")
        {
            return $"different {differences[0]}";
        }

        if (differences.Count > DifferenceBound)
        {
            return $"different more than {DifferenceBound} differences, too many to be {status}: {differences[0]}";
        }

        return (listed is null ? $"open {differences[0]}" : $"listed {listed}: {differences[0]}") + "\n" + DifferenceList(differences);
    }

    /// <summary>The most differences a <c>listed</c> or <c>open</c> case may have: a case with more is <c>different</c>.
    /// Every listed case has fewer than a tenth of this; a larger count says the case is compared out of step (a page or a
    /// line moved), and that is not a limit the list can name.</summary>
    public const int DifferenceBound = 500;

    /// <summary>How many differences a status file writes out one by one; past this it writes the first ones, the count and
    /// a SHA-256 of the whole list (<see cref="DifferenceList"/>).</summary>
    public const int ShownDifferences = 60;

    /// <summary>
    /// The differences of a <c>listed</c> or <c>open</c> case as its status file holds them, after its first line: the count,
    /// then one difference per line (<c>- </c> and the difference) in the order the comparison finds them. A list longer than
    /// <see cref="ShownDifferences"/> writes the first of them and, on the count's line, the SHA-256 (lowercase hexadecimal)
    /// of the whole list in UTF-8, the differences joined by line feeds. Every number in a difference is rounded as the
    /// comparison prints it (three decimals at most), so the list is the same on every platform the tests run on; a change in
    /// any difference, or one more or one fewer, changes the file.
    /// </summary>
    /// <param name="differences">The differences, at least one.</param>
    /// <returns>The lines, joined by line feeds.</returns>
    public static string DifferenceList(IReadOnlyList<string> differences)
    {
        var count = differences.Count == 1 ? "1 difference" : $"{differences.Count} differences";
        if (differences.Count <= ShownDifferences)
        {
            return $"{count}:\n" + string.Join("\n", differences.Select(difference => "- " + difference));
        }

        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join("\n", differences))));
        return $"{count}, SHA-256 {hash} of all of them; the first {ShownDifferences}:\n" + string.Join("\n", differences.Take(ShownDifferences).Select(difference => "- " + difference));
    }

    /// <summary>Chromium's print of a case as <see cref="Status(CaseFolder, string, string?)"/> compares it: <c>print.txt</c>
    /// with each line that Chromium clips around the glyphs' ink replaced by its pieces (<see cref="Clips"/>).</summary>
    /// <param name="name">The case.</param>
    /// <returns>The print.</returns>
    public static PrintedPages ChromiumPrint(string name)
    {
        var clips = Clips(Cases, name);
        var print = PrintedPages.Parse(File.ReadAllText(Measured(Cases, name) + ".print.txt"));
        return new([.. print.Pages.Select(page => page with { Rectangles = [.. Cut(page.Rectangles, clips)] })]);
    }

    /// <summary>
    /// The lines Chromium cuts around the glyphs' ink (<c>text-decoration-skip-ink: auto</c>), from <c>&lt;case&gt;.ink.txt</c>
    /// (README of <c>tools/reference/chromium</c>, read back by <c>ink.py</c>): Chromium paints such a line whole inside clips,
    /// so <c>print.txt</c> and <c>lines.txt</c> hold the whole line; what the page shows are its pieces (<c>piece</c>: the
    /// line's rows between two gaps; <c>part</c>: a rectangle of its own, where a clip cuts only part of the line's height).
    /// Each clipped line with the pieces it leaves; none for a case without the file.
    /// </summary>
    private static List<(PrintedBox Line, List<PrintedBox> Pieces)> Clips(CaseFolder folder, string name)
    {
        var path = Measured(folder, name) + ".ink.txt";
        var clips = new List<(PrintedBox Line, List<PrintedBox> Pieces)>();
        foreach (var parts in File.Exists(path) ? File.ReadAllLines(path).Where(line => line.Length > 0).Select(line => line.Split(' ')) : [])
        {
            var page = int.Parse(parts[1], CultureInfo.InvariantCulture);
            switch (parts[0])
            {
                case "fill":
                    clips.Add((new PrintedBox(page, Number(parts[2]), Number(parts[3]), Number(parts[4]), Number(parts[5])), []));
                    break;
                case "piece":
                    clips[^1].Pieces.Add(clips[^1].Line with { X0 = Number(parts[2]), X1 = Number(parts[3]) });
                    break;
                case "part":
                    clips[^1].Pieces.Add(new PrintedBox(page, Number(parts[2]), Number(parts[3]), Number(parts[4]), Number(parts[5])));
                    break;
            }
        }

        return clips;
    }

    /// <summary>The rectangles with each clipped line (<see cref="Clips"/>; the same page and edges within 0.002 pt, the
    /// files' precision) replaced by its pieces, in its place in drawing order and in its colour; each clip is used once and
    /// taken out of <paramref name="clips"/>.</summary>
    private static List<PrintedBox> Cut(IEnumerable<PrintedBox> boxes, List<(PrintedBox Line, List<PrintedBox> Pieces)> clips)
    {
        var cut = new List<PrintedBox>();
        foreach (var box in boxes)
        {
            var index = clips.FindIndex(clip => clip.Line.Page == box.Page && Math.Abs(clip.Line.X0 - box.X0) < 0.002 && Math.Abs(clip.Line.Y0 - box.Y0) < 0.002
                && Math.Abs(clip.Line.X1 - box.X1) < 0.002 && Math.Abs(clip.Line.Y1 - box.Y1) < 0.002);
            if (index < 0)
            {
                cut.Add(box);
                continue;
            }

            cut.AddRange(clips[index].Pieces.Select(piece => piece with { Colour = box.Colour, Stroked = box.Stroked }));
            clips.RemoveAt(index);
        }

        return cut;
    }

    /// <summary>A link annotation of a print, or a link area of the engine's pages: its page, its rectangle in points with
    /// the origin at the top left of the page, and its target as <c>links.txt</c> writes it (<c>uri &lt;URI&gt;</c>, or
    /// <c>dest &lt;page&gt; &lt;top&gt;</c> for a link inside the document: the destination's page and its top from the top of
    /// that page; for the engine the place of the target's anchor, <see cref="Anchor"/>).</summary>
    internal sealed record PrintedLink(int Page, double X0, double Y0, double X1, double Y1, string Target);

    /// <summary>The link annotations of Chromium's print, from <c>&lt;case&gt;.links.txt</c> (README of
    /// <c>tools/reference/chromium</c>, read back by <c>links.py</c>: <c>page x0 y0 x1 y1 kind target</c>, page by page in
    /// the order of each page's <c>/Annots</c>); none for a case without the file, whose print has no annotation.</summary>
    internal static List<PrintedLink> Links(CaseFolder folder, string name)
    {
        var path = Measured(folder, name) + ".links.txt";
        return File.Exists(path)
            ? [.. File.ReadAllLines(path).Where(line => line.Length > 0).Select(line => line.Split(' ', 7))
                .Select(parts => new PrintedLink(int.Parse(parts[0], CultureInfo.InvariantCulture), Number(parts[1]), Number(parts[2]), Number(parts[3]), Number(parts[4]), $"{parts[5]} {parts[6]}"))]
            : [];
    }

    /// <summary>The engine's link areas (<see cref="LinkArea"/>), page by page in drawing order, each with the target of its
    /// link (the <see cref="SemanticRole.LinkRole"/> of the structure bracket it lies in, <see cref="StructureStart"/>).</summary>
    internal static List<PrintedLink> Links(PageDocument pages)
    {
        var targets = new Dictionary<NodeId, string>();
        var links = new List<PrintedLink>();
        var anchors = pages.Pages.SelectMany((page, index) => page.Items.OfType<Anchor>().Select(anchor => (anchor.Node, Place: $"dest {index + 1} {Of(anchor.At.Y)}")))
            .ToDictionary(anchor => anchor.Node, anchor => anchor.Place);
        foreach (var (page, number) in pages.Pages.Select((page, index) => (page, index + 1)))
        {
            foreach (var item in page.Items)
            {
                switch (item)
                {
                    case StructureStart { Role: SemanticRole.LinkRole { Target: LinkTarget.UriTarget uri } } start:
                        targets[start.Node] = $"uri {uri.Value}";
                        break;
                    case StructureStart { Role: SemanticRole.LinkRole { Target: LinkTarget.NodeTarget node } } start:
                        targets[start.Node] = anchors.TryGetValue(node.Value, out var place) ? place : $"dest none {node.Value}";
                        break;
                    case LinkArea area:
                        var rect = area.Area;
                        links.Add(new PrintedLink(number, rect.Origin.X, rect.Origin.Y, rect.Origin.X + rect.Size.Width, rect.Origin.Y + rect.Size.Height, targets[area.Link]));
                        break;
                }
            }
        }

        return links;
    }

    /// <summary>One CSS pixel in points: how far an edge of a link area may be from Chromium's. Chromium writes each edge of
    /// a link's rectangle at a whole CSS pixel (measured, case <c>lk-one-line</c>: the word "link" from 45.703 to 61.894 pt
    /// gets the rectangle 45.75 to 62.25 pt), the engine at its exact place (ISO 32000-1 §12.5.2: <c>/Rect</c> is any rectangle), so
    /// an edge may differ by up to half a pixel each way of the rounding; the bound is one pixel.</summary>
    public const double LinkBound = 0.75;

    /// <summary>
    /// One CSS pixel in points: how far the top of a link's destination may be from Chromium's, once Chromium's offset is
    /// taken out. Chromium writes a destination at the target's place in the coordinates of the page area, one page margin
    /// above and left of where the target stands on the page (measured: case <c>lk-later-heading</c>, a heading at the top
    /// of page 2's page area, 18 pt from the page's top, gets the top 0), and at a whole CSS pixel (case <c>lk-cell</c>: the
    /// target's content area starts 37.875 pt from the page's top, Chromium's top is 37.5 pt with the margin added back);
    /// the engine writes the exact place. So the top Chromium gives, plus the page's top margin, may be up to one pixel above
    /// the engine's.
    /// </summary>
    public const double DestinationBound = 0.75;

    /// <summary>
    /// The first difference between the engine's link areas and the link annotations of Chromium's print
    /// (<c>links.txt</c>), or null: the same number of rectangles, in the same order, each on the same page, every edge within
    /// <see cref="LinkBound"/>, and the same target: a URI compared whole, a destination on the same page with its top
    /// within <see cref="DestinationBound"/> of Chromium's plus <paramref name="marginTop"/>.
    /// </summary>
    /// <param name="pages">The engine's pages.</param>
    /// <param name="chromium">Chromium's links.</param>
    /// <param name="marginTop">The page's top margin, in points: Chromium's offset of every destination's top.</param>
    public static string? LinkDifference(PageDocument pages, IReadOnlyList<PrintedLink> chromium, double marginTop = 0) =>
        LinkDifferences(pages, chromium, marginTop).FirstOrDefault();

    /// <summary>Every difference between the engine's link areas and Chromium's link annotations, by the rules of
    /// <see cref="LinkDifference"/>, link by link in order: a link one side has and the other does not, or of a pair its
    /// page, each edge and its target.</summary>
    public static IEnumerable<string> LinkDifferences(PageDocument pages, IReadOnlyList<PrintedLink> chromium, double marginTop = 0)
    {
        var engine = Links(pages);
        for (var index = 0; index < Math.Max(engine.Count, chromium.Count); index++)
        {
            if (index >= engine.Count)
            {
                var missing = chromium[index];
                yield return $"link {index + 1}: Chromium's rectangle on page {missing.Page} at ({Of(missing.X0)}, {Of(missing.Y0)}) to {missing.Target} has none in the engine, which has {engine.Count} (links.txt)";
                continue;
            }

            var ours = engine[index];
            if (index >= chromium.Count)
            {
                yield return $"link {index + 1}: the engine's rectangle on page {ours.Page} at ({Of(ours.X0)}, {Of(ours.Y0)}) to {ours.Target} is not in Chromium's print, which has {chromium.Count} (links.txt)";
                continue;
            }

            var theirs = chromium[index];
            var at = $"link {index + 1} on page {ours.Page}";
            var found = new[]
            {
                ours.Page != theirs.Page ? $"link {index + 1} is on page {ours.Page}, Chromium's on page {theirs.Page}" : null,
                Edge(at, "x0", ours.X0, theirs.X0), Edge(at, "y0", ours.Y0, theirs.Y0), Edge(at, "x1", ours.X1, theirs.X1), Edge(at, "y1", ours.Y1, theirs.Y1),
                TargetDifference(at, ours.Target, theirs.Target, marginTop),
            };
            foreach (var difference in found.OfType<string>())
            {
                yield return difference + " (links.txt)";
            }
        }

        static string? Edge(string at, string what, double ours, double theirs) =>
            Math.Abs(ours - theirs) > LinkBound + 1e-9 ? $"{at}: {what} {Of(ours)}, Chromium {Of(theirs)}" : null;
    }

    private static string? TargetDifference(string at, string ours, string theirs, double marginTop)
    {
        if (ours.Split(' ') is ["dest", var page, var top] && theirs.Split(' ') is ["dest", var theirPage, var theirTop])
        {
            var chromiumTop = Number(theirTop) + marginTop;
            return page != theirPage ? $"{at} leads to page {page}, Chromium's to page {theirPage}"
                : !double.TryParse(top, NumberStyles.Float, CultureInfo.InvariantCulture, out var engineTop) || Math.Abs(engineTop - chromiumTop) > DestinationBound + 1e-9
                    ? $"{at} leads to the top {top} of page {page}, Chromium's to {Of(chromiumTop)} (its {theirTop} plus the top margin {Of(marginTop)})"
                    : null;
        }

        return ours != theirs ? $"{at} leads to {ours}, Chromium's to {theirs}" : null;
    }

    /// <summary>
    /// The status of a case whose path ended in an error: <c>refused code what at position</c> where the first part is a
    /// refusal (<see cref="Refusals"/>) that says where in the source it is, otherwise <c>failed</c>. The position of the
    /// front-end's refusals is a line and a column; layout's is the line of the box, the only place a box keeps
    /// (<see cref="SourceLocation"/>).
    /// </summary>
    internal static string Refusal(Error error)
    {
        var first = error.Parts().First();
        var metadata = first.Metadata;
        var what = metadata is { } named && named.TryGetValue(ErrorMetadataKeys.Property, out var property)
            ? $"{property}: {named[ErrorMetadataKeys.Value]}"
            : metadata is { } other && other.TryGetValue(ErrorMetadataKeys.Value, out var value) ? $"{value}" : first.Message;
        if (!Refusals.Contains(first.Code.ToString()))
        {
            return $"failed {first.Code} {first.Message}";
        }

        var line = metadata is { } lined && lined.TryGetValue(ErrorMetadataKeys.SourceLine, out var number) ? number : null;
        var column = metadata is { } columned && columned.TryGetValue(ErrorMetadataKeys.SourceColumn, out var at) ? at : null;
        return (line, column) switch
        {
            ({ }, { }) => $"refused {first.Code} {what} at {line}:{column}",
            ({ }, null) when first.Code == LayoutErrorCodes.DoesNotFit || first.Code == LayoutErrorCodes.ColumnsDoNotFit || first.Code == LayoutErrorCodes.OverflowAcrossPages || first.Code == LayoutErrorCodes.MarkerOutsideSheet || first.Code == LayoutErrorCodes.TableBreak => $"refused {first.Code} {what} at {line}",
            _ => $"failed {first.Code} {what}: a refusal without its line and column",
        };
    }

    /// <summary>The non-empty lines of a measured file of a case, split at spaces.</summary>
    private static List<string[]> Rows(CaseFolder folder, string name, string kind) =>
        [.. File.ReadAllLines($"{Measured(folder, name)}.{kind}.txt").Where(line => line.Length > 0).Select(line => line.Split(' '))];

    private static double Number(string value) => double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static string Of(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>
    /// The first difference between the engine's pages and Chromium's print as <c>print.txt</c> holds it, or null: the same
    /// number of pages of the same size (within 0.001 pt, the file's precision); on each page the same words in the same
    /// order, each with its start and end (<see cref="PrintedWord.X0"/>, <see cref="PrintedWord.X1"/>) and its font size
    /// within <see cref="Bound"/>, its baseline where Chromium paints it (<see cref="BaselineDifference"/>) within <see cref="Bound"/>, and the same
    /// colour and face (<see cref="PrintedWord.Colour"/>, <see cref="PrintedWord.Face"/>: the PostScript name, which names
    /// the font file and with it the weight and the style); the same area painted in each colour by the rectangles,
    /// filled or stroked (<see cref="PaintedDifferences"/>); the same image areas in drawing order, each edge after
    /// <see cref="WholePixel"/> within <see cref="Bound"/>; the same areas painted by paths that are not rectangles
    /// (<see cref="PathDifferences"/>).
    /// </summary>
    public static string? Difference(PrintedPages engine, PrintedPages chromium, IReadOnlyDictionary<(int Page, double Baseline), double>? lineTops = null) =>
        Differences(engine, chromium, lineTops).FirstOrDefault();

    /// <summary>
    /// Every difference between the engine's pages and Chromium's print as <c>print.txt</c> holds it, by the rules of
    /// <see cref="Difference"/>, in one fixed order: the number of pages, then page by page (the pages both have, in turn) its
    /// size, its text or else each word's start, end, baseline, font size, colour and face, the painted area
    /// (<see cref="PaintedDifferences"/>), the images and the paths. A page whose text differs is one difference: its words
    /// are not compared one by one, since they cannot be paired.
    /// </summary>
    public static IEnumerable<string> Differences(PrintedPages engine, PrintedPages chromium, IReadOnlyDictionary<(int Page, double Baseline), double>? lineTops = null)
    {
        if (engine.Pages.Length != chromium.Pages.Length)
        {
            yield return $"{engine.Pages.Length} pages, Chromium {chromium.Pages.Length} (print.txt)";
        }

        foreach (var (ours, theirs) in engine.Pages.Zip(chromium.Pages))
        {
            if (Math.Abs(ours.Width - theirs.Width) > 0.001 || Math.Abs(ours.Height - theirs.Height) > 0.001)
            {
                yield return $"page {ours.Number} is {Of(ours.Width)}x{Of(ours.Height)}, Chromium {Of(theirs.Width)}x{Of(theirs.Height)} (print.txt)";
            }

            var words = Joined(ours.Words);
            var text = string.Join(" ", words.Select(word => word.Text));
            if (text != theirs.Text)
            {
                yield return $"page {ours.Number} reads '{text}', Chromium '{theirs.Text}' (print.txt)";
            }
            else
            {
                foreach (var (word, measured) in words.Zip(theirs.Words))
                {
                    var at = $"page {ours.Number} '{word.Text}'";
                    var found = new[]
                    {
                        Far(at, "x0", word.X0, measured.X0), Far(at, "x1", word.X1, measured.X1),
                        BaselineDifference(at, word.Baseline, measured.Baseline, LineTop(lineTops, ours.Number, word.Baseline)), Far(at, "font size", word.FontSize, measured.FontSize),
                        Same(at, "colour", word.Colour, measured.Colour), Same(at, "face", word.Face, measured.Face),
                    };
                    foreach (var difference in found.OfType<string>())
                    {
                        yield return difference + " (print.txt)";
                    }
                }
            }

            var shapes = ShapesOfRectangles(ours, theirs);
            var (ourRectangles, theirRectangles) = (Outside(ours.Rectangles, shapes, ours: true), Outside(theirs.Rectangles, shapes, ours: false));
            var (ourPaths, theirPaths) = (ours with { Paths = [.. ours.Paths.Where(path => !shapes.Exists(shape => shape.InOurs && ReferenceEquals(shape.Path, path)))] },
                theirs with { Paths = [.. theirs.Paths.Where(path => !shapes.Exists(shape => !shape.InOurs && ReferenceEquals(shape.Path, path)))] });
            foreach (var area in PaintedDifferences(ours, ourRectangles, theirRectangles).Concat(Areas($"page {ours.Number} image", ours.Images, theirs.Images)).Concat(PathDifferences(ourPaths, theirPaths)))
            {
                yield return area + " (print.txt)";
            }
        }
    }

    /// <summary>
    /// Every difference between the glyphs inside the engine's words and those of Chromium's print (<c>glyphs.txt</c>,
    /// <see cref="PrintedWord.Glyphs"/>), on each page whose text is the same (<see cref="Differences"/> names a page whose
    /// text differs), word by word as <see cref="Differences"/> pairs them: the same number of glyphs; the same glyph id
    /// for each (Chromium embeds subsets of the fonts that keep the font program's glyph ids); each glyph's x as its
    /// distance from the word's first glyph within <see cref="Bound"/>, the bound of a word's x0 and x1 (the word's own x0
    /// is compared there); and each glyph's distance below the baseline within <see cref="Bound"/>. At most one difference
    /// per word and kind, the first glyph that differs, with how many differ.
    /// </summary>
    /// <param name="engine">The engine's pages.</param>
    /// <param name="chromium">Chromium's print, with the glyphs of its words.</param>
    /// <returns>The differences.</returns>
    public static IEnumerable<string> GlyphDifferences(PrintedPages engine, PrintedPages chromium)
    {
        foreach (var (ours, theirs) in engine.Pages.Zip(chromium.Pages))
        {
            var words = Joined(ours.Words);
            if (string.Join(" ", words.Select(word => word.Text)) != theirs.Text)
            {
                continue;
            }

            foreach (var (word, measured) in words.Zip(theirs.Words))
            {
                var at = $"page {ours.Number} '{word.Text}'";
                if (word.Glyphs.Length != measured.Glyphs.Length)
                {
                    yield return $"{at} is drawn with {word.Glyphs.Length} glyphs, Chromium's with {measured.Glyphs.Length} (glyphs.txt)";
                    continue;
                }

                var pairs = word.Glyphs.Zip(measured.Glyphs, (glyph, other) => (Ours: glyph, Theirs: other)).Select((pair, index) => (pair.Ours, pair.Theirs, Number: index + 1)).ToList();
                var found = new[]
                {
                    Glyphs(at, "is ", pairs.Where(pair => pair.Ours.Glyph != pair.Theirs.Glyph).ToList(), pairs.Count, (glyph, first) => $"{glyph.Glyph}"),
                    Glyphs(at, "lies at +", pairs.Where(pair => Math.Abs(pair.Ours.X - word.Glyphs[0].X - (pair.Theirs.X - measured.Glyphs[0].X)) > Bound).ToList(), pairs.Count,
                        (glyph, first) => Of(glyph.X - first!.X), word.Glyphs[0], measured.Glyphs[0]),
                    Glyphs(at, "lies below the baseline by ", pairs.Where(pair => Math.Abs(pair.Ours.Below - pair.Theirs.Below) > Bound).ToList(), pairs.Count, (glyph, first) => Of(glyph.Below)),
                };
                foreach (var difference in found.OfType<string>())
                {
                    yield return difference + " (glyphs.txt)";
                }
            }
        }

        static string? Glyphs(string at, string what, List<(PrintedGlyph Ours, PrintedGlyph Theirs, int Number)> differ, int count, Func<PrintedGlyph, PrintedGlyph?, string> value,
            PrintedGlyph? ourFirst = null, PrintedGlyph? theirFirst = null) =>
            differ.Count == 0 ? null
                : $"{at} glyph {differ[0].Number} {what}{value(differ[0].Ours, ourFirst)}, Chromium's {what}{value(differ[0].Theirs, theirFirst)}" + (differ.Count > 1 ? $" ({differ.Count} of {count} glyphs)" : string.Empty);
    }

    /// <summary>The first difference between the area the engine's rectangles paint and the area of the rectangles
    /// <c>lines.txt</c> holds (<c>page x0 y0 x1 y1 height</c>, read back by <c>rectangles.py</c>; the height is y1 less y0
    /// of the same line, nothing of its own), page by page as <see cref="PaintedDifferences"/> compares them. The file
    /// holds no colour, so the area is one for every colour; and it holds fills only, so the bands that Chromium's strokes
    /// paint (<c>print.txt</c>, <see cref="PrintedBox.Stroked"/>: Chromium strokes a border of one colour and style on
    /// all four sides as one rectangle) are added to it.</summary>
    private static IEnumerable<string> RectangleDifferences(PrintedPages engine, PrintedPages chromium, List<PrintedBox> measured)
    {
        foreach (var beyond in measured.Where(box => box.Page < 1 || box.Page > engine.Pages.Length).Select(box => box.Page).Distinct())
        {
            yield return $"rectangles on page {beyond}, the engine has {engine.Pages.Length} pages (lines.txt)";
        }

        static PrintedBox Colourless(PrintedBox box) => box with { Colour = null, Stroked = false };
        foreach (var (page, print) in engine.Pages.Zip(chromium.Pages))
        {
            // A shape one side paints with rectangles and the other with a path (ShapesOfRectangles) is compared in its
            // path's box by print.txt; lines.txt holds no colour, so Chromium's rectangles there are cut whatever colour.
            var shapes = ShapesOfRectangles(page, print);
            var filled = Outside(measured.Where(box => box.Page == page.Number).Concat(print.Rectangles.Where(box => box.Stroked).Select(Colourless)), shapes, ours: false);
            foreach (var found in PaintedDifferences(page, [.. Outside(page.Rectangles, shapes, ours: true).Select(Colourless)], [.. filled]))
            {
                yield return found + " (lines.txt)";
            }
        }
    }

    /// <summary>A path of one side whose area the other side paints with rectangles of its colour (<see cref="ShapesOfRectangles"/>).</summary>
    /// <param name="Path">The path; its box of whole pixels is where the rectangles were compared with it.</param>
    /// <param name="InOurs">Whether the path is the engine's (the rectangles Chromium's), else Chromium's.</param>
    private sealed record ShapeOfRectangles(PrintedPath Path, bool InOurs);

    /// <summary>
    /// The paths of a page that the other side paints with rectangles instead: a path that no path of the other side paints
    /// the same (<see cref="PrintedPath.Paints"/>), and whose rows are the rows the other side's rectangles of its colour
    /// give its box (<see cref="PathSampler.Rows"/>: the same sample points; a rectangle's part outside the box not
    /// counted). Case <c>bs-solid-one-colour</c>: Chromium fills a border of one colour and unequal widths as one shape, the
    /// border box with the padding box cut out of it, where the engine fills four rectangles of the same pixels. Such a
    /// path is compared with those rectangles only, in its box: the path leaves the comparison of paths, and the
    /// rectangles of its colour on the other side are cut to what lies outside the box (<see cref="Outside"/>) for the
    /// comparison of rectangles, so each pixel is compared once, in one of the two. A path against a path and rectangles
    /// against rectangles are compared as before.
    /// </summary>
    private static List<ShapeOfRectangles> ShapesOfRectangles(PrintedPage ours, PrintedPage theirs)
    {
        static bool Overlaps(PrintedBox box, PrintedPath path) => box.X0 < path.X1 && path.X0 < box.X1 && box.Y0 < path.Y1 && path.Y0 < box.Y1;
        IEnumerable<ShapeOfRectangles> Of(PrintedPage side, PrintedPage other, bool inOurs) =>
            side.Paths.Where(path => !other.Paths.Any(path.Paints))
                .Where(path => PathSampler.Rows([.. other.Rectangles.Where(box => box.Colour == path.Colour && Overlaps(box, path))], path).SequenceEqual(path.Rows))
                .Select(path => new ShapeOfRectangles(path, inOurs));
        return [.. Of(ours, theirs, inOurs: true), .. Of(theirs, ours, inOurs: false)];
    }

    /// <summary>Rectangles of one side without their parts inside the boxes of the paths the other side's shapes were
    /// compared with (<see cref="ShapesOfRectangles"/>): a rectangle of the path's colour (any colour where the rectangle has
    /// none, as in <c>lines.txt</c>) that reaches into a box is cut to its parts above, below, left and right of it.</summary>
    private static List<PrintedBox> Outside(IEnumerable<PrintedBox> boxes, List<ShapeOfRectangles> shapes, bool ours)
    {
        var kept = boxes.ToList();
        foreach (var shape in shapes.Where(shape => shape.InOurs != ours))
        {
            var path = shape.Path;
            kept = [.. kept.SelectMany(box => box.Colour is not null && box.Colour != path.Colour || box.X1 <= path.X0 || path.X1 <= box.X0 || box.Y1 <= path.Y0 || path.Y1 <= box.Y0
                ? [box]
                : new[]
                {
                    box with { Y1 = Math.Min(box.Y1, path.Y0) },
                    box with { Y0 = Math.Max(box.Y0, path.Y1) },
                    box with { Y0 = Math.Max(box.Y0, path.Y0), Y1 = Math.Min(box.Y1, path.Y1), X1 = Math.Min(box.X1, path.X0) },
                    box with { Y0 = Math.Max(box.Y0, path.Y0), Y1 = Math.Min(box.Y1, path.Y1), X0 = Math.Max(box.X0, path.X1) },
                }.Where(piece => piece.X0 < piece.X1 && piece.Y0 < piece.Y1))];
        }

        return kept;
    }

    /// <summary>The first difference between the areas the paths that are not rectangles (curves: a list marker's disc or
    /// circle) paint on a page (<see cref="PrintedPath"/>): the same number, and each in drawing order the same box of
    /// whole pixels, colour and painted share per pixel. Both sides are sampled by one rule: the engine's filled paths by
    /// <see cref="PathSampler"/>, Chromium's curves by <c>paths.py</c>.</summary>
    private static IEnumerable<string> PathDifferences(PrintedPage ours, PrintedPage theirs)
    {
        if (ours.Paths.Length != theirs.Paths.Length)
        {
            return [$"page {ours.Number} has {ours.Paths.Length} painted paths, Chromium {theirs.Paths.Length}"];
        }

        return ours.Paths.Zip(theirs.Paths).Where(pair => !pair.First.Paints(pair.Second)).Select(pair =>
            $"page {ours.Number} path at ({Of(pair.First.X0)}, {Of(pair.First.Y0)}) paints {string.Join('/', pair.First.Rows)} {pair.First.Colour}, " +
            $"Chromium at ({Of(pair.Second.X0)}, {Of(pair.Second.Y0)}) {string.Join('/', pair.Second.Rows)} {pair.Second.Colour}");
    }

    /// <summary>
    /// The differences between what two sets of painted rectangles show on a page: for each whole pixel of the page
    /// (0.75 pt), the colour of the last rectangle in drawing order that covers it, each edge after
    /// <see cref="WholePixel"/>, so an edge within <see cref="Bound"/> of Chromium's after snapping covers the same pixels.
    /// The area of each colour is compared, not each rectangle: Chromium cuts a border into other rectangles than the
    /// engine, which paint the same pixels (case <c>fr-f9-padded-box-orphans3</c>: Chromium's side borders run over the
    /// top border, from 93 pt down, the engine's start below it, at 93.75 pt), and strokes a border of one colour on all
    /// four sides as one rectangle where the engine fills four. The number of the rectangles is not compared; their order
    /// only where two colours overlap. One difference for each pair of colours (the engine's, Chromium's) that a pixel
    /// differs by, in the order of its first such pixel, row by row: that pixel, the area of its colour on each side, and
    /// how many pixels differ so and the box of whole pixels around them.
    /// </summary>
    private static IEnumerable<string> PaintedDifferences(PrintedPage page, IReadOnlyList<PrintedBox> ours, IReadOnlyList<PrintedBox> theirs)
    {
        var (engine, chromium) = (Painted(page, ours), Painted(page, theirs));
        var columns = ChromiumLayout.Pixel(page.Width);
        var pairs = new List<(string? Engine, string? Chromium, int First, int Count, int Left, int Top, int Right, int Bottom)>();
        for (var cell = 0; cell < engine.Length; cell++)
        {
            if (engine[cell] == chromium[cell])
            {
                continue;
            }

            var (x, y) = (cell % columns, cell / columns);
            var index = pairs.FindIndex(pair => pair.Engine == engine[cell] && pair.Chromium == chromium[cell]);
            if (index < 0)
            {
                pairs.Add((engine[cell], chromium[cell], cell, 1, x, y, x, y));
                continue;
            }

            var found = pairs[index];
            pairs[index] = found with { Count = found.Count + 1, Left = Math.Min(found.Left, x), Right = Math.Max(found.Right, x), Bottom = y };
        }

        foreach (var pair in pairs)
        {
            var (x, y) = (pair.First % columns * 0.75, pair.First / columns * 0.75);
            var who = (pair.Engine, pair.Chromium) switch
            {
                (null, _) => "Chromium paints it and the engine does not",
                (_, null) => "the engine paints it and Chromium does not",
                _ => $"the engine paints it {pair.Engine}, Chromium {pair.Chromium}",
            };
            var colour = pair.Engine ?? pair.Chromium;
            var area = colour is null or "" ? string.Empty : $" {colour}";
            yield return $"page {page.Number} painted area: the pixel at ({Of(x)}, {Of(y)}): {who}; the engine paints {engine.Count(painted => painted == colour)} px{area}, " +
                $"Chromium {chromium.Count(painted => painted == colour)} px; {pair.Count} px differ so, from ({Of(pair.Left * 0.75)}, {Of(pair.Top * 0.75)}) to ({Of((pair.Right + 1) * 0.75)}, {Of((pair.Bottom + 1) * 0.75)})";
        }
    }

    /// <summary>The whole pixels of a page, row by row from the top left: the colour of the last rectangle that covers it
    /// (an empty colour for a rectangle without one), or null; each edge after <see cref="WholePixel"/> and cut at the
    /// page's edges.</summary>
    private static string?[] Painted(PrintedPage page, IReadOnlyList<PrintedBox> boxes)
    {
        static int Pixel(double length) => ChromiumLayout.Pixel(length);
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
    /// Chromium's: their number per page is the engine's, its words joined as poppler joins them
    /// (<see cref="PrintedPage.JoinedWords"/>). Each is an engine word on the same page with the same text (matched by
    /// text and nearest place, its start and its baseline against <c>yMax</c>, since poppler writes them in its own reading
    /// order and the words of one text can start at one x on many lines) whose start and end are
    /// within <see cref="Bound"/> of <c>xMin</c> and <c>xMax</c>, and whose line box, the baseline where Chromium paints it less the
    /// ascender and plus the descender of the face's line metrics (<see cref="FontMetrics.Line"/>: the typographic ones
    /// where the face sets USE_TYPO_METRICS, as Chromium writes them into the PDF's font descriptor; measured with Theano
    /// Didot, case <c>f-theano-13-ab</c>) at the word's size, is within <see cref="Bound"/> of <c>yMin</c> and <c>yMax</c>.
    /// </summary>
    private static IEnumerable<string> PopplerDifferences(PageDocument document, PrintedPages engine, List<string[]> rows, IReadOnlyDictionary<(int Page, double Baseline), double> lineTops)
    {
        foreach (var beyond in rows.Select(row => int.Parse(row[0], CultureInfo.InvariantCulture)).Where(page => page < 1 || page > engine.Pages.Length).Distinct())
        {
            yield return $"words on page {beyond}, the engine has {engine.Pages.Length} pages (words.txt)";
        }

        foreach (var page in engine.Pages)
        {
            var inside = page.JoinedWords.Select(joined => joined.Word).Where(word => WholePixel(word.Baseline) <= page.Height && WholePixel(word.Baseline) - word.FontSize >= 0).ToList();
            var poppler = rows.Where(row => int.Parse(row[0], CultureInfo.InvariantCulture) == page.Number).ToList();
            if (inside.Count != poppler.Count)
            {
                yield return $"page {page.Number} has {inside.Count} words inside the page, Chromium {poppler.Count} (words.txt)";
            }

            foreach (var row in poppler)
            {
                var text = System.Net.WebUtility.HtmlDecode(row[5]);
                var word = inside.Where(word => word.Text == text).OrderBy(word => Math.Abs(word.X0 - Number(row[1])) + Math.Abs(WholePixel(word.Baseline) - Number(row[4]))).FirstOrDefault();
                if (word is null)
                {
                    yield return $"page {page.Number} has no word '{text}' (words.txt)";
                    continue;
                }

                inside.Remove(word);
                var at = $"page {page.Number} '{text}'";
                var metrics = Face(document.Pages[page.Number - 1], word);
                var baseline = PaintedBaseline(word.Baseline, LineTop(lineTops, page.Number, word.Baseline));
                var found = new[]
                {
                    Far(at, "xMin", word.X0, Number(row[1])), Far(at, "xMax", word.X1, Number(row[3])),
                    Far(at, "yMin", baseline - metrics.ToPoints(metrics.Line.Ascender, word.FontSize), Number(row[2])),
                    Far(at, "yMax", baseline - metrics.ToPoints(metrics.Line.Descender, word.FontSize), Number(row[4])),
                };
                foreach (var difference in found.OfType<string>())
                {
                    yield return difference + " (words.txt)";
                }
            }
        }
    }

    /// <summary>
    /// The first difference between the engine's boxes and the sizes measured in the page (<c>boxes.txt</c>: <c>tag width
    /// height label</c>, the label the element's id where it has one): the height and the width of the box of the
    /// element, each within <see cref="Bound"/>, unsnapped (Chromium's <c>getBoundingClientRect</c>). <c>measure.mjs</c>
    /// measures the boxes with print media in a browser window as wide as the print's page area, the width of the clip
    /// around each page's body rounded up to a whole pixel (README of <c>tools/reference/chromium</c>, "Box sizes"), so a
    /// box is as wide as in the print, and a height that depends on the width, text that wraps, is the print's.
    /// <para>What is compared with what. The window is one layout without page breaks, so the file holds one size per
    /// element, the whole box (case <c>a2-row-end</c>: a cell of 22 lines, 330.75 pt, over two pages). A box the engine
    /// prints in one part is compared on every page it is on: that part, and each copy of it in a header row a table
    /// repeats (<see cref="BoxFragment.IsRepeated"/>), which is repeated whole. A box the engine cuts over pages has no
    /// part of that height: each part ends where its page ends, with the room left empty above the break, and a table
    /// repeats its header rows inside it. Its height and width are compared in the engine's layout of the same boxes
    /// without breaks (<see cref="Unbroken"/>), and its copies in repeated header rows as above; its parts are what the print shows, and
    /// <c>print.txt</c>, <c>lines.txt</c> and <c>words.txt</c> compare them.</para>
    /// <para>How a box is found. A table cell (<c>td</c>, <c>th</c>; <c>measure.mjs</c> writes every cell, in document
    /// order, with its id or the start of its text): the file's n-th cell is the document's n-th cell element, and its box
    /// the cell box written on its line in the same place among the cells that start on that line (a cell often shares its
    /// line with its row and the cells beside it). Another element, by its id (<see cref="IdBox"/>): by the same rule
    /// among the elements of its kind on its line. A row the file names that is not one element of that tag, or whose box
    /// the rule does not find on a page, is a difference: the comparison does not skip it.</para>
    /// </summary>
    private static IEnumerable<string> BoxDifferences(CaseFolder folder, string name, HtmlBoxes boxes, List<string[]> rows) =>
        BoxDifferences(Parsed(folder, name).Value.Document, boxes, rows);

    /// <summary>The first difference between the boxes of a case of a folder and the given rows of a <c>boxes.txt</c>
    /// (<see cref="BoxDifferences(CaseFolder, string, HtmlBoxes, List{string[]})"/>), or null; the refusal where the case
    /// has no boxes.</summary>
    public static string? BoxDifference(CaseFolder folder, string name, IReadOnlyList<string> rows)
    {
        var boxes = Boxes(folder, name);
        return boxes.IsFailure
            ? Refusal(boxes.Error)
            : BoxDifference(Parsed(folder, name).Value.Document, boxes.Value, [.. rows.Where(row => row.Length > 0).Select(row => row.Split(' '))]);
    }

    /// <summary>The first difference between the boxes of an HTML text, read as a file of the cases folder (its relative
    /// URLs, <c>common.css</c> and the fonts, resolve there), and the given rows of a <c>boxes.txt</c>, or null; the
    /// refusal where it has no boxes.</summary>
    public static string? BoxDifference(string html, IReadOnlyList<string> rows)
    {
        var baseUrl = new Uri(Path.Combine(Folder(), "text.html")).AbsoluteUri;
        var parsed = HtmlParser.Parse(html, new HtmlParseOptions("text.html"));
        var boxes = parsed
            .OnSuccessBind(document => HtmlStyleResolver.Resolve(document, new HtmlStyleOptions { Page = Letter, Resources = Resource, BaseUrl = baseUrl }))
            .OnSuccessBind(styled => HtmlBoxGenerator.Generate(styled, new HtmlBoxOptions { Resources = Resource, BaseUrl = baseUrl, MissingCharacters = Shaper.MissingCharacters }));
        return boxes.IsFailure
            ? Refusal(boxes.Error)
            : BoxDifference(parsed.Value.Document, boxes.Value, [.. rows.Where(row => row.Length > 0).Select(row => row.Split(' '))]);
    }

    /// <summary>The measured <c>boxes.txt</c> of a case of a folder, line by line.</summary>
    public static IReadOnlyList<string> BoxRows(CaseFolder folder, string name) => File.ReadAllLines(Measured(folder, name) + ".boxes.txt");

    /// <summary>The first difference between the boxes of a document and the rows of its <c>boxes.txt</c>, by the rules of
    /// <see cref="BoxDifferences(CaseFolder, string, HtmlBoxes, List{string[]})"/>, or null.</summary>
    /// <param name="document">The parsed document.</param>
    /// <param name="boxes">Its boxes.</param>
    /// <param name="rows">The rows of the file, split at spaces.</param>
    /// <returns>The difference, ending in <c>(boxes.txt)</c>, or null.</returns>
    public static string? BoxDifference(HtmlDocument document, HtmlBoxes boxes, IReadOnlyList<string[]> rows) => BoxDifferences(document, boxes, rows).FirstOrDefault();

    /// <summary>Every difference between the boxes of a document and the rows of its <c>boxes.txt</c>, by the rules of
    /// <see cref="BoxDifferences(CaseFolder, string, HtmlBoxes, List{string[]})"/>, row by row in the file's order: of each
    /// row the box not found, or its height and its width. Where the number of cells differs the rows cannot be paired
    /// with the cells, and that is the one difference.</summary>
    public static IEnumerable<string> BoxDifferences(HtmlDocument document, HtmlBoxes boxes, IReadOnlyList<string[]> rows)
    {
        if (rows.Count == 0)
        {
            yield break;
        }

        var elements = Elements(document).ToList();
        var environment = LayoutEnvironment.Create(boxes.Fonts);
        var pages = Paginator.Paginate(environment, boxes.Document);
        if (pages.IsFailure)
        {
            yield return $"the pages for the boxes failed: {pages.Error.Code} (boxes.txt)";
            yield break;
        }

        var cells = elements.Where(IsCell).ToList();
        var cellRows = rows.Count(row => row[0] is "td" or "th");
        if (cellRows != cells.Count)
        {
            yield return $"{cellRows} cells, the document {cells.Count} (boxes.txt)";
            yield break;
        }

        var unbroken = new Lazy<Result<ValueList<PageFragment>, Error>>(() => Paginator.Paginate(environment, Unbroken(boxes.Document)));
        var cellIndex = 0;
        foreach (var row in rows)
        {
            var label = row.Length > 3 ? string.Join(' ', row[3..]) : string.Empty;
            var (box, missing) = row[0] is "td" or "th" ? CellBox(boxes.Document.Root, cells, cells[cellIndex++], row, label) : IdBox(boxes.Document.Root, elements, row, label);
            foreach (var different in missing is not null ? [missing] : SizeDifferences(box!, pages.Value, unbroken, row, label))
            {
                yield return different + " (boxes.txt)";
            }
        }
    }

    private static bool IsCell(HtmlElement element) => element.Namespace == ElementNamespace.Html && element.LocalName is "td" or "th";

    /// <summary>The cell box of one cell row of <c>boxes.txt</c> (<see cref="BoxDifferences(CaseFolder, string, HtmlBoxes, List{string[]})"/>):
    /// the same tag and, where the element has an id, the same label; the cell box in the same place among the cells
    /// written on the element's line. Else the difference.</summary>
    private static (Box? Box, string? Difference) CellBox(Box root, List<HtmlElement> cells, HtmlElement cell, string[] row, string label)
    {
        var at = $"box {row[0]} '{label}'";
        if (cell.LocalName != row[0] || (cell.Attribute("id") is { } id && id != label))
        {
            return (null, $"{at} is the document's <{cell.LocalName}> '{cell.Attribute("id")}' at line {cell.Position.Line}");
        }

        var line = cell.Position.Line;
        var place = cells.Where(other => other.Position.Line == line).ToList().IndexOf(cell);
        var box = Boxes(root).Where(box => box.Source.Line == line && (box.Role == SemanticRole.TableCell || box.Role is SemanticRole.TableHeaderCellRole)).ElementAtOrDefault(place);
        return box is null ? (null, $"{at} has no cell box on any page") : (box, null);
    }

    /// <summary>
    /// The box of an element <c>boxes.txt</c> names by its id: the one element with that id, of the row's tag; the role
    /// its tag gives its box (<see cref="RoleOf"/>: a <c>table</c> a table, a <c>p</c> a paragraph, a heading its level,
    /// any other element a generic box); and its box found as a cell's is: in the same place among the boxes of that role
    /// written on its line as the element among the elements of that role that start on the line (case <c>te-top</c>:
    /// <c>&lt;div&gt;&lt;table id="t"&gt;</c> on one line, the table's box is the line's one table box, not the div's box
    /// before it). A box carries its source line only, so where the line holds more or fewer boxes of the role than
    /// elements (an element of the role that makes no box, an inline element, an anonymous box of a block written on the
    /// line) the place is not known, and that is the difference. Else the difference. Column elements and a <c>br</c>
    /// are no peers: they never make a box of their own (a column is read by its table, a <c>br</c> is a line break in
    /// the text; case <c>tw-col</c>: <c>&lt;div id="w"&gt;&lt;table&gt;&lt;colgroup&gt;&lt;col&gt;</c> on one line).
    /// </summary>
    internal static (Box? Box, string? Difference) IdBox(Box root, IReadOnlyList<HtmlElement> elements, string[] row, string label)
    {
        var at = $"box {row[0]} '{label}'";
        var named = elements.Where(element => element.Attribute("id") == label).ToList();
        if (named.Count != 1 || named[0].LocalName != row[0])
        {
            return (null, $"{at} is not one <{row[0]}> with that id");
        }

        var (element, role) = (named[0], RoleOf(named[0]));
        var line = element.Position.Line;
        var peers = elements.Where(other => other.Position.Line == line && RoleOf(other) == role && !MakesNoBox(other)).ToList();
        var candidates = Boxes(root).Where(box => box.Source.Line == line && (box.Role == role || (box.Role is SemanticRole.TableHeaderCellRole && role is SemanticRole.TableHeaderCellRole))).ToList();
        return candidates.Count == peers.Count
            ? (candidates[peers.IndexOf(element)], null)
            : (null, $"{at} is not found: line {line} has {candidates.Count} boxes of role {role.GetType().Name} for {peers.Count} elements of that role");
    }

    /// <summary>An element the HTML front-end never makes a box for: a column, a column group, a line break.</summary>
    private static bool MakesNoBox(HtmlElement element) => element.Namespace == ElementNamespace.Html && element.LocalName is "col" or "colgroup" or "br";

    /// <summary>The role the HTML front-end gives the box of an element by its tag, for <see cref="IdBox"/>: a table, its
    /// caption, a table cell, a paragraph, a heading, a row group or a row; any other element a generic box.</summary>
    private static SemanticRole RoleOf(HtmlElement element) => element.Namespace != ElementNamespace.Html ? SemanticRole.Generic : element.LocalName switch
    {
        "table" => SemanticRole.Table,
        "caption" => SemanticRole.Caption,
        "td" => SemanticRole.TableCell,
        "th" => SemanticRole.TableHeaderCell,
        "p" => SemanticRole.Paragraph,
        ['h', >= '1' and <= '6'] name => SemanticRole.Heading(name[1] - '0'),
        "thead" => SemanticRole.TableHead,
        "tbody" or "tfoot" => SemanticRole.TableBody,
        "tr" => SemanticRole.TableRow,
        _ => SemanticRole.Generic,
    };

    /// <summary>
    /// The size difference of one box, its height and then its width (<see cref="BoxDifferences(CaseFolder, string, HtmlBoxes, List{string[]})"/>): its
    /// parts on the pages, a copy in a repeated header row not counted as a part. One part: that part and every copy.
    /// More than one: the box in the layout without breaks (<see cref="Unbroken"/>), which must be one page, and every
    /// copy. Else the difference.
    /// </summary>
    internal static string? SizeDifference(Box box, IReadOnlyList<PageFragment> pages, Lazy<Result<ValueList<PageFragment>, Error>> unbroken, string[] row, string label) =>
        SizeDifferences(box, pages, unbroken, row, label).FirstOrDefault();

    /// <summary>Every size difference of one box, by the rules of <see cref="SizeDifference"/>: of each part or copy it
    /// compares, the height and then the width.</summary>
    private static IEnumerable<string> SizeDifferences(Box box, IReadOnlyList<PageFragment> pages, Lazy<Result<ValueList<PageFragment>, Error>> unbroken, string[] row, string label)
    {
        var at = $"box {row[0]} '{label}'";
        var found = pages.Select(page => Part(page.Root.Fragment, box.Id)).Where(part => part is not null).Select(part => part!.Value).ToList();
        if (found.Count == 0)
        {
            return [$"{at} has no box on any page"];
        }

        var copies = found.Where(part => part.Repeated).Select(part => part.Fragment).ToList();
        var parts = found.Where(part => !part.Repeated).Select(part => part.Fragment).ToList();
        if (parts.Count <= 1)
        {
            return parts.Concat(copies).SelectMany(fragment => Sized(at, string.Empty, fragment, row));
        }

        if (unbroken.Value.IsFailure)
        {
            return [$"{at}: the layout without breaks failed: {unbroken.Value.Error.Code}"];
        }

        if (unbroken.Value.Value.Count != 1)
        {
            return [$"{at}: the layout without breaks has {unbroken.Value.Value.Count} pages"];
        }

        if (Part(unbroken.Value.Value[0].Root.Fragment, box.Id) is not { } whole)
        {
            return [$"{at} has no box in the layout without breaks"];
        }

        return Sized(at, $" without breaks (in {parts.Count} parts)", whole.Fragment, row).Concat(copies.SelectMany(fragment => Sized(at, string.Empty, fragment, row)));
    }

    /// <summary>The height of a fragment against the row's height, then its width against the row's width (<c>tag width
    /// height label</c>), each within <see cref="Bound"/>: the ones that differ.</summary>
    private static IEnumerable<string> Sized(string at, string where, BoxFragment fragment, string[] row) =>
        new[] { Far(at, "height" + where, fragment.Size.Height, Number(row[2])), Far(at, "width" + where, fragment.Size.Width, Number(row[1])) }.OfType<string>();

    /// <summary>
    /// The same boxes on one page tall enough that nothing in them breaks: the page as tall as the model allows
    /// (<see cref="ModelLimits.MaxLength"/>, 14,400 pt; the longest document, <c>timesheet-pages</c>, is under 5,000 pt
    /// laid out), as wide, with the same margins and the same templates. The window <c>boxes.txt</c> is measured in is such
    /// a layout. A window has no pages, so it ignores a break the document forces (<c>break-before: page</c>): the forced
    /// breaks are dropped too (case <c>fr-forced-table</c>: the cell cut after a forced break is one box in the window).
    /// That nothing broke is checked, not assumed: the layout must be one page (<see cref="SizeDifference"/>). The header
    /// and footer areas (the HTML front-end's rows of page-margin boxes) stay: no box of <c>boxes.txt</c> is in them, and
    /// they take from the page area what they take on a printed first page, so the body starts where it starts there and is
    /// as wide.
    /// </summary>
    internal static StyledDocument Unbroken(StyledDocument document) =>
        document with { Page = document.Page with { Height = ModelLimits.MaxLength }, Root = WithoutForcedBreaks(document.Root) };

    /// <summary>The box and every box inside it without a forced break before or after it.</summary>
    private static Box WithoutForcedBreaks(Box box) => box with
    {
        Style = box.Style with
        {
            BreakBefore = box.Style.BreakBefore == BreakBetween.Page ? BreakBetween.Auto : box.Style.BreakBefore,
            BreakAfter = box.Style.BreakAfter == BreakBetween.Page ? BreakBetween.Auto : box.Style.BreakAfter,
        },
        Children = [.. box.Children.Select(child => child is Box inner ? WithoutForcedBreaks(inner) : child)],
    };

    /// <summary>The first fragment of a page of the box with the id, in document order, and whether it is inside a
    /// repeated copy (<see cref="BoxFragment.IsRepeated"/>, set on the copy's row group, not on the boxes inside it); or
    /// null.</summary>
    private static (BoxFragment Fragment, bool Repeated)? Part(Fragment root, NodeId id)
    {
        var stack = new Stack<(Fragment Fragment, bool Repeated)>([(root, false)]);
        while (stack.Count > 0)
        {
            var (fragment, repeated) = stack.Pop();
            if (fragment is BoxFragment box)
            {
                if (box.Box.Id == id)
                {
                    return (box, repeated || box.IsRepeated);
                }

                foreach (var child in box.Children.Reverse())
                {
                    stack.Push((child.Fragment, repeated || box.IsRepeated));
                }
            }
        }

        return null;
    }

    /// <summary>Every box of the model, in document order, with an explicit stack.</summary>
    private static IEnumerable<Box> Boxes(Box root)
    {
        var stack = new Stack<Box>([root]);
        while (stack.Count > 0)
        {
            var box = stack.Pop();
            yield return box;
            foreach (var child in box.Children.OfType<Box>().Reverse())
            {
                stack.Push(child);
            }
        }
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

    /// <summary>Every edge of the images of a page that after <see cref="WholePixel"/> is not within <see cref="Bound"/> of
    /// Chromium's, image by image in drawing order; or a different count, the one difference.</summary>
    private static IEnumerable<string> Areas(string at, IReadOnlyList<PrintedBox> ours, IReadOnlyList<PrintedBox> theirs)
    {
        if (ours.Count != theirs.Count)
        {
            return [$"{at}s: {ours.Count}, Chromium {theirs.Count}"];
        }

        return ours.Zip(theirs.Select((box, index) => (box, index + 1))).SelectMany(pair =>
        {
            var (box, (measured, index)) = pair;
            var where = $"{at} {index}";
            return new[]
            {
                Snapped(where, "x0", box.X0, measured.X0), Snapped(where, "y0", box.Y0, measured.Y0),
                Snapped(where, "x1", box.X1, measured.X1), Snapped(where, "y1", box.Y1, measured.Y1),
            }.OfType<string>();
        });
    }

    private static string? Same(string at, string what, string? ours, string? chromium) =>
        ours != chromium ? $"{at} {what} {ours}, Chromium {chromium}" : null;

    private static string? Far(string at, string what, double ours, double chromium) =>
        Math.Abs(ours - chromium) > Bound ? $"{at} {what} {Of(ours)}, Chromium {Of(chromium)}" : null;

    /// <summary>
    /// A baseline compared where Chromium paints it: the top of its line at the nearest whole pixel (<see cref="WholePixel"/>),
    /// and the baseline at its distance from that top as laid out; without the line's top, the baseline itself after
    /// <see cref="WholePixel"/>. Chromium rounds the line's top to a whole pixel and keeps the text's place inside the line.
    /// For a line of text alone the two rules agree, since its baseline lies a whole number of pixels below its top
    /// (<see cref="FontMetricRounding.WholePixels"/>); beside an image in the line they do not: in cases <c>ii-height</c>
    /// and <c>ii-page</c> the engine lays the baseline out at 52 pt and Chromium prints 51.996 pt: equal by this rule,
    /// where the baseline alone at the nearest whole pixel would be 51.75 pt.
    /// </summary>
    private static string? BaselineDifference(string at, double ours, double chromium, double? top)
    {
        var painted = PaintedBaseline(ours, top);
        return Math.Abs(painted - chromium) > Bound ? $"{at} baseline {Of(painted)} (laid out at {Of(ours)}), Chromium {Of(chromium)}" : null;
    }

    /// <summary>The top of the line a baseline of a page lies in (<see cref="LineTops"/>), or null.</summary>
    private static double? LineTop(IReadOnlyDictionary<(int Page, double Baseline), double>? lineTops, int page, double baseline) =>
        lineTops is not null && lineTops.TryGetValue((page, Math.Round(baseline, 6)), out var top) ? top : null;

    /// <summary>Where Chromium paints a baseline (<see cref="BaselineDifference"/>).</summary>
    private static double PaintedBaseline(double baseline, double? top) => top is { } lineTop ? WholePixel(lineTop) + (baseline - lineTop) : WholePixel(baseline);

    /// <summary>The top of the line each text of the engine's pages lies in, by page number and baseline (rounded to 1e-6
    /// pt), from the fragments of the same layout: a line's offset, and a text's baseline its offset plus its ascent. The
    /// lines of the page-margin boxes too: Chromium lays a margin box's margins and padding out in its unit of 1/64 px, as
    /// in the body, and paints their lines by the same rule (cases <c>pm-len-sum</c>, <c>pm-len-margin</c>,
    /// <c>pm-len-padding</c>, <c>pm-len-body</c>: 5pt + 5pt above a line puts its top at 13 px, not 14).</summary>
    private static Dictionary<(int Page, double Baseline), double> LineTops(HtmlBoxes boxes)
    {
        var tops = new Dictionary<(int Page, double Baseline), double>();
        var pages = Paginator.Paginate(LayoutEnvironment.Create(boxes.Fonts), boxes.Document);
        if (pages.IsFailure)
        {
            return tops;
        }

        foreach (var (page, number) in pages.Value.Select((page, index) => (page, index + 1)))
        {
            var pending = new Stack<(PlacedFragment Placed, double X, double Y, double? LineTop)>();
            foreach (var area in new[] { page.Footer, page.Root, page.Header })
            {
                if (area is not null)
                {
                    pending.Push((area, 0, 0, null));
                }
            }
            while (pending.Count > 0)
            {
                var (placed, x, y, lineTop) = pending.Pop();
                var (left, top) = (x + placed.Offset.X, y + placed.Offset.Y);
                switch (placed.Fragment)
                {
                    case TextFragment text when lineTop is { } line:
                        tops.TryAdd((number, Math.Round(top + text.Ascent, 6)), line);
                        break;
                    case LineFragment line:
                        foreach (var child in line.Children)
                        {
                            pending.Push((child, left, top, top));
                        }

                        break;
                    case BoxFragment box:
                        foreach (var child in box.Children)
                        {
                            pending.Push((child, left, top, lineTop));
                        }

                        break;
                    case SliceFragment slice:
                        pending.Push((slice.Content, left, top, lineTop));
                        break;
                }
            }
        }

        return tops;
    }

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
    /// A length down or across the page as Chromium paints it: at the nearest whole CSS pixel of the page (0.75 pt), half a
    /// pixel up for either sign (<see cref="ChromiumLayout.Pixel"/>).
    /// Chromium paints a baseline at a whole pixel (docs/architecture.md, "Against Chromium"; <c>FontMetricRoundingTests</c>
    /// compares the same way) where the line holds text only (beside an image: <see cref="BaselineDifference"/>), and the
    /// edges of a filled rectangle and of an image area too. Measured: all 6,104 rectangle
    /// edges and all 83 image edges of the 294 committed prints are whole pixels, and all but 10 of the 4,349 baselines (the
    /// 10 are in case <c>font-sizes</c>, at 8.2 and 9.1 pt, which the HTML path refuses). The rule moves a value by up to
    /// 0.375 pt, so a difference that small in a baseline or an edge can stay inside one pixel; the x positions of words
    /// and the box heights of <c>boxes.txt</c> are not snapped in Chromium's files and are compared as they are.
    /// </summary>
    private static double WholePixel(double length) => ChromiumLayout.WholePixel(length);

    /// <summary>
    /// The engine's words as PyMuPDF finds them in Chromium's PDF (<c>positions.py</c>, <c>print.txt</c>): two runs of
    /// glyphs on one baseline at the same font size, in the same face and colour, with no white space between
    /// (<see cref="PrintedPage.PyMuPdfWordGap"/>: text on both sides of an inline element's edge, kerned or not, or the
    /// words of two cells side by side close enough), are one word;
    /// PyMuPDF starts a new span, and <c>positions.py</c> a new word, where the font, the size or the colour changes.
    /// Poppler's words (<c>words.txt</c>) join across a change of face or colour too: <see cref="PrintedPage.JoinedWords"/>.
    /// </summary>
    private static List<PrintedWord> Joined(IEnumerable<PrintedWord> words)
    {
        var joined = new List<PrintedWord>();
        foreach (var word in words)
        {
            if (joined.Count > 0 && joined[^1] is var last && last.Baseline == word.Baseline && word.X0 - last.X1 < PrintedPage.PyMuPdfWordGap * word.FontSize
                && last.FontSize == word.FontSize && last.Face == word.Face && last.Colour == word.Colour && word.IsDrawnRightAfter(last))
            {
                joined[^1] = last with { X1 = word.X1, Text = last.Text + word.Text, Drawn = word.Drawn, Glyphs = last.Glyphs.AddRange(word.Glyphs) };
                continue;
            }

            joined.Add(word);
        }

        return joined;
    }
}

/// <summary>A folder of HTML files compared with Chromium's prints of them (<see cref="HtmlCases.Status(CaseFolder, string, string?)"/>):
/// the HTML files, and the folder of their measured files and committed status files, one status file per HTML file.</summary>
/// <param name="Html">The folder of the HTML files.</param>
/// <param name="Measured">The folder of the files measured for each (<c>&lt;name&gt;.print.txt</c> and the others) and of
/// their status files.</param>
internal sealed record CaseFolder(string Html, string Measured)
{
    /// <summary>The ending of a status file: <c>&lt;name&gt;.status.txt</c> holds the one line <c>name status rest</c>
    /// (<see cref="HtmlCases.Accepted"/>).</summary>
    public const string StatusEnding = ".status.txt";

    /// <summary>Every HTML file of the folder, by name without its extension, in ordinal order.</summary>
    public IReadOnlyList<string> Names =>
        [.. Directory.GetFiles(Html, "*.html").Select(Path.GetFileNameWithoutExtension).Order(StringComparer.Ordinal)!];

    /// <summary>Every status file of the folder, by the name it is of, in ordinal order.</summary>
    public IReadOnlyList<string> StatusNames =>
        [.. Directory.GetFiles(Measured, "*" + StatusEnding).Select(file => Path.GetFileName(file)[..^StatusEnding.Length]).Order(StringComparer.Ordinal)];

    /// <summary>The committed status file of an HTML file.</summary>
    /// <param name="name">The file, without its extension.</param>
    /// <returns>The path.</returns>
    public string StatusFile(string name) => Path.Combine(Measured, name + StatusEnding);

    /// <summary>The committed status of an HTML file without its name, where its status file starts with the name: its
    /// first line (<c>equal</c>, <c>refused …</c>, or <c>listed …</c> and <c>open …</c>, which the list of their
    /// differences follows, <see cref="HtmlCases.DifferenceList"/>) and the lines after it, joined by line feeds; otherwise
    /// null.</summary>
    /// <param name="name">The file, without its extension.</param>
    /// <returns>The status, or null.</returns>
    public string? Committed(string name) =>
        File.Exists(StatusFile(name)) && StatusText(File.ReadAllText(StatusFile(name))) is var text && text.StartsWith(name + " ", StringComparison.Ordinal) ? text[(name.Length + 1)..] : null;

    /// <summary>The text of a status file as the status test compares it: every line ending a line feed, whatever the
    /// checkout wrote (a Windows checkout writes carriage returns before them), and without the line feed that ends the
    /// file.</summary>
    /// <param name="file">The file's text.</param>
    /// <returns>The lines joined by line feeds.</returns>
    public static string StatusText(string file)
    {
        var text = file.ReplaceLineEndings("\n");
        return text.EndsWith('\n') ? text[..^1] : text;
    }
}
