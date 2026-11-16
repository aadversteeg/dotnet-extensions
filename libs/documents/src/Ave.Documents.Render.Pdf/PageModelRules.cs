using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Ave.Documents.Core;
using Ave.Documents.PageModel;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Documents.Render.Pdf;

/// <summary>
/// The contract a page model must meet before the back-end writes it, whatever the profile. The back-end is a public
/// library and other producers than this engine's layout may hand it a page model, so it checks everything it is
/// given and answers with an error value: no input value may make it throw, or allocate without a bound, or write a
/// file no reader can open. What a profile requires on top is checked by <see cref="ConformanceRules"/>.
/// </summary>
internal static class PageModelRules
{
    /// <summary>
    /// Checks the page model and reports every breach together (<c>Documents.Pdf.Write.InvalidPageModel</c>):
    /// <list type="bullet">
    /// <item>metadata and at least one page: a PDF without a page has nothing to show and is not useful to write;</item>
    /// <item>no missing page, item, glyph, font, image, role or attachment;</item>
    /// <item>every glyph is a glyph of its font (below maxp.numGlyphs), since the font program is embedded from those
    /// glyphs, and has a text, which its ToUnicode entry needs (the empty text for a glyph that continues the text of
    /// the one before it, as the later glyphs of a one-to-many substitution do); every font size is a positive finite
    /// number;</item>
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
    /// <returns>The document unchanged, or the breaches.</returns>
    public static Result<PageDocument, Error> Check(PageDocument document)
    {
        var problems = Problems(document).ToList();
        return problems.Count == 0
            ? Result<PageDocument, Error>.Success(document)
            : Result<PageDocument, Error>.Failure(Errors.Aggregate(problems));
    }

    private static IEnumerable<Error> Problems(PageDocument? document)
    {
        if (document is null)
        {
            yield return Invalid("There is no page document.", "null");
            yield break;
        }

        if (document.Metadata is null)
        {
            yield return Invalid("The page document has no metadata.", "metadata");
        }

        if (document.Pages.Count == 0)
        {
            yield return Invalid("The document has no pages; a PDF needs at least one.", "0 pages");
        }

        var roles = new Dictionary<NodeId, SemanticRole>();
        for (var index = 0; index < document.Pages.Count; index++)
        {
            var page = document.Pages[index];
            var problems = page is null
                ? [Invalid($"Page {index + 1} is missing.", Number(index + 1))]
                : ItemProblems(page, index + 1).Concat(StructureNesting.Problems(page, index + 1)).Concat(RoleProblems(page, index + 1, roles));
            foreach (var problem in problems)
            {
                yield return problem;
            }
        }

        foreach (var problem in StructureNesting.TableParts(document.Pages))
        {
            yield return problem;
        }

        foreach (var problem in AttachmentProblems(document.Metadata?.Attachments ?? []))
        {
            yield return problem;
        }
    }

    private static IEnumerable<Error> ItemProblems(Page page, int number) =>
        page.Items.Select((item, index) => ItemProblem(item, index, number)).OfType<Error>();

    private static Error? ItemProblem(DrawItem? item, int index, int number) => item switch
    {
        null => Invalid($"Item {index + 1} of page {number} is missing.", Number(number)),
        GlyphRun { Font: null } => Invalid($"A glyph run on page {number} has no font.", Number(number)),
        GlyphRun run when !(double.IsFinite(run.FontSize) && run.FontSize > 0) =>
            Invalid($"A glyph run on page {number} has the font size {run.FontSize}; it must be a positive number.", run.FontSize.ToString(CultureInfo.InvariantCulture)),
        GlyphRun run when run.Glyphs.Any(glyph => glyph is null) => Invalid($"A glyph run on page {number} has a missing glyph.", Number(number)),
        GlyphRun run when run.Glyphs.FirstOrDefault(glyph => glyph.Glyph.Value >= run.Font.GlyphCount) is { } glyph =>
            Invalid($"Page {number} draws glyph {glyph.Glyph.Value} of font '{run.Font.Names.PostScriptName}', which has {run.Font.GlyphCount} glyphs.", glyph.Glyph.Value.ToString(CultureInfo.InvariantCulture)),
        GlyphRun run when run.Glyphs.Any(glyph => glyph.Text is null) =>
            Invalid($"A glyph on page {number} has no text; every shown glyph needs one for its ToUnicode entry (empty for a glyph that continues the text of the glyph before it).", Number(number)),
        DrawImage { Image: null } => Invalid($"An image on page {number} has no image data.", Number(number)),
        StructureStart { Role: null } start => Invalid($"Node {start.Node} on page {number} has no role.", start.Node.ToString()).WithSource(start.Source),
        StructureStart start when !TableSlots.IsValidSpan(start.Span) =>
            Invalid($"Node {start.Node} on page {number} spans {start.Span}; a cell spans 1 to {TableSlots.MaxColumnSpan} columns and 1 to {TableSlots.MaxRowSpan} rows.", start.Span.ToString()).WithSource(start.Source),
        ArtifactStart artifact when !Enum.IsDefined(artifact.Kind) =>
            Invalid($"An artifact on page {number} has the kind {(int)artifact.Kind}, which is not an artifact kind.", ((int)artifact.Kind).ToString(CultureInfo.InvariantCulture)),
        _ => null,
    };

