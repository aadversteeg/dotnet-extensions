using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Extensions.Documents.PageModel;

/// <summary>
/// The contract a page model must meet before a back-end draws it, whoever produced it. The back-ends (PDF, raster)
/// are public libraries and other producers than this engine's layout may hand them a page model, so each checks
/// everything it is given with these rules and answers with an error value: no input value may make it throw, or
/// allocate without a bound, or write a file no reader can open. One rule set for every back-end, so a page model one
/// back-end accepts the other accepts too; each back-end reports a breach under its own error code (the caller says
/// which), because the refusal is that back-end's. What a PDF profile requires on top is checked by the PDF back-end.
/// </summary>
public static class PageModelRules
{
    /// <summary>
    /// Checks the page model and reports every breach together (<c>Documents.Pdf.Write.InvalidPageModel</c>):
    /// <list type="bullet">
    /// <item>metadata and at least one page: a PDF without a page has nothing to show and is not useful to write; every
    /// page of positive, finite width and height;</item>
    /// <item>no missing page, item, glyph, font, image, role or attachment;</item>
    /// <item>every glyph is a glyph of its font (below maxp.numGlyphs), since the font program is embedded from those
    /// glyphs, and has a text, which its ToUnicode entry needs (the empty text for a glyph that continues the text of
    /// the one before it, as the later glyphs of a one-to-many substitution do); every font size is a positive finite
    /// number;</item>
    /// <item>every position and size of a glyph run, a filled rectangle and an image is a finite number;</item>
    /// <item>every artifact kind and attachment relationship is a value of its enum;</item>
    /// <item>a table cell spans at least one column and one row, and no more than the table model allows
    /// (<see cref="TableSlots.MaxColumnSpan"/>, <see cref="TableSlots.MaxRowSpan"/>), so no grid is ever sized by one
    /// number;</item>
    /// <item>the structure nests as tagged PDF's table elements do (<see cref="StructureNesting"/>), and one node keeps
    /// one role on every page;</item>
    /// <item>every attachment has a file name, a media type and content, and no two share a file name: the embedded-files
    /// name tree has one entry per name.</item>
    /// </list>
    /// </summary>
    /// <param name="document">The page document.</param>
    /// <param name="code">The error code the back-end reports a breach with.</param>
    /// <returns>The document unchanged, or the breaches.</returns>
    public static Result<PageDocument, Error> Check(PageDocument document, ErrorCode code)
    {
        var problems = Problems(document, code).ToList();
        return problems.Count == 0
            ? Result<PageDocument, Error>.Success(document)
            : Result<PageDocument, Error>.Failure(Errors.Aggregate(problems));
    }

    /// <summary>
    /// Checks one page on its own, by the rules that concern one page (all of <see cref="Check(PageDocument, ErrorCode)"/>
    /// except those across pages: one role per node, the parts of a table, the attachments), for a back-end that draws a
    /// single page. Messages call it page 1.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <param name="code">The error code the back-end reports a breach with.</param>
    /// <returns>The page unchanged, or the breaches.</returns>
    public static Result<Page, Error> Check(Page page, ErrorCode code)
    {
        var problems = page is null
            ? [Invalid(code, "There is no page.", "null")]
            : SizeProblems(page, 1, code).Concat(ItemProblems(page, 1, code)).Concat(StructureNesting.Problems(page, 1, code)).ToList();
        return problems.Count == 0
            ? Result<Page, Error>.Success(page!)
            : Result<Page, Error>.Failure(Errors.Aggregate(problems));
    }

    private static IEnumerable<Error> Problems(PageDocument? document, ErrorCode code)
    {
        if (document is null)
        {
            yield return Invalid(code, "There is no page document.", "null");
            yield break;
        }

        if (document.Metadata is null)
        {
            yield return Invalid(code, "The page document has no metadata.", "metadata");
        }

        if (document.Pages.Count == 0)
        {
            yield return Invalid(code, "The document has no pages; a back-end needs at least one to draw.", "0 pages");
        }

        var roles = new Dictionary<NodeId, SemanticRole>();
        for (var index = 0; index < document.Pages.Count; index++)
        {
            var page = document.Pages[index];
            var problems = page is null
                ? [Invalid(code, $"Page {index + 1} is missing.", Number(index + 1))]
                : SizeProblems(page, index + 1, code).Concat(ItemProblems(page, index + 1, code)).Concat(StructureNesting.Problems(page, index + 1, code)).Concat(RoleProblems(page, index + 1, roles, code));
            foreach (var problem in problems)
            {
                yield return problem;
            }
        }

        foreach (var problem in StructureNesting.TableParts(document.Pages, code))
        {
            yield return problem;
        }

        foreach (var problem in AttachmentProblems(document.Metadata?.Attachments ?? [], code))
        {
            yield return problem;
        }
    }

