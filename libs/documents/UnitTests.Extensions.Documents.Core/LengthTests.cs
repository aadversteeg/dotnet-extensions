using Ave.Documents.Core;

namespace UnitTests.Ave.Documents.Core;

public class LengthTests
{
    [Fact]
    public void One_inch_is_72_points_and_25_4_millimetres()
    {
        25.4.Mm().Points.Should().BeApproximately(72, 1e-12);
        2.54.Cm().Points.Should().BeApproximately(72, 1e-12);
    }

    [Fact]
    public void Int_and_double_helpers_give_the_same_length()
    {
        12.Pt().Should().Be(12.0.Pt());
        3.Mm().Should().Be(3.0.Mm());
        2.Cm().Should().Be(2.0.Cm());
    }

    [Fact]
    public void A4_width_of_210_mm_is_595_276_points()
    {
        // ISO 216: 210 mm = 210 / 25.4 * 72 = 595.2756 pt.
        210.Mm().Points.Should().BeApproximately(595.2756, 1e-4);
    }

    [Fact]
    public void Arithmetic_works_in_points()
    {
        (10.Pt() + 2.Pt()).Should().Be(12.Pt());
        (10.Pt() - 2.Pt()).Should().Be(8.Pt());
        (10.Pt() * 1.5).Should().Be(15.Pt());
    }

    [Fact]
    public void Formats_in_points_with_at_most_three_decimals() =>
        1.Cm().ToString().Should().Be("28.346pt");

    [Fact]
    public void Non_finite_values_are_reported_as_not_finite()
    {
        Length.FromPoints(double.NaN).IsFinite.Should().BeFalse();
        Length.FromPoints(double.PositiveInfinity).IsFinite.Should().BeFalse();
        Length.FromPoints(-3).IsFinite.Should().BeTrue();
        Length.FromPoints(double.NaN).ToString().Should().Be("NaNpt", "error messages show the invalid value");
    }
}
