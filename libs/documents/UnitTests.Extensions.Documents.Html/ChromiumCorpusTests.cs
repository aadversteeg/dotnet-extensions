using System.IO;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Css;
using Ave.Extensions.Documents.Html;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;
using Xunit.Abstractions;

namespace UnitTests.Extensions.Documents.Html;

/// <summary>
/// The computed-style corpus (tools/reference/chromium/computed-style/cases) against Chromium 159's getComputedStyle,
/// measured unrendered into measured/&lt;case&gt;.txt: every registered longhand on every element. Values are compared
/// as text in Chromium's serialisation (<see cref="ComputedText"/>); exact, except where a stated rule says otherwise:
/// <list type="bullet">
/// <item>line-height as a number: Chromium returns the used value, number × font size, even unrendered; that product
/// of the computed values, written as Chromium writes a length, is compared.</item>
/// </list>
/// Each value is one of three outcomes: equal; refused, when the engine refuses the element or one around it (a
/// refusal at its start tag or one of its attributes: an element outside v0, a form control, a refused presentational
/// attribute), so the value does not count; or different, which fails. The number of values, and of each outcome, is
/// pinned per case, so an emptied or shortened measurement fails too; and so is what the default mode (refuse)
/// answers for the case.
/// </summary>
public class ChromiumCorpusTests(ITestOutputHelper output)
{
    /// <summary>Per case: the values measured, the values equal, the values refused, and the refusals of the default
    /// mode (0 when the case is accepted). Why a case is refused in the default mode: cascade has a media query with
    /// orientation (a feature outside v0) and a var() that puts 4px into color (Chromium makes it invalid at
    /// computed-value time, the engine cannot tell that from a value beyond the profile: the refusal policy in
    /// docs/architecture.md), and a cycle of var() references (Chromium does not make every member of a cycle
    /// invalid; both members are refused); fonts-tables-lists has break-after: always, selectors has [type=CHECKME s]
    /// and properties has text-align: match-parent, which Chromium 159 drops and the engine refuses; elements has every element outside v0, one refusal each. The values
    /// are equal in all of them, as the warnings-only resolution shows. hints-gaps and ua-extra hold elements and
    /// attributes outside v0 among the ones they test, refused one by one.</summary>
    private static readonly Dictionary<string, (int Values, int Equal, int Refused, int Refusals)> Pinned = new()
    {
        ["cascade"] = (5_438, 5_438, 0, 4),
        ["elements"] = (18_521, 13_376, 5_145, 34),
        ["fonts-tables-lists"] = (17_051, 17_051, 0, 1),
        ["hints"] = (17_198, 17_198, 0, 0),
        ["hints-gaps"] = (12_788, 12_200, 588, 4),
        ["properties"] = (16_757, 16_757, 0, 1),
        ["selectors"] = (8_966, 8_966, 0, 1),
        ["ua-bare"] = (10_289, 10_289, 0, 0),
        ["ua-extra"] = (15_875, 15_581, 294, 2),
    };

    public static TheoryData<string> Cases =>
        [.. Directory.GetFiles(Path.Combine(Styling.Reference(), "cases"), "*.html").Select(file => Path.GetFileNameWithoutExtension(file)).Order(StringComparer.Ordinal)];

