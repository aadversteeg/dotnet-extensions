using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Ave.Documents.Core;
using Ave.Documents.DocumentModel;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Documents.Fluent;

/// <summary>
/// Translates a code-front-end <see cref="Fluent.Document"/> into the engine's <see cref="StyledDocument"/>. This
/// is the front-end's cascade: specified values become computed values (CSS Cascade 4 §4), and every value is
/// validated. The front-end contains no layout code.
/// </summary>
public static class DocumentTranslator
{
    /// <summary>
    /// Validates and translates the document. Node ids are assigned in pre-order, starting with 0 for the
    /// root box, so the same document always gets the same ids. ALL problems in the document are reported
    /// together (as a <see cref="DocumentErrorCodes.Multiple"/> error when there are several), each with the
    /// source location of the call that caused it.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <returns>The document model, or the <see cref="DocumentErrorCodes.Input"/> errors.</returns>
    public static Result<StyledDocument, Error> ToModel(Document document)
    {
        var problems = InputChecks.Page(document.Page, document.Source)
            .Concat(InputChecks.Text(document.Format, document.Source))
            .Concat(document.Format.FontFamily is null ? [InputChecks.MissingFontFamily(document.Source)] : [])
            .ToImmutableArray();

        var rootStyle = new TextStyle(
            document.Format.FontFamily ?? string.Empty,
            document.Format.FontSize ?? UserAgentStyles.InitialFontSize,
            document.Format.FontWeight ?? FontWeight.Regular,
            document.Format.Color ?? Color.Black);
        var root = Children(document.Body, new NodeId(1), rootStyle).OnSuccessMap(children =>
            new Box(new NodeId(0), SemanticRole.Generic, LayoutAlgorithmId.Flow, BoxStyle.Initial, children, document.Source));

        return WithProblems(problems, root).OnSuccessMap(box => new StyledDocument(
            new DocumentMetadata(document.Title, document.Author),
            new PageStyle(document.Page.Width, document.Page.Height, document.Page.Margins),
            box));
    }

    /// <summary>
    /// Translates the elements of one list. A page break is not a node of its own: it sets
    /// <c>break-before: page</c> on the next element of the list (CSS Fragmentation 3 §3.1). Ids continue from
    /// <paramref name="firstId"/>, each element taking as many as it has nodes.
    /// </summary>
    private static Result<ImmutableArray<Node>, Error> Children(ImmutableArray<Element> elements, NodeId firstId, TextStyle inherited)
    {
        var pending = new List<(Element Element, BreakBefore BreakBefore, NodeId Id)>();
        PageBreakElement? pageBreak = null;
        var nextId = firstId.Value;
        foreach (var element in elements)
        {
            if (element is PageBreakElement breakElement)
            {
                pageBreak = breakElement;
                continue;
            }

            pending.Add((element, pageBreak is null ? BreakBefore.Auto : BreakBefore.Page, new NodeId(nextId)));
            nextId += NodeCount(element);
            pageBreak = null;
        }

        ImmutableArray<Error> problems = pageBreak is null ? [] : [InputChecks.TrailingPageBreak(pageBreak.Source)];
        var nodes = pending.TraverseAll(item => Translate(item.Element, item.Id, item.BreakBefore, inherited));
        return WithProblems(problems, nodes);
    }

    private static Result<Node, Error> Translate(Element element, NodeId id, BreakBefore breakBefore, TextStyle inherited) => element switch
    {
        BlockElement block => Block(block, id, breakBefore, inherited),
        ParagraphElement paragraph => Paragraph(paragraph, id, breakBefore, inherited),
        _ => throw new System.InvalidOperationException($"No translation for {element.GetType().Name}."),
    };

    private static Result<Node, Error> Block(BlockElement block, NodeId id, BreakBefore breakBefore, TextStyle inherited)
    {
        var problems = InputChecks.Box(block.Box, block.Source).Concat(InputChecks.Text(block.Format, block.Source)).ToImmutableArray();
        var style = Inherit(inherited, block.Format);
        var children = Children(block.Children, new NodeId(id.Value + 1), style);
        return WithProblems(problems, children).OnSuccessMap(nodes =>
            (Node)new Box(id, SemanticRole.Generic, LayoutAlgorithmId.Flow, BoxStyleOf(block.Box, breakBefore), nodes, block.Source));
    }

    /// <summary>
    /// A paragraph becomes a box with inline layout whose children are one text run per span. Cascade order
    /// for its text: inherited, then the user-agent heading defaults, then the paragraph's format, then each
    /// span's format.
    /// </summary>
    private static Result<Node, Error> Paragraph(ParagraphElement paragraph, NodeId id, BreakBefore breakBefore, TextStyle inherited)
    {
        var problems = InputChecks.Box(paragraph.Box, paragraph.Source)
            .Concat(InputChecks.Text(paragraph.Format, paragraph.Source))
            .Concat(InputChecks.Role(paragraph.Role, paragraph.Source))
            .Concat(paragraph.Spans.SelectMany(span => InputChecks.Text(span.Format, span.Source)))
            .ToImmutableArray();
        if (!problems.IsEmpty)
        {
            return Result<Node, Error>.Failure(ResultCollections.Combine(problems));
        }

        var style = Inherit(WithRoleDefaults(inherited, paragraph.Role), paragraph.Format);
        var runs = paragraph.Spans
            .Select((span, index) => (Node)new TextRun(new NodeId(id.Value + 1 + index), span.Text, Inherit(style, span.Format), span.Source))
            .ToImmutableArray();
        return Result<Node, Error>.Success(new Box(
            id, paragraph.Role, LayoutAlgorithmId.Inline, BoxStyleOf(paragraph.Box, breakBefore), runs, paragraph.Source));
    }

    /// <summary>The user-agent defaults for a heading: bold, and a size relative to the inherited one.</summary>
    private static TextStyle WithRoleDefaults(TextStyle inherited, SemanticRole role) => role switch
    {
        SemanticRole.HeadingRole heading => inherited with
        {
            FontWeight = FontWeight.Bold,
            FontSize = inherited.FontSize * UserAgentStyles.HeadingSizeFactor(heading.Level),
        },
        _ => inherited,
    };

    /// <summary>CSS inheritance (CSS Cascade 4 §7.1): a value the author did not set is the parent's value.</summary>
    private static TextStyle Inherit(TextStyle parent, TextFormat format) => new(
        format.FontFamily ?? parent.FontFamily,
        format.FontSize ?? parent.FontSize,
        format.FontWeight ?? parent.FontWeight,
        format.Color ?? parent.Color);

    private static BoxStyle BoxStyleOf(BoxFormat box, BreakBefore breakBefore) =>
        new(box.Margin ?? Edges.Zero, box.Padding ?? Edges.Zero, box.Background, breakBefore);

    /// <summary>How many model nodes an element becomes: itself, plus its children (blocks) or one text run per
    /// span (paragraphs).</summary>
    private static int NodeCount(Element element) => element switch
    {
        BlockElement block => 1 + block.Children.Sum(NodeCount),
        ParagraphElement paragraph => 1 + paragraph.Spans.Length,
        _ => 0,
    };

    /// <summary>Fails with every problem plus every error of <paramref name="result"/>; without problems,
    /// returns the result unchanged.</summary>
    private static Result<T, Error> WithProblems<T>(ImmutableArray<Error> problems, Result<T, Error> result)
    {
        if (problems.IsEmpty)
        {
            return result;
        }

        IReadOnlyList<Error> all = result.IsFailure ? [.. problems, .. ResultCollections.Parts(result.Error)] : problems;
        return Result<T, Error>.Failure(ResultCollections.Combine(all));
    }
}
