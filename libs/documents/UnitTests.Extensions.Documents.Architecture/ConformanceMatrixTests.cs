using System.IO;
using System.Text.RegularExpressions;

namespace UnitTests.Extensions.Documents.Architecture;

/// <summary>
/// docs/conformance-matrix.md promises, rule by rule, how the engine meets veraPDF's profiles; these tests check that
/// what it names is real. Every error code it names exists in an engine library; every test class it names exists in
/// the test project it names; a "checked" rule names at least one code and one test class, and every test class it
/// names refers to one of the rule's codes in its source. That is what is checked: the named class tests the code, not
/// necessarily this rule. Several rules share one code and one class (ten rows name only Pdf.Write.InvalidPageModel and
/// Render.Pdf PageModelContractTests), so one mention satisfies all of them; whether a test fails when a particular
/// refusal is removed is shown by the mutant runs of the fix rounds, not here. The matrix builder
/// (tools/conformance/build-matrix.py) lists every rule id on its own.
/// </summary>
public class ConformanceMatrixTests
{
    /// <summary>One row of the matrix.</summary>
    private sealed record Row(string Rule, string Class, string How);

    /// <summary>The roots of the error-code tree as the matrix writes them (without the leading "Documents.").</summary>
    private static readonly Regex Code = new(@"`((?:Pdf|Input|Font|Image|Layout|Typesetting)(?:\.[A-Z][A-Za-z0-9]*)+)`", RegexOptions.CultureInvariant);

    /// <summary>A test class named with its project: "Render.Pdf PageModelContractTests".</summary>
    private static readonly Regex TestClass = new(
        @"\b(Core|DocumentModel|Fluent|Fonts|Images|Layout|PageModel|Render\.Pdf|Pdf|Samples|Typesetting) ([A-Z][A-Za-z]*Tests)\b", RegexOptions.CultureInvariant);

    private static readonly Regex RuleId = new(@"^\d[\d.]*-\d+\z", RegexOptions.CultureInvariant);

    private static List<Row> Rows() =>
    [
        .. File.ReadAllLines(Path.Combine(ProjectRulesTests.FamilyRoot(), "docs", "conformance-matrix.md"))
            .Where(line => line.StartsWith("| ", StringComparison.Ordinal))
            .Select(line => line.Split('|').Select(cell => cell.Trim()).ToArray())
            .Where(cells => cells.Length >= 6 && RuleId.IsMatch(cells[1]))
            .Select(cells => new Row(cells[1], cells[4], cells[5])),
    ];

    private static IEnumerable<string> CodesOf(Row row) => Code.Matches(row.How).Select(match => match.Groups[1].Value).Distinct();

    private static IEnumerable<(string Project, string Name)> TestClassesOf(Row row) =>
        TestClass.Matches(row.How).Select(match => (match.Groups[1].Value, match.Groups[2].Value)).Distinct();

    /// <summary>The source of a test class: the text of the files of its test project that declare it, or null.</summary>
    private static string? SourceOf(string project, string name)
    {
        var folder = Path.Combine(ProjectRulesTests.FamilyRoot(), "UnitTests.Extensions.Documents." + project);
        var files = Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(File.ReadAllText)
            .Where(text => Regex.IsMatch(text, $@"\bclass {name}\b"))
            .ToList();
        return files.Count == 0 ? null : string.Concat(files);
    }

    /// <summary>How a test refers to a code: its last two segments, as in PdfA3.MediaType or Write.InvalidPageModel.</summary>
    private static string Reference(string code) => string.Join(".", code.Split('.').TakeLast(2));

    [Fact]
    public void The_matrix_has_a_row_for_each_of_its_rules()
    {
        var rows = Rows();

        rows.Should().HaveCountGreaterThan(250);
        rows.Select(row => row.Rule).Should().OnlyHaveUniqueItems();
        rows.Select(row => row.Class).Distinct().Should().BeSubsetOf(["by construction", "checked", "cannot occur"]);
    }

    [Fact]
    public void Every_error_code_the_matrix_names_exists()
    {
        var codes = ErrorCodeTests.AllCodes().Select(code => code.Path).ToHashSet();

        var unknown = Rows().SelectMany(row => CodesOf(row).Where(code => !codes.Contains("Documents." + code)).Select(code => $"{row.Rule}: {code}"));

        unknown.Should().BeEmpty();
    }

    [Fact]
    public void Every_test_class_the_matrix_names_exists()
    {
        var missing = Rows().SelectMany(row => TestClassesOf(row).Where(test => SourceOf(test.Project, test.Name) is null).Select(test => $"{row.Rule}: {test.Project} {test.Name}"));

        missing.Should().BeEmpty();
    }

    [Fact]
    public void Every_checked_rule_names_a_code_and_test_classes_that_refer_to_one_of_its_codes()
    {
        var problems = new List<string>();
        foreach (var row in Rows().Where(row => row.Class == "checked"))
        {
            var codes = CodesOf(row).ToList();
            var tests = TestClassesOf(row).ToList();
            if (codes.Count == 0 || tests.Count == 0)
            {
                problems.Add($"{row.Rule}: names {codes.Count} codes and {tests.Count} test classes");
                continue;
            }

            problems.AddRange(tests
                .Where(test => SourceOf(test.Project, test.Name) is { } source && !codes.Any(code => source.Contains(Reference(code), StringComparison.Ordinal)))
                .Select(test => $"{row.Rule}: {test.Project} {test.Name} tests none of {string.Join(", ", codes)}"));
        }

        problems.Should().BeEmpty(string.Join(" | ", problems));
    }
}
