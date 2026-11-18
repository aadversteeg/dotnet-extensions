using System.IO;
using System.Text.Json;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Css;
using Ave.Extensions.Documents.Css.Cascade;
using Ave.Extensions.Documents.Css.Properties;
using Ave.Extensions.Documents.Css.Selectors;
using Ave.Extensions.Documents.Css.Syntax;
using Xunit.Abstractions;

namespace UnitTests.Extensions.Documents.Css;

/// <summary>
/// The value and selector cases of web-platform-tests (css/selectors, css-values, css-variables, css-cascade,
/// css-syntax), extracted by tools/extract-wpt-css-cases.py into assets/test-suites/wpt/css/cases.json:
/// test_valid_value, test_invalid_value, test_valid_selector and test_invalid_selector. The expected answer is
/// Chromium's (the reference, measured by tools/reference/chromium/computed-style into
/// measured/wpt-css-verdicts.json), not the test's: WPT also holds tests of drafts Chromium 159 does not implement
/// (random(), {}-wrapped var() arguments), and the engine follows Chromium. Where Chromium and the test disagree the
/// cases are counted. Whether a value or selector is valid is compared; the CSSOM serialisation the tests also check is
/// not (the engine has no CSSOM), by rule. test_computed_value needs the test's markup and runs in the HTML front-end's
/// tests.
/// <para>Each case has one of three outcomes. Equal: Chromium takes it and the engine does, or Chromium drops it and
/// the engine drops it with a warning. Refused: the engine refuses it, whatever Chromium says (valid CSS outside the
/// profile, or input the engine cannot read, review 39 ruling D-b); never counted as agreeing. Different: the engine
/// takes what Chromium drops or drops what Chromium takes; it fails. The number of each is pinned.</para>
/// </summary>
public class WptCssSuiteTests(ITestOutputHelper output)
{
    private enum Verdict
    {
        Taken,
        Refused,
        Dropped,
        OutsideRegistry,
    }

    private static readonly JsonDocument Cases = JsonDocument.Parse(File.ReadAllText(Suites.WptCssCases));

    private static IEnumerable<JsonElement> Of(string helper) =>
        Cases.RootElement.GetProperty("cases").EnumerateArray().Where(item => item.GetProperty("helper").GetString() == helper);

    private static string Place(JsonElement item) => $"{item.GetProperty("file").GetString()}:{item.GetProperty("line").GetInt32()}";

    /// <summary>The CSSOM name of a property: style["marginTop"] is margin-top.</summary>
    private static string PropertyName(string name) =>
        name.StartsWith("--", StringComparison.Ordinal) ? name : string.Concat(name.Select(c => char.IsAsciiLetterUpper(c) ? "-" + char.ToLowerInvariant(c) : c.ToString()));

    /// <summary>What the engine makes of <c>e.style[property] = value</c>: the value is read as component values
    /// (CSSOM "set a CSS declaration") and prepared as the cascade prepares an author declaration.</summary>
    private static Verdict Judge(string property, string value)
    {
        var name = PropertyName(property);
        if (!name.StartsWith("--", StringComparison.Ordinal) && name != "all" && PropertyRegistry.Find(name) is null && PropertyRegistry.FindShorthand(name) is null)
        {
            return Verdict.OutsideRegistry;
        }

        var (values, tooDeep) = SyntaxParser.ParseComponentValues(CssSource.Of(value), (_, _) => { });
        if (tooDeep is not null || values.Any(item => item is CssToken { Kind: CssTokenKind.Semicolon or CssTokenKind.BadString or CssTokenKind.BadUrl or CssTokenKind.CloseParen or CssTokenKind.CloseSquare or CssTokenKind.CloseCurly })
            || ImportantAtEnd(values))
        {
            return Verdict.Dropped;
        }

        var diagnostics = new DiagnosticCollector();
        var order = 0;
        var declaration = new Declaration(name, values, false, TextPosition.Start) { OriginalText = name.StartsWith("--", StringComparison.Ordinal) ? value : null };
        var block = DeclarationPreparer.Prepare([declaration], "wpt", CascadeOrigin.Author, ref order, diagnostics);
        var found = diagnostics.ToValueList();
        if (found.Any(error => CssDiagnostics.ClassOf(error.Code) == DiagnosticClass.Refusal) || block.Entries.Any(entry => entry.Value is RefusedEntry))
        {
            return Verdict.Refused;
        }

        return found.IsEmpty ? Verdict.Taken : Verdict.Dropped;
    }

    private static bool ImportantAtEnd(ValueList<ComponentValue> values)
    {
        var tokens = values.Where(item => item is not CssToken { Kind: CssTokenKind.Whitespace }).ToList();
        return tokens.Count >= 2 && tokens[^1] is CssToken last && last.IsIdent("important") && tokens[^2] is CssToken bang && bang.IsDelim('!');
    }

