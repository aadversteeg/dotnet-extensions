using System;

namespace Ave.Documents.Images.Png;

/// <summary>
/// Undoes PNG filter method 0 (PNG §9 "Filtering"). Every scanline starts with one filter-type byte; the encoder
/// replaced each byte x by x minus a prediction from its neighbours, and the decoder adds the prediction back
/// (§9.2, Table 9.1): a = the byte one pixel to the left, b = the byte above, c = the byte above and to the left,
/// all zero outside the image. Arithmetic is modulo 256.
/// </summary>
internal static class ScanlineFilters
{
    /// <summary>
    /// Unfilters the decompressed data in place, scanline by scanline.
    /// </summary>
    /// <param name="data">Height × (1 + stride) bytes: each scanline's filter type and its filtered bytes.</param>
    /// <param name="stride">The bytes of one scanline without the filter-type byte.</param>
    /// <param name="bytesPerPixel">"bpp" of §9.2: bytes per complete pixel, rounded up to 1 for bit depths below 8.</param>
    /// <returns>The index of the first scanline with an undefined filter type, or −1 when all are valid.</returns>
    public static int Unfilter(Span<byte> data, int stride, int bytesPerPixel)
    {
        var rows = data.Length / (stride + 1);
        for (var row = 0; row < rows; row++)
        {
            var line = data.Slice(row * (stride + 1) + 1, stride);
            var previous = row == 0 ? Span<byte>.Empty : data.Slice((row - 1) * (stride + 1) + 1, stride);
            var filterType = data[row * (stride + 1)];
            if (filterType > 4)
            {
                return row;
            }

            for (var index = 0; index < stride; index++)
            {
                var a = index >= bytesPerPixel ? line[index - bytesPerPixel] : 0;
                var b = previous.IsEmpty ? 0 : previous[index];
                var c = index >= bytesPerPixel && !previous.IsEmpty ? previous[index - bytesPerPixel] : 0;
                line[index] = (byte)(line[index] + Prediction(filterType, a, b, c));
            }
        }

        return -1;
    }

    /// <summary>The prediction of each filter type (Table 9.1): None, Sub, Up, Average, Paeth.</summary>
    private static int Prediction(byte filterType, int a, int b, int c) => filterType switch
    {
        0 => 0,
        1 => a,
        2 => b,
        3 => (a + b) / 2,
        _ => Paeth(a, b, c),
    };

    /// <summary>
    /// The Paeth predictor (§9.4): estimate p = a + b − c, then pick whichever of a, b, c is closest to p, preferring
    /// a, then b, on ties — in exactly this order, as the specification requires.
    /// </summary>
    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }
}
