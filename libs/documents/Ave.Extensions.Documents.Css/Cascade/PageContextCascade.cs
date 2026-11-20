using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Css.Properties;
using Ave.Extensions.Documents.Css.Rules;
using Ave.Extensions.Documents.Css.Selectors;
using Ave.Extensions.Documents.Css.Syntax;
using Ave.Extensions.Documents.Css.Values;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Extensions.Documents.Css.Cascade;

/// <summary>The page-margin boxes of the top and bottom rows (CSS Paged Media 3 §4.2, §5): the ones the engine
/// styles.</summary>
public enum PageMarginBox
{
    /// <summary><c>@top-left</c>.</summary>
    TopLeft,

    /// <summary><c>@top-center</c>.</summary>
    TopCenter,

    /// <summary><c>@top-right</c>.</summary>
    TopRight,

    /// <summary><c>@bottom-left</c>.</summary>
    BottomLeft,

    /// <summary><c>@bottom-center</c>.</summary>
    BottomCenter,

    /// <summary><c>@bottom-right</c>.</summary>
    BottomRight,
}

/// <summary>A generated page-margin box with its computed style (CSS Paged Media 3 §5).</summary>
/// <param name="Box">Which box.</param>
/// <param name="Style">Its computed style, inheriting from the page context; <c>content</c> holds a
/// <see cref="CssContentList"/> that <see cref="CssContentList.Generates"/>.</param>
/// <param name="SourceName">The style sheet of the <c>content</c> declaration that won.</param>
/// <param name="Position">Where that declaration is.</param>
public sealed record MarginBoxStyle(PageMarginBox Box, ComputedStyle Style, string SourceName, TextPosition Position)
{
    /// <summary>The value of <c>content</c>.</summary>
    public CssContentList Content => (CssContentList)Style[PageContextCascade.ContentProperty];

    /// <summary>The base URL of the style sheet of the <c>content</c> declaration, for the URLs of its images (CSS
    /// Values 4 §4.5.1); null when the caller gave none.</summary>
    public string? BaseUrl { get; init; }
}

/// <summary>The page context of one kind of page and its generated margin boxes (CSS Paged Media 3 §5.3).</summary>
/// <param name="Style">The page context's computed style.</param>
/// <param name="MarginBoxes">The margin boxes whose <c>content</c> generates a box, in the order of
/// <see cref="PageMarginBox"/>; a box with <c>none</c>, <c>normal</c> or no <c>content</c> has no entry.</param>
public sealed record PageContextStyle(ComputedStyle Style, ValueList<MarginBoxStyle> MarginBoxes);

/// <summary>
/// The page contexts of a document: every page (the <c>@page</c> rules without a selector) and page 1 (those with
/// <c>:first</c> over them), with what the page-context cascade found. Box generation reports
/// <see cref="Diagnostics"/> when it draws margin boxes; until it does, it refuses each <c>@page</c> rule with a
/// selector or margin rules as a whole.
/// </summary>
/// <param name="EveryPage">The page context of every page.</param>
/// <param name="FirstPage">The page context of page 1.</param>
/// <param name="Diagnostics">The refusals and warnings of the page-context cascade, each at its place.</param>
public sealed record PageContexts(PageContextStyle EveryPage, PageContextStyle FirstPage, ValueList<Error> Diagnostics)
{
    /// <summary>No page rules: the initial style, no margin boxes.</summary>
    public static PageContexts None { get; } = new(new PageContextStyle(ComputedStyle.Initial, []), new PageContextStyle(ComputedStyle.Initial, []), []);
}

