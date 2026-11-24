using Ave.Extensions.Documents.Core;
using Ave.Extensions.ErrorPaths;

namespace Ave.Extensions.Documents.Layout;

/// <summary>Errors from layout.</summary>
public static class LayoutErrorCodes
{
    /// <summary><c>Documents.Layout</c>.</summary>
    public static readonly ErrorCode _ = DocumentErrorCodes._ / "Layout";

    /// <summary>Content that does not fit where it is and cannot be broken, with nothing before it on the page that
    /// could move to a later page: content that starts the page (nothing above it, not even a margin), or the document's
    /// root below its own top margin; also header or footer content that does not fit in its area. Metadata: Node, Value
    /// (the room it had, in points), SourceFile, SourceLine.</summary>
    public static readonly ErrorCode DoesNotFit = _ / "DoesNotFit";

    /// <summary>Monolithic content (an image, a line box) taller than the page area that the document's profile slices
    /// (<c>MonolithicOverflow.Slice</c>) and that would need more than 100 slices, one per page: a piece of content
    /// shows on at most 100 pages, so the number of pages stays bounded by the size of the input. Metadata: Node, Value
    /// (the content's height and the height shown in 100 slices, in points), SourceFile, SourceLine. Also content that
    /// would bring the slices of the whole document past 1 000 (<c>MonolithicSlices.MaxSlicesPerDocument</c>); the
    /// message says which of the two limits was passed. Metadata then: Node (the document's root), Value (the slices up
    /// to the page that passes the limit, and the limit), SourceFile, SourceLine.</summary>
    public static readonly ErrorCode TooManySlices = _ / "TooManySlices";

    /// <summary>A node whose layout algorithm is unknown, or whose children that algorithm cannot lay out.
    /// Metadata: Node, Value, SourceFile, SourceLine.</summary>
    public static readonly ErrorCode InvalidContent = _ / "InvalidContent";

    /// <summary>Table columns whose fixed widths and minimum content widths together are wider than the
    /// space the table has. Metadata: Node, Value (needed and available width), SourceFile, SourceLine.</summary>
    public static readonly ErrorCode ColumnsDoNotFit = _ / "ColumnsDoNotFit";

    /// <summary>A table in the separated borders model (<c>BoxStyle.BorderModel</c> <c>Separate</c>, CSS
    /// <c>border-collapse: separate</c>) that breaks where no measured case shows how Chromium prints it: a cell over several
    /// rows across the break, content sliced in a cell, a cut row beside a cell aligned at the middle or bottom under
    /// <c>CutRowAlignment.RestAlignedInItsPart</c>,
    /// <c>box-decoration-break: clone</c>, or a page of the table past the page end; and a table of either border model
    /// whose caption does not fit on the page with the first row below it, or below the last row, or with a height on the
    /// table, a row or a cell that does not fit whole on its page. Metadata: Node, Value (the
    /// room it had, in points), SourceFile, SourceLine.</summary>
    public static readonly ErrorCode TableBreak = _ / "TableBreak";

    /// <summary>A box with rounded corners and a border that a page break cuts (<c>BoxStyle.BorderRadius</c>): Chromium 159
    /// draws such a border's sides under clipping paths, which are not drawn in this version. Metadata: Node, SourceFile,
    /// SourceLine.</summary>
    public static readonly ErrorCode RoundedBorderBreak = _ / "RoundedBorderBreak";

    /// <summary>Computed content (the page count) that does not settle: every layout pass gives a different
    /// number of pages, up to the pass limit. Metadata: Node, Value (the page counts per pass), SourceFile,
    /// SourceLine of the first page-count reference.</summary>
    public static readonly ErrorCode NotConverging = _ / "NotConverging";

    /// <summary>Content wider than the page-margin box it is in (<c>MarginBoxesLayout</c>): text whose narrowest line is
    /// wider than the width the row gives the box, also when not even the min-content widths of the row's boxes fit the
    /// row; or a box whose own margins, padding and border are taller than the row. Chromium 159 lets such content
    /// overflow its box unclipped; the engine refuses it. Content taller than its box is drawn, as Chromium draws it.
    /// Metadata: Node, Property (<c>width</c> or <c>height</c>), Value (needed and available, in points), SourceFile,
    /// SourceLine of the box.</summary>
    public static readonly ErrorCode MarginBoxOverflow = _ / "MarginBoxOverflow";

    /// <summary>A list item's label that would start left of the sheet's left edge (<c>ListItemLayout</c>): a label wider
    /// than the room left of its item, which Chromium 159 draws past the sheet and cuts (case <c>li-types</c>: "MCMXCIX. "
    /// loses its first glyphs; case <c>li-marker-sheet</c>). The engine refuses it. Metadata: Node (the item), Value (the
    /// label's left edge on the sheet, in points), SourceFile, SourceLine of the item.</summary>
    public static readonly ErrorCode MarkerOutsideSheet = _ / "MarkerOutsideSheet";

    /// <summary>A break token a layout algorithm cannot have returned for the box it is given: a token of another
    /// algorithm's kind, or a position the box does not have (a child, line, row or cell outside it, a height shown that is
    /// negative, not finite, or the whole height of the sliced image or line or more). Only a caller of the algorithms
    /// themselves (<c>FlowLayout.Layout</c> and the other four) meets it: pagination hands each box the token that box's
    /// own layout returned. Metadata: Node, Value (the token's type), SourceFile, SourceLine.</summary>
    public static readonly ErrorCode InvalidBreakToken = _ / "InvalidBreakToken";

    /// <summary>A defect of the engine, never a property of the document: a layout algorithm broke the contract every
    /// algorithm keeps (it made no progress from one page to the next, reported no room at the start of a page, or did not
    /// break at the early break it reported). Reported instead of an exception, so pagination always ends and the caller
    /// gets an answer; a document that shows it is worth a bug report. Metadata: Value (which contract), Node.</summary>
    public static readonly ErrorCode EngineDefect = _ / "EngineDefect";

    /// <summary>Content of a box with a height (<c>BoxStyle.SpecifiedHeight</c>, <c>MaxHeight</c>) that goes on past a
    /// page break after the box's height has ended on the page: content that overflows the box down past the page end,
    /// or a forced break inside it. Chromium 159 ends the box on that page, starts what follows the box below its end there,
    /// and prints the rest of the content on the next page (cases <c>sz-height-overflow-page</c>, <c>sz-height-forced</c>);
    /// the engine's pages show a box's content before what follows the box, so it refuses. Metadata: Node, Value (the
    /// height of the box's rest on the page, in points), SourceFile, SourceLine.</summary>
    public static readonly ErrorCode OverflowAcrossPages = _ / "OverflowAcrossPages";
}
