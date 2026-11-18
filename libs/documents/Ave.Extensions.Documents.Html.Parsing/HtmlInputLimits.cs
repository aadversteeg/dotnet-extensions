namespace Ave.Extensions.Documents.Html.Parsing;

/// <summary>
/// The limits of the HTML parser (docs/architecture.md, "Bounded work"). The parts of the parser that could cost more
/// than the input are bounded as follows, and the nodes it makes by <see cref="MaxElements"/> and
/// <see cref="MaxNodes"/>:
/// <list type="bullet">
/// <item>every walk of the stack of open elements (the "in scope" checks, the adoption agency algorithm) is at most
/// <see cref="MaxDepth"/> entries;</item>
/// <item>the attributes of a tag and of an element are looked up by name in constant time once there are more than
/// eight (duplicate check, the "Noah's Ark" comparison, attribute tests of tree construction, the merge of repeated
/// html and body tags);</item>
/// <item>whether an element is on the list of active formatting elements is a flag, and the entries searched by
/// position lie after the last marker, which are on the stack of open elements after the next reconstruction, so at
/// most <see cref="MaxDepth"/> (markers can stay on the list for ever, as the standard says, and are never
/// walked);</item>
/// <item>selectedcontent elements are filled once per change of their select's selected option that something can
/// see, and every node and character cloned is charged to a budget of 4 per input character, at least 65 536;</item>
/// <item>the tree is frozen and cloned with explicit stacks: no recursion on the input, so the parser runs on a thread
/// with a 256 KB stack;</item>
/// <item>at most <see cref="MaxDiagnostics"/> diagnostics are kept.</item>
/// </list>
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

    /// <summary>The most elements one parse makes: 2^18 (262 144), counting every element the parser makes, also the
    /// formatting elements it reopens, those the adoption agency algorithm makes and the clones in selectedcontent
    /// elements. Every element is styled later, and its computed style is most of its memory: parse plus style
    /// resolution of 2^18 elements that each have a style of their own take about 0.80 GB resident (about 3 KB per
    /// element; measured on a Linux development machine, .NET 10, Release). Past it the parse fails
    /// (<see cref="HtmlErrorCodes.TooLarge"/>).</summary>
    public const int MaxElements = 1 << 18;

    /// <summary>The most nodes one parse makes: 2^20 (1 048 576), elements, text, comments, processing instructions and
    /// the doctype together, clones included. 2^18 elements with a style of their own and three more nodes each (2^20
    /// nodes) take about 0.88 GB resident, so a node that is not an element costs about 100 bytes (measured as above).
    /// Past it the parse fails (<see cref="HtmlErrorCodes.TooLarge"/>).</summary>
    public const int MaxNodes = 1 << 20;

    /// <summary>The most diagnostics one parse keeps: 10 000, far more than a person reads (the corpus documents have
    /// none). Past the limit one <see cref="Core.DocumentErrorCodes.TooManyDiagnostics"/> says how many were left out, so the
    /// memory a text can cost through its errors is a constant, not one diagnostic per character.</summary>
    public const int MaxDiagnostics = 10_000;
}
