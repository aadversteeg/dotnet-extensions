namespace Ave.Extensions.Documents.Css;

/// <summary>
/// The limits of the CSS reader (docs/architecture.md, "Bounded work"). Tokenizing and parsing are linear in the text.
/// The parser and the code that walks its result (rules, media and supports conditions, math functions, var()
/// fallbacks) recurse into blocks and functions, at most <see cref="MaxDepth"/> levels; selector matching recurses at
/// most <see cref="MaxSelectorNesting"/> levels; nothing else recurses on the input. At these depths every path runs on
/// a thread with a 256 KB stack (tested).
/// </summary>
public static class CssInputLimits
{
    /// <summary>The longest style sheet: 2^24 UTF-16 code units, the same as an HTML document.</summary>
    public const int MaxCharacters = 1 << 24;

    /// <summary>The deepest nesting of blocks, functions and nested rules: 64. Real style sheets nest some levels (a
    /// style rule in two at-rules, a calc() in a min()); the recursive code costs up to about 1 KB of stack per level,
    /// so 64 levels fit a 256 KB thread with room to spare. Deeper input is refused
    /// (<see cref="CssErrorCodes.TooDeep"/>), at the first block past the limit.</summary>
    public const int MaxDepth = 64;

    /// <summary>The most style sheets one cascade imports with <c>@import</c>, counted over every import it follows: 1024.
    /// The same sheet imported twice counts twice, so a chain of sheets that each import the next one twice cannot grow
    /// the rule list exponentially. Imports past the limit are refused (<see cref="CssErrorCodes.TooLarge"/>).</summary>
    public const int MaxImportedSheets = 1024;

    /// <summary>The longest value one <c>var()</c> substitution makes, in characters of its text: 2 MiB (2 097 152),
    /// Chromium 159's own limit, measured (a value of 2 097 151 characters is kept, one of 3 342 335 is dropped). Past
    /// it the property is invalid at computed-value time, as in Chromium (CSS Variables 1 §3.1); substitution stops
    /// there, so the work per value is bounded.</summary>
    public const int MaxSubstitutionLength = 1 << 21;

    /// <summary>The most text all <c>var()</c> substitutions of one cascade make together: 64 MiB, 32 times the largest
    /// single value. A real template substitutes some kilobytes per element; past the limit the document is refused
    /// (<see cref="CssErrorCodes.TooLarge"/>).</summary>
    public const long MaxSubstitutionWork = 1L << 26;

    /// <summary>The deepest nesting of selectors: 32 levels of :is(), :where(), :not(), :has(), :nth-child(of S),
    /// :host() and ::slotted() arguments and of nested style rules (CSS Nesting 1) together. Selector matching recurses
    /// once per level (and nothing else in it recurses), so this bounds its stack use; at 32 every path runs on a
    /// 256 KB stack with room (tested). A deeper selector refuses the rule (<see cref="CssErrorCodes.TooDeep"/>).</summary>
    public const int MaxSelectorNesting = 32;

    /// <summary>The most matching and collecting one cascade does: 2^26 units (one unit is one simple selector tested,
    /// one fact remembered, one matched rule or declaration collected). Measured on the development machine (Release):
    /// 1 000 rules that match every one of 10 000 elements reach it after about 3 s, while 1 000 such rules against
    /// 5 000 elements (1.7 s) and 100 000 class rules against one element with 100 000 classes (1.9 s) stay inside it.
    /// A style sheet and document that need more (many rules that no index can narrow, times many elements) refuse the
    /// document (<see cref="CssErrorCodes.TooLarge"/>) instead of running for minutes.</summary>
    public const long MaxMatchingWork = 1L << 26;

    /// <summary>The most matching facts one cascade remembers: 2^22, a bound on the memory of matching. Past it facts
    /// are computed again when needed, inside <see cref="MaxMatchingWork"/>.</summary>
    public const int MaxMatchingMemo = 1 << 22;

    /// <summary>The largest computed length the engine keeps, in px (and the largest percentage): 2^24. Chromium 159
    /// clamps computed lengths to its layout unit's range, about ±3.36e7 px (measured: <c>width: 99999999999px</c>
    /// computes to 3.35544e+07px, review 39 F9 and CR-13); the engine refuses a length past 2^24 px (about 4.4 km)
    /// instead of copying a clamp it cannot see exactly, and nothing on paper comes near it.</summary>
    public const double MaxLengthPixels = 1 << 24;

    /// <summary>The most diagnostics one reading of a style sheet, one cascade or one HTML resolution keeps: 10 000.
    /// Past it one <see cref="Core.DocumentErrorCodes.TooManyDiagnostics"/> says how many were left out, and the document is
    /// refused: every diagnostic costs memory, and hostile input can make one per few characters.</summary>
    public const int MaxDiagnostics = 10_000;
}
