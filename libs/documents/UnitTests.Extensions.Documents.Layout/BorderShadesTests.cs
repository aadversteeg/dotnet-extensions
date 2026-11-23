using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Layout;

namespace UnitTests.Extensions.Documents.Layout;

/// <summary>
/// The two colours of an inset or outset border side (<see cref="BorderShades"/>): every colour Chromium 159 printed in the
/// cases <c>bs-colours</c> (14 given colours), <c>bs-colour-bands</c> (19 colours at the edges of the bands of luminance) and
/// <c>bs-widths</c> (#eeeeee, the colour a side in currentcolor takes), each with the darker and the lighter colour the
/// print shows.
/// </summary>
public class BorderShadesTests
{
    private static Color Parse(string hex) => new(System.Convert.ToByte(hex[1..3], 16), System.Convert.ToByte(hex[3..5], 16), System.Convert.ToByte(hex[5..7], 16));

    [Theory]
    [InlineData("#000000", "#545454", "#a8a8a8")] // bs-colours
    [InlineData("#ffffff", "#ababab", "#ffffff")] // bs-colours
    [InlineData("#808080", "#2c2c2c", "#d4d4d4")] // bs-colours
    [InlineData("#ff0000", "#ab0000", "#ff0000")] // bs-colours
    [InlineData("#00ff00", "#00ab00", "#00ff00")] // bs-colours
    [InlineData("#0000ff", "#0000ab", "#0000ff")] // bs-colours
    [InlineData("#336699", "#172e45", "#4f9eee")] // bs-colours
    [InlineData("#c86432", "#743a1d", "#ff7f3f")] // bs-colours
    [InlineData("#c0c0c0", "#6c6c6c", "#ffffff")] // bs-colours
    [InlineData("#010101", "#555555", "#a9a9a9")] // bs-colours
    [InlineData("#fefefe", "#aaaaaa", "#fefefe")] // bs-colours
    [InlineData("#545454", "#000000", "#a8a8a8")] // bs-colours
    [InlineData("#a8a8a8", "#545454", "#fdfdfd")] // bs-colours
    [InlineData("#400000", "#940000", "#e90000")] // bs-colours
    [InlineData("#1c1c1c", "#707070", "#c4c4c4")] // bs-colour-bands
    [InlineData("#1f1f1f", "#737373", "#c7c7c7")] // bs-colour-bands
    [InlineData("#202020", "#747474", "#c8c8c8")] // bs-colour-bands
    [InlineData("#212121", "#000000", "#757575")] // bs-colour-bands
    [InlineData("#222222", "#000000", "#767676")] // bs-colour-bands
    [InlineData("#000060", "#0000b4", "#0000ff")] // bs-colour-bands
    [InlineData("#600000", "#0b0000", "#b40000")] // bs-colour-bands
    [InlineData("#000080", "#00002c", "#0000d4")] // bs-colour-bands
    [InlineData("#480000", "#9c0000", "#f10000")] // bs-colour-bands
    [InlineData("#500000", "#000000", "#a40000")] // bs-colour-bands
    [InlineData("#e6e6e6", "#929292", "#ffffff")] // bs-colour-bands
    [InlineData("#e9e9e9", "#959595", "#ffffff")] // bs-colour-bands
    [InlineData("#eaeaea", "#969696", "#ffffff")] // bs-colour-bands
    [InlineData("#ebebeb", "#979797", "#ffffff")] // bs-colour-bands
    [InlineData("#ececec", "#989898", "#ececec")] // bs-colour-bands
    [InlineData("#f0f0f0", "#9c9c9c", "#f0f0f0")] // bs-colour-bands
    [InlineData("#b0f0b0", "#729c72", "#bbffbb")] // bs-colour-bands
    [InlineData("#f0f0b0", "#9c9c72", "#f0f0b0")] // bs-colour-bands
    [InlineData("#d0d0d0", "#7c7c7c", "#ffffff")] // bs-colour-bands
    [InlineData("#eeeeee", "#9a9a9a", "#eeeeee")] // bs-widths
    public void The_darker_and_the_lighter_colour_are_the_ones_Chromium_prints(string given, string dark, string light) =>
        BorderShades.Pair(Parse(given)).Should().Be((Parse(dark), Parse(light)));

    [Fact]
    public void The_luminance_decides_the_band_not_the_largest_channel()
    {
        // #000060 and #600000 have the same largest channel; only the second is above the dark band (bs-colour-bands).
        BorderShades.Luminance(Parse("#000060")).Should().BeLessThan(BorderShades.Luminance(Parse("#202020")));
        BorderShades.Luminance(Parse("#600000")).Should().BeGreaterThan(BorderShades.Luminance(Parse("#212121")));
        BorderShades.Pair(Parse("#000060")).Dark.Should().Be(BorderShades.Lighter(Parse("#000060")));
        BorderShades.Pair(Parse("#600000")).Dark.Should().Be(BorderShades.Darker(Parse("#600000")));
    }
}
