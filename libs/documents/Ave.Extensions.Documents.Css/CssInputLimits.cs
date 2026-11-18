namespace Ave.Extensions.Documents.Css;

/// <summary>
/// The limits of the CSS reader (docs/architecture.md, "Bounded work"). Tokenizing and parsing are linear in the text;
/// the parser and everything that walks its result (selectors, values, nested rules) recurse into blocks and functions,
/// so their depth is limited.
/// </summary>
public static class CssInputLimits
{
    /// <summary>The longest style sheet: 2^24 UTF-16 code units, the same as an HTML document.</summary>
    public const int MaxCharacters = 1 << 24;

    /// <summary>The deepest nesting of blocks, functions and nested rules: 256. Deeper input is refused
    /// (<see cref="CssErrorCodes.TooDeep"/>), at the first block past the limit.</summary>
    public const int MaxDepth = 256;

    /// <summary>The most style sheets one cascade imports with <c>@import</c>, counted over every import it follows: 1024.
    /// The same sheet imported twice counts twice, so a chain of sheets that each import the next one twice cannot grow
    /// the rule list exponentially. Imports past the limit are refused (<see cref="CssErrorCodes.TooLarge"/>).</summary>
    public const int MaxImportedSheets = 1024;

    /// <summary>The most component values one <c>var()</c> substitution produces for one property: 65536. Custom
    /// properties that each refer to the previous one several times would otherwise grow exponentially; past the limit
    /// the value is refused (<see cref="CssErrorCodes.TooLarge"/>) and the property is unset.</summary>
    public const int MaxSubstitutionValues = 1 << 16;
}
