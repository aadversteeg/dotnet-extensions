using System.IO;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Html;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.ErrorPaths;
using Xunit.Abstractions;

namespace UnitTests.Extensions.Documents.Html.Boxes;

/// <summary>
/// Every Chromium reference case through the HTML path, against its committed status file
/// (<c>tools/reference/chromium/measured/&lt;case&gt;.status.txt</c>): a first line <c>name status rest</c>, where the status, the
/// second field, is <c>equal</c> (every fact of the case's measured files within its bound, <see cref="HtmlCases"/>),
/// <c>listed</c> with the test that pins the limit and the difference it lists, <c>refused</c> with the first refusal's
/// code, what it names, and its line and column, or <c>open</c> with a known difference, engine against Chromium; a
/// <c>listed</c> or <c>open</c> file then holds every difference of the case (<see cref="HtmlCases.DifferenceList"/>). A case
/// that lays out and differs otherwise fails, and so does a status that changes without its file changing, a case without
/// a status file and a status file without a case: the status files are the list of what the HTML front-end draws, and
/// what it refuses is the work list of the packages after this one. The test prints how many cases have each status. With
/// <c>DOCUMENTS_UPDATE_HTML_STATUS=1</c> the test writes the status files instead (and deletes those without a case); a
/// case is <c>open</c> or <c>listed</c> only where its committed file already says so, so a written file never accepts a
/// difference of another case; of a listed or open case it writes the differences found, for the diff to show. Each case's status is a file of its own, so two changes that add different cases touch no status
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
        var open = statuses.Where(status => HtmlCases.Fields(status).Status == "open").Select(status => status.Split('\n')[0]).ToList();
        output.WriteLine($"{open.Count} open: {string.Join("; ", open)}");
        statuses.Where(status => !HtmlCases.Accepted.Contains(HtmlCases.Fields(status).Status))
            .Should().BeEmpty("a file that lays out is laid out as Chromium prints it");
        folder.StatusNames.Should().Equal(folder.Names, "every file has a status file, and every status file is of a file");
        var committed = folder.Names.Select(name => File.Exists(folder.StatusFile(name)) ? CaseFolder.StatusText(File.ReadAllText(folder.StatusFile(name))) : $"{name} has no status file").ToList();
        var changed = committed.Zip(statuses).Where(pair => pair.First != pair.Second).Select(pair => $"{pair.Second}\n(committed: {pair.First.Split('\n')[0]})").ToList();
        changed.Should().BeEmpty($"the status of each file is the committed one, with every difference of a listed or open file:\n{string.Join("\n\n", changed)}\n");
    }

    /// <summary>The file that names every open case and document (<c>tools/reference/chromium/open-cases.txt</c>): one line
    /// each, the name and why it is open. A case is open only where the file names it, and every name in the file is of an
    /// open case or document, so a difference cannot be parked as open by its status file alone.</summary>
    [Fact]
    public void Every_open_case_is_named_with_its_reason_in_the_list_of_open_cases_and_no_other_is()
    {
        var named = OpenCases();
        var open = new[] { HtmlCases.Cases, HtmlCases.Documents }
            .SelectMany(folder => folder.Names.Where(name => HtmlCases.Fields($"{name} {folder.Committed(name)}").Status == "open")).Order(StringComparer.Ordinal).ToList();

        named.Should().OnlyContain(line => line.Reason.Length > 0, "each open case says why it is open");
        named.Select(line => line.Name).Should().OnlyHaveUniqueItems().And.Equal(open, "the list of open cases names exactly the cases whose status is open");
    }

    /// <summary>The lines of <c>open-cases.txt</c> (<see cref="Every_open_case_is_named_with_its_reason_in_the_list_of_open_cases_and_no_other_is"/>),
    /// in the file's order, which is ordinal; lines that start with <c>#</c> are comments.</summary>
    private static List<(string Name, string Reason)> OpenCases()
    {
        var lines = CaseFolder.StatusText(File.ReadAllText(Path.Combine(HtmlCases.Folder(), "..", "open-cases.txt"))).Split('\n')
            .Where(line => line.Length > 0 && !line.StartsWith('#')).Select(line => line.Split(' ', 2)).Select(fields => (fields[0], fields.Length > 1 ? fields[1].Trim() : string.Empty)).ToList();
        lines.Select(line => line.Item1).Should().BeInAscendingOrder(StringComparer.Ordinal);
        return lines;
    }

    /// <summary>A status file read on a checkout that wrote carriage returns is the same status: the comparison sees line
    /// feeds only, and no line feed after the last line.</summary>
    [Fact]
    public void A_status_file_with_carriage_returns_reads_as_the_same_status()
    {
        var folder = Directory.CreateTempSubdirectory("documents-status-");
        try
        {
            var lines = new CaseFolder(folder.FullName, folder.FullName);
            File.WriteAllText(lines.StatusFile("a"), "a listed T: x\r\n2 differences:\r\n- x\r\n- y\r\n");
            File.WriteAllText(lines.StatusFile("b"), "b equal\n");

            lines.Committed("a").Should().Be("listed T: x\n2 differences:\n- x\n- y");
            lines.Committed("b").Should().Be("equal");
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    /// <summary>
    /// A listed case names, in its status line, the test that pins its difference, and the link is data, not text: one
    /// test method of this project by that name, a theory with a row (<see cref="InlineDataAttribute"/>) whose first value is
    /// the case's name, read by reflection, and whose first parameter is <c>string name</c>; and that test lays out the case
    /// it is given: its body (<see cref="TestSource"/> from the declaration on, comments left out) uses <c>name</c> and holds
    /// no case's name as a string, so it cannot be given one case and look at another. A name changed in the line alone, a
    /// test of another case, or a case named only in a comment fails here.
    /// </summary>
    [Fact]
    public void Every_listed_case_names_a_test_that_lays_out_that_case()
    {
        var project = Path.GetFullPath(Path.Combine(HtmlCases.Folder(), "..", "..", "..", "..", "UnitTests.Extensions.Documents.Html"));
        var obj = $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}";
        var sources = Directory.GetFiles(project, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains(obj, StringComparison.Ordinal)).Select(file => CaseFolder.StatusText(File.ReadAllText(file))).ToList();
        var listed = HtmlCases.Names.Select(name => (Name: name, Fields: HtmlCases.Cases.Committed(name)?.Split('\n')[0].Split(' ', 2) ?? []))
            .Where(pair => pair.Fields is ["listed", _]).Select(pair => (pair.Name, Test: pair.Fields[1].Split(": ", 2)[0])).ToList();
        var methods = typeof(CaseStatusTests).Assembly.GetTypes().SelectMany(type => type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly)).ToList();

        var wrong = listed.Select(pair => (pair.Name, pair.Test, Why: Unlinked(pair.Name, pair.Test, methods, sources))).Where(found => found.Why is not null)
            .Select(found => $"{found.Name}: {found.Test} {found.Why}").ToList();
        listed.Should().NotBeEmpty();
        wrong.Should().BeEmpty($"a listed case names one test of this project, and that test is given the case by its name in a row and lays it out ({string.Join("; ", wrong)})");
    }

    /// <summary>Why a listed case and the test its status line names are not linked
    /// (<see cref="Every_listed_case_names_a_test_that_lays_out_that_case"/>), or null where they are.</summary>
    internal static string? Unlinked(string name, string test, IReadOnlyList<System.Reflection.MethodInfo> methods, IReadOnlyList<string> sources)
    {
        if (methods.Where(method => method.Name == test).ToList() is not [var method])
        {
            return "is not one test method of this project";
        }

        if (method.GetParameters() is not [{ Name: "name" } first, ..] || first.ParameterType != typeof(string))
        {
            return "has no first parameter string name";
        }

        var rows = method.GetCustomAttributes(typeof(InlineDataAttribute), inherit: false).OfType<InlineDataAttribute>().SelectMany(row => row.GetData(method)).ToList();
        if (!rows.Exists(row => row is [string given, ..] && given == name))
        {
            return "has no row whose first value is the case";
        }

        if (sources.Select(source => TestSource(source, test)).OfType<string>().ToList() is not [var text])
        {
            return "is not one method in the sources";
        }

        var lines = text.Split('\n');
        var body = string.Join("\n", lines.SkipWhile(line => !line.Contains($" void {test}(", StringComparison.Ordinal)).Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));
        if (System.Text.RegularExpressions.Regex.Matches(body, @"\bname\b").Count < 2)
        {
            return "does not use the name it is given";
        }

        return HtmlCases.Names.FirstOrDefault(other => body.Contains($"\"{other}\"", StringComparison.Ordinal)) is { } named ? $"names the case \"{named}\" in its body" : null;
    }

    [Theory]
    [InlineData("Linked", null)]
    [InlineData("Missing", "is not one test method of this project")]
    [InlineData("NoName", "has no first parameter string name")]
    [InlineData("OtherRow", "has no row whose first value is the case")]
    [InlineData("Unused", "does not use the name it is given")]
    [InlineData("Other", "names the case \"ta-items\" in its body")]
    public void A_listed_case_is_linked_to_its_test_by_a_row_that_names_it_and_a_body_that_uses_it(string test, string? why)
    {
        var methods = typeof(LinkedTests).GetMethods().Where(method => method.DeclaringType == typeof(LinkedTests)).ToList();
        string[] sources = [CaseFolder.StatusText(LinkedSource)];

        Unlinked("ta-kerning-scale", test, methods, sources).Should().Be(why);
    }

    private const string LinkedSource = """
            [InlineData("ta-kerning-scale")]
            public void Linked(string name) => Run(name);
            [InlineData("ta-kerning-scale")]
            public void NoName(string other) => Run(other);
            [InlineData("ta-items")]
            public void OtherRow(string name) => Run(name);
            [InlineData("ta-kerning-scale")]
            public void Unused(string name)
            {
                // see name
                Run("x");
            }
            [InlineData("ta-kerning-scale")]
            public void Other(string name)
            {
                Run(name, "ta-items");
            }
        """;

    /// <summary>The methods <see cref="A_listed_case_is_linked_to_its_test_by_a_row_that_names_it_and_a_body_that_uses_it"/>
    /// checks, by the rows <see cref="LinkedSource"/> shows for them.</summary>