    private static readonly JsonDocument Chromium = JsonDocument.Parse(File.ReadAllText(Suites.ChromiumWptVerdicts()));

    /// <summary>The value and selector cases with Chromium's verdict, in the order of cases.json.</summary>
    private static List<(JsonElement Case, bool Chromium)> Measured()
    {
        var cases = Cases.RootElement.GetProperty("cases").EnumerateArray().Where(item => item.GetProperty("helper").GetString() != "test_computed_value").ToList();
        var verdicts = Chromium.RootElement.GetProperty("values").EnumerateArray().ToList();
        verdicts.Should().HaveCount(cases.Count, "measured/wpt-css-verdicts.json must be measured from this cases.json");
        return [.. cases.Zip(verdicts, (item, verdict) =>
        {
            verdict.GetProperty("at").GetString().Should().Be(Place(item), "measured/wpt-css-verdicts.json must be measured from this cases.json");
            return (item, verdict.GetProperty("chromium").GetBoolean());
        })];
    }

    [Fact]
    public void Values_are_taken_where_Chromium_takes_them_dropped_where_it_drops_them_or_refused()
    {
        var (equal, refusedTaken, refusedDropped, outside, testDiffers, failures) = (0, 0, 0, 0, 0, new List<string>());
        foreach (var (item, chromium) in Measured().Where(pair => pair.Case.GetProperty("helper").GetString()!.EndsWith("_value", StringComparison.Ordinal)))
        {
            var args = item.GetProperty("args");
            var (property, value) = (args[0].GetString()!, args[1].GetString()!);
            var verdict = Judge(property, value);
            testDiffers += chromium != (item.GetProperty("helper").GetString() == "test_valid_value") ? 1 : 0;
            switch (verdict)
            {
                case Verdict.OutsideRegistry:
                    outside++;
                    break;
                case Verdict.Refused:
                    _ = chromium ? refusedTaken++ : refusedDropped++;
                    break;
                case Verdict.Taken when chromium:
                case Verdict.Dropped when !chromium:
                    equal++;
                    break;
                default:
                    failures.Add($"{Place(item)} {property}: {value} → engine {verdict}, Chromium {(chromium ? "takes it" : "drops it")}");
                    break;
            }
        }

        output.WriteLine($"values: {equal} equal, {refusedTaken + refusedDropped} refused ({refusedTaken} that Chromium takes, {refusedDropped} that it drops), "
            + $"{failures.Count} different; {outside} outside the registry; Chromium disagrees with the test in {testDiffers}");
        failures.ForEach(output.WriteLine);
        failures.Should().BeEmpty();
        (equal, refusedTaken, refusedDropped, outside, testDiffers).Should().Be((17, 144, 253, 45, 52));
    }

    /// <summary>What the engine makes of a selector: taken (it parses, and every part is supported or never matches in a
    /// static document), or refused (it does not parse, or a part is outside the profile).</summary>
    private static bool SelectorTaken(string selector)
    {
        var (values, tooDeep) = SyntaxParser.ParseComponentValues(CssSource.Of(selector), (_, _) => { });
        return tooDeep is null && SelectorParser.Parse(values, relative: false, TextPosition.Start) is { List: not null } parsed
            && parsed.Findings.All(finding => finding.Support != SelectorSupport.Unsupported);
    }

    [Fact]
    public void Selectors_are_taken_where_Chromium_takes_them_or_refused()
    {
        var (equal, refusedTaken, refusedDropped, testDiffers, failures) = (0, 0, 0, 0, new List<string>());
        foreach (var (item, chromium) in Measured().Where(pair => pair.Case.GetProperty("helper").GetString()!.EndsWith("_selector", StringComparison.Ordinal)))
        {
            var selector = item.GetProperty("args")[0].GetString()!;
            testDiffers += chromium != (item.GetProperty("helper").GetString() == "test_valid_selector") ? 1 : 0;
            var taken = SelectorTaken(selector);
            if (taken && chromium)
            {
                equal++;
            }
            else if (taken)
            {
                failures.Add($"{Place(item)} {selector} → engine takes it, Chromium drops it");
            }
            else
            {
                _ = chromium ? refusedTaken++ : refusedDropped++;
            }
        }

        output.WriteLine($"selectors: {equal} equal, {refusedTaken + refusedDropped} refused ({refusedTaken} that Chromium takes, {refusedDropped} that it drops), "
            + $"{failures.Count} different; Chromium disagrees with the test in {testDiffers}");
        failures.ForEach(output.WriteLine);
        failures.Should().BeEmpty();
        (equal, refusedTaken, refusedDropped, testDiffers).Should().Be((101, 68, 134, 56));
    }
}
