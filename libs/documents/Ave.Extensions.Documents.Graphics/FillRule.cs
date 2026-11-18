namespace Ave.Extensions.Documents.Graphics;

/// <summary>Which points a filled path covers (ISO 32000-1 §8.5.3.3).</summary>
public enum FillRule
{
    /// <summary>A point is inside when the path winds around it a number of times other than zero (§8.5.3.3.2). TrueType
    /// glyphs are filled with this rule.</summary>
    NonZero,

    /// <summary>A point is inside when the path winds around it an odd number of times (§8.5.3.3.3).</summary>
    EvenOdd,
}
