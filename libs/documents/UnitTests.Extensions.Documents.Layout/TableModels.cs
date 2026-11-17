using System.Runtime.CompilerServices;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;

namespace UnitTests.Extensions.Documents.Layout;

/// <summary>
/// Document-model builders for tracks tests. Ids come from one instance per test, so every node of a
/// document has its own id. Cells hold one Lato 10 pt paragraph (line height 12 pt).
/// </summary>
internal sealed class TableModels
{
    private int _next = 1;

    /// <summary>The Chromium reference cell style: <c>border: 0.75pt solid #000; padding: 2pt 4pt</c>.</summary>
    public static BoxStyle ReferenceCell { get; } = BoxStyle.Initial with
    {
        Padding = new Edges(2.Pt(), 4.Pt(), 2.Pt(), 4.Pt()),
        Border = Border.All(new BorderSide(0.75.Pt(), Color.Black)),
    };

    public NodeId Next() => new(_next++);

    public Box Cell(string text, BoxStyle? style = null, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        var id = Next();
        var paragraph = new Box(Next(), SemanticRole.Paragraph, LayoutAlgorithmId.Inline, BoxStyle.Initial,
            [new TextRun(Next(), text, Models.Text(), new SourceLocation(file, line))], new SourceLocation(file, line));
        return new Box(id, SemanticRole.TableCell, LayoutAlgorithmId.Flow, style ?? ReferenceCell, [paragraph], new SourceLocation(file, line));
    }

    /// <summary>A reference cell with one paragraph per line.</summary>
    public Box Lines(string[] lines, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        var id = Next();
        ValueList<Node> paragraphs = [.. lines.Select(text => (Node)new Box(Next(), SemanticRole.Paragraph, LayoutAlgorithmId.Inline, BoxStyle.Initial,
            [new TextRun(Next(), text, Models.Text(), new SourceLocation(file, line))], new SourceLocation(file, line)))];
        return new Box(id, SemanticRole.TableCell, LayoutAlgorithmId.Flow, ReferenceCell, paragraphs, new SourceLocation(file, line));
    }

    /// <summary>The ways a cell can draw nothing, for the table sweeps: without content (spot check of fix round 3,
    /// SC-1), and with content that draws nothing — invisible characters, an empty block with padding only (probe run
    /// 35, SP-1).</summary>
    public static IReadOnlyList<string> EmptyKinds { get; } =
        ["no children", "empty text", "only spaces", "empty block", "zero width space", "soft hyphen", "word joiner", "padded block"];

    /// <summary>A cell that draws nothing, of the given kind (<see cref="EmptyKinds"/>), in the given style.</summary>
    public Box Empty(string kind, BoxStyle style)
    {
        Box Paragraph(string text) => new(Next(), SemanticRole.Paragraph, LayoutAlgorithmId.Inline, BoxStyle.Initial, [new TextRun(Next(), text, Models.Text(), Models.At(0))], Models.At(0));
        Box Block(BoxStyle blockStyle) => new(Next(), SemanticRole.Generic, LayoutAlgorithmId.Flow, blockStyle, [], Models.At(0));
        ValueList<Node> children = kind switch
        {
            "no children" => [],
            "empty text" => [Paragraph(string.Empty)],
            "only spaces" => [Paragraph("   ")],
            "empty block" => [Block(BoxStyle.Initial)],
            "zero width space" => [Paragraph("\u200B")],
            "soft hyphen" => [Paragraph("\u00AD")],
            "word joiner" => [Paragraph("\u2060")],
            "padded block" => [Block(BoxStyle.Initial with { Padding = Edges.All(3.Pt()) })],
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "not a kind of cell that draws nothing"),
        };
        return new Box(Next(), SemanticRole.TableCell, LayoutAlgorithmId.Flow, style, children, Models.At(0));
    }

    public Box Row(BoxStyle? style, params Box[] cells) =>
        new(Next(), SemanticRole.TableRow, LayoutAlgorithmId.Tracks, (style ?? BoxStyle.Initial) with { TableDisplay = TableDisplay.Row }, [.. cells], Models.At(0));

    public Box Row(params Box[] cells) => Row(null, cells);

    public Box Body(BoxStyle? style, params Box[] rows) =>
        new(Next(), SemanticRole.TableBody, LayoutAlgorithmId.Tracks, (style ?? BoxStyle.Initial) with { TableDisplay = TableDisplay.RowGroup }, [.. rows], Models.At(0));

    public Box Body(params Box[] rows) => Body(null, rows);

    public Box Head(params Box[] rows) =>
        new(Next(), SemanticRole.TableHead, LayoutAlgorithmId.Tracks, BoxStyle.Initial with { TableDisplay = TableDisplay.HeaderGroup }, [.. rows], Models.At(0));

    public Box Table(ValueList<TrackSize> columns, params Box[] groups) =>
        new(Next(), SemanticRole.Table, LayoutAlgorithmId.Tracks, BoxStyle.Initial with { Columns = columns }, [.. groups], Models.At(0));

    /// <summary>A document whose page area is <paramref name="width"/> × <paramref name="height"/> (10 pt margins).</summary>
    public static StyledDocument Page(double width, double height, params Node[] children) =>
        new(new DocumentMetadata("Test", null), new PageStyle((width + 20).Pt(), (height + 20).Pt(), Edges.All(10.Pt())),
            new Box(new NodeId(0), SemanticRole.Generic, LayoutAlgorithmId.Flow, BoxStyle.Initial, [.. children], Models.At(0)));
}
