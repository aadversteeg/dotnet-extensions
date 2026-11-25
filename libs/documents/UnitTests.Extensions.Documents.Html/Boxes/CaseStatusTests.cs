using System.IO;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Html;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.ErrorPaths;
using Xunit.Abstractions;

namespace UnitTests.Extensions.Documents.Html.Boxes;

/// <summary>
/// Every Chromium reference case through the HTML path, against its committed status file
/// (<c>tools/reference/chromium/measured/&lt;case&gt;.status.txt</c>): one line, <c>name status rest</c>, where the status, the
/// second field, is <c>equal</c> (every fact of the case's measured files within its bound, <see cref="HtmlCases"/>),
/// <c>listed</c> with the test that pins the limit and the difference it lists, <c>refused</c> with the first refusal's
/// code, what it names, and its line and column, or <c>open</c> with a known difference, engine against Chromium. A case
/// that lays out and differs otherwise fails, and so does a status that changes without its file changing, a case without
/// a status file and a status file without a case: the status files are the list of what the HTML front-end draws, and
/// what it refuses is the work list of the packages after this one. The test prints how many cases have each status. With
/// <c>DOCUMENTS_UPDATE_HTML_STATUS=1</c> the test writes the status files instead (and deletes those without a case); a
/// case is <c>open</c> or <c>listed</c> only where its committed file already says so, so a written file never accepts a
/// new difference. Each case's status is a file of its own, so two changes that add different cases touch no status
/// file in common. The whole documents (<c>tools/reference/chromium/documents</c>) have status files of their own, in
/// <c>documents/measured</c>, compared and written the same way.
/// </summary>
public class CaseStatusTests(ITestOutputHelper output)
{
    [Fact]
    public void Every_case_is_equal_to_Chromium_or_refused_as_the_status_file_says() => StatusesAreCommitted(HtmlCases.Cases);

    /// <summary>The timesheets (one page, a normal month, a long month) and the invoice, each compared whole, with the
    /// same comparison as a case, against <c>documents/measured/&lt;document&gt;.status.txt</c>.</summary>
    [Fact]
    public void Every_document_is_equal_to_Chromium_or_refused_as_its_status_file_says()
    {
        HtmlCases.Documents.Names.Should().Equal("invoice", "timesheet-normal", "timesheet-one-page", "timesheet-pages");
        StatusesAreCommitted(HtmlCases.Documents);
    }

    private void StatusesAreCommitted(CaseFolder folder)
    {
        var statuses = folder.Names.Select(name => $"{name} {HtmlCases.Status(folder, name, folder.Committed(name))}").ToList();
        if (Environment.GetEnvironmentVariable("DOCUMENTS_UPDATE_HTML_STATUS") == "1")
        {
            foreach (var status in statuses)
            {
                File.WriteAllText(folder.StatusFile(HtmlCases.Fields(status).Name), status + "\n");
            }

            foreach (var name in folder.StatusNames.Except(folder.Names))
            {
                File.Delete(folder.StatusFile(name));
            }
        }

        var counts = statuses.GroupBy(status => HtmlCases.Fields(status).Status).OrderByDescending(group => group.Count()).ThenBy(group => group.Key, StringComparer.Ordinal);
        output.WriteLine($"{statuses.Count} files: {string.Join(", ", counts.Select(group => $"{group.Count()} {group.Key}"))}");
        var open = statuses.Where(status => HtmlCases.Fields(status).Status == "open").ToList();
        output.WriteLine($"{open.Count} open: {string.Join("; ", open)}");
        statuses.Where(status => !HtmlCases.Accepted.Contains(HtmlCases.Fields(status).Status))
            .Should().BeEmpty("a file that lays out is laid out as Chromium prints it");
        folder.StatusNames.Should().Equal(folder.Names, "every file has a status file, and every status file is of a file");
        folder.Names.Select(name => File.Exists(folder.StatusFile(name)) ? string.Join("\n", File.ReadAllLines(folder.StatusFile(name))) : $"{name} has no status file")
            .Should().Equal(statuses, "the status of each file is the committed one");
    }

