namespace Ave.Extensions.Documents.Core;

/// <summary>
/// The optional OpenType features a text may switch on (CSS Fonts 4 §6.7, <c>font-variant-numeric</c>, and §7.1,
/// <c>font-feature-settings</c>): lining, oldstyle, proportional and tabular figures and ordinal forms. One list for the
/// style sheet, the document model and the shaper. Measured in Lato, whose lookups for them are single substitutions and
/// whose default figures are tabular and lining (cases <c>fn-numeric</c>, <c>fn-table</c> of
/// <c>tools/reference/chromium</c>).
/// </summary>
public static class OptionalFontFeatures
{
    /// <summary>The tags, in the order a run lists them: <c>lnum</c>, <c>onum</c>, <c>pnum</c>, <c>tnum</c>,
    /// <c>ordn</c>.</summary>
    public static ValueList<string> Tags { get; } = ["lnum", "onum", "pnum", "tnum", "ordn"];
}