/// <summary>
/// The cascade of the page context and its page-margin boxes (CSS Paged Media 3 §5.3), next to the elements' (CSS
/// Cascade 5 §6): for every page and for page 1, the declarations of the <c>@page</c> rules that apply, by origin and
/// importance, layer, specificity (§4.1: <c>:first</c> over none) and order, make the page context's computed style;
/// each margin rule's declarations make its margin box's, inheriting from that page context. The same property parsers
/// as for elements read every value, with the same refusals; on top, what the engine does not draw on a margin box is
/// refused at its place.
/// </summary>
/// <remarks>
/// <para>Inheritance, as Chromium 159 prints (measured): the page context inherits from the root element's computed
/// style, <c>font-family</c> included (<c>html { font-family: Lato; color: green; font-size: 20pt; font-weight: bold }</c>
/// and no font in <c>@page</c> print the margin boxes in Lato, green, 20pt, bold: cases <c>pm-inherit-family</c>,
/// <c>pm-inherit</c>); <c>body</c>'s style does not reach them. A face that only a margin box uses is a known limit:
/// Chromium has not loaded it when it prints the margin boxes and prints its fallback serif face there, the engine
/// draws the face (case <c>pm-family-unused</c>). <c>em</c> and a percentage font size are the page context's size
/// (2em in a margin rule under a 10pt page context is 20pt). <c>rem</c> is the page context's size too, not the root's
/// (1rem under a 10pt page context is 10pt while <c>html</c> is 20pt): refused, so no writer is surprised by it.</para>
/// <para>Defaults of the margin boxes (§5.3, measured): text-align left, center and right in the left, centre and right
/// boxes, vertical-align middle in the top and bottom rows; a margin rule's own declarations override them, inheritance
/// does not, so <c>text-align</c> in <c>@page</c> is refused.</para>
/// <para>What is decided here and what is not: <c>size</c>, <c>margin</c> and the other declarations of <c>@page</c>
/// that do not reach a margin box are the page box's (box generation reads them); everything that reaches a margin box
/// is checked here. Overflow of a margin box is the layout's.</para>
/// <para>Bounded work: one pass over the rules, each declaration prepared once; the two page contexts and twelve margin
/// boxes are computed by a <see cref="StyleComputer"/> charged to the document's cascade budget.</para>
/// </remarks>
public static class PageContextCascade
{
    /// <summary>The longhand <c>content</c>.</summary>
    internal static PropertyDefinition ContentProperty { get; } = PropertyRegistry.Find("content")!;

