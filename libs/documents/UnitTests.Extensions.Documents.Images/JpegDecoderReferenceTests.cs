using System.IO;
using System.Text;
using Ave.Extensions.Documents.Images;
using Ave.Extensions.Documents.TestSupport;

namespace UnitTests.Extensions.Documents.Images;

/// <summary>
/// The JPEG decoder against libjpeg-turbo 2.1.5's djpeg with the accurate integer transform (<c>-dct int</c>, default
/// "fancy" up-sampling), in the reference tools image, on the files of tools/generate-test-jpegs.py. Each case states how
/// far a sample may differ and why; the differences come from three choices, each described where it is made:
/// <list type="bullet">
/// <item>the inverse DCT: this decoder computes the definition in double precision (<c>InverseDct</c>), djpeg a 13-bit
/// fixed-point approximation of it, so samples differ by its rounding: at most 1 for these files in grey;</item>
/// <item>the colour transform: djpeg uses 16-bit fixed-point tables, this decoder doubles (<c>JpegColor</c>): at most 1
/// more per channel;</item>
/// <item>up-sampling: for factor 2 both use the same weights (¾, ¼), but djpeg rounds the interpolated chroma before the
/// colour transform and this decoder does not; for other factors djpeg repeats each chroma sample and this decoder
/// interpolates, so near a sharp change of colour the two differ by up to the size of that change.</item>
/// </list>
/// Needs Docker (trait <c>Category=Reference</c>).
/// </summary>
[Trait("Category", "Reference")]
public sealed class JpegDecoderReferenceTests
{
    /// <summary>
    /// Files without up-sampling or with factor 2: the largest difference of one sample is the inverse DCT's rounding
    /// (1 in grey and for RGB without colour transform), and with the YCbCr transform a chroma difference of 1 counts up
    /// to 1.772 times (B from Cb), plus djpeg's rounding of the interpolated chroma: at most 3. Samples that differ by
    /// more than 1 need two roundings to add up; at most 1 % of them may (measured: 0 for grey and RGB, 0.27 % to
    /// 0.69 % for the others).
    /// </summary>
    [DockerTheory]
    [InlineData("jpeg-grey.jpg", 1)]
    [InlineData("jpeg-progressive-grey.jpg", 1)]
    [InlineData("jpeg-rgb.jpg", 1)]
    [InlineData("jpeg-444.jpg", 3)]
    [InlineData("jpeg-progressive-444.jpg", 3)]
    [InlineData("jpeg-16bit-tables.jpg", 3)]
    [InlineData("jpeg-1x1.jpg", 3)]
    [InlineData("jpeg-422.jpg", 3)]
    [InlineData("jpeg-440.jpg", 3)]
    [InlineData("jpeg-420.jpg", 3)]
    [InlineData("jpeg-restart.jpg", 3)]
    [InlineData("jpeg-progressive.jpg", 3)]
    [InlineData("jpeg-progressive-restart.jpg", 3)]
    [InlineData("jpeg-mixed.jpg", 3)]
    public void Decodes_like_djpeg_within_the_rounding_of_the_transforms(string name, int largest)
    {
        var (decoded, expected) = BothDecodings(name);

        var differences = decoded.Samples.Select((value, index) => Math.Abs(value - expected[index])).ToArray();
        var report = $"{name}: largest {differences.Max()}, over 1: {differences.Count(d => d > 1)} of {differences.Length}, mean {differences.Average():F3}";
        differences.Max().Should().BeLessThanOrEqualTo(largest, report);
        differences.Count(d => d > 1).Should().BeLessThanOrEqualTo(differences.Length / 100, report);
    }

    /// <summary>
    /// Factors 3 and 4: djpeg repeats each chroma sample, this decoder interpolates between neighbours. Where it
    /// interpolates, its chroma lies between two neighbouring samples, so it differs from either by at most their
    /// difference Δ; through the colour transform that counts at most 1.772 Δ, plus the 3 of the rounding above. Δ is the
    /// largest difference between neighbouring chroma samples of the file, read from the decoded component planes.
    /// </summary>
    [DockerTheory]
    [InlineData("jpeg-411.jpg")]
    [InlineData("jpeg-311.jpg")]
    public void With_factors_three_and_four_differs_from_djpeg_at_most_by_the_chroma_steps(string name)
    {
        var (decoded, expected) = BothDecodings(name);
        var planes = Ave.Extensions.Documents.Images.Jpeg.JpegDecoder.Components(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", name))).Value;
        var step = planes.Skip(1).Max(plane => NeighbourStep(plane.Samples, plane.BlocksPerLine * 8, plane.Width, plane.Height));

        var largest = decoded.Samples.Select((value, index) => Math.Abs(value - expected[index])).Max();

        step.Should().BeGreaterThan(20, "the file has sharp colour edges, so the case shows the difference");
        largest.Should().BeLessThanOrEqualTo((int)Math.Ceiling(1.772 * step) + 3, $"{name}: largest {largest}, chroma step {step}");
    }

    private static int NeighbourStep(byte[] samples, int stride, int width, int height)
    {
        var step = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var value = samples[(y * stride) + x];
                step = Math.Max(step, x + 1 < width ? Math.Abs(value - samples[(y * stride) + x + 1]) : 0);
                step = Math.Max(step, y + 1 < height ? Math.Abs(value - samples[((y + 1) * stride) + x]) : 0);
            }
        }

        return step;
    }

    /// <summary>The decoder's samples and djpeg's (<c>-dct int</c>) for one asset; sizes and channels must agree.</summary>
    private static (RasterImage Decoded, byte[] Expected) BothDecodings(string name)
    {
        var folder = Directory.CreateTempSubdirectory("ave-extensions-documents-djpeg-").FullName;
        try
        {
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Assets", name), Path.Combine(folder, name));
            var (exitCode, output) = Docker.Run(
                ["run", "--rm", .. Docker.UserArguments, "-v", $"{folder}:/work", ReferenceTools.Image, "djpeg", "-dct", "int", "-outfile", "/work/out.pnm", "/work/" + name],
                TimeSpan.FromMinutes(2));
            exitCode.Should().Be(0, output);
            var (width, height, channels, expected) = Pnm(File.ReadAllBytes(Path.Combine(folder, "out.pnm")));

            var decoded = ImageDecoder.DecodeSamples(ImageDecoder.Decode(File.ReadAllBytes(Path.Combine(folder, name))).Value);
            decoded.IsSuccess.Should().BeTrue(decoded.IsFailure ? decoded.Error.ToString() : string.Empty);
            var image = decoded.Value;
            (image.PixelWidth, image.PixelHeight, image.ColorSpace == ImageColorSpace.Gray ? 1 : 3).Should().Be((width, height, channels), "the sizes and channels agree");
            return (image, expected);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>A binary PPM (P6) or PGM (P5) file with maximum value 255.</summary>
    private static (int Width, int Height, int Channels, byte[] Samples) Pnm(byte[] file)
    {
        var fields = new List<string>();
        var position = 0;
        while (fields.Count < 4)
        {
            while (char.IsWhiteSpace((char)file[position]))
            {
                position++;
            }

            var start = position;
            while (!char.IsWhiteSpace((char)file[position]))
            {
                position++;
            }

            fields.Add(Encoding.ASCII.GetString(file, start, position - start));
        }

        position++;
        var channels = fields[0] == "P6" ? 3 : 1;
        return (int.Parse(fields[1]), int.Parse(fields[2]), channels, file[position..]);
    }
}
