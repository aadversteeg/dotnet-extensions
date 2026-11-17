using Ave.Documents.Core;

namespace UnitTests.Ave.Documents.Core;

public class ValueFormattingTests
{
    [Fact]
    public void Quote_escapes_quotes_backslashes_and_control_characters() =>
        QuotedText.Quote("a \"b\" \\ \n\t\u0007").Should().Be("\"a \\\"b\\\" \\\\ \\n\\t\\u0007\"");

    [Fact]
    public void Colour_formats_as_css_hex() =>
        new Color(0x1F, 0x4E, 0x79).ToString().Should().Be("#1f4e79");

    [Fact]
    public void Edges_follow_the_css_order_top_right_bottom_left()
    {
        var edges = new Edges(1.Pt(), 2.Pt(), 3.Pt(), 4.Pt());

        edges.ToString().Should().Be("1,2,3,4");
        edges.Horizontal.Should().Be(6.Pt());
        edges.Vertical.Should().Be(4.Pt());
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(1000, true)]
    [InlineData(0, false)]
    [InlineData(1001, false)]
    public void Font_weight_is_valid_from_1_to_1000(int value, bool valid) =>
        new FontWeight(value).IsValid.Should().Be(valid);

    [Fact]
    public void Fits_allows_floating_point_noise_but_not_a_visible_overflow()
    {
        Fitting.Fits(100.0000000001, 100).Should().BeTrue();
        Fitting.Fits(100.001, 100).Should().BeFalse();
    }
}
