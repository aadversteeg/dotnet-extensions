using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Ave.Extensions.Documents.Css.Properties;
using Ave.Extensions.Documents.Fluent;

namespace UnitTests.Extensions.Documents.Architecture;

/// <summary>
/// Until stage 4 the code front-end (Fluent) keeps its own cascade and its own user-agent facts next to the HTML
/// front-end's property registry and style sheet (docs/architecture.md, known limits of stage 3). These tests tie the
/// facts both hold, so they cannot drift apart before stage 4 reads one set (review 39 R-1).
/// </summary>
public class FrontEndFactsTests
{
    [Fact]
    public void The_initial_font_size_is_the_same_in_both_front_ends() =>
        UserAgentStyles.InitialFontSize.Points.Should().Be(PropertyRegistry.InitialFontSizePoints);

    [Fact]
    public void The_heading_sizes_are_the_user_agent_style_sheets()
    {
        var sheet = File.ReadAllText(Path.Combine(ProjectRulesTests.FamilyRoot(), "Ave.Extensions.Documents.Html", "UserAgent", "html.css"));

        for (var level = 1; level <= 6; level++)
        {
            var size = Regex.Match(sheet, $@"h{level} \{{[^}}]*font-size: ([0-9.]+)em").Groups[1].Value;
            double.Parse(size, CultureInfo.InvariantCulture).Should().Be(UserAgentStyles.HeadingSizeFactor(level), $"h{level}");
        }
    }
}
