using System.IO;
using Xunit.Abstractions;

namespace UnitTests.Extensions.Documents.Html.Boxes;

/// <summary>
/// Every Chromium reference case through the HTML path, against the committed status file
/// (<c>tools/reference/chromium/html-status.txt</c>): one line per case, <c>name status rest</c>, where the status, the
/// second field, is <c>equal</c> (every fact of the case's measured files within its bound, <see cref="HtmlCases"/>),
/// <c>listed</c> with the test that pins the limit and the difference it lists, <c>refused</c> with the first refusal's
/// code, what it names, and its line and column, or <c>open</c> with a known difference, engine against Chromium. A case
/// that lays out and differs otherwise fails, and so does a status that changes without the file changing: the file is
/// the list of what the HTML front-end draws, and what it refuses is the work list of the packages after this one. With
/// <c>DOCUMENTS_UPDATE_HTML_STATUS=1</c> the test writes the file instead; a case is <c>open</c> only where the committed
/// file already says so, so a written file never accepts a new difference.
/// </summary>
public class CaseStatusTests(ITestOutputHelper output)
{
    [Fact]
    public void Every_case_is_equal_to_Chromium_or_refused_as_the_status_file_says()
    {
        var committed = File.ReadAllLines(HtmlCases.StatusFile).Select(HtmlCases.Fields).ToDictionary(fields => fields.Name, fields => fields.Status, StringComparer.Ordinal);
        var statuses = HtmlCases.Names.Select(name => $"{name} {HtmlCases.Status(name, committed.GetValueOrDefault(name))}").ToList();
        if (Environment.GetEnvironmentVariable("DOCUMENTS_UPDATE_HTML_STATUS") == "1")
        {
            File.WriteAllText(HtmlCases.StatusFile, string.Join("\n", statuses) + "\n");
        }

        var open = statuses.Where(status => HtmlCases.Fields(status).Status == "open").ToList();
        output.WriteLine($"{open.Count} open: {string.Join("; ", open)}");
        statuses.Where(status => !HtmlCases.Accepted.Contains(HtmlCases.Fields(status).Status))
            .Should().BeEmpty("a case that lays out is laid out as Chromium prints it");
        File.ReadAllLines(HtmlCases.StatusFile).Should().Equal(statuses, "the status of each case is the committed one");
    }

    [Fact]
    public void At_least_ten_cases_are_equal_to_Chromium() =>
        File.ReadAllLines(HtmlCases.StatusFile).Count(line => HtmlCases.Fields(line).Status == "equal").Should().BeGreaterThanOrEqualTo(10);

    [Theory]
    [InlineData("a different: equal, but listed as a difference", "different:")]
    [InlineData("a different x0 1, Chromium 2 equal", "different")]
    [InlineData("a failed Documents.Css.Parse refused listed open", "failed")]
    [InlineData("a equal", "equal")]
    [InlineData("a", "")]
    public void A_status_is_its_second_field_whatever_the_rest_says(string line, string status)
    {
        HtmlCases.Fields(line).Status.Should().Be(status);
        HtmlCases.Accepted.Contains(HtmlCases.Fields(line).Status).Should().Be(status is "equal");
    }
}
