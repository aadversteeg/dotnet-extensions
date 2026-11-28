using System.Collections.Immutable;
using System.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.ErrorPaths;

namespace Ave.Extensions.Documents.Layout;

/// <summary>The errors layout reports, each with the node and its source location. A message names the author's
/// element by its role and the line that made it, not by its node number.</summary>
internal static class LayoutErrors
{
    /// <summary>The element a box was made from, for a message: its role and its source.</summary>
    private static string Describe(Box box) => $"The {box.Role} made at {box.Source}";

    /// <summary>
    /// A box of which not even the first piece fits, where nothing before it on the page can move to a later page: it
    /// starts the page (<see cref="ConstraintSpace.IsAtPageStart"/>), or it is the root below its own top margin. The
    /// message says what does not fit and how much room it had.
    /// </summary>
    /// <param name="box">The box.</param>
    /// <param name="available">The height it had, from the top of its border box (zero or less: none).</param>
    public static Error DoesNotFit(Box box, double available) =>
        new Error(
                LayoutErrorCodes.DoesNotFit,
                $"{Describe(box)} does not fit in {Room(available)} and cannot be broken; nothing before it on the page can move to a later page.")
            .With(ErrorMetadataKeys.Node, box.Id.ToString())
            .With(ErrorMetadataKeys.Value, InvariantNumber.Format(available))
            .WithSource(box.Source);

    /// <summary>Content of a box with a height that goes on past a page break after the box's height ended on the page
    /// (<see cref="LayoutErrorCodes.OverflowAcrossPages"/>).</summary>
    /// <param name="box">The box.</param>
    /// <param name="rest">The height of the box's rest on the page.</param>
    public static Error OverflowAcrossPages(Box box, double rest) =>
        new Error(
                LayoutErrorCodes.OverflowAcrossPages,
                $"{Describe(box)} ends on this page, {InvariantNumber.Format(rest)}pt below its top here, and its content goes on past a page break; content that overflows a box onto a later page is not drawn in this version.")
            .With(ErrorMetadataKeys.Node, box.Id.ToString())
            .With(ErrorMetadataKeys.Value, InvariantNumber.Format(rest))
            .WithSource(box.Source);

    /// <summary>A box with rounded corners and a border that a page break cuts
    /// (<see cref="LayoutErrorCodes.RoundedBorderBreak"/>).</summary>
    /// <param name="box">The box.</param>
    public static Error RoundedBorderBreak(Box box) =>
        new Error(
                LayoutErrorCodes.RoundedBorderBreak,
                $"{Describe(box)} has rounded corners and a border, and a page break cuts it; such a border is not drawn in this version. Keep the box on one page (break-inside: avoid), or leave out its border or its radius.")
            .With(ErrorMetadataKeys.Node, box.Id.ToString())
            .WithSource(box.Source);

    /// <summary>An inline box with rounded corners and a border that a line break cuts
    /// (<see cref="LayoutErrorCodes.RoundedBorderLineBreak"/>).</summary>
    /// <param name="group">The inline box's group.</param>
    public static Error RoundedBorderLineBreak(InlineGroup group) =>
        new Error(
                LayoutErrorCodes.RoundedBorderLineBreak,
                "An inline box with rounded corners and a border is broken over two or more lines; such a border is not drawn in this version. Keep the text of the box on one line (white-space: nowrap), or leave out its border or its radius.")
            .With(ErrorMetadataKeys.Node, group.Id.ToString())
            .WithSource(group.Source);

    /// <summary>A dashed border side too short or too long to dash (<see cref="LayoutErrorCodes.DashedBorder"/>).</summary>
    /// <param name="box">The box.</param>
    /// <param name="length">The side's length, in CSS pixels.</param>
    /// <param name="width">The side's width, in CSS pixels.</param>
    /// <param name="tooMany">True for a side with more dashes than one path holds, false for one too short.</param>
    public static Error DashedBorder(Box box, double length, double width, bool tooMany) =>
        new Error(
                LayoutErrorCodes.DashedBorder,
                !tooMany
                    ? $"{Describe(box)} has a dashed border side {InvariantNumber.Format(length)} px long, shorter than two dashes and a gap of a {InvariantNumber.Format(width)} px border; Chromium draws such a side as a solid line, which is not drawn in this version. Make the box larger, or use a solid border."
                    : $"{Describe(box)} has a dashed border side {InvariantNumber.Format(length)} px long, more than {Layout.DashedBorder.MaxDashes} dashes of a {InvariantNumber.Format(width)} px border; such a side is not drawn in this version. Use a solid border.")
            .With(ErrorMetadataKeys.Node, box.Id.ToString())
            .With(ErrorMetadataKeys.Value, $"{InvariantNumber.Format(length)} px, {InvariantNumber.Format(width)} px")
            .WithSource(box.Source);

