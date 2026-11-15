using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Ave.Documents.Core;
using Ave.Documents.DocumentModel;
using Ave.Documents.Images;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.ErrorPaths.Functional;
using Ave.Extensions.Functional;

namespace Ave.Documents.Fluent;

/// <summary>
/// Translates a code-front-end <see cref="Fluent.Document"/> into the engine's <see cref="StyledDocument"/>. This
/// is the front-end's cascade: specified values become computed values (CSS Cascade 4 §4), and every value is
/// validated. The front-end contains no layout code. Tables and rows of blocks are translated by
/// <see cref="TableTranslator"/>.
/// </summary>
public static class DocumentTranslator
{
    /// <summary>
    /// Validates and translates the document. Node ids are assigned in pre-order, starting with 0 for the root box,
    /// then the header and footer areas after the body, so the same document always gets the same ids. ALL problems
    /// in the document are reported together (as a <see cref="DocumentErrorCodes.Multiple"/> error when there are
    /// several), each with the source location of the element that caused it.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <returns>The document model, or the <see cref="DocumentErrorCodes.Input"/> errors.</returns>
    public static Result<StyledDocument, Error> ToModel(Document document)
    {
        var format = document.Format.Text;
        var problems = InputChecks.Page(document.Page, document.Source)
            .Concat(InputChecks.Text(format, document.Source))
            .Concat(format.FontFamily is null ? [InputChecks.MissingFontFamily(document.Source)] : [])
            .ToImmutableArray();

        var inherited = new Inherited(
            new TextStyle(
                format.FontFamily ?? string.Empty,
                format.FontSize ?? UserAgentStyles.InitialFontSize,
                format.FontWeight ?? FontWeight.Regular,
                format.Color ?? Color.Black,
                format.FontStyle ?? FontStyle.Normal),
            format.TextAlign ?? TextAlign.Left);
        var root = Children(document.Body, new NodeId(1), inherited).OnSuccessMap(children =>
            new Box(new NodeId(0), SemanticRole.Generic, LayoutAlgorithmId.Flow, BoxStyle.Initial, children, document.Source));
        var page = PageStyle(document.Page, new NodeId(1 + document.Body.Sum(NodeCount)), inherited);

        return WithProblems(problems, root.OnSuccessBind(box => page.OnSuccessMap(pageStyle => (box, pageStyle))))
            .OnSuccessMap(parts => new StyledDocument(new DocumentMetadata(document.Title, document.Author), parts.pageStyle, parts.box));
    }

    /// <summary>The computed values of the inherited properties that a parent passes down.</summary>
    /// <param name="Text">The text properties.</param>
    /// <param name="Align">The text alignment.</param>
    internal sealed record Inherited(TextStyle Text, TextAlign Align)
    {
        /// <summary>CSS inheritance (CSS Cascade 4 §7.1): what an element does not specify is its parent's value.</summary>
        public Inherited With(TextFormat format) => new(
            new TextStyle(
                format.FontFamily ?? Text.FontFamily,
                format.FontSize ?? Text.FontSize,
                format.FontWeight ?? Text.FontWeight,
                format.Color ?? Text.Color,
                format.FontStyle ?? Text.FontStyle),
            format.TextAlign ?? Align);
    }

    /// <summary>
    /// Translates the elements of one list. A page break is not a node of its own: it sets
    /// <c>break-before: page</c> on the next element of the list (CSS Fragmentation 3 §3.1). Ids continue from
    /// <paramref name="firstId"/>, each element taking as many as it has nodes.
    /// </summary>
    internal static Result<ImmutableArray<Node>, Error> Children(ValueList<Element> elements, NodeId firstId, Inherited inherited)
    {
        var pending = new List<(Element Element, bool PageBreakBefore, NodeId Id)>();
        PageBreakElement? pageBreak = null;
        var nextId = firstId.Value;
        foreach (var element in elements)
        {
            if (element is PageBreakElement breakElement)
            {
                pageBreak = breakElement;
                continue;
            }

            pending.Add((element, pageBreak is not null, new NodeId(nextId)));
            nextId += NodeCount(element);
            pageBreak = null;
        }

        ImmutableArray<Error> problems = pageBreak is null ? [] : [InputChecks.TrailingPageBreak(pageBreak.Source)];
        var nodes = pending.TraverseAll(item => Translate(item.Element, item.Id, item.PageBreakBefore, inherited));
        return WithProblems(problems, nodes);
    }

