using System.Collections.Immutable;
using System.Linq;

namespace Ave.Documents.Typesetting;

/// <summary>One line of a paragraph, as produced by line breaking: consecutive pieces of the input runs.</summary>
/// <param name="Segments">The pieces, left to right.</param>
public sealed record TextLine(ImmutableArray<LineSegment> Segments)
{
    /// <summary>The width of the line's content, in points.</summary>
    public double Width => Segments.Sum(segment => segment.Width);
}

/// <summary>Consecutive glyphs of one input run on one line.</summary>
/// <param name="Run">The index of the run in the list passed to line breaking.</param>
/// <param name="Glyphs">The glyphs, left to right.</param>
public sealed record LineSegment(int Run, ImmutableArray<ShapedGlyph> Glyphs)
{
    /// <summary>The width of the glyphs, in points.</summary>
    public double Width => Glyphs.Sum(glyph => glyph.Advance);
}