    /// <summary>A list item's label that would start left of the sheet (<see cref="LayoutErrorCodes.MarkerOutsideSheet"/>).</summary>
    /// <param name="label">The label; its source is the item's.</param>
    /// <param name="left">The label's left edge on the sheet, in points (negative).</param>
    public static Error MarkerOutsideSheet(Box label, double left) =>
        new Error(
                LayoutErrorCodes.MarkerOutsideSheet,
                $"The label of the list item made at {label.Source} would start {InvariantNumber.Format(-left)}pt left of the sheet's edge: it is wider than the room left of its item. Give the list more padding on the left.")
            .With(ErrorMetadataKeys.Node, label.Id.ToString())
            .With(ErrorMetadataKeys.Value, InvariantNumber.Format(left))
            .WithSource(label.Source);
    /// <summary>A table with separated borders that breaks over pages in a way this version does not draw
    /// (<see cref="LayoutErrorCodes.TableBreak"/>).</summary>
    /// <param name="table">The tracks box.</param>
    /// <param name="available">The height it had on the page.</param>
    public static Error TableBreak(Box table, double available) =>
        new Error(
                LayoutErrorCodes.TableBreak,
                $"{Describe(table)} has separated borders and breaks in {Room(available)} where this version does not draw the break: "
                + "a cell over several rows across it, content sliced in a cell, a row cut beside a cell aligned at the middle or bottom "
                + "under the profile's CutRowAlignment RestAlignedInItsPart, "
                + "box-decoration-break: clone, or a part past the page end. Give the table the room on one page, or draw it with collapsed borders.")
            .With(ErrorMetadataKeys.Node, table.Id.ToString())
            .With(ErrorMetadataKeys.Value, InvariantNumber.Format(available))
            .WithSource(table.Source);

    /// <summary>A table caption that does not fit where it is drawn, with the table's first row below it or below the
    /// table's last row (<see cref="LayoutErrorCodes.TableBreak"/>; <see cref="TableCaption"/>).</summary>
    /// <param name="table">The tracks box.</param>
    /// <param name="caption">The caption.</param>
    /// <param name="available">The height the table had on the page.</param>
    /// <param name="where">Where the caption is drawn.</param>
    public static Error CaptionBreak(Box table, Box caption, double available, string where) =>
        new Error(
                LayoutErrorCodes.TableBreak,
                $"{Describe(table)} has a caption that does not fit {where} in {Room(available)}: a table whose caption and first or last row "
                + "do not fit on one page together is not drawn in this version. Give the table the room, for example with a page break before it, or move the caption's text out of the table.")
            .With(ErrorMetadataKeys.Node, caption.Id.ToString())
            .With(ErrorMetadataKeys.Value, InvariantNumber.Format(available))
            .WithSource(table.Source);

    /// <summary>A table with a height of its own, of a row or of a cell where a page break cuts a cell over several rows
    /// (<see cref="LayoutErrorCodes.TableBreak"/>; <see cref="TableHeights"/>).</summary>
    /// <param name="table">The tracks box.</param>
    /// <param name="available">The height it had on the page.</param>
    public static Error HeightBreak(Box table, double available) =>
        new Error(
                LayoutErrorCodes.TableBreak,
                $"{Describe(table)} has a height on the table, a row or a cell, and the page end in {Room(available)} cuts a cell over several rows: "
                + "such a break is not drawn in this version. Give that row the room on one page, for example with a page break before "
                + "the table, or leave the heights out.")
            .With(ErrorMetadataKeys.Node, table.Id.ToString())
            .With(ErrorMetadataKeys.Value, InvariantNumber.Format(available))
            .WithSource(table.Source);

    /// <summary>A header or footer area's content that does not fit in the area: an area never breaks.</summary>
    /// <param name="content">The area's content.</param>
    /// <param name="height">The area's height: its fixed height, or the page area's.</param>
    public static Error AreaDoesNotFit(Box content, double height) =>
        new Error(
                LayoutErrorCodes.DoesNotFit,
                $"{Describe(content)} does not fit in the {InvariantNumber.Format(height)}pt of its header or footer area, and an area never breaks.")
            .With(ErrorMetadataKeys.Node, content.Id.ToString())
            .With(ErrorMetadataKeys.Value, InvariantNumber.Format(height))
            .WithSource(content.Source);

