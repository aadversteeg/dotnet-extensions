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

    /// <summary>A defect of the engine, never a property of the document: a layout algorithm broke the contract every
    /// algorithm keeps (it made no progress from one page to the next, reported no room at the start of a page, or did not
    /// break at the early break it reported). Reported instead of an exception, so pagination always ends and the caller
    /// gets an answer; a document that shows it is worth a bug report. Metadata: Value (which contract), Node.</summary>
    public static readonly ErrorCode EngineDefect = _ / "EngineDefect";
}
