using System;

namespace Ave.Documents.Typesetting.Shaping;

/// <summary>Which limit of a <see cref="ShapingBudget"/> a text reached.</summary>
internal enum ShapingLimit
{
    /// <summary>The substitutions would make more glyphs than <see cref="GlyphSubstitution.MaxGlyphs"/>.</summary>
    Glyphs,

    /// <summary>The lookups would spend more operations than <see cref="ShapingBudget.MaxOperations"/>.</summary>
    Operations,
}

/// <summary>
/// The limits of shaping ONE text, shared by all its pieces (each script run, and each part between forced line
/// breaks), so that cutting a text into pieces does not multiply them:
/// <list type="bullet">
/// <item>glyphs: the substitutions may turn the text's glyphs into at most <see cref="GlyphSubstitution.MaxGlyphs"/>,
/// counted over all pieces;</item>
/// <item>operations: every glyph a lookup looks at costs one operation — each position of each lookup's walk over the
/// buffer, each subtable tried at a position (the lookup of its coverage, whether it covers the glyph or not), each
/// glyph that a search for a ligature's next component or a pair's second glyph steps over, and each glyph of the one
/// pass that finds the marks' bases — and the text may spend at most <see cref="MaxOperations"/>.</item>
/// </list>
/// The text's own shape costs a fixed few operations per glyph per lookup: every search steps over a run of skipped
/// glyphs at most once per subtable, and a mark finds its base in one step (the bases are found in one pass), so a
/// long run of marks costs in proportion to its length. Spending more than <see cref="OperationsPerGlyph"/> per glyph
/// therefore takes a font whose lookups look at each glyph more often than any real font does: one lookup thousands
/// of times in the lookup list, thousands of subtables in a lookup, or lookups that walk a buffer their own
/// substitutions grew. A font that reaches a limit is malformed or hostile; the text is refused
/// (<see cref="TypesettingErrorCodes.TooManyGlyphs"/>, <see cref="TypesettingErrorCodes.TooMuchWork"/>) instead of
/// costing time and memory without bound. HarfBuzz likewise bounds a buffer by a length limit and an operations limit
/// (hb-limits.hh, applied in <c>hb_buffer_t::enter</c>); it counts its operations its own way, so the numbers below bound
/// the same kind of work, not the same amount.
/// The budget is the state of one call of <see cref="Shaper.Shape"/>; it is created for it and never shared.
/// </summary>
internal sealed class ShapingBudget
{
    /// <summary>Operations a text may spend per glyph it starts with: room for each glyph to be looked at by a thousand
    /// lookups or subtables, far more than the features the shaper applies hold in any real font, while the text itself
    /// costs a fixed few per glyph per lookup (see the class remarks); so the budget grows with the text and refuses
    /// only the font's work. HarfBuzz's HB_BUFFER_MAX_OPS_FACTOR from version 2.7.3 to 11.4.1 (4 096 since 11.4.2).</summary>
    public const int OperationsPerGlyph = 1024;

    /// <summary>The fewest operations any text may spend, so that a short text is not held to 1 024 per glyph: one glyph
    /// that real decompositions turn into a few still has room for every lookup of the font. HarfBuzz's
    /// HB_BUFFER_MAX_OPS_MIN since version 11.4.2.</summary>
    public const int MinOperations = 65_536;

    private long _glyphs;
    private long _operationsLeft;

    /// <summary>A budget for a text of <paramref name="inputLength"/> glyphs.</summary>
    /// <param name="inputLength">The glyphs of the whole text before substitution.</param>
    public ShapingBudget(int inputLength)
    {
        _glyphs = inputLength;
        InputLength = inputLength;
        GlyphLimit = GlyphSubstitution.MaxGlyphs(inputLength);
        OperationLimit = MaxOperations(inputLength);
        _operationsLeft = OperationLimit;
    }

    /// <summary>The most operations shaping a text of <paramref name="inputLength"/> glyphs may spend.</summary>
    /// <param name="inputLength">The glyphs before substitution.</param>
    /// <returns>The limit, computed in long.</returns>
    public static long MaxOperations(int inputLength) => Math.Max(MinOperations, (long)OperationsPerGlyph * inputLength);

    /// <summary>The glyphs of the whole text before substitution.</summary>
    public int InputLength { get; }

    /// <summary>The glyph limit of this text.</summary>
    public long GlyphLimit { get; }

    /// <summary>The operations limit of this text.</summary>
    public long OperationLimit { get; }

    /// <summary>The limit the text reached, or null while it is inside both.</summary>
    public ShapingLimit? Exceeded { get; private set; }

    /// <summary>Spends <paramref name="operations"/>; false once the budget is used up (and from then on).</summary>
    /// <param name="operations">The glyphs about to be looked at.</param>
    /// <returns>Whether the work may be done.</returns>
    public bool Spend(long operations)
    {
        _operationsLeft -= operations;
        if (_operationsLeft < 0)
        {
            Exceeded ??= ShapingLimit.Operations;
        }

        return Exceeded is null;
    }

    /// <summary>Records that a substitution changes the text's glyph count by <paramref name="glyphs"/> (negative for a
    /// ligature); false once the text has more glyphs than its limit (and from then on).</summary>
    /// <param name="glyphs">Glyphs added, less glyphs removed.</param>
    /// <returns>Whether the substitution may be made.</returns>
    public bool Grow(long glyphs)
    {
        _glyphs += glyphs;
        if (_glyphs > GlyphLimit)
        {
            Exceeded ??= ShapingLimit.Glyphs;
        }

        return Exceeded is null;
    }
}