    /// <summary>
    /// Content wider than its page-margin box, or a box whose margins, padding and border are taller than its row
    /// (<see cref="LayoutErrorCodes.MarginBoxOverflow"/>): Chromium lets wider content overflow the box unclipped, which
    /// the engine does not draw, so it refuses it at the box's source (the margin rule it was made from).
    /// </summary>
    /// <param name="box">The page-margin box, or the row whose own padding and border leave no room.</param>
    /// <param name="axis"><c>width</c> or <c>height</c>.</param>
    /// <param name="needed">What the box needs: its content's min-content width, or its margins, padding and border.</param>
    /// <param name="room">What the box has.</param>
    public static Error MarginBoxOverflow(Box box, string axis, double needed, double room) =>
        new Error(
                LayoutErrorCodes.MarginBoxOverflow,
                $"{Describe(box)} needs a {axis} of {InvariantNumber.Format(needed)}pt and its page-margin box has {InvariantNumber.Format(room)}pt; "
                + "content wider than its page-margin box is not drawn over the boxes beside it or past the sheet, and a box is not drawn taller than its row.")
            .With(ErrorMetadataKeys.Node, box.Id.ToString())
            .With(ErrorMetadataKeys.Property, axis)
            .With(ErrorMetadataKeys.Value, $"{InvariantNumber.Format(needed)}>{InvariantNumber.Format(room)}")
            .WithSource(box.Source);

    /// <summary>A layout algorithm broke the layout contract (<see cref="LayoutErrorCodes.EngineDefect"/>).</summary>
    /// <param name="box">The box whose algorithm broke it.</param>
    /// <param name="contract">Which contract, for the message and the metadata.</param>
    public static Error EngineDefect(Box box, string contract) =>
        new Error(
                LayoutErrorCodes.EngineDefect,
                $"A defect of the layout engine: the algorithm of the {box.Role} made at {box.Source} {contract}. The document is not at fault.")
            .With(ErrorMetadataKeys.Node, box.Id.ToString())
            .With(ErrorMetadataKeys.Value, contract)
            .WithSource(box.Source);

    /// <summary>A break token the box's algorithm cannot have returned for it (<see cref="LayoutErrorCodes.InvalidBreakToken"/>).</summary>
    /// <param name="box">The box the token was given with.</param>
    /// <param name="token">The token.</param>
    /// <param name="reason">What does not belong to the box, for the message.</param>
    public static Error InvalidBreakToken(Box box, BreakToken token, string reason) =>
        new Error(
                LayoutErrorCodes.InvalidBreakToken,
                $"{Describe(box)} cannot resume from the {token.GetType().Name} it was given: {reason}. A break token resumes only the box whose layout returned it.")
            .With(ErrorMetadataKeys.Node, box.Id.ToString())
            .With(ErrorMetadataKeys.Value, token.GetType().Name)
            .WithSource(box.Source);

    /// <summary>Content taller than the page area that would need more than <see cref="MonolithicSlices.MaxSlices"/>
    /// slices.</summary>
    /// <param name="box">The box the content belongs to.</param>
    /// <param name="height">The content's height.</param>
    /// <param name="shown">The height the slices so far showed.</param>
    public static Error TooManySlices(Box box, double height, double shown) =>
        new Error(
                LayoutErrorCodes.TooManySlices,
                $"{Describe(box)} is {InvariantNumber.Format(height)}pt tall and would need more than {MonolithicSlices.MaxSlices} slices, one per page, "
                + $"the limit for one piece; {MonolithicSlices.MaxSlices} slices showed {InvariantNumber.Format(shown)}pt of it.")
            .With(ErrorMetadataKeys.Node, box.Id.ToString())
            .With(ErrorMetadataKeys.Value, $"{InvariantNumber.Format(height)}>{InvariantNumber.Format(shown)}")
            .WithSource(box.Source);

    /// <summary>A document whose content taller than the page area would need more than
    /// <see cref="MonolithicSlices.MaxSlicesPerDocument"/> slices, all pieces together.</summary>
    /// <param name="root">The document's root box.</param>
    /// <param name="page">The page that would bring the count past the limit.</param>
    /// <param name="slices">The number of slices up to and including that page.</param>
    public static Error TooManySlicesInDocument(Box root, int page, int slices) =>
        new Error(
                LayoutErrorCodes.TooManySlices,
                $"The document would need more than {MonolithicSlices.MaxSlicesPerDocument} slices of content taller than the page area, "
                + $"the limit for one document; its first {page} pages hold {slices}.")
            .With(ErrorMetadataKeys.Node, root.Id.ToString())
            .With(ErrorMetadataKeys.Value, $"{slices}>{MonolithicSlices.MaxSlicesPerDocument}")
            .WithSource(root.Source);

