using Ave.Extensions.Documents.TestSupport;

namespace UnitTests.Extensions.Documents.Layout;

/// <summary>
/// The whole pixel the comparisons put a position on as Chromium paints it (<see cref="ChromiumLayout.Pixel"/>): the
/// nearest, and half a pixel up, towards the larger value, for either sign (case <c>a4-header-tall-line</c>: a line drawn
/// again a page area higher, its top at -202.5 px, prints at -202 px).
/// </summary>
public class ChromiumLayoutPixelTests
{
    [Theory]
    [InlineData(28.125, 38)]
    [InlineData(28.5, 38)]
    [InlineData(28.4, 38)]
    [InlineData(28.6, 38)]
    [InlineData(28.875, 39)]
    [InlineData(0, 0)]
    [InlineData(0.375, 1)]
    [InlineData(-0.375, 0)]
    [InlineData(-151.875, -202)]
    [InlineData(-152.25, -203)]
    [InlineData(-151.8, -202)]
    [InlineData(-152.1, -203)]
    public void A_position_is_painted_at_the_nearest_pixel_and_half_a_pixel_up_for_either_sign(double points, int pixel)
    {
        ChromiumLayout.Pixel(points).Should().Be(pixel);
        ChromiumLayout.WholePixel(points).Should().Be(pixel * 0.75);
    }
}
