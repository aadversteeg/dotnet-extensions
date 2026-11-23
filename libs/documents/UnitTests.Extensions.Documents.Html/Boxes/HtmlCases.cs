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
    /// The bound covers both and is no wider than 0.1 pt, the widest the reference rules of the family allow. Baselines are
    /// compared where Chromium paints them (<see cref="BaselineDifference"/>) and the edges of rectangles and images after
    /// the snapping rule of <see cref="WholePixel"/>, which the bound does not widen; the x positions of words, font sizes and box heights are compared as they are.
    /// </summary>
    public const double Bound = 0.1;

    /// <summary>
    /// The cases whose difference is a listed limit of layout, pinned by name in the code front-end's tests
    /// (<c>ListedDifferenceTests</c>, <c>PinnedDifferenceTests</c>, <c>PageBreakProgressTests</c>, <c>LineBoxReferenceTests</c> and
    /// <c>FontMetricRoundingTests</c> of
    /// <c>UnitTests.Extensions.Documents.Fluent</c>) or in <see cref="HtmlListedDifferenceTests"/>
    /// (docs/architecture.md, "Known limits"):
    /// the status file says <c>listed</c>, the test name and the first difference, so the case must still differ, and
    /// differ first in the same fact by the same amount.
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
        ["fr-lh-first-baseline"] = "Under_an_explicit_line_height_the_first_baseline_is_within_the_measured_difference_of_Chromium_s",
        ["f-tiny"] = "At_font_sizes_below_half_a_pixel_Chromium_draws_no_text_where_the_engine_does",
        ["f-tiny-038"] = "At_font_sizes_below_half_a_pixel_Chromium_draws_no_text_where_the_engine_does",
        ["h-wide-word-little"] = "Content_wider_than_the_page_area_is_not_shrunk_as_Chromium_shrinks_the_page",
        ["h-wide-word-much"] = "Content_wider_than_the_page_area_is_not_shrunk_as_Chromium_shrinks_the_page",
        ["hw-nowrap"] = "Content_wider_than_the_page_area_is_not_shrunk_as_Chromium_shrinks_the_page",
        ["h-page-a4-zero-right"] = "The_page_area_of_A4_without_margins_reaches_past_the_sheet_where_the_engine_ends_it_at_the_sheet",
        ["h-page-a4-zero-justify"] = "The_page_area_of_A4_without_margins_reaches_past_the_sheet_where_the_engine_ends_it_at_the_sheet",
        ["h-page-a4-zero-lines"] = "The_page_area_of_A4_without_margins_reaches_past_the_sheet_where_the_engine_ends_it_at_the_sheet",
        ["sr-same-font"] = "A_ligature_across_the_border_of_an_inline_element_keeps_every_character_where_Chromium_keeps_only_those_of_the_first_run",
        ["sr-zwnj"] = "A_zero_width_non_joiner_is_not_in_the_text_where_Chromium_keeps_it",
        ["a4-continued-cell-pad-bottom-30-under-header"] = "A_continued_cell_whose_rest_fits_the_page_area_but_not_with_its_bottom_padding_goes_on_without_the_outer_repeated_header",
        ["fr-old-row-taller-by-padding"] = "A_row_taller_than_the_page_only_through_its_cell_padding_starts_on_a_page_that_Chromium_leaves_blank",
        ["fr-header-pad-box-72"] = "The_same_on_a_page_where_Chromium_repeats_the_header_is_printed",
        ["tl-fixed-fill-wide-word"] = "A_fill_column_of_the_fixed_layout_is_never_narrower_than_its_content_where_Chromium_lets_the_content_overflow",
        ["tf-pages"] = "A_footer_group_is_drawn_once_after_the_body_where_Chromium_repeats_it_on_every_page",
        ["pm-family-unused"] = "A_face_that_only_a_margin_box_uses_is_drawn_where_Chromium_prints_its_fallback_face",
        ["a4-image-in-cell"] = "What_follows_the_row_of_an_image_sliced_in_a_table_starts_below_its_last_slice_where_Chromium_draws_it_over_the_slices",
        ["a4-row-image-row"] = "What_follows_the_row_of_an_image_sliced_in_a_table_starts_below_its_last_slice_where_Chromium_draws_it_over_the_slices",
        ["a4-two-image-rows"] = "What_follows_the_row_of_an_image_sliced_in_a_table_starts_below_its_last_slice_where_Chromium_draws_it_over_the_slices",
        ["a4-slice4-cell-row-after"] = "What_follows_the_row_of_an_image_sliced_in_a_table_starts_below_its_last_slice_where_Chromium_draws_it_over_the_slices",
        ["a4-header-image-360-image"] = "A_repeated_header_is_drawn_above_a_slice_where_Chromium_draws_it_over_the_slice",
        ["a4-header-row-image-row"] = "A_repeated_header_is_drawn_above_a_slice_where_Chromium_draws_it_over_the_slice",
        ["a4-header-image-row-image"] = "A_repeated_header_is_drawn_above_a_slice_where_Chromium_draws_it_over_the_slice",
        ["a4-header-two-image-rows"] = "A_repeated_header_is_drawn_above_a_slice_where_Chromium_draws_it_over_the_slice",
        ["a4-image-nested-table"] = "What_follows_the_row_of_an_image_sliced_in_a_table_starts_below_its_last_slice_where_Chromium_draws_it_over_the_slices",
        ["a4-image-rowspan"] = "An_image_in_a_cell_that_spans_two_rows_follows_the_engine_s_rules_for_rows_and_spanning_cells",
        ["a4-line-beside-lines"] = "A_fill_column_of_the_fixed_layout_is_never_narrower_than_its_content_where_Chromium_lets_the_content_overflow",
        ["fr-f4-padding-page-start"] = "At_the_top_of_a_page_such_a_row_is_cut_where_Chromium_leaves_a_blank_page",
        ["fr-old-margin-at-row-cut"] = "A_margin_dropped_at_a_row_cut_does_not_stay_in_the_row_s_height",
        ["ii-hyphen"] = "A_hyphen_after_an_image_keeps_its_letter_as_after_a_space_where_Chromium_breaks_after_it_in_the_HTML_case",
        ["ii-joiners"] = "A_line_breaks_beside_an_image_also_next_to_a_joiner_like_Chromium_whose_text_keeps_the_joiners",
        ["ws-border"] = "Where_an_element_that_wraps_differently_from_its_parent_ends_the_engine_follows_the_character_before_not_Chromium_in_the_HTML_case",
        ["ws-image-border"] = "Beside_an_image_at_the_end_of_an_element_that_wraps_differently_the_engine_follows_the_character_before_not_Chromium_in_the_HTML_case",
        ["ws-soft"] = "A_soft_hyphen_and_a_zero_width_space_break_by_the_white_space_mode_like_Chromium_whose_text_keeps_them",
        ["sk-misc"] = "An_image_a_shown_soft_hyphen_and_ligatures_cut_the_underline_like_Chromium_whose_text_adds_a_hyphen",
        ["bs-solid-one-colour"] = "A_border_of_one_colour_and_unequal_widths_is_four_rectangles_where_Chromium_fills_one_shape_of_the_same_pixels",
    };

    /// <summary>The statuses the status test accepts, as the second field of a line, compared whole: <c>equal</c>,
    /// <c>listed</c> (a listed limit, <see cref="Listed"/>), <c>refused</c> (the first refusal, with its line and column)
    /// and <c>open</c> (a known difference the committed file names; while one is open the branch is not ready for a pull
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
    /// sheet's edge or leaves it out). Any other error, also another code under the same parent, is a failure of the case.</summary>
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
        CssErrorCodes.Unsupported.PageRule.ToString(),
        LayoutErrorCodes.DoesNotFit.ToString(),
        LayoutErrorCodes.MarginBoxOverflow.ToString(),
        LayoutErrorCodes.ColumnsDoNotFit.ToString(),
        LayoutErrorCodes.OverflowAcrossPages.ToString(),
        LayoutErrorCodes.MarkerOutsideSheet.ToString(),
    };

    /// <summary>The cases folder in the source tree.</summary>
    public static string Folder([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", "tools", "reference", "chromium", "cases"));

    /// <summary>The reference cases: <c>cases</c>, their measured files in <c>measured</c> and their status file
    /// <c>html-status.txt</c>, beside the folder.</summary>
    public static CaseFolder Cases { get; } = new(Folder(), Path.Combine(Folder(), "..", "measured"), Path.Combine(Folder(), "..", "html-status.txt"));

    /// <summary>The whole documents (<c>tools/reference/chromium/documents</c>: the timesheets and the invoice), compared as
    /// the cases are: their measured files in the folder's <c>measured</c>, their status file <c>html-status.txt</c> in the
    /// folder.</summary>
    public static CaseFolder Documents { get; } = new(
        Path.Combine(Folder(), "..", "documents"), Path.Combine(Folder(), "..", "documents", "measured"), Path.Combine(Folder(), "..", "documents", "html-status.txt"));

    /// <summary>The committed status file of the cases: one line per case, <c>name status rest</c> (<see cref="Accepted"/>).</summary>
    public static string StatusFile => Cases.StatusFile;

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
    public static string Status(string name, string? committed = null) => Status(Cases, name, committed);

    /// <summary>The status of an HTML file of a folder, by the same rules as a case's (<see cref="Status(string, string?)"/>).</summary>
    /// <param name="folder">The folder of the file, its measured files and its status file.</param>
    /// <param name="name">The file, without its extension.</param>
    /// <param name="committed">The status the folder's committed file gives it (its second field), or null.</param>
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
        var chromium = PrintedPages.Parse(File.ReadAllText(Measured(folder, name) + ".print.txt"));
        var unused = clips.ToList();
        chromium = new([.. chromium.Pages.Select(page => page with { Rectangles = [.. Cut(page.Rectangles, unused)] })]);
        var lines = Cut(Rows(folder, name, "lines").Select(row => new PrintedBox(int.Parse(row[0], CultureInfo.InvariantCulture), Number(row[1]), Number(row[2]), Number(row[3]), Number(row[4]))), clips.ToList());
        var missed = unused.Count > 0 ? $"page {unused[0].Line.Page} the line clipped at y {Of(unused[0].Line.Y0)} is no rectangle of the print (ink.txt)" : null;
        var difference = missed ?? Difference(engine, chromium, LineTops(boxes.Value))
            ?? RectangleDifference(engine, chromium, lines)
            ?? PopplerDifference(pages.Value, engine, Rows(folder, name, "words"), LineTops(boxes.Value))
            ?? BoxDifference(folder, name, boxes.Value, Rows(folder, name, "boxes"));
        return (difference, Listed.TryGetValue(name, out var listed)) switch
        {
            (null, false) => "equal",
            (null, true) => "different equal to Chromium, but listed as a difference",
            ({ } found, true) => $"listed {listed}: {found}",
            ({ } found, false) => committed == "open" ? $"open {found}" : $"different {found}",
        };
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

    /// <summary>
    /// The status of a case whose path ended in an error: <c>refused code what at position</c> where the first part is a
    /// refusal (<see cref="Refusals"/>) that says where in the source it is, otherwise <c>failed</c>. The position of the
    /// front-end's refusals is a line and a column; layout's is the line of the box, the only place a box keeps
    /// (<see cref="SourceLocation"/>).
    /// </summary>
    private static string Refusal(Error error)
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
            ({ }, null) when first.Code == LayoutErrorCodes.DoesNotFit || first.Code == LayoutErrorCodes.ColumnsDoNotFit || first.Code == LayoutErrorCodes.OverflowAcrossPages || first.Code == LayoutErrorCodes.MarkerOutsideSheet => $"refused {first.Code} {what} at {line}",
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
    /// filled or stroked (<see cref="PaintedDifference"/>); the same image areas in drawing order, each edge after
    /// <see cref="WholePixel"/> within <see cref="Bound"/>; the same areas painted by paths that are not rectangles
    /// (<see cref="PathDifference"/>).
    /// </summary>
    public static string? Difference(PrintedPages engine, PrintedPages chromium, IReadOnlyDictionary<(int Page, double Baseline), double>? lineTops = null)
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
                    ?? BaselineDifference(at, word.Baseline, measured.Baseline, LineTop(lineTops, ours.Number, word.Baseline)) ?? Far(at, "font size", word.FontSize, measured.FontSize)
                    ?? Same(at, "colour", word.Colour, measured.Colour) ?? Same(at, "face", word.Face, measured.Face);
                if (found is not null)
                {
                    return found + " (print.txt)";
                }
            }

            if ((PaintedDifference(ours, ours.Rectangles, theirs.Rectangles) ?? Areas($"page {ours.Number} image", ours.Images, theirs.Images) ?? PathDifference(ours, theirs)) is { } area)
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
    private static string? RectangleDifference(PrintedPages engine, PrintedPages chromium, List<PrintedBox> measured)
    {
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

    /// <summary>The first difference between the areas the paths that are not rectangles (curves: a list marker's disc or
    /// circle) paint on a page (<see cref="PrintedPath"/>): the same number, and each in drawing order the same box of
    /// whole pixels, colour and painted share per pixel. Both sides are sampled by one rule: the engine's filled paths by
    /// <see cref="PathSampler"/>, Chromium's curves by <c>paths.py</c>.</summary>
    private static string? PathDifference(PrintedPage ours, PrintedPage theirs)
    {
        if (ours.Paths.Length != theirs.Paths.Length)
        {
            return $"page {ours.Number} has {ours.Paths.Length} painted paths, Chromium {theirs.Paths.Length}";
        }

        return ours.Paths.Zip(theirs.Paths).Where(pair => !pair.First.Paints(pair.Second)).Select(pair =>
            $"page {ours.Number} path at ({Of(pair.First.X0)}, {Of(pair.First.Y0)}) paints {string.Join('/', pair.First.Rows)} {pair.First.Colour}, " +
            $"Chromium at ({Of(pair.Second.X0)}, {Of(pair.Second.Y0)}) {string.Join('/', pair.Second.Rows)} {pair.Second.Colour}").FirstOrDefault();
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
        var columns = ChromiumLayout.Pixel(page.Width);
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
    private static string? PopplerDifference(PageDocument document, PrintedPages engine, List<string[]> rows, IReadOnlyDictionary<(int Page, double Baseline), double> lineTops)
    {
        foreach (var page in engine.Pages)
        {
            var inside = page.JoinedWords.Select(joined => joined.Word).Where(word => WholePixel(word.Baseline) <= page.Height && WholePixel(word.Baseline) - word.FontSize >= 0).ToList();
            var poppler = rows.Where(row => int.Parse(row[0], CultureInfo.InvariantCulture) == page.Number).ToList();
            if (inside.Count != poppler.Count)
            {
                return $"page {page.Number} has {inside.Count} words inside the page, Chromium {poppler.Count} (words.txt)";
            }

            foreach (var row in poppler)
            {
                var text = System.Net.WebUtility.HtmlDecode(row[5]);
                var word = inside.Where(word => word.Text == text).OrderBy(word => Math.Abs(word.X0 - Number(row[1])) + Math.Abs(WholePixel(word.Baseline) - Number(row[4]))).FirstOrDefault();
                if (word is null)
                {
                    return $"page {page.Number} has no word '{text}' (words.txt)";
                }

                inside.Remove(word);
                var at = $"page {page.Number} '{text}'";
                var metrics = Face(document.Pages[page.Number - 1], word);
                var baseline = PaintedBaseline(word.Baseline, LineTop(lineTops, page.Number, word.Baseline));
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
    private static string? BoxDifference(CaseFolder folder, string name, HtmlBoxes boxes, List<string[]> rows) =>
        BoxDifference(Parsed(folder, name).Value.Document, boxes, rows);

    /// <summary>The first difference between the boxes of a case of a folder and the given rows of a <c>boxes.txt</c>
    /// (<see cref="BoxDifference(CaseFolder, string, HtmlBoxes, List{string[]})"/>), or null; the refusal where the case
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
            .OnSuccessBind(styled => HtmlBoxGenerator.Generate(styled, new HtmlBoxOptions { Resources = Resource, BaseUrl = baseUrl }));
        return boxes.IsFailure
            ? Refusal(boxes.Error)
            : BoxDifference(parsed.Value.Document, boxes.Value, [.. rows.Where(row => row.Length > 0).Select(row => row.Split(' '))]);
    }

    /// <summary>The measured <c>boxes.txt</c> of a case of a folder, line by line.</summary>
    public static IReadOnlyList<string> BoxRows(CaseFolder folder, string name) => File.ReadAllLines(Measured(folder, name) + ".boxes.txt");

    /// <summary>The first difference between the boxes of a document and the rows of its <c>boxes.txt</c>, by the rules of
    /// <see cref="BoxDifference(CaseFolder, string, HtmlBoxes, List{string[]})"/>, or null.</summary>
    /// <param name="document">The parsed document.</param>
    /// <param name="boxes">Its boxes.</param>
    /// <param name="rows">The rows of the file, split at spaces.</param>
    /// <returns>The difference, ending in <c>(boxes.txt)</c>, or null.</returns>
    public static string? BoxDifference(HtmlDocument document, HtmlBoxes boxes, IReadOnlyList<string[]> rows)
    {
        if (rows.Count == 0)
        {
            return null;
        }

        var elements = Elements(document).ToList();
        var environment = LayoutEnvironment.Create(boxes.Fonts);
        var pages = Paginator.Paginate(environment, boxes.Document);
        if (pages.IsFailure)
        {
            return $"the pages for the boxes failed: {pages.Error.Code} (boxes.txt)";
        }

        var cells = elements.Where(IsCell).ToList();
        var cellRows = rows.Count(row => row[0] is "td" or "th");
        if (cellRows != cells.Count)
        {
            return $"{cellRows} cells, the document {cells.Count} (boxes.txt)";
        }

        var unbroken = new Lazy<Result<ValueList<PageFragment>, Error>>(() => Paginator.Paginate(environment, Unbroken(boxes.Document)));
        var cellIndex = 0;
        foreach (var row in rows)
        {
            var label = row.Length > 3 ? string.Join(' ', row[3..]) : string.Empty;
            var (box, missing) = row[0] is "td" or "th" ? CellBox(boxes.Document.Root, cells, cells[cellIndex++], row, label) : IdBox(boxes.Document.Root, elements, row, label);
            if ((missing ?? SizeDifference(box!, pages.Value, unbroken, row, label)) is { } different)
            {
                return different + " (boxes.txt)";
            }
        }

        return null;
    }

    private static bool IsCell(HtmlElement element) => element.Namespace == ElementNamespace.Html && element.LocalName is "td" or "th";

    /// <summary>The cell box of one cell row of <c>boxes.txt</c> (<see cref="BoxDifference(CaseFolder, string, HtmlBoxes, List{string[]})"/>):
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
        var box = Boxes(root).Where(box => box.Source.Line == line && (box.Role == SemanticRole.TableCell || box.Role == SemanticRole.TableHeaderCell)).ElementAtOrDefault(place);
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
        var candidates = Boxes(root).Where(box => box.Source.Line == line && box.Role == role).ToList();
        return candidates.Count == peers.Count
            ? (candidates[peers.IndexOf(element)], null)
            : (null, $"{at} is not found: line {line} has {candidates.Count} boxes of role {role.GetType().Name} for {peers.Count} elements of that role");
    }

    /// <summary>An element the HTML front-end never makes a box for: a column, a column group, a line break.</summary>
    private static bool MakesNoBox(HtmlElement element) => element.Namespace == ElementNamespace.Html && element.LocalName is "col" or "colgroup" or "br";

    /// <summary>The role the HTML front-end gives the box of an element by its tag, for <see cref="IdBox"/>: a table, a
    /// table cell, a paragraph, a heading, a row group or a row; any other element a generic box.</summary>
    private static SemanticRole RoleOf(HtmlElement element) => element.Namespace != ElementNamespace.Html ? SemanticRole.Generic : element.LocalName switch
    {
        "table" => SemanticRole.Table,
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
    /// The size difference of one box, its height and then its width (<see cref="BoxDifference(CaseFolder, string, HtmlBoxes, List{string[]})"/>): its
    /// parts on the pages, a copy in a repeated header row not counted as a part. One part: that part and every copy.
    /// More than one: the box in the layout without breaks (<see cref="Unbroken"/>), which must be one page, and every
    /// copy. Else the difference.
    /// </summary>
    internal static string? SizeDifference(Box box, IReadOnlyList<PageFragment> pages, Lazy<Result<ValueList<PageFragment>, Error>> unbroken, string[] row, string label)
    {
        var at = $"box {row[0]} '{label}'";
        var found = pages.Select(page => Part(page.Root.Fragment, box.Id)).Where(part => part is not null).Select(part => part!.Value).ToList();
        if (found.Count == 0)
        {
            return $"{at} has no box on any page";
        }

        var copies = found.Where(part => part.Repeated).Select(part => part.Fragment).ToList();
        var parts = found.Where(part => !part.Repeated).Select(part => part.Fragment).ToList();
        if (parts.Count <= 1)
        {
            return parts.Concat(copies).Select(fragment => Sized(at, string.Empty, fragment, row)).FirstOrDefault(difference => difference is not null);
        }

        if (unbroken.Value.IsFailure)
        {
            return $"{at}: the layout without breaks failed: {unbroken.Value.Error.Code}";
        }

        if (unbroken.Value.Value.Count != 1)
        {
            return $"{at}: the layout without breaks has {unbroken.Value.Value.Count} pages";
        }

        if (Part(unbroken.Value.Value[0].Root.Fragment, box.Id) is not { } whole)
        {
            return $"{at} has no box in the layout without breaks";
        }

        return Sized(at, $" without breaks (in {parts.Count} parts)", whole.Fragment, row)
            ?? copies.Select(fragment => Sized(at, string.Empty, fragment, row)).FirstOrDefault(difference => difference is not null);
    }

    /// <summary>The height of a fragment against the row's height, then its width against the row's width (<c>tag width
    /// height label</c>), each within <see cref="Bound"/>; or null.</summary>
    private static string? Sized(string at, string where, BoxFragment fragment, string[] row) =>
        Far(at, "height" + where, fragment.Size.Height, Number(row[2])) ?? Far(at, "width" + where, fragment.Size.Width, Number(row[1]));

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
    /// (<see cref="PrintedPage.WordGap"/>: text on both sides of an inline element's edge, kerned or not), are one word;
    /// PyMuPDF starts a new span, and <c>positions.py</c> a new word, where the font, the size or the colour changes.
    /// Poppler's words (<c>words.txt</c>) join across a change of face or colour too: <see cref="PrintedPage.JoinedWords"/>.
    /// </summary>
    private static List<PrintedWord> Joined(IEnumerable<PrintedWord> words)
    {
        var joined = new List<PrintedWord>();
        foreach (var word in words)
        {
            if (joined.Count > 0 && joined[^1] is var last && last.Baseline == word.Baseline && word.X0 - last.X1 < PrintedPage.WordGap
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

/// <summary>A folder of HTML files compared with Chromium's prints of them (<see cref="HtmlCases.Status(CaseFolder, string, string?)"/>):
/// the HTML files, the folder of their measured files and the committed status file, one line per file.</summary>
/// <param name="Html">The folder of the HTML files.</param>
/// <param name="Measured">The folder of the files measured for each (<c>&lt;name&gt;.print.txt</c> and the others).</param>
/// <param name="StatusFile">The committed status file.</param>
internal sealed record CaseFolder(string Html, string Measured, string StatusFile)
{
    /// <summary>Every HTML file of the folder, by name without its extension, in ordinal order.</summary>
    public IReadOnlyList<string> Names =>
        [.. Directory.GetFiles(Html, "*.html").Select(Path.GetFileNameWithoutExtension).Order(StringComparer.Ordinal)!];
}
