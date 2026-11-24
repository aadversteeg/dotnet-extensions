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
/// allocate without a bound, or write a file no reader can open. One rule set for every back-end, so a page model that
/// breaks these rules is refused by every back-end; each back-end reports a breach under its own error code (the caller
/// says which), because the refusal is that back-end's. What a PDF profile requires on top is checked by the PDF
/// back-end. Past these rules each back-end refuses, with an error of its own, only what it cannot draw exactly: the
/// raster back-end an image whose pixel grid lies beyond its reach (<c>Documents.Raster.ImageOutOfReach</c>), the PDF
/// back-end a gap between two glyphs of a run that, in thousandths of a very small font size, is a number beyond the
/// range of a PDF real (<c>Documents.Pdf.Write.InvalidNumber</c>, ISO 32000-1 §9.4.3: TJ gives such a gap in those
/// units).
/// </summary>
public static class PageModelRules
{
    /// <summary>
    /// The most clips open one inside the other on a page, for every back-end: 27. The PDF back-end writes every clip as
    /// one <c>q</c> of its content stream, and every drawing inside it adds one more <c>q</c> of its own (a rectangle, an
    /// image and a glyph run each draw inside one <c>q</c>…<c>Q</c>, never deeper), so 27 clips make 28 levels of
    /// <c>q</c>: the most ISO 32000-1 Annex C (Table C.1, "q/Q nesting depth") names as a limit of every PDF reader, and
    /// what ISO 19005-3 §6.1.13 allows a PDF/A file ("shall not nest q/Q pairs by more than 28 nesting levels"), in every
    /// profile. The raster back-end has no such limit of its own; it takes the PDF one, so that both back-ends accept the
    /// same nesting of clips. The layout never opens more than one clip.
    /// </summary>
    public const int MaxClipDepth = 27;

    /// <summary>
    /// The most link areas (<see cref="LinkArea"/>) one page may hold: 8 191. Each becomes one link annotation, one object
    /// and one entry of the page's <c>/Annots</c> array in PDF, so the bound keeps what one page writes from growing
    /// with its input without limit; it is far more than any page of text can show (a link area per word of a page of
    /// 6 pt text is some thousands). More is <c>InvalidPageModel</c> under the back-end's code.
    /// </summary>
    public const int MaxLinkAreasPerPage = 8_191;

    /// <summary>
    /// The most anchors (<see cref="Anchor"/>) one document may hold: 65 536, the most nodes a document model's links may
    /// lead to (the layout writes one anchor per such node). The PDF back-end keeps a map from each anchor's node to its
    /// page and place, so the bound keeps that map from growing with its input without limit. More is
    /// <c>InvalidPageModel</c> under the back-end's code.
    /// </summary>
    public const int MaxAnchors = 65_536;

