namespace Ave.Extensions.Documents.Html.Parsing;

/// <summary>
/// The limits of the HTML parser (docs/architecture.md, "Bounded work"). The parser's work is linear in its input:
/// every step of tree construction that walks the stack of open elements (the "in scope" checks, the adoption agency
/// algorithm) walks at most <see cref="MaxDepth"/> entries, and the list of active formatting elements holds at most
/// three entries of one kind between two markers (§13.2.4.3, "Noah's Ark clause").
/// </summary>
public static class HtmlInputLimits
{
    /// <summary>The longest text the parser reads: 2^24 UTF-16 code units (16 MiB of characters). Bytes are checked
    /// against the same number before they are decoded, since every code unit takes at least one byte.</summary>
    public const int MaxCharacters = 1 << 24;

    /// <summary>The most elements open at once: 512, the depth at which Chromium's tree builder stops nesting. Chromium
    /// then appends deeper elements to the element at that depth (a flatter tree than the standard's); this parser
    /// refuses the document instead (<see cref="HtmlErrorCodes.TooDeep"/>), so it is never silently different.</summary>
    public const int MaxDepth = 512;
}