#pragma warning disable xUnit1008 // Rows on methods that are no tests: the check reads them by reflection, nothing runs them.
    private static class LinkedTests
    {
        [InlineData("ta-kerning-scale")]
        public static void Linked(string name) => _ = name;

        [InlineData("ta-kerning-scale")]
        public static void NoName(string other) => _ = other;

        [InlineData("ta-items")]
        public static void OtherRow(string name) => _ = name;

        [InlineData("ta-kerning-scale")]
        public static void Unused(string name) => _ = name;

        [InlineData("ta-kerning-scale")]
        public static void Other(string name) => _ = name;
    }
#pragma warning restore xUnit1008

    /// <summary>The text of a test method in a source file: from the attributes and comments right above its declaration
    /// (<c> void name(</c>) to the end of its body, the first line after it that closes a member (four spaces and a brace), or
    /// the first line that ends in a semicolon for an expression body; null where the file has no such method.</summary>
    internal static string? TestSource(string source, string test)
    {
        var lines = source.Split('\n');
        var declaration = Array.FindIndex(lines, line => line.Contains($" void {test}(", StringComparison.Ordinal));
        if (declaration < 0)
        {
            return null;
        }

        var start = declaration;
        while (start > 0 && lines[start - 1].TrimStart() is var above && (above.StartsWith('[') || above.StartsWith("///", StringComparison.Ordinal)))
        {
            start--;
        }

        var expression = lines[declaration].Contains("=>", StringComparison.Ordinal);
        var end = declaration;
        while (end < lines.Length - 1 && !(expression ? lines[end].TrimEnd().EndsWith(';') : lines[end] == "    }"))
        {
            end++;
        }

        return string.Join("\n", lines[start..(end + 1)]);
    }

    [Theory]
    [InlineData("    [InlineData(\"c\")]\n    public void T(string name)\n    {\n        x;\n    }\n    [InlineData(\"d\")]\n    public void U(string name)\n    {\n    }", "T", "\"c\"", "\"d\"")]
    [InlineData("    /// <summary>\"c\"</summary>\n    [Fact]\n    public void T() =>\n        Run(\"d\");\n    [Fact]\n    public void U() => Run(\"e\");", "T", "\"d\"", "\"e\"")]
    public void The_text_of_a_test_ends_with_its_body_and_starts_at_its_attributes(string source, string test, string inside, string outside)
    {
        TestSource(source, test).Should().Contain(inside).And.NotContain(outside);
        TestSource(source, "V").Should().BeNull();
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