    [Theory]
    [MemberData(nameof(Cases))]
    public void Every_value_is_Chromiums_or_refused(string name)
    {
        var measured = Path.Combine(Styling.Reference(), "measured", $"{name}.txt");
        File.Exists(measured).Should().BeTrue($"the case {name} is measured (computed-style/measure.ps1 -Step corpus, then copy out/{name}.txt to measured/)");
        var html = File.ReadAllText(Path.Combine(Styling.Reference(), "cases", $"{name}.html"));
        var styled = Styling.Styled(html, Styling.MeasuredPage);
        var ours = new Dictionary<string, StyledElement>(StringComparer.Ordinal);
        Paths(styled.Root, "", 1, ours);
        var refused = RefusedElements(styled);

        var (values, equal, refusedValues, failures) = (0, 0, 0, new List<string>());
        foreach (var line in File.ReadLines(measured))
        {
            values++;
            var parts = line.Split(' ', 3);
            var (path, property, chromium) = (parts[0], parts[1], parts.Length > 2 ? parts[2] : string.Empty);
            if (!ours.TryGetValue(path, out var element))
            {
                failures.Add($"{path}: no such element in the engine's tree");
                continue;
            }

            if (refused.Contains(element))
            {
                refusedValues++;
                continue;
            }

            var engine = element.Value(property);
            if (property == "line-height" && element.Style[Ave.Extensions.Documents.Css.Properties.PropertyRegistry.LineHeight] is Ave.Extensions.Documents.Css.Values.CssNumber number)
            {
                // The product of the number and the font size as computed, not as written with six significant digits
                // (a font size of 13.3333px from 16px / 1.2 times 2 is 26.6667px, as in Chromium).
                engine = ComputedText.Pixels(number.Value * ((Ave.Extensions.Documents.Css.Values.CssLength)element.Style[Ave.Extensions.Documents.Css.Properties.PropertyRegistry.FontSize]).Points / 0.75);
            }

            if (engine == chromium)
            {
                equal++;
            }
            else
            {
                failures.Add($"{path} {property}: engine {engine}, Chromium {chromium}");
            }
        }

        var refusals = Styling.Resolve(html, Styling.MeasuredPage with { Unsupported = UnsupportedPolicy.Refuse })
            .Match(_ => 0, error => error.Code == ErrorCodes.Aggregate ? error.Parts().Count() : 1);
        output.WriteLine($"{name}: {values} values: {equal} equal, {refusedValues} refused, {failures.Count} different; default mode: {refusals} refusals");
        failures.Take(200).ToList().ForEach(output.WriteLine);
        styled.Diagnostics.ToList().ForEach(error => output.WriteLine($"diagnostic: {error.Code}: {error.Message}"));
        failures.Should().BeEmpty();
        (values, equal, refusedValues, refusals).Should().Be((Pinned[name].Values, Pinned[name].Equal, Pinned[name].Refused, Pinned[name].Refusals));
    }

    [Fact]
    public void Every_case_is_pinned() =>
        Directory.GetFiles(Path.Combine(Styling.Reference(), "cases"), "*.html").Select(Path.GetFileNameWithoutExtension).Should().BeEquivalentTo(Pinned.Keys);

    /// <summary>The elements the engine refuses, with everything inside them: a refusal at an element's start tag or
    /// at one of its attributes.</summary>
    private static HashSet<StyledElement> RefusedElements(StyledHtmlDocument styled)
    {
        var places = styled.Diagnostics.Where(error => HtmlStyleDiagnostics.ClassOf(error.Code) == DiagnosticClass.Refusal
                && error.Metadata?.GetValueOrDefault(ErrorMetadataKeys.SourceFile) as string == styled.SourceName
                && error.Metadata?.GetValueOrDefault(ErrorMetadataKeys.SourceLine) is int)
            .Select(error => new TextPosition((int)error.Metadata![ErrorMetadataKeys.SourceLine]!, (int)error.Metadata![ErrorMetadataKeys.SourceColumn]!))
            .ToHashSet();
        var refused = new HashSet<StyledElement>(ReferenceEqualityComparer.Instance);
        void Walk(StyledElement element, bool inside)
        {
            inside |= places.Contains(element.Element.Position) || element.Element.Attributes.Any(attribute => places.Contains(attribute.Position));
            if (inside)
            {
                refused.Add(element);
            }

            foreach (var child in element.Children.OfType<StyledElement>())
            {
                Walk(child, inside);
            }
        }

        Walk(styled.Root, false);
        return refused;
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
