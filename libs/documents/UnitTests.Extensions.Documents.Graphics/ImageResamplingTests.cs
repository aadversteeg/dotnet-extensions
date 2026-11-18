using System.Collections.Immutable;
using Ave.Extensions.Documents.Graphics;

namespace UnitTests.Extensions.Documents.Graphics;

/// <summary>
/// The image rule against analytic values: an image is a grid of uniformly coloured sample squares (ISO 32000-1 §8.9.5.3,
/// no Interpolate; the PDF/A profiles forbid asking for interpolation, ISO 19005-3 §6.2.8, veraPDF rule 6.2.8-3), and a
/// device pixel shows the area-weighted mean of the squares it covers, colours weighted by their alpha, with the share of
/// the pixel the image covers as its alpha. The expected value of every pixel is computed here independently: each
/// sample square mapped to device space is a parallelogram, and <see cref="Geometry.AreaInPixel"/> gives its exact area
/// inside the pixel. Allowed difference: one 8-bit step (the rounding of the mean and of the alpha).
/// </summary>
public class ImageResamplingTests
{
    private static RgbaImage Pattern(int width, int height, int seed, bool transparent)
    {
        var random = new Random(seed);
        var pixels = new byte[width * height * 4];
        random.NextBytes(pixels);
        for (var index = 3; index < pixels.Length; index += 4)
        {
            pixels[index] = transparent ? pixels[index] : (byte)255;
        }

        return new RgbaImage(width, height, ImmutableArray.Create(pixels));
    }

    /// <summary>The exact pixel (r, g, b, a) the rule gives at device pixel (x, y): Σ area·α·c / Σ area·α and Σ area·α.</summary>
    private static (double R, double G, double B, double A) Expected(RgbaImage image, Affine transform, int x, int y)
    {
        double weight = 0, red = 0, green = 0, blue = 0;
        for (var row = 0; row < image.Height; row++)
        {
            for (var column = 0; column < image.Width; column++)
            {
                var corners = new (double X, double Y)[]
                {
                    (transform.MapX(column, row), transform.MapY(column, row)),
                    (transform.MapX(column + 1, row), transform.MapY(column + 1, row)),
                    (transform.MapX(column + 1, row + 1), transform.MapY(column + 1, row + 1)),
                    (transform.MapX(column, row + 1), transform.MapY(column, row + 1)),
                };
                var area = Geometry.AreaInPixel(corners, x, y);
                if (area == 0)
                {
                    continue;
                }

                var offset = ((row * image.Width) + column) * 4;
                var alpha = area * image.Pixels[offset + 3] / 255.0;
                weight += alpha;
                red += alpha * image.Pixels[offset];
                green += alpha * image.Pixels[offset + 1];
                blue += alpha * image.Pixels[offset + 2];
            }
        }

        return weight == 0 ? (0, 0, 0, 0) : (red / weight, green / weight, blue / weight, weight * 255);
    }