    /// <summary>The smallest page side: 0.0001 pt, the smallest length the page model's text form and the PDF writer
    /// keep as more than 0 (they write 4 decimals); a smaller side would be a page without area there. A PDF/A profile
    /// requires 3 units.</summary>
    public const double MinPageSide = 0.0001;

    /// <summary>A page is a rectangle of finite width and height, each at least <see cref="MinPageSide"/> (in PDF its
    /// /MediaBox, ISO 32000-1 §14.11.2). A page of no area shows nothing.</summary>
    private static IEnumerable<Error> SizeProblems(Page page, int number, ErrorCode code) =>
        page.Size is { Width: >= MinPageSide and < double.PositiveInfinity, Height: >= MinPageSide and < double.PositiveInfinity }
            ? []
            : [Invalid(code, $"Page {number} is {Number(page.Size.Width)} x {Number(page.Size.Height)}; a page has a finite width and height of at least {Number(MinPageSide)}.", $"{Number(page.Size.Width)}x{Number(page.Size.Height)}")];

    private static string Number(double value) => value.ToString(CultureInfo.InvariantCulture);

    private static IEnumerable<Error> ItemProblems(Page page, int number, ErrorCode code) =>
        page.Items.Select((item, index) => ItemProblem(item, index, number, code)).OfType<Error>();

    private static Error? ItemProblem(DrawItem? item, int index, int number, ErrorCode code) => item switch
    {
        null => Invalid(code, $"Item {index + 1} of page {number} is missing.", Number(number)),
        GlyphRun { Font: null } => Invalid(code, $"A glyph run on page {number} has no font.", Number(number)),
        GlyphRun run when !(double.IsFinite(run.FontSize) && run.FontSize > 0) =>
            Invalid(code, $"A glyph run on page {number} has the font size {run.FontSize}; it must be a positive number.", run.FontSize.ToString(CultureInfo.InvariantCulture)),
        GlyphRun run when run.Glyphs.Any(glyph => glyph is null) => Invalid(code, $"A glyph run on page {number} has a missing glyph.", Number(number)),
        GlyphRun run when run.Glyphs.FirstOrDefault(glyph => glyph.Glyph.Value >= run.Font.GlyphCount) is { } glyph =>
            Invalid(code, $"Page {number} draws glyph {glyph.Glyph.Value} of font '{run.Font.Names.PostScriptName}', which has {run.Font.GlyphCount} glyphs.", glyph.Glyph.Value.ToString(CultureInfo.InvariantCulture)),
        GlyphRun run when run.Glyphs.Any(glyph => glyph.Text is null) =>
            Invalid(code, $"A glyph on page {number} has no text; every shown glyph needs one for its ToUnicode entry (empty for a glyph that continues the text of the glyph before it).", Number(number)),
        DrawImage { Image: null } => Invalid(code, $"An image on page {number} has no image data.", Number(number)),
        StructureStart { Role: null } start => Invalid(code, $"Node {start.Node} on page {number} has no role.", start.Node.ToString()).WithSource(start.Source),
        StructureStart start when !TableSlots.IsValidSpan(start.Span) =>
            Invalid(code, $"Node {start.Node} on page {number} spans {start.Span}; a cell spans 1 to {TableSlots.MaxColumnSpan} columns and 1 to {TableSlots.MaxRowSpan} rows.", start.Span.ToString()).WithSource(start.Source),
        GlyphRun run when !IsFinite(run.Origin) || run.Glyphs.Any(glyph => !double.IsFinite(glyph.X) || !double.IsFinite(glyph.Y)) =>
            Invalid(code, $"A glyph run on page {number} has a position that is not a finite number.", $"{run.Origin}"),
        FillRectangle fill when !IsFinite(fill.Area) => Invalid(code, $"A filled rectangle on page {number} has a position or size that is not a finite number.", $"{fill.Area}"),
        DrawImage image when !IsFinite(image.Area) => Invalid(code, $"An image on page {number} has a position or size that is not a finite number.", $"{image.Area}"),
        ArtifactStart artifact when !Enum.IsDefined(artifact.Kind) =>
            Invalid(code, $"An artifact on page {number} has the kind {(int)artifact.Kind}, which is not an artifact kind.", ((int)artifact.Kind).ToString(CultureInfo.InvariantCulture)),
        _ => null,
    };

