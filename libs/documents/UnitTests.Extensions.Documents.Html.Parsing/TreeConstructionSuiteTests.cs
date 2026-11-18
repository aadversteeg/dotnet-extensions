using System.IO;
using System.Text;
using System.Text.Json;
using Ave.Extensions.Documents.Html.Parsing;
using Ave.Extensions.Functional;
using Xunit.Abstractions;

namespace UnitTests.Extensions.Documents.Html.Parsing;

/// <summary>
/// The tree-construction tests (<c>.dat</c> files), maintained in web-platform-tests since June 2026 and vendored at
/// commit 93494ff (BSD-3): the tree the parser builds, in the suite's text form (<see cref="HtmlTreeText"/>), must equal
/// the expected <c>#document</c>, for whole documents and for fragments (<c>#document-fragment</c>). The <c>#errors</c>
/// lists are html5lib's own and are not compared. One rule skips tests: those marked <c>#script-on</c> need the
/// scripting flag on; this parser has it off.
/// </summary>
/// <remarks>The reference is Chromium 159, also against the suite: where Chromium's tree differs
/// from the suite's (measured into <c>tools/reference/chromium/html-tree/measured</c>), the parser must build
/// Chromium's tree, except in the listed limits below, where it builds the suite's (the standard's). Each case is one of
/// three outcomes, and their numbers are pinned.</remarks>
public class TreeConstructionSuiteTests(ITestOutputHelper output)
{
    private sealed record TreeTest(string File, int Line, string Data, string? Fragment, bool ScriptOn, string Document);

    public static TheoryData<string> Files => [.. FileNames()];

    /// <summary>The cases where Chromium's innerHTML differs from the standard and the parser keeps the standard: fragment
    /// parsing (<c>HtmlParser.ParseFragment</c>, which the style front-end does not use; HD-8). A NUL in a
    /// textarea or title fragment, which Chromium drops and the standard makes U+FFFD; a form start tag in a template
    /// fragment, and the end of a form in one; an input in a select fragment, which customizable select keeps.</summary>
    private static readonly HashSet<string> StandardWhereChromiumDiffers =
    [
        "plain-text-unsafe.dat:712", "plain-text-unsafe.dat:723", "template.dat:1764", "template.dat:1817", "tests_innerHTML_1.dat:790",
    ];

