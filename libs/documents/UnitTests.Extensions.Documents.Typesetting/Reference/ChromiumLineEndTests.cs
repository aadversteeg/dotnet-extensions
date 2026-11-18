using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.Documents.Typesetting;
using Xunit.Abstractions;

namespace UnitTests.Extensions.Documents.Typesetting.Reference;

/// <summary>
/// Where a line ends, against Chromium's print: the line breaks and the line widths of 3 667 short paragraphs in Lato
/// Regular 75 pt, in the four alignments, at widths around every place where Chromium's lines change. The texts end lines
/// at soft hyphens after kerned letters, at spaces after kerned letters, beside ligatures, at zero width spaces, at forced
/// breaks with and without a space before them, and at the paragraph's end after a space (README.md in
/// <c>Reference/Chromium</c>). Each paragraph is shaped once and broken as the layout breaks it (the line end at a space
/// kept for left-aligned text, shaped again for the others); then for every line its words and the extent its words cover
/// on the page are compared: the left edge of the first word and the right edge of the last, measured as
/// <c>positions.py</c> measures them (the origin of the last character plus its own advance).
/// </summary>
/// <remarks>
/// One kind of position is not compared, and the test counts it so that it stays what it is: the right edge of a word
/// that holds an invisible character the print draws (a zero width space, or a soft hyphen not at a shown hyphen). The
/// PDF gives that character the advance of the glyph it is drawn with, so the measured edge lies one space further right.
/// The line's width is compared where it shows: by the left edge of right-aligned and centred lines. A line wider than its
/// box starts at the left edge in both (CSS Text 3 §7.1: "If (after justification, if any) the inline contents of a line
/// box are too long to fit within it, then the contents are start-aligned").
/// </remarks>
public partial class ChromiumLineEndTests(ITestOutputHelper output)
{
    private const double Size = 75;
    private const double Tolerance = 0.06;
    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "Reference", "Chromium");

    private sealed record Paragraph(int Index, double Width, TextAlign Align, string Text);

    private sealed record Line(string Words, double Left, double Right, string Raw = "", double Width = 0);

    [GeneratedRegex("<p class=\"l\">#(\\d+)</p><div style=\"width:(\\d+)px;text-align:(\\w+)\">(.*?)</div>")]
    private static partial Regex Case();

    private static IEnumerable<Paragraph> Paragraphs(string align)
    {
        var html = File.ReadAllText(Path.Combine(Folder, $"t-line-ends-{align}.html"));
        foreach (Match match in Case().Matches(html))
        {
            var text = match.Groups[4].Value.Replace("&shy;", "\u00AD", StringComparison.Ordinal).Replace("&#x200B;", "\u200B", StringComparison.Ordinal)
                .Replace("<br>", char.ConvertFromUtf32(0x2028), StringComparison.Ordinal).Replace("&amp;", "&", StringComparison.Ordinal);
            var alignment = match.Groups[3].Value switch { "left" => TextAlign.Left, "right" => TextAlign.Right, "center" => TextAlign.Center, _ => TextAlign.Justify };
            yield return new Paragraph(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) * 0.75, alignment, text);
        }
    }

    /// <summary>Chromium's lines of each paragraph, read from the print: the words after the label "#n" (6 pt), one line per
    /// baseline, positions relative to the page margin (12 pt).</summary>
    private static Dictionary<int, List<Line>> Printed(string align)
    {
        var paragraphs = new Dictionary<int, List<Line>>();
        var (current, page, baseline) = (-1, "", "");
        foreach (var row in File.ReadLines(Path.Combine(Folder, $"t-line-ends-{align}.print.txt")))
        {
            var parts = row.Split(' ', 7);
            if (parts[0] != "word")
            {
                continue;
            }

            var (x0, x1, size, text) = (Number(parts[2]) - 12, Number(parts[3]) - 12, Number(parts[5]), parts[6]);
            if (size < 10 && text.StartsWith('#'))
            {
                current = int.Parse(text[1..], CultureInfo.InvariantCulture);
                paragraphs[current] = [];
                continue;
            }

            var lines = paragraphs[current];
            if (lines.Count == 0 || (parts[1], parts[4]) != (page, baseline))
            {
                lines.Add(new Line(Words(text), x0, x1, text));
                (page, baseline) = (parts[1], parts[4]);
            }
            else
            {
                lines[^1] = lines[^1] with { Words = lines[^1].Words + " " + Words(text), Right = x1, Raw = lines[^1].Raw + " " + text };
            }
        }

        return paragraphs;
    }

    private static double Number(string text) => double.Parse(text, CultureInfo.InvariantCulture);

    /// <summary>Words without the invisible characters, with the shown hyphen as a hyphen-minus.</summary>
    private static string Words(string text) => text.Replace("\u00AD", string.Empty, StringComparison.Ordinal).Replace("\u200B", string.Empty, StringComparison.Ordinal)
        .Replace("\u2010", "-", StringComparison.Ordinal).Trim();

    /// <summary>The engine's lines of a paragraph, broken and placed as the layout does it.</summary>
    private static List<Line> Engine(Paragraph paragraph)
    {
        var font = TestFonts.Regular;
        var glyphs = Shaper.Shape(WhiteSpaceCollapsing.Collapse([paragraph.Text])[0], font, Size).Value;
        var atSpace = paragraph.Align == TextAlign.Left ? LineEndShaping.KeepParagraphGlyphs : LineEndShaping.ShapeAgainBeforeSpace;
        var broken = GreedyLineBreaker.BreakLines([[.. glyphs]], paragraph.Width, (_, text, showsHyphen, budget) => Shaper.ShapeLinePiece(text, font, Size, showsHyphen, budget), atSpace).Value;
        var lines = new List<Line>();
        for (var index = 0; index < broken.Count; index++)
        {
            var (placed, offset) = LineJustification.Place(broken[index], paragraph.Width, paragraph.Align, isLastLine: index == broken.Count - 1);
            var all = placed.Segments.SelectMany(segment => segment.Glyphs).ToList();
            var (pen, right) = (0.0, 0.0);
            foreach (var glyph in all)
            {
                if (!glyph.IsInvisible && !glyph.IsSpace && !glyph.IsLineBreak)
                {
                    right = pen + font.Metrics.ToPoints(font.AdvanceWidth(glyph.Glyph), Size);
                }

                pen += glyph.Advance;
            }

            var words = string.Concat(all.Where(glyph => !glyph.IsInvisible && !glyph.IsLineBreak).Select(glyph => glyph.Text == "\u00AD" ? "-" : glyph.Text));
            lines.Add(new Line(Words(words), offset, offset + right, Width: broken[index].Width));
        }

        return lines;
    }

    [Fact]
    public void The_engine_breaks_every_line_where_chromium_does_and_draws_it_as_wide()
    {
        var (paragraphs, lines, overflowing, invisible) = (0, 0, 0, 0);
        var differences = new List<string>();
        foreach (var align in new[] { "left", "right", "center", "justify" })
        {
            var printed = Printed(align);
            foreach (var paragraph in Paragraphs(align))
            {
                paragraphs++;
                var (theirs, ours) = (printed[paragraph.Index], Engine(paragraph));
                var where = $"{align} #{paragraph.Index} {paragraph.Width / 0.75} px '{paragraph.Text}'";
                if (!theirs.Select(line => line.Words).SequenceEqual(ours.Select(line => line.Words)))
                {
                    differences.Add($"{where}: lines chromium [{string.Join(" | ", theirs.Select(line => line.Words))}] engine [{string.Join(" | ", ours.Select(line => line.Words))}]");
                    continue;
                }

                for (var index = 0; index < theirs.Count; index++)
                {
                    lines++;
                    var (chromium, engine) = (theirs[index], ours[index]);
                    overflowing += engine.Width > paragraph.Width + Fitting.TolerancePoints ? 1 : 0;
                    if (Math.Abs(chromium.Left - engine.Left) > Tolerance)
                    {
                        differences.Add($"{where} line {index}: left chromium {chromium.Left} engine {engine.Left:0.###}");
                    }

                    var justified = paragraph.Align == TextAlign.Justify && index < theirs.Count - 1;
                    if (justified)
                    {
                        continue;
                    }

                    if (chromium.Raw.Contains('\u200B', StringComparison.Ordinal) || (chromium.Raw.Contains('\u00AD', StringComparison.Ordinal) && !chromium.Raw.EndsWith('\u2010')))
                    {
                        invisible++;
                    }
                    else if (Math.Abs(chromium.Right - engine.Right) > Tolerance)
                    {
                        differences.Add($"{where} line {index}: right chromium {chromium.Right} engine {engine.Right:0.###}");
                    }
                }
            }
        }

        output.WriteLine($"{paragraphs} paragraphs, {lines} lines, {overflowing} of them wider than the box, "
            + $"{invisible} right edges after an invisible character not compared; {differences.Count} differences.");
        foreach (var difference in differences.Take(20))
        {
            output.WriteLine(difference);
        }

        differences.Should().BeEmpty();
        paragraphs.Should().Be(3_667, "every measured paragraph was compared");
        lines.Should().Be(10_323, "every line of every paragraph was compared");
        overflowing.Should().Be(663, "lines wider than their box start at the left edge in both");
        invisible.Should().Be(741, "the right edges measured after an invisible character");
    }
}
