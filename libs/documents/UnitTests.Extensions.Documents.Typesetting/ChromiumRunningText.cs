using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace UnitTests.Extensions.Documents.Typesetting;

/// <summary>
/// Chromium's print of the running-text documents (<c>Reference/Chromium/line-breaks-running.txt</c>, format in the
/// README beside it): per string, one block per position, as wide as the string up to that position plus half of the
/// next character, with the places where Chromium's print ends the block's lines.
/// </summary>
internal static class ChromiumRunningText
{
    /// <summary>One block: the position its width is cut at, in code points of the string, and the places where Chromium
    /// ends its lines, each the number of visible characters before the place.</summary>
    public sealed record Block(int Position, ImmutableArray<int> Breaks);

    /// <summary>One string of a document, with its blocks in the order of their positions.</summary>
    public sealed record Row(string Document, int Line, string Text, ImmutableArray<Block> Blocks);

    public static string ResourceFile([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "Reference", "Chromium", "line-breaks-running.txt"));

    /// <summary>Every string of the resource, in file order. The file's line endings do not matter.</summary>
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

            var blocks = fields.Skip(1)
                .Select(field => field.Split(' ').Select(number => int.Parse(number, CultureInfo.InvariantCulture)).ToList())
                .Select(numbers => new Block(numbers[0], [.. numbers.Skip(1)]));
            rows.Add(new Row(document, index + 1, text.ToString(), [.. blocks]));
        }

        return rows;
    }

    /// <summary>The UTF-16 offset of a position given in code points.</summary>
    public static int Offset(string text, int position)
    {
        var offset = 0;
        for (var count = 0; count < position; count++)
        {
            offset += Rune.GetRuneAt(text, offset).Utf16SequenceLength;
        }

        return offset;
    }

    /// <summary>The number of visible characters of a text (<see cref="ChromiumLineBreakMatrix.Invisible"/>).</summary>
    public static int Visible(string text) => text.EnumerateRunes().Count(rune => !ChromiumLineBreakMatrix.Invisible.Contains(rune.Value));
}