    private static bool IsFinite(Point point) => double.IsFinite(point.X) && double.IsFinite(point.Y);

    private static bool IsFinite(Rect area) => IsFinite(area.Origin) && double.IsFinite(area.Size.Width) && double.IsFinite(area.Size.Height);

    /// <summary>A node is the same node on every page: the structure tree has one element for it, with one type.</summary>
    private static IEnumerable<Error> RoleProblems(Page page, int number, Dictionary<NodeId, SemanticRole> roles, ErrorCode code)
    {
        foreach (var start in page.Items.OfType<StructureStart>().Where(start => start.Role is not null))
        {
            if (roles.TryGetValue(start.Node, out var role) && role != start.Role)
            {
                yield return Invalid(code, $"Node {start.Node} has the role {role} and, on page {number}, {start.Role}; a node has one role.", start.Node.ToString()).WithSource(start.Source);
            }

            roles.TryAdd(start.Node, start.Role);
        }
    }

    private static IEnumerable<Error> AttachmentProblems(ValueList<Attachment> attachments, ErrorCode code)
    {
        foreach (var attachment in attachments)
        {
            if (attachment is null)
            {
                yield return Invalid(code, "An attachment is missing.", "attachment");
                continue;
            }

            if (string.IsNullOrWhiteSpace(attachment.FileName))
            {
                yield return Invalid(code, "An attachment has no file name; the file specification needs one (veraPDF rule 7.11-1: \"non-empty F and UF keys\").", attachment.FileName ?? "null").WithSource(attachment.Source);
            }

            if (string.IsNullOrWhiteSpace(attachment.MediaType))
            {
                yield return Invalid(code, $"Attachment '{attachment.FileName}' has no media type.", attachment.MediaType ?? "null").WithSource(attachment.Source);
            }

            if (attachment.Relationship is { } relationship && !Enum.IsDefined(relationship))
            {
                yield return Invalid(code, $"Attachment '{attachment.FileName}' has the relationship {(int)relationship}, which is not a relationship.", ((int)relationship).ToString(CultureInfo.InvariantCulture)).WithSource(attachment.Source);
            }
        }

        foreach (var duplicate in attachments.Where(attachment => attachment?.FileName is not null).GroupBy(attachment => attachment.FileName).Where(group => group.Count() > 1))
        {
            yield return Invalid(code, $"Two attachments have the file name '{duplicate.Key}'; a name can be embedded once.", duplicate.Key)
                .WithSource(duplicate.Last().Source);
        }
    }

    private static string Number(int number) => number.ToString(CultureInfo.InvariantCulture);

    /// <summary>A breach of the page-model contract.</summary>
    /// <param name="message">What is wrong, in words about the page model.</param>
    /// <param name="value">The offending value.</param>
    /// <param name="code">The back-end's error code.</param>
    /// <returns>The error.</returns>
    internal static Error Invalid(ErrorCode code, string message, string value) =>
        new Error(code, message).With(ErrorMetadataKeys.Value, value);
}

/// <summary>
/// How tagged PDF's table structure nests (ISO 32000-1 §14.8.4.3.4, table elements, Table 337): a row (TR) is a child of a table or
/// of one of its row groups; a header or data cell (TH, TD) is a child of a row; a row group is a child of a table.
/// Seen from the parent, a table holds only rows and row groups, a row group only rows, a row only cells. veraPDF
/// checks the same for PDF/UA-1 (rules 7.2-3 to 7.2-10, 7.2-36 to 7.2-38). Parent here means the nearest enclosing node
/// that has a structure type; a decorative node (an artifact) is not one. The row groups are THead and TBody: there is no
/// role for a table footer (TFoot, Core SemanticRole), so the rules have no case for it.
/// </summary>
internal static class StructureNesting
{
    private static readonly ImmutableHashSet<string> RowParents = ["Table", "THead", "TBody"];

    private static readonly ImmutableDictionary<string, ImmutableHashSet<string>> AllowedParents = new Dictionary<string, ImmutableHashSet<string>>
    {
        ["TR"] = RowParents,
        ["TH"] = ["TR"],
        ["TD"] = ["TR"],
        ["THead"] = ["Table"],
        ["TBody"] = ["Table"],
    }.ToImmutableDictionary();

    private static readonly ImmutableDictionary<string, ImmutableHashSet<string>> AllowedChildren = new Dictionary<string, ImmutableHashSet<string>>
    {
        ["Table"] = ["TR", "THead", "TBody"],
        ["THead"] = ["TR"],
        ["TBody"] = ["TR"],
        ["TR"] = ["TH", "TD"],
    }.ToImmutableDictionary();

