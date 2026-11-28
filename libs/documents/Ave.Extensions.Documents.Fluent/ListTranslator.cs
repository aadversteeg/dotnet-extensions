using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.ErrorPaths.Functional;
using Ave.Extensions.Functional;

namespace Ave.Extensions.Documents.Fluent;

/// <summary>
/// Translates a list (<see cref="ListElement"/>) into the document model's list structure (ISO 32000-1 §14.8.4.3.3): a
/// flow box with the list role and its numbering, one list item per item (<see cref="LayoutAlgorithmId.ListItem"/>) with a
/// label and a body. The front-end computes each label: a number's text, "N. " with the space after the period (CSS Lists 3
/// §3.1, the <c>::marker</c> of <c>decimal</c>, measured in Chromium 159: case <c>li-ol-12</c>), or a bullet the layout
/// draws; layout places it.
/// </summary>
internal static class ListTranslator
{
    /// <summary>The indent of a list when no padding is set: 30 pt, 40 CSS pixels (HTML's user-agent style sheet,
    /// <c>padding-inline-start: 40px</c> on <c>ul</c> and <c>ol</c>).</summary>
    private static readonly BoxFormat Indent = BoxFormat.None with { PaddingLeft = Length.FromPoints(30) };

    /// <summary>How many model nodes a list becomes: the list, and per item the item, its label and the label's run or
    /// symbol (none without a label), its body and the body's content.</summary>
    public static int NodeCount(ListElement list) =>
        1 + list.Items.Sum(item => 1 + (HasLabel(list) ? 2 : 0) + 1 + item.Content.Sum(DocumentTranslator.NodeCount));

    private static bool HasLabel(ListElement list) => list.IsNumbered || list.BulletShape != ListBullet.None;

    /// <summary>Translates a list one level below <paramref name="inherited"/>'s lists.</summary>
    public static Result<Node, Error> List(ListElement list, NodeId id, bool pageBreakBefore, DocumentTranslator.Inherited inherited)
    {
        var box = Cascade.Box(list.Format, list.Style, Indent);
        var text = Cascade.Text(list.Format, list.Style);
        var own = inherited.With(text) with { Lists = inherited.Lists + 1 };
        var problems = DocumentTranslator.BoxProblems(box, list.Source)
            .Concat(InputChecks.Text(text, list.Source))
            .Concat(InputChecks.List(list, own.Lists))
            .ToImmutableArray();
        if (!problems.IsEmpty)
        {
            return Result<Node, Error>.Failure(DiagnosticLimits.Aggregate(problems));
        }

        var numbering = list.IsNumbered ? ListNumbering.Decimal : (list.BulletShape ?? LevelBullet(own.Lists)) switch
        {
            ListBullet.Disc => ListNumbering.Disc,
            ListBullet.Circle => ListNumbering.Circle,
            ListBullet.Square => ListNumbering.Square,
            _ => ListNumbering.None,
        };
        var next = id.Value + 1;
        var first = list.StartNumber ?? 1;
        var items = list.Items.Select((item, index) =>
        {
            var itemId = new NodeId(next);
            next += 1 + (HasLabel(list) ? 2 : 0) + 1 + item.Content.Sum(DocumentTranslator.NodeCount);
            return (Item: item, Id: itemId, Number: first + index);
        }).ToImmutableArray();
        return items.TraverseAll(item => Item(item.Item, item.Id, own, numbering, item.Number))
            .OnSuccessMap(nodes => (Node)new Box(
                id, SemanticRole.List(numbering), LayoutAlgorithmId.Flow, DocumentTranslator.BoxStyleOf(box, pageBreakBefore, own), nodes.ToValueList(), list.Source));
    }

    /// <summary>The bullet of a level (1 for a list in no other list): disc, circle, then square, as HTML's user-agent style
    /// sheet gives nested <c>ul</c> (case <c>li-ul-levels</c>: square again at level 4).</summary>
    private static ListBullet LevelBullet(int level) => level switch
    {
        1 => ListBullet.Disc,
        2 => ListBullet.Circle,
        _ => ListBullet.Square,
    };

    /// <summary>
    /// One item: the item box, its label (a number as text, or a bullet as a <see cref="MarkerSymbol"/> whose structure
    /// element carries the bullet's character as replacement text), and its body with the item's content. The label is in
    /// the item's font without its text decorations, its spaces kept and on one line (a marker's <c>white-space: pre</c>).
    /// </summary>
    private static Result<Node, Error> Item(ListItemElement item, NodeId id, DocumentTranslator.Inherited inherited, ListNumbering numbering, int number)
    {
        var box = Cascade.Box(item.Format, item.Style);
        var text = Cascade.Text(item.Format, item.Style);
        var problems = DocumentTranslator.BoxProblems(box, item.Source).Concat(InputChecks.Text(text, item.Source)).ToImmutableArray();
        var own = inherited.With(text);
        var labelStyle = own.Text with { Decorations = [], WhiteSpaceCollapse = WhiteSpaceCollapse.Preserve, TextWrapMode = TextWrapMode.NoWrap };
        var label = numbering switch
        {
            ListNumbering.None => null,
            ListNumbering.Decimal => Label(id, null, new TextRun(new NodeId(id.Value + 2), number.ToString(CultureInfo.InvariantCulture) + ". ", labelStyle, item.Source), labelStyle, item.Source),
            _ => Label(id, Character(numbering), new MarkerSymbol(new NodeId(id.Value + 2), Shape(numbering), own.Text with { Decorations = [] }, item.Source), labelStyle, item.Source),
        };
        var bodyId = new NodeId(id.Value + (label is null ? 1 : 3));
        var content = DocumentTranslator.Children(item.Content, new NodeId(bodyId.Value + 1), own);
        return DocumentTranslator.WithProblems(problems, content).OnSuccessMap(children =>
        {
            var body = new Box(bodyId, SemanticRole.ListBody, LayoutAlgorithmId.Flow, BoxStyle.Initial, children, item.Source);
            IEnumerable<Node> parts = label is null ? [body] : [label, body];
            return (Node)new Box(id, SemanticRole.ListItem, LayoutAlgorithmId.ListItem, DocumentTranslator.BoxStyleOf(box, false, own), [.. parts], item.Source);
        });
    }

    private static Box Label(NodeId item, string? replacement, Node content, TextStyle style, SourceLocation source) =>
        new(new NodeId(item.Value + 1), SemanticRole.ListLabel(replacement), LayoutAlgorithmId.Inline, BoxStyle.Initial with { Text = style }, [content], source);

    private static MarkerShape Shape(ListNumbering numbering) => numbering switch
    {
        ListNumbering.Circle => MarkerShape.Circle,
        ListNumbering.Square => MarkerShape.Square,
        _ => MarkerShape.Disc,
    };

    /// <summary>The character a bullet stands for, for assistive technology: U+2022 BULLET, U+25E6 WHITE BULLET, U+25AA BLACK
    /// SMALL SQUARE (the symbols of <c>disc</c>, <c>circle</c> and <c>square</c>, CSS Counter Styles 3 §6.1).</summary>
    private static string Character(ListNumbering numbering) => numbering switch
    {
        ListNumbering.Circle => "◦",
        ListNumbering.Square => "▪",
        _ => "•",
    };
}
