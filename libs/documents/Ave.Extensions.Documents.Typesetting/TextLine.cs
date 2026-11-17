using System.Linq;
using Ave.Extensions.Documents.Core;

namespace Ave.Extensions.Documents.Typesetting;

/// <summary>One line of a paragraph, as produced by line breaking: consecutive pieces of the input runs.</summary>
/// <param name="Segments">The pieces, left to right.</param>
/// <param name="EndsWithForcedBreak">True when the line ends at a mandatory break (UAX #14 "!") inside the
/// paragraph; such a line is not justified (CSS Text 3 §6.3, <c>text-align-last</c>).</param>
public sealed record TextLine(ValueList<LineSegment> Segments, bool EndsWithForcedBreak = false)
{
    /// <summary>The width of the line's content, in points.</summary>
    public double Width => Segments.Sum(segment => segment.Width);
}

/// <summary>Consecutive glyphs of one input run on one line.</summary>
/// <param name="Run">The index of the run in the list passed to line breaking.</param>
/// <param name="Glyphs">The glyphs, left to right.</param>
public sealed record LineSegment(int Run, ValueList<ShapedGlyph> Glyphs)
{
    /// <summary>The width of the glyphs, in points.</summary>
    public double Width => Glyphs.Sum(glyph => glyph.Advance);
}