    /// <summary>A document that would have more than <paramref name="maxPages"/> pages (<see cref="LayoutEnvironment.MaxPages"/>).</summary>
    /// <param name="root">The document's root box.</param>
    /// <param name="maxPages">The page limit of the render call.</param>
    public static Error TooManyPages(Box root, int maxPages) =>
        new Error(
                LayoutErrorCodes.TooManyPages,
                $"The document would have more than {maxPages} pages, the page limit of this render call; pagination stopped there. "
                + $"Raise the limit with the render option MaxPages (HtmlRenderOptions.MaxPages, LayoutEnvironment.MaxPages; default {LayoutEnvironment.DefaultMaxPages}) to render a longer document.")
            .With(ErrorMetadataKeys.Node, root.Id.ToString())
            .With(ErrorMetadataKeys.Value, maxPages.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .WithSource(root.Source);

    private static string Room(double available) =>
        available > 0 ? $"the {InvariantNumber.Format(available)}pt left for it on the page" : "the page, which has no room left for it,";

    /// <summary>An inline group with a box of its own (<see cref="InlineGroup.Box"/>) that this version does not draw where it
    /// stands, at the group's source.</summary>
    public static Error InlineBox(InlineGroup group, string message) =>
        new Error(DocumentErrorCodes.Input.Unsupported.InlineBox, $"The inline box made at {group.Source} is not drawn in this version: {message}.")
            .With(ErrorMetadataKeys.Node, group.Id.ToString())
            .With(ErrorMetadataKeys.Value, group.Box?.ToString() ?? "none")
            .With(ErrorMetadataKeys.Property, "box")
            .WithSource(group.Source);

    /// <summary>An inline-block taller than the page area that its line starts: Chromium moves the line to the page's top and
    /// slices the box over the pages, which is not drawn in this version (case <c>iblk-align</c>, its last paragraph).</summary>
    public static Error InlineBlockTallerThanPage(Box box, double height, double room) =>
        new Error(DocumentErrorCodes.Input.Unsupported.InlineBox, $"The inline-block made at {box.Source} is {InvariantNumber.Format(height)}pt tall with its line, and a page has {Room(room)}: an inline-block taller than a page is not drawn in this version. Make it a block, or give it less content.")
            .With(ErrorMetadataKeys.Node, box.Id.ToString())
            .With(ErrorMetadataKeys.Value, InvariantNumber.Format(height))
            .With(ErrorMetadataKeys.Property, "height")
            .WithSource(box.Source);

    public static Error InvalidContent(Box box, string message) =>
        new Error(LayoutErrorCodes.InvalidContent, $"{Describe(box)} cannot be laid out: {message}")
            .With(ErrorMetadataKeys.Node, box.Id.ToString())
            .With(ErrorMetadataKeys.Value, box.Layout.ToString())
            .WithSource(box.Source);

    /// <summary>The header and footer of a page leave no height for the body: reported at the header when the page
    /// has one, otherwise at the footer.</summary>
    public static Error NoBodyArea(PageTemplate template, int pageNumber)
    {
        var area = template.Header?.Content ?? template.Footer!.Content;
        return new Error(
                LayoutErrorCodes.DoesNotFit,
                $"The header and footer of page {pageNumber} leave no room for the page's content.")
            .With(ErrorMetadataKeys.Node, area.Id.ToString())
            .WithSource(area.Source);
    }

    public static Error ColumnsDoNotFit(Box table, double needed, double available) =>
        new Error(
                LayoutErrorCodes.ColumnsDoNotFit,
                $"{Describe(table)} needs {InvariantNumber.Format(needed)}pt for its fixed columns and the narrowest content of the others, "
                + $"but has {InvariantNumber.Format(available)}pt.")
            .With(ErrorMetadataKeys.Node, table.Id.ToString())
            .With(ErrorMetadataKeys.Value, $"{InvariantNumber.Format(needed)}>{InvariantNumber.Format(available)}")
            .WithSource(table.Source);

    /// <summary>
    /// The page count did not settle. Reported at the first page-count reference (in the header and footer areas
    /// first, then the body), or at the root when there is none — then some other computed content is to blame.
    /// </summary>
    public static Error NotConverging(StyledDocument document, ImmutableArray<int> counts)
    {
        var reference = Paginator.PageCountReferences(document).FirstOrDefault();
        var (node, source) = reference is null ? (document.Root.Id, document.Root.Source) : (reference.Id, reference.Source);
        return new Error(
                LayoutErrorCodes.NotConverging,
                $"The page count did not settle in {Paginator.MaxPasses} layout passes (page counts: {string.Join(", ", counts)}).")
            .With(ErrorMetadataKeys.Node, node.ToString())
            .With(ErrorMetadataKeys.Value, string.Join(",", counts))
            .WithSource(source);
    }

}
