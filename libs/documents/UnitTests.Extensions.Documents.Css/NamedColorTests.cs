using System.IO;
using Ave.Extensions.Documents.Css.Values;

namespace UnitTests.Extensions.Documents.Css;

/// <summary>
/// The named colour table (Values/NamedColors.g.cs, generated from the CC0 keyword files of css-parsing-tests by
/// tools/generate-css-named-colors.py) against Chromium 159: every name, and only those, with the colour Chromium
/// computes for it (measured into tools/reference/chromium/computed-style/measured/named-colors.txt).
/// </summary>
public class NamedColorTests
{
    [Fact]
    public void Every_named_colour_is_the_one_Chromium_computes()
    {
        var chromium = File.ReadAllLines(Suites.ChromiumNamedColors()).Select(line => line.Split(' ', 2)).ToDictionary(parts => parts[0], parts => parts[1]);

        chromium.Should().HaveCount(148);
        Colors.NamedColorNames.Should().BeEquivalentTo(chromium.Keys);
        foreach (var (name, value) in chromium)
        {
            Colors.NamedColor(name)!.Value.ToString().Should().Be(value, name);
        }
    }
}