    /// <summary>A node is the same node on every page: the structure tree has one element for it, with one type.</summary>
    private static IEnumerable<Error> RoleProblems(Page page, int number, Dictionary<NodeId, SemanticRole> roles)
    {
        foreach (var start in page.Items.OfType<StructureStart>().Where(start => start.Role is not null))
        {
            if (roles.TryGetValue(start.Node, out var role) && role != start.Role)
            {
                yield return Invalid($"Node {start.Node} has the role {role} and, on page {number}, {start.Role}; a node has one role.", start.Node.ToString()).WithSource(start.Source);
            }

            roles.TryAdd(start.Node, start.Role);
        }
    }

    private static IEnumerable<Error> AttachmentProblems(ValueList<Attachment> attachments)
    {
        foreach (var attachment in attachments)
        {
            if (attachment is null)
            {
                yield return Invalid("An attachment is missing.", "attachment");
                continue;
            }

            if (string.IsNullOrWhiteSpace(attachment.FileName))
            {
                yield return Invalid("An attachment has no file name; the file specification needs one (veraPDF rule 7.11-1: \"non-empty F and UF keys\").", attachment.FileName ?? "null").WithSource(attachment.Source);
            }

            if (string.IsNullOrWhiteSpace(attachment.MediaType))
            {
                yield return Invalid($"Attachment '{attachment.FileName}' has no media type.", attachment.MediaType ?? "null").WithSource(attachment.Source);
            }

            if (attachment.Relationship is { } relationship && !Enum.IsDefined(relationship))
            {
                yield return Invalid($"Attachment '{attachment.FileName}' has the relationship {(int)relationship}, which is not a relationship.", ((int)relationship).ToString(CultureInfo.InvariantCulture)).WithSource(attachment.Source);
            }
        }

        foreach (var duplicate in attachments.Where(attachment => attachment?.FileName is not null).GroupBy(attachment => attachment.FileName).Where(group => group.Count() > 1))
        {
            yield return Invalid($"Two attachments have the file name '{duplicate.Key}'; a name can be embedded once.", duplicate.Key)
                .WithSource(duplicate.Last().Source);
        }
    }

    private static string Number(int number) => number.ToString(CultureInfo.InvariantCulture);

    /// <summary>A breach of the page-model contract.</summary>
    /// <param name="message">What is wrong, in words about the page model.</param>
    /// <param name="value">The offending value.</param>
    /// <returns>The error.</returns>
    internal static Error Invalid(string message, string value) =>
        new Error(PdfRenderErrorCodes.Write.InvalidPageModel, message).With(ErrorMetadataKeys.Value, value);
}

/// <summary>
/// How tagged PDF's table structure nests (ISO 32000-1 §14.8.4.3.4, table elements, Table 337): a row (TR) is a child of a table or
/// of one of its row groups (THead, TBody, TFoot); a header or data cell (TH, TD) is a child of a row; a row group is a
/// child of a table. Seen from the parent, a table holds only rows and row groups, a row group only rows, a row only
/// cells. veraPDF checks the same for PDF/UA-1 (rules 7.2-3 to 7.2-10, 7.2-36 to 7.2-38). Parent here means the nearest
/// enclosing node that has a structure type; a decorative node (an artifact) is not one.
/// </summary>
internal static class StructureNesting
{
    private static readonly ImmutableHashSet<string> RowParents = ["Table", "THead", "TBody", "TFoot"];

