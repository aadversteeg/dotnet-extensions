using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Ave.Extensions.Documents.Images.Jpeg;

/// <summary>
/// Turns the decoded component planes into 8-bit grey or RGB samples: up-sampling of components stored at a lower
/// resolution, then the YCbCr to RGB transform.
/// </summary>
/// <remarks>
/// <para><b>Up-sampling rule.</b> A component sampled Hi of Hmax times across (and Vi of Vmax down) has its samples at the
/// centres of wider cells: full-resolution pixel x has its centre at component coordinate (x + ½)·Hi/Hmax − ½. The value
/// there is the linear interpolation of the two nearest component samples, in both directions (bilinear, separable),
/// with the edge sample repeated beyond the edge. For the common factor 2 these are the weights ¾ and ¼ (and 9/16, 3/16,
/// 3/16, 1/16 for 2 × 2) of libjpeg's "fancy" up-sampling; libjpeg rounds the interpolated samples to integers before the
/// colour transform and replicates samples for other factors, this decoder keeps the interpolated value exact until the
/// final rounding and interpolates for every factor. That is the difference the tests allow against djpeg.</para>
/// <para><b>Narrow images.</b> A component stored at a lower resolution across whose rows have at most 2 samples is not
/// interpolated but replicated, in both directions: full-resolution pixel x takes component sample ⌊x·Hi/Hmax⌋ (and y
/// likewise). That is what djpeg does, measured from its output (not read from its code): for 4:2:0 and 4:2:2 its result
/// equals its replicating mode (<c>-nosmooth</c>) exactly up to 4 pixels across, and differs from 5 on, whatever the
/// height; for 4:4:0 no such exception shows. With it the narrow test files decode within the same bound as the
/// others.</para>
/// <para><b>Colour transform.</b> Three components are YCbCr unless an Adobe APP14 segment says transform 0 (then they are
/// RGB as they are): this is the rule of PDF's DCTDecode filter (ISO 32000-1 §7.4.8, ColorTransform: default 1 for three
/// components, and "the value provided in the encoded data", the Adobe marker, wins), so the raster shows what a PDF
/// viewer shows of the same image. YCbCr to RGB as JFIF defines it (ITU-T T.871 §7): R = Y + 1.402 (Cr − 128),
/// G = Y − 0.344136286 (Cb − 128) − 0.714136286 (Cr − 128), B = Y + 1.772 (Cb − 128), rounded to the nearest (halves up)
/// and clamped to 0–255.</para>
/// </remarks>
internal static class JpegColor
{
    public static ImmutableArray<byte> Convert(
        int width,
        int height,
        int maxHorizontal,
        int maxVertical,
        List<(byte[] Samples, int Stride, int Width, int Height, int Horizontal, int Vertical)> planes,
        bool transform)
    {
        var channels = planes.Count;
        var output = new byte[(long)width * height * channels];
        var values = new double[channels];
        var columns = new (int Left, int Right, double Share)[channels][];
        var replicated = new bool[channels];
        for (var channel = 0; channel < channels; channel++)
        {
            var plane = planes[channel];
            replicated[channel] = plane.Horizontal < maxHorizontal && plane.Width <= 2;
            columns[channel] = Weights(width, plane.Horizontal, maxHorizontal, plane.Width, replicated[channel]);
        }

        var rows = new (int Top, int Bottom, double Share)[channels];
        for (var y = 0; y < height; y++)
        {
            for (var channel = 0; channel < channels; channel++)
            {
                var plane = planes[channel];
                rows[channel] = Weight(y, plane.Vertical, maxVertical, plane.Height, replicated[channel]);
            }

            for (var x = 0; x < width; x++)
            {
                for (var channel = 0; channel < channels; channel++)
                {
                    var plane = planes[channel];
                    var (left, right, across) = columns[channel][x];
                    var (top, bottom, down) = rows[channel];
                    var upper = Mix(plane.Samples[(top * plane.Stride) + left], plane.Samples[(top * plane.Stride) + right], across);
                    var lower = Mix(plane.Samples[(bottom * plane.Stride) + left], plane.Samples[(bottom * plane.Stride) + right], across);
                    values[channel] = Mix(upper, lower, down);
                }

                var offset = (((long)y * width) + x) * channels;
                if (channels == 1)
                {
                    output[offset] = Round(values[0]);
                }
                else if (transform)
                {
                    var (luma, blue, red) = (values[0], values[1] - 128, values[2] - 128);
                    output[offset] = Round(luma + (1.402 * red));
                    output[offset + 1] = Round(luma - (0.344136286 * blue) - (0.714136286 * red));
                    output[offset + 2] = Round(luma + (1.772 * blue));
                }
                else
                {
                    output[offset] = Round(values[0]);
                    output[offset + 1] = Round(values[1]);
                    output[offset + 2] = Round(values[2]);
                }
            }
        }

        return ImmutableArray.Create(output);
    }

    private static (int Left, int Right, double Share)[] Weights(int size, int factor, int maxFactor, int componentSize, bool replicated)
    {
        var weights = new (int, int, double)[size];
        for (var index = 0; index < size; index++)
        {
            weights[index] = Weight(index, factor, maxFactor, componentSize, replicated);
        }

        return weights;
    }

    /// <summary>The two component samples around full-resolution position <paramref name="index"/> and the share of the
    /// second; when <paramref name="replicated"/>, the one sample whose cell holds the position.</summary>
    private static (int First, int Second, double Share) Weight(int index, int factor, int maxFactor, int componentSize, bool replicated)
    {
        if (factor == maxFactor)
        {
            return (index, index, 0);
        }

        if (replicated)
        {
            var cell = Math.Min((int)((long)index * factor / maxFactor), componentSize - 1);
            return (cell, cell, 0);
        }

        var position = (((index + 0.5) * factor) / maxFactor) - 0.5;
        var first = (int)Math.Floor(position);
        var share = position - first;
        return (Math.Clamp(first, 0, componentSize - 1), Math.Clamp(first + 1, 0, componentSize - 1), share);
    }

    private static double Mix(double first, double second, double share) => share == 0 ? first : first + ((second - first) * share);

    private static byte Round(double value) => (byte)Math.Clamp((int)Math.Floor(value + 0.5), 0, 255);
}
