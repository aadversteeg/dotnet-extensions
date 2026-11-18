using System.IO;
using System.Text.Json;
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
/// helper sets it. Each case has one of three outcomes: equal; refused, when the engine refuses the declaration at
/// #target's style attribute (outside v0, or input it cannot read: review 39 ruling D-b), never counted as equal; or
/// different, which fails. Skipped by stated rule, and counted: a call of a function the test file defines itself
/// under the helper's name (the viewport-units files: their markup has no #target, review 39 SU-4); a property outside
/// the registry. The number of each is pinned.
/// </summary>
public class WptComputedValueTests(ITestOutputHelper output)
{
    /// <summary>The files that define a function of their own named test_computed_value (their cases are not the WPT
    /// helper's, and their markup has no #target).</summary>
    private static readonly string[] OwnHelperFiles =
    [
        "css/css-values/viewport-units-compute.html", "css/css-values/viewport-units-scrollbars-compute.html",
        "css/css-values/viewport-units-extreme-scale.html",
    ];

    /// <summary>The page the WPT cases were measured on: Playwright's default viewport, 1280 × 720 CSS px, screen.</summary>
    private static readonly HtmlStyleOptions Screen = Styling.Warn with
    {
        Media = new MediaContext("screen", Length.FromPoints(1280 * 0.75), Length.FromPoints(720 * 0.75)),
        Values = new ValueContext(Length.FromPoints(1280 * 0.75), Length.FromPoints(720 * 0.75)),
    };

    [Fact]
    public void Computed_values_are_Chromiums_or_refused()
    {
        using var cases = JsonDocument.Parse(File.ReadAllText(Path.Combine(Styling.Reference(), "..", "..", "..", "..", "assets", "test-suites", "wpt", "css", "cases.json")));
        using var measured = JsonDocument.Parse(File.ReadAllText(Path.Combine(Styling.Reference(), "measured", "wpt-css-verdicts.json")));
        var markup = cases.RootElement.GetProperty("markup");
        var items = cases.RootElement.GetProperty("cases").EnumerateArray().Where(item => item.GetProperty("helper").GetString() == "test_computed_value").ToList();
        var chromium = measured.RootElement.GetProperty("computed").EnumerateArray().ToList();
        chromium.Should().HaveCount(items.Count);

        var (ownHelper, outside, equal, refused, failures) = (0, 0, 0, 0, new List<string>());
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var file = item.GetProperty("file").GetString()!;
            var place = $"{file}:{item.GetProperty("line").GetInt32()}";
            chromium[i].GetProperty("at").GetString().Should().Be(place);
            var args = item.GetProperty("args");
            var property = args[0].GetString()!;
            var specified = args[1].GetString()!;
            if (OwnHelperFiles.Contains(file))
            {
                chromium[i].GetProperty("chromium").ValueKind.Should().Be(JsonValueKind.Null, "a file of its own helper has no #target");
                ownHelper++;
                continue;
            }

            if (PropertyRegistry.Find(property) is null)
            {
                outside++;
                continue;
            }

            var expected = chromium[i].GetProperty("chromium").GetString()!;
            var document = WithTargetStyle(Styling.Parsed(markup.GetProperty(file).GetString()!), $"{property}: {specified}");
            var styled = HtmlStyleResolver.Resolve(document, Screen).Match(result => result, error => throw new InvalidOperationException(error.ToString()));
            var target = styled.Root.Elements().First(element => element.Element.Attribute("id") == "target");
            var at = target.Element.Position;
            if (styled.Diagnostics.Any(error => HtmlStyleDiagnostics.ClassOf(error.Code) == DiagnosticClass.Refusal
                    && Equals(error.Metadata?.GetValueOrDefault(ErrorMetadataKeys.SourceLine), at.Line)
                    && Equals(error.Metadata?.GetValueOrDefault(ErrorMetadataKeys.SourceColumn), at.Column)))
            {
                refused++;
                continue;
            }

            var engine = target.Value(property);
            if (engine == expected)
            {
                equal++;
            }
            else
            {
                failures.Add($"{place} {property}: {specified} → engine {engine}, Chromium {expected}");
            }
        }

        output.WriteLine($"test_computed_value: {items.Count} cases: {equal} equal, {refused} refused, {failures.Count} different; skipped: "
            + $"{ownHelper} of a file's own helper, {outside} outside the registry");
        failures.ForEach(output.WriteLine);
        failures.Should().BeEmpty();
        (items.Count, equal, refused, ownHelper, outside).Should().Be((178, 2, 99, 72, 5));
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
