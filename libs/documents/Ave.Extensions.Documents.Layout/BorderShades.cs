using System;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;

namespace Ave.Extensions.Documents.Layout;

/// <summary>
/// The two colours an inset or outset border side is painted in, taken from the side's colour (CSS Backgrounds 3 §3.2:
/// for inset "the colors of the bottom and right borders are lighter, the top and left darker", for outset the other way
/// round; the colours themselves the specification leaves to the user agent). The rule is Chromium 159's as its print
/// shows it, 34 colours with none off (cases <c>bs-colours</c>, <c>bs-colour-bands</c>, <c>bs-widths</c>).
/// </summary>
/// <remarks>
/// With v the largest channel of the colour (0 to 1), the darker colour scales each channel by max(0, (v − 0.33) / v) and
/// the lighter one by min(1, v + 0.33) / v, in single precision, each channel then times the largest float below 256 and
/// truncated to a byte; the lighter colour of black is #545454. Which two of them a side gets depends on the colour's
/// relative luminance Y (sRGB, CSS Color 4 §10.7): up to Y(#202020) the lighter colour and the lighter colour of that, from
/// Y(#ececec) the darker colour and the colour itself, between them the darker and the lighter colour. Y decides, not v:
/// #000060 and #600000 have the same v and fall in different bands (<c>bs-colour-bands</c>). LIMIT: the cases bracket the
/// edges of the bands, not pin them: the dark band ends between Y 0.01444 (#202020, in it) and 0.01521 (#212121, out), the
/// light band starts between 0.83077 (#ebebeb, out) and 0.8388 (#ececec, in); a colour between those is painted by the
/// stated edges, which no measured case confirms.
/// </remarks>
internal static class BorderShades
{
    /// <summary>The largest single-precision number below 256: a channel share of 1 becomes 255, not 256.</summary>
    private static readonly float Scale = MathF.BitDecrement(256f);

    private static readonly double DarkBandEnd = Luminance(new Color(0x20, 0x20, 0x20));

    private static readonly double LightBandStart = Luminance(new Color(0xec, 0xec, 0xec));

    /// <summary>The colour a side is painted in: its own for a solid side; for an inset side the darker colour on the top
    /// and left and the lighter one on the bottom and right, for an outset side the other way round.</summary>
    /// <param name="side">The side.</param>
    /// <param name="isTopOrLeft">Whether it is the top or the left side of its box.</param>
    /// <returns>The colour.</returns>
    public static Color Painted(BorderSide side, bool isTopOrLeft)
    {
        if (side.Style == BorderStyle.Solid)
        {
            return side.Color;
        }

        var (dark, light) = Pair(side.Color);
        return isTopOrLeft == (side.Style == BorderStyle.Inset) ? dark : light;
    }

    /// <summary>The darker and the lighter colour of an inset or outset side, by the band of the colour's luminance.</summary>
    /// <param name="color">The side's colour.</param>
    /// <returns>The colour of the darker sides and that of the lighter sides.</returns>
    public static (Color Dark, Color Light) Pair(Color color)
    {
        var luminance = Luminance(color);
        if (luminance <= DarkBandEnd)
        {
            var light = Lighter(color);
            return (light, Lighter(light));
        }

        return luminance >= LightBandStart ? (Darker(color), color) : (Darker(color), Lighter(color));
    }

    /// <summary>Each channel scaled by max(0, (v − 0.33) / v), v the largest channel; black stays black.</summary>
    internal static Color Darker(Color color)
    {
        var v = Largest(color);
        var share = v == 0 ? 0f : MathF.Max(0f, (v - 0.33f) / v);
        return new Color(Channel(color.Red, share), Channel(color.Green, share), Channel(color.Blue, share));
    }

    /// <summary>Each channel scaled by min(1, v + 0.33) / v, v the largest channel, at most 255; black becomes #545454.</summary>
    internal static Color Lighter(Color color)
    {
        var v = Largest(color);
        if (v == 0)
        {
            return new Color(0x54, 0x54, 0x54);
        }

        var share = MathF.Min(1f, v + 0.33f) / v;
        return new Color(Channel(color.Red, share), Channel(color.Green, share), Channel(color.Blue, share));
    }

    /// <summary>The relative luminance of an sRGB colour (CSS Color 4 §10.7), from 0 for black to 1 for white.</summary>
    internal static double Luminance(Color color)
    {
        static double Linear(byte channel)
        {
            var c = channel / 255.0;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Linear(color.Red) + 0.7152 * Linear(color.Green) + 0.0722 * Linear(color.Blue);
    }

    private static float Largest(Color color) => Math.Max(color.Red, Math.Max(color.Green, color.Blue)) / 255f;

    private static byte Channel(byte channel, float share) => (byte)Math.Min(255, (int)(channel / 255f * share * Scale));
}
