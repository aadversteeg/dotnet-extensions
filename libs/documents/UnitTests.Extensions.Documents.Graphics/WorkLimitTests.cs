using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Graphics;
using Ave.Extensions.ErrorPaths;
using Path = Ave.Extensions.Documents.Graphics.Path;

namespace UnitTests.Extensions.Documents.Graphics;

/// <summary>
/// The work limits of exact coverage (<see cref="Canvas.MaxFillWork"/>, <see cref="Canvas.MaxCanvasWork"/>,
/// <see cref="Canvas.MaxRowPieces"/>): a shape over a limit is refused with <c>Documents.Graphics.TooMuchWork</c>, the
/// work asked for and the limit in its metadata, and nothing of it is painted.
/// </summary>
public class WorkLimitTests
{
    /// <summary>A thousand bars 0.1 pixel wide through the centre of pixel (50, 50), each its own contour, turned
    /// evenly: two thousand pieces of edges in that pixel.</summary>
    private static Path ThousandBars()
    {
        var builder = new PathBuilder();
        for (var index = 0; index < 1000; index++)
        {
            var angle = Math.PI * index / 1000;
            var (c, s) = (Math.Cos(angle), Math.Sin(angle));
            double X(double u, double v) => 50.5 + (u * c) - (v * s);
            double Y(double u, double v) => 50.5 + (u * s) + (v * c);
            builder.MoveTo(X(-20, -0.05), Y(-20, -0.05)).LineTo(X(20, -0.05), Y(20, -0.05)).LineTo(X(20, 0.05), Y(20, 0.05)).LineTo(X(-20, 0.05), Y(-20, 0.05)).Close();
        }

        return builder.ToPath();
    }

    /// <summary>A star polygon {n/k} of radius 0.45 inside pixel (10, 10): one contour that crosses itself, all n
    /// edges in one pixel.</summary>
    internal static Path StarInOnePixel(int n, int k)
    {
        var builder = new PathBuilder();
        SubpathBuilder? subpath = null;
        for (var index = 0; index < n; index++)
        {
            var angle = 2 * Math.PI * ((long)index * k % n) / n;
            var (x, y) = (10.5 + (0.45 * Math.Cos(angle)), 10.5 + (0.45 * Math.Sin(angle)));
            subpath = subpath is null ? builder.MoveTo(x, y) : subpath.LineTo(x, y);
        }

        return subpath!.Close().ToPath();
    }

    private static void ShouldBeTooMuchWork(Error error, long limit)
    {
        error.Code.Should().Be(GraphicsErrorCodes.TooMuchWork);
        // Value: the work asked for and the limit, "asked>limit".
        var parts = error.Metadata![ErrorMetadataKeys.Value].ToString()!.Split('>');
        parts.Should().HaveCount(2);
        long.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture).Should().Be(limit);
        long.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture).Should().BeGreaterThan(limit);
    }

    [Fact]
    public void A_thousand_bars_through_one_pixel_are_refused_and_nothing_is_painted()
    {
        var canvas = Canvas.Create(101, 101, Color.White).Value;
        var before = canvas.ToImage().Pixels;

        var result = canvas.Fill(ThousandBars(), Affine.Identity, FillRule.NonZero, Color.Black);

        result.IsFailure.Should().BeTrue();
        ShouldBeTooMuchWork(result.Error, Canvas.MaxFillWork);
        canvas.ToImage().Pixels.Should().Equal(before, "a refused fill paints no pixel");
        Canvas.Coverage(ThousandBars(), Affine.Identity, FillRule.EvenOdd, new PixelBox(0, 0, 101, 101)).Error.Code.Should().Be(GraphicsErrorCodes.TooMuchWork);
    }

    [Fact]
    public void A_contour_crossing_itself_a_thousand_times_in_one_pixel_is_refused()
    {
        var canvas = Canvas.Create(21, 21, Color.White).Value;
        var before = canvas.ToImage().Pixels;

        var result = canvas.Fill(StarInOnePixel(1001, 500), Affine.Identity, FillRule.EvenOdd, Color.Black);

        ShouldBeTooMuchWork(result.Error, Canvas.MaxFillWork);
        canvas.ToImage().Pixels.Should().Equal(before);
    }

    [Fact]
    public void A_star_just_inside_the_fill_limit_is_drawn()
    {
        // {643/321}: 643 edges in one pixel, a charge just below 2^27 (133,957,476); 644 pieces would be over it, so
        // {645/322} is refused.
        CellArrangement.Charge(643).Should().Be(133_957_476).And.BeLessThan(Canvas.MaxFillWork);
        CellArrangement.Charge(644).Should().BeGreaterThan(Canvas.MaxFillWork);

        Canvas.Coverage(StarInOnePixel(643, 321), Affine.Identity, FillRule.NonZero, new PixelBox(0, 0, 21, 21)).IsSuccess.Should().BeTrue();
        ShouldBeTooMuchWork(Canvas.Coverage(StarInOnePixel(645, 322), Affine.Identity, FillRule.NonZero, new PixelBox(0, 0, 21, 21)).Error, Canvas.MaxFillWork);
    }

    [Fact]
    public void A_row_with_more_pieces_in_flagged_pixels_than_the_limit_is_refused()
    {
        // Two thin overlapping bars along one row of 70,000 pixels: every pixel of the row holds four pieces of two
        // contours, 280,000 in the row, over 2^18; the charge itself (72 per pixel) is far below the fill limit.
        var path = new PathBuilder()
            .MoveTo(0.25, 0.2).LineTo(69_999.75, 0.2).LineTo(69_999.75, 0.4).LineTo(0.25, 0.4).Close()
            .MoveTo(0.25, 0.3).LineTo(69_999.75, 0.3).LineTo(69_999.75, 0.6).LineTo(0.25, 0.6).Close()
            .ToPath();

        var result = Canvas.Coverage(path, Affine.Identity, FillRule.NonZero, new PixelBox(0, 0, 70_000, 1));

        ShouldBeTooMuchWork(result.Error, Canvas.MaxRowPieces);
        CellArrangement.Charge(4).Should().Be(72);
    }

    [Fact]
    public void The_fills_of_one_canvas_share_one_limit()
    {
        // Each star has a charge just below the fill limit; two of them fit the canvas limit, the third does not and is
        // not painted, and a fill of a plain shape after it still is.
        var canvas = Canvas.Create(21, 21, Color.White).Value;
        var star = StarInOnePixel(639, 319);
        var charge = CellArrangement.Charge(639);
        var fits = (int)(Canvas.MaxCanvasWork / charge);
        for (var index = 0; index < fits; index++)
        {
            canvas.Fill(star, Affine.Identity, FillRule.NonZero, new Color(0, 0, (byte)index)).IsSuccess.Should().BeTrue($"fill {index + 1} of {fits}");
        }

        var before = canvas.ToImage().Pixels;
        var refused = canvas.Fill(star, Affine.Identity, FillRule.NonZero, Color.Black);

        ShouldBeTooMuchWork(refused.Error, Canvas.MaxCanvasWork);
        canvas.ToImage().Pixels.Should().Equal(before);
        canvas.Fill(Path.Rectangle(1, 1, 3, 3), Affine.Identity, FillRule.NonZero, Color.Black).IsSuccess.Should().BeTrue("a shape without flagged pixels costs nothing");
    }
}
