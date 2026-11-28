using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Css.Properties;
using Ave.Extensions.Documents.Css.Values;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Fonts;
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
/// <see cref="LengthRounding.SixtyFourthPixels"/>, <see cref="LineBreakRules.Chromium"/>). The metadata: the title element's text (HTML Standard §4.2.2) and the
/// root element's language (§3.2.6.2).</para>
/// <para>The tree is walked with an explicit stack, never by recursion, so the depth of the input costs heap, not call
/// frames.</para>
/// </remarks>
public static partial class HtmlBoxGenerator
{
    /// <summary>The most characters without a glyph one document is refused for, each pair of face and character once:
    /// the rest are not reported.</summary>
    public const int MaxMissingGlyphs = 64;

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
        MarkerSnapping = MarkerSnapping.WholePixels,
        InkGapBand = InkGapBand.AtUnroundedLine,
        LineBreakRules = LineBreakRules.Chromium,
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
        var problems = (options.GenericFamilies ?? GenericFamilies.None).Problems().ToList();
        if (problems.Count > 0)
        {
            return Result<HtmlBoxes, Error>.Failure(Combined(problems));
        }

        // The caller's resolver is asked at most once per URL in this generation and the style resolution before it.
        var run = new Run(styled, options with { Resources = ResourceTable.Once(options.Resources, styled) });
        var document = run.Generate();
        if (run.Failures.Count > 0)
        {
            return Result<HtmlBoxes, Error>.Failure(Combined([.. run.Failures, .. options.Unsupported == UnsupportedPolicy.Refuse ? run.Refusals : []]));
        }

        if (options.Unsupported == UnsupportedPolicy.Refuse && run.Refusals.Count > 0)
        {
            return Result<HtmlBoxes, Error>.Failure(Combined(run.Refusals));
        }

