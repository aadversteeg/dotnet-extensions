namespace Ave.Documents.Fluent;

/// <summary>The limits the code front-end sets on a document, beyond the rules of CSS.</summary>
public static class InputLimits
{
    /// <summary>
    /// How deep elements may nest: a block in a block in a table cell … at most 64 levels, the document's own list
    /// counting as level 1. Each level costs the layout one call frame, so without a limit a deep enough document ends
    /// the process with a stack overflow; 64 is far beyond any report or letter and costs a few milliseconds to lay
    /// out. Deeper nesting is <c>Documents.Input.Invalid.TooDeep</c>.
    /// </summary>
    public const int MaxNestingDepth = 64;
}
