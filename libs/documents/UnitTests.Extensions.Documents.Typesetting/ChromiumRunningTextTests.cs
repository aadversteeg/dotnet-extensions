using System.Collections.Immutable;
using Ave.Extensions.Documents.Typesetting.LineBreaking;
using Xunit.Abstractions;

namespace UnitTests.Extensions.Documents.Typesetting;

/// <summary>
/// The line-break rules of Chromium's print (<see cref="ChromiumLineBreaking"/>) in running text, against Chromium's print
/// of the running-text documents (<c>Reference/Chromium/line-breaks-running.txt</c>). The matrix of
/// <see cref="ChromiumLineBreakingTests"/> is measured in a box 1 pt wide, so each of its lines ends at the first place
/// after the line's start where it may: it cannot show a place that is not the first one. Here each block is as wide as
/// its string up to a position plus half of the next character, so Chromium ends the block's first line at the last place
/// at or before that position, or at the first one after it where there is none; and the rules are run ONCE over each
/// string, as the engine runs them over a paragraph.
/// </summary>
/// <remarks>
/// What a row proves: for every block of the string, Chromium's first line ends where one pass of the rules puts that
/// place, so one pass has a place exactly where Chromium ends the first line, and none that Chromium would have taken
/// before it at or before the position. And every later line ends at a place of one pass, and at a place of the rules
/// found anew from that line's start. What it does not prove: the
/// fit of a line in its width (the test has no widths: the block's width is taken as the position), the places past the
/// first line that Chromium did not need, characters and contexts outside the documents' generators, and text in other
/// fonts or sizes. The later lines show the limit of the line start (<see cref="ChromiumLineBreaking"/>, LIMIT): a later
/// line that starts with a hyphen-minus after <c>-</c> or <c>?</c>, before a letter beyond ASCII, a character of class AI or
/// a middle dot, has a place after that hyphen in one pass and none in Chromium, which finds the places of that line anew
/// from its start. The test counts the blocks where one pass has a place inside a later line of Chromium's that the places
/// found anew lack (330, all of that shape); whether the engine would end the line there depends on the line's width,
/// which the test does not have.
/// </remarks>
public class ChromiumRunningTextTests(ITestOutputHelper output)
{
    [Fact]
    public void Every_first_line_of_running_text_ends_where_one_pass_of_the_rules_puts_a_place()
    {
        var rows = ChromiumRunningText.Rows();
        var blocks = rows.SelectMany(row => row.Blocks.Select(block => (Row: row, Block: block))).ToList();
        var failures = blocks
            .Select(found => (found.Row, found.Block, Expected: FirstLineEnd(found.Row.Text, ChromiumLineBreaking.Find(found.Row.Text), found.Block.Position)))
            .Where(found => found.Expected != ChromiumFirstLineEnd(found.Row.Text, found.Block))
            .ToList();
        var standardDiffers = blocks.Count(found => FirstLineEnd(found.Row.Text, UnicodeLineBreaking.Find(found.Row.Text), found.Block.Position) != ChromiumFirstLineEnd(found.Row.Text, found.Block));

        output.WriteLine($"line-breaks-running.txt: {rows.Count} strings, {blocks.Count} blocks; first line as one pass of the rules says: "
            + $"{blocks.Count - failures.Count}, not: {failures.Count}; the standard alone ends {standardDiffers} of them otherwise.");
        foreach (var (row, block, expected) in failures.Take(20))
        {
            output.WriteLine($"line {row.Line} ({row.Document}): {Hex(row.Text)} position {block.Position}: Chromium [{string.Join(",", block.Breaks)}], one pass ends the first line after {expected}");
        }

        rows.Select(row => row.Text).Distinct().Count().Should().Be(rows.Count, "each string is written once");
        rows.Count.Should().Be(1_646, "the whole resource was read: eight documents, each string once");
        blocks.Count.Should().Be(6_362);
        standardDiffers.Should().BeGreaterThan(0, "the method sees a difference in the rules");
        failures.Should().BeEmpty();
    }

