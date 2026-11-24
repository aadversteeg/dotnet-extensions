using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Images;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.ErrorPaths.Functional;
using Ave.Extensions.Functional;

namespace Ave.Extensions.Documents.Fluent;

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
    /// in the document are reported together (as a <see cref="ErrorCodes.Aggregate"/> error when there are
    /// several), each with the source location of the element that caused it.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <returns>The document model, or the <see cref="DocumentErrorCodes.Input"/> errors.</returns>
    public static Result<StyledDocument, Error> ToModel(Document document)
    {
        // Checked first, without recursion: everything after this walks the element tree recursively.
        if (InputChecks.TooDeep(document) is { } tooDeep)
        {
            return Result<StyledDocument, Error>.Failure(tooDeep);
        }

        var format = document.Format.Text;
        var problems = InputChecks.Page(document.Page, document.Source)
            .Concat(InputChecks.Text(format, document.Source))
            .Concat(InputChecks.Metadata(document.Title, document.Author, document.Source))
            .Concat(InputChecks.Language(document.Language, document.Source))
            .Concat(InputChecks.Profile(document.Profile, document.Source))
            .Concat(InputChecks.Attachments(document.Attachments))
            .Concat(format.FontFamily is null ? [InputChecks.MissingFontFamily(document.Source)] : [])
            .ToImmutableArray();

        var rootFontSize = format.FontSize ?? UserAgentStyles.InitialFontSize;
        var inherited = new Inherited(
            new TextStyle(
                format.FontFamily ?? string.Empty,
                rootFontSize,
                format.FontWeight ?? FontWeight.Regular,
                format.Color ?? Color.Black,
                format.FontStyle ?? FontStyle.Normal)
            {
                LineHeight = format.LineHeight?.Computed(rootFontSize) ?? LineHeight.Normal.Instance,
                WhiteSpaceCollapse = format.WhiteSpaceCollapse ?? WhiteSpaceCollapse.Collapse,
                TextWrapMode = format.TextWrapMode ?? TextWrapMode.Wrap,
                DecorationSkipInk = format.DecorationSkipInk ?? TextDecorationSkipInk.Auto,
            },
            format.TextAlign ?? TextAlign.Left,
            format.Orphans ?? BoxStyle.Initial.Orphans,
            format.Widows ?? BoxStyle.Initial.Widows).Decorated(format);
        var links = new InternalLinks();
        var root = Children(document.Body, new NodeId(1), inherited with { Links = links }).OnSuccessMap(children =>
            new Box(new NodeId(0), SemanticRole.Document, LayoutAlgorithmId.Flow, BoxStyle.Initial, children, document.Source))
            .OnSuccessBind(links.Resolve);
        var page = PageStyle(document.Page, new NodeId(1 + document.Body.Sum(NodeCount)), inherited);

        // The body and the page areas are translated independently, so the problems of both are reported together.
        return WithProblems([.. problems, .. ProblemsOf(page)], root)
            .OnSuccessBind(box => page.OnSuccessMap(pageStyle => new StyledDocument(Metadata(document), pageStyle, box) { Profile = document.Profile }));
    }

    /// <summary>The document's metadata, carried through every layer unchanged (checked in <see cref="ToModel"/>).</summary>
    private static DocumentMetadata Metadata(Document document) => new(document.Title, document.Author)
    {
        Language = document.Language,
        Attachments = document.Attachments,
    };

    /// <summary>The computed values of the inherited properties that a parent passes down.</summary>
    /// <param name="Text">The text properties.</param>
    /// <param name="Align">The text alignment.</param>
    /// <param name="Orphans">The <c>orphans</c> value (CSS Fragmentation 3 §3.3; initial 2).</param>
    /// <param name="Widows">The <c>widows</c> value (initial 2).</param>
    internal sealed record Inherited(TextStyle Text, TextAlign Align, int Orphans, int Widows)
    {
        /// <summary>The anchors and the links to them of the body; null in a page area, where neither may stand.</summary>
        public InternalLinks? Links { get; init; }

        /// <summary>CSS inheritance (CSS Cascade 4 §7.2): what an element does not specify is its parent's value. A line
        /// height the element specifies is computed with its own font size (CSS 2.1 §10.8.1).</summary>
        public Inherited With(TextFormat format)
        {
            var fontSize = format.FontSize ?? Text.FontSize;
            return new Inherited(
                new TextStyle(
                    format.FontFamily ?? Text.FontFamily,
                    fontSize,
                    format.FontWeight ?? Text.FontWeight,
                    format.Color ?? Text.Color,
                    format.FontStyle ?? Text.FontStyle)
                {
                    LineHeight = format.LineHeight?.Computed(fontSize) ?? Text.LineHeight,
                    WhiteSpaceCollapse = format.WhiteSpaceCollapse ?? Text.WhiteSpaceCollapse,
                    TextWrapMode = format.TextWrapMode ?? Text.TextWrapMode,
                    DecorationSkipInk = format.DecorationSkipInk ?? Text.DecorationSkipInk,
                    Decorations = Text.Decorations,
                },
                format.TextAlign ?? Align,
                format.Orphans ?? Orphans,
                format.Widows ?? Widows) { Links = Links }.Decorated(format);
        }

        /// <summary>The decorations an element applies (CSS Text Decoration 3 §2: "When specified on or propagated to a
        /// block container that establishes an inline formatting context, the decorations are propagated to an anonymous inline box
        /// that wraps all the in-flow inline-level children"): those it gets from its ancestors, outermost first,
        /// then its own lines in the order Chromium paints the lines of one box (underline, overline, line-through; case
        /// <c>dl-lines</c>), each in this element's font, coloured as set or in its text colour.</summary>
        public Inherited Decorated(TextFormat format) =>
            format.Decorations is not { Count: > 0 } lines
                ? this
                : this with
                {
                    Text = Text with
                    {
                        Decorations =
                        [
                            .. Text.Decorations,
                            .. lines.OrderBy(line => PaintOrder(line.Line)).Select(line =>
                                new TextDecoration(line.Line, line.Color ?? Text.Color, Text.FontFamily, Text.FontSize, Text.FontWeight, Text.FontStyle)),
                        ],
                    },
                };

        private static int PaintOrder(TextDecorationLine line) => line switch
        {
            TextDecorationLine.Underline => 0,
            TextDecorationLine.Overline => 1,
            _ => 2,
        };
    }

    /// <summary>
    /// Translates the elements of one list. A page break is not a node of its own: it sets
    /// <c>break-before: page</c> on the next element of the list (CSS Fragmentation 3 §3.1). Ids continue from
    /// <paramref name="firstId"/>, each element taking as many as it has nodes.
    /// </summary>
    internal static Result<ValueList<Node>, Error> Children(ValueList<Element> elements, NodeId firstId, Inherited inherited)
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
        var nodes = pending
            .TraverseAll(item => Translate(item.Element, item.Id, item.PageBreakBefore, inherited))
            .OnSuccessMap(translated => translated.ToValueList());
        return WithProblems(problems, nodes);
    }

    /// <summary>How many model nodes an element becomes, so ids can be handed out before translating.</summary>
    internal static int NodeCount(Element element) => element switch
    {
        BlockElement block => 1 + block.Children.Sum(NodeCount),
        ParagraphElement paragraph => 1 + paragraph.Spans.Sum(SpanNodeCount),
        ImageElement => 2,
        TableElement table => TableTranslator.NodeCount(table),
        RowElement row => TableTranslator.NodeCount(row),
        _ => 0,
    };

    /// <summary>How many model nodes a span becomes: one run, an image's nodes, or a link's group and its run.</summary>
    private static int SpanNodeCount(Span span) => span.Image is { } image ? NodeCount(image) : span.Link is null && span.LinkAnchor is null ? 1 : 2;

    /// <summary>The computed box style of an element from its specified box values and the inherited paragraph
    /// properties it computes (its own <see cref="Inherited"/>).</summary>
    internal static BoxStyle BoxStyleOf(Cascade.BoxValues box, bool pageBreakBefore, Inherited own) =>
        BoxStyleOf(box, pageBreakBefore, own.Align) with { Orphans = own.Orphans, Widows = own.Widows };

    /// <summary>The computed box style of an element without lines (an image): the paragraph properties keep their
    /// initial values.</summary>
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

    /// <summary>The errors of a failed result, each part on its own; none for a success.</summary>
    private static ImmutableArray<Error> ProblemsOf<T>(Result<T, Error> result) =>
        result.IsFailure ? [.. result.Error.Parts()] : [];

    /// <summary>Fails with every problem plus every error of <paramref name="result"/>; without problems,
    /// returns the result unchanged.</summary>
    internal static Result<T, Error> WithProblems<T>(ImmutableArray<Error> problems, Result<T, Error> result)
    {
        if (problems.IsEmpty)
        {
            return result;
        }

        IReadOnlyList<Error> all = result.IsFailure ? [.. problems, .. result.Error.Parts()] : problems;
        return Result<T, Error>.Failure(Errors.Aggregate(all));
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
            (Node)new Box(id, SemanticRole.Generic, LayoutAlgorithmId.Flow, BoxStyleOf(box, pageBreakBefore, own), nodes, block.Source));
    }

    /// <summary>
    /// A paragraph becomes a box with inline layout whose children are one inline run per span. Cascade order
    /// for its text: inherited, then the user-agent heading defaults, then the paragraph's own values (style,
    /// then modifiers; see <see cref="Cascade"/>), then each span's own values. A page-counter span becomes a
    /// <see cref="CounterRun"/>; an image span the image's box (<see cref="Image"/>), which inline layout sets in the line.
    /// </summary>
    private static Result<Node, Error> Paragraph(ParagraphElement paragraph, NodeId id, bool pageBreakBefore, Inherited inherited)
    {
        var box = Cascade.Box(paragraph.Format, paragraph.Style);
        var text = Cascade.Text(paragraph.Format, paragraph.Style);
        var problems = BoxProblems(box, paragraph.Source)
            .Concat(InputChecks.Text(text, paragraph.Source))
            .Concat(InputChecks.Role(paragraph.Role, paragraph.Source))
            .Concat(paragraph.Spans.SelectMany(SpanProblems))
            .Concat(InternalLinks.Problems(paragraph, inherited.Links))
            .ToImmutableArray();
        if (!problems.IsEmpty)
        {
            return Result<Node, Error>.Failure(Errors.Aggregate(problems));
        }

        if (paragraph.AnchorName is { } anchor && inherited.Links?.Anchor(anchor, id, paragraph.Source) is { } duplicate)
        {
            return Result<Node, Error>.Failure(duplicate);
        }

        var own = (inherited with { Text = WithRoleDefaults(inherited.Text, paragraph.Role) }).With(text);
        // A span of only forced line breaks (LineBreak()) draws nothing and has no text style of its own: it takes the
        // paragraph's, so a size set on it cannot make its line taller. An image span takes the ids of its image's nodes.
        var next = id.Value + 1;
        var spans = paragraph.Spans.Select(span =>
        {
            var spanId = new NodeId(next);
            next += SpanNodeCount(span);
            return (Span: span, Id: spanId);
        }).ToImmutableArray();
        var runs = spans.TraverseAll(item => item.Span.Image is { } image
            ? Image(image, item.Id, pageBreakBefore: false)
            : Result<Node, Error>.Success(item.Span.Link is { } || item.Span.LinkAnchor is { }
                ? LinkGroup(item.Span, item.Id, own.With(Cascade.Text(item.Span.Format, item.Span.Style)).Text, inherited.Links)
                : Run(item.Span, item.Id, IsLineBreaks(item.Span) ? own.Text : own.With(Cascade.Text(item.Span.Format, item.Span.Style)).Text)));
        // The paragraph's own font and line height make the strut of its lines (CSS 2.1 §10.8.1).
        return runs.OnSuccessMap(children => (Node)new Box(
            id, paragraph.Role, LayoutAlgorithmId.Inline, BoxStyleOf(box, pageBreakBefore, own) with { Text = own.Text }, children.ToValueList(), paragraph.Source));
    }

    /// <summary>A span whose text is one or more forced line breaks: the line separator U+2028 that LineBreak() writes,
    /// or another mandatory break character that white-space collapsing keeps.</summary>
    private static bool IsLineBreaks(Span span) =>
        span.Counter is null && span.Text.Length > 0 && span.Text.All(character => character is '\u2028' or '\n' or '\r' or '\v' or '\f' or '\u0085');

    /// <summary>A link span: an inline group with the link's role (<see cref="InlineGroup"/>) around one run of its text, both
    /// in the span's text style; its description by THE RULE FOR A LINK'S DESCRIPTION (<see cref="LinkDescription"/>): the
    /// text, else the URI or the anchor's name. A link to an anchor gets its node when the body is built
    /// (<see cref="InternalLinks"/>).</summary>
    private static Node LinkGroup(Span span, NodeId id, TextStyle style, InternalLinks? links)
    {
        var target = span.Link ?? links!.Link(id, span.LinkAnchor!, span.Source);
        var fallback = span.Link is LinkTarget.UriTarget uri ? uri.Value : span.LinkAnchor!;
        return new InlineGroup(
            id,
            SemanticRole.Link(target, LinkDescription.Of(null, span.Text, null, fallback)),
            style,
            [new TextRun(new NodeId(id.Value + 1), span.Text, style, span.Source)],
            span.Source);
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
        // An image takes text values too (they inherit to nothing, but are the author's input): checked as any element's.
        var problems = BoxProblems(box, image.Source)
            .Concat(InputChecks.Text(image.Format.Text, image.Source))
            .Concat(InputChecks.ImageSize(image.SpecifiedWidth, image.SpecifiedHeight, image.Source))
            .Concat(InputChecks.ImageDescription(image))
            .Concat(InputChecks.Characters(image.AlternativeText, "alt", image.Source))
            .ToImmutableArray();
        var decoded = ImageDecoder.Decode(image.File.Bytes.AsMemory()).MapError(error => error.WithSource(image.Source));
        return WithProblems(problems, decoded).OnSuccessMap(data => (Node)new Box(
            id,
            image.IsDecorative ? SemanticRole.Decorative : SemanticRole.Figure(image.AlternativeText),
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
        // Each area's root gets the next id, its content the ids after it; an absent area takes none.
        var areas = new[] { page.Header, page.Footer, page.FirstPageHeader, page.FirstPageFooter };
        var ids = new int[areas.Length];
        var next = firstId.Value;
        for (var index = 0; index < areas.Length; index++)
        {
            ids[index] = next;
            next += areas[index] is { } area ? 1 + NodeCount(area.Content) : 0;
        }

        return areas.Select((area, index) => (area, id: ids[index]))
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
        return WithProblems(
            [.. InputChecks.AreaHeight(area.Height, source), .. InputChecks.PageBreakIn([area.Content], "page header or footer")],
            Children([area.Content], new NodeId(id.Value + 1), inherited).OnSuccessMap(children =>
                (PageArea?)new PageArea(new Box(id, SemanticRole.Generic, LayoutAlgorithmId.Flow, BoxStyle.Initial, children, source), area.Height)));
    }

    /// <summary>The checks every element with a box gets: margins, padding and border widths.</summary>
    internal static IEnumerable<Error> BoxProblems(Cascade.BoxValues box, SourceLocation source) =>
        InputChecks.Box(box, source).Concat(InputChecks.Border(box.Border, source));

    /// <summary>A span's text values must be valid, and its shared style may not carry box properties or a text
    /// alignment: a span has no box and no lines of its own in this front-end, so they would be silently lost.</summary>
    private static IEnumerable<Error> SpanProblems(Span span) =>
        span.Image is not null
            ? span.Format == Style.Empty && span.Style == Style.Empty ? [] : [InputChecks.FormatOnImageSpan(span.Source)]
            : InputChecks.Text(Cascade.Text(span.Format, span.Style), span.Source)
            .Concat(InputChecks.Characters(span.Text, "text", span.Source))
            .Concat(InputChecks.Link(span.Link, span.Source))
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
