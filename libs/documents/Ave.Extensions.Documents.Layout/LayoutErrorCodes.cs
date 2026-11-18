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

    /// <summary>Computed content (the page count) that does not settle: every layout pass gives a different
    /// number of pages, up to the pass limit. Metadata: Node, Value (the page counts per pass), SourceFile,
    /// SourceLine of the first page-count reference.</summary>
    public static readonly ErrorCode NotConverging = _ / "NotConverging";
}
