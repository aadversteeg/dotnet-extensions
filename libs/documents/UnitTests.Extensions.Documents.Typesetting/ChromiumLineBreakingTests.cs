using System.IO;
using Ave.Extensions.Documents.Typesetting.LineBreaking;
using Xunit.Abstractions;

namespace UnitTests.Extensions.Documents.Typesetting;

/// <summary>
/// The line-break rules of Chromium's print (<see cref="ChromiumLineBreaking"/>) against the measured matrix
/// (<c>Reference/Chromium/line-breaks.txt</c>): every row breaks where Chromium's print breaks it. The standard itself
/// stays pinned by <see cref="LineBreakTestFileTests"/>.
/// </summary>
public class ChromiumLineBreakingTests(ITestOutputHelper output)
{
    [Fact]
    public void Every_row_of_the_measured_matrix_breaks_as_chromium_prints_it()
    {
        var rows = ChromiumLineBreakMatrix.Rows();
        var failures = rows
            .Select(row => (Row: row, Actual: ChromiumLineBreakMatrix.BoxBreaks(row.Text)))
            .Where(result => !result.Actual.SequenceEqual(result.Row.Breaks))
            .ToList();
        var standardDiffers = rows.Count(row => !ChromiumLineBreakMatrix.VisibleBreaks(row.Text, UnicodeLineBreaking.Find(row.Text)).SequenceEqual(row.Breaks));
        var onePassDiffers = rows.Count(row => !ChromiumLineBreakMatrix.VisibleBreaks(row.Text, ChromiumLineBreaking.Find(row.Text)).SequenceEqual(row.Breaks));

        output.WriteLine($"line-breaks.txt: {rows.Count} rows, {rows.Count - failures.Count} reproduced, {failures.Count} not; "
            + $"the standard alone breaks {standardDiffers} of them otherwise; the rules found once for the whole row {onePassDiffers}.");
        foreach (var (row, actual) in failures.Take(20))
        {
            output.WriteLine($"line {row.Line} ({row.Document}): {Hex(row.Text)}  Chromium [{string.Join(",", row.Breaks)}] rules [{string.Join(",", actual)}]");
        }

        rows.Select(row => row.Text).Distinct().Count().Should().Be(rows.Count, "each row is written once");
        rows.Count.Should().Be(34_223, "the whole matrix was read: thirteen documents, each row once");
        standardDiffers.Should().BeGreaterThan(0);
        failures.Should().BeEmpty();
        onePassDiffers.Should().Be(476, "the limit of THE LINE START: rows where a line starts right at an opportunity, found once for the whole text");
    }

    [Fact]
    public void A_line_that_starts_at_a_break_is_decided_anew_by_chromium_and_once_by_the_rules()
    {
        // Row a--éd: in the box 1 pt wide Chromium breaks after the first hyphen only, as the second then starts a line
        // (UAX #14 LB20a at the start of a text); found once for the whole text the rules also break after the second, as
        // Chromium does in running text (case lb-dash-before-sign, a-- / élan).
        const string text = "a--\u00E9d";

        ChromiumLineBreakMatrix.Rows().Single(row => row.Text == text).Breaks.Should().Equal(2);
        ChromiumLineBreakMatrix.BoxBreaks(text).Should().Equal(2);
        ChromiumLineBreakMatrix.VisibleBreaks(text, ChromiumLineBreaking.Find(text)).Should().Equal(2, 3);
    }

    [Fact]
    public void The_committed_ascii_pair_table_matches_the_measured_matrix()
    {
        var expected = ChromiumLineBreakMatrix.GenerateAsciiPairs();
        if (Environment.GetEnvironmentVariable("UPDATE_GENERATED") == "1")
        {
            File.WriteAllText(ChromiumLineBreakMatrix.GeneratedFile(), expected);
        }

        File.ReadAllText(ChromiumLineBreakMatrix.GeneratedFile()).ReplaceLineEndings("\n").Should().Be(
            expected, "the table is generated from line-breaks.txt; run the tests with UPDATE_GENERATED=1 after measuring again");
    }

