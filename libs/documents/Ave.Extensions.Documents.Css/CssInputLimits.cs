namespace Ave.Extensions.Documents.Css;

/// <summary>
/// The limits of the CSS reader and of the cascade (docs/architecture.md, "Bounded work"). The parser and the code that
/// walks its result (rules, media and supports conditions, math functions, values made by var() substitution) recurse
/// into blocks and functions at most <see cref="MaxDepth"/> levels; selector matching recurses at most
/// <see cref="MaxSelectorNesting"/> levels; @import chains and the styling of the element tree use explicit stacks. At
/// the deepest allowed input every one of them runs on a thread with a 256 KB stack (tested). A limit that is reached
/// ends the work: the reading or the style resolution fails with the limit's error.
/// </summary>
public static class CssInputLimits
{
    /// <summary>The longest style sheet: 2^24 UTF-16 code units, the same as an HTML document.</summary>
    public const int MaxCharacters = 1 << 24;

    /// <summary>The deepest nesting of blocks, functions and nested rules: 64, also for a value that var()
    /// substitution makes. Real style sheets nest some levels (a style rule in two at-rules, a calc() in a min()); 64
    /// levels run on a 256 KB thread (tested). Deeper input fails the reading, and a deeper substituted value the style
    /// resolution (<see cref="CssErrorCodes.TooDeep"/>).</summary>
    public const int MaxDepth = 64;

    /// <summary>The most style sheets one cascade takes in: 1024, the document's own (style elements, linked sheets, the
    /// user-agent sheet) and those they import together. A sheet taken in twice counts twice, so a chain of sheets that
    /// each import the next one twice cannot grow the rule list exponentially. Past it the cascade stops and the style
    /// resolution fails (<see cref="CssErrorCodes.TooLarge"/>).</summary>
    public const int MaxStyleSheets = 1024;

    /// <summary>The most entries one cascade's index holds: 2^21 (2 097 152). An entry is one complex selector of a style
    /// rule or one declaration the cascade keeps for a property, counted every time a sheet is taken in, so a sheet
    /// linked or imported many times costs its size each time. Past it the cascade stops and the style resolution fails
    /// (<see cref="CssErrorCodes.TooLarge"/>).</summary>
    public const int MaxCascadeEntries = 1 << 21;

    /// <summary>The longest value one <c>var()</c> substitution makes, in characters of its text: 2 MiB (2 097 152),
    /// Chromium 159's own limit, measured (a value of 2 097 151 characters is kept, one of 3 342 335 is dropped). Past
    /// it the property is invalid at computed-value time, as in Chromium (CSS Variables 1 §3.1); substitution stops
    /// there. Within 8 characters per substitution below it the engine's measure cannot tell, and the declaration is
    /// refused.</summary>
    public const int MaxSubstitutionLength = 1 << 21;

    /// <summary>The most text all <c>var()</c> substitutions of one cascade make together: 8 MiB (8 388 608 characters),
    /// 4 times the largest single value. Past the limit the style resolution fails (<see cref="CssErrorCodes.TooLarge"/>).
    /// Set from measurement: the costliest shape found, elements that each read a large shared value beside a custom
    /// property of their own (so no two share a substitution) and keep it as their font-family, ends at this limit
    /// within 3.4 s of user time and 0.6 GB (131,074 elements and a value of 256 characters; a sheet of 1.5 KB with a value
    /// of 1 MiB: 0.9 s, 0.33 GB); the documents of the family's corpora use at most 58 characters.</summary>
    public const long MaxSubstitutionWork = 1L << 23;

    /// <summary>The deepest nesting of selectors: 32 levels of :is(), :where(), :not(), :has(), :nth-child(of S),
    /// :host() and ::slotted() arguments and of nested style rules (CSS Nesting 1) together. Selector matching recurses
    /// once per level (and nothing else in it recurses), so this bounds its stack use; at 32 it runs on a 256 KB stack
    /// (tested). A deeper selector fails the style resolution (<see cref="CssErrorCodes.TooDeep"/>).</summary>
    public const int MaxSelectorNesting = 32;

    /// <summary>The most work one cascade does: 2^26 units. A unit is one simple selector tested, one matching fact
    /// computed, one sibling counted for <c>:nth-child(of S)</c>, 64 characters a name or value test compares, one rule
    /// or declaration collected, one comparison of sorting the candidates (n log2 n per property), one candidate the
    /// cascade looks at for a winner, and one var() reference or custom property the dependency walk visits. Every loop
    /// whose length the input sets is charged per step, so this number bounds the cascade's work (the places that
    /// charge are pinned by tests). A selector whose cost has no linear bound (a chain of next-sibling combinators over
    /// different compounds costs its steps times the elements) is charged what it costs. Past it the cascade stops and
    /// the style resolution fails (<see cref="CssErrorCodes.TooLarge"/>).</summary>
    public const long MaxCascadeWork = 1L << 26;

    /// <summary>The most entries one cascade remembers to avoid doing work twice: 2^22 (matching facts, the matches of a
    /// parent rule's selectors, the sibling tables of <c>:nth-child(of S)</c>, var() substitutions and custom-property
    /// tables). Past it results are computed again when needed, inside <see cref="MaxCascadeWork"/>.</summary>
    public const int MaxCascadeMemo = 1 << 22;

    /// <summary>The largest computed length the engine keeps, in px (and the largest percentage): 2^24. Chromium 159
    /// clamps computed lengths to its layout unit's range, about ±3.36e7 px (measured: <c>width: 99999999999px</c>
    /// computes to 3.35544e+07px); the engine refuses a length past 2^24 px (about 4.4 km)
    /// instead of copying a clamp it cannot see exactly, and nothing on paper comes near it.</summary>
    public const double MaxLengthPixels = 1 << 24;

    /// <summary>The most diagnostics one reading of a style sheet, one cascade or one HTML resolution keeps: 10 000.
    /// Past it one <see cref="Core.DocumentErrorCodes.TooManyDiagnostics"/> says how many were left out, how many of them
    /// are refusals and the first of those, and the document is refused: every diagnostic costs memory, and hostile
    /// input can make one per few characters.</summary>
    public const int MaxDiagnostics = 10_000;
}
