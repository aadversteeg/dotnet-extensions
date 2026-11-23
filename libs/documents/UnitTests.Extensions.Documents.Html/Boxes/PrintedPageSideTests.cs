using Ave.Extensions.Documents.Html.Boxes;

namespace UnitTests.Extensions.Documents.Html.Boxes;

/// <summary>
/// A side of the sheet as Chromium 159 prints it (<c>PageRules.Printed</c>): whole points, then whole 1/300 inch computed
/// in single precision. The two ties are measured: case <c>h-page-size-hundred-px</c> (75 pt, 312.5 pixels, printed as
/// 313) and case <c>li-page-break</c> (201 pt, 837.5 pixels, printed as 837).
/// </summary>
public class PrintedPageSideTests
{
    [Theory]
    // The ties: 312.5 rounds up, 837.5 rounds down, as the float products 312.5 and 837.49994 do.
    [InlineData(75.375, 75.12)]
    [InlineData(201, 200.88)]
    // A4 (595.28 x 841.89 pt), US Letter, and 200 pt.
    [InlineData(595.28, 594.96)]
    [InlineData(841.89, 841.92)]
    [InlineData(612, 612)]
    [InlineData(792, 792)]
    [InlineData(200, 199.92)]
    public void A_side_is_whole_points_then_whole_three_hundredths_of_an_inch_in_single_precision(double points, double printed) =>
        PageRules.Printed(points).Points.Should().BeApproximately(printed, 1e-9);
}