    /// <summary>The rows each rule's documentation names, where the standard alone breaks otherwise: each is a row of the
    /// matrix with these breaks, and the rules give them.</summary>
    [Theory]
    [InlineData("space rule", "ab )cd", new[] { 2 })]
    [InlineData("space rule", "ab( cd", new[] { 3 })]
    [InlineData("space rule", "ab \u2060cd", new[] { 2 })]
    [InlineData("space rule", "ab \u00B7cd", new[] { 2 })]
    [InlineData("middle dot as a letter", "ab!\u00B7cd", new[] { 3 })]
    [InlineData("middle dot as a letter", "ab\u00B7(cd", new int[0])]
    [InlineData("middle dot as a letter", "a\u00B7$!d", new int[0])]
    [InlineData("middle dot as a letter", "ab/\u0387cd", new[] { 3 })]
    [InlineData("class AI as AL", "ab \u2013\u00A7cd", new[] { 2 })]
    [InlineData("class AI as AL", "ab\u00A0\u00AD\u00A7cd", new[] { 2 })]
    [InlineData("minus-sign rule", "2026-11-28", new[] { 5, 8 })]
    [InlineData("minus-sign rule", "REF-2026-0042", new[] { 4, 9 })]
    [InlineData("minus-sign rule", "12--34", new[] { 3 })]
    [InlineData("ASCII pair table", "and/or", new int[0])]
    [InlineData("ASCII pair table", "INV/2026/0042", new int[0])]
    [InlineData("ASCII pair table", "a|b|c", new int[0])]
    [InlineData("ASCII pair table", "ab -cd", new[] { 2, 3 })]
    [InlineData("ASCII pair table", "https://example.com/a/b/c?d=e&f=g#h", new[] { 26 })]
    [InlineData("Latin-1 sign rule", "12/\u00A334", new[] { 3 })]
    [InlineData("Latin-1 sign rule", "12\u00A3(34", new[] { 3 })]
    [InlineData("Latin-1 sign rule", "12/\u00B034 x\u20AC", new[] { 3, 6 })]
    [InlineData("Latin-1 sign rule", "12$\u00A134", new[] { 3 })]
    [InlineData("Latin-1 sign rule", "12\u00A3\u00BF34", new[] { 3 })]
    [InlineData("Latin-1 sign rule", "12%\u00A134", new[] { 3 })]
    [InlineData("Latin-1 sign rule", "12\u00B0\u00BF34", new[] { 3 })]
    [InlineData("BA before GL", "ab\u2013\u00A0cd", new[] { 3 })]
    [InlineData("BA before GL", "ab|\u00A0cd", new[] { 3 })]
    [InlineData("soft hyphen", "ab\u00A0\u00ADcd", new[] { 2 })]
    [InlineData("dash after a space", "ab \u2013cd", new[] { 2 })]
    [InlineData("dash after a space", "ab\u00A0\u2013cd", new int[0])]
    [InlineData("dash after a space", "\u2013cd", new int[0])]
    public void The_rows_a_rule_names_break_as_chromium_prints_them(string rule, string text, int[] breaks)
    {
        var row = ChromiumLineBreakMatrix.Rows().SingleOrDefault(found => found.Text == text);

        row.Should().NotBeNull($"the {rule} names a measured row");
        row!.Breaks.Should().Equal(breaks);
        ChromiumLineBreakMatrix.VisibleBreaks(text, UnicodeLineBreaking.Find(text)).Should().NotEqual(breaks, $"the {rule} departs from the standard here");
        ChromiumLineBreakMatrix.VisibleBreaks(text, ChromiumLineBreaking.Find(text)).Should().Equal(breaks);
    }

    /// <summary>The rows just past a bound that a rule's documentation names, where the rule does not apply: each is a row
    /// of the matrix with these breaks, and the rules give the standard's breaks there. The clause of the Latin-1 sign rule
    /// before an inverted exclamation or question mark takes the signs of class PR and PO up to U+00FF only: the euro sign
    /// (class PR) and the per mille sign (class PO) keep the standard's LB25 before them.</summary>
    [Theory]
    [InlineData("Latin-1 sign rule", "12\u20AC\u00A134", new int[0])]
    [InlineData("Latin-1 sign rule", "12\u2030\u00BF34", new int[0])]
    public void The_rows_past_a_bound_of_a_rule_keep_the_standard_breaks_as_chromium_prints_them(string rule, string text, int[] breaks)
    {
        var row = ChromiumLineBreakMatrix.Rows().SingleOrDefault(found => found.Text == text);

        row.Should().NotBeNull($"the {rule} names a measured row");
        row!.Breaks.Should().Equal(breaks);
        ChromiumLineBreakMatrix.VisibleBreaks(text, UnicodeLineBreaking.Find(text)).Should().Equal(breaks, $"the {rule} does not apply here");
        ChromiumLineBreakMatrix.VisibleBreaks(text, ChromiumLineBreaking.Find(text)).Should().Equal(breaks);
    }

    [Fact]
    public void The_rules_keep_every_mandatory_break_and_the_end_of_the_text()
    {
        const string text = "a \u2028b \nc";

        ChromiumLineBreaking.Find(text).Where(found => found.IsMandatory).Should().Equal(UnicodeLineBreaking.Find(text).Where(found => found.IsMandatory));
        ChromiumLineBreaking.Find(text).Select(found => found.Offset).Should().NotContain([2, 5], "no break between a space and a mandatory break");
        ChromiumLineBreaking.Find("").Should().BeEmpty();
    }

    [Fact]
    public void A_pair_with_a_character_that_is_not_ascii_follows_the_standard()
    {
        foreach (var text in new[] { "\u00E9/\u00E9", "\u00E9\u0301/\u00E9", "\U0001F600/\U0001F600", "\uD800/\u00E9" })
        {
            ChromiumLineBreaking.Find(text).Should().Equal(UnicodeLineBreaking.Find(text), text);
        }
    }

    private static string Hex(string text) => string.Join(" ", text.EnumerateRunes().Select(rune => rune.Value.ToString("X4")));
}
