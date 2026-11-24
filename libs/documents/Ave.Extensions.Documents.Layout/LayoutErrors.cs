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

    /// <summary>A table with a height of its own, of a row or of a cell where a page break cuts a cell's content or a cell
    /// over several rows (<see cref="LayoutErrorCodes.TableBreak"/>; <see cref="TableHeights"/>).</summary>
    /// <param name="table">The tracks box.</param>
    /// <param name="available">The height it had on the page.</param>
    public static Error HeightBreak(Box table, double available) =>
        new Error(
                LayoutErrorCodes.TableBreak,
                $"{Describe(table)} has a height on the table, a row or a cell, and the page end in {Room(available)} cuts the content of a cell or a cell over "
                + "several rows: such a break is not drawn in this version. Give that row the room on one page, for example with a page break before "
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

    private static string Room(double available) =>
        available > 0 ? $"the {InvariantNumber.Format(available)}pt left for it on the page" : "the page, which has no room left for it,";

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