    [Fact]
    public void Every_later_line_of_running_text_ends_at_a_place_of_one_pass_and_differs_only_where_a_hyphen_starts_it()
    {
        var rows = ChromiumRunningText.Rows();
        var (lines, missing, notAnew) = (0, new List<string>(), new List<string>());
        var limit = new List<(ChromiumRunningText.Row Row, ChromiumRunningText.Block Block, int Start)>();
        foreach (var row in rows)
        {
            var text = row.Text;
            var once = ChromiumLineBreaking.Find(text);
            var places = ChromiumLineBreakMatrix.VisibleBreaks(text, once).ToHashSet();
            var total = ChromiumRunningText.Visible(text);
            foreach (var block in row.Blocks)
            {
                for (var index = 0; index < block.Breaks.Length; index++)
                {
                    lines++;
                    var (start, end) = (block.Breaks[index], index + 1 < block.Breaks.Length ? block.Breaks[index + 1] : total);
                    if (!places.Contains(start))
                    {
                        missing.Add($"line {row.Line}: {Hex(text)} position {block.Position}: Chromium ends a line after {start}");
                        continue;
                    }

                    // The line as Chromium finds its places: anew from its start, the first place of one pass there.
                    var from = once.Where(found => ChromiumLineBreakMatrix.VisibleBreaks(text, [found]) is [var at] && at == start).Min(found => found.Offset);
                    var anew = ChromiumLineBreakMatrix.VisibleBreaks(text, ChromiumLineBreaking.Find(text[from..]).Select(found => found with { Offset = from + found.Offset })).ToHashSet();
                    if (end < total && !anew.Contains(end))
                    {
                        notAnew.Add($"line {row.Line}: {Hex(text)} position {block.Position}: Chromium ends a line after {end}, not a place anew from {start}");
                    }

                    if (places.Any(at => at > start && at < end && !anew.Contains(at)))
                    {
                        limit.Add((row, block, from));
                    }
                }
            }
        }

        output.WriteLine($"line-breaks-running.txt: {lines} later lines; ending where one pass has no place: {missing.Count}; "
            + $"ending where the places found anew from the line's start have none: {notAnew.Count}; "
            + $"one pass has a place inside the line that the places found anew do not: {limit.Count}, "
            + $"in {limit.Select(found => found.Row.Text).Distinct().Count()} strings.");
        foreach (var failure in missing.Concat(notAnew).Take(20))
        {
            output.WriteLine(failure);
        }

        missing.Should().BeEmpty();
        notAnew.Should().BeEmpty();
        limit.Should().AllSatisfy(found => LineStartLimit(found.Row.Text, found.Start).Should().BeTrue(
            $"only a hyphen-minus after - or ? before a letter beyond ASCII starts such a line ({Hex(found.Row.Text)}, position {found.Block.Position})"));
        limit.Count.Should().Be(330, "the limit of THE LINE START shows on later lines only");
    }

    /// <summary>Where one pass of a rule set ends a block's first line: at the last place at or before the position, else
    /// at the first one after it, else at the end; as the number of visible characters before it.</summary>
    private static int FirstLineEnd(string text, ImmutableArray<BreakOpportunity> opportunities, int position)
    {
        var cut = ChromiumRunningText.Offset(text, position);
        var inside = opportunities.Select(found => found.Offset).Where(offset => offset > 0 && offset < text.Length).ToList();
        var end = inside.Where(offset => offset <= cut).DefaultIfEmpty(-1).Max();
        end = end >= 0 ? end : inside.Where(offset => offset > cut).DefaultIfEmpty(text.Length).Min();
        return ChromiumLineBreakMatrix.VisibleBreaks(text, [new BreakOpportunity(end, false)]) is [var at] ? at : ChromiumRunningText.Visible(text);
    }

    private static int ChromiumFirstLineEnd(string text, ChromiumRunningText.Block block) =>
        block.Breaks.Length > 0 ? block.Breaks[0] : ChromiumRunningText.Visible(text);

    /// <summary>The shape of the limit of THE LINE START: the line starts with a hyphen-minus after a hyphen-minus or a
    /// question mark, and a character beyond ASCII follows it.</summary>
    private static bool LineStartLimit(string text, int start) =>
        start > 0 && start + 1 < text.Length && text[start] == '-' && text[start - 1] is '-' or '?' && text[start + 1] > 0x7F;

    private static string Hex(string text) => string.Join(" ", text.EnumerateRunes().Select(rune => rune.Value.ToString("X4")));
}
