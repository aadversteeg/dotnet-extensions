using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;
using Ave.Documents.Images;
using Ave.Documents.Images.Png;

namespace UnitTests.Ave.Documents.Images;

/// <summary>
/// PNG rules that the Pillow-made test files never reach: the mutation run changed each of them without a test
/// failing. The files are built here, byte by byte, so each one hits exactly the case the rule is about.
/// </summary>
public class PngRuleTests
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

    private static byte[] Header(int width, int height, byte bitDepth, byte colorType)
    {
        var data = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(data, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4), (uint)height);
        data[8] = bitDepth;
        data[9] = colorType;
        return Chunk("IHDR", data);
    }

    /// <summary>The image data: the filtered scanlines (each starting with its filter type byte), zlib-compressed.</summary>
    private static byte[] Data(params byte[] scanlines)
    {
        using var buffer = new MemoryStream();
        using (var zlib = new ZLibStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(scanlines);
        }

        return Chunk("IDAT", buffer.ToArray());
    }

    private static byte[] Png(params byte[][] chunks) => [.. Signature, .. chunks.SelectMany(chunk => chunk), .. Chunk("IEND", [])];

    [Fact]
    public void A_paeth_tie_between_above_and_upper_left_picks_above()
    {
        // PNG §9.4, Paeth: "if pa <= pb and pa <= pc then Pr = a else if pb <= pc then Pr = b else Pr = c". For the
        // second pixel of row 1: a (left) = 0, b (above) = 30, c (upper left) = 10, so p = 20, pa = 20, pb = 10, pc = 10:
        // pb and pc tie and b wins. The raw value 100 is filtered as 100 − 30 = 70.
        // Row 1's first pixel: a = 0, b = 10, c = 0 → p = 10, b is closest; raw 0 is filtered as 0 − 10 = 246.
        var file = Png(Header(2, 2, 8, 0), Data(0, 10, 30, 4, 246, 70));

        var result = ImageDecoder.Decode(file);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        ((RasterImage)result.Value).Samples.Should().Equal([10, 30, 0, 100]);
    }

    [Fact]
    public void A_palette_index_equal_to_the_number_of_entries_is_invalid()
    {
        // PNG §11.2.3: "any out-of-range pixel value found in the image data is an error"; indices count from 0, so with
        // two entries index 2 names no entry.
        var file = Png(Header(1, 1, 8, 3), Chunk("PLTE", [1, 2, 3, 4, 5, 6]), Data(0, 2));

        var result = ImageDecoder.Decode(file);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ImageErrorCodes.Invalid);
    }

    [Fact]
    public void The_last_palette_entry_is_a_valid_index()
    {
        var file = Png(Header(1, 1, 8, 3), Chunk("PLTE", [1, 2, 3, 4, 5, 6]), Data(0, 1));

        ((RasterImage)ImageDecoder.Decode(file).Value).Samples.Should().Equal([4, 5, 6]);
    }
}
