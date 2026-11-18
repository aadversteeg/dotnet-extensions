using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Extensions.Documents.Fonts.Tables;

/// <summary>
/// Decodes the glyph descriptions of 'glyf' (OpenType spec, "glyf — Glyph data") into contours: simple glyphs from
/// their flags and coordinate deltas, composite glyphs by decoding each component and moving it into place. No
/// hinting: the instructions are skipped, and so is ROUND_XY_TO_GRID, which rounds to the pixel grid only when a glyph
/// is hinted. No variations.
/// </summary>
/// <remarks>
/// <para>The decoder is a reader of input and follows the bounded-work rule of the engine (docs/architecture.md,
/// "Bounded work"): a glyph's data is checked before it is read, and what a composite glyph makes is bounded by fixed,
/// named limits, never by a number the font states. Composites can nest, and a component can be used many times, so
/// without limits a few bytes could ask for an exponential number of points, or refer back to themselves.</para>
/// <list type="bullet">
/// <item><see cref="MaxPoints"/>: the points of one glyph, all components together. TrueType numbers the points of a
/// glyph with 16 bits (endPtsOfContours, and the point numbers a component is matched by), so a glyph cannot address
/// more.</item>
/// <item><see cref="MaxComponents"/>: the component references one glyph visits, nested ones included. This bounds
/// the work for components without points too.</item>
/// <item><see cref="MaxDepth"/>: how deep components nest. A glyph that refers to itself, directly or through other
/// glyphs, reaches it.</item>
/// </list>
/// </remarks>
internal static class GlyphOutlineDecoder
{
    /// <summary>The most points one glyph has, components included: 65 536, the point numbers 16 bits can hold.</summary>
    public const int MaxPoints = 65_536;

    /// <summary>The most component references one glyph visits, nested ones included: 65 536.</summary>
    public const int MaxComponents = 65_536;

    /// <summary>The deepest nesting of composite glyphs: 64 levels, the nesting limit used across the engine.</summary>
    public const int MaxDepth = 64;

    private const string Tag = "glyf";

    // Simple glyph flags (OpenType spec, "glyf", Table "Simple Glyph Flags").
    private const byte OnCurvePoint = 0x01;
    private const byte XShortVector = 0x02;
    private const byte YShortVector = 0x04;
    private const byte RepeatFlag = 0x08;
    private const byte XIsSameOrPositive = 0x10;
    private const byte YIsSameOrPositive = 0x20;
    private const byte ReservedSimpleFlag = 0x80;

    // Composite glyph flags (OpenType spec, "glyf", Table "Component Glyph Flags").
    private const ushort ArgsAreWords = 0x0001;
    private const ushort ArgsAreXyValues = 0x0002;
    private const ushort WeHaveAScale = 0x0008;
    private const ushort MoreComponents = 0x0020;
    private const ushort WeHaveAnXAndYScale = 0x0040;
    private const ushort WeHaveATwoByTwo = 0x0080;
    private const ushort ScaledComponentOffset = 0x0800;
    private const ushort UnscaledComponentOffset = 0x1000;

    /// <summary>Decodes the outline of a glyph.</summary>
    /// <param name="glyphData">The bytes of a glyph in 'glyf', by glyph id (empty for a glyph without an outline).</param>
    /// <param name="glyphCount">The number of glyphs ('maxp'); a component must be one of them.</param>
    /// <param name="glyph">The glyph id.</param>
    /// <returns>The outline, or a <c>Documents.Font.Parse</c> error that names the glyph.</returns>
    public static Result<GlyphShape, Error> Decode(Func<int, ReadOnlyMemory<byte>> glyphData, int glyphCount, int glyph)
    {
        var work = new Work();
        return Points(glyphData, glyphCount, glyph, depth: 0, work).OnSuccessMap(ToShape);
    }

    /// <summary>The decoded points of a glyph and the index of the last point of each contour.</summary>
    private sealed record DecodedGlyph(ImmutableArray<OutlinePoint> Points, ImmutableArray<int> ContourEnds);

    /// <summary>What one glyph has made so far, charged against the limits.</summary>
    private sealed class Work
    {
        public int Points { get; set; }

        public int Components { get; set; }
    }

    private static GlyphShape ToShape(DecodedGlyph decoded)
    {
        var contours = ImmutableArray.CreateBuilder<GlyphContour>(decoded.ContourEnds.Length);
        var start = 0;
        foreach (var end in decoded.ContourEnds)
        {
            contours.Add(new GlyphContour(decoded.Points[start..(end + 1)]));
            start = end + 1;
        }

        return new GlyphShape(contours.MoveToImmutable());
    }

