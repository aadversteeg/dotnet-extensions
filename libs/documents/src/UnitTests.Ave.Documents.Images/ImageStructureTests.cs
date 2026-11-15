using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;
using Ave.Documents.Images;
using Ave.Documents.Images.Png;

namespace UnitTests.Ave.Documents.Images;

/// <summary>
/// The structure rules of the two formats, beyond their checksums: the PNG chunk order (PNG §5.6) and a JPEG frame
/// followed by its scan (T.81 §B.2.1): a PNG whose chunks are out of order, and a 15-byte JPEG of a frame header
/// without a scan.
/// </summary>
public class ImageStructureTests
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

    private static byte[] Deflate(byte[] raw)
    {
        using var buffer = new MemoryStream();
        using (var zlib = new ZLibStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(raw);
        }

        return buffer.ToArray();
    }

    private static byte[] Png(params byte[][] chunks) => [.. Signature, .. chunks.SelectMany(chunk => chunk)];

    private static readonly byte[] Rgb = Deflate([0, 10, 20, 30, 40, 50, 60]);

    public static TheoryData<string, byte[]> Misordered => new()
    {
        { "IDAT, tEXt, IDAT", Png(Header(2, 1, 8, 2), Chunk("IDAT", Rgb[..5]), Chunk("tEXt", "k\0v"u8.ToArray()), Chunk("IDAT", Rgb[5..]), Chunk("IEND", [])) },
        { "PLTE after IDAT", Png(Header(2, 1, 1, 3), Chunk("IDAT", Deflate([0, 0b0100_0000])), Chunk("PLTE", [1, 2, 3, 4, 5, 6]), Chunk("IEND", [])) },
        { "two IHDR chunks", Png(Header(2, 1, 8, 2), Header(9, 9, 8, 2), Chunk("IDAT", Rgb), Chunk("IEND", [])) },
        { "two PLTE chunks", Png(Header(2, 1, 1, 3), Chunk("PLTE", [1, 2, 3]), Chunk("PLTE", [4, 5, 6]), Chunk("IDAT", Deflate([0, 0])), Chunk("IEND", [])) },
    };

    [Theory]
    [MemberData(nameof(Misordered))]
    public void A_PNG_whose_critical_chunks_are_out_of_order_is_invalid(string _, byte[] file) =>
        ImageDecoder.Decode(file).Error.Code.Should().Be(ImageErrorCodes.Invalid);

    [Fact]
    public void Ancillary_chunks_before_and_after_the_image_data_are_allowed() =>
        ImageDecoder.Decode(Png(Header(2, 1, 8, 2), Chunk("tEXt", "a\0b"u8.ToArray()), Chunk("IDAT", Rgb), Chunk("tEXt", "c\0d"u8.ToArray()), Chunk("IEND", [])))
            .IsSuccess.Should().BeTrue();

    [Fact]
    public void A_JPEG_frame_header_without_a_scan_is_invalid()
    {
        // SOI, SOF0 for a 1 x 1 greyscale image, and nothing after it: 15 bytes, no image data.
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xC0, 0x00, 0x0B, 8, 0, 1, 0, 1, 1, 1, 0x11, 0];

        ImageDecoder.Decode(jpeg).Error.Code.Should().Be(ImageErrorCodes.Invalid);
    }
}