    /// <summary>Every structure start on the page whose parent or own type breaks the table rules.</summary>
    /// <param name="page">The page.</param>
    /// <param name="number">Its page number.</param>
    /// <param name="code">The back-end's error code.</param>
    /// <returns>The breaches.</returns>
    public static IEnumerable<Error> Problems(Page page, int number, ErrorCode code)
    {
        var open = new Stack<string?>();
        foreach (var item in page.Items)
        {
            if (item is StructureStart start)
            {
                var type = start.Role?.StandardStructureType;
                var parent = open.FirstOrDefault(candidate => candidate is not null);
                if (type is not null && Breach(type, parent) is { } reason)
                {
                    yield return PageModelRules.Invalid(code, $"Node {start.Node} on page {number} is a {type} {reason}.", type).WithSource(start.Source);
                }

                open.Push(type);
            }
            else if (item is StructureEnd && open.Count > 0)
            {
                open.Pop();
            }
        }
    }

    /// <summary>
    /// The kids of every table, counted over all pages, where a node split over pages is one element. ISO 32000-1
    /// Table 337 says a table "contains either one or more table rows (structure type TR) as children; or an optional
    /// table head (structure type THead) followed by one or more table body elements (structure type TBody) and an
    /// optional table footer (structure type TFoot)". (It may also have a caption as its first or last child; there is
    /// no caption role, and no footer role either.) So a table holds rows or row groups, not both, and at most one THead,
    /// which comes first. veraPDF checks the count for PDF/UA-1 (rule 7.2-11). Whether a head needs a body is a profile's
    /// rule (PDF/UA-1, rule 7.2-14). Linear in the number of structure markers, whatever their shape.
    /// </summary>
    /// <param name="pages">The pages; missing pages are reported elsewhere and skipped here.</param>
    /// <param name="code">The back-end's error code.</param>
    /// <returns>One breach per table that has one.</returns>
    public static IEnumerable<Error> TableParts(IEnumerable<Page?> pages, ErrorCode code)
    {
        var kids = new Dictionary<NodeId, List<(string Type, SourceLocation Source)>>();
        var seen = new HashSet<(NodeId Table, NodeId Kid)>();
        foreach (var page in pages.OfType<Page>())
        {
            var open = new Stack<(NodeId Node, string? Type)>();
            foreach (var item in page.Items)
            {
                if (item is StructureStart start)
                {
                    var type = start.Role?.StandardStructureType;
                    if (type is "TR" or "THead" or "TBody"
                        && open.FirstOrDefault(entry => entry.Type is not null) is { Type: "Table" } table
                        && seen.Add((table.Node, start.Node)))
                    {
                        (kids.TryGetValue(table.Node, out var list) ? list : kids[table.Node] = []).Add((type, start.Source));
                    }

                    open.Push((start.Node, type));
                }
                else if (item is StructureEnd && open.Count > 0)
                {
                    open.Pop();
                }
            }
        }

        return kids.Select(table => PartsBreach(table.Key, table.Value, code)).OfType<Error>();
    }

    /// <summary>The first way a table's kids, in order, break the rule of <see cref="TableParts"/>, or null.</summary>
    private static Error? PartsBreach(NodeId table, List<(string Type, SourceLocation Source)> kids, ErrorCode code)
    {
        var types = kids.Select(kid => kid.Type).ToList();
        var reason =
            types.Contains("TR") && types.Exists(type => type != "TR") ? "holds rows beside row groups; it holds one or the other"
            : types.Count(type => type == "THead") > 1 ? "has more than one THead; it has at most one"
            : types.IndexOf("THead") > 0 ? "has its THead after another row group; the head comes first"
            : null;
        return reason is null
            ? null
            : PageModelRules.Invalid(code, $"Table node {table} {reason} (ISO 32000-1 Table 337).", string.Join(",", types)).WithSource(kids[0].Source);
    }

    private static string? Breach(string type, string? parent) =>
        AllowedParents.TryGetValue(type, out var parents) && (parent is null || !parents.Contains(parent))
            ? $"inside {parent ?? "nothing"}; it belongs in {string.Join(" or ", parents.Order(StringComparer.Ordinal))}"
            : parent is not null && AllowedChildren.TryGetValue(parent, out var children) && !children.Contains(type)
                ? $"inside a {parent}, which holds only {string.Join(", ", children.Order(StringComparer.Ordinal))}"
                : null;
}
