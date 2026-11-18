using System.Collections.Immutable;
using System.Security.Cryptography;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Graphics;
using Path = Ave.Extensions.Documents.Graphics.Path;

namespace UnitTests.Extensions.Documents.Graphics;

/// <summary>
/// Painting on a canvas: source-over compositing on the encoded 8-bit values, the background, image drawing with the
/// exact box filter, and deterministic output.
/// </summary>
public class CanvasTests
{
    private static (byte R, byte G, byte B, byte A) Pixel(RgbaImage image, int x, int y)
    {
        var offset = ((y * image.Width) + x) * 4;
        return (image.Pixels[offset], image.Pixels[offset + 1], image.Pixels[offset + 2], image.Pixels[offset + 3]);
    }

    private static RgbaImage Image(int width, int height, Func<int, int, (byte R, byte G, byte B, byte A)> pixel)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var (r, g, b, a) = pixel(x, y);
                var offset = ((y * width) + x) * 4;
                (pixels[offset], pixels[offset + 1], pixels[offset + 2], pixels[offset + 3]) = (r, g, b, a);
            }
        }

        return new RgbaImage(width, height, ImmutableArray.Create(pixels));
    }

    [Fact]
    public void The_background_fills_the_canvas_or_leaves_it_transparent()
    {
        Pixel(new Canvas(3, 2, new Color(10, 20, 30)).ToImage(), 2, 1).Should().Be(((byte)10, (byte)20, (byte)30, (byte)255));
        Pixel(new Canvas(3, 2, null).ToImage(), 2, 1).Should().Be(((byte)0, (byte)0, (byte)0, (byte)0));
    }

    [Fact]
    public void A_partly_covered_pixel_mixes_the_encoded_values_by_coverage()
    {
        var canvas = new Canvas(4, 1, Color.White);

        // Pixel 0 fully covered, pixel 1 by a quarter, pixel 2 by three quarters, pixel 3 not at all.
        canvas.Fill(Path.Rectangle(0, 0, 1, 1), Affine.Identity, FillRule.NonZero, new Color(0, 100, 200));
        canvas.Fill(Path.Rectangle(1, 0, 0.25, 1), Affine.Identity, FillRule.NonZero, Color.Black);
        canvas.Fill(Path.Rectangle(2, 0, 0.75, 1), Affine.Identity, FillRule.NonZero, Color.Black);
        var image = canvas.ToImage();

        Pixel(image, 0, 0).Should().Be(((byte)0, (byte)100, (byte)200, (byte)255));

        // Coverage 0.25 → 64 (63.75 rounded); 255 · (255 − 64) / 255 = 191. Coverage 0.75 → 191; 255 − 191 = 64.
        Pixel(image, 1, 0).Should().Be(((byte)191, (byte)191, (byte)191, (byte)255));
        Pixel(image, 2, 0).Should().Be(((byte)64, (byte)64, (byte)64, (byte)255));
        Pixel(image, 3, 0).Should().Be(((byte)255, (byte)255, (byte)255, (byte)255));
    }

    [Fact]
    public void On_a_transparent_canvas_the_alpha_is_the_coverage_and_the_colour_stays_whole()
    {
        var canvas = new Canvas(2, 1, null);

        canvas.Fill(Path.Rectangle(0, 0, 0.5, 1), Affine.Identity, FillRule.NonZero, new Color(200, 100, 50));
        canvas.Fill(Path.Rectangle(0, 0, 0.5, 1), Affine.Identity, FillRule.NonZero, new Color(0, 0, 250));
        var image = canvas.ToImage();

        // First: alpha 128, colour as given. Second over it: alpha' = 128 + 128·127/255 = 191.75 → 192;
        // blue = (250·128·255 + 50·128·127) / (128·255 + 128·127) = 183.5 → 184.
        var (r, g, b, a) = Pixel(image, 0, 0);
        a.Should().Be(192);
        b.Should().Be(184);
        r.Should().Be((byte)Math.Round(200.0 * 128 * 127 / ((128 * 255) + (128 * 127))));
        g.Should().Be((byte)Math.Round(100.0 * 128 * 127 / ((128 * 255) + (128 * 127))));
        Pixel(image, 1, 0).A.Should().Be(0);
    }

    [Fact]
    public void An_image_drawn_one_to_one_on_the_grid_keeps_its_pixels()
    {
        var source = Image(3, 2, (x, y) => ((byte)(x * 80), (byte)(y * 90), 7, 255));
        var canvas = new Canvas(5, 4, Color.White);

        canvas.DrawImage(source, Affine.Translate(1, 2));
        var image = canvas.ToImage();

        for (var y = 0; y < 2; y++)
        {
            for (var x = 0; x < 3; x++)
            {
                Pixel(image, x + 1, y + 2).Should().Be(((byte)(x * 80), (byte)(y * 90), (byte)7, (byte)255));
            }
        }

        Pixel(image, 0, 0).Should().Be(((byte)255, (byte)255, (byte)255, (byte)255));
        Pixel(image, 4, 3).Should().Be(((byte)255, (byte)255, (byte)255, (byte)255));
    }

    [Fact]
    public void Magnification_shows_sharp_squares_and_reduction_averages_by_area()
    {
        var source = Image(2, 2, (x, y) => (x == y ? ((byte)0, (byte)0, (byte)0, (byte)255) : ((byte)255, (byte)255, (byte)255, (byte)255)));

        var large = new Canvas(6, 6, Color.White);
        large.DrawImage(source, Affine.Scale(3, 3));
        var enlarged = large.ToImage();
        Pixel(enlarged, 2, 2).R.Should().Be(0);
        Pixel(enlarged, 3, 2).R.Should().Be(255);
        Pixel(enlarged, 3, 3).R.Should().Be(0);

        var small = new Canvas(1, 1, Color.White);
        small.DrawImage(source, Affine.Scale(0.5, 0.5));
        Pixel(small.ToImage(), 0, 0).R.Should().Be(128, "two black and two white pixels average to 127.5");
    }

    [Fact]
    public void An_image_half_a_pixel_off_the_grid_blends_neighbours_and_anti_aliases_its_edges()
    {
        var source = Image(2, 1, (x, _) => (x == 0 ? ((byte)0, (byte)0, (byte)0, (byte)255) : ((byte)200, (byte)200, (byte)200, (byte)255)));
        var canvas = new Canvas(4, 1, null);

        canvas.DrawImage(source, Affine.Translate(0.5, 0));
        var image = canvas.ToImage();

        Pixel(image, 0, 0).Should().Be(((byte)0, (byte)0, (byte)0, (byte)128), "half the pixel is covered by black");
        Pixel(image, 1, 0).Should().Be(((byte)100, (byte)100, (byte)100, (byte)255), "half black, half 200");
        Pixel(image, 2, 0).Should().Be(((byte)200, (byte)200, (byte)200, (byte)128));
        Pixel(image, 3, 0).A.Should().Be(0);
    }

    [Fact]
    public void A_quarter_turn_and_a_mirror_move_the_pixels_exactly()
    {
        var source = Image(3, 2, (x, y) => ((byte)((10 * x) + y), 0, 0, 255));
        var turned = new Canvas(2, 3, null);
        var mirrored = new Canvas(3, 2, null);

        // (x, y) → (2 − y, x): image row 0 becomes the right column.
        turned.DrawImage(source, new Affine(0, 1, -1, 0, 2, 0));
        mirrored.DrawImage(source, new Affine(-1, 0, 0, 1, 3, 0));

        for (var y = 0; y < 2; y++)
        {
            for (var x = 0; x < 3; x++)
            {
                Pixel(turned.ToImage(), 1 - y, x).R.Should().Be((byte)((10 * x) + y));
                Pixel(mirrored.ToImage(), 2 - x, y).R.Should().Be((byte)((10 * x) + y));
            }
        }
    }

    [Fact]
    public void Transparent_image_pixels_add_no_colour()
    {
        var source = Image(2, 1, (x, _) => (x == 0 ? ((byte)255, (byte)0, (byte)0, (byte)0) : ((byte)0, (byte)0, (byte)255, (byte)255)));
        var canvas = new Canvas(1, 1, Color.White);

        canvas.DrawImage(source, Affine.Scale(0.5, 1));

        // The pixel's preimage is the whole image: alpha (0 + 255)/2 → 128, colour all from the opaque blue pixel.
        Pixel(canvas.ToImage(), 0, 0).Should().Be(((byte)127, (byte)127, (byte)255, (byte)255));
    }

    [Fact]
    public void An_image_flattened_to_a_line_draws_nothing()
    {
        var canvas = new Canvas(2, 2, Color.White);

        canvas.DrawImage(Image(1, 1, (_, _) => (0, 0, 0, 255)), new Affine(1, 1, 1, 1, 0, 0));

        canvas.ToImage().Pixels.Should().OnlyContain(value => value == 255);
    }

    [Fact]
    public void A_clip_box_limits_both_fills_and_images()
    {
        var canvas = new Canvas(4, 1, Color.White);
        var clip = new PixelBox(1, 0, 2.5, 1);

        canvas.Fill(Path.Rectangle(0, 0, 4, 1), Affine.Identity, FillRule.NonZero, Color.Black, clip);
        var image = canvas.ToImage();

        image.Pixels.Where((_, index) => index % 4 == 0).Should().Equal(255, 0, 127, 255);

        var drawn = new Canvas(4, 1, Color.White);
        drawn.DrawImage(Image(4, 1, (_, _) => (0, 0, 0, 255)), Affine.Identity, clip);
        drawn.ToImage().Pixels.Should().Equal(image.Pixels);
    }

    [Fact]
    public void Invalid_input_is_a_bug_of_the_caller_and_throws()
    {
        var canvas = new Canvas(2, 2, null);

        canvas.Invoking(c => c.Fill(Path.Rectangle(0, 0, double.NaN, 1), Affine.Identity, FillRule.NonZero, Color.Black)).Should().Throw<ArgumentException>();
        canvas.Invoking(c => c.Fill(Path.Rectangle(0, 0, 1, 1), Affine.Scale(double.PositiveInfinity, 1), FillRule.NonZero, Color.Black)).Should().Throw<ArgumentException>();
        canvas.Invoking(c => c.Fill(Path.Rectangle(0, 0, 1, 1), Affine.Identity, FillRule.NonZero, Color.Black, new PixelBox(2, 0, 1, 1))).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new Canvas(1 << 14, (1 << 12) + 1, null)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => new PathBuilder().LineTo(1, 1)).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void A_scene_gives_the_same_pixels_on_every_platform()
    {
        // Curves, a rotation, both fill rules, a clip, a transparent background and an image: the SHA-256 of the
        // pixels is pinned, and the tests run on Linux and Windows. A change here means the pixels changed.
        var canvas = new Canvas(97, 61, null);
        var star = new PathBuilder().MoveTo(48, 3).LineTo(60, 55).LineTo(10, 22).LineTo(88, 22).LineTo(36, 55).Close().ToPath();
        var blob = new PathBuilder().MoveTo(5, 30).CubicTo(5, -10, 90, 70, 92, 30).QuadraticTo(50, 70, 5, 30).Close().ToPath();
        canvas.Fill(blob, Affine.Identity, FillRule.NonZero, new Color(30, 144, 255));
        canvas.Fill(star, new Affine(0.8, 0.3, -0.3, 0.8, 18.5, -6.25), FillRule.EvenOdd, new Color(255, 69, 0), new PixelBox(3.5, 2.25, 90.75, 58.5));
        canvas.DrawImage(Image(5, 4, (x, y) => ((byte)(x * 60), (byte)(y * 80), 128, (byte)(255 - (x * 30)))), new Affine(4.3, 1.1, -0.9, 3.7, 60.2, 20.9));

        Convert.ToHexStringLower(SHA256.HashData(canvas.ToImage().Pixels.AsSpan())).Should().Be("2f96bf27e7f12abc33bda90bd0358574e47e23832d4097156e2810aeb0576134");
    }

    [Fact]
    public void Painting_order_decides_and_later_items_cover_earlier_ones()
    {
        var canvas = new Canvas(1, 1, Color.White);

        canvas.Fill(Path.Rectangle(0, 0, 1, 1), Affine.Identity, FillRule.NonZero, Color.Black);
        canvas.Fill(Path.Rectangle(0, 0, 1, 1), Affine.Identity, FillRule.NonZero, new Color(1, 2, 3));

        Pixel(canvas.ToImage(), 0, 0).Should().Be(((byte)1, (byte)2, (byte)3, (byte)255));
    }
}
