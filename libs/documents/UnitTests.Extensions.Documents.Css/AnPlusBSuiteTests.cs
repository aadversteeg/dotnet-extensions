using System.IO;
using System.Text.Json;
using Ave.Extensions.Documents.Css.Selectors;
using Ave.Extensions.Documents.Css.Syntax;
using Xunit.Abstractions;

namespace UnitTests.Extensions.Documents.Css;

/// <summary>An+B.json of css-parsing-tests: the &lt;a-n-plus-b&gt; type of CSS Syntax 3 §6.2, every case.</summary>
public class AnPlusBSuiteTests(ITestOutputHelper output)
{
    [Fact]
    public void Every_case_passes()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(Suites.CssParsingTests, "An+B.json")));
        var items = json.RootElement.EnumerateArray().ToList();
        var failures = new List<string>();
        for (var i = 0; i < items.Count; i += 2)
        {
            var input = items[i].GetString()!;
            var expected = items[i + 1].ValueKind == JsonValueKind.Null ? "null" : $"[{items[i + 1][0].GetDouble()}, {items[i + 1][1].GetDouble()}]";
            var values = SyntaxParser.ParseComponentValues(CssSource.Of(input), (_, _) => { }).Values;
            var actual = AnPlusBParser.Parse(values) is { } anb ? $"[{(double)anb.A}, {(double)anb.B}]" : "null";
            if (actual != expected)
            {
                failures.Add($"{JsonSerializer.Serialize(input)}: expected {expected}, actual {actual}");
            }
        }

        output.WriteLine($"An+B.json: {items.Count / 2} cases, {failures.Count} failed");
        failures.ForEach(output.WriteLine);
        failures.Should().BeEmpty();
        (items.Count / 2).Should().Be(128);
    }
}
