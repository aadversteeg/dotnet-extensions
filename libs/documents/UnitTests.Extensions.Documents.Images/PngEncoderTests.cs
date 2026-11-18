using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Ave.Extensions.Documents.Images;
using Ave.Extensions.Documents.TestSupport;

namespace UnitTests.Extensions.Documents.Images;

/// <summary>
/// The PNG encoder: every pixel comes back from the engine's decoder and from Pillow, the chunks and the filter of each
/// scanline follow the stated rule, and the bytes are pinned (the tests run on Linux and Windows, so a change of the
/// zlib output on either shows here).
/// </summary>
public class PngEncoderTests
{
    private static byte[] Pixels(int width, int height, Func<int, int, (byte R, byte G, byte B, byte A)> pixel)
    {
        var bytes = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var (r, g, b, a) = pixel(x, y);
                var offset = ((y * width) + x) * 4;
                (bytes[offset], bytes[offset + 1], bytes[offset + 2], bytes[offset + 3]) = (r, g, b, a);
            }
        }

        return bytes;
    }

    private static byte[] Photo(int width, int height, bool opaque) =>
        Pixels(width, height, (x, y) => ((byte)(x * 7 % 256), (byte)((x * y) % 256), (byte)(y * 13 % 256), opaque ? (byte)255 : (byte)((x + y) * 9 % 256)));

    /// <summary>The chunks of a PNG file: type and data, in order.</summary>
    private static List<(string Type, byte[] Data)> Chunks(byte[] file)
    {
        var chunks = new List<(string, byte[])>();
        for (var offset = 8; offset < file.Length;)
        {
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(offset));
            chunks.Add((Encoding.ASCII.GetString(file, offset + 4, 4), file.AsSpan(offset + 8, length).ToArray()));
            offset += 12 + length;
        }

        return chunks;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Every_pixel_comes_back_from_the_decoder(bool opaque)
    {
        var pixels = Photo(37, 23, opaque);

        var file = ImageEncoder.EncodePng(37, 23, pixels);
        var image = (RasterImage)ImageDecoder.Decode(file.AsMemory()).Value;

        image.ColorSpace.Should().Be(ImageColorSpace.Rgb);
        image.Alpha.HasValue.Should().Be(!opaque, "an opaque image is written without an alpha channel");
        for (var index = 0; index < 37 * 23; index++)
        {
            image.Samples.Skip(index * 3).Take(3).Should().Equal(pixels.Skip(index * 4).Take(3));
            if (!opaque)
            {
                image.Alpha!.Value[index].Should().Be(pixels[(index * 4) + 3]);
            }
        }
    }

    [Fact]
    public void The_file_has_the_stated_chunks_and_header()
    {
        var file = ImageEncoder.EncodePng(5, 3, Photo(5, 3, opaque: false)).ToArray();

        var chunks = Chunks(file);

        chunks.Select(chunk => chunk.Type).Should().Equal("IHDR", "sRGB", "IDAT", "IEND");
        chunks[0].Data.Should().Equal(0, 0, 0, 5, 0, 0, 0, 3, 8, 6, 0, 0, 0);
        chunks[1].Data.Should().Equal(0);
        Chunks(ImageEncoder.EncodePng(5, 3, Photo(5, 3, opaque: true)).ToArray())[0].Data[9].Should().Be(2, "opaque pixels are written as truecolour");
    }

    [Fact]
    public void Each_scanline_gets_the_filter_with_the_smallest_sum_of_absolute_values()
    {
        // Row 0: a ramp across (Sub leaves 7s, the smallest). Row 1: the same ramp (Up leaves zeros). Row 2: all 200
        // (None sums 3 × 4 × 56, Sub 56s only in the first pixel: Sub wins; Up has the ramp's differences). Row 3: a
        // pattern that only None keeps small: alternating 0 and 1.
        var pixels = Pixels(4, 4, (x, y) => y switch
        {
            0 or 1 => ((byte)(x * 7), (byte)(x * 7), (byte)(x * 7), 255),
            2 => (200, 200, 200, 255),
            _ => ((byte)(x % 2), (byte)(x % 2), (byte)(x % 2), 255),
        });

        var data = Chunks(ImageEncoder.EncodePng(4, 4, pixels).ToArray()).Single(chunk => chunk.Type == "IDAT").Data;
        using var zlib = new ZLibStream(new MemoryStream(data), CompressionMode.Decompress);
        var raw = new byte[4 * (1 + 12)];
        zlib.ReadExactly(raw);

        Enumerable.Range(0, 4).Select(row => raw[row * 13]).Should().Equal(1, 2, 1, 0);
    }

    [Fact]
    public void The_same_pixels_give_the_same_bytes_on_every_platform()
    {
        var file = ImageEncoder.EncodePng(64, 48, Photo(64, 48, opaque: false));

        ImageEncoder.EncodePng(64, 48, Photo(64, 48, opaque: false)).Should().Equal(file);
        Convert.ToHexStringLower(SHA256.HashData(file.AsSpan())).Should().Be("39d1221ab6d3a18b127d79c091dd9ca95ca89ca799963afce256e251d1b0540a");
    }

    [Fact]
    public void Invalid_sizes_are_a_bug_of_the_caller()
    {
        FluentActions.Invoking(() => ImageEncoder.EncodePng(0, 1, [])).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => ImageEncoder.EncodePng(2, 2, new byte[15])).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => ImageEncoder.EncodePng(1 << 14, (1 << 12) + 1, [])).Should().Throw<ArgumentOutOfRangeException>();
    }

    [DockerFact]
    [Trait("Category", "Reference")]
    public void Pillow_reads_back_every_pixel()
    {
        var folder = Directory.CreateTempSubdirectory("ave-extensions-documents-png-").FullName;
        try
        {
            var pixels = Photo(37, 23, opaque: false);
            File.WriteAllBytes(Path.Combine(folder, "image.png"), [.. ImageEncoder.EncodePng(37, 23, pixels)]);
            var (exitCode, output) = Docker.Run(
                ["run", "--rm", .. Docker.UserArguments, "-v", $"{folder}:/work", ReferenceTools.Image, "python3", "-I", "-c",
                    "from PIL import Image; image = Image.open('/work/image.png'); image.load(); "
                    + "assert image.mode == 'RGBA', image.mode; open('/work/pixels', 'wb').write(image.tobytes())"],
                TimeSpan.FromMinutes(2));

            exitCode.Should().Be(0, output);
            File.ReadAllBytes(Path.Combine(folder, "pixels")).Should().Equal(pixels);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
