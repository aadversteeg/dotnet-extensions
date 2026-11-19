using System.IO;

namespace UnitTests.Extensions.Documents.Html.Boxes;

/// <summary>
/// Every Chromium reference case through the HTML path, against the committed status file
/// (<c>tools/reference/chromium/html-status.txt</c>): one line per case, <c>equal</c> (the same pages, the same words on
/// each page in the same order, every word within <see cref="HtmlCases.Bound"/> of Chromium's) or <c>refused</c> with
/// the first refusal's code and what it names. A case that lays out and differs fails, and so does a status that changes
/// without the file changing: the file is the list of what the HTML front-end draws, and what it refuses is the work
/// list of the packages after this one. With <c>DOCUMENTS_UPDATE_HTML_STATUS=1</c> the test writes the file instead.
/// </summary>
public class CaseStatusTests
{
    [Fact]
    public void Every_case_is_equal_to_Chromium_or_refused_as_the_status_file_says()
    {
        var statuses = HtmlCases.Names.Select(name => $"{name} {HtmlCases.Status(name)}").ToList();
        if (Environment.GetEnvironmentVariable("DOCUMENTS_UPDATE_HTML_STATUS") == "1")
        {
            File.WriteAllText(HtmlCases.StatusFile, string.Join("\n", statuses) + "\n");
        }

        statuses.Where(status => !status.Contains(" equal", StringComparison.Ordinal) && !status.Contains(" refused ", StringComparison.Ordinal) && !status.Contains(" listed ", StringComparison.Ordinal))
            .Should().BeEmpty("a case that lays out is laid out as Chromium prints it");
        File.ReadAllLines(HtmlCases.StatusFile).Should().Equal(statuses, "the status of each case is the committed one");
    }

    [Fact]
    public void At_least_ten_cases_are_equal_to_Chromium() =>
        File.ReadAllLines(HtmlCases.StatusFile).Count(line => line.EndsWith(" equal", StringComparison.Ordinal)).Should().BeGreaterThanOrEqualTo(10);
}