        // One bound for the diagnostics of the parse, the style resolution and the box generation together.
        return Result<HtmlBoxes, Error>.Success(new HtmlBoxes(document, run.Fonts.Used,
            [.. DiagnosticLimits.Kept([.. styled.Diagnostics, .. run.Refusals], error => HtmlStyleDiagnostics.ClassOf(error.Code) == Css.DiagnosticClass.Refusal)]));
    }

    private static Error Combined(IReadOnlyList<Error> errors) => DiagnosticLimits.Aggregate(errors);

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

        /// <summary>A block, a list element's list or a list item.</summary>
        public FrameKind Kind { get; init; }

        /// <summary>An item's label, the label's run or symbol, its body, and the body's box of lines.</summary>
        public (NodeId Label, NodeId Mark, NodeId Body, NodeId Lines)? ItemIds { get; init; }

        /// <summary>The list box open around a run of items outside a list element.</summary>
        public (NodeId Id, ListNumbering Numbering, List<Node> Items, SourceLocation Source)? Implied { get; set; }

        /// <summary>The lists around the box, its own included.</summary>
        public int Lists { get; init; }

        /// <summary>For the frame of an inline-block: the inline content its box goes into, as an atomic inline among the runs
        /// (THE RULE FOR AN INLINE-BLOCK IN HTML), and the element whose content that is; null for a block-level box.</summary>
        public (List<Node> Target, StyledElement Parent)? Atomic { get; set; }
    }

    private enum FrameKind
    {
        Block,
        List,
        Item,
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

        /// <summary>The link's own box (THE RULE FOR AN INLINE BOX IN HTML), or null.</summary>
        public InlineBoxStyle? Box { get; init; }
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
        private readonly Dictionary<StyledElement, TrueTypeFont> _faces = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<(TrueTypeFont Face, int CodePoint)> _missingGlyphs = [];
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

        /// <summary>The display of a rendered element: block, inline, inline-block or none; a refused type counts as block.</summary>
        private static string Display(StyledElement element) => element.Style[PropertyRegistry.Display] switch
        {
            CssKeyword { Name: "inline" } => "inline",
            CssKeyword { Name: "inline-block" } => "inline-block",
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
        private string Characters(HtmlText text, string? sourceName = null)
        {
            var data = text.Data;
            var first = -1;
            for (var i = 0; i < data.Length && first < 0; i += UnitsAt(data, i))
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
                var length = UnitsAt(data, i);
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

            var codePoint = UnitsAt(data, first) == 2 ? char.ConvertToUtf32(data[first], data[first + 1]) : data[first];
            var others = count == 1 ? string.Empty : $" ({count.ToString(CultureInfo.InvariantCulture)} such characters in this text)";
            var error = new Error(HtmlStyleErrorCodes.Unsupported.Character, $"The character U+{codePoint:X4} is not drawn in this version{others}: only text, tab, line feed and carriage return are.")
                .With(ErrorMetadataKeys.Value, $"U+{codePoint:X4}").With(ErrorMetadataKeys.Offset, first).With(ErrorMetadataKeys.Count, count);
            Refusals.Add(error.WithSource(sourceName ?? _sourceName, text.Position));
            return kept.ToString();
        }

        /// <summary>The code units of the character at an index: a surrogate pair is one character; a lone surrogate is
        /// one code unit.</summary>
        private static int UnitsAt(string data, int i) => char.IsHighSurrogate(data[i]) && i + 1 < data.Length && char.IsLowSurrogate(data[i + 1]) ? 2 : 1;

        /// <summary>Whether the character at an index is one <see cref="Characters"/> keeps.</summary>
        private static bool IsDrawn(string data, int i)
        {
            var unit = data[i];
            if (UnitsAt(data, i) == 2)
            {
                return (char.ConvertToUtf32(unit, data[i + 1]) & 0xFFFE) != 0xFFFE;
            }

            return !char.IsSurrogate(unit)
                && (unit is '\t' or '\n' or '\r' || !(char.IsControl(unit) || unit is '\u2028' or '\u2029' || unit is >= '\uFDD0' and <= '\uFDEF' || (unit & 0xFFFE) == 0xFFFE));
        }

        /// <summary>
        /// Refuses, in both modes, each character of a text that the face of its element has no glyph for: the engine
        /// takes no glyph from another font and draws no box for a missing one. Each pair of face and character is
        /// refused once, at its first place in the order the text is met (the source order of the document's text), up
        /// to <see cref="MaxMissingGlyphs"/> in a document. What is never missing: the characters that need no glyph
        /// (<c>Shaper.NeedsNoGlyph</c>), the white space that collapses under the text's <c>white-space-collapse</c> (it is
        /// drawn as a space or not at all), a preserved tab (refused on its own and drawn as a space,
        /// <see cref="RefusePreservedTab"/>), and the characters <see cref="Characters"/> refuses on their own. The rest is
        /// the shaper's own rule (<see cref="HtmlBoxOptions.MissingCharacters"/>), so a text this lets through is shaped.
        /// Without that option nothing is checked here.
        /// </summary>
        /// <param name="data">The text as written (before white space collapses).</param>
        /// <param name="element">The element whose face draws it.</param>
        /// <param name="place">The place of an offset in <paramref name="data"/>: the character's own place for text from
        /// the document, the declaration for generated text.</param>
        private void RefuseMissingGlyphs(string data, StyledElement element, Func<int, TextPosition> place) => RefuseMissingGlyphs(data, data, "none", element, place);

        /// <summary>
        /// Refuses each character of a text as it is drawn that the face has no glyph for (<see
        /// cref="RefuseMissingGlyphs(string, StyledElement, Func{int, TextPosition})"/>): after its <c>text-transform</c>, which
        /// keeps every offset where it is, so a character the transform makes (a capital, a small letter) is checked and
        /// refused at the place of the character it is made of, with both named.
        /// </summary>
        /// <param name="data">The text as written with its transform applied (<see cref="Drawn"/>).</param>
        /// <param name="written">The text as written, as long as <paramref name="data"/>.</param>
        /// <param name="transform">The text's computed <c>text-transform</c>.</param>
        /// <param name="element">The element whose face draws it.</param>
        /// <param name="place">The place of an offset: the character's own place for text from the document, the declaration
        /// for generated text.</param>
        private void RefuseMissingGlyphs(string data, string written, string transform, StyledElement element, Func<int, TextPosition> place)
        {
            if (options.MissingCharacters is not { } missingCharacters || _missingGlyphs.Count >= MaxMissingGlyphs || !_faces.TryGetValue(element, out var face))
            {
                return;
            }

            var mode = CollapseOf(element.Style);
            var checkedText = new StringBuilder(data.Length);
            for (var i = 0; i < data.Length;)
            {
                var length = UnitsAt(data, i);
                if (!IsDrawn(data, i) || CollapsesUnder(data[i], mode) || data[i] == '\t')
                {
                    // A character that needs no glyph keeps every offset where it is.
                    checkedText.Append('\u200B', length);
                }
                else
                {
                    checkedText.Append(data, i, length);
                }

                i += length;
            }

            foreach (var (offset, character) in missingCharacters(checkedText.ToString(), face))
            {
                if (_missingGlyphs.Count >= MaxMissingGlyphs)
                {
                    return;
                }

                // The font raises the error; a character the face has after all (a caller's rule) is not refused.
                if (face.RequireGlyph(character) is { IsFailure: true } raised && _missingGlyphs.Add((face, character.Value)))
                {
                    var later = Fonts.Describe(element.Style, face, other => missingCharacters(character.ToString(), other).IsEmpty);
                    var madeOf = Rune.TryGetRuneAt(written, offset, out var source) && source != character ? (transform, source) : ((string, Rune)?)null;
                    Failures.Add(MissingGlyph(raised.Error, face, character, later, madeOf).WithSource(_sourceName, place(offset)));
                }
            }
        }

        /// <summary>The refusal of a character a face has no glyph for: the font's own error (its code, face, character
        /// and code point, <see cref="TrueTypeFont.RequireGlyph"/>), with a message that names the character, its code
        /// point, the face, the family list in effect and what to do; and the later family of that list that has it, when
        /// one does.</summary>
        private static Error MissingGlyph(Error raised, TrueTypeFont face, Rune character, (string Families, string? Later) list, (string Transform, Rune Source)? madeOf = null)
        {
            var (families, later) = list;
            var codePoint = $"U+{character.Value:X4}";
            var made = madeOf is { } of ? $", which 'text-transform: {of.Transform}' makes of '{of.Source}' (U+{of.Source.Value:X4}) written here," : string.Empty;
            var hint = later is null
                ? string.Empty
                : $" The family '{later}' of the list has it: set '{later}' on an element around the character.";
            return new Error(raised.Code,
                    $"The character '{character}' ({codePoint}){made} has no glyph in the face '{face.Names.PostScriptName}' that {families} gives here, and the engine takes no glyph from another font: put the character in an element whose font has it, or use an image.{hint}",
                    raised.Metadata)
                .With(ErrorMetadataKeys.Value, codePoint);
        }

        /// <summary>
        /// Checks a rendered element once, the first time it is met: its own rule, then every computed value. True when
        /// it is drawn; false when it is left out (its own rule refuses it).
        /// </summary>
        private bool Check(StyledElement element, bool isBlock)
        {
            ReportCounterRefusals(element);
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
            Number(root);
            Count(root);
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
                        var (box, forcedAfter) = Finish(block, block.Atomic?.Parent ?? (stack.Count > 0 ? ((BlockFrame)stack.Peek()).Element : null));
                        if (block.Atomic is { } atomic)
                        {
                            // Layout refuses an inline-block at its source (one taller than a page), so it carries its column.
                            atomic.Target.Add(box with { Source = box.Source with { Column = block.Element.Element.Position.Column } });
                        }
                        else if (stack.Count == 0)
                        {
                            // A forced break after the last box of the document has no page to start: Chromium prints no
                            // empty page after it (measured, cases h-break-after-last, h-break-after-last-nested).
                            result = box;
                        }
                        else
                        {
                            var parent = (BlockFrame)stack.Peek();
                            (block.Kind == FrameKind.Item && parent.Implied is { } implied ? implied.Items : parent.Nodes).Add(LanguageOf(block.Element, parent.Element) is { } language ? box with { Language = language } : box);
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

        private BlockFrame Frame(StyledElement element, NodeId id, int depth, FrameKind kind = FrameKind.Block, int lists = 0, (NodeId, NodeId, NodeId, NodeId)? itemIds = null)
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

            return new BlockFrame(element, id, depth, kinds, dropped) { Kind = kind, Lists = lists, ItemIds = itemIds };
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
            if (block.Dropped[index] || (node is StyledElement hidden && Display(hidden) == "none"))
            {
                RefuseBreakBetweenBlocks(block.Dropped[index] ? node : null);
                return;
            }

            var bareItem = block.Kind != FrameKind.List && node is StyledElement candidate && IsListItem(candidate);
            if (!bareItem)
            {
                CloseImplied(block);
            }

            if (node is StyledElement table && PartOf(table) == TablePart.Table && stack.OfType<BlockFrame>().Any(open => open.Atomic is not null))
            {
                // THE RULE FOR AN INLINE-BLOCK IN HTML: a table inside one is not measured.
                Refuse(HtmlStyleErrorCodes.Unsupported.InlineBox, $"A <{table.Element.LocalName}> inside an inline-block is not drawn in this version; put the table outside the inline-block, or make the inline-block a block.",
                    table.Element.LocalName, table.Element.Position, "display");
                return;
            }

            if (RefusedInList(block, node) || TableChild(stack, block, node))
            {
                RefuseBreakBetweenBlocks(null);
                return;
            }

            if (block.IsBlockChild[index])
            {
                var element = (StyledElement)node;
                CloseAnonymous(block);
                if (IsListItem(element))
                {
                    ListItem(stack, block, element, bareItem);
                    return;
                }

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

                ReportListRefusals(element);
                var isList = IsListElement(element);
                if (isList && ListTooLarge(element, block.Lists + 1))
                {
                    return;
                }

                var frame = isList ? Frame(element, NewId(), block.Depth + 1, FrameKind.List, block.Lists + 1) : Frame(element, NewId(), block.Depth + 1, lists: block.Lists);
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

        /// <summary>A list item: in its list, or in the list box opened around it (outside a list element); its frame holds
        /// the ids of its label and body, so they follow the item's in pre-order.</summary>
        private void ListItem(Stack<object> stack, BlockFrame block, StyledElement element, bool bare)
        {
            if (!Check(element, isBlock: true))
            {
                return;
            }

            ReportListRefusals(element);
            if (bare && (ListTooLarge(element, block.Lists + 1, (block.Implied?.Items.Count ?? 0) + 1) || !OpenImplied(block, element)))
            {
                return;
            }

            // The item, its body and the body's box of lines lie between the list and the item's content.
            var depth = block.Depth + (bare ? 2 : 1);
            if (Deeper(depth + 2, element.Element.Position))
            {
                return;
            }

            var id = NewId();
            var frame = Frame(element, id, depth + 2, FrameKind.Item, block.Lists + (bare ? 1 : 0), (NewId(), NewId(), NewId(), NewId()));
            frame.ForcedBreakBefore = block.ForcedBreakPending;
            block.ForcedBreakPending = false;
            stack.Push(frame);
        }

        /// <summary>
        /// Refuses a letter-spaced text (<c>letter-spacing</c> other than zero) that holds a character whose spacing is not
        /// measured: a combining mark (Unicode general category M, which Chromium spaces with its base, as one typographic
        /// character unit) or a format character (category Cf, such as the soft hyphen and the zero width joiners, which may
        /// get none), at the first such character with its offset. The text is drawn as written under
        /// <see cref="UnsupportedPolicy.Warn"/>, spaced after each glyph.
        /// </summary>
        private void RefuseUnspacedCharacter(StyledText text)
        {
            if (SpacingOf(text.Style[PropertyRegistry.Find("letter-spacing")!]) == Length.Zero)
            {
                return;
            }

            var data = text.Text.Data;
            for (var offset = 0; offset < data.Length; offset++)
            {
                if (char.GetUnicodeCategory(data[offset]) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark
                    or UnicodeCategory.EnclosingMark or UnicodeCategory.Format)
                {
                    Refusals.Add(new Error(HtmlStyleErrorCodes.Unsupported.Value, $"A mark or a format character (U+{(int)data[offset]:X4}) in letter-spaced text is not drawn in this version: its letter spacing is not measured.")
                        .With(ErrorMetadataKeys.Value, SpacingText(SpacingOf(text.Style[PropertyRegistry.Find("letter-spacing")!]))).With(ErrorMetadataKeys.Property, "letter-spacing").With(ErrorMetadataKeys.Offset, offset)
                        .WithSource(_sourceName, text.Text.Position));
                    return;
                }
            }
        }

        /// <summary>The computed <c>text-transform</c> of a text: <c>none</c>, <c>uppercase</c>, <c>lowercase</c> or
        /// <c>capitalize</c>.</summary>
        private static string TransformOf(StyledText text) =>
            text.Style[PropertyRegistry.Find("text-transform")!] is CssKeyword { Name: var name } ? name : "none";

        /// <summary>The text as written with its transform applied, for the glyph check (<see cref="RefuseMissingGlyphs(string, string, string, StyledElement, Func{int, TextPosition})"/>):
        /// the same length, so each offset is the source's; the text as written where the transform does not map it (that
        /// text is refused and drawn as written, <see cref="Transformed"/>).</summary>
        private static string Drawn(string written, string transform, InlineFrame frame, char previous) =>
            transform == "none" ? written : TextTransforms.Apply(written, transform, frame.Element.Language, previous) is { Reason: null } result ? result.Text : written;

        /// <summary>
        /// A text with its <c>text-transform</c> applied (<see cref="TextTransforms"/>), in the language of the element it
        /// lies in. Capitalize looks at the character before the text (<see cref="PreviousCharacter"/>). A text with a
        /// character the transform does not map is refused there, with its offset, and drawn as written.
        /// </summary>
        private string Transformed(StyledText text, string transform, InlineFrame frame, char previous, string data)
        {
            if (transform == "none")
            {
                return data;
            }

            var result = TextTransforms.Apply(data, transform, frame.Element.Language, previous);
            if (result.Reason is null)
            {
                return result.Text;
            }

            Refusals.Add(new Error(HtmlStyleErrorCodes.Unsupported.Value, $"'text-transform: {transform}' is not drawn for this text: {result.Reason}.")
                .With(ErrorMetadataKeys.Value, transform).With(ErrorMetadataKeys.Property, "text-transform").With(ErrorMetadataKeys.Offset, result.Offset)
                .WithSource(_sourceName, text.Text.Position));
            return data;
        }

        /// <summary>
        /// The character before a text in its inline content, for capitalize: the last character of the text before it, past
        /// the edges of every inline element, a group's too (a link, an element with a language or with a box of its own,
        /// a raised element), whether the text lies inside the element or after it, and past elements without text; generated
        /// text counts as the text it is. A space after an image or at the start of the inline content. Chromium looks back
        /// so, to the previous text in the tree, and takes a space at anything else: case <c>tt-inline</c> for plain spans,
        /// case <c>tt-groups</c> for a link, a span with a language, with a background, with padding, a border or a margin,
        /// a <c>sup</c> and generated text, each starting and ending inside a word (<c>wo&lt;a href&gt;rd&lt;/a&gt;</c> prints
        /// "Word"; an edge that takes room starts no word either: "Wo rd").
        /// </summary>
        /// <param name="stack">The open frames: the groups being built around the text lie on it, innermost first.</param>
        /// <param name="frame">The frame the text goes into.</param>
        private static char PreviousCharacter(Stack<object> stack, InlineFrame frame)
        {
            // The contents to look back through, innermost first: the text's own, then for each group being built around it
            // the content the group goes into. Closed groups in them are read from their end, with an explicit stack.
            var contents = new List<IReadOnlyList<Node>> { frame.Target };
            foreach (var open in stack)
            {
                if (open is not InlineFrame inline)
                {
                    break;
                }

                if ((inline.Link?.Parent ?? inline.Span?.Parent) is { } parent)
                {
                    contents.Add(parent);
                }
            }

            var pending = new Stack<(IReadOnlyList<Node> Nodes, int Next)>();
            foreach (var content in contents)
            {
                pending.Push((content, content.Count - 1));
                while (pending.Count > 0)
                {
                    var (nodes, next) = pending.Pop();
                    if (next < 0)
                    {
                        continue;
                    }

                    pending.Push((nodes, next - 1));
                    switch (nodes[next])
                    {
                        case TextRun { Text.Length: 0 }:
                            break;
                        case TextRun { Text: var before }:
                            return before[^1];
                        case InlineGroup group:
                            pending.Push((group.Children, group.Children.Count - 1));
                            break;
                        default:
                            return ' ';
                    }
                }
            }

            return ' ';
        }

        /// <summary>
        /// Refuses generated text inside an inline element with a box of its own (<see cref="InlineBoxStyle"/>), at its
        /// <c>content</c> declaration, and draws it there under <see cref="UnsupportedPolicy.Warn"/>: Chromium 159 draws that
        /// text from the line's pixels and the element's own text from the box's (case <c>mx-link-gen</c>: under a padding of
        /// 1pt the <c>::before</c> text "from" on the baseline 78.75 pt, the element's "inside" 0.246 pt below it), where the
        /// engine draws both from the box's (THE RULE FOR AN INLINE BOX ON WHOLE PIXELS).
        /// </summary>
        private void RefuseGeneratedTextInBox(StyledText text, InlineFrame frame)
        {
            if (frame.Element.Generated is not { } declared || !frame.InBox || text.Text.Data.All(IsCollapsible))
            {
                return;
            }

            Refuse(HtmlStyleErrorCodes.Unsupported.Value,
                "Generated text inside an element with a background, border, padding or margin of its own is not drawn in this version: Chromium draws it on the pixels of the line and the element's own text on those of the box. Generate the text outside that element, or give the box to an element inside it.",
                InputQuote.Of(text.Text.Data), declared.Position, "content");
        }

        /// <summary>The texts of a pseudo-element's content, one per item, or null when the counting refused it.</summary>
        private string[]? Pieces(StyledElement element, Css.Cascade.GeneratedContent generated) =>
            ReadsCounters(generated)
                ? _generatedTexts.GetValueOrDefault(element)
                : [.. generated.Items.Select(item => item is CssContentString literal ? literal.Value : string.Empty)];

        /// <summary>
        /// A text's runs: one, or for generated content one per item of its <c>content</c> that has text, as an element's
        /// text nodes are runs. Chromium 159 rounds the width of each item up to a 64th of a pixel on its own, as it does
        /// each text node (measured: cases <c>gc-counter-styles</c> and <c>gc-items-kern</c>; its shaping across the items
        /// is the engine's shaping across runs). A content without any text keeps one empty run, which counts for the
        /// line's height; a text whose characters were refused stays one run.
        /// </summary>
        private static IEnumerable<string> Split(string data, string[]? pieces)
        {
            if (pieces is null || pieces.Sum(piece => piece.Length) != data.Length || pieces.Count(piece => piece.Length > 0) < 2)
            {
                yield return data;
                yield break;
            }

            var offset = 0;
            foreach (var piece in pieces)
            {
                if (piece.Length > 0)
                {
                    yield return data.Substring(offset, piece.Length);
                    offset += piece.Length;
                }
            }
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

                    // The text of a ::before or ::after that reads counters is what the counting found; none when it
                    // was refused there.
                    var pieces = frame.Element.Generated is { } content ? Pieces(frame.Element, content) : null;
                    if (frame.Element.Generated is not null && pieces is null)
                    {
                        break;
                    }

                    var counted = pieces is not null ? new StyledText(new HtmlText(string.Concat(pieces), text.Text.Position), text.Style) : text;
                    var sourceName = frame.Element.Generated?.SourceName ?? _sourceName;
                    var preservedTab = RefusePreservedTab(counted, sourceName);
                    RefuseUnspacedCharacter(counted);
                    RefuseGeneratedTextInBox(counted, frame);
                    if (TextStyleOf(frame.Element) is { } style)
                    {
                        // The glyphs are checked in the text as it is drawn, after its transform, at the source's places: for
                        // generated text each item of its content and the text of each counter, at the declaration.
                        var transform = TransformOf(counted);
                        var previous = transform == "none" ? ' ' : PreviousCharacter(stack, frame);
                        RefuseMissingGlyphs(Drawn(counted.Text.Data, transform, frame, previous), counted.Text.Data, transform, frame.Element,
                            frame.Element.Generated is { } declared ? _ => declared.Position : counted.Text.PositionOf);
                        var data = Characters(counted.Text, sourceName);
                        data = preservedTab ? data.Replace('\t', ' ') : data;
                        var source = frame.Element.Generated is { } generated ? new SourceLocation(generated.SourceName, generated.Position.Line) : Source(text.Text.Position);
                        // Each item is transformed on its own, so a transform that changes the length (ß to SS) keeps the
                        // items apart; capitalize looks back into the item before it.
                        foreach (var piece in Split(data, pieces))
                        {
                            var drawn = Transformed(counted, transform, frame, previous, piece);
                            previous = drawn.Length > 0 ? drawn[^1] : previous;
                            frame.Target.Add(new TextRun(NewId(), drawn, style, source) { Generated = frame.Element.Generated is not null });
                        }
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
                case StyledElement element when Display(element) == "inline-block":
                    InlineBlock(stack, frame, element);
                    break;
                case StyledElement element when Display(element) == "inline":
                    if (Check(element, isBlock: false) && !Deeper(frame.Depth, element.Element.Position))
                    {
                        // The shift is opened first, so the text of a link or a span group is shifted as its element is.
                        OpenShift(element, frame.Element);
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

        private (Box Box, bool ForcedAfter) Finish(BlockFrame block, StyledElement? parent)
        {
            CloseAnonymous(block);
            CloseImplied(block);
            var element = block.Element;
            var (layout, children) = block.HasBlockChildren ? (LayoutAlgorithmId.Flow, (ValueList<Node>)[.. block.Nodes]) : Content(block.Nodes);
            var style = BoxStyleOf(element) with { Text = layout == LayoutAlgorithmId.Inline && block.Kind == FrameKind.Block ? TextStyleOf(element) : null };
            if (HtmlBoxGenerator.PartOf(element.Style[PropertyRegistry.Display]) == TablePart.None)
            {
                style = Sized(element, style, parent);
            }

            if (block.ForcedBreakBefore)
            {
                style = style with { BreakBefore = BreakBetween.Page };
            }

            // CSS Fragmentation 3 §3.1: a forced break after the last child is a break after this box.
            var forcedAfter = block.ForcedBreakPending || Value(element, "break-after") is CssKeyword { Name: "page" };
            var box = block.Kind switch
            {
                FrameKind.Item => Item(block, layout, children, style),
                FrameKind.List => ListBox(block, children, style),
                _ => TablePartBox(block, new Box(block.Id, RoleOf(element, children), layout, style, children, Source(element.Element.Position))),
            };
            return (box, forcedAfter);
        }

        /// <summary>The role of an element's box. A block <c>::before</c> or <c>::after</c> is a generic box, as the
        /// anonymous box around its element's own text is, and its text is the element's; one without text, drawn only
        /// for its border or background (<c>content: ""</c>, a rule under a heading), is decoration: an artifact.</summary>
        private SemanticRole RoleOf(StyledElement element, ValueList<Node> children)
        {
            var html = element.Element;
            if (ReferenceEquals(element, styled.Root))
            {
                return SemanticRole.Document;
            }

            if (element.Generated is not null)
            {
                return children.IsEmpty ? SemanticRole.Decorative : SemanticRole.Generic;
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
            var inline = new InlineBoxStyle(box.Margin.Left, box.Margin.Right, box.Padding, box.Border, box.Background) { BorderRadius = box.BorderRadius };
            return inline.Background is not null || inline.Start > 0 || inline.End > 0
                || box.Padding.Vertical.Points > 0 || box.Border.Top.IsVisible || box.Border.Bottom.IsVisible
                ? inline
                : null;
        }

        private BoxStyle BoxStyleOf(StyledElement element)
        {
            Length Side(string name) => Value(element, name) is CssLength length ? LayoutUnits(length.Value) : Length.Zero;
            // An inset or outset side whose colour is currentcolor (the initial value) takes its two shades from #eeeeee,
            // whatever the colour is (Chromium 159: black and gray both print #9a9a9a and #eeeeee, an explicit black
            // #545454 and #a8a8a8; cases bs-widths, hr-default).
            BorderSide Border(string side) =>
                Value(element, $"border-{side}-style") is CssKeyword { Name: "solid" or "inset" or "outset" or "dashed" } keyword && Value(element, $"border-{side}-width") is CssLength { Value.Points: > 0 } width
                && Value(element, $"border-{side}-color") is { } color && ColorOf(BoxProperties.Resolved(color, element.Style)) is { } drawn
                    ? keyword.Name switch
                    {
                        "solid" => new BorderSide(width.Value, drawn),
                        "dashed" => new BorderSide(width.Value, drawn, BorderStyle.Dashed),
                        var shaded => new BorderSide(width.Value, color is CssKeyword { Name: "currentcolor" } ? new Color(0xee, 0xee, 0xee) : drawn, shaded == "inset" ? BorderStyle.Inset : BorderStyle.Outset),
                    }
                    : BorderSide.None;
            // A corner radius as computed: a length or a percentage of the border box, resolved by layout (RoundedBox). One
            // refused corner carries the refusal of all four (BoxProperties), so then the box keeps square corners. A table
            // in the collapsed border model keeps them too: Chromium 159 ignores its radius (case bri-collapse).
            var squared = new[] { "top-left", "top-right", "bottom-right", "bottom-left" }.Any(corner => Value(element, $"border-{corner}-radius") is null)
                || (Value(element, "display") is CssKeyword { Name: "table" } && Value(element, "border-collapse") is CssKeyword { Name: "collapse" });
            CornerRadius Radius(string corner) => squared ? CornerRadius.Zero : Value(element, $"border-{corner}-radius") switch
            {
                CssLength length => new CornerRadius(length.Value),
                CssPercentage percentage => CornerRadius.OfPercent(percentage.Value),
                _ => CornerRadius.Zero,
            };
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
                BorderRadius = new BorderRadius(Radius("top-left"), Radius("top-right"), Radius("bottom-right"), Radius("bottom-left")),
                BreakBefore = Between(Value(element, "break-before")),

                // A forced break after the box is a forced break before the next one (Finish); here only avoid.
                BreakAfter = Between(Value(element, "break-after")) == BreakBetween.Avoid ? BreakBetween.Avoid : BreakBetween.Auto,
                BreakInside = Value(element, "break-inside") is CssKeyword { Name: "avoid" or "avoid-page" } ? BreakInside.Avoid : BreakInside.Auto,
            };
        }

        /// <summary>
        /// The width, minimum and maximum width, sizing box and auto horizontal margins of a block box that is not a table
        /// part (CSS Sizing 3 §3.1–3.2, CSS Box Model 3 §4.3, CSS 2.1 §10.3.3). A length is put on Chromium's layout unit of
        /// 1/64 px, cut toward zero, as a margin is (<see cref="LayoutUnits"/>; measured: 200pt is 266.65625 px, 199.992 pt,
        /// case <c>sz-width-percent-parent</c>; 100.3pt is 133.71875 px, 100.289 pt, case <c>sz-width-fraction</c>); a
        /// percentage is resolved by layout. A <c>min-width</c> of 0 is its initial value for a block box. The auto margins
        /// are set only beside a width: at width <c>auto</c> they are 0, which <see cref="BoxStyle.Margin"/> holds already.
        /// A height, a minimum and a maximum height of a length are put on the layout unit as well (60pt is 79.9921875 px,
        /// 59.994 pt; case <c>sz-height</c>, 10pt is 9.996 pt); a percentage height is resolved here
        /// (<see cref="PercentageHeight"/>). A <c>min-height</c> of 0 is its initial value.
        /// </summary>
        private BoxStyle Sized(StyledElement element, BoxStyle style, StyledElement? parent)
        {
            SizeValue? Size(string name) => Value(element, name) switch
            {
                CssLength length => new SizeValue.Fixed(LayoutUnits(length.Value)),
                CssPercentage percentage => new SizeValue.Percentage(percentage.Value),
                _ => null,
            };

            Length? Height(string name) => Value(element, name) is CssLength { Value.Points: > 0 } length ? LayoutUnits(length.Value) : null;

            var min = Value(element, "min-width") is CssLength { Value.Points: 0 } or CssPercentage { Value: 0 } ? null : Size("min-width");
            var sized = style with { SpecifiedWidth = Size("width"), MinWidth = min, MaxWidth = Size("max-width") };
            var hasWidth = sized.SpecifiedWidth is not null || sized.MinWidth is not null || sized.MaxWidth is not null;
            var height = Value(element, "height") switch
            {
                CssLength length => LayoutUnits(length.Value),
                CssPercentage percentage => PercentageHeight(element, percentage, parent),
                _ => null,
            };
            var maxHeight = Value(element, "max-height") is CssLength maximum ? LayoutUnits(maximum.Value) : (Length?)null;
            sized = sized with { SpecifiedHeight = height, MinHeight = Height("min-height"), MaxHeight = maxHeight };
            if (!hasWidth && sized.SpecifiedHeight is null && sized.MinHeight is null && sized.MaxHeight is null)
            {
                return style;
            }

            var (left, right) = hasWidth
                ? (Value(element, "margin-left") is CssKeyword { Name: "auto" }, Value(element, "margin-right") is CssKeyword { Name: "auto" })
                : (false, false);
            return sized with
            {
                BoxSizing = Value(element, "box-sizing") is CssKeyword { Name: "border-box" } ? BoxSizing.BorderBox : BoxSizing.ContentBox,
                AutoMargins = (left, right) switch
                {
                    (true, true) => AutoMargins.Both,
                    (true, false) => AutoMargins.Left,
                    (false, true) => AutoMargins.Right,
                    _ => AutoMargins.None,
                },
            };
        }

        /// <summary>
        /// A percentage height (CSS 2.1 §10.5): of the height of the parent's content box where the parent's height is a
        /// length in a content box, put on the layout unit, cut toward zero as Chromium floors it (case
        /// <c>sz-height-percent</c>: 50% of 99.996 pt is 49.992 pt, 33.3% is 33.293 pt); <c>auto</c> where the parent's
        /// height depends on its content (height <c>auto</c>, no minimum or maximum height), as §10.5 says and Chromium
        /// prints it (the same case). Refused elsewhere: on the root, against a parent's percentage, border-box height,
        /// minimum or maximum height, which no case measures.
        /// </summary>
        private Length? PercentageHeight(StyledElement element, CssPercentage percentage, StyledElement? parent)
        {
            if (parent is not null && Value(parent, "height") is CssLength height && Value(parent, "box-sizing") is not CssKeyword { Name: "border-box" })
            {
                return LayoutUnits(LayoutUnits(height.Value) * (percentage.Value / 100));
            }

            if (parent is not null && Value(parent, "height") is CssKeyword { Name: "auto" }
                && Value(parent, "min-height") is CssKeyword { Name: "auto" } or CssLength { Value.Points: 0 } && Value(parent, "max-height") is CssKeyword { Name: "none" })
            {
                return null;
            }

            Refuse(HtmlStyleErrorCodes.Unsupported.Value, $"'height: {percentage}' on <{element.Element.LocalName}> is not drawn: a percentage height is drawn of a parent whose height is a length in a content box, or of one whose height is auto, in this version.",
                percentage.ToString()!, element.Element.Position, "height");
            return null;
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
        /// white space (<see cref="FaceStyleOf"/>), the decorations applied to it (<see cref="Decorations"/>), whether
        /// they skip the glyphs' ink, and for an inline element its baseline shift (<see cref="ShiftOf"/>, opened before);
        /// null after a missing face was reported.</summary>
        private TextStyle? TextStyleOf(StyledElement element)
        {
            if (_decorated.TryGetValue(element, out var known))
            {
                return known;
            }

            var style = FaceStyleOf(element) is { } face ? face with { Decorations = Decorations(element), DecorationSkipInk = SkipInkOf(element), BaselineShift = ShiftOf(element) } : null;
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
            var face = Fonts.Match(element.Style, element.Element.Position, element.Generated?.SourceName);
            if (face.IsFailure)
            {
                Failures.Add(face.Error);
            }
            else
            {
                _faces[element] = face.Value;
                var size = element.Style[PropertyRegistry.FontSize] is CssLength length ? length.Value : Length.FromPoints(PropertyRegistry.InitialFontSizePoints);
                var color = Value(element, "color") is { } value && ColorOf(value) is { } drawn ? drawn : new Color(0, 0, 0);
                style = new TextStyle(face.Value.Names.Family, size, new FontWeight(face.Value.Metrics.WeightClass), color,
                    face.Value.Metrics.IsItalic ? CoreFontStyle.Italic : CoreFontStyle.Normal)
                {
                    LineHeight = LineHeightOf(element.Style[PropertyRegistry.LineHeight], size),
                    WhiteSpaceCollapse = CollapseOf(element.Style),
                    TextWrapMode = WrapOf(element.Style),
                    LetterSpacing = SpacingOf(Value(element, "letter-spacing")),
                    WordSpacing = SpacingOf(Value(element, "word-spacing")),
                    FontFeatures = FeaturesOf(Value(element, "font-variant-numeric"), Value(element, "font-feature-settings")),
                };
            }

            _text[element] = style;
            return style;
        }

        /// <summary>A computed letter-spacing or word-spacing as the model's length: zero for normal and 0%. Not put on
        /// the layout unit: Chromium adds the length as it is (case <c>ls-values</c>: 0.37pt after each of 46 characters
        /// moves the last word 17.02pt).</summary>
        private static Length SpacingOf(CssValue? value) => value is CssLength length ? length.Value : Length.Zero;

        /// <summary>
        /// The OpenType features a text switches on (CSS Fonts 4 §6.7, §7.1): each keyword of font-variant-numeric as its
        /// tag (<c>lining-nums</c> lnum, <c>oldstyle-nums</c> onum, <c>proportional-nums</c> pnum, <c>tabular-nums</c> tnum,
        /// <c>ordinal</c> ordn), then font-feature-settings over them, a tag at 1 on and at 0 off, the last of a tag
        /// winning (CSS Fonts 4 §7.2, the order of feature precedence: font-feature-settings after the font variant
        /// properties; case <c>fn-numeric</c>: <c>"tnum" on, "onum"</c> prints oldstyle figures). In the order of
        /// <see cref="OptionalFontFeatures.Tags"/>.
        /// </summary>
        private static ValueList<string> FeaturesOf(CssValue? numeric, CssValue? settings)
        {
            var on = new HashSet<string>(StringComparer.Ordinal);
            IEnumerable<CssValue> Items(CssValue? value) => value switch
            {
                CssList list => list.Items,
                CssKeyword { Name: "normal" } or null => [],
                _ => [value],
            };

            foreach (var item in Items(numeric))
            {
                if (item is CssKeyword { Name: var name } && name switch
                {
                    "lining-nums" => "lnum",
                    "oldstyle-nums" => "onum",
                    "proportional-nums" => "pnum",
                    "tabular-nums" => "tnum",
                    "ordinal" => "ordn",
                    _ => null,
                } is { } tag)
                {
                    on.Add(tag);
                }
            }

            foreach (var item in Items(settings))
            {
                if (item is CssList { Items: [CssString { Value: var tag }, CssNumber { Value: var value }] })
                {
                    if (value == 0)
                    {
                        on.Remove(tag);
                    }
                    else
                    {
                        on.Add(tag);
                    }
                }
            }

            return [.. OptionalFontFeatures.Tags.Where(on.Contains)];
        }

        private static string SpacingText(Length spacing) => spacing.Points.ToString("0.###", CultureInfo.InvariantCulture) + "pt";

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
            if (MetadataTexts.Problem(content) is { } problem)
            {
                Refuse(HtmlStyleErrorCodes.Unsupported.Metadata, $"The {name} in <meta name> is not taken into the document's metadata: {problem}.", name, meta.Position, "content");
                return null;
            }

            return content;
        }

        /// <summary>The text of the first title element (HTML Standard §4.2.2: its child text content with ASCII white
        /// space stripped and collapsed), or null; one that THE RULE FOR A DOCUMENT'S METADATA TEXT does not accept
        /// (<see cref="MetadataTexts.TitleProblem"/>) is reported at its start tag and left out (<see cref="HtmlMetadata"/>).</summary>
        private string? Title(StyledElement root)
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
                    if (words.Length == 0)
                    {
                        return null;
                    }

                    var title = string.Join(' ', words);
                    if (MetadataTexts.TitleProblem(title) is { } problem)
                    {
                        Refuse(HtmlStyleErrorCodes.Unsupported.Metadata, $"The <title> is not taken into the document's metadata: {problem}.", "title", element.Element.Position, "text");
                        return null;
                    }

                    return title;
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
