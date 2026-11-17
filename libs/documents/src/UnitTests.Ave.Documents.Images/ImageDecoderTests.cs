using System.IO;
using Ave.Documents.Core;
using Ave.Documents.Images;

namespace UnitTests.Ave.Documents.Images;

/// <summary>
/// The decoder against files written by Pillow (tools/generate-test-images.py). Every pixel is checked against the
/// formula the generator used, so a wrong filter, a wrong bit order or a wrong palette lookup shows at once.
/// </summary>
public class ImageDecoderTests
{
    private const int Width = 7;
    private const int Height = 5;

    private static byte[] Asset(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", name));

    private static ImageData Decode(string name)
    {
        var result = ImageDecoder.Decode(Asset(name));
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        return result.Value;
    }

    private static (byte R, byte G, byte B) Rgb(int x, int y) => ((byte)(x * 36 % 256), (byte)(y * 50 % 256), (byte)(x * y * 13 % 256));

    private static byte Grey(int x, int y) => (byte)((x * 30 + y * 20) % 256);

    private static byte Alpha(int x, int y) => (byte)((x + y) * 25 % 256);

    /// <summary>The generator's palette entry i: (i*40, 255 - i*40, i*17), each modulo 256 as Python computes it (the
    /// result of % in Python is never negative).</summary>
    private static byte[] PaletteEntry(int i) => [(byte)(i * 40 % 256), (byte)(((255 - i * 40) % 256 + 256) % 256), (byte)(i * 17 % 256)];

    private static IEnumerable<(int X, int Y)> Pixels(int width = Width, int height = Height) =>
        Enumerable.Range(0, height).SelectMany(y => Enumerable.Range(0, width).Select(x => (x, y)));

    [Fact]
    public void Greyscale_8_bit_decodes_to_one_sample_per_pixel()
    {
        var image = (RasterImage)Decode("grey8.png");

        (image.PixelWidth, image.PixelHeight, image.ColorSpace, image.Alpha).Should().Be((Width, Height, ImageColorSpace.Gray, (System.Collections.Immutable.ImmutableArray<byte>?)null));
        Pixels().Select(p => image.Samples[p.Y * Width + p.X]).Should().Equal(Pixels().Select(p => Grey(p.X, p.Y)));
    }

    [Fact]
    public void Truecolour_8_bit_decodes_to_three_samples_per_pixel()
    {
        var image = (RasterImage)Decode("rgb8.png");

        image.ColorSpace.Should().Be(ImageColorSpace.Rgb);
        image.Alpha.Should().BeNull();
        AssertRgb(image, Width, Height);
    }

    [Fact]
    public void Truecolour_with_alpha_splits_off_the_alpha_channel()
    {
        var image = (RasterImage)Decode("rgba8.png");

        AssertRgb(image, Width, Height);
        image.Alpha!.Value.Should().Equal(Pixels().Select(p => Alpha(p.X, p.Y)));
    }

    [Fact]
    public void Greyscale_with_alpha_splits_off_the_alpha_channel()
    {
        var image = (RasterImage)Decode("grey-alpha8.png");

        image.ColorSpace.Should().Be(ImageColorSpace.Gray);
        image.Samples.Should().Equal(Pixels().Select(p => Grey(p.X, p.Y)));
        image.Alpha!.Value.Should().Equal(Pixels().Select(p => Alpha(p.X, p.Y)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    public void Indexed_colour_at_every_bit_depth_is_looked_up_in_the_palette(int bits)
    {
        // The generator: index = (x + 2y) mod 2^bits; entry i = (i*40, 255 - i*40, i*17).
        var image = (RasterImage)Decode($"palette{bits}.png");
        var entries = 1 << bits;

        image.ColorSpace.Should().Be(ImageColorSpace.Rgb);
        image.Alpha.Should().BeNull("there is no tRNS chunk");
        foreach (var (x, y) in Pixels())
        {
            var index = (x + 2 * y) % entries;
            var offset = (y * Width + x) * 3;
            image.Samples.Skip(offset).Take(3).Should().Equal(PaletteEntry(index), $"pixel ({x}, {y}) is palette entry {index}");
        }
    }

    [Fact]
    public void Palette_transparency_becomes_alpha_per_entry_and_missing_entries_are_opaque()
    {
        // tRNS gives entry 0 alpha 0 and entry 1 alpha 128; entries 2 and 3 are not listed, so opaque (PNG §11.3.2.1).
        var image = (RasterImage)Decode("palette2-trns.png");
        byte[] alphaOfEntry = [0, 128, 255, 255];

        image.Alpha!.Value.Should().Equal(Pixels().Select(p => alphaOfEntry[(p.X + 2 * p.Y) % 4]));
    }

    [Fact]
    public void A_greyscale_colour_key_makes_exactly_the_matching_pixels_transparent()
    {
        var image = (RasterImage)Decode("grey8-trns.png");
        var key = Grey(1, 1);

        image.Alpha!.Value.Should().Equal(Pixels().Select(p => Grey(p.X, p.Y) == key ? (byte)0 : (byte)255));
    }

    [Fact]
    public void A_truecolour_colour_key_makes_exactly_the_matching_pixels_transparent()
    {
        var image = (RasterImage)Decode("rgb8-trns.png");
        var key = Rgb(2, 3);

        image.Alpha!.Value.Should().Equal(Pixels().Select(p => Rgb(p.X, p.Y) == key ? (byte)0 : (byte)255));
    }

    [Fact]
    public void Every_filter_type_is_undone()
    {
        // Rows 0..9 use filter types 0,1,2,3,4,0,1,2,3,4 (the generator writes them per row, PNG §9.2).
        var image = (RasterImage)Decode("rgb8-all-filters.png");

        AssertRgb(image, Width, 10);
    }

    [Fact]
    public void A_larger_image_filtered_by_libpng_decodes_exactly()
    {
        var image = (RasterImage)Decode("rgb8-filters.png");

        foreach (var (x, y) in Pixels(64, 48))
        {
            var offset = (y * 64 + x) * 3;
            image.Samples.Skip(offset).Take(3).Should().Equal([(byte)((x * 7 + y * 3) % 256), (byte)((x * x + y) % 256), (byte)(x * y % 256)]);
        }
    }

    [Theory]
    [InlineData("baseline-rgb.jpg", ImageColorSpace.Rgb)]
    [InlineData("baseline-grey.jpg", ImageColorSpace.Gray)]
    [InlineData("progressive-rgb.jpg", ImageColorSpace.Rgb)]
    public void A_JPEG_is_kept_as_it_is_with_its_size_and_colour_space_from_the_frame_header(string name, ImageColorSpace colorSpace)
    {
        var image = (JpegImage)Decode(name);

        (image.PixelWidth, image.PixelHeight, image.ColorSpace).Should().Be((40, 30, colorSpace));
        image.File.Should().Equal(Asset(name), "the file is embedded unchanged and decoded by the PDF viewer (DCTDecode)");
    }

    [Theory]
    [InlineData("grey16.png", "PNG greyscale with bit depth 16")]
    [InlineData("rgb8-interlaced-header.png", "PNG interlaced (Adam7)")]
    [InlineData("cmyk.jpg", "JPEG with 4 colour components")]
    public void An_unsupported_variant_is_an_error_that_names_it(string name, string variant)
    {
        var error = ImageDecoder.Decode(Asset(name)).Error;

        error.Code.Should().Be(ImageErrorCodes.Unsupported);
        error.Metadata![ErrorMetadataKeys.Value].Should().Be(variant);
    }

    [Fact]
    public void A_JPEG_with_arithmetic_coding_is_named_as_unsupported()
    {
        // The baseline file with its SOF0 marker (FF C0) changed to SOF9 (FF C9, extended sequential, arithmetic).
        var bytes = Asset("baseline-rgb.jpg");
        var sof = Enumerable.Range(0, bytes.Length - 1).First(index => bytes[index] == 0xFF && bytes[index + 1] == 0xC0);
        bytes[sof + 1] = 0xC9;

        var error = ImageDecoder.Decode(bytes).Error;

        error.Code.Should().Be(ImageErrorCodes.Unsupported);
        error.Metadata![ErrorMetadataKeys.Value].Should().Be("JPEG arithmetic coding (SOF9)");
    }

    [Fact]
    public void A_damaged_chunk_fails_its_CRC_check()
    {
        var bytes = Asset("rgb8.png");
        bytes[^20] ^= 0xFF; // inside the IDAT data

        ImageDecoder.Decode(bytes).Error.Code.Should().Be(ImageErrorCodes.Invalid);
    }

    [Fact]
    public void A_wrong_CRC_is_invalid_even_when_the_data_is_intact()
    {
        // PNG §5.3: the CRC covers type and data; a mismatch means a damaged chunk. Here only the IHDR's stored CRC
        // (bytes 29–32) is wrong, so nothing but the CRC check can notice.
        var bytes = Asset("rgb8.png");
        bytes[29] ^= 0x01;

        var error = ImageDecoder.Decode(bytes).Error;

        error.Code.Should().Be(ImageErrorCodes.Invalid);
        error.Message.Should().Contain("CRC");
    }

    [Fact]
    public void A_truncated_file_is_invalid()
    {
        ImageDecoder.Decode(Asset("rgb8.png").AsMemory(0, 40)).Error.Code.Should().Be(ImageErrorCodes.Invalid);
        ImageDecoder.Decode(Asset("baseline-rgb.jpg").AsMemory(0, 10)).Error.Code.Should().Be(ImageErrorCodes.Invalid);
    }

    [Fact]
    public void Bytes_that_are_neither_PNG_nor_JPEG_are_unsupported()
    {
        var error = ImageDecoder.Decode("GIF89a"u8.ToArray()).Error;

        error.Code.Should().Be(ImageErrorCodes.Unsupported);
        error.Metadata![ErrorMetadataKeys.Value].Should().Be("a format other than PNG or JPEG");
    }

    [Fact]
    public void Images_are_equal_when_their_files_are_byte_identical()
    {
        Decode("rgb8.png").Should().Be(Decode("rgb8.png"));
        Decode("rgb8.png").Should().NotBe(Decode("rgba8.png"));
    }

    [Fact]
    public void Any_corruption_of_a_file_gives_an_error_value_and_never_an_exception()
    {
        // Every single byte of each file flipped in turn: the decoder must return a result every time.
        foreach (var name in new[] { "palette4.png", "rgba8.png", "baseline-rgb.jpg" })
        {
            var original = Asset(name);
            for (var index = 0; index < original.Length; index++)
            {
                var bytes = (byte[])original.Clone();
                bytes[index] ^= 0x5A;
                var act = () => ImageDecoder.Decode(bytes);
                act.Should().NotThrow($"{name} byte {index}");
            }
        }
    }

    private static void AssertRgb(RasterImage image, int width, int height)
    {
        foreach (var (x, y) in Pixels(width, height))
        {
            var (r, g, b) = Rgb(x, y);
            var offset = (y * width + x) * 3;
            image.Samples.Skip(offset).Take(3).Should().Equal([r, g, b], $"pixel ({x}, {y})");
        }
    }
}
