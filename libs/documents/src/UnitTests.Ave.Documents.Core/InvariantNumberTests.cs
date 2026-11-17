using Ave.Documents.Core;

namespace UnitTests.Ave.Documents.Core;

public class InvariantNumberTests
{
    [Theory]
    [InlineData(12.0, "12")]
    [InlineData(12.5, "12.5")]
    [InlineData(1.0 / 3.0, "0.333")]
    [InlineData(-2.25, "-2.25")]
    [InlineData(0.0005, "0.001")]
    [InlineData(1e20, "100000000000000000000")]
    public void Formats_with_a_dot_and_without_exponent(double value, string expected) =>
        InvariantNumber.Format(value).Should().Be(expected);

    [Theory]
    [InlineData(-0.0)]
    [InlineData(-0.0001)]
    public void Never_writes_negative_zero(double value) =>
        InvariantNumber.Format(value).Should().Be("0");

    [Fact]
    public void Respects_the_number_of_decimals() =>
        InvariantNumber.Format(Math.PI, 4).Should().Be("3.1416");

    [Theory]
    [InlineData(double.NaN, "NaN")]
    [InlineData(double.PositiveInfinity, "Infinity")]
    [InlineData(double.NegativeInfinity, "-Infinity")]
    public void Writes_non_finite_values_by_name_so_diagnostics_never_throw(double value, string expected) =>
        InvariantNumber.Format(value).Should().Be(expected);
}
