using Ave.Documents.Core;
using Ave.Extensions.ErrorPaths;

namespace Ave.Documents.Layout;

/// <summary>Errors from layout.</summary>
public static class LayoutErrorCodes
{
    /// <summary><c>Documents.Layout</c>.</summary>
    public static readonly ErrorCode _ = DocumentErrorCodes._ / "Layout";

    /// <summary>Content that does not fit where it is and cannot be broken, with nothing before it on the page that
    /// could move to a later page: on an empty page, or — LIMIT (NF-1, docs/architecture.md, Known limits) — after only
    /// leading space (margins, empty boxes) at the document start or after a forced page break; also header or footer
    /// content that does not fit in its area. Metadata: Node, Value (the room it had, in points), SourceFile,
    /// SourceLine.</summary>
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
}
