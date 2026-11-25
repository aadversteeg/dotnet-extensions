using System.Text.RegularExpressions;
using Ave.Extensions.Documents.Css.Properties;
using Ave.Extensions.Documents.Css.Syntax;
using Ave.Extensions.Documents.Css.Values;

namespace UnitTests.Extensions.Documents.Css;

public class PropertyRegistryTests
{
    [Fact]
    public void Every_shorthand_sets_registered_longhands_only()
    {
        var missing = PropertyRegistry.Shorthands.SelectMany(shorthand => shorthand.Longhands.Where(name => PropertyRegistry.Find(name) is null).Select(name => $"{shorthand.Name} → {name}"));

        missing.Should().BeEmpty();
    }

    [Fact]
    public void Every_keyword_a_description_lists_is_taken_by_the_parser_and_a_shorthand_sets_its_longhands()
    {
        var longhands = PropertyRegistry.Longhands
            .SelectMany(property => Keywords(property.Supported).Select(keyword => (property.Name, keyword, property.Parse(Reader(keyword)).IsValid)));
        var shorthands = PropertyRegistry.Shorthands
            .SelectMany(shorthand => Keywords(shorthand.Supported).Select(keyword =>
                (shorthand.Name, keyword, shorthand.Parse(Reader(string.Format(Within.GetValueOrDefault(shorthand.Name, "{0}"), keyword))).Values is { } values
                    && values.Keys.Order(StringComparer.Ordinal).SequenceEqual(shorthand.Longhands.Order(StringComparer.Ordinal)))));
        var checkedKeywords = longhands.Concat(shorthands).ToList();

        string.Join(", ", checkedKeywords.Where(entry => !entry.Item3).Select(entry => $"{entry.Name}: {entry.keyword}")).Should().BeEmpty();
        checkedKeywords.Should().HaveCountGreaterThan(200);
    }

    [Fact]
    public void A_name_is_in_one_list_only()
    {
        var names = PropertyRegistry.Longhands.Select(property => property.Name)
            .Concat(PropertyRegistry.Shorthands.Select(shorthand => shorthand.Name))
            .Concat(PropertyRegistry.Ignored.Select(ignored => ignored.Name))
            .ToList();

        names.GroupBy(name => name).Where(group => group.Count() > 1).Select(group => group.Key).Should().BeEmpty();
        names.Should().OnlyContain(name => name == name.ToLowerInvariant());
    }

    [Fact]
    public void The_registry_is_readable_from_outside_with_the_facts_of_each_property()
    {
        var margin = PropertyRegistry.Find("MARGIN-TOP")!;

        margin.Inherited.Should().BeFalse();
        margin.Initial.Should().Be(new CssLength(Ave.Extensions.Documents.Core.Length.Zero));
        margin.Support.Should().Be(PropertySupport.Supported);
        margin.Specification.Should().Contain("CSS Box Model 3");
        PropertyRegistry.Find("color")!.Inherited.Should().BeTrue();
        PropertyRegistry.Find("float")!.Support.Should().Be(PropertySupport.InitialOnly);
        PropertyRegistry.FindShorthand("border")!.Longhands.Should().HaveCount(12);
        PropertyRegistry.FindIgnored("cursor").Should().NotBeNull();
        PropertyRegistry.Find("frobnicate").Should().BeNull();
    }