    private static Result<DecodedGlyph, Error> Points(Func<int, ReadOnlyMemory<byte>> glyphData, int glyphCount, int glyph, int depth, Work work)
    {
        var data = glyphData(glyph).Span;
        if (data.IsEmpty)
        {
            return Result<DecodedGlyph, Error>.Success(new DecodedGlyph([], []));
        }

        // The parser checked that every outline holds its 10-byte header (LocaTable.GlyphHeaderLength).
        // "If the number of contours is greater than or equal to zero, this is a simple glyph. If negative, this is a
        // composite glyph" (glyf, glyph headers).
        int contours = BigEndian.Int16(data, 0);
        return contours >= 0
            ? Simple(data, glyph, contours, work)
            : Composite(glyphData, glyphCount, data, glyph, depth, work);
    }

    /// <summary>
    /// A simple glyph (glyf, "Simple glyph description"): endPtsOfContours[numberOfContours], instructionLength,
    /// instructions, then one flag per point (a flag with REPEAT_FLAG is followed by how many times more it applies),
    /// then the x coordinates and the y coordinates, each a delta to the previous point: one unsigned byte with a sign
    /// from the flags when the SHORT_VECTOR bit is set, nothing (the same value) when the SAME_OR_POSITIVE bit is set
    /// without it, an int16 otherwise. The first point's deltas are from (0, 0).
    /// </summary>
    private static Result<DecodedGlyph, Error> Simple(ReadOnlySpan<byte> data, int glyph, int contours, Work work)
    {
        if (contours == 0)
        {
            // No contours: nothing to draw, whatever follows the header.
            return Result<DecodedGlyph, Error>.Success(new DecodedGlyph([], []));
        }

        var position = LocaTable.GlyphHeaderLength;
        if (data.Length < position + 2L * contours + 2)
        {
            return FontParseErrors.TruncatedGlyph<DecodedGlyph>(glyph, position + 2L * contours + 2, data.Length);
        }

        var ends = ImmutableArray.CreateBuilder<int>(contours);
        for (var index = 0; index < contours; index++)
        {
            int end = BigEndian.UInt16(data, position + 2 * index);

            // "Array of point indices for the last point of each contour, in increasing numeric order": every contour
            // has at least one point.
            if (index > 0 && end <= ends[index - 1])
            {
                return Invalid<DecodedGlyph>(glyph, $"the end of contour {index} ({end}) does not come after the end of the contour before it.", end);
            }

            ends.Add(end);
        }

        var count = ends[^1] + 1;
        if (work.Points + (long)count > MaxPoints)
        {
            return TooLarge<DecodedGlyph>(glyph, "points", MaxPoints);
        }

        work.Points += count;
        position += 2 * contours;
        int instructionLength = BigEndian.UInt16(data, position);
        position += 2 + instructionLength;
        if (position > data.Length)
        {
            return FontParseErrors.TruncatedGlyph<DecodedGlyph>(glyph, position, data.Length);
        }

        var flags = new byte[count];
        for (var index = 0; index < count;)
        {
            if (position >= data.Length)
            {
                return FontParseErrors.TruncatedGlyph<DecodedGlyph>(glyph, position + 1L, data.Length);
            }

            var flag = data[position++];
            if ((flag & ReservedSimpleFlag) != 0)
            {
                // Bit 7 "is reserved: set to zero". Some tools give it a meaning (cubic curves), so a glyph that sets it is
                // refused instead of being drawn as something it may not be.
                return Invalid<DecodedGlyph>(glyph, "point flag bit 7 is reserved and must be zero.", flag);
            }

            var repeat = 0;
            if ((flag & RepeatFlag) != 0)
            {
                if (position >= data.Length)
                {
                    return FontParseErrors.TruncatedGlyph<DecodedGlyph>(glyph, position + 1L, data.Length);
                }

                repeat = data[position++];
                if (index + 1 + repeat > count)
                {
                    return Invalid<DecodedGlyph>(glyph, $"a repeated flag runs past the last of its {count} points.", repeat);
                }
            }

            for (var times = 0; times <= repeat; times++)
            {
                flags[index++] = flag;
            }
        }

        var xs = new int[count];
        var ys = new int[count];
        var afterX = Coordinates(data, glyph, flags, xs, XShortVector, XIsSameOrPositive, position);
        if (afterX.IsFailure)
        {
            return Result<DecodedGlyph, Error>.Failure(afterX.Error);
        }

        var afterY = Coordinates(data, glyph, flags, ys, YShortVector, YIsSameOrPositive, afterX.Value);
        if (afterY.IsFailure)
        {
            return Result<DecodedGlyph, Error>.Failure(afterY.Error);
        }

        var points = ImmutableArray.CreateBuilder<OutlinePoint>(count);
        for (var index = 0; index < count; index++)
        {
            points.Add(new OutlinePoint(xs[index], ys[index], (flags[index] & OnCurvePoint) != 0));
        }

        return Result<DecodedGlyph, Error>.Success(new DecodedGlyph(points.MoveToImmutable(), ends.MoveToImmutable()));
    }

