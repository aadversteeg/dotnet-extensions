using System.Buffers.Binary;
using System.IO;
using Ave.Extensions.Documents.Images;
using Ave.Extensions.Documents.Images.Jpeg;

namespace UnitTests.Extensions.Documents.Images;

/// <summary>
/// The JPEG decoder's own rules, without a reference tool: the inverse DCT's constants and a DC-only block, the
/// up-sampling weights and the colour transform, damaged data, and the limits. <see cref="JpegConformanceTests"/> and
/// <see cref="JpegDecoderReferenceTests"/> compare whole files with T.83 and with djpeg.
/// </summary>
public class JpegDecoderTests
{
    private static byte[] Asset(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", name));

    private static Ave.Extensions.Functional.Result<RasterImage, Ave.Extensions.ErrorPaths.Error> Samples(byte[] file)
    {
        var header = ImageDecoder.Decode(file);
        header.IsSuccess.Should().BeTrue(header.IsFailure ? header.Error.ToString() : string.Empty);
        return ImageDecoder.DecodeSamples(header.Value);
    }

    [Fact]
    public void The_cosine_constants_are_the_cosines()
    {
        // Each constant cos(kπ/16) of the transform, read back through the basis: a block with only coefficient (0, u)
        // gives samples 128 + ½ · ½ · C(0) · C(u) · F · cos((2x + 1)uπ/16) across.
        for (var u = 1; u < 8; u++)
        {
            var coefficients = new double[64];
            coefficients[u] = 200;
            var output = new byte[64];

            InverseDct.Transform(coefficients, output, 0, 8);

            for (var x = 0; x < 8; x++)
            {
                var expected = 128 + (0.25 / Math.Sqrt(2) * 200 * Math.Cos((2 * x + 1) * u * Math.PI / 16));
                output[x].Should().Be((byte)Math.Floor(expected + 0.5), $"u = {u}, x = {x}");
            }
        }
    }

    [Fact]
    public void A_block_with_only_a_DC_coefficient_is_flat_at_an_eighth_of_it()
    {
        var coefficients = new double[64];
        coefficients[0] = -400;
        var output = new byte[64];

        InverseDct.Transform(coefficients, output, 0, 8);

        output.Should().OnlyContain(value => value == 78, "128 + (−400 / 8)");
    }

    [Fact]
    public void Chroma_at_half_resolution_is_interpolated_with_three_quarters_and_one_quarter()
    {
        // Luma 100 everywhere; Cb samples 128 and 228 (one row), Cr 128: full-resolution x = 0, 1, 2, 3 has Cb at component
        // positions −0.25, 0.25, 0.75, 1.25 → 128, 153, 203, 228; B = Y + 1.772 (Cb − 128).
        var luma = (Enumerable.Repeat((byte)100, 32).ToArray(), 32, 4, 1, 2, 1);
        var cb = (new byte[] { 128, 228 }.Concat(new byte[14]).ToArray(), 16, 2, 1, 1, 1);
        var cr = (Enumerable.Repeat((byte)128, 16).ToArray(), 16, 2, 1, 1, 1);

        var samples = JpegColor.Convert(4, 1, 2, 1, [luma, cb, cr], transform: true);

        Enumerable.Range(0, 4).Select(x => samples[(x * 3) + 2]).Should().Equal(100, 144, 233, 255);
        Enumerable.Range(0, 4).Select(x => samples[x * 3]).Should().Equal([100, 100, 100, 100], "Cr is 128: R is Y");
    }

    [Theory]
    [InlineData(3, new byte[] { 0, 0, 33, 67, 100, 100 })]
    [InlineData(4, new byte[] { 0, 0, 13, 38, 63, 88, 100, 100 })]
    public void Other_factors_are_interpolated_between_the_nearest_samples_too(int factor, byte[] expected)
    {
        // A grey component stored at 1/factor of the width: samples 0 and 100. Full-resolution x lies at component
        // position (x + ½)/factor − ½.
        var plane = (new byte[] { 0, 100 }.Concat(new byte[14]).ToArray(), 16, 2, 1, 1, 1);

        var samples = JpegColor.Convert(2 * factor, 1, factor, 1, [plane], transform: false);

        samples.Should().Equal(expected);
    }

    [Fact]
    public void Every_truncation_of_a_JPEG_is_an_error_and_never_throws()
    {
        foreach (var name in new[] { "jpeg-420.jpg", "jpeg-progressive.jpg" })
        {
            var file = Asset(name);
            for (var length = 3; length < file.Length - 2; length += Math.Max(1, file.Length / 300))
            {
                var cut = file[..length];
                var header = ImageDecoder.Decode(cut);
                if (header.IsFailure)
                {
                    continue;
                }

                var result = ImageDecoder.DecodeSamples(header.Value);

                result.IsFailure.Should().BeTrue($"{name} cut at {length} of {file.Length} bytes is incomplete");
                result.Error.Is(ImageErrorCodes._).Should().BeTrue();
            }
        }
    }

    [Fact]
    public void Corrupted_bytes_never_throw()
    {
        var random = new Random(181118);
        foreach (var name in new[] { "jpeg-420.jpg", "jpeg-progressive-restart.jpg", "jpeg-16bit-tables.jpg" })
        {
            var file = Asset(name);
            for (var attempt = 0; attempt < 300; attempt++)
            {
                var copy = (byte[])file.Clone();
                copy[random.Next(2, copy.Length)] = (byte)random.Next(256);

                var act = () =>
                {
                    var header = ImageDecoder.Decode(copy);
                    return header.IsSuccess ? ImageDecoder.DecodeSamples(header.Value).IsSuccess : false;
                };

                act.Should().NotThrow($"{name} with a changed byte");
            }
        }
    }

    [Fact]
    public void A_missing_restart_marker_is_invalid()
    {
        var file = Asset("jpeg-restart.jpg");
        var marker = Enumerable.Range(0, file.Length - 1).First(index => file[index] == 0xFF && file[index + 1] == 0xD0);
        file[marker + 1] = 0xD3;

        var result = Samples(file);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ImageErrorCodes.Invalid);
        result.Error.Message.Should().Contain("RST0");
    }

