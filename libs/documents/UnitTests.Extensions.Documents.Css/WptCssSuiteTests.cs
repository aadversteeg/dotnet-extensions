using System.IO;
using System.Text.Json;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Css.Cascade;
using Ave.Extensions.Documents.Css.Properties;
using Ave.Extensions.Documents.Css.Selectors;
using Ave.Extensions.Documents.Css.Syntax;
using Ave.Extensions.ErrorPaths;
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
/// <para>A value of a feature outside v0 (a property supported at its initial value only, a math function the engine
/// does not evaluate) is refused without its grammar being checked. Where Chromium finds such a value invalid the
/// engine refuses it rather than drops it: never silently different, at worst refused when it need not be. Those
/// cases are counted and listed, not failed; a value Chromium takes that the engine drops, or one Chromium drops that
/// the engine takes, fails.</para>
/// </summary>
public class WptCssSuiteTests(ITestOutputHelper output)
{
    private enum Verdict
    {
        Valid,
        Refused,
        Invalid,
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
            return Verdict.Invalid;
        }

        var diagnostics = new List<Error>();
        var order = 0;
        var declaration = new Declaration(name, values, false, TextPosition.Start) { OriginalText = name.StartsWith("--", StringComparison.Ordinal) ? value : null };
        var block = DeclarationPreparer.Prepare([declaration], "wpt", CascadeOrigin.Author, ref order, diagnostics);
        if (diagnostics.Count > 0)
        {
            return Verdict.Invalid;
        }

        return block.Entries.Any(entry => entry.Value is RefusedEntry) ? Verdict.Refused : Verdict.Valid;
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
    public void Values_are_valid_where_Chromium_takes_them_and_invalid_where_it_drops_them()
    {
        var (taken, outside, refused, testDiffers, failures, needlessly) = (0, 0, 0, 0, new List<string>(), new List<string>());
        foreach (var (item, chromium) in Measured().Where(pair => pair.Case.GetProperty("helper").GetString()!.EndsWith("_value", StringComparison.Ordinal)))
        {
            var args = item.GetProperty("args");
            var (property, value) = (args[0].GetString()!, args[1].GetString()!);
            var verdict = Judge(property, value);
            if (verdict == Verdict.OutsideRegistry)
            {
                outside++;
                continue;
            }

            taken++;
            testDiffers += chromium != (item.GetProperty("helper").GetString() == "test_valid_value") ? 1 : 0;
            var line = $"{Place(item)} {property}: {value} → engine {verdict}, Chromium {(chromium ? "valid" : "invalid")}";
            if (!chromium && verdict == Verdict.Refused)
            {
                needlessly.Add(line);
            }
            else if (chromium ? verdict == Verdict.Invalid : verdict != Verdict.Invalid)
            {
                failures.Add(line);
            }
            else
            {
                refused += verdict == Verdict.Refused ? 1 : 0;
            }
        }

        output.WriteLine($"values: {taken} taken, {taken - failures.Count - needlessly.Count} agree with Chromium ({refused} of them valid and refused as outside v0), "
            + $"{needlessly.Count} refused where Chromium drops them (outside v0, grammar not checked), {outside} skipped (property outside the registry); "
            + $"Chromium disagrees with the test in {testDiffers}; {failures.Count} failed");
        failures.ForEach(output.WriteLine);
        output.WriteLine("Refused where Chromium drops them:");
        needlessly.ForEach(output.WriteLine);
        failures.Should().BeEmpty();
    }

    private static bool SelectorParses(string selector)
    {
        var (values, tooDeep) = SyntaxParser.ParseComponentValues(CssSource.Of(selector), (_, _) => { });
        return tooDeep is null && SelectorParser.Parse(values, relative: false, TextPosition.Start).List is not null;
    }

    [Fact]
    public void Selectors_parse_where_Chromium_takes_them()
    {
        var (taken, testDiffers, failures) = (0, 0, new List<string>());
        foreach (var (item, chromium) in Measured().Where(pair => pair.Case.GetProperty("helper").GetString()!.EndsWith("_selector", StringComparison.Ordinal)))
        {
            taken++;
            var selector = item.GetProperty("args")[0].GetString()!;
            testDiffers += chromium != (item.GetProperty("helper").GetString() == "test_valid_selector") ? 1 : 0;
            if (SelectorParses(selector) != chromium)
            {
                failures.Add($"{Place(item)} {selector} → engine {(chromium ? "invalid" : "valid")}, Chromium {(chromium ? "valid" : "invalid")}");
            }
        }

        output.WriteLine($"selectors: {taken} taken, {taken - failures.Count} agree with Chromium; Chromium disagrees with the test in {testDiffers}; {failures.Count} failed");
        failures.ForEach(output.WriteLine);
        failures.Should().BeEmpty();
    }
}
