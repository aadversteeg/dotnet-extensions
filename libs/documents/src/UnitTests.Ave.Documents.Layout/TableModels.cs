using System.Runtime.CompilerServices;
using Ave.Documents.Core;
using Ave.Documents.DocumentModel;

namespace UnitTests.Ave.Documents.Layout;

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

    public Box Row(BoxStyle? style, params Box[] cells) =>
        new(Next(), SemanticRole.TableRow, LayoutAlgorithmId.TableRow, style ?? BoxStyle.Initial, [.. cells], Models.At(0));

    public Box Row(params Box[] cells) => Row(null, cells);

    public Box Body(BoxStyle? style, params Box[] rows) =>
        new(Next(), SemanticRole.TableBody, LayoutAlgorithmId.TableRowGroup, style ?? BoxStyle.Initial, [.. rows], Models.At(0));

    public Box Body(params Box[] rows) => Body(null, rows);

    public Box Head(params Box[] rows) =>
        new(Next(), SemanticRole.TableHead, LayoutAlgorithmId.TableHeaderGroup, BoxStyle.Initial, [.. rows], Models.At(0));

    public Box Table(ValueList<TrackSize> columns, params Box[] groups) =>
        new(Next(), SemanticRole.Table, LayoutAlgorithmId.Tracks, BoxStyle.Initial with { Columns = columns }, [.. groups], Models.At(0));

    /// <summary>A document whose page area is <paramref name="width"/> × <paramref name="height"/> (10 pt margins).</summary>
    public static StyledDocument Page(double width, double height, params Node[] children) =>
        new(new DocumentMetadata("Test", null), new PageStyle((width + 20).Pt(), (height + 20).Pt(), Edges.All(10.Pt())),
            new Box(new NodeId(0), SemanticRole.Generic, LayoutAlgorithmId.Flow, BoxStyle.Initial, [.. children], Models.At(0)));
}
