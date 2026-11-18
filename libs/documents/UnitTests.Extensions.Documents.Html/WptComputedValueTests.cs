using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Css;
using Ave.Extensions.Documents.Css.Cascade;
using Ave.Extensions.Documents.Css.Media;
using Ave.Extensions.Documents.Css.Properties;
using Ave.Extensions.Documents.Html;
using Ave.Extensions.Documents.Html.Parsing;
using Ave.Extensions.Functional;
using Xunit.Abstractions;

namespace UnitTests.Extensions.Documents.Html;

/// <summary>
/// The test_computed_value cases of web-platform-tests (assets/test-suites/wpt/css/cases.json), against the value
/// Chromium 159 computes for them (measured into tools/reference/chromium/computed-style/measured/wpt-css-verdicts.json):
/// the test file's markup is parsed and styled, with the specified value in #target's style attribute, as the WPT
/// helper sets it. Skipped by stated rule, and counted: a file with no #target in its markup (its script makes it); a
/// call whose first argument is not a property (the file defines a helper of its own with that name); a property
/// outside the registry; a declaration the engine refuses (outside v0); and, for the properties whose resolved value
/// is the used value when rendered (CSSOM §9: width, height, margins, paddings, line-height), a case where Chromium
/// returns layout's px for a value the engine keeps as computed.
/// </summary>
public class WptComputedValueTests(ITestOutputHelper output)
{
    private static readonly string[] UsedValueProperties =
        ["width", "height", "min-width", "min-height", "max-width", "max-height", "line-height",
         "margin-top", "margin-right", "margin-bottom", "margin-left", "padding-top", "padding-right", "padding-bottom", "padding-left"];

    /// <summary>The page the WPT cases were measured on: Playwright's default viewport, 1280 × 720 CSS px, screen.</summary>
    private static readonly HtmlStyleOptions Screen = Styling.Warn with
    {
        Media = new MediaContext("screen", Length.FromPoints(1280 * 0.75), Length.FromPoints(720 * 0.75)),
        Values = new ValueContext(Length.FromPoints(1280 * 0.75), Length.FromPoints(720 * 0.75)),
    };

    [Fact]
    public void Computed_values_are_Chromiums()
    {
        using var cases = JsonDocument.Parse(File.ReadAllText(Path.Combine(Styling.Reference(), "..", "..", "..", "..", "assets", "test-suites", "wpt", "css", "cases.json")));
        using var measured = JsonDocument.Parse(File.ReadAllText(Path.Combine(Styling.Reference(), "measured", "wpt-css-verdicts.json")));
        var markup = cases.RootElement.GetProperty("markup");
        var items = cases.RootElement.GetProperty("cases").EnumerateArray().Where(item => item.GetProperty("helper").GetString() == "test_computed_value").ToList();
        var chromium = measured.RootElement.GetProperty("computed").EnumerateArray().ToList();
        chromium.Should().HaveCount(items.Count);

        var skipped = new Dictionary<string, int>();
        void Skip(string rule) => skipped[rule] = skipped.GetValueOrDefault(rule) + 1;
        var (passed, failures) = (0, new List<string>());
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var place = $"{item.GetProperty("file").GetString()}:{item.GetProperty("line").GetInt32()}";
            chromium[i].GetProperty("at").GetString().Should().Be(place);
            var args = item.GetProperty("args");
            var property = args[0].GetString()!;
            var specified = args[1].GetString()!;
            if (chromium[i].GetProperty("chromium").ValueKind == JsonValueKind.Null)
            {
                Skip("no #target in the markup (the script makes it)");
                continue;
            }

            if (!Regex.IsMatch(property, "^-?[a-z][a-z-]*$"))
            {
                Skip("not a property (the file has its own helper of that name)");
                continue;
            }

            if (PropertyRegistry.Find(property) is null)
            {
                Skip("property outside the registry");
                continue;
            }

            var expected = chromium[i].GetProperty("chromium").GetString()!;
            var document = WithTargetStyle(Styling.Parsed(markup.GetProperty(item.GetProperty("file").GetString()!).GetString()!), $"{property}: {specified}");
            var styled = HtmlStyleResolver.Resolve(document, Screen).Match(result => result, error => throw new InvalidOperationException(error.ToString()));
            var target = styled.Root.Elements().First(element => element.Element.Attribute("id") == "target");
            if (styled.Diagnostics.Any(error => HtmlStyleDiagnostics.ClassOf(error.Code) == DiagnosticClass.Refusal
                    && error.Metadata?.GetValueOrDefault(ErrorMetadataKeys.Property) as string == property))
            {
                Skip("refused (outside v0)");
                continue;
            }

            var engine = target.Value(property);
            if (engine == expected)
            {
                passed++;
            }
            else if (UsedValueProperties.Contains(property) && expected.EndsWith("px", StringComparison.Ordinal) && !engine.EndsWith("px", StringComparison.Ordinal))
            {
                Skip("Chromium returns the used value (layout)");
            }
            else
            {
                failures.Add($"{place} {property}: {specified} → engine {engine}, Chromium {expected}");
            }
        }

        output.WriteLine($"test_computed_value: {items.Count} cases, {passed} equal Chromium, {failures.Count} differ; skipped: "
            + string.Join("; ", skipped.Select(pair => $"{pair.Value} {pair.Key}")));
        failures.ForEach(output.WriteLine);
        failures.Should().BeEmpty();
    }

    /// <summary>The document with the declaration added to #target's style attribute.</summary>
    private static HtmlParseResult WithTargetStyle(HtmlParseResult parsed, string declaration)
    {
        HtmlNode Rewrite(HtmlNode node) => node switch
        {
            HtmlElement { } element when element.Attribute("id") == "target" => element with
            {
                Attributes = [.. element.Attributes.Where(attribute => attribute.LocalName != "style"),
                    new HtmlAttribute(AttributeNamespace.None, null, "style", (element.Attribute("style") is { } style ? style + "; " : string.Empty) + declaration, element.Position)],
            },
            HtmlElement element => element with { Children = [.. element.Children.Select(Rewrite)] },
            _ => node,
        };

        return parsed with { Document = parsed.Document with { Children = [.. parsed.Document.Children.Select(Rewrite)] } };
    }
}