    /// <summary>A listed case names, in its status line, the test that pins its difference: that test is one of the
    /// family's tests, so a name changed in the line alone fails here.</summary>
    [Fact]
    public void Every_listed_case_names_a_test_of_the_family()
    {
        var family = Path.GetFullPath(Path.Combine(HtmlCases.Folder(), "..", "..", "..", ".."));
        var obj = $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}";
        var sources = Directory.GetDirectories(family, "UnitTests.*").SelectMany(project => Directory.GetFiles(project, "*.cs", SearchOption.AllDirectories))
            .Where(file => !file.Contains(obj, StringComparison.Ordinal)).Select(File.ReadAllText).ToList();
        var tests = HtmlCases.Names.Select(name => HtmlCases.Cases.Committed(name)?.Split(' ', 2) ?? [])
            .Where(fields => fields is ["listed", _]).Select(fields => fields[1].Split(": ", 2)[0]).Distinct(StringComparer.Ordinal).ToList();

        tests.Should().NotBeEmpty();
        tests.Where(test => !sources.Any(source => source.Contains($" void {test}(", StringComparison.Ordinal))).Should().BeEmpty("a listed case names a test that pins its difference");
    }

    [Fact]
    public void At_least_ten_cases_are_equal_to_Chromium() =>
        HtmlCases.Names.Count(name => HtmlCases.Cases.Committed(name) == "equal").Should().BeGreaterThanOrEqualTo(10);

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

    /// <summary>An inline box that layout does not draw where it stands is a refusal at the element that made it, as the
    /// HTML front-end's refusals are; another code under the same parent, or the same code without its place, still fails
    /// the case.</summary>
    [Fact]
    public void An_inline_box_refused_by_layout_reads_refused_and_another_code_still_fails()
    {
        var at = new SourceLocation("case.html", 3) { Column = 14 };
        var refused = new Error(DocumentErrorCodes.Input.Unsupported.InlineBox, "The inline box made at case.html:3 is not drawn in this version.")
            .With(ErrorMetadataKeys.Property, "box").With(ErrorMetadataKeys.Value, "box").WithSource(at);
        var sibling = new Error(DocumentErrorCodes.Input.Unsupported.AtomicInline, "A box among the runs is not set in a line.").WithSource(at);
        var placeless = new Error(DocumentErrorCodes.Input.Unsupported.InlineBox, "The inline box is not drawn in this version.");

        HtmlCases.Refusal(refused).Should().Be("refused Documents.Input.Unsupported.InlineBox box: box at 3:14");
        HtmlCases.Refusal(sibling).Should().StartWith("failed Documents.Input.Unsupported.AtomicInline ");
        HtmlCases.Refusal(placeless).Should().StartWith("failed Documents.Input.Unsupported.InlineBox ").And.EndWith("a refusal without its line and column");
    }

    /// <summary>The HTML front-end's own refusal of an inline box (an image inside both a link and a box) is a refusal at the
    /// element, as the layout refusal of an inline box is; the same code without its place still fails the case.</summary>
    [Fact]
    public void The_front_end_s_refusal_of_an_inline_box_reads_refused_and_without_its_place_fails()
    {
        var at = new SourceLocation("case.html", 2) { Column = 30 };
        var refused = new Error(HtmlStyleErrorCodes.Unsupported.InlineBox, "An <img> inside a link and inside an element with a background is not drawn in this version.")
            .With(ErrorMetadataKeys.Property, "img").With(ErrorMetadataKeys.Value, "img").WithSource(at);
        var placeless = new Error(HtmlStyleErrorCodes.Unsupported.InlineBox, "An <img> inside a link and a box is not drawn in this version.");

        HtmlCases.Refusal(refused).Should().Be("refused Documents.Html.Unsupported.InlineBox img: img at 2:30");
        HtmlCases.Refusal(placeless).Should().StartWith("failed Documents.Html.Unsupported.InlineBox ").And.EndWith("a refusal without its line and column");
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