    private static void ShouldFollowTheRule(RgbaImage image, Affine transform, int width, int height)
    {
        var canvas = Canvas.Create(width, height, null).Value;
        canvas.DrawImage(image, transform);
        var drawn = canvas.ToImage();

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var (r, g, b, a) = Expected(image, transform, x, y);
                var offset = ((y * width) + x) * 4;
                Math.Abs(drawn.Pixels[offset + 3] - a).Should().BeLessThanOrEqualTo(1, $"alpha of pixel ({x}, {y})");
                if (a >= 1)
                {
                    Math.Abs(drawn.Pixels[offset] - r).Should().BeLessThanOrEqualTo(1, $"red of pixel ({x}, {y})");
                    Math.Abs(drawn.Pixels[offset + 1] - g).Should().BeLessThanOrEqualTo(1, $"green of pixel ({x}, {y})");
                    Math.Abs(drawn.Pixels[offset + 2] - b).Should().BeLessThanOrEqualTo(1, $"blue of pixel ({x}, {y})");
                }
            }
        }
    }

    [Theory]
    [InlineData(2, 0, 0)]
    [InlineData(3, 0, 0)]
    [InlineData(2, 0.5, 0.25)]
    [InlineData(1.5, 0, 0)]
    [InlineData(2.37, 0.3, 0.6)]
    [InlineData(1, 0.4, 0.7)]
    public void An_enlargement_by_any_factor_and_offset_shows_the_area_weighted_squares(double factor, double dx, double dy)
    {
        var image = Pattern(5, 4, 1, transparent: false);
        var transform = Affine.Scale(factor, factor).Then(Affine.Translate(1 + dx, 1 + dy));

        ShouldFollowTheRule(image, transform, (int)Math.Ceiling((5 * factor) + 2 + dx), (int)Math.Ceiling((4 * factor) + 2 + dy));
    }

    [Theory]
    [InlineData(0.5, 0, 0)]
    [InlineData(0.25, 0, 0)]
    [InlineData(0.7, 0.2, 0.9)]
    [InlineData(0.33, 0.5, 0.5)]
    public void A_reduction_by_any_factor_and_offset_averages_the_squares_by_area(double factor, double dx, double dy)
    {
        var image = Pattern(12, 9, 2, transparent: false);
        var transform = Affine.Scale(factor, factor).Then(Affine.Translate(dx, dy));

        ShouldFollowTheRule(image, transform, (int)Math.Ceiling((12 * factor) + dx) + 1, (int)Math.Ceiling((9 * factor) + dy) + 1);
    }

    [Fact]
    public void Different_factors_across_and_down_and_a_mirror_follow_the_rule()
    {
        var image = Pattern(6, 5, 3, transparent: false);

        ShouldFollowTheRule(image, Affine.Scale(1.7, 0.6).Then(Affine.Translate(0.35, 0.8)), 13, 5);
        ShouldFollowTheRule(image, Affine.Scale(-1.3, 1.1).Then(Affine.Translate(9.2, 0.4)), 10, 7);
    }

    [Theory]
    [InlineData(30, 1.4)]
    [InlineData(-71, 0.8)]
    [InlineData(45, 2.2)]
    public void A_rotated_and_scaled_image_follows_the_rule(double degrees, double factor)
    {
        // The affine case: the sample squares are rotated parallelograms in device space. The angle's cosine and sine are
        // computed here, in the test, only to build the transform.
        var image = Pattern(5, 4, 4, transparent: false);
        var (cos, sin) = (Math.Cos(degrees * Math.PI / 180) * factor, Math.Sin(degrees * Math.PI / 180) * factor);
        var transform = new Affine(cos, sin, -sin, cos, 9.3, 8.6);

        ShouldFollowTheRule(image, transform, 19, 18);
    }

    [Fact]
    public void Drawing_an_image_allocates_nothing_per_pixel()
    {
        // 7 × 5 samples over 400 × 300 pixels: 120 000 device pixels, each sampled through the image's grid. What a draw
        // allocates (the path of the image's outline, the row callback) does not grow with the pixels.
        var image = Pattern(7, 5, 6, transparent: false);
        var canvas = Canvas.Create(400, 300, null).Value;
        var transform = Affine.Scale(400 / 7.0, 300 / 5.0);
        canvas.DrawImage(image, transform);

        var before = GC.GetAllocatedBytesForCurrentThread();
        canvas.DrawImage(image, transform);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        allocated.Should().BeLessThan(64 * 1024, "120 000 pixels must not cost an allocation each");
    }

    [Fact]
    public void A_sheared_image_with_transparency_follows_the_rule()
    {
        var image = Pattern(4, 4, 5, transparent: true);

        ShouldFollowTheRule(image, new Affine(1.6, 0.3, 0.9, 1.2, 0.45, 0.7), 12, 8);
    }
}
