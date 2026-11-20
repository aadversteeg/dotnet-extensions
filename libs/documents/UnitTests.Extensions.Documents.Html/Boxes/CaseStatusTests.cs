using System.IO;
using Ave.Extensions.Documents.TestSupport;
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
/// file already says so, so a written file never accepts a new difference. The whole documents
/// (<c>tools/reference/chromium/documents</c>) have a status file of their own, compared and written the same way.
/// </summary>
public class CaseStatusTests(ITestOutputHelper output)
{
    [Fact]
    public void Every_case_is_equal_to_Chromium_or_refused_as_the_status_file_says() => StatusesAreCommitted(HtmlCases.Cases);

    /// <summary>The timesheets (one page, a normal month, a long month) and the invoice, each compared whole, with the
    /// same comparison as a case, against <c>documents/html-status.txt</c>.</summary>
    [Fact]
    public void Every_document_is_equal_to_Chromium_or_refused_as_its_status_file_says()
    {
        HtmlCases.Documents.Names.Should().Equal("invoice", "timesheet-normal", "timesheet-one-page", "timesheet-pages");
        StatusesAreCommitted(HtmlCases.Documents);
    }

    private void StatusesAreCommitted(CaseFolder folder)
    {
        var committed = File.ReadAllLines(folder.StatusFile).Select(HtmlCases.Fields).ToDictionary(fields => fields.Name, fields => fields.Status, StringComparer.Ordinal);
        var statuses = folder.Names.Select(name => $"{name} {HtmlCases.Status(folder, name, committed.GetValueOrDefault(name))}").ToList();
        if (Environment.GetEnvironmentVariable("DOCUMENTS_UPDATE_HTML_STATUS") == "1")
        {
            File.WriteAllText(folder.StatusFile, string.Join("\n", statuses) + "\n");
        }

        var open = statuses.Where(status => HtmlCases.Fields(status).Status == "open").ToList();
        output.WriteLine($"{open.Count} open: {string.Join("; ", open)}");
        statuses.Where(status => !HtmlCases.Accepted.Contains(HtmlCases.Fields(status).Status))
            .Should().BeEmpty("a file that lays out is laid out as Chromium prints it");
        File.ReadAllLines(folder.StatusFile).Should().Equal(statuses, "the status of each file is the committed one");
    }

    [Fact]
    public void At_least_ten_cases_are_equal_to_Chromium() =>
        File.ReadAllLines(HtmlCases.StatusFile).Count(line => HtmlCases.Fields(line).Status == "equal").Should().BeGreaterThanOrEqualTo(10);

    /// <summary>The three prints with a border Chromium strokes (one rectangle path, all four sides) are refused by the
    /// HTML path today, so no status line compares them; the comparison reads their strokes as what they paint: the same
    /// area as four filled sides, and a difference without them.</summary>
    [Theory]
    [InlineData("fr-header-cell-margin")]
    [InlineData("kb-v8-padded-box-kept-para")]
    [InlineData("kb-v9-padded-box-kept-table-row")]
    public void A_stroked_border_paints_what_four_filled_sides_paint(string name)
    {
        var lines = File.ReadAllLines(Path.Combine(HtmlCases.Folder(), "..", "measured", name + ".print.txt"));
        var chromium = PrintedPages.Parse(string.Join("\n", lines));
        var filled = PrintedPages.Parse(string.Join("\n", lines.Select(line => line.StartsWith("stroke ", StringComparison.Ordinal) ? "rect " + line[7..] : line)));
        var without = PrintedPages.Parse(string.Join("\n", lines.Where(line => !line.StartsWith("stroke ", StringComparison.Ordinal))));

        lines.Count(line => line.StartsWith("stroke ", StringComparison.Ordinal)).Should().Be(4);
        HtmlCases.Difference(filled, chromium).Should().BeNull();
        HtmlCases.Difference(without, chromium).Should().Contain("Chromium paints it and the engine does not");
    }

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
