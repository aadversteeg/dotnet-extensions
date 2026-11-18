using System;

namespace Ave.Extensions.Documents.Images.Jpeg;

/// <summary>
/// The inverse DCT of one 8 × 8 block (ITU-T T.81 §A.3.3):
/// s(y, x) = ¼ Σu Σv C(u) C(v) S(v, u) cos((2x + 1)uπ/16) cos((2y + 1)vπ/16), with C(0) = 1/√2 and C(k) = 1 otherwise,
/// computed as written (rows, then columns) in double precision, then level-shifted by +128 (§A.3.1), rounded to the
/// nearest integer (halves up) and clamped to 0–255.
/// </summary>
/// <remarks>
/// This is the definition itself, more accurate than the fixed-point transforms of libjpeg (whose "accurate integer"
/// transform is the reference the tests compare with), so samples may differ from it by the rounding of that transform,
/// at most a step or two. Determinism: the cosines are written out as constants (cos(kπ/16), k = 0 … 7) instead of being
/// computed with <see cref="Math.Cos"/>, whose last bit may differ per platform; everything else is multiplication and
/// addition in a fixed order.
/// </remarks>
internal static class InverseDct
{
    /// <summary>cos(kπ/16) for k = 0 … 7.</summary>
    private static readonly double[] Cosines =
    [
        1.0,
        0.98078528040323044912,
        0.92387953251128675613,
        0.83146961230254523708,
        0.70710678118654752440,
        0.55557023301960222474,
        0.38268343236508977173,
        0.19509032201612826785,
    ];

    /// <summary>Basis[x · 8 + u] = ½ · C(u) · cos((2x + 1)uπ/16): the 1-D transform with its factor ½.</summary>
    private static readonly double[] Basis = CreateBasis();

    /// <summary>The zig-zag order (Figure A.6): Natural[k] is the position, row by row, of the k-th coefficient.</summary>
    public static ReadOnlySpan<byte> Natural =>
    [
        0, 1, 8, 16, 9, 2, 3, 10, 17, 24, 32, 25, 18, 11, 4, 5,
        12, 19, 26, 33, 40, 48, 41, 34, 27, 20, 13, 6, 7, 14, 21, 28,
        35, 42, 49, 56, 57, 50, 43, 36, 29, 22, 15, 23, 30, 37, 44, 51,
        58, 59, 52, 45, 38, 31, 39, 46, 53, 60, 61, 54, 47, 55, 62, 63,
    ];

    /// <summary>Transforms dequantised coefficients (row by row) into 64 samples written to <paramref name="output"/>
    /// at <paramref name="offset"/>, rows <paramref name="stride"/> bytes apart.</summary>
    public static void Transform(ReadOnlySpan<double> coefficients, Span<byte> output, int offset, int stride)
    {
        Span<double> rows = stackalloc double[64];
        for (var v = 0; v < 8; v++)
        {
            for (var x = 0; x < 8; x++)
            {
                var sum = 0.0;
                for (var u = 0; u < 8; u++)
                {
                    sum += Basis[(x * 8) + u] * coefficients[(v * 8) + u];
                }

                rows[(v * 8) + x] = sum;
            }
        }

        for (var x = 0; x < 8; x++)
        {
            for (var y = 0; y < 8; y++)
            {
                var sum = 0.0;
                for (var v = 0; v < 8; v++)
                {
                    sum += Basis[(y * 8) + v] * rows[(v * 8) + x];
                }

                output[offset + (y * stride) + x] = (byte)Math.Clamp((int)Math.Floor(sum + 128.5), 0, 255);
            }
        }
    }

    private static double[] CreateBasis()
    {
        var basis = new double[64];
        for (var x = 0; x < 8; x++)
        {
            for (var u = 0; u < 8; u++)
            {
                // cos((2x + 1)uπ/16) from the table: reduce the angle k·π/16 to 0 … 7 with its sign.
                var k = ((2 * x) + 1) * u % 32;
                var sign = 1.0;
                if (k > 16)
                {
                    k = 32 - k;
                }

                if (k > 8)
                {
                    k = 16 - k;
                    sign = -1;
                }

                var cosine = k == 8 ? 0 : sign * Cosines[k];
                basis[(x * 8) + u] = 0.5 * (u == 0 ? Cosines[4] : 1) * cosine;
            }
        }

        return basis;
    }
}