    private static readonly ImmutableDictionary<string, ImmutableHashSet<string>> AllowedParents = new Dictionary<string, ImmutableHashSet<string>>
    {
        ["TR"] = RowParents,
        ["TH"] = ["TR"],
        ["TD"] = ["TR"],
        ["THead"] = ["Table"],
        ["TBody"] = ["Table"],
        ["TFoot"] = ["Table"],
    }.ToImmutableDictionary();

    private static readonly ImmutableDictionary<string, ImmutableHashSet<string>> AllowedChildren = new Dictionary<string, ImmutableHashSet<string>>
    {
        ["Table"] = ["TR", "THead", "TBody", "TFoot"],
        ["THead"] = ["TR"],
        ["TBody"] = ["TR"],
        ["TFoot"] = ["TR"],
        ["TR"] = ["TH", "TD"],
    }.ToImmutableDictionary();

    /// <summary>Every structure start on the page whose parent or own type breaks the table rules.</summary>
    /// <param name="page">The page.</param>
    /// <param name="number">Its page number.</param>
    /// <returns>The breaches.</returns>
    public static IEnumerable<Error> Problems(Page page, int number)
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
                    yield return PageModelRules.Invalid($"Node {start.Node} on page {number} is a {type} {reason}.", type).WithSource(start.Source);
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
    /// Table 337 says a table "contains either one or more table rows (TR) as children; or an optional table head
    /// (THead) followed by one or more table body elements (TBody) and an optional table footer (TFoot)". So a table
    /// holds rows or row groups, not both; at most one THead, which comes first; at most one TFoot, which comes last.
    /// veraPDF checks the counts for PDF/UA-1 (rules 7.2-11 and 7.2-12). Whether a head needs a body is a profile's rule
    /// (PDF/UA-1, rule 7.2-14). Linear in the number of structure markers, whatever their shape.
    /// </summary>
    /// <param name="pages">The pages; missing pages are reported elsewhere and skipped here.</param>
    /// <returns>One breach per table that has one.</returns>
    public static IEnumerable<Error> TableParts(IEnumerable<Page?> pages)
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
                    if (type is "TR" or "THead" or "TBody" or "TFoot"
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

        return kids.Select(table => PartsBreach(table.Key, table.Value)).OfType<Error>();
    }

    /// <summary>The first way a table's kids, in order, break the rule of <see cref="TableParts"/>, or null.</summary>
    private static Error? PartsBreach(NodeId table, List<(string Type, SourceLocation Source)> kids)
    {
        var types = kids.Select(kid => kid.Type).ToList();
        var reason =
            types.Contains("TR") && types.Exists(type => type != "TR") ? "holds rows beside row groups; it holds one or the other"
            : types.Count(type => type == "THead") > 1 ? "has more than one THead; it has at most one"
            : types.Count(type => type == "TFoot") > 1 ? "has more than one TFoot; it has at most one"
            : types.IndexOf("THead") > 0 ? "has its THead after another row group; the head comes first"
            : types.Contains("TFoot") && types[^1] != "TFoot" ? "has a row group after its TFoot; the footer comes last"
            : null;
        return reason is null
            ? null
            : PageModelRules.Invalid($"Table node {table} {reason} (ISO 32000-1 Table 337).", string.Join(",", types)).WithSource(kids[0].Source);
    }

    private static string? Breach(string type, string? parent) =>
        AllowedParents.TryGetValue(type, out var parents) && (parent is null || !parents.Contains(parent))
            ? $"inside {parent ?? "nothing"}; it belongs in {string.Join(" or ", parents.Order(StringComparer.Ordinal))}"
            : parent is not null && AllowedChildren.TryGetValue(parent, out var children) && !children.Contains(type)
                ? $"inside a {parent}, which holds only {string.Join(", ", children.Order(StringComparer.Ordinal))}"
                : null;
}
