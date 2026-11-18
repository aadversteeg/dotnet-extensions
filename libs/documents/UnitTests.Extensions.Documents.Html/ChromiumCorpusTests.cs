using System.Globalization;
using System.IO;
using Ave.Extensions.Documents.Html;
using Xunit.Abstractions;

namespace UnitTests.Extensions.Documents.Html;

/// <summary>
/// The computed-style corpus (tools/reference/chromium/computed-style/cases) against Chromium 159's getComputedStyle,
/// measured unrendered into measured/&lt;case&gt;.txt: every registered longhand on every element. Values are compared
/// as text in Chromium's serialisation (<see cref="ComputedText"/>); exact, except where a stated rule says otherwise:
/// <list type="bullet">
/// <item>line-height as a number: Chromium returns the used value, number × font size, even unrendered; that product
/// is compared.</item>
/// </list>
/// </summary>
public class ChromiumCorpusTests(ITestOutputHelper output)
{
    public static TheoryData<string> Cases =>
        [.. Directory.GetFiles(Path.Combine(Styling.Reference(), "cases"), "*.html").Select(file => Path.GetFileNameWithoutExtension(file)).Order(StringComparer.Ordinal)];

    [Theory]
    [MemberData(nameof(Cases))]
    public void Every_value_is_Chromiums(string name)
    {
        var measured = Path.Combine(Styling.Reference(), "measured", $"{name}.txt");
        File.Exists(measured).Should().BeTrue($"the case {name} is measured (computed-style/measure.ps1 -Step corpus, then copy out/{name}.txt to measured/)");
        var html = File.ReadAllText(Path.Combine(Styling.Reference(), "cases", $"{name}.html"));
        var styled = Styling.Styled(html, Styling.MeasuredPage);
        var ours = new Dictionary<string, StyledElement>(StringComparer.Ordinal);
        Paths(styled.Root, "", 1, ours);

        var (compared, failures) = (0, new List<string>());
        foreach (var line in File.ReadLines(measured))
        {
            var parts = line.Split(' ', 3);
            var (path, property, chromium) = (parts[0], parts[1], parts.Length > 2 ? parts[2] : string.Empty);
            if (!ours.TryGetValue(path, out var element))
            {
                failures.Add($"{path}: no such element in the engine's tree");
                continue;
            }

            compared++;
            var engine = element.Value(property);
            if (property == "line-height" && element.Style["line-height"] is Ave.Extensions.Documents.Css.Values.CssNumber number)
            {
                engine = ComputedText.Pixels(number.Value * double.Parse(element.Value("font-size")[..^2], CultureInfo.InvariantCulture));
            }

            if (engine != chromium)
            {
                failures.Add($"{path} {property}: engine {engine}, Chromium {chromium}");
            }
        }

        output.WriteLine($"{name}: {compared} values compared, {failures.Count} differ");
        failures.Take(200).ToList().ForEach(output.WriteLine);
        styled.Diagnostics.ToList().ForEach(error => output.WriteLine($"diagnostic: {error.Code}: {error.Message}"));
        failures.Should().BeEmpty();
    }

    /// <summary>The element paths measure.mjs writes: /html[1]/body[2]/p[3], the index among the element children.</summary>
    private static void Paths(StyledElement element, string parent, int index, Dictionary<string, StyledElement> paths)
    {
        var path = $"{parent}/{element.Element.LocalName}[{index}]";
        paths[path] = element;
        var i = 0;
        foreach (var child in element.Children.OfType<StyledElement>())
        {
            Paths(child, path, ++i, paths);
        }
    }
}
