using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Ave.Extensions.Documents.Images.Png;

/// <summary>
/// Writes 8-bit RGBA pixels as a PNG file (PNG Specification, Second Edition = ISO/IEC 15948:2004), always the same
/// bytes for the same pixels:
/// <list type="bullet">
/// <item>colour type 2 (truecolour) when every pixel is opaque, else 6 (truecolour with alpha); bit depth 8; not
/// interlaced (§11.2.2);</item>
/// <item>chunks IHDR, sRGB (rendering intent 0, perceptual: the samples are sRGB, §11.3.3.5), one IDAT, IEND; nothing
/// that depends on the clock or the machine;</item>
/// <item>filter method 0, the filter type chosen per scanline by the heuristic §12.8 recommends: the type whose filtered
/// bytes, read as signed numbers, have the smallest sum of absolute values; on a tie the lowest type number;</item>
/// <item>the filtered scanlines compressed by the base library's zlib (<see cref="ZLibStream"/>) at
/// <see cref="CompressionLevel.Optimal"/>.</item>
/// </list>
/// </summary>
/// <remarks>
/// The compressed bytes come from the runtime's zlib (in .NET 10 the zlib-ng it ships), so they can differ with another
/// .NET release or another zlib build (a Linux distribution may build .NET against its system zlib); the samples, and so
/// the decoded pixels, cannot. Checked equal on x64 Linux, x64 Windows and arm64 Linux for .NET 10 (docs/architecture.md,
/// "Determinism").
/// </remarks>
internal static class PngEncoder
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    public static ImmutableArray<byte> Encode(int width, int height, ReadOnlySpan<byte> rgba)
    {
        var opaque = true;
        for (var index = 3; index < rgba.Length && opaque; index += 4)
        {
            opaque = rgba[index] == 255;
        }

        var channels = opaque ? 3 : 4;
        var stride = width * channels;
        var line = new byte[stride];
        var previous = new byte[stride];
        var candidate = new byte[stride];
        var best = new byte[stride];

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            for (var y = 0; y < height; y++)
            {
                var row = rgba.Slice(y * width * 4, width * 4);
                for (var x = 0; x < width; x++)
                {
                    row.Slice(x * 4, channels).CopyTo(line.AsSpan(x * channels));
                }

                var bestType = (byte)0;
                var bestSum = long.MaxValue;
                for (byte type = 0; type <= 4; type++)
                {
                    ScanlineFilters.Filter(line, y == 0 ? [] : previous, channels, type, candidate);
                    var sum = 0L;
                    foreach (var value in candidate)
                    {
                        sum += Math.Abs((int)(sbyte)value);
                    }

                    if (sum < bestSum)
                    {
                        (bestSum, bestType) = (sum, type);
                        candidate.CopyTo(best, 0);
                    }
                }

                zlib.WriteByte(bestType);
                zlib.Write(best);
                (line, previous) = (previous, line);
            }
        }

        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)height);
        header[8] = 8;
        header[9] = (byte)(opaque ? 2 : 6);

        using var file = new MemoryStream();
        file.Write(Signature);
        Chunk(file, "IHDR", header);
        Chunk(file, "sRGB", [0]);
        Chunk(file, "IDAT", compressed.ToArray());
        Chunk(file, "IEND", []);
        return ImmutableArray.Create(file.ToArray());
    }

    /// <summary>A chunk (§5.3): length, type, data, and the CRC-32 of type and data.</summary>
    private static void Chunk(MemoryStream file, string type, byte[] data)
    {
        var typeAndData = new byte[4 + data.Length];
        Encoding.ASCII.GetBytes(type, typeAndData);
        data.CopyTo(typeAndData, 4);
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(number, (uint)data.Length);
        file.Write(number);
        file.Write(typeAndData);
        BinaryPrimitives.WriteUInt32BigEndian(number, Crc32.Compute(typeAndData));
        file.Write(number);
    }
}