    /// <summary>How many model nodes an element becomes, so ids can be handed out before translating.</summary>
    internal static int NodeCount(Element element) => element switch
    {
        BlockElement block => 1 + block.Children.Sum(NodeCount),
        ParagraphElement paragraph => 1 + paragraph.Spans.Count,
        ImageElement => 2,
        TableElement table => TableTranslator.NodeCount(table),
        RowElement row => TableTranslator.NodeCount(row),
        _ => 0,
    };

    /// <summary>The computed box style of an element from its specified box values.</summary>
    internal static BoxStyle BoxStyleOf(Cascade.BoxValues box, bool pageBreakBefore, TextAlign align) =>
        BoxStyle.Initial with
        {
            Margin = box.Margin,
            Padding = box.Padding,
            Border = box.Border,
            Background = box.Background,
            BreakBefore = pageBreakBefore ? BreakBetween.Page : box.KeepWithPrevious ? BreakBetween.Avoid : BreakBetween.Auto,
            BreakAfter = box.KeepWithNext ? BreakBetween.Avoid : BreakBetween.Auto,
            BreakInside = box.KeepTogether ? BreakInside.Avoid : BreakInside.Auto,
            TextAlign = align,
        };

    /// <summary>Fails with every problem plus every error of <paramref name="result"/>; without problems,
    /// returns the result unchanged.</summary>
    internal static Result<T, Error> WithProblems<T>(ImmutableArray<Error> problems, Result<T, Error> result)
    {
        if (problems.IsEmpty)
        {
            return result;
        }

        IReadOnlyList<Error> all = result.IsFailure ? [.. problems, .. ResultCollections.Parts(result.Error)] : problems;
        return Result<T, Error>.Failure(ResultCollections.Combine(all));
    }

    private static Result<Node, Error> Translate(Element element, NodeId id, bool pageBreakBefore, Inherited inherited) => element switch
    {
        BlockElement block => Block(block, id, pageBreakBefore, inherited),
        ParagraphElement paragraph => Paragraph(paragraph, id, pageBreakBefore, inherited),
        ImageElement image => Image(image, id, pageBreakBefore),
        TableElement table => TableTranslator.Table(table, id, pageBreakBefore, inherited),
        RowElement row => TableTranslator.Row(row, id, pageBreakBefore, inherited),
        _ => throw new System.InvalidOperationException($"No translation for {element.GetType().Name}."),
    };

    private static Result<Node, Error> Block(BlockElement block, NodeId id, bool pageBreakBefore, Inherited inherited)
    {
        var box = Cascade.Box(block.Format, block.Style);
        var text = Cascade.Text(block.Format, block.Style);
        var problems = BoxProblems(box, block.Source).Concat(InputChecks.Text(text, block.Source)).ToImmutableArray();
        var own = inherited.With(text);
        var children = Children(block.Children, new NodeId(id.Value + 1), own);
        return WithProblems(problems, children).OnSuccessMap(nodes =>
            (Node)new Box(id, SemanticRole.Generic, LayoutAlgorithmId.Flow, BoxStyleOf(box, pageBreakBefore, own.Align), nodes, block.Source));
    }

    /// <summary>
    /// A paragraph becomes a box with inline layout whose children are one inline run per span. Cascade order
    /// for its text: inherited, then the user-agent heading defaults, then the paragraph's own values (style,
    /// then modifiers; see <see cref="Cascade"/>), then each span's own values. A page-counter span becomes a
    /// <see cref="CounterRun"/>.
    /// </summary>
    private static Result<Node, Error> Paragraph(ParagraphElement paragraph, NodeId id, bool pageBreakBefore, Inherited inherited)
    {
        var box = Cascade.Box(paragraph.Format, paragraph.Style);
        var text = Cascade.Text(paragraph.Format, paragraph.Style);
        var problems = BoxProblems(box, paragraph.Source)
            .Concat(InputChecks.Text(text, paragraph.Source))
            .Concat(InputChecks.Role(paragraph.Role, paragraph.Source))
            .Concat(paragraph.Spans.SelectMany(SpanProblems))
            .ToImmutableArray();
        if (!problems.IsEmpty)
        {
            return Result<Node, Error>.Failure(ResultCollections.Combine(problems));
        }

        var own = (inherited with { Text = WithRoleDefaults(inherited.Text, paragraph.Role) }).With(text);
        var runs = paragraph.Spans
            .Select((span, index) => Run(span, new NodeId(id.Value + 1 + index), own.With(Cascade.Text(span.Format, span.Style)).Text))
            .ToImmutableArray();
        return Result<Node, Error>.Success(new Box(
            id, paragraph.Role, LayoutAlgorithmId.Inline, BoxStyleOf(box, pageBreakBefore, own.Align), runs, paragraph.Source));
    }

