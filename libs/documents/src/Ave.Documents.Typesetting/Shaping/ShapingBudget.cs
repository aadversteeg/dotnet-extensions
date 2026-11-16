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
/// buffer, and each glyph that a search for a ligature's next component, a pair's second glyph or a mark's base steps
/// over — and the text may spend at most <see cref="MaxOperations"/>.</item>
/// </list>
/// The glyph limit alone does not bound the work: a lookup list may hold one lookup thousands of times, and every one of
/// them walks the whole buffer. A font that reaches a limit is malformed or hostile; the text is refused
/// (<see cref="TypesettingErrorCodes.TooManyGlyphs"/>, <see cref="TypesettingErrorCodes.TooMuchWork"/>) instead of
/// costing time and memory without bound. HarfBuzz likewise bounds a buffer by a length limit and an operations limit.
/// The budget is the state of one call of <see cref="Shaper.Shape"/>; it is created for it and never shared.
/// </summary>
internal sealed class ShapingBudget
{
    /// <summary>Operations a text may spend per glyph it starts with: room for each glyph to be looked at by a thousand
    /// lookups, far more than the features the shaper applies hold in any real font.</summary>
    public const int OperationsPerGlyph = 1024;

    /// <summary>The fewest operations any text may spend, so that a short text is not held to 1 024 per glyph: one glyph
    /// that real decompositions turn into a few still has room for every lookup of the font.</summary>
    public const int MinOperations = 65_536;

    private long _glyphs;
    private long _operationsLeft;

    /// <summary>A budget for a text of <paramref name="inputLength"/> glyphs.</summary>
    /// <param name="inputLength">The glyphs of the whole text before substitution.</param>
    public ShapingBudget(int inputLength)
    {
        _glyphs = inputLength;
        GlyphLimit = GlyphSubstitution.MaxGlyphs(inputLength);
        OperationLimit = MaxOperations(inputLength);
        _operationsLeft = OperationLimit;
    }

    /// <summary>The most operations shaping a text of <paramref name="inputLength"/> glyphs may spend.</summary>
    /// <param name="inputLength">The glyphs before substitution.</param>
    /// <returns>The limit, computed in long.</returns>
    public static long MaxOperations(int inputLength) => Math.Max(MinOperations, (long)OperationsPerGlyph * inputLength);

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