    private static readonly Lazy<Dictionary<string, string>> Chromium = new(() =>
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Suites.ChromiumTrees()));
        return json.RootElement.GetProperty("differ").EnumerateArray()
            .ToDictionary(item => item.GetProperty("case").GetString()!, item => item.GetProperty("tree").GetString()!);
    });

    /// <summary>The tree a test must build: Chromium's where it differs from the suite and that is not a listed limit.</summary>
    private static string Expected(TreeTest test) =>
        Chromium.Value.TryGetValue($"{test.File}:{test.Line}", out var chromium) && !StandardWhereChromiumDiffers.Contains($"{test.File}:{test.Line}")
            ? chromium
            : test.Document;

    private static IEnumerable<string> FileNames() =>
        Directory.GetFiles(Suites.TreeConstruction, "*.dat").Select(path => Path.GetFileName(path)).Order(StringComparer.Ordinal);

    [Theory]
    [MemberData(nameof(Files))]
    public void Every_test_of_the_file_builds_the_expected_tree(string file)
    {
        var tests = Read(file);
        var failures = new List<string>();
        foreach (var test in tests.Where(test => !test.ScriptOn))
        {
            var actual = Run(test);
            var expected = Expected(test);
            if (actual != expected)
            {
                failures.Add($"{file}:{test.Line}{(test.Fragment is null ? string.Empty : $" (fragment {test.Fragment})")}\n#data\n{test.Data}\n#expected\n{expected}#actual\n{actual}");
            }
        }

        var chromium = tests.Count(test => !test.ScriptOn && Chromium.Value.ContainsKey($"{file}:{test.Line}") && !StandardWhereChromiumDiffers.Contains($"{file}:{test.Line}"));
        output.WriteLine($"{file}: {tests.Count} tests, {tests.Count(test => test.ScriptOn)} skipped (script on), {chromium} built as Chromium where the suite differs, {failures.Count} failed");
        foreach (var failure in failures.Take(30))
        {
            output.WriteLine(failure);
        }

        failures.Should().BeEmpty();
    }

    [Fact]
    public void The_suite_has_all_its_tests()
    {
        var tests = FileNames().SelectMany(Read).ToList();

        // 1 973 tests in the 62 files at 93494ff (lines "#data"); 14 of them need scripting on.
        tests.Should().HaveCount(1973);
        tests.Count(test => test.ScriptOn).Should().Be(14);
    }

    [Fact]
    public void Chromium_differs_from_the_suite_in_seventeen_cases_and_the_parser_follows_it_in_twelve()
    {
        var tests = FileNames().SelectMany(Read).Where(test => !test.ScriptOn).ToList();
        var differ = tests.Where(test => Chromium.Value.ContainsKey($"{test.File}:{test.Line}")).ToList();

        // Measured in Chromium 159 (1 959 cases): NUL handling (HD-1 .. HD-3) and search (HD-4) are built as Chromium
        // builds them; five fragment cases keep the standard (the listed limit above).
        tests.Should().HaveCount(1959);
        differ.Should().HaveCount(17);
        differ.Count(test => StandardWhereChromiumDiffers.Contains($"{test.File}:{test.Line}")).Should().Be(5);
        differ.Where(test => !StandardWhereChromiumDiffers.Contains($"{test.File}:{test.Line}")).Select(test => test.File).Distinct()
            .Should().BeEquivalentTo("plain-text-unsafe.dat", "search-element.dat", "tests19.dat");
    }

    private static string Run(TreeTest test)
    {
        if (test.Fragment is null)
        {
            return HtmlParser.Parse(test.Data).Match(result => HtmlTreeText.Format(result.Document), error => $"error: {error}\n");
        }

        var parts = test.Fragment.Split(' ');
        var context = parts.Length == 2
            ? new HtmlFragmentContext(parts[0] == "svg" ? ElementNamespace.Svg : ElementNamespace.MathMl, parts[1])
            : new HtmlFragmentContext(ElementNamespace.Html, parts[0]);
        return HtmlParser.ParseFragment(test.Data, context).Match(result => HtmlTreeText.Format(result.Fragment), error => $"error: {error}\n");
    }

    /// <summary>Reads the tests of one file. A test starts with a line <c>#description</c> or <c>#data</c> (at the start
    /// of the file or after an empty line); its sections run to the next section heading; the document ends at the empty line before the next
    /// test.</summary>
    private static List<TreeTest> Read(string file)
    {
        var lines = File.ReadAllText(Path.Combine(Suites.TreeConstruction, file), Encoding.UTF8).Split('\n');
        var tests = new List<TreeTest>();
        var index = 0;
        while (index < lines.Length)
        {
            if (!StartsTest(lines, index))
            {
                index++;
                continue;
            }

            var start = index + 1;
            var sections = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var current = lines[index];
            sections[current] = [];
            index++;
            while (index < lines.Length && !StartsTest(lines, index))
            {
                var line = lines[index];
                if (current != "#document" && line is "#data" or "#errors" or "#new-errors" or "#document-fragment" or "#script-on" or "#script-off" or "#document")
                {
                    current = line;
                    sections[current] = [];
                }
                else
                {
                    sections[current].Add(line);
                }

                index++;
            }

            var document = sections.GetValueOrDefault("#document") ?? [];
            while (document.Count > 0 && document[^1].Length == 0)
            {
                document.RemoveAt(document.Count - 1);
            }

            tests.Add(new TreeTest(
                file,
                start,
                string.Join('\n', sections["#data"]),
                sections.TryGetValue("#document-fragment", out var fragment) ? fragment[0] : null,
                sections.ContainsKey("#script-on"),
                document.Count == 0 ? string.Empty : string.Join('\n', document) + "\n"));
        }

        return tests;
    }

    /// <summary>A test starts with <c>#description</c> or <c>#data</c>, at the start of the file or after an empty
    /// line; a <c>#data</c> right after a description belongs to the same test.</summary>
    private static bool StartsTest(string[] lines, int index) =>
        lines[index] is "#data" or "#description" && (index == 0 || lines[index - 1].Length == 0);
}
