using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Ave.Extensions.Documents.Render.Pdf;

/// <summary>
/// Builds the ToUnicode CMap of a composite font (ISO 32000-1 §9.10.3): the map from each glyph code to the
/// Unicode text it shows. Without it a viewer can draw the text but cannot copy, search or read it aloud.
/// </summary>
internal static class ToUnicodeCMap
{
    /// <summary>"There can be a maximum of 100 lines in each ~bfchar set" (Adobe Technical Note #5014, "CMap Reference", the CMap
    /// file format that §9.10.3 refers to).</summary>
    private const int MaximumEntriesPerBlock = 100;

    /// <summary>
    /// Writes the CMap. The structure follows the example in §9.10.3 (Example 2): a code space of two-byte codes
    /// <c>&lt;0000&gt;</c>–<c>&lt;FFFF&gt;</c>, matching the Identity-H encoding where a code is the glyph id, then
    /// one <c>bfchar</c> entry per glyph: the code, and the text in UTF-16BE.
    /// </summary>
    /// <param name="glyphTexts">Each used glyph id and its text, ordered by glyph id.</param>
    /// <returns>The CMap program (ASCII).</returns>
    public static ImmutableArray<byte> Build(ImmutableSortedDictionary<ushort, string> glyphTexts)
    {
        var blocks = glyphTexts.Chunk(MaximumEntriesPerBlock).Select(Block);
        IEnumerable<string> lines =
        [
            "/CIDInit /ProcSet findresource begin",
            "12 dict begin",
            "begincmap",
            "/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def",
            "/CMapName /Adobe-Identity-UCS def",
            "/CMapType 2 def",
            "1 begincodespacerange",
            "<0000> <FFFF>",
            "endcodespacerange",
            .. blocks,
            "endcmap",
            "CMapName currentdict /CMap defineresource pop",
            "end",
            "end",
        ];
        return [.. Encoding.ASCII.GetBytes(string.Join("\n", lines) + "\n")];
    }

    private static string Block(KeyValuePair<ushort, string>[] entries) =>
        $"{entries.Length} beginbfchar\n"
        + string.Concat(entries.Select(entry => $"<{entry.Key:X4}> <{Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes(entry.Value))}>\n"))
        + "endbfchar";
}
