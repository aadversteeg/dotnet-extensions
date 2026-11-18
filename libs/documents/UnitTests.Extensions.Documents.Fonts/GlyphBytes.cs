using Ave.Extensions.Documents.TestSupport;

namespace UnitTests.Extensions.Documents.Fonts;

/// <summary>
/// Hand-made glyph descriptions of 'glyf' (OpenType spec, "glyf — Glyph data") for the outline tests, written byte by
/// byte from the specification, not by the code under test.
/// </summary>
internal static class GlyphBytes
{
    /// <summary>A simple glyph whose points are written with one flag each and int16 deltas (no short vectors, no
    /// repeats): the plainest encoding.</summary>
    /// <param name="contours">The contours, each a list of points (x, y, on-curve).</param>
    /// <returns>The glyph description.</returns>
    public static byte[] Simple(params (int X, int Y, bool On)[][] contours)
    {
        var builder = Header(contours.Length);
        var end = -1;
        foreach (var contour in contours)
        {
            end += contour.Length;
            builder.UInt16(end);
        }

        builder.UInt16(0);
        var points = contours.SelectMany(contour => contour).ToArray();
        builder.Bytes(points.Select(point => (byte)(point.On ? 0x01 : 0x00)));
        var previous = (X: 0, Y: 0);
        foreach (var point in points)
        {
            builder.UInt16(point.X - previous.X);
            previous.X = point.X;
        }

        foreach (var point in points)
        {
            builder.UInt16(point.Y - previous.Y);
            previous.Y = point.Y;
        }

        return builder.ToArray();
    }

    /// <summary>A simple glyph given as its raw parts after the header: contour ends, instructions, flags, coordinate bytes.</summary>
    /// <param name="ends">endPtsOfContours.</param>
    /// <param name="body">Everything after endPtsOfContours: instructionLength, instructions, flags, x and y data.</param>
    /// <returns>The glyph description.</returns>
    public static byte[] Raw(int[] ends, byte[] body)
    {
        var builder = Header(ends.Length);
        foreach (var end in ends)
        {
            builder.UInt16(end);
        }

        return builder.Bytes(body).ToArray();
    }

    /// <summary>One component of a composite glyph.</summary>
    /// <param name="Glyph">The component glyph id.</param>
    /// <param name="Argument1">x offset, or the point of the glyph so far that is matched.</param>
    /// <param name="Argument2">y offset, or the point of the component that is matched.</param>
    /// <param name="Flags">The flags other than MORE_COMPONENTS (set by <see cref="Composite"/>) and ARG_1_AND_2_ARE_WORDS
    /// (always set here).</param>
    /// <param name="Transform">The F2Dot14 values: none, 1 (scale), 2 (x and y scale) or 4 (2 × 2); the flag must match.</param>
    public sealed record Component(int Glyph, int Argument1, int Argument2, int Flags, params int[] Transform);

    public const int ArgsAreXyValues = 0x0002;
    public const int WeHaveAScale = 0x0008;
    public const int WeHaveAnXAndYScale = 0x0040;
    public const int WeHaveATwoByTwo = 0x0080;
    public const int ScaledComponentOffset = 0x0800;
    public const int UnscaledComponentOffset = 0x1000;

    /// <summary>A composite glyph; every component has word arguments.</summary>
    /// <param name="components">The components in order.</param>
    /// <returns>The glyph description.</returns>
    public static byte[] Composite(params Component[] components)
    {
        var builder = Header(-1);
        for (var index = 0; index < components.Length; index++)
        {
            var component = components[index];
            var more = index < components.Length - 1 ? 0x0020 : 0;
            builder.UInt16(component.Flags | more | 0x0001).UInt16(component.Glyph).UInt16(component.Argument1).UInt16(component.Argument2);
            foreach (var value in component.Transform)
            {
                builder.UInt16(value);
            }
        }

        return builder.ToArray();
    }

    /// <summary>F2Dot14 of a value: value × 16384.</summary>
    /// <param name="value">The value, −2 to just under 2.</param>
    /// <returns>The 16-bit encoding.</returns>
    public static int F2Dot14(double value) => (int)Math.Round(value * 16384) & 0xFFFF;

    /// <summary>The 10-byte glyph header: numberOfContours, then a bounding box (not read by the decoder).</summary>
    private static BigEndianBuilder Header(int contours) => new BigEndianBuilder().UInt16(contours).UInt16(0).UInt16(0).UInt16(0).UInt16(0);
}