    /// <summary>Reads one axis of coordinates: each point's delta, summed from 0. Returns the position after them.</summary>
    private static Result<int, Error> Coordinates(ReadOnlySpan<byte> data, int glyph, byte[] flags, int[] values, byte shortVector, byte sameOrPositive, int position)
    {
        var value = 0;
        for (var index = 0; index < flags.Length; index++)
        {
            var flag = flags[index];
            if ((flag & shortVector) != 0)
            {
                if (position + 1 > data.Length)
                {
                    return FontParseErrors.TruncatedGlyph<int>(glyph, position + 1L, data.Length);
                }

                value += (flag & sameOrPositive) != 0 ? data[position] : -data[position];
                position++;
            }
            else if ((flag & sameOrPositive) == 0)
            {
                if (position + 2 > data.Length)
                {
                    return FontParseErrors.TruncatedGlyph<int>(glyph, position + 2L, data.Length);
                }

                value += BigEndian.Int16(data, position);
                position += 2;
            }

            values[index] = value;
        }

        return Result<int, Error>.Success(position);
    }

    /// <summary>
    /// A composite glyph (glyf, "Composite glyph description"): component records until one without MORE_COMPONENTS.
    /// Each record is flags (uint16), glyphIndex (uint16), two arguments (bytes, or words with ARG_1_AND_2_ARE_WORDS),
    /// then an optional transform: one scale, an x and a y scale, or a 2 × 2 matrix, in F2Dot14. With
    /// ARGS_ARE_XY_VALUES the arguments are a signed offset; without it they are two point numbers: the component is
    /// moved so that its point arg2 lands on point arg1 of the glyph made so far. The component's points are
    /// transformed (x' = a·x + c·y, y' = b·x + d·y, with the matrix read as a, b, c, d) before the point numbers are
    /// matched; an offset is added after the transform unless SCALED_COMPONENT_OFFSET asks for it to be transformed as
    /// well. When neither offset flag is set the offset is not scaled ("On Microsoft and Apple platforms, the default
    /// behavior is the same as when the UNSCALED_COMPONENT_OFFSET flag is set"), and UNSCALED_COMPONENT_OFFSET wins
    /// when both are set.
    /// </summary>
    private static Result<DecodedGlyph, Error> Composite(Func<int, ReadOnlyMemory<byte>> glyphData, int glyphCount, ReadOnlySpan<byte> data, int glyph, int depth, Work work)
    {
        if (depth >= MaxDepth)
        {
            return TooLarge<DecodedGlyph>(glyph, "levels of nested components (or it contains itself)", MaxDepth);
        }

        var points = new List<OutlinePoint>();
        var ends = new List<int>();
        var position = LocaTable.GlyphHeaderLength;
        ushort flags;
        do
        {
            if (data.Length < position + 4)
            {
                return FontParseErrors.TruncatedGlyph<DecodedGlyph>(glyph, position + 4L, data.Length);
            }

            flags = BigEndian.UInt16(data, position);
            int component = BigEndian.UInt16(data, position + 2);
            position += 4;
            var words = (flags & ArgsAreWords) != 0;
            var transformLength = (flags & WeHaveAScale) != 0 ? 2 : (flags & WeHaveAnXAndYScale) != 0 ? 4 : (flags & WeHaveATwoByTwo) != 0 ? 8 : 0;
            var recordLength = (words ? 4 : 2) + transformLength;
            if (data.Length < position + recordLength)
            {
                return FontParseErrors.TruncatedGlyph<DecodedGlyph>(glyph, (long)position + recordLength, data.Length);
            }

            var xy = (flags & ArgsAreXyValues) != 0;
            int argument1 = words ? (xy ? BigEndian.Int16(data, position) : BigEndian.UInt16(data, position)) : (xy ? (sbyte)data[position] : data[position]);
            int argument2 = words ? (xy ? BigEndian.Int16(data, position + 2) : BigEndian.UInt16(data, position + 2)) : (xy ? (sbyte)data[position + 1] : data[position + 1]);
            position += words ? 4 : 2;
            var transform = Transform(data, position, flags);
            position += transformLength;

            if (component >= glyphCount)
            {
                return Invalid<DecodedGlyph>(glyph, $"component glyph {component} is not a glyph of the font ({glyphCount} glyphs).", component);
            }

            if (++work.Components > MaxComponents)
            {
                return TooLarge<DecodedGlyph>(glyph, "component references", MaxComponents);
            }

            var child = Points(glyphData, glyphCount, component, depth + 1, work);
            if (child.IsFailure)
            {
                return child;
            }

            var placed = Place(child.Value.Points, transform, flags, xy, argument1, argument2, points, glyph);
            if (placed.IsFailure)
            {
                return Result<DecodedGlyph, Error>.Failure(placed.Error);
            }

            foreach (var end in child.Value.ContourEnds)
            {
                ends.Add(points.Count + end);
            }

            points.AddRange(placed.Value);
        }
        while ((flags & MoreComponents) != 0);

        return Result<DecodedGlyph, Error>.Success(new DecodedGlyph([.. points], [.. ends]));
    }