    private static Node Run(Span span, NodeId id, TextStyle style) =>
        span.Counter is { } counter ? new CounterRun(id, counter, style, span.Source) : new TextRun(id, span.Text, style, span.Source);

    /// <summary>
    /// An image becomes a box laid out by the image algorithm with the decoded image as its only child. The bytes
    /// are decoded here, so a damaged or unsupported file is reported with the element's source location.
    /// </summary>
    private static Result<Node, Error> Image(ImageElement image, NodeId id, bool pageBreakBefore)
    {
        var box = Cascade.Box(image.Format, image.Style);
        var problems = BoxProblems(box, image.Source)
            .Concat(InputChecks.ImageSize(image.SpecifiedWidth, image.SpecifiedHeight, image.Source))
            .ToImmutableArray();
        var decoded = ImageDecoder.Decode(image.File.Bytes.AsMemory()).MapError(error => error.WithSource(image.Source));
        return WithProblems(problems, decoded).OnSuccessMap(data => (Node)new Box(
            id,
            SemanticRole.Figure,
            LayoutAlgorithmId.Image,
            BoxStyleOf(box, pageBreakBefore, TextAlign.Left),
            [new ImageNode(new NodeId(id.Value + 1), data, image.SpecifiedWidth, image.SpecifiedHeight, image.Source)],
            image.Source));
    }

    /// <summary>
    /// The page style: size, margins, and the header and footer areas. Each area's content is wrapped in a flow box
    /// (the root of the area's formatting context) and gets ids after the body. A first-page template exists only
    /// when a first-page header or footer is set; the side it does not set is the normal one.
    /// </summary>
    private static Result<PageStyle, Error> PageStyle(PageSetup page, NodeId firstId, Inherited inherited)
    {
        var areas = new[] { page.Header, page.Footer, page.FirstPageHeader, page.FirstPageFooter };
        var ids = areas.Aggregate(
            (Next: firstId.Value, Ids: ImmutableArray<int>.Empty),
            (state, area) => (state.Next + (area is null ? 0 : 1 + NodeCount(area.Content)), state.Ids.Add(state.Next)));
        return areas.Select((area, index) => (area, id: ids.Ids[index]))
            .TraverseAll(pair => Area(pair.area, new NodeId(pair.id), inherited))
            .OnSuccessMap(translated =>
            {
                var (header, footer, firstHeader, firstFooter) = (translated[0], translated[1], translated[2], translated[3]);
                return new PageStyle(page.Width, page.Height, page.Margins)
                {
                    Template = new PageTemplate(header, footer),
                    FirstPage = firstHeader is null && firstFooter is null ? null : new PageTemplate(firstHeader ?? header, firstFooter ?? footer),
                };
            });
    }

    private static Result<PageArea?, Error> Area(PageAreaSetup? area, NodeId id, Inherited inherited)
    {
        if (area is null)
        {
            return Result<PageArea?, Error>.Success(null);
        }

        var source = area.Content.Source;
        IEnumerable<Error> heightProblems = area.Height is { } height && !(height.IsFinite && height.Points > 0)
            ? [new Error(DocumentErrorCodes.Input.Invalid.Length, $"A header or footer height must be a positive length ({source}).")
                .With(ErrorMetadataKeys.Value, height.ToString()).With(ErrorMetadataKeys.Property, "height").WithSource(source)]
            : [];
        return WithProblems(
            [.. heightProblems],
            Children([area.Content], new NodeId(id.Value + 1), inherited).OnSuccessMap(children =>
                (PageArea?)new PageArea(new Box(id, SemanticRole.Generic, LayoutAlgorithmId.Flow, BoxStyle.Initial, children, source), area.Height)));
    }

    /// <summary>The checks every element with a box gets: margins, padding and border widths.</summary>
    internal static IEnumerable<Error> BoxProblems(Cascade.BoxValues box, SourceLocation source) =>
        InputChecks.Box(box, source).Concat(InputChecks.Border(box.Border, source));

    /// <summary>A span's text values must be valid, and its shared style may not carry box properties or a text
    /// alignment: a span has no box and no lines of its own in this front-end, so they would be silently lost.</summary>
    private static IEnumerable<Error> SpanProblems(Span span) =>
        InputChecks.Text(Cascade.Text(span.Format, span.Style), span.Source)
            .Concat(span.Style.Box.IsEmpty && span.Style.Text.TextAlign is null ? [] : [InputChecks.BoxPropertiesOnSpan(span.Source)]);

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
}
