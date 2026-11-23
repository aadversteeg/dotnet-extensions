using System.Collections.Generic;
using System.Linq;
using Ave.Extensions.Documents.Core;

namespace Ave.Extensions.Documents.Layout;

/// <summary>
/// Writes the fragment tree as stable, human-readable text: one line per fragment, children indented by two
/// spaces; positions are relative to the parent. Used for golden-file tests and to see exactly where layout
/// put things and why a page broke where it did.
/// </summary>
/// <example>
/// <code>
/// page 1 size=595.276x841.89
///   box #0 generic at (56.693, 56.693) size=481.89x700.4
///     box #1 heading-1 at (0, 0) size=481.89x28.8
///       line at (0, 0) size=96.3x28.8
///         text #2 Lato-Bold 24pt at (0, 0) size=96.3x28.8 ascent=23.688 "Invoice"
/// </code>
/// </example>
public static class FragmentText
{
    /// <summary>Writes the pages.</summary>
    /// <param name="pages">The fragment tree, one entry per page.</param>
    /// <returns>The text, lines separated by <c>\n</c>, ending with a line feed.</returns>
    public static string Write(ValueList<PageFragment> pages) =>
        string.Join("\n", pages.SelectMany((page, index) => PageLines(page, index + 1))) + "\n";

    private static IEnumerable<string> PageLines(PageFragment page, int number) =>
    [
        $"page {number} size={page.PageSize}",
        .. AreaLines("header", page.Header),
        .. FragmentLines(page.Root, depth: 1),
        .. AreaLines("footer", page.Footer),
    ];

    private static IEnumerable<string> AreaLines(string name, PlacedFragment? area) =>
        area is null ? [] : [$"  {name}", .. FragmentLines(area, depth: 2)];

    private static IEnumerable<string> FragmentLines(PlacedFragment placed, int depth)
    {
        var indent = new string(' ', depth * 2);
        var position = $"at {placed.Offset} size={placed.Fragment.Size}";
        return placed.Fragment switch
        {
            BoxFragment box =>
                [$"{indent}box {box.Box.Id} {box.Box.Role} {position}{BoxExtras(box)}", .. box.Children.SelectMany(child => FragmentLines(child, depth + 1))],
            LineFragment line =>
                [$"{indent}line {position}", .. line.Children.SelectMany(child => FragmentLines(child, depth + 1)),
                    .. line.Groups.Select(part => $"{indent}  group {part.Group.Id} {part.Group.Role.StandardStructureType} area={part.Area} children={part.First}+{part.Count}")],
            TextFragment text =>
                [$"{indent}text {text.Run.Id} {text.Font} {text.Run.Style.FontSize} {position} ascent={InvariantNumber.Format(text.Ascent)} "
                    + QuotedText.Quote(string.Concat(text.Glyphs.Select(glyph => glyph.Text)))],
            ImageFragment image => [$"{indent}image {image.Image.Id} {position} {image.Image.Image.Describe()}"],
            RuleFragment rule => [$"{indent}rule {position} {rule.Color}"],
            SliceFragment slice =>
                [$"{indent}slice {position}{(slice.IsContinuation ? " continued" : string.Empty)}", .. FragmentLines(slice.Content, depth + 1)],
            _ => [$"{indent}unknown {placed.Fragment.GetType().Name} {position}"],
        };
    }

    /// <summary>What a box fragment adds when it is not plain: the border it paints, and the repeated flag.</summary>
    private static string BoxExtras(BoxFragment box) =>
        (box.Border == default ? string.Empty : $" border={box.Border}") + (box.IsRepeated ? " repeated" : string.Empty);
}