    /// <summary>A component's linear transform (a, b, c, d), from the record's F2Dot14 values; null for none.</summary>
    private static (double A, double B, double C, double D)? Transform(ReadOnlySpan<byte> data, int position, ushort flags) =>
        (flags & WeHaveAScale) != 0 ? (F2Dot14(data, position), 0, 0, F2Dot14(data, position))
        : (flags & WeHaveAnXAndYScale) != 0 ? (F2Dot14(data, position), 0, 0, F2Dot14(data, position + 2))
        : (flags & WeHaveATwoByTwo) != 0 ? (F2Dot14(data, position), F2Dot14(data, position + 2), F2Dot14(data, position + 4), F2Dot14(data, position + 6))
        : null;

    /// <summary>F2Dot14: a signed 2.14 fixed-point number (OpenType spec, "Data Types").</summary>
    private static double F2Dot14(ReadOnlySpan<byte> data, int position) => BigEndian.Int16(data, position) / 16384.0;

    /// <summary>The component's points, transformed and moved into place (see <see cref="Composite"/>).</summary>
    private static Result<ImmutableArray<OutlinePoint>, Error> Place(
        ImmutableArray<OutlinePoint> child,
        (double A, double B, double C, double D)? transform,
        ushort flags,
        bool xy,
        int argument1,
        int argument2,
        List<OutlinePoint> made,
        int glyph)
    {
        var points = Transformed(child, transform);
        if (!xy)
        {
            if (argument1 >= made.Count || argument2 >= points.Length)
            {
                return Invalid<ImmutableArray<OutlinePoint>>(
                    glyph,
                    $"a component is matched by points {argument1} and {argument2}, but the glyph so far has {made.Count} points and the component {points.Length}.",
                    $"{argument1},{argument2}");
            }

            return Result<ImmutableArray<OutlinePoint>, Error>.Success(Moved(points, made[argument1].X - points[argument2].X, made[argument1].Y - points[argument2].Y));
        }

        // SCALED_COMPONENT_OFFSET: move first, then transform, so the offset is transformed too.
        var scaled = transform is not null && (flags & ScaledComponentOffset) != 0 && (flags & UnscaledComponentOffset) == 0;
        return Result<ImmutableArray<OutlinePoint>, Error>.Success(scaled
            ? Transformed(Moved(child, argument1, argument2), transform)
            : Moved(points, argument1, argument2));
    }

    /// <summary>x' = a·x + c·y, y' = b·x + d·y; the points unchanged without a transform.</summary>
    private static ImmutableArray<OutlinePoint> Transformed(ImmutableArray<OutlinePoint> points, (double A, double B, double C, double D)? transform) =>
        transform is { } matrix
            ? ImmutableArray.CreateRange(points, point => point with { X = (point.X * matrix.A) + (point.Y * matrix.C), Y = (point.X * matrix.B) + (point.Y * matrix.D) })
            : points;

    /// <summary>The points moved by (dx, dy); unchanged for a zero move, which keeps a coordinate of −0 as it is.</summary>
    private static ImmutableArray<OutlinePoint> Moved(ImmutableArray<OutlinePoint> points, double dx, double dy) =>
        dx == 0 && dy == 0 ? points : ImmutableArray.CreateRange(points, point => point with { X = point.X + dx, Y = point.Y + dy });

    private static Result<T, Error> Invalid<T>(int glyph, string message, object value) =>
        FontParseErrors.InvalidValue<T>(Tag, $"glyph {glyph}: {message}", value);

    private static Result<T, Error> TooLarge<T>(int glyph, string what, int limit) =>
        Invalid<T>(glyph, $"the glyph has more than {limit} {what}; the engine decodes at most {limit}.", limit);
}