    /// <summary>
    /// Checks the page model and reports every breach together, under the code the back-end passes
    /// (<c>Documents.Pdf.Write.InvalidPageModel</c>, <c>Documents.Raster.InvalidPageModel</c>):
    /// <list type="bullet">
    /// <item>metadata and at least one page: a document without a page has nothing to show; every
    /// page of positive width and height;</item>
    /// <item>every number is a number PDF can hold: finite and within ±<see cref="MaxNumber"/> (page sizes, font sizes,
    /// positions and sizes of glyph runs, glyphs, filled rectangles, images and clips), and so is every edge computed
    /// from them: x + width and y + height of a rectangle, the place of a glyph (run origin + glyph), and their height
    /// above the foot of the page;</item>
    /// <item>no missing page, item, glyph, font, image, role or attachment;</item>
    /// <item>every glyph is a glyph of its font (below maxp.numGlyphs), since the font program is embedded from those
    /// glyphs, and has a text, which its ToUnicode entry needs (the empty text for a glyph that continues the text of
    /// the one before it, as the later glyphs of a one-to-many substitution do); every font size is a positive
    /// number;</item>
    /// <item>every artifact kind and attachment relationship is a value of its enum;</item>
    /// <item>a table cell spans at least one column and one row, and no more than the table model allows
    /// (<see cref="TableSlots.MaxColumnSpan"/>, <see cref="TableSlots.MaxRowSpan"/>), so no grid is ever sized by one
    /// number;</item>
    /// <item>the markers of every page nest by THE NESTING RULE FOR MARKERS (<see cref="DrawItem"/>,
    /// <see cref="MarkerNesting"/>), with at most <see cref="MaxClipDepth"/> clips one inside the other;</item>
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
            : SizeProblems(page, 1, code).Concat(ItemProblems(page, 1, code)).Concat(MarkerNesting.Problems(page, 1, code)).Concat(StructureNesting.Problems(page, 1, code)).ToList();
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
                : SizeProblems(page, index + 1, code).Concat(ItemProblems(page, index + 1, code)).Concat(MarkerNesting.Problems(page, index + 1, code)).Concat(StructureNesting.Problems(page, index + 1, code)).Concat(RoleProblems(page, index + 1, roles, code));
            foreach (var problem in problems)
            {
                yield return problem;
            }
        }

        foreach (var problem in AnchorProblems(document.Pages, code))
        {
            yield return problem;
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

    /// <summary>
    /// The largest magnitude of a number of the page model: 3.403 × 10^38, the range of a real number in PDF (ISO 32000-1
    /// Annex C, Table C.1, "real: ±3.403 × 10^38"; veraPDF rule 6.1.13-2 enforces it for PDF/A). The same value as the
    /// PDF writer's limit. The rules hold each number to it and also the edges a back-end computes from them (x + width,
    /// y + height, and the height above the foot of the page, which the PDF back-end writes), so a page model that passes
    /// holds no position or size the PDF writer cannot write; inside it the raster back-end computes in double precision
    /// without overflow.
    /// </summary>
    public const double MaxNumber = 3.403e38;

    /// <summary>A page is a rectangle of finite width and height, each at least <see cref="MinPageSide"/> (in PDF its
    /// /MediaBox, ISO 32000-1 §14.11.2). A page of no area shows nothing.</summary>
    private static IEnumerable<Error> SizeProblems(Page page, int number, ErrorCode code) =>
        page.Size is { Width: >= MinPageSide and <= MaxNumber, Height: >= MinPageSide and <= MaxNumber }
            ? []
            : [Invalid(code, $"Page {number} is {Number(page.Size.Width)} x {Number(page.Size.Height)}; a page has a width and height of {Number(MinPageSide)} to {Number(MaxNumber)}.", $"{Number(page.Size.Width)}x{Number(page.Size.Height)}")];

    private static string Number(double value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>The breaches of the items of a page. Heights above the foot of a page whose height is itself a breach
    /// are measured from 0, so that breach is reported once, as the page's.</summary>
    private static IEnumerable<Error> ItemProblems(Page page, int number, ErrorCode code)
    {
        var height = page.Size.Height is >= MinPageSide and <= MaxNumber ? page.Size.Height : 0;
        return page.Items.Select((item, index) => ItemProblem(item, index, number, height, code)).OfType<Error>();
    }

    private static Error? ItemProblem(DrawItem? item, int index, int number, double pageHeight, ErrorCode code) => item switch
    {
        null => Invalid(code, $"Item {index + 1} of page {number} is missing.", Number(number)),
        GlyphRun { Font: null } => Invalid(code, $"A glyph run on page {number} has no font.", Number(number)),
        GlyphRun run when !(run.FontSize is > 0 and <= MaxNumber) =>
            Invalid(code, $"A glyph run on page {number} has the font size {Number(run.FontSize)}; {(run.FontSize > MaxNumber ? $"that is above the limit of {Number(MaxNumber)}" : "it must be a positive number")}.", Number(run.FontSize)),
        GlyphRun run when run.Glyphs.Any(glyph => glyph is null) => Invalid(code, $"A glyph run on page {number} has a missing glyph.", Number(number)),
        GlyphRun run when run.Glyphs.FirstOrDefault(glyph => glyph.Glyph.Value >= run.Font.GlyphCount) is { } glyph =>
            Invalid(code, $"Page {number} draws glyph {glyph.Glyph.Value} of font '{run.Font.Names.PostScriptName}', which has {run.Font.GlyphCount} glyphs.", glyph.Glyph.Value.ToString(CultureInfo.InvariantCulture)),
        GlyphRun run when run.Glyphs.Any(glyph => glyph.Text is null) =>
            Invalid(code, $"A glyph on page {number} has no text; every shown glyph needs one for its ToUnicode entry (empty for a glyph that continues the text of the glyph before it).", Number(number)),
        DrawImage { Image: null } => Invalid(code, $"An image on page {number} has no image data.", Number(number)),
        StructureStart { Role: null } start => Invalid(code, $"Node {start.Node} on page {number} has no role.", start.Node.ToString()).WithSource(start.Source),
        StructureStart { Language: { } language } start when !LanguageTags.IsWellFormed(language) =>
            Invalid(code, $"Node {start.Node} on page {number} has the language '{language}', which is not a well-formed language tag (BCP 47 syntax, at most {LanguageTags.MaxLength} characters).", language).WithSource(start.Source),
        StructureStart start when !TableSlots.IsValidSpan(start.Span) =>
            Invalid(code, $"Node {start.Node} on page {number} spans {start.Span}; a cell spans 1 to {TableSlots.MaxColumnSpan} columns and 1 to {TableSlots.MaxRowSpan} rows.", start.Span.ToString()).WithSource(start.Source),
        GlyphRun run when !InRange(run.Origin) || run.Glyphs.Any(glyph => !InRange(glyph.X) || !InRange(glyph.Y)) =>
            Invalid(code, $"A glyph run on page {number} has a position that is not a number within ±{Number(MaxNumber)}.", $"{run.Origin}"),
        FillRectangle fill when !InRange(fill.Area) => Invalid(code, $"A filled rectangle on page {number} has a position or size that is not a number within ±{Number(MaxNumber)}.", $"{fill.Area}"),
        DrawImage image when !InRange(image.Area) => Invalid(code, $"An image on page {number} has a position or size that is not a number within ±{Number(MaxNumber)}.", $"{image.Area}"),
        ClipStart clip when !InRange(clip.Area) => Invalid(code, $"A clip on page {number} has a position or size that is not a number within ±{Number(MaxNumber)}.", $"{clip.Area}"),
        LinkArea link when !InRange(link.Area) => Invalid(code, $"A link area on page {number} has a position or size that is not a number within ±{Number(MaxNumber)}.", $"{link.Area}"),
        LinkArea link when !EdgesInRange(link.Area, pageHeight) => Invalid(code, $"A link area on page {number} has {EdgesText}.", $"{link.Area}"),
        LinkArea link when !(link.Area.Size.Width >= 0 && link.Area.Size.Height >= 0) => Invalid(code, $"A link area on page {number} has a negative width or height.", $"{link.Area}"),
        Anchor anchor when !InRange(anchor.At) || !InRange(pageHeight - anchor.At.Y) =>
            Invalid(code, $"The anchor of node {anchor.Node} on page {number} has a position that is not a number within ±{Number(MaxNumber)} (or its height above the foot of the page is not).", $"{anchor.At}"),
        StructureStart { Role: SemanticRole.LinkRole link } start when LinkProblem(link) is { } problem =>
            Invalid(code, $"Link {start.Node} on page {number} is refused: {problem}.", (link.Target as LinkTarget.UriTarget)?.Value ?? "null").WithSource(start.Source),
        GlyphRun run when run.Glyphs.Any(glyph => !EdgesInRange(run.Origin, glyph, pageHeight)) =>
            Invalid(code, $"A glyph run on page {number} places a glyph at {EdgesText}.", $"{run.Origin}"),
        FillRectangle fill when !EdgesInRange(fill.Area, pageHeight) => Invalid(code, $"A filled rectangle on page {number} has {EdgesText}.", $"{fill.Area}"),
        DrawImage image when !EdgesInRange(image.Area, pageHeight) => Invalid(code, $"An image on page {number} has {EdgesText}.", $"{image.Area}"),
        ClipStart clip when !EdgesInRange(clip.Area, pageHeight) => Invalid(code, $"A clip on page {number} has {EdgesText}.", $"{clip.Area}"),
        ArtifactStart artifact when !Enum.IsDefined(artifact.Kind) =>
            Invalid(code, $"An artifact on page {number} has the kind {(int)artifact.Kind}, which is not an artifact kind.", ((int)artifact.Kind).ToString(CultureInfo.InvariantCulture)),
        StructureStart { Role: SemanticRole.TableHeaderCellRole header } start when !Enum.IsDefined(header.Scope) =>
            Invalid(code, $"Header cell {start.Node} on page {number} has the scope {(int)header.Scope}; a header cell heads its column, its row or both.", ((int)header.Scope).ToString(CultureInfo.InvariantCulture)).WithSource(start.Source),
        _ => null,
    };

    /// <summary>Why a link role cannot be written, or null: its target by THE RULE FOR A LINK'S URI (<see cref="LinkUris"/>),
    /// and a description that is not blank and holds only characters text may hold.</summary>
    private static string? LinkProblem(SemanticRole.LinkRole link)
    {
        var target = link.Target switch
        {
            LinkTarget.UriTarget uri => LinkUris.Problem(uri.Value),
            LinkTarget.NodeTarget => null,
            null => "a link needs a target",
            _ => $"a target of the kind {link.Target.GetType().Name} cannot be written",
        };
        return target ?? (string.IsNullOrWhiteSpace(link.Description) || TextCharacters.IndexOfForbidden(link.Description) is not null
            ? "a link needs a description that is not blank and holds only characters text may hold"
            : null);
    }

    /// <summary>Finite and within ±<see cref="MaxNumber"/> (NaN fails both comparisons).</summary>
    private static bool InRange(double value) => value is >= -MaxNumber and <= MaxNumber;

    private static bool InRange(Point point) => InRange(point.X) && InRange(point.Y);

    private static bool InRange(Rect area) => InRange(area.Origin) && InRange(area.Size.Width) && InRange(area.Size.Height);

    private static readonly string EdgesText = $"an edge that is not a number within ±{Number(MaxNumber)} (x + width, y + height, or its height above the foot of the page)";

    /// <summary>
    /// The edges a back-end computes from a rectangle whose four numbers are in range: its far edges (x + width,
    /// y + height) and its lower edge measured from the foot of the page (page height − y − height, in that order of
    /// operations: the y a PDF content stream writes for the rectangle, whose origin is the page's lower left corner,
    /// ISO 32000-1 §8.3.2.3). Each must be in range too, or one back-end would draw what the other cannot write.
    /// </summary>
    private static bool EdgesInRange(Rect area, double pageHeight) =>
        InRange(area.Origin.X + area.Size.Width) && InRange(area.Origin.Y + area.Size.Height) && InRange(pageHeight - area.Origin.Y - area.Size.Height);

    /// <summary>The place of a glyph on the page (run origin + glyph offset) and its height above the foot of the page
    /// (page height − that y, the y a PDF text position is written with), each in range.</summary>
    private static bool EdgesInRange(Point origin, PositionedGlyph glyph, double pageHeight) =>
        InRange(origin.X + glyph.X) && InRange(origin.Y + glyph.Y) && InRange(pageHeight - (origin.Y + glyph.Y));

    /// <summary>
    /// The anchors across the pages: at most one per node, at most <see cref="MaxAnchors"/> in the document, and one for
    /// the node of every link inside the document (a link role whose target is <see cref="LinkTarget.Node"/>, on any
    /// page): without it the back-end has no place to send the link to. Each breach once.
    /// </summary>
    private static IEnumerable<Error> AnchorProblems(IReadOnlyList<Page?> pages, ErrorCode code)
    {
        var anchors = new HashSet<NodeId>();
        var links = new Dictionary<NodeId, StructureStart>();
        for (var index = 0; index < pages.Count; index++)
        {
            foreach (var item in pages[index]?.Items ?? [])
            {
                if (item is Anchor anchor && !anchors.Add(anchor.Node))
                {
                    yield return Invalid(code, $"Node {anchor.Node} has a second anchor, on page {index + 1}; a node has one place a link leads to.", anchor.Node.ToString());
                }
                else if (item is Anchor && anchors.Count > MaxAnchors)
                {
                    yield return Invalid(code, $"The document has more than {MaxAnchors} anchors.", Number(MaxAnchors));
                    yield break;
                }
                else if (item is StructureStart { Role: SemanticRole.LinkRole { Target: LinkTarget.NodeTarget target } } start)
                {
                    links.TryAdd(target.Value, start);
                }
            }
        }

        foreach (var (node, start) in links)
        {
            if (!anchors.Contains(node))
            {
                yield return Invalid(code, $"Link {start.Node} leads to node {node}, which has no anchor in the document.", node.ToString()).WithSource(start.Source);
            }
        }
    }

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
/// THE NESTING RULE FOR MARKERS of the page model (<see cref="DrawItem"/>), checked once for every back-end: one stack of
/// open structure, artifact and clip markers per page, where every end closes the innermost open marker, which must be a
/// start of its kind (for a structure end, of its node); nothing is left open at the end of the page; no artifact or
/// structure marker opens inside an artifact; at most <see cref="PageModelRules.MaxClipDepth"/> clips are open at once.
/// The PDF back-end keeps its graphics-state and marked-content pairs inside each other by this rule; the raster
/// back-end opens and closes its clips by it.
/// </summary>
internal static class MarkerNesting
{
    /// <summary>The first breach of the rule on the page, or none.</summary>
    /// <param name="page">The page.</param>
    /// <param name="number">Its page number.</param>
    /// <param name="code">The back-end's error code.</param>
    /// <returns>The breach, if any: after the first one the rest of the page cannot be read by the rule.</returns>
    public static IEnumerable<Error> Problems(Page page, int number, ErrorCode code)
    {
        var open = new Stack<DrawItem>();
        var inArtifact = false;
        var clips = 0;
        var links = 0;
        foreach (var item in page.Items)
        {
            links += item is LinkArea ? 1 : 0;
            string? problem = item switch
            {
                StructureStart or StructureEnd or ArtifactStart when inArtifact =>
                    item is ArtifactStart ? "an artifact inside an artifact" : "a structure marker inside an artifact",
                LinkArea when links > PageModelRules.MaxLinkAreasPerPage => $"more than {PageModelRules.MaxLinkAreasPerPage} link areas",
                LinkArea link => LinkAreaProblem(link, inArtifact, open.OfType<StructureStart>().FirstOrDefault()),
                Anchor anchor when inArtifact => $"the anchor of {anchor.Node} inside an artifact",
                ClipStart when clips >= PageModelRules.MaxClipDepth => $"more than {PageModelRules.MaxClipDepth} clips open one inside the other",
                StructureEnd or ArtifactEnd or ClipEnd => EndProblem(item, open.Count > 0 ? open.Peek() : null),
                _ => null,
            };
            if (problem is not null)
            {
                yield return Invalid(code, number, problem, (item as StructureEnd)?.Node);
                yield break;
            }

            switch (item)
            {
                case StructureStart or ArtifactStart or ClipStart:
                    open.Push(item);
                    inArtifact |= item is ArtifactStart;
                    clips += item is ClipStart ? 1 : 0;
                    break;
                case StructureEnd or ArtifactEnd or ClipEnd:
                    open.Pop();
                    inArtifact &= item is not ArtifactEnd;
                    clips -= item is ClipEnd ? 1 : 0;
                    break;
            }
        }

        if (open.Count > 0)
        {
            yield return open.Peek() switch
            {
                StructureStart start => Invalid(code, number, $"{start.Node} without its end", start.Node),
                ArtifactStart => Invalid(code, number, "an artifact without its end"),
                _ => Invalid(code, number, "a clip without its end"),
            };
        }
    }

    /// <summary>Why a link area does not stand where it belongs, or null: inside the structure marker of its own link, as
    /// the innermost open structure marker, outside every artifact (<see cref="LinkArea"/>).</summary>
    private static string? LinkAreaProblem(LinkArea link, bool inArtifact, StructureStart? innermost) =>
        inArtifact ? $"a link area of {link.Link} inside an artifact"
        : innermost is { Role: SemanticRole.LinkRole } start && start.Node == link.Link ? null
        : $"a link area of {link.Link} outside the structure marker of its link" + Inside(innermost);

    /// <summary>Why an end marker does not close the innermost open marker, or null when it does.</summary>
    private static string? EndProblem(DrawItem end, DrawItem? innermost) => (end, innermost) switch
    {
        (StructureEnd closing, StructureStart start) when start.Node == closing.Node => null,
        (ArtifactEnd, ArtifactStart) or (ClipEnd, ClipStart) => null,
        (StructureEnd closing, _) => $"the end of {closing.Node} without its start" + Inside(innermost),
        (ArtifactEnd, _) => "the end of an artifact without its start" + Inside(innermost),
        _ => "the end of a clip without its start" + Inside(innermost),
    };

    private static string Inside(DrawItem? innermost) => innermost switch
    {
        null => string.Empty,
        StructureStart start => $" (inside {start.Node})",
        ArtifactStart => " (inside an artifact)",
        _ => " (inside a clip)",
    };

    private static Error Invalid(ErrorCode code, int page, string problem, NodeId? node = null)
    {
        var error = PageModelRules.Invalid(code, $"Page {page} has {problem}.", page.ToString(CultureInfo.InvariantCulture));
        return node is { } id ? error.With(ErrorMetadataKeys.Node, id.ToString()) : error;
    }
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
