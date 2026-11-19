using System.IO;
using System.Runtime.CompilerServices;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Html;
using Ave.Extensions.Documents.Html.Boxes;
using Ave.Extensions.Documents.Html.Parsing;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace UnitTests.Extensions.Documents.Html.Boxes;

/// <summary>
/// The Chromium reference cases (<c>tools/reference/chromium/cases</c>) through the whole HTML path, unchanged: parse,
/// resolve the styles (the resolver serves <c>common.css</c> and the font files from the repository), generate the
/// boxes, lay out; then the engine's pages beside Chromium's print of the same file (<c>measured/&lt;case&gt;.print.txt</c>).
/// </summary>
internal static class HtmlCases
{
    /// <summary>
    /// How far a word may be from Chromium's, in points, across and down: 0.1 pt. Where it comes from: the whole-print
    /// tests of the code front-end compare baselines within one Chromium layout unit (1/64 px, 0.0117 pt,
    /// <c>FontMetricRoundingTests</c>), and x positions differ by Chromium's own font-size rounding, which scales the
    /// advances of a 10 pt line by 0.99961 (<c>ChromiumLayout.FontSizeFactor</c>): 0.08 pt at the end of a 210 pt line.
    /// The bound covers both and is no wider than 0.1 pt, the widest the reference rules of the family allow; a case
    /// that needs more fails as different, it is not hidden.
    /// </summary>
    public const double Bound = 0.1;

    /// <summary>
    /// The cases whose difference is a listed limit of layout, pinned by name in the code front-end's tests
    /// (<c>ListedDifferenceTests</c> of <c>UnitTests.Extensions.Documents.Fluent</c>; docs/architecture.md, "Known limits"):
    /// the status file says <c>listed</c> and the test name, and the case must still differ.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Listed = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["fr-old-forced-after-empty"] = "A_forced_break_after_only_empty_boxes_is_not_taken",
        ["fr-old-forced-in-keep-together"] = "A_forced_break_inside_a_box_kept_together_moves_the_whole_box_first",
        ["fr-old-empty-box-past-end"] = "An_empty_box_whose_margin_carries_it_past_the_page_end_stays_on_the_page",
        ["fr-old-blank-last-page"] = "An_empty_box_past_the_end_of_the_last_page_adds_no_page",
        ["fr-old-keep-together-hides"] = "A_box_kept_together_does_not_hide_a_better_breakpoint_before_it",
        ["fr-kwn-pad-on-inner-box"] = "Keep_with_next_inside_a_box_holds_across_its_bottom_padding_on_a_page_of_its_own",
        ["fr-kwn-pad-then-bordered-box-widows4"] = "Keep_with_next_inside_a_box_holds_across_its_bottom_padding_on_a_page_of_its_own",
        ["fr-kwn-pad-then-kept-para"] = "Keep_with_next_inside_a_box_holds_across_its_bottom_padding_on_a_page_of_its_own",
        ["fr-kwn-pad-then-para"] = "Keep_with_next_inside_a_box_holds_across_its_bottom_padding_on_a_page_of_its_own",
        ["fr-moved-para-widows-4"] = "A_paragraph_that_moved_to_a_new_page_whole_counts_only_its_orphans_before_a_break_there",

        // LineBoxReferenceTests, FontMetricRoundingTests and BottomPaddingTests of the same project. The last pins only the
        // words of each page: Chromium prints the continued box's bottom border on page 2, past the end of the page area
        // the engine computes (43.92 pt), and the engine on page 3, so the word after it is 1.5 pt lower.
        ["fr-border-continued"] = "A_box_that_continues_keeps_its_lines_and_moves_only_its_bottom_border",