    [Fact]
    public void A_frame_above_the_pixel_limit_is_refused_before_decoding()
    {
        var file = Asset("jpeg-444.jpg");
        var frame = Enumerable.Range(0, file.Length - 1).First(index => file[index] == 0xFF && file[index + 1] == 0xC0);
        BinaryPrimitives.WriteUInt16BigEndian(file.AsSpan(frame + 5), 65000);
        BinaryPrimitives.WriteUInt16BigEndian(file.AsSpan(frame + 7), 65000);

        var result = Samples(file);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ImageErrorCodes.TooLarge);
    }

    [Fact]
    public void Scans_that_visit_the_blocks_too_often_are_refused()
    {
        // The first scan of a progressive file is an interleaved DC scan over every block. Written 40 times it asks for 40
        // passes over the frame; the limit is 32.
        var file = Asset("jpeg-progressive.jpg");
        var start = Enumerable.Range(0, file.Length - 1).First(index => file[index] == 0xFF && file[index + 1] == 0xDA);
        var end = Enumerable.Range(start + 2, file.Length - start - 3).First(index => file[index] == 0xFF && file[index + 1] is not 0x00 and not (>= 0xD0 and <= 0xD7));
        var scan = file[start..end];
        var repeated = file[..start].Concat(Enumerable.Repeat(scan, 40).SelectMany(bytes => bytes)).Concat(file[end..]).ToArray();

        Samples(file[..start].Concat(Enumerable.Repeat(scan, 3).SelectMany(bytes => bytes)).Concat(file[end..]).ToArray())
            .IsSuccess.Should().BeTrue("a DC scan sent again only overwrites the DC coefficients");
        var result = Samples(repeated);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ImageErrorCodes.TooLarge);
    }

    [Fact]
    public void A_scan_that_uses_an_undefined_table_is_invalid()
    {
        var file = Asset("jpeg-grey.jpg");
        var table = Enumerable.Range(0, file.Length - 1).First(index => file[index] == 0xFF && file[index + 1] == 0xC4);
        file[table + 4] = 0x03; // the first Huffman table becomes DC table 3, which the scan does not use

        var result = Samples(file);

        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("was not defined");
    }

    [Fact]
    public void An_Adobe_marker_with_transform_0_keeps_three_components_as_RGB()
    {
        // jpeg-rgb.jpg is written by cjpeg -rgb: an Adobe marker with transform 0, so a sample of (r, g, b) is not
        // converted. Read as YCbCr the same samples would give other colours.
        var file = Asset("jpeg-rgb.jpg");
        var adobe = Enumerable.Range(0, file.Length - 1).First(index => file[index] == 0xFF && file[index + 1] == 0xEE);
        var asRgb = Samples(file).Value.Samples;
        file[adobe + 15] = 1;
        var asYcc = Samples(file).Value.Samples;

        asRgb.Should().NotEqual(asYcc);
    }
}
