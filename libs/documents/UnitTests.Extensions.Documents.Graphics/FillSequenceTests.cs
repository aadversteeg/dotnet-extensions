using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Graphics;
using Path = Ave.Extensions.Documents.Graphics.Path;

namespace UnitTests.Extensions.Documents.Graphics;

/// <summary>
/// A canvas keeps one rasteriser for all its fills, as a page does for all its glyphs and shapes: a fill must give what
/// the same fill gives on a fresh canvas, whatever the fills before it left in the rasteriser. The fills here end on whole
/// columns and rows, are cut by the canvas's edge and by clip boxes, and come after fills that leave pixels where
/// contours overlap.
/// </summary>
public class FillSequenceTests
{
    /// <summary>A fill's outcome as the rasteriser reports it: the error code or the charge, and every value reported.</summary>
    private static string Outcome(AreaRasterizer rasterizer, List<List<Vertex>> polygons, FillRule rule, PixelBox box)
    {
        var values = new List<string>();
        try
        {
            var result = rasterizer.Rasterize(Clipping.ToBox(polygons, box), rule, box, (row, first, coverage) =>
            {
                for (var index = 0; index < coverage.Length; index++)
                {
                    values.Add($"({first + index},{row})={BitConverter.DoubleToInt64Bits(coverage[index]):X}");
                }
            });
            return (result.IsSuccess ? $"charge {result.Value}" : $"error {result.Error.Code}") + ": " + string.Join(" ", values);
        }
        catch (Exception exception)
        {
            return $"throws {exception.GetType().Name}";
        }
    }

    private static List<Vertex> Polygon(params (double X, double Y)[] points) => [.. points.Select(point => new Vertex(point.X, point.Y))];

    /// <summary>A coordinate of the canvas: a whole number half the time, else a random place.</summary>
    private static double Coordinate(Random random, int size) =>
        random.Next(2) == 0 ? random.Next(size + 1) : Math.Round(random.NextDouble() * size * 64) / 64;

    /// <summary>One random fill of a sequence: small triangles each inside one pixel (they leave flagged pixels),
    /// rectangles that overlap with sides on whole columns and rows, or triangles that overlap with corners on them; some
    /// reach past the canvas's right edge.</summary>
    private static List<List<Vertex>> Fill(Random random, int width, int height)
    {
        var polygons = new List<List<Vertex>>();
        switch (random.Next(3))
        {
            case 0:
                for (var count = 1 + random.Next(4); count > 0; count--)
                {
                    var (x, y) = (random.Next(width), random.Next(height));
                    polygons.Add(Polygon((x + 0.2, y + 0.2), (x + 0.8, y + 0.2), (x + 0.5, y + 0.8)));
                }

                break;
            case 1:
                for (var count = 2 + random.Next(2); count > 0; count--)
                {
                    var (left, top) = (Coordinate(random, width - 1), Coordinate(random, height - 1));
                    var (right, bottom) = (left + 0.25 + Coordinate(random, width + 6 - (int)left), top + 0.25 + Coordinate(random, height - (int)top));
                    polygons.Add(random.Next(2) == 0
                        ? Polygon((left, top), (right, top), (right, bottom), (left, bottom))
                        : Polygon((left, top), (left, bottom), (right, bottom), (right, top)));
                }

                break;
            default:
                for (var count = 2 + random.Next(2); count > 0; count--)
                {
                    polygons.Add(Polygon(
                        (Coordinate(random, width + 4), Coordinate(random, height)),
                        (Coordinate(random, width + 4), Coordinate(random, height)),
                        (Coordinate(random, width + 4), Coordinate(random, height))));
                }

                break;
        }

        return polygons;
    }

    /// <summary>The canvas, or a clip box inside it with sides on whole columns and rows or between them.</summary>
    private static PixelBox Box(Random random, int width, int height) => random.Next(3) switch
    {
        0 => new PixelBox(0, 0, width, height),
        1 => new PixelBox(random.Next(width / 2), random.Next(height / 2), (width / 2) + random.Next(width / 2) + 1, (height / 2) + random.Next(height / 2) + 1),
        _ => new PixelBox(random.Next(width / 2) + 0.375, random.Next(height / 2) + 0.625, (width / 2) + random.Next(width / 2) + 0.25, (height / 2) + random.Next(height / 2) + 0.5),
    };

    [Fact]
    public void Each_fill_of_a_sequence_gives_what_it_gives_on_a_fresh_canvas()
    {
        // Fixed seed: the same 400 sequences of six fills every run, under both rules.
        var random = new Random(20261119);
        var (fills, differ) = (0, new List<string>());
        for (var sequence = 0; sequence < 400; sequence++)
        {
            var (width, height) = (8 + random.Next(17), 6 + random.Next(11));
            var rule = random.Next(2) == 0 ? FillRule.NonZero : FillRule.EvenOdd;
            var shared = new AreaRasterizer();
            for (var step = 0; step < 6; step++)
            {
                var (polygons, box) = (Fill(random, width, height), Box(random, width, height));
                var (got, want) = (Outcome(shared, polygons, rule, box), Outcome(new AreaRasterizer(), polygons, rule, box));
                fills++;
                if (got != want)
                {
                    differ.Add($"sequence {sequence} fill {step} ({rule}, box {box}): {got[..int.Min(got.Length, 60)]} instead of {want[..int.Min(want.Length, 60)]}");
                }
            }
        }

        fills.Should().Be(2400);
        differ.Should().BeEmpty($"a fill must not depend on the fills before it; all that do: {string.Join("; ", differ)}");
    }