    [Theory]
    [InlineData("display", "flex", "Refused")]
    [InlineData("display", "table-cell", "Valid")]
    [InlineData("display", "blocky", "Invalid")]
    [InlineData("margin-top", "-1px", "Refused")]
    [InlineData("margin-top", "auto", "Valid")]
    [InlineData("width", "calc(10px + 5%)", "Valid")]
    [InlineData("width", "min-content", "Refused")]
    [InlineData("width", "-3px", "Invalid")]
    [InlineData("border-top-style", "dashed", "Valid")]
    [InlineData("border-top-style", "dotted", "Refused")]
    [InlineData("color", "lab(50% 10 10)", "Refused")]
    [InlineData("color", "#12345", "Invalid")]
    [InlineData("font-style", "oblique", "Refused")]
    [InlineData("font-family", "\"A\", serif", "Valid")]
    [InlineData("font-family", "serif,", "Invalid")]
    [InlineData("line-height", "1.25", "Valid")]
    [InlineData("list-style-type", "lower-greek", "Refused")]
    [InlineData("vertical-align", "super", "Valid")]
    [InlineData("vertical-align", "sub", "Valid")]
    [InlineData("vertical-align", "-2.35pt", "Valid")]
    [InlineData("vertical-align", "30%", "Valid")]
    [InlineData("vertical-align", "text-top", "Refused")]
    [InlineData("vertical-align", "text-bottom", "Refused")]
    [InlineData("vertical-align", "5px 5px", "Invalid")]
    [InlineData("white-space-collapse", "preserve-breaks", "Valid")]
    [InlineData("white-space-collapse", "preserve-spaces", "Refused")]
    [InlineData("white-space-collapse", "discard", "Refused")]
    [InlineData("white-space-collapse", "break-spaces", "Valid")]
    [InlineData("text-decoration-line", "underline", "Valid")]
    [InlineData("text-decoration-line", "line-through overline underline", "Valid")]
    [InlineData("text-decoration-line", "none", "Valid")]
    [InlineData("text-decoration-line", "underline underline", "Invalid")]
    [InlineData("text-decoration-line", "none underline", "Invalid")]
    [InlineData("text-decoration-line", "underline wavy", "Invalid")]
    [InlineData("text-decoration-line", "blink", "Refused")]
    [InlineData("text-decoration-line", "underline spelling-error", "Refused")]
    [InlineData("text-decoration-line", "grammar-error", "Refused")]
    [InlineData("text-decoration-skip-ink", "auto", "Valid")]
    [InlineData("text-decoration-skip-ink", "none", "Valid")]
    [InlineData("text-decoration-skip-ink", "all", "Refused")]
    [InlineData("text-decoration-thickness", "from-font", "Refused")]
    [InlineData("text-underline-offset", "auto", "Valid")]
    [InlineData("text-underline-offset", "0", "Refused")]
    [InlineData("text-underline-position", "auto", "Valid")]
    [InlineData("text-underline-position", "under left", "Refused")]
    [InlineData("break-before", "column", "Refused")]
    [InlineData("orphans", "0", "Invalid")]
    [InlineData("margin-top", "3q", "Valid")]
    [InlineData("margin-top", "3cqw", "Refused")]
    public void Each_value_is_supported_refused_or_invalid(string property, string value, string expected)
    {
        var values = Ave.Extensions.Documents.Css.Syntax.SyntaxParser.ParseComponentValues(Ave.Extensions.Documents.Css.Syntax.CssSource.Of(value), (_, _) => { }).Values;
        var parsed = PropertyRegistry.Find(property)!.Parse(new ValueReader(values));

        (parsed.IsValid ? "Valid" : parsed.Unsupported ? "Refused" : "Invalid").Should().Be(expected, parsed.Problem);
    }

    /// <summary>The value a keyword is tried in, for a shorthand where a keyword alone is not a whole value.</summary>
    private static readonly Dictionary<string, string> Within = new() { ["font"] = "{0} 12px serif" };

    /// <summary>The bare keywords of a description: what is left after removing notes in brackets and <c>&lt;type&gt;</c> terms.</summary>
    private static IEnumerable<string> Keywords(string description)
    {
        var text = Regex.Replace(Regex.Replace(description, @"\([^)]*\)", " "), "<[^>]*>", " ");
        return Regex.Matches(text, @"(?<![\w-])-?[a-z][a-z0-9-]*(?![\w-])").Select(match => match.Value).Distinct(StringComparer.Ordinal);
    }

    private static ValueReader Reader(string text) => new(SyntaxParser.ParseComponentValues(CssSource.Of(text), (_, _) => { }).Values);
}

public class ReferenceHarnessTests(Xunit.Abstractions.ITestOutputHelper output)
{
    /// <summary>The Chromium harness measures every registered longhand (tools/reference/chromium/computed-style/
    /// properties.txt): when the registry changes, the list and the measurement change with it.</summary>
    [Fact]
    public void The_harness_measures_every_registered_longhand()
    {
        var file = System.IO.Path.GetFullPath(System.IO.Path.Combine(Suites.ChromiumWptVerdicts(), "..", "..", "properties.txt"));
        var expected = PropertyRegistry.Longhands.Select(property => property.Name).Order(StringComparer.Ordinal).ToList();
        var listed = System.IO.File.Exists(file)
            ? System.IO.File.ReadAllLines(file).Select(line => line.Trim()).Where(line => line.Length > 0 && !line.StartsWith('#')).ToList()
            : [];

        if (!listed.SequenceEqual(expected))
        {
            output.WriteLine(string.Join("\n", expected));
        }

        listed.Should().Equal(expected);
    }

}
