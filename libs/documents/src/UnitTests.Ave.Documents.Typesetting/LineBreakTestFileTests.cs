using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Text;
using Ave.Documents.Typesetting.LineBreaking;
using Xunit.Abstractions;

namespace UnitTests.Ave.Documents.Typesetting;

/// <summary>
/// The gate of rule B1: the official Unicode conformance test for line breaking, LineBreakTest.txt of the same
/// Unicode version as the property table (assets/unicode/18.0.0). Every case must pass; nothing is skipped.
/// </summary>
/// <remarks>
/// Format (header of the file): each line is a sequence of code points in hexadecimal separated by "×" (no
/// break allowed) or "÷" (break allowed), with a leading "×" for the start of text and a trailing "÷" for the
/// end. The file does not distinguish mandatory from allowed breaks, so both count as "÷".
/// </remarks>
public class LineBreakTestFileTests(ITestOutputHelper output)
{
    private sealed record TestCase(int LineNumber, string Text, ImmutableArray<int> Breaks, string Source);

    private static IEnumerable<TestCase> Cases()
    {
        var lines = File.ReadAllLines(Path.Combine(UnicodeDataGenerator.DataFolder(), "LineBreakTest.txt"));
        for (var index = 0; index < lines.Length; index++)
        {
            var data = lines[index].Split('#')[0].Trim();
            if (data.Length == 0)
            {
                continue;
            }

            var text = new StringBuilder();
            var breaks = ImmutableArray.CreateBuilder<int>();
            foreach (var token in data.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                switch (token)
                {
                    case "÷" when text.Length > 0:
                        breaks.Add(text.Length);
                        break;
                    case "÷" or "×":
                        break;
                    default:
                        text.Append(char.ConvertFromUtf32(int.Parse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture)));
                        break;
                }
            }

            yield return new TestCase(index + 1, text.ToString(), breaks.ToImmutable(), data);
        }
    }

    [Fact]
    public void Every_case_of_the_official_line_break_test_file_passes()
    {
        var cases = Cases().ToList();
        var failures = cases
            .Select(test => (Test: test, Actual: UnicodeLineBreaking.Find(test.Text).Select(found => found.Offset).ToImmutableArray()))
            .Where(result => !result.Actual.SequenceEqual(result.Test.Breaks))
            .ToList();

        output.WriteLine($"LineBreakTest-18.0.0.txt: {cases.Count} cases, {cases.Count - failures.Count} passed, {failures.Count} failed, 0 skipped.");
        foreach (var (test, actual) in failures.Take(20))
        {
            output.WriteLine($"line {test.LineNumber}: {test.Source}  expected [{string.Join(",", test.Breaks)}] got [{string.Join(",", actual)}]");
        }

        cases.Count.Should().BeGreaterThan(10_000, "the whole file was read");
        failures.Should().BeEmpty();
    }
}
