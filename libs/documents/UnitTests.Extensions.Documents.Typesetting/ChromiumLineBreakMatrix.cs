using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using Ave.Extensions.Documents.Typesetting.LineBreaking;

namespace UnitTests.Extensions.Documents.Typesetting;

/// <summary>
/// The measured matrix of Chromium's line breaks (<c>Reference/Chromium/line-breaks.txt</c>, format in the README beside
/// it), and the generator of <c>Ave.Extensions.Documents.Typesetting/LineBreaking/ChromiumLineBreaking.AsciiPairs.g.cs</c>
/// from it. The generator lives in the tests so that a test can prove the committed table is exactly what the matrix
/// says; run the tests with <c>UPDATE_GENERATED=1</c> to rewrite the table after measuring again.
/// </summary>
internal static class ChromiumLineBreakMatrix
{
    /// <summary>One row: its document, its text, and the places where Chromium's print breaks it, each the number of
    /// visible characters before the break.</summary>
    public sealed record Row(string Document, int Line, string Text, ImmutableArray<int> Breaks);

    /// <summary>The characters that print nothing; a break beside one of them is the same print as beside the visible
    /// character next to it, so breaks are counted between visible characters only.</summary>
    public static readonly ImmutableHashSet<int> Invisible =
        [0x20, 0xA0, 0x202F, 0x3000, 0xAD, 0x200B, 0x2060, 0x200E, 0x200F, 0xFEFF, 0x09, 0x0A];

    public static string ResourceFile([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "Reference", "Chromium", "line-breaks.txt"));

    public static string GeneratedFile([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "Ave.Extensions.Documents.Typesetting", "LineBreaking", "ChromiumLineBreaking.AsciiPairs.g.cs"));

    /// <summary>Every row of the matrix, in file order. The file's line endings do not matter.</summary>
    public static IReadOnlyList<Row> Rows()
    {
        var lines = File.ReadAllText(ResourceFile()).ReplaceLineEndings("\n").Split('\n');
        var rows = new List<Row>();
        var document = "";
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith('@'))
            {
                document = line[1..];
                continue;
            }

            var fields = line.Split(';');
            var text = new StringBuilder();
            foreach (var codePoint in fields[0].Split(' '))
            {
                text.Append(char.ConvertFromUtf32(int.Parse(codePoint, NumberStyles.HexNumber, CultureInfo.InvariantCulture)));
            }

            var breaks = fields[1].Length == 0
                ? ImmutableArray<int>.Empty
                : [.. fields[1].Split(' ').Select(found => int.Parse(found, CultureInfo.InvariantCulture))];
            rows.Add(new Row(document, index + 1, text.ToString(), breaks));
        }

        return rows;
    }

    /// <summary>The breaks of a text as the number of visible characters before each: the opportunities that lie
    /// between two visible characters, as a print shows them.</summary>
    public static ImmutableArray<int> VisibleBreaks(string text, IEnumerable<BreakOpportunity> opportunities)
    {
        // For every UTF-16 offset: how many visible characters start before it.
        var visibleBefore = new int[text.Length + 1];
        var count = 0;
        for (var offset = 0; offset < text.Length;)
        {
            var rune = Rune.GetRuneAt(text, offset);
            var visible = !Invisible.Contains(rune.Value);
            for (var unit = 0; unit < rune.Utf16SequenceLength; unit++)
            {
                visibleBefore[offset + unit] = count;
            }

            count += visible ? 1 : 0;
            offset += rune.Utf16SequenceLength;
        }

        visibleBefore[text.Length] = count;
        return [.. opportunities.Select(opportunity => visibleBefore[opportunity.Offset]).Where(at => at > 0 && at < count).Distinct().Order()];
    }

    /// <summary>
    /// The source text of the ASCII pair table: for every pair of printable ASCII characters other than the space, whether
    /// Chromium's print breaks between them, read from the rows <c>ab</c> X Y <c>cd</c> of the document
    /// <c>lb-ascii-letters</c> (a break between X and Y is a break after three visible characters).
    /// </summary>
    public static string GenerateAsciiPairs()
    {
        const int first = ChromiumLineBreaking.FirstAscii;
        const int count = 0x7E - first + 1;
        var table = new bool?[count, count];
        foreach (var row in Rows().Where(row => row.Document == "lb-ascii-letters"))
        {
            var (x, y) = (row.Text[2], row.Text[3]);
            if (x >= first && y >= first)
            {
                table[x - first, y - first] = row.Breaks.Contains(3);
            }
        }

        var source = new StringBuilder();
        source.Append("// <auto-generated>\n");
        source.Append("// Generated from Chromium's measured print of the line-break matrix, the rows ab X Y cd of the document\n");
        source.Append("// lb-ascii-letters in UnitTests.Extensions.Documents.Typesetting/Reference/Chromium/line-breaks.txt, by\n");
        source.Append("// UnitTests.Extensions.Documents.Typesetting/ChromiumLineBreakMatrix.cs. Do not edit: run the tests with\n");
        source.Append("// UPDATE_GENERATED=1 to regenerate.\n");
        source.Append("// </auto-generated>\n");
        source.Append("using System.Collections.Immutable;\n\n");
        source.Append("namespace Ave.Extensions.Documents.Typesetting.LineBreaking;\n\n");
        source.Append("public static partial class ChromiumLineBreaking\n{\n");
        source.Append("    /// <summary>The ASCII pair table: row X, column Y is '|' where a line may break between X and Y, '.' where it\n");
        source.Append("    /// may not; rows and columns from U+0021 to U+007E.</summary>\n");
        source.Append("    internal static readonly ImmutableArray<string> AsciiPairs =\n    [\n");
        for (var x = 0; x < count; x++)
        {
            var marks = new char[count];
            for (var y = 0; y < count; y++)
            {
                marks[y] = table[x, y] switch
                {
                    true => ChromiumLineBreaking.Break,
                    false => ChromiumLineBreaking.NoBreak,
                    null => throw new InvalidDataException($"line-breaks.txt has no row ab U+{x + first:X4} U+{y + first:X4} cd."),
                };
            }

            source.Append(CultureInfo.InvariantCulture, $"        \"{new string(marks)}\", // after U+{x + first:X4}\n");
        }

        source.Append("    ];\n}\n");
        return source.ToString();
    }
}