        // Not pinned by a test: the page size. Chromium prints this 595.5 x 842.25 pt page as 595.92 x 841.92 pt, which
        // the page rounding of the HTML front-end (to 1/300 in) does not give; the words are where Chromium has them.
        ["fr-pad-figure"] = "Page_size_rounding_of_595.5_x_842.25_pt_not_settled",
        ["fr-lh-first-baseline"] = "Under_an_explicit_line_height_the_first_baseline_is_within_the_measured_difference_of_Chromium_s",
        ["f-tiny"] = "At_font_sizes_below_half_a_pixel_Chromium_draws_no_text_where_the_engine_does",
        ["f-tiny-038"] = "At_font_sizes_below_half_a_pixel_Chromium_draws_no_text_where_the_engine_does",
    };

    /// <summary>The cases folder in the source tree.</summary>
    public static string Folder([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", "tools", "reference", "chromium", "cases"));

    /// <summary>The committed status file: one line per case, <c>name equal</c> or <c>name refused code what</c>.</summary>
    public static string StatusFile => Path.Combine(Folder(), "..", "html-status.txt");

    /// <summary>Every case, by name, in ordinal order.</summary>
    public static IReadOnlyList<string> Names =>
        [.. Directory.GetFiles(Folder(), "*.html").Select(Path.GetFileNameWithoutExtension).Order(StringComparer.Ordinal)!];

    /// <summary>The family folder: what the cases' relative URLs may reach.</summary>
    private static string Family => Path.GetFullPath(Path.Combine(Folder(), "..", "..", "..", ".."));

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

    /// <summary>The boxes of a case.</summary>
    public static Result<HtmlBoxes, Error> Boxes(string name, UnsupportedPolicy policy = UnsupportedPolicy.Refuse)
    {
        var path = Path.Combine(Folder(), name + ".html");
        var baseUrl = new Uri(path).AbsoluteUri;
        return HtmlParser.Parse(File.ReadAllText(path), new HtmlParseOptions(name + ".html"))
            .OnSuccessBind(parsed => HtmlStyleResolver.Resolve(parsed, new HtmlStyleOptions { Resources = Resource, BaseUrl = baseUrl, Unsupported = policy }))
            .OnSuccessBind(styled => HtmlBoxGenerator.Generate(styled, new HtmlBoxOptions { Resources = Resource, BaseUrl = baseUrl, Unsupported = policy }));
    }

    /// <summary>The status of a case: <c>equal</c>, <c>refused code what</c>, or <c>different …</c> / <c>failed …</c>,
    /// which the status test never accepts.</summary>
    public static string Status(string name)
    {
        var boxes = Boxes(name);
        if (boxes.IsFailure)
        {
            var first = boxes.Error.Parts().First();
            var what = first.Metadata is { } metadata && metadata.TryGetValue(ErrorMetadataKeys.Property, out var property)
                ? $"{property}: {metadata[ErrorMetadataKeys.Value]}"
                : first.Metadata is { } other && other.TryGetValue(ErrorMetadataKeys.Value, out var value) ? $"{value}" : first.Message;
            return first.Code.ToString().StartsWith("Documents.Html.Unsupported", StringComparison.Ordinal)
                || first.Code.ToString().StartsWith("Documents.Css", StringComparison.Ordinal)
                ? $"refused {first.Code} {what}"
                : $"failed {first.Code} {first.Message}";
        }

        var pages = DocumentLayout.Run(boxes.Value.Document, boxes.Value.Fonts);
        if (pages.IsFailure)
        {
            return $"failed {pages.Error.Code} {pages.Error.Message}";
        }

        var print = Path.Combine(Folder(), "..", "measured", name + ".print.txt");
        return File.Exists(print)
            ? (Difference(PrintedPages.Engine(pages.Value), PrintedPages.Parse(File.ReadAllText(print))), Listed.TryGetValue(name, out var listed)) switch
            {
                (null, false) => "equal",
                ({ } difference, false) => $"different {difference}",
                ({ }, true) => $"listed {listed}",
                (null, true) => "different: equal, but listed as a difference",
            }
            : "failed no print of the case";
    }

    /// <summary>The first difference between the engine's pages and Chromium's, or null: the same pages of the same size,
    /// the same words on each page in the same order, each word's start and baseline within <see cref="Bound"/>.</summary>
    public static string? Difference(PrintedPages engine, PrintedPages chromium)
    {
        if (engine.Pages.Length != chromium.Pages.Length)
        {
            return $"{engine.Pages.Length} pages, Chromium {chromium.Pages.Length}";
        }

        foreach (var (ours, theirs) in engine.Pages.Zip(chromium.Pages))
        {
            if (Math.Abs(ours.Width - theirs.Width) > 0.001 || Math.Abs(ours.Height - theirs.Height) > 0.001)
            {
                return $"page {ours.Number} is {ours.Width}x{ours.Height}, Chromium {theirs.Width}x{theirs.Height}";
            }

            var words = Joined(ours.Words);
            var text = string.Join(" ", words.Select(word => word.Text));
            if (text != theirs.Text)
            {
                return $"page {ours.Number} reads '{text}', Chromium '{theirs.Text}'";
            }

            foreach (var (word, measured) in words.Zip(theirs.Words))
            {
                if (Math.Abs(word.X0 - measured.X0) > Bound || Math.Abs(WholePixel(word.Baseline) - measured.Baseline) > Bound)
                {
                    return $"page {ours.Number} '{word.Text}' at ({word.X0:0.###}, {word.Baseline:0.###}), Chromium ({measured.X0:0.###}, {measured.Baseline:0.###})";
                }
            }
        }

        return null;
    }

    /// <summary>
    /// A baseline as Chromium paints it: at the nearest whole CSS pixel of the page (0.75 pt), which moves ink by at most
    /// half a pixel and never a line (docs/architecture.md, "Against Chromium"; <c>FontMetricRoundingTests</c> compares
    /// the same way).
    /// </summary>
    private static double WholePixel(double baseline) => Math.Round(baseline / 0.75, MidpointRounding.AwayFromZero) * 0.75;

    /// <summary>
    /// The engine's words as the read-back of Chromium's PDF finds them: two runs of glyphs that touch on one baseline,
    /// with no white space between (text on both sides of an inline element's edge), are one word.
    /// </summary>
    private static List<PrintedWord> Joined(IEnumerable<PrintedWord> words)
    {
        var joined = new List<PrintedWord>();
        foreach (var word in words)
        {
            if (joined.Count > 0 && joined[^1] is var last && last.Baseline == word.Baseline && Math.Abs(last.X1 - word.X0) < 1e-6)
            {
                joined[^1] = last with { X1 = word.X1, Text = last.Text + word.Text };
                continue;
            }

            joined.Add(word);
        }

        return joined;
    }
}
