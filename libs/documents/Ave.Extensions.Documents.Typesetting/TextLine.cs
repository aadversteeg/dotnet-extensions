using System;
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
/// <param name="Step">The step the segment's width is rounded up to, in points; 0 (the default) keeps the width as the
/// glyphs' advances sum (<see cref="GreedyLineBreaker"/>, the text item width).</param>
public sealed record LineSegment(int Run, ValueList<ShapedGlyph> Glyphs, double Step = 0)
{
    /// <summary>The width of the segment, in points: its glyphs' advances, rounded up to <see cref="Step"/> when it is
    /// positive.</summary>
    public double Width => RoundedUp(Glyphs.Sum(glyph => glyph.Advance), Step);

    /// <summary>
    /// A width rounded up to a step (none when the step is not positive). A width within a millionth of a step above a
    /// whole number of steps stays on that step: the advances are summed in double precision from values that are whole
    /// steps or single-precision sums, and their last bits must not add a step.
    /// </summary>
    /// <param name="width">The width, in points.</param>
    /// <param name="step">The step, in points.</param>
    /// <returns>The width on the step.</returns>
    public static double RoundedUp(double width, double step) =>
        step > 0 ? Math.Ceiling((width / step) - 1e-6) * step : width;
}
