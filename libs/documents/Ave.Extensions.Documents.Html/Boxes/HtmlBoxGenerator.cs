using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Css.Properties;
using Ave.Extensions.Documents.Css.Values;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Html.Parsing;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;
using CoreFontStyle = Ave.Extensions.Documents.Core.FontStyle;

namespace Ave.Extensions.Documents.Html.Boxes;

/// <summary>
/// Box generation (CSS Display 3 §2, CSS 2.1 §9.2): a styled HTML document to the document model every front-end hands
/// to layout. <c>display: block</c> makes a block box, <c>inline</c> inline content, <c>none</c> nothing; a block box
/// whose children are all inline-level gets lines (the inline algorithm), one with block-level children stacks them,
/// and the inline-level children between them are wrapped in anonymous block boxes (CSS 2.1 §9.2.1.1), except a run of
/// white space only, which would be collapsed away and generates no box (CSS 2.1 §9.2.2.1). The text is handed on as
/// written: white space is collapsed by inline layout, across the runs of one paragraph and so across inline element
/// boundaries (CSS Text 3 §4.1.1, <c>WhiteSpaceCollapsing</c>), which is the one place the code front-end's text is
/// collapsed too.
/// </summary>
/// <remarks>
/// <para>Every computed value is checked against <see cref="BoxProperties"/>, one row per property of the registry; an
/// element whose rendering no property says is refused by its own rule (wbr, q); br is a forced line break and a
/// block-level img an image box (<c>HtmlBoxGenerator.LineBreaks.cs</c>, <c>HtmlBoxGenerator.Images.cs</c>). A refusal names the
/// property and the value and points at the element's start tag. Under <see cref="UnsupportedPolicy.Refuse"/> a document
/// with any refusal fails with all of them. Under <see cref="UnsupportedPolicy.Warn"/> the refusals are diagnostics
/// and the document is laid out with each refused value replaced by the document model's initial value of what it
/// sets (no margin, padding, border or background; no forced or avoided break; black text), an element of a refused
/// display type as a block box, and an element refused by its own rule, or a block-level box inside an inline box,
/// left out with its content. A missing font face and boxes nested deeper than the model allows
/// (<see cref="ModelLimits.MaxNestingDepth"/>) fail under both: there is nothing to draw them with.</para>
/// <para>The page (CSS Paged Media 3 §3, §7): the <c>size</c> and margins of <c>@page</c> rules without a selector;
/// without them, the caller's page (<see cref="StyledHtmlDocument.Page"/>, from <see cref="HtmlStyleOptions.Page"/>), as
/// Chromium prints on the paper of the print call (measured: case <c>h-default-page</c> of
/// <c>tools/reference/chromium</c>, printed on US Letter, 612 × 792 pt, no margins). The layout policies are Chromium's
/// (<see cref="LayoutProfile"/>: <see cref="MonolithicOverflow.Slice"/>, <see cref="EmptyRowParts.Allow"/>,
/// <see cref="FontMetricRounding.WholePixels"/>, <see cref="FontSizeRounding.FlooredToPixelFractions"/>,
/// <see cref="LengthRounding.SixtyFourthPixels"/>). The metadata: the title element's text (HTML Standard §4.2.2) and the
/// root element's language (§3.2.6.2).</para>
/// <para>The tree is walked with an explicit stack, never by recursion, so the depth of the input costs heap, not call
/// frames.</para>
/// </remarks>
public static partial class HtmlBoxGenerator
{
    /// <summary>The elements whose rendering no property says, which box generation does not draw.</summary>
    internal static readonly IReadOnlyDictionary<string, string> RefusedRenderings = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["wbr"] = "a line break opportunity",
        ["q"] = "quotation marks generated before and after its content",
    };

    /// <summary>Chromium's layout policies, which the HTML front-end follows.</summary>
    public static LayoutProfile ChromiumProfile { get; } = new()
    {
        MonolithicOverflow = MonolithicOverflow.Slice,
        EmptyRowParts = EmptyRowParts.Allow,
        FontMetricRounding = FontMetricRounding.WholePixels,
        FontSizeRounding = FontSizeRounding.FlooredToPixelFractions,
        LengthRounding = LengthRounding.SixtyFourthPixels,
        SplitLineHalves = SplitLineHalves.AtLeastOnePixel,
        TableEndOverflow = TableEndOverflow.ContinuesOnNextPage,
        BodyClip = BodyClip.BodyArea,
        CellsBesideSlices = CellsBesideSlices.Below,
        CutRowAlignment = CutRowAlignment.TopOnEveryPage,
        RepeatedHeaderRows = RepeatedHeaderRows.WhereContentIsLaidOut,
        SliceRestEnd = SliceRestEnd.AtLineCentre,
        InlineBoxSnapping = InlineBoxSnapping.WholePixels,
    };

    /// <summary>Generates the boxes of a styled document.</summary>
    /// <param name="styled">The document, as <see cref="HtmlStyleResolver.Resolve"/> gave it.</param>
    /// <param name="options">The fonts, resources, base URL and policy.</param>
    /// <returns>The document model and its fonts; or the refusals (under <see cref="UnsupportedPolicy.Refuse"/>); or the
    /// first error that leaves nothing to draw.</returns>
    public static Result<HtmlBoxes, Error> Generate(StyledHtmlDocument styled, HtmlBoxOptions options)
    {
        ArgumentNullException.ThrowIfNull(styled);
        ArgumentNullException.ThrowIfNull(options);
        var run = new Run(styled, options);
        var document = run.Generate();
        if (run.Failures.Count > 0)
        {
            return Result<HtmlBoxes, Error>.Failure(Combined([.. run.Failures, .. options.Unsupported == UnsupportedPolicy.Refuse ? run.Refusals : []]));
        }

        if (options.Unsupported == UnsupportedPolicy.Refuse && run.Refusals.Count > 0)
        {
            return Result<HtmlBoxes, Error>.Failure(Combined(run.Refusals));
        }

        return Result<HtmlBoxes, Error>.Success(new HtmlBoxes(document, run.Fonts.Used, [.. styled.Diagnostics, .. run.Refusals]));
    }

    private static Error Combined(IReadOnlyList<Error> errors) => errors.Count == 1 ? errors[0] : Errors.Aggregate([.. errors]);

    /// <summary>A block box being built: its element, its id, its level and what goes into it.</summary>
    private sealed class BlockFrame(StyledElement element, NodeId id, int depth, bool[] kinds, bool[] dropped)
    {
        public StyledElement Element { get; } = element;
        public NodeId Id { get; } = id;
        public int Depth { get; } = depth;

        /// <summary>Per child: true when block-level.</summary>
        public bool[] IsBlockChild { get; } = kinds;

        /// <summary>Per child: an inline-level child of a run of white space between blocks, which generates nothing.</summary>
        public bool[] Dropped { get; } = dropped;

        public bool HasBlockChildren => IsBlockChild.Any(isBlock => isBlock);
        public List<Node> Nodes { get; } = [];
        public (NodeId Id, List<Node> Runs)? Anonymous { get; set; }
        public int Next { get; set; }

        /// <summary>A forced break before the next box, from the break-after of the box before it.</summary>
        public bool ForcedBreakPending { get; set; }

        public bool ForcedBreakBefore { get; set; }
    }

    /// <summary>An inline element whose content is being added to <see cref="Target"/>.</summary>
    private sealed class InlineFrame(StyledElement element, List<Node> target, int depth)
    {
        public StyledElement Element { get; } = element;
        public List<Node> Target { get; } = target;
        public int Depth { get; } = depth;
        public int Next { get; set; }

        /// <summary>For the frame of an <c>a</c> element that is a link: the group being built, whose content is
        /// <see cref="Target"/>; null otherwise.</summary>
        public LinkFrame? Link { get; init; }

        /// <summary>Whether the frame lies inside a link.</summary>
        public bool InLink { get; init; }

        /// <summary>For the frame of an inline element a link leads to, with a language of its own or with a box of its own:
        /// the span group being built; null otherwise.</summary>
        public SpanFrame? Span { get; init; }

        /// <summary>Whether the frame lies inside an inline element with a box of its own (<see cref="InlineBoxStyle"/>).</summary>
        public bool InBox { get; init; }
    }

    /// <summary>A link being built: the content of its parent it goes into, its id, its URI (for a link inside the document
    /// the <c>href</c> without its white space, for the messages and the description), the fragment of a link inside the
    /// document or null, and its text style.</summary>
    private sealed record LinkFrame(List<Node> Parent, NodeId Id, string Uri, string? Fragment, TextStyle Style)
    {
        /// <summary>The link's language where it differs from its parent's, or null.</summary>
        public string? Language { get; init; }
    }

    /// <summary>An inline element a link leads to, being built as a span group: the content of its parent it goes into, its
    /// id and its text style.</summary>
    private sealed record SpanFrame(List<Node> Parent, NodeId Id, TextStyle Style, string? Language)
    {
        /// <summary>The element's own box, or null.</summary>
        public InlineBoxStyle? Box { get; init; }

        /// <summary>Whether a link leads to the element.</summary>
        public bool IsTarget { get; init; }
    }

    private sealed partial class Run(StyledHtmlDocument styled, HtmlBoxOptions options)
    {
        private readonly string _sourceName = styled.SourceName;
        private readonly Dictionary<StyledElement, HashSet<string>> _refused = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<StyledElement, TextStyle?> _text = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<StyledElement, TextStyle?> _decorated = new(ReferenceEqualityComparer.Instance);
        private int _nextId;
        private bool _tooDeep;

        public List<Error> Refusals { get; } = [];
        public List<Error> Failures { get; } = [];
        public FontMatcher Fonts { get; } = new(styled.FontFaces, options, styled.SourceName);

        public StyledDocument Generate()
        {
            var metadata = new DocumentMetadata(Title(styled.Root), Meta(styled.Root, "author"))
            {
                Language = string.IsNullOrEmpty(styled.Root.Language) ? null : styled.Root.Language,
                Description = Meta(styled.Root, "description"),
            };
            if (Display(styled.Root) == "none")
            {
                // CSS Display 3 §2.5: the root with display: none generates no box at all. Chromium prints one empty page
                // on the paper of the print call, and the @page rules have no effect (measured, case
                // h-root-display-none: an @page of 300 x 100 pt prints US Letter, 612 x 792 pt).
                var empty = new Box(NewId(), SemanticRole.Generic, LayoutAlgorithmId.Flow, BoxStyle.Initial, [], Source(styled.Root.Element.Position));
                return new StyledDocument(metadata, new PageRules(_sourceName, Refusals).Apply([], styled.Page), empty) { Profile = ChromiumProfile };
            }

            FindTargets(styled.Root);
            var root = ResolveLinks(Build(styled.Root));
            var page = Page();
            return new StyledDocument(metadata, page, root) { Profile = ChromiumProfile };
        }

        private NodeId NewId() => new(_nextId++);

        private SourceLocation Source(TextPosition position) => new(_sourceName, position.Line);

        private void Refuse(ErrorCode code, string message, string value, TextPosition position, string? property = null)
        {
            var error = new Error(code, message).With(ErrorMetadataKeys.Value, value);
            Refusals.Add((property is null ? error : error.With(ErrorMetadataKeys.Property, property)).WithSource(_sourceName, position));
        }

        /// <summary>The display of a rendered element: block, inline or none; a refused type counts as block.</summary>
        private static string Display(StyledElement element) => element.Style[PropertyRegistry.Display] switch
        {
            CssKeyword { Name: "inline" } => "inline",
            CssKeyword { Name: "none" } => "none",
            _ => "block",
        };

        private static bool IsCollapsibleOnly(StyledNode node) => node is StyledText text && text.Text.Data.All(IsCollapsible);

        /// <summary>The white space that collapses (CSS Text 3 §4.1.1: space, tab, segment breaks; the HTML parser turns a
        /// CR LF pair into one line feed and keeps a lone CR from a character reference, which Chromium prints as a
        /// space, case <c>h-char-cr</c>).</summary>
        private static bool IsCollapsible(char c) => c is ' ' or '\t' or '\n' or '\r';

        /// <summary>
        /// The text of a run, with the characters that are not drawn refused, and under <see cref="UnsupportedPolicy.Warn"/>
        /// left out: a control character other than tab, line feed and carriage return, a noncharacter, half of a
        /// surrogate pair, and the line and paragraph separators. Measured (cases <c>h-char-*</c>): Chromium draws form
        /// feed and next line (U+0085) as a missing glyph one em wide, which the engine has no glyph for, and the
        /// separators as a space that does not collapse with the spaces beside it, where layout would break the line
        /// (UAX #14 class BK). One pass over the text and at most one refusal per text, as the parser keeps its own
        /// diagnostics bounded: it names the first such character, its offset and how many the text holds.
        /// </summary>
        private string Characters(HtmlText text)
        {
            var data = text.Data;
            var first = -1;
            for (var i = 0; i < data.Length && first < 0; i += Length(data, i))
            {
                first = IsDrawn(data, i) ? -1 : i;
            }

            if (first < 0)
            {
                return data;
            }

            var kept = new System.Text.StringBuilder(data.Length);
            kept.Append(data, 0, first);
            var count = 0;
            for (var i = first; i < data.Length;)
            {
                var length = Length(data, i);
                if (IsDrawn(data, i))
                {
                    kept.Append(data, i, length);
                }
                else
                {
                    count++;
                }

                i += length;
            }

            var codePoint = Length(data, first) == 2 ? char.ConvertToUtf32(data[first], data[first + 1]) : data[first];
            var others = count == 1 ? string.Empty : $" ({count.ToString(CultureInfo.InvariantCulture)} such characters in this text)";
            var error = new Error(HtmlStyleErrorCodes.Unsupported.Character, $"The character U+{codePoint:X4} is not drawn in this version{others}: only text, tab, line feed and carriage return are.")
                .With(ErrorMetadataKeys.Value, $"U+{codePoint:X4}").With(ErrorMetadataKeys.Offset, first).With(ErrorMetadataKeys.Count, count);
            Refusals.Add(error.WithSource(_sourceName, text.Position));
            return kept.ToString();

            // A surrogate pair is one character; a lone surrogate is one code unit.
            static int Length(string data, int i) => char.IsHighSurrogate(data[i]) && i + 1 < data.Length && char.IsLowSurrogate(data[i + 1]) ? 2 : 1;

            static bool IsDrawn(string data, int i)
            {
                var unit = data[i];
                if (Length(data, i) == 2)
                {
                    return (char.ConvertToUtf32(unit, data[i + 1]) & 0xFFFE) != 0xFFFE;
                }

                return !char.IsSurrogate(unit)
                    && (unit is '\t' or '\n' or '\r' || !(char.IsControl(unit) || unit is '\u2028' or '\u2029' || unit is >= '\uFDD0' and <= '\uFDEF' || (unit & 0xFFFE) == 0xFFFE));
            }
        }

        /// <summary>
        /// Checks a rendered element once, the first time it is met: its own rule, then every computed value. True when
        /// it is drawn; false when it is left out (its own rule refuses it).
        /// </summary>
        private bool Check(StyledElement element, bool isBlock)
        {
            var html = element.Element;
            if (html.Namespace == ElementNamespace.Html && RefusedRenderings.TryGetValue(html.LocalName, out var rendering))
            {
                Refuse(HtmlStyleErrorCodes.Unsupported.Rendering, $"The element <{html.LocalName}> is not drawn in this version: {rendering}.", html.LocalName, html.Position);
                return false;
            }

            var facts = new ElementFacts(
                element.Style,
                isBlock,
                ReferenceEquals(element, styled.Root) || (html.Namespace == ElementNamespace.Html && html.LocalName == "body"),
                element.Children.Any(child => child is StyledText),
                element.Children.Any(child => child is StyledText && !IsCollapsibleOnly(child)),
                element.Children.Count > 0,
                IsReplaced: IsHtml(element, "img"));
            var refused = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (property, reason) in BoxProperties.Refusals(facts))
            {
                var value = element.Style[property];
                var code = property.Name == "background-color" && facts.IsCanvasElement ? HtmlStyleErrorCodes.Unsupported.CanvasBackground : HtmlStyleErrorCodes.Unsupported.Value;
                Refuse(code, $"'{property.Name}: {value}' on <{html.LocalName}> is not drawn: {reason}.", value.ToString()!, html.Position, property.Name);
                refused.Add(property.Name);
            }

            _refused[element] = refused;
            RefuseBlockLink(element, isBlock);
            return true;
        }

        /// <summary>The computed value, or null when it was refused (the caller then takes the model's initial value).</summary>
        private CssValue? Value(StyledElement element, string name) =>
            _refused.TryGetValue(element, out var refused) && refused.Contains(name) ? null : element.Style[PropertyRegistry.Find(name)!];

        /// <summary>The box tree, built depth first with a stack of frames: ids in pre-order (a box's id before its
        /// content's), every check made once per element.</summary>
        private Box Build(StyledElement root)
        {
            Check(root, isBlock: true);
            var stack = new Stack<object>();
            stack.Push(Frame(root, NewId(), 0));
            Box? result = null;
            while (stack.Count > 0)
            {
                switch (stack.Peek())
                {
                    case InlineFrame inline when inline.Next >= inline.Element.Children.Count:
                        stack.Pop();
                        if (inline.Link is { } link)
                        {
                            FinishLink(inline, link);
                        }

                        if (inline.Span is { } span)
                        {
                            FinishSpan(inline, span);
                        }

                        break;
                    case InlineFrame inline:
                        Inline(stack, inline, inline.Element.Children[inline.Next++]);
                        break;
                    case BlockFrame block when block.Next >= block.Element.Children.Count:
                        stack.Pop();
                        var (box, forcedAfter) = Finish(block);
                        if (stack.Count == 0)
                        {
                            // A forced break after the last box of the document has no page to start: Chromium prints no
                            // empty page after it (measured, cases h-break-after-last, h-break-after-last-nested).
                            result = box;
                        }
                        else
                        {
                            var parent = (BlockFrame)stack.Peek();
                            parent.Nodes.Add(LanguageOf(block.Element, parent.Element) is { } language ? box with { Language = language } : box);
                            parent.ForcedBreakPending = forcedAfter;
                        }

                        break;
                    case BlockFrame block:
                        var index = block.Next++;
                        Child(stack, block, index);
                        break;
                }
            }

            return result!;
        }

        private BlockFrame Frame(StyledElement element, NodeId id, int depth)
        {
            Placed(element, id);
            var children = element.Children;
            var kinds = new bool[children.Count];
            var rendered = new bool[children.Count];
            for (var i = 0; i < children.Count; i++)
            {
                rendered[i] = children[i] is StyledText || Display((StyledElement)children[i]) != "none";
                kinds[i] = rendered[i] && children[i] is StyledElement child && Display(child) == "block";
            }

            // CSS 2.1 §9.2.1.1 / §9.2.2.1: between block-level children, a run of inline-level children that is white
            // space only generates no anonymous box; with no block-level child the content is one inline run anyway.
            var dropped = new bool[children.Count];
            if (kinds.Any(isBlock => isBlock))
            {
                var start = 0;
                for (var i = 0; i <= children.Count; i++)
                {
                    if (i < children.Count && !kinds[i])
                    {
                        continue;
                    }

                    var group = Enumerable.Range(start, i - start).Where(j => rendered[j]).ToList();
                    if (group.All(j => IsCollapsibleOnly(children[j])))
                    {
                        group.ForEach(j => dropped[j] = true);
                    }

                    start = i + 1;
                }
            }

            return new BlockFrame(element, id, depth, kinds, dropped);
        }

        private bool Deeper(int depth, TextPosition position)
        {
            if (depth <= ModelLimits.MaxNestingDepth)
            {
                return false;
            }

            if (!_tooDeep)
            {
                _tooDeep = true;
                Failures.Add(new Error(HtmlStyleErrorCodes.Unsupported.TooDeep, $"Boxes may nest at most {ModelLimits.MaxNestingDepth} levels deep below the root element.")
                    .With(ErrorMetadataKeys.Value, ModelLimits.MaxNestingDepth.ToString(CultureInfo.InvariantCulture)).WithSource(_sourceName, position));
            }

            return true;
        }

        private void Child(Stack<object> stack, BlockFrame block, int index)
        {
            var node = block.Element.Children[index];
            if (block.Dropped[index] || (node is StyledElement hidden && Display(hidden) == "none") || TableChild(stack, block, node))
            {
                RefuseBreakBetweenBlocks(block.Dropped[index] ? node : null);
                return;
            }

            if (block.IsBlockChild[index])
            {
                var element = (StyledElement)node;
                CloseAnonymous(block);
                if (IsHtml(element, "img"))
                {
                    Image(block, element);
                    return;
                }

                if (IsHtml(element, "br"))
                {
                    BlockLineBreak(element);
                    return;
                }

                if (!Check(element, isBlock: true) || Deeper(block.Depth + 1, element.Element.Position))
                {
                    return;
                }

                var frame = Frame(element, NewId(), block.Depth + 1);
                frame.ForcedBreakBefore = block.ForcedBreakPending;
                block.ForcedBreakPending = false;
                stack.Push(frame);
                return;
            }

            // Inline-level: into the block's own lines, or into the anonymous block box between its block children.
            List<Node> target;
            int depth;
            if (block.HasBlockChildren)
            {
                if (block.Anonymous is null)
                {
                    if (Deeper(block.Depth + 1, Position(node)))
                    {
                        return;
                    }

                    block.Anonymous = (NewId(), []);
                }

                target = block.Anonymous.Value.Runs;
                depth = block.Depth + 2;
            }
            else
            {
                target = block.Nodes;
                depth = block.Depth + 1;
            }

            Inline(stack, new InlineFrame(block.Element, target, depth), node);
        }

        private static TextPosition Position(StyledNode node) => node is StyledElement element ? element.Element.Position : ((StyledText)node).Text.Position;

        /// <summary>One child of inline content: a text becomes a run in its parent's text style; an inline element's
        /// content follows (its frame); a block-level child is refused.</summary>
        private void Inline(Stack<object> stack, InlineFrame frame, StyledNode node)
        {
            switch (node)
            {
                case StyledText text:
                    if (Deeper(frame.Depth, text.Text.Position))
                    {
                        return;
                    }

                    var preservedTab = RefusePreservedTab(text);
                    if (TextStyleOf(frame.Element) is { } style)
                    {
                        var data = Characters(text.Text);
                        frame.Target.Add(new TextRun(NewId(), preservedTab ? data.Replace('\t', ' ') : data, style, Source(text.Text.Position)));
                    }

                    break;
                case StyledElement element when Display(element) == "none":
                    break;
                case StyledElement element when IsHtml(element, "br") && Display(element) == "inline":
                    LineBreak(frame, element);
                    break;
                case StyledElement element when IsHtml(element, "img"):
                    InlineImage(frame, element);
                    break;
                case StyledElement element when Display(element) == "inline":
                    if (Check(element, isBlock: false) && !Deeper(frame.Depth, element.Element.Position))
                    {
                        if (StartLink(stack, frame, element) || StartSpan(stack, frame, element))
                        {
                            break;
                        }

                        // CSS 2.1 §10.8.1: every inline box, also one without a glyph, has a line height that counts
                        // for the line box ("empty inline elements ... still have margins, padding, borders and a line
                        // height"); the model has no inline box, so an empty run in the element's style carries it.
                        if (TextStyleOf(element) is { } own)
                        {
                            frame.Target.Add(new TextRun(NewId(), string.Empty, own, Source(element.Element.Position)));
                        }

                        stack.Push(new InlineFrame(element, frame.Target, frame.Depth) { InLink = frame.InLink, InBox = frame.InBox });
                    }

                    break;
                case StyledElement element:
                    Refuse(HtmlStyleErrorCodes.Unsupported.BlockInInline, $"The block-level <{element.Element.LocalName}> inside the inline <{frame.Element.Element.LocalName}> is not drawn in this version.", element.Element.LocalName, element.Element.Position);
                    break;
            }
        }

        private void CloseAnonymous(BlockFrame block)
        {
            if (block.Anonymous is not { } anonymous)
            {
                return;
            }

            block.Anonymous = null;
            var inherited = Inherited(block.Element);
            var forced = block.ForcedBreakPending;
            block.ForcedBreakPending = false;
            var (layout, children) = Content(anonymous.Runs);
            block.Nodes.Add(new Box(anonymous.Id, SemanticRole.Generic, layout,
                inherited with { BreakBefore = forced ? BreakBetween.Page : BreakBetween.Auto, Text = layout == LayoutAlgorithmId.Inline ? TextStyleOf(block.Element) : null },
                children, Source(block.Element.Element.Position)));
        }

        /// <summary>Lines for inline content with something to draw; else an empty block (white space that collapses away,
        /// CSS 2.1 §9.4.2: a line box with no text and no preserved white space is treated as not there).</summary>
        private static (LayoutAlgorithmId Layout, ValueList<Node> Children) Content(List<Node> runs) =>
            runs.All(run => run is TextRun text && text.Text.All(c => CollapsesUnder(c, text.Style.WhiteSpaceCollapse)))
                ? (LayoutAlgorithmId.Flow, [])
                : (LayoutAlgorithmId.Inline, [.. runs]);

        private (Box Box, bool ForcedAfter) Finish(BlockFrame block)
        {
            CloseAnonymous(block);
            var element = block.Element;
            var (layout, children) = block.HasBlockChildren ? (LayoutAlgorithmId.Flow, (ValueList<Node>)[.. block.Nodes]) : Content(block.Nodes);
            var style = BoxStyleOf(element) with { Text = layout == LayoutAlgorithmId.Inline ? TextStyleOf(element) : null };
            if (block.ForcedBreakBefore)
            {
                style = style with { BreakBefore = BreakBetween.Page };
            }

            // CSS Fragmentation 3 §3.1: a forced break after the last child is a break after this box.
            var forcedAfter = block.ForcedBreakPending || Value(element, "break-after") is CssKeyword { Name: "page" };
            return (TablePartBox(block, new Box(block.Id, RoleOf(element), layout, style, children, Source(element.Element.Position))), forcedAfter);
        }

        private SemanticRole RoleOf(StyledElement element)
        {
            var html = element.Element;
            if (ReferenceEquals(element, styled.Root))
            {
                return SemanticRole.Document;
            }

            return html.Namespace != ElementNamespace.Html ? SemanticRole.Generic : html.LocalName switch
            {
                "p" => SemanticRole.Paragraph,
                ['h', >= '1' and <= '6'] name => SemanticRole.Heading(name[1] - '0'),
                _ => SemanticRole.Generic,
            };
        }

        /// <summary>The inherited box values of an element, for an anonymous box inside it (CSS 2.1 §9.2.1.1: "The
        /// properties of anonymous boxes are inherited from the enclosing non-anonymous box").</summary>
        private BoxStyle Inherited(StyledElement element) => BoxStyle.Initial with
        {
            TextAlign = TextAlignOf(element),
            Orphans = IntegerOf(element, "orphans", 2),
            Widows = IntegerOf(element, "widows", 2),
        };

        /// <summary>
        /// THE RULE FOR AN INLINE BOX IN HTML: an inline element with a background of alpha 1, a visible border, padding or a
        /// left or right margin has a box of its own (<see cref="InlineBoxStyle"/>), its lengths on Chromium's layout unit as a
        /// block box's; null for an element without any of these, which stays without a group, as before. Its values were
        /// checked by <see cref="BoxProperties"/>; a value refused there is not drawn.
        /// </summary>
        private InlineBoxStyle? InlineBoxOf(StyledElement element)
        {
            var box = BoxStyleOf(element);
            var inline = new InlineBoxStyle(box.Margin.Left, box.Margin.Right, box.Padding, box.Border, box.Background);
            return inline.Background is not null || inline.Start > 0 || inline.End > 0
                || box.Padding.Vertical.Points > 0 || box.Border.Top.IsVisible || box.Border.Bottom.IsVisible
                ? inline
                : null;
        }

        private BoxStyle BoxStyleOf(StyledElement element)
        {
            Length Side(string name) => Value(element, name) is CssLength length ? LayoutUnits(length.Value) : Length.Zero;
            BorderSide Border(string side) =>
                Value(element, $"border-{side}-style") is CssKeyword { Name: "solid" } && Value(element, $"border-{side}-width") is CssLength { Value.Points: > 0 } width
                && Value(element, $"border-{side}-color") is { } color && ColorOf(BoxProperties.Resolved(color, element.Style)) is { } drawn
                    ? new BorderSide(width.Value, drawn)
                    : BorderSide.None;
            static BreakBetween Between(CssValue? value) => value switch
            {
                CssKeyword { Name: "page" } => BreakBetween.Page,
                CssKeyword { Name: "avoid" or "avoid-page" } => BreakBetween.Avoid,
                _ => BreakBetween.Auto,
            };

            return Inherited(element) with
            {
                Margin = new Edges(Side("margin-top"), Side("margin-right"), Side("margin-bottom"), Side("margin-left")),
                Padding = new Edges(Side("padding-top"), Side("padding-right"), Side("padding-bottom"), Side("padding-left")),
                Border = new Border(Border("top"), Border("right"), Border("bottom"), Border("left")),
                Background = Value(element, "background-color") is { } background && BoxProperties.Alpha(background) == 1 ? ColorOf(background) : null,
                BreakBefore = Between(Value(element, "break-before")),

                // A forced break after the box is a forced break before the next one (Finish); here only avoid.
                BreakAfter = Between(Value(element, "break-after")) == BreakBetween.Avoid ? BreakBetween.Avoid : BreakBetween.Auto,
                BreakInside = Value(element, "break-inside") is CssKeyword { Name: "avoid" or "avoid-page" } ? BreakInside.Avoid : BreakInside.Auto,
            };
        }

        private TextAlign TextAlignOf(StyledElement element) => Value(element, "text-align") switch
        {
            CssKeyword { Name: "right" or "end" or "-webkit-right" } => TextAlign.Right,
            CssKeyword { Name: "center" or "-webkit-center" } => TextAlign.Center,
            CssKeyword { Name: "justify" } => TextAlign.Justify,
            _ => TextAlign.Left,
        };

        private int IntegerOf(StyledElement element, string name, int initial) => Value(element, name) is CssNumber number ? (int)number.Value : initial;

        private static Color? ColorOf(CssValue value)
        {
            static byte Channel(double channel) => (byte)Math.Round(Math.Clamp(channel, 0, 255), MidpointRounding.AwayFromZero);
            return value is CssColorValue { Color: var color } && color.Alpha == 1 ? new Color(Channel(color.Red), Channel(color.Green), Channel(color.Blue)) : null;
        }

        /// <summary>The text style of an element's text and of its lines' strut: its face, size, colour, line height and
        /// white space (<see cref="FaceStyleOf"/>), the decorations applied to it (<see cref="Decorations"/>) and whether
        /// they skip the glyphs' ink; null after a missing face was reported.</summary>
        private TextStyle? TextStyleOf(StyledElement element)
        {
            if (_decorated.TryGetValue(element, out var known))
            {
                return known;
            }

            var style = FaceStyleOf(element) is { } face ? face with { Decorations = Decorations(element), DecorationSkipInk = SkipInkOf(element) } : null;
            _decorated[element] = style;
            return style;
        }

        /// <summary>An element's face (<see cref="FontMatcher"/>), size, colour, line height and how its white space
        /// collapses and wraps, without decorations; null after a missing face was reported.</summary>
        private TextStyle? FaceStyleOf(StyledElement element)
        {
            if (_text.TryGetValue(element, out var known))
            {
                return known;
            }

            TextStyle? style = null;
            var face = Fonts.Match(element.Style, element.Element.Position);
            if (face.IsFailure)
            {
                Failures.Add(face.Error);
            }
            else
            {
                var size = element.Style[PropertyRegistry.FontSize] is CssLength length ? length.Value : Length.FromPoints(PropertyRegistry.InitialFontSizePoints);
                var color = Value(element, "color") is { } value && ColorOf(value) is { } drawn ? drawn : new Color(0, 0, 0);
                style = new TextStyle(face.Value.Names.Family, size, new FontWeight(face.Value.Metrics.WeightClass), color,
                    face.Value.Metrics.IsItalic ? CoreFontStyle.Italic : CoreFontStyle.Normal)
                {
                    LineHeight = LineHeightOf(element.Style[PropertyRegistry.LineHeight], size),
                    WhiteSpaceCollapse = CollapseOf(element.Style),
                    TextWrapMode = WrapOf(element.Style),
                };
            }

            _text[element] = style;
            return style;
        }

        /// <summary>
        /// A margin or padding in Chromium's layout unit of 1/64 CSS px: 25.313pt is laid out as 33.75 px, 25.3125pt
        /// (measured: cases <c>a6-nested-first-h253125</c> and <c>a6-nested-late-h2521875</c>, whose headers repeat only at
        /// the length so cut). Cut toward zero; the direction is not settled, as these cases give the same to the nearest
        /// unit.
        /// </summary>
        private static Length LayoutUnits(Length length)
        {
            const double unit = 0.75 / 64;
            return Length.FromPoints(Math.Truncate(length.Points / unit) * unit);
        }

        /// <summary>
        /// The line height in Chromium's layout unit of 1/64 CSS px (<see cref="FontMetricRounding.WholePixels"/>): a
        /// length rounded to the unit, half away from zero (<c>LayoutUnit::FromFloatRound</c>); a number times the font
        /// size floored to it. Each element's own value, so a number is already resolved against its own font size
        /// (CSS Inline 3 §5.1: a number inherits as a number).
        /// </summary>
        private static LineHeight LineHeightOf(CssValue value, Length fontSize)
        {
            const double unit = 0.75 / 64;
            return value switch
            {
                CssNumber number => new LineHeight.Fixed(Length.FromPoints(Math.Floor(number.Value * fontSize.Points / unit) * unit)),
                CssLength length => new LineHeight.Fixed(Length.FromPoints(Math.Round(length.Value.Points / unit, MidpointRounding.AwayFromZero) * unit)),
                _ => LineHeight.Normal.Instance,
            };
        }

        /// <summary>The author or the description by THE RULE FOR A DOCUMENT'S METADATA IN HTML (<see cref="HtmlMetadata"/>),
        /// or null; a refused one is reported at its <c>meta</c> element and left out.</summary>
        private string? Meta(StyledElement root, string name)
        {
            if (HtmlMetadata.First(root, name) is not { } found)
            {
                return null;
            }

            var (meta, content) = found;
            if (HtmlMetadata.Problem(content) is { } problem)
            {
                Refuse(HtmlStyleErrorCodes.Unsupported.Metadata, $"The {name} in <meta name> is not taken into the document's metadata: {problem}.", name, meta.Position, "content");
                return null;
            }

            return content;
        }

        /// <summary>The text of the first title element (HTML Standard §4.2.2: its child text content with ASCII white
        /// space stripped and collapsed), or null.</summary>
        private static string? Title(StyledElement root)
        {
            var pending = new Stack<StyledElement>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                var element = pending.Pop();
                if (element.Element.Namespace == ElementNamespace.Html && element.Element.LocalName == "title")
                {
                    var text = string.Concat(element.Children.OfType<StyledText>().Select(child => child.Text.Data));
                    var words = text.Split([' ', '\t', '\n', '\f', '\r'], StringSplitOptions.RemoveEmptyEntries);
                    return words.Length == 0 ? null : string.Join(' ', words);
                }

                foreach (var child in element.Children.OfType<StyledElement>().Reverse())
                {
                    pending.Push(child);
                }
            }

            return null;
        }

        private PageStyle Page()
        {
            var rules = new PageRules(_sourceName, Refusals);
            var page = rules.Apply(styled.PageRules, styled.Page);
            return WithMarginBoxes(page, rules.RowStart);
        }
    }
}
