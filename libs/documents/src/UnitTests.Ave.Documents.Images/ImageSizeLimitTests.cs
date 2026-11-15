using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;
using Ave.Documents.Images;
using Ave.Documents.Images.Png;

namespace UnitTests.Ave.Documents.Images;

/// <summary>
/// A PNG header may announce up to 2^31 − 1 × 2^31 − 1 pixels in 13 bytes. The decoder refuses an image above its
/// stated limit (<see cref="ImageLimits.MaxPixels"/>) with an error value, before it allocates anything for it.
/// </summary>
public class ImageSizeLimitTests
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    private static byte[] Chunk(string type, byte[] data)
    {
        var chunk = new byte[12 + data.Length];
        BinaryPrimitives.WriteUInt32BigEndian(chunk, (uint)data.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(chunk, 4);
        data.CopyTo(chunk, 8);
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(8 + data.Length), Crc32.Compute(chunk.AsSpan(4, 4 + data.Length)));
        return chunk;
    }

    private static byte[] Header(uint width, uint height, byte bitDepth, byte colorType)
    {
        var data = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(data, width);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4), height);
        data[8] = bitDepth;
        data[9] = colorType;
        return Chunk("IHDR", data);
    }

    private static byte[] Deflate(long length)
    {
        using var buffer = new MemoryStream();
        using (var zlib = new ZLibStream(buffer, CompressionLevel.Fastest, leaveOpen: true))
        {
            var zeros = new byte[1 << 20];
            for (var left = length; left > 0; left -= zeros.Length)
            {
                zlib.Write(zeros, 0, (int)Math.Min(left, zeros.Length));
            }
        }

        return buffer.ToArray();
    }

    private static byte[] Png(params byte[][] chunks) => [.. Signature, .. chunks.SelectMany(chunk => chunk)];

    /// <summary>A 1-bit indexed PNG of zero pixels: a few kilobytes for a very large image.</summary>
    private static byte[] OneBitIndexed(int width, int height) =>
        Png(Header((uint)width, (uint)height, 1, 3), Chunk("PLTE", [1, 2, 3]), Chunk("IDAT", Deflate((long)height * ((width + 7) / 8 + 1))), Chunk("IEND", []));

    private static void ShouldBeTooLarge(byte[] file)
    {
        var allocated = GC.GetAllocatedBytesForCurrentThread();

        var result = ImageDecoder.Decode(file);

        (GC.GetAllocatedBytesForCurrentThread() - allocated).Should().BeLessThan(16L * 1024 * 1024, "nothing is decoded for an image above the limit");
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ImageErrorCodes.TooLarge, result.Error.ToString());
    }

    [Fact]
    public void A_header_of_the_largest_size_PNG_allows_is_an_error_not_an_overflow() =>
        ShouldBeTooLarge(Png(Header(0x7FFFFFFF, 0x7FFFFFFF, 8, 6), Chunk("IDAT", [0x78, 0x9C, 0x03, 0x00, 0x00, 0x00, 0x00, 0x01]), Chunk("IEND", [])));

    [Theory]
    [InlineData(30000, 30000)]
    [InlineData(12000, 12000)]
    public void A_small_one_bit_PNG_of_a_huge_image_is_refused_by_its_pixel_count(int width, int height) =>
        ShouldBeTooLarge(OneBitIndexed(width, height));

    [Fact]
    public void An_image_at_the_limit_is_decoded()
    {
        var side = (int)Math.Sqrt(ImageLimits.MaxPixels);

        var result = ImageDecoder.Decode(OneBitIndexed(side, side));

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        ((RasterImage)result.Value).PixelWidth.Should().Be(side);
    }
}
