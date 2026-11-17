namespace Ave.Extensions.Documents.Typesetting.Shaping;

/// <summary>A mark attached to a base glyph by GPOS mark-to-base.</summary>
/// <param name="BaseIndex">The index of the base glyph in the buffer.</param>
/// <param name="X">Base anchor x minus mark anchor x.</param>
/// <param name="Y">Base anchor y minus mark anchor y.</param>
internal sealed record MarkAttachment(int BaseIndex, int X, int Y);