    /// <summary>Three rectangles wound the same way, the widest from x = 0 to <paramref name="right"/>.</summary>
    private static Path Rectangles(double right, double height)
    {
        var builder = new PathBuilder();
        foreach (var (left, end) in new[] { (0.0, right), (3.3, 5.0), (3.6, 5.5) })
        {
            builder.MoveTo(left, 0).LineTo(end, 0).LineTo(end, height).LineTo(left, height).Close();
        }

        return builder.ToPath();
    }

    /// <summary>A sliver from x = 0, so column 0 is the first fill's first, and a triangle inside pixel (10, 5), which
    /// is flagged.</summary>
    private static Path SliverAndTriangle() => new PathBuilder()
        .MoveTo(0, 5.1).LineTo(0.5, 5.1).LineTo(0, 5.9).Close()
        .MoveTo(10.2, 5.2).LineTo(10.8, 5.2).LineTo(10.5, 5.8).Close()
        .ToPath();

    /// <summary>Two triangles whose long edges cross in pixel (2, 5), a small triangle in pixel (0, 5) and a bar from
    /// x = 9.5 to 10 in row 5, its right side on the shape's largest x, wound down or up.</summary>
    private static Path CrossingAndBar(bool down)
    {
        var builder = new PathBuilder()
            .MoveTo(2.1, 4).LineTo(2.9, 7).LineTo(8, 5.5).Close()
            .MoveTo(2.1, 7).LineTo(2.9, 4).LineTo(8.5, 6.2).Close()
            .MoveTo(0, 5.2).LineTo(0.8, 5.2).LineTo(0.4, 5.8).Close();
        return (down
            ? builder.MoveTo(9.5, 5.2).LineTo(10, 5.2).LineTo(10, 6).LineTo(9.5, 6).Close()
            : builder.MoveTo(9.5, 5.2).LineTo(9.5, 6).LineTo(10, 6).LineTo(10, 5.2).Close()).ToPath();
    }

    private static byte[] Red(Canvas canvas)
    {
        var image = canvas.ToImage();
        return [.. Enumerable.Range(0, image.Width * image.Height).Select(index => image.Pixels[index * 4])];
    }

    public static TheoryData<string, FillRule> Cases() => new()
    {
        { "rectangles to x = 10 over 150 rows", FillRule.NonZero },
        { "rectangles to x = 10 over 150 rows", FillRule.EvenOdd },
        { "rectangles to x = 25 under a clip box to x = 10", FillRule.NonZero },
        { "rectangles to x = 25 under a clip box to x = 10", FillRule.EvenOdd },
        { "crossing triangles and a bar wound down", FillRule.NonZero },
        { "crossing triangles and a bar wound down", FillRule.EvenOdd },
        { "crossing triangles and a bar wound up", FillRule.NonZero },
        { "crossing triangles and a bar wound up", FillRule.EvenOdd },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void A_shape_ending_on_a_whole_column_after_a_fill_with_a_flagged_pixel_is_drawn_as_on_a_fresh_canvas(string name, FillRule rule)
    {
        // Known cases through the public API: the second fill ends on x = 10 (its own side or the clip box's), the first
        // left a flagged pixel ten columns from the second's first column. Before the repair: an exception
        // (IndexOutOfRangeException), pixel (6, 5) at 198 instead of 161 with success, or InexactCoverage.
        var (height, second, clip) = name switch
        {
            "rectangles to x = 10 over 150 rows" => (200, Rectangles(10, 150), (PixelBox?)null),
            "rectangles to x = 25 under a clip box to x = 10" => (200, Rectangles(25, 150), new PixelBox(0, 0, 10, 200)),
            "crossing triangles and a bar wound down" => (20, CrossingAndBar(down: true), null),
            _ => (20, CrossingAndBar(down: false), null),
        };
        var fresh = Canvas.Create(40, height, Color.White).Value;
        fresh.Fill(second, Affine.Identity, rule, Color.Black, clip).IsSuccess.Should().BeTrue();
        var canvas = Canvas.Create(40, height, Color.White).Value;
        canvas.Fill(SliverAndTriangle(), Affine.Identity, rule, Color.Black).IsSuccess.Should().BeTrue();
        var before = Red(canvas);

        var result = canvas.Fill(second, Affine.Identity, rule, Color.Black, clip);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : "");
        var (want, got) = (Red(fresh), Red(canvas));
        var differ = Enumerable.Range(0, got.Length)
            .Where(index => before[index] == 255 && got[index] != want[index])
            .Select(index => $"({index % 40},{index / 40}) {got[index]} instead of {want[index]}")
            .ToList();
        differ.Should().BeEmpty($"where the first fill painted nothing the canvas must show the second fill alone; all that differ: {string.Join("; ", differ)}");
    }
}
