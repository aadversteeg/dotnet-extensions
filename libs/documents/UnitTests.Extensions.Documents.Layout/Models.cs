using System.Runtime.CompilerServices;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;

namespace UnitTests.Extensions.Documents.Layout;

/// <summary>
/// Small document models for layout tests. Pages are 200 × 100 pt with 10 pt margins: a page area of
/// 180 × 80 pt. Lato at 10 pt has a line height of (1974 + 426) / 2000 × 10 = 12 pt and an ascent of 9.87 pt
/// (hhea, line gap 0), so a page area holds 6 lines (72 pt) with 8 pt to spare.
/// </summary>
internal static class Models
{
    public const double LineBoxHeight = 12;
    public const double Ascent = 9.87;

    public static TextStyle Text(double size = 10) => new("Lato", size.Pt(), FontWeight.Regular, Color.Black);

    public static Box Paragraph(
        int id, string text, BoxStyle? style = null, double size = 10, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0) =>
        new(new NodeId(id), SemanticRole.Paragraph, LayoutAlgorithmId.Inline, style ?? BoxStyle.Initial,
            [new TextRun(new NodeId(id + 100), text, Text(size), new SourceLocation(file, line))], new SourceLocation(file, line));

    public static Box Flow(
        int id, ValueList<Node> children, BoxStyle? style = null, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0) =>
        new(new NodeId(id), SemanticRole.Generic, LayoutAlgorithmId.Flow, style ?? BoxStyle.Initial, children, new SourceLocation(file, line));

    public static StyledDocument Document(params Node[] children) =>
        new(new DocumentMetadata("Test", null), new PageStyle(200.Pt(), 100.Pt(), Edges.All(10.Pt())), Flow(0, [.. children]));

    /// <summary>A paragraph of n words "w1 w2 …" that each take a line of their own in a 20 pt wide box.</summary>
    public static string Words(int count) => string.Join(" ", Enumerable.Range(1, count).Select(number => $"word{number}"));

    public static SourceLocation At(int line, [CallerFilePath] string file = "") => new(file, line);
}
