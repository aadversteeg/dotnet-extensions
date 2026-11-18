using System.IO;
using System.Text.Json;
using Ave.Extensions.Documents.Css.Syntax;
using Ave.Extensions.Documents.Css.Values;
using Xunit.Abstractions;

namespace UnitTests.Extensions.Documents.Css;

/// <summary>
/// Every colour file of css-parsing-tests. Each input is one &lt;color&gt;; the expected result is its serialisation, or
/// null when it is not a colour. The colour functions the engine refuses (color(), hwb(), lab(), lch(), oklab(),
/// oklch(), color-mix()) are refused in their files, and that is counted: no file is left out. The suite writes alpha with six decimals; CSS Color 4 §15.2 asks
/// for the shortest decimal that rounds to the same 8-bit value, so alpha is compared as that byte. The suite keeps
/// fractional channels for hsl(); Chromium 159 stores rgb() and hsl() colours with 8-bit channels and serialises them
/// rounded (in the pinned container hsl(0 0% 12.5%) is rgb(32, 32, 32)), and so does the engine, so channels are
/// compared after rounding the expected ones half away from zero. A colour the engine refuses (a <c>none</c> component,
/// a hue past its range) is its own outcome, never "agree": its number per file is pinned.
/// </summary>
public class ColorSuiteTests(ITestOutputHelper output)
{
    public static TheoryData<string> Files => [.. Pinned.Keys.Order(StringComparer.Ordinal)];

    /// <summary>The cases of each file, and those the engine refuses, counted when they were last read.</summary>
    private static readonly Dictionary<string, (int Cases, int Refused)> Pinned = new()
    {
        ["color_function_4.json"] = (219, 219), ["color_functions_5.json"] = (20, 20), ["color_hexadecimal_3.json"] = (81, 0),
        ["color_hexadecimal_4.json"] = (324, 0), ["color_hsl_3.json"] = (256, 0), ["color_hsl_4.json"] = (500, 244),
        ["color_hwb_4.json"] = (500, 500), ["color_keywords_3.json"] = (160, 0), ["color_keywords_4.json"] = (1, 0),
        ["color_lab_4.json"] = (1500, 1500), ["color_lch_4.json"] = (1500, 1500), ["color_oklab_4.json"] = (1500, 1500),
        ["color_oklch_4.json"] = (1500, 1500),
    };

    [Fact]
    public void Every_colour_file_is_run() =>
        Directory.GetFiles(Suites.CssParsingTests, "color_*.json").Select(Path.GetFileName).Should().BeEquivalentTo(Pinned.Keys);

    [Theory]
    [MemberData(nameof(Files))]
    public void Every_case_of_the_file_passes(string file)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(Suites.CssParsingTests, file)));
        var items = json.RootElement.EnumerateArray().ToList();
        var failures = new List<string>();
        var refused = 0;
        for (var i = 0; i < items.Count; i += 2)
        {
            var input = items[i].GetString()!;
            // A light-dark() case expects its light and dark value; the engine's colour scheme is light.
            var answer = items[i + 1].ValueKind == JsonValueKind.Array ? items[i + 1][0] : items[i + 1];
            var expected = answer.ValueKind == JsonValueKind.Null ? "null" : answer.GetString()!;
            var actual = Serialise(input);
            if (actual == "refused")
            {
                refused++;
            }
            else if (!SameColor(actual, expected))
            {
                failures.Add($"{JsonSerializer.Serialize(input)}: expected {expected}, actual {actual}");
            }
        }

        output.WriteLine($"{file}: {items.Count / 2} cases, {items.Count / 2 - refused - failures.Count} equal, {refused} refused, {failures.Count} failed");
        failures.Take(40).ToList().ForEach(output.WriteLine);
        failures.Should().BeEmpty();
        (items.Count / 2, refused).Should().Be(Pinned[file]);
    }

    /// <summary>Equal colours: channels rounded half away from zero, alpha compared as round(alpha × 255).</summary>
    private static bool SameColor(string actual, string expected)
    {
        if (actual == expected)
        {
            return true;
        }

        static (int R, int G, int B, int A)? Parse(string text)
        {
            var open = text.IndexOf('(');
            if (open < 0 || !text.EndsWith(')'))
            {
                return null;
            }

            var parts = text[(open + 1)..^1].Split(',').Select(part => double.Parse(part, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            int Round(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);
            return (Round(parts[0]), Round(parts[1]), Round(parts[2]), parts.Length == 4 ? (int)Math.Round(parts[3] * 255) : 255);
        }

        return Parse(actual) is { } a && Parse(expected) is { } e && a == e;
    }

    private static string Serialise(string input)
    {
        var values = SyntaxParser.ParseComponentValues(CssSource.Of(input), (_, _) => { }).Values
            .Where(value => value is not CssToken { Kind: CssTokenKind.Whitespace }).ToList();
        if (values.Count != 1)
        {
            return "null";
        }

        return Colors.Read(values[0], out var color, out _) switch
        {
            ColorOutcome.Color => color.ToString(),
            ColorOutcome.CurrentColor => "currentcolor",
            ColorOutcome.Unsupported => "refused",
            _ => "null",
        };
    }
}