    /// <summary>The margin rules the engine styles, by name.</summary>
    private static readonly FrozenDictionary<string, PageMarginBox> Boxes = new Dictionary<string, PageMarginBox>(StringComparer.Ordinal)
    {
        ["top-left"] = PageMarginBox.TopLeft,
        ["top-center"] = PageMarginBox.TopCenter,
        ["top-right"] = PageMarginBox.TopRight,
        ["bottom-left"] = PageMarginBox.BottomLeft,
        ["bottom-center"] = PageMarginBox.BottomCenter,
        ["bottom-right"] = PageMarginBox.BottomRight,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>The longhands a margin box takes: content, the text properties (font, colour, line height, white space,
    /// alignment, spacing), vertical-align, margins, padding, borders and the background colour.</summary>
    internal static readonly FrozenSet<string> MarginBoxLonghands = FrozenSet.Create(
        StringComparer.Ordinal,
        "content", "font-family", "font-size", "font-weight", "font-style", "color", "line-height", "white-space-collapse",
        "text-wrap-mode", "text-align", "letter-spacing", "word-spacing", "vertical-align",
        "margin-top", "margin-right", "margin-bottom", "margin-left",
        "padding-top", "padding-right", "padding-bottom", "padding-left",
        "border-top-width", "border-right-width", "border-bottom-width", "border-left-width",
        "border-top-style", "border-right-style", "border-bottom-style", "border-left-style",
        "border-top-color", "border-right-color", "border-bottom-color", "border-left-color",
        "background-color");

    /// <summary>The shorthands a margin box takes: of the longhands above (<c>font</c> also resets the font longhands
    /// the engine keeps at their initial values).</summary>
    internal static readonly FrozenSet<string> MarginBoxShorthands = FrozenSet.Create(
        StringComparer.Ordinal,
        "font", "white-space", "margin", "padding", "border", "border-top", "border-right", "border-bottom", "border-left",
        "border-width", "border-style", "border-color");

    /// <summary>The declarations of <c>@page</c> that belong to the page box, not to the page context: box generation
    /// reads them.</summary>
    private static readonly FrozenSet<string> PageBoxDescriptors = FrozenSet.Create(
        StringComparer.Ordinal, "size", "margin", "margin-top", "margin-right", "margin-bottom", "margin-left");

    /// <summary>
    /// Computes the page contexts of a document and their margin boxes.
    /// </summary>
    /// <param name="index">The document's style sheets; <see cref="CascadeIndex.PageRules"/> are cascaded.</param>
    /// <param name="root">The root element's computed style.</param>
    /// <param name="values">The viewport and fonts relative units need.</param>
    /// <returns>The page contexts with the diagnostics of the cascade, or the error of a limit of the document's
    /// cascade.</returns>
    public static Result<PageContexts, Error> Compute(CascadeIndex index, ComputedStyle root, ValueContext values)
    {
        if (index.PageRules.IsEmpty)
        {
            return Result<PageContexts, Error>.Success(PageContexts.None);
        }

        var builder = new Builder();
        foreach (var rule in index.PageRules)
        {
            builder.Rule(rule);
        }

        var computer = new StyleComputer(index, values);
        var every = builder.Context(computer, root, firstPage: false);
        if (every.IsFailure)
        {
            return Result<PageContexts, Error>.Failure(every.Error);
        }

        var first = builder.Context(computer, root, firstPage: true);
        if (first.IsFailure)
        {
            return Result<PageContexts, Error>.Failure(first.Error);
        }

        return Result<PageContexts, Error>.Success(new PageContexts(every.Value, first.Value, [.. builder.Diagnostics.ToValueList(), .. computer.Diagnostics]));
    }

    /// <summary>A <c>@page</c> rule the engine cascades: its declarations for the page context and its margin rules.</summary>
    private sealed record Page(CascadedPageRule Rule, bool IsFirst, PreparedBlock Block, List<Margin> Margins)
    {
        public Specificity Specificity => IsFirst ? Rule.Rule.Selectors[0].Specificity : Specificity.Zero;
    }

    /// <summary>A margin rule of the six: its declarations without <c>content</c>, and its <c>content</c>
    /// declarations, each with its place in the order of appearance.</summary>
    private sealed record Margin(PageMarginBox Box, PageMarginRule Rule, PreparedBlock Block, List<(Declaration Declaration, int Order)> Content);

    /// <summary>One cascade's worth of state: the rules read once, the diagnostics in order.</summary>
    private sealed class Builder
    {
        private readonly List<Page> _pages = [];
        private readonly HashSet<Declaration> _reported = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<PageMarginBox, PreparedBlock> _defaults = [];
        private int _order;

        public DiagnosticCollector Diagnostics { get; } = new();

        /// <summary>Reads one <c>@page</c> rule: refuses what the engine does not draw, prepares the rest.</summary>
        public void Rule(CascadedPageRule cascaded)
        {
            var rule = cascaded.Rule;
            var isFirst = rule.Selectors is [{ Name: null, PseudoClasses: ["first"] }];
            if (rule.Selectors.Count > 0 && !isFirst)
            {
                Refuse(cascaded, rule.Position, "page", "Only @page and @page :first are drawn: every page uses the rules without a selector, page 1 also the :first ones (CSS Paged Media 3 §4.1); this rule is left out.");
                return;
            }

            var pageDeclarations = rule.Declarations.Where(declaration => !PageBoxDescriptors.Contains(InfraAscii.ToLower(declaration.Name)) && CheckPage(cascaded, declaration)).ToList();
            var block = DeclarationPreparer.Prepare(pageDeclarations, cascaded.SourceName, cascaded.Origin, ref _order, Diagnostics);
            var margins = new List<Margin>();
            foreach (var margin in rule.Margins)
            {
                if (!Boxes.TryGetValue(margin.Name, out var box))
                {
                    Refuse(cascaded, margin.Position, "@" + margin.Name, $"'@{margin.Name}' is not drawn: only the top and bottom rows (@top-left, @top-center, @top-right, @bottom-left, @bottom-center, @bottom-right) are; move the content there.");
                    continue;
                }

                var content = new List<(Declaration, int)>();
                var others = new List<Declaration>();
                foreach (var declaration in margin.Declarations)
                {
                    if (!CheckMargin(cascaded, declaration))
                    {
                        continue;
                    }

                    if (InfraAscii.ToLower(declaration.Name) == "content")
                    {
                        content.Add((declaration, _order++));
                    }
                    else
                    {
                        others.Add(declaration);
                    }
                }

                var prepared = DeclarationPreparer.Prepare(others, cascaded.SourceName, cascaded.Origin, ref _order, Diagnostics);
                margins.Add(new Margin(box, margin, prepared, content));
            }

            _pages.Add(new Page(cascaded, isFirst, block, margins));
        }

        /// <summary>The page context of every page or of page 1, with its generated margin boxes.</summary>
        public Result<PageContextStyle, Error> Context(StyleComputer computer, ComputedStyle parent, bool firstPage)
        {
            var pages = _pages.Where(page => !page.IsFirst || firstPage).ToList();
            var context = computer.ComputeOutside(pages.Select(page => new StyleComputer.OutsideBlock(page.Block, page.Rule.Origin, page.Specificity, page.Rule.Layer)), parent);
            if (context.IsFailure)
            {
                return Result<PageContextStyle, Error>.Failure(context.Error);
            }

            var boxes = new List<MarginBoxStyle>();
            foreach (var box in Enum.GetValues<PageMarginBox>())
            {
                var margins = pages.SelectMany(page => page.Margins.Where(margin => margin.Box == box).Select(margin => (Page: page, Margin: margin))).ToList();
                if (Content(margins) is not { } found || !found.Value.Generates)
                {
                    continue;
                }

                var (content, contentPage, declaration) = found;

                var blocks = new List<StyleComputer.OutsideBlock> { new(Defaults(box), CascadeOrigin.UserAgent, Specificity.Zero, null) };
                blocks.AddRange(margins.Select(item => new StyleComputer.OutsideBlock(item.Margin.Block, item.Page.Rule.Origin, item.Page.Specificity, item.Page.Rule.Layer)));
                var style = computer.ComputeOutside(blocks, context.Value);
                if (style.IsFailure)
                {
                    return Result<PageContextStyle, Error>.Failure(style.Error);
                }

                boxes.Add(new MarginBoxStyle(box, style.Value.With(PageContextCascade.ContentProperty, content), contentPage.Rule.SourceName, declaration.Position) { BaseUrl = contentPage.Rule.BaseUrl });
            }

            return Result<PageContextStyle, Error>.Success(new PageContextStyle(context.Value, [.. boxes]));
        }

        /// <summary>The <c>content</c> of a margin box: the declaration highest in the cascade order (origin and importance,
        /// layer, specificity, order; CSS Cascade 5 §6.1) whose value is drawn; one that is not is refused and the next
        /// is taken, as for an element's refused value. A CSS-wide keyword is <c>normal</c>: <c>content</c> is not
        /// inherited, and the page context's is its initial value. Null when no declaration is left.</summary>
        private (CssContentList Value, Page Page, Declaration Declaration)? Content(List<(Page Page, Margin Margin)> margins)
        {
            var candidates = new List<(Declaration Declaration, Page Page, (int Origin, int Layer, Specificity Specificity, int Order) Key)>();
            foreach (var (page, margin) in margins)
            {
                foreach (var (declaration, order) in margin.Content)
                {
                    var layer = page.Rule.Layer?.Rank ?? 0;
                    var important = declaration.Important;
                    candidates.Add((declaration, page, (StyleComputer.OriginRank(page.Rule.Origin, important), important ? -layer : layer, page.Specificity, order)));
                }
            }

            candidates.Sort((left, right) => right.Key.CompareTo(left.Key));
            foreach (var (declaration, page, _) in candidates)
            {
                if (DeclarationPreparer.WideKeyword(declaration.Value) is { } keyword)
                {
                    if (keyword is "revert" or "revert-layer")
                    {
                        RefuseOnce(page.Rule, declaration, $"'content: {keyword}' on a margin box is not drawn: write the value.");
                        continue;
                    }

                    return (CssContentList.Normal, page, declaration);
                }

                var (value, refused) = ContentValue.Parse(declaration.Value);
                if (value is not null)
                {
                    return (value, page, declaration);
                }

                RefuseOnce(page.Rule, declaration, $"'content: {CssSerializer.Shortened(Trimmed(declaration.Value))}' on a margin box: {refused}.");
            }

            return null;
        }

        /// <summary>The defaults of a margin box (CSS Paged Media 3 §5.3; Chromium 159, measured): text-align by its
        /// place in the row, vertical-align middle; as user-agent declarations, so a margin rule overrides them and an
        /// inherited value does not.</summary>
        private PreparedBlock Defaults(PageMarginBox box)
        {
            if (_defaults.TryGetValue(box, out var known))
            {
                return known;
            }

            var align = box switch
            {
                PageMarginBox.TopLeft or PageMarginBox.BottomLeft => "left",
                PageMarginBox.TopCenter or PageMarginBox.BottomCenter => "center",
                _ => "right",
            };
            Declaration Ident(string name, string value) => new(name, [new CssToken(CssTokenKind.Ident, value, default)], false, default);
            var block = DeclarationPreparer.Prepare([Ident("text-align", align), Ident("vertical-align", "middle")], string.Empty, CascadeOrigin.UserAgent, ref _order, Diagnostics);
            _defaults[box] = block;
            return block;
        }

        /// <summary>Whether a declaration of <c>@page</c> that reaches the margin boxes is drawn there; refused (and
        /// false) when it is not. The page box's own declarations are not seen here; the other declarations that are
        /// not inherited reach no margin box, and box generation refuses them as the page box's.</summary>
        private bool CheckPage(CascadedPageRule rule, Declaration declaration)
        {
            var name = InfraAscii.ToLower(declaration.Name);
            if (name is "counter-reset" or "counter-increment" or "counter-set")
            {
                return Refuse(rule, declaration.Position, name, $"'{name}' in @page is not drawn: page counters start at 1 and count every page (CSS Paged Media 3 §6.1).");
            }

            if (name == "text-align")
            {
                return Refuse(rule, declaration.Position, name, "'text-align' in @page does not reach the margin boxes, which align by their place in the row unless their own rule says otherwise: set it in the margin rule.");
            }

            // The page box's declarations and the ones that reach no margin box (not inherited, unknown, custom):
            // box generation refuses them as the page box's.
            var longhand = PropertyRegistry.Find(name);
            var shorthand = longhand is null ? PropertyRegistry.FindShorthand(name) : null;
            var inherited = longhand is { Inherited: true }
                || (shorthand is not null && shorthand.Longhands.All(item => PropertyRegistry.Find(item) is { Inherited: true }));
            if (declaration.IsCustomProperty || !inherited)
            {
                return false;
            }

            if ((longhand is not null && !MarginBoxLonghands.Contains(longhand.Name)) || (shorthand is not null && !MarginBoxShorthands.Contains(shorthand.Name)))
            {
                return Refuse(rule, declaration.Position, name, $"'{name}' in @page reaches the margin boxes, which do not draw it: a margin box takes font, color, line-height, white-space, letter-spacing and word-spacing from @page.");
            }

            return CheckCommon(rule, declaration, name);
        }

        /// <summary>Whether a declaration of a margin rule is drawn on its margin box; refused (and false) when it is
        /// not.</summary>
        private bool CheckMargin(CascadedPageRule rule, Declaration declaration)
        {
            var name = declaration.IsCustomProperty ? declaration.Name : InfraAscii.ToLower(declaration.Name);
            if (name is "width" or "height" or "min-width" or "min-height" or "max-width" or "max-height" or "inline-size" or "block-size")
            {
                return Refuse(rule, declaration.Position, name, $"'{name}' on a margin box is not drawn: the row sets the boxes' widths and heights from their content and the page margin (CSS Paged Media 3 §5.3); change the content or the page margin.");
            }

            if (name.StartsWith("background-", StringComparison.Ordinal) && name != "background-color")
            {
                return Refuse(rule, declaration.Position, name, $"'{name}' on a margin box is not drawn: only background-color is.");
            }

            if (!declaration.IsCustomProperty && !MarginBoxLonghands.Contains(name) && !MarginBoxShorthands.Contains(name))
            {
                return Refuse(rule, declaration.Position, name, $"'{name}' on a margin box is not drawn: a margin box takes content, font, color, line-height, white-space, text-align, letter-spacing, word-spacing, vertical-align, margins, padding, borders and background-color.");
            }

            if (name == "vertical-align" && !IsRowAlignment(declaration.Value))
            {
                return Refuse(rule, declaration.Position, name, $"'vertical-align: {CssSerializer.Shortened(Trimmed(declaration.Value))}' on a margin box is not drawn: it takes top, middle or bottom.");
            }

            if (name is not ("font-size" or "line-height" or "font") && HasToken(declaration.Value, token => token.Kind == CssTokenKind.Percentage))
            {
                return Refuse(rule, declaration.Position, name, $"A percentage in '{name}' on a margin box is not drawn: use a length.");
            }

            return CheckCommon(rule, declaration, name);
        }

        /// <summary>The checks of both kinds of rule: custom properties and <c>var()</c>, and <c>rem</c>.</summary>
        private bool CheckCommon(CascadedPageRule rule, Declaration declaration, string name)
        {
            if (declaration.IsCustomProperty || DeclarationPreparer.ContainsFunction(declaration.Value, "var"))
            {
                return Refuse(rule, declaration.Position, name, $"Custom properties and var() in a page or margin rule are not drawn: write the value in '{CssSerializer.Shortened(declaration.Name)}'.");
            }

            if (HasToken(declaration.Value, token => token.Kind == CssTokenKind.Dimension && InfraAscii.EqualsIgnoreCase(token.Text, "rem")))
            {
                return Refuse(rule, declaration.Position, name, $"'rem' in '{name}' is the page context's font size in a page or margin rule, not the root element's: use pt or em.");
            }

            return true;
        }

        private static bool IsRowAlignment(IReadOnlyList<ComponentValue> values) =>
            Trimmed(values) is [CssToken { Kind: CssTokenKind.Ident } keyword] && (keyword.IsIdent("top") || keyword.IsIdent("middle") || keyword.IsIdent("bottom"));


        /// <summary>Whether a token anywhere in a value (in functions and blocks too) matches, without recursion.</summary>
        private static bool HasToken(IReadOnlyList<ComponentValue> values, Func<CssToken, bool> matches)
        {
            var pending = new Stack<IReadOnlyList<ComponentValue>>();
            pending.Push(values);
            while (pending.Count > 0)
            {
                foreach (var value in pending.Pop())
                {
                    switch (value)
                    {
                        case CssToken token when matches(token):
                            return true;
                        case FunctionValue function:
                            pending.Push(function.Values);
                            break;
                        case SimpleBlock block:
                            pending.Push(block.Values);
                            break;
                    }
                }
            }

            return false;
        }

        private static List<ComponentValue> Trimmed(IReadOnlyList<ComponentValue> values) =>
            [.. values.Where(value => value is not CssToken { Kind: CssTokenKind.Whitespace })];

        /// <summary>Refuses a <c>content</c> declaration once, though both page contexts cascade it.</summary>
        private void RefuseOnce(CascadedPageRule rule, Declaration declaration, string message)
        {
            if (_reported.Add(declaration))
            {
                Refuse(rule, declaration.Position, "content", message);
            }
        }

        /// <summary>Refuses something at its place; false, so a check can return it.</summary>
        private bool Refuse(CascadedPageRule rule, TextPosition position, string what, string message)
        {
            Diagnostics.Add(CssDiagnostics.At(CssErrorCodes.Unsupported.PageRule, message, rule.SourceName, position)
                .With(ErrorMetadataKeys.Value, what));
            return false;
        }
    }
}
