using Ave.Documents.Core;
using Ave.Extensions.ErrorPaths;

namespace Ave.Documents.Layout;

/// <summary>Errors from layout.</summary>
public static class LayoutErrorCodes
{
    /// <summary><c>Documents.Layout</c>.</summary>
    public static readonly ErrorCode _ = DocumentErrorCodes._ / "Layout";

    /// <summary>Content that can never fit on a page, not even on an empty one, and cannot be broken.
    /// Metadata: Node, SourceFile, SourceLine.</summary>
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
