using System.Collections.Generic;
using System.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Css.Conditions;
using Ave.Extensions.Documents.Css.Media;
using Ave.Extensions.Documents.Css.Properties;
using Ave.Extensions.Documents.Css.Rules;
using Ave.Extensions.Documents.Css.Selectors;
using Ave.Extensions.Documents.Css.Syntax;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Extensions.Documents.Css.Cascade;

/// <summary>One complex selector of a style rule with the rule's declarations, as the cascade matches it.</summary>
/// <param name="Selector">The selector, nesting desugared (CSS Nesting 1 §3: <c>&amp;</c> is <c>:is(parent)</c>).</param>
/// <param name="Specificity">Its specificity.</param>
/// <param name="Block">The declarations.</param>
/// <param name="Origin">The origin.</param>
/// <param name="Layer">The cascade layer.</param>
internal sealed record IndexedRule(ComplexSelector Selector, Specificity Specificity, PreparedBlock Block, CascadeOrigin Origin, LayerNode Layer);

/// <summary>A <c>@page</c> rule with its origin, in order of appearance.</summary>
/// <param name="Rule">The rule.</param>
/// <param name="Origin">Its origin.</param>
public sealed record CascadedPageRule(PageRule Rule, CascadeOrigin Origin);

/// <summary>
/// The style sheets of one document made ready for the cascade (CSS Cascade 5 §6): every style rule whose
/// <c>@media</c> and <c>@supports</c> conditions hold, in order of appearance, with its origin and cascade layer,
/// nested rules desugared, imports followed, declarations parsed once. Also the <c>@font-face</c> and <c>@page</c>
/// rules that apply, for the later stages.
/// </summary>
/// <remarks>
/// Media queries are evaluated here, once, against the one media the document is laid out for; a paged document does
/// not change its media while it is styled. Rules are bucketed by the ID, class or type of their rightmost compound
/// selector, so an element is matched only against the rules that can match it.
/// </remarks>
public sealed class CascadeIndex
{
    private readonly Dictionary<string, List<IndexedRule>> _byId;
    private readonly Dictionary<string, List<IndexedRule>> _byClass;
    private readonly Dictionary<string, List<IndexedRule>> _byType;
    private readonly List<IndexedRule> _universal;

    private CascadeIndex(Builder builder)
    {
        _byId = builder.ById;
        _byClass = builder.ByClass;
        _byType = builder.ByType;
        _universal = builder.Universal;
        AllRules = builder.All;
        Budget = builder.Budget;
        FontFaces = [.. builder.FontFaces];
        PageRules = [.. builder.Pages];
        Media = builder.MediaContext;
    }

    /// <summary>The <c>@font-face</c> rules that apply, in order of appearance.</summary>
    public ValueList<FontFaceRule> FontFaces { get; }

    /// <summary>The <c>@page</c> rules that apply, in order of appearance.</summary>
    public ValueList<CascadedPageRule> PageRules { get; }

    /// <summary>The media the conditions were evaluated for.</summary>
    public MediaContext Media { get; }

    /// <summary>The limits of the document's cascade and what is spent of them: the index's, and then the computer's that
    /// styles the document with it.</summary>
    internal CascadeBudget Budget { get; }

    /// <summary>Every style rule of every origin, in order of appearance: the candidates in quirks mode, where the
    /// buckets by ID and class do not hold (HTML Standard §4.16.2).</summary>
    internal List<IndexedRule> AllRules { get; }

    /// <summary>
    /// Builds the index of a document's style sheets.
    /// </summary>
    /// <param name="sheets">The style sheets in order of appearance, each with its origin.</param>
    /// <param name="media">The media to evaluate <c>@media</c> and <c>@import</c> conditions for.</param>
    /// <param name="imports">Loads the sheets <c>@import</c> names; null when the caller follows no imports (each import
    /// is then reported and skipped).</param>
    /// <returns>The index, with the diagnostics of building it: invalid declarations and conditions, refused or skipped
    /// imports. Unsupported and ignored properties are reported when they apply, by <see cref="StyleComputer"/>. Past a
    /// limit (more style sheets than <see cref="CssInputLimits.MaxStyleSheets"/>, more entries than
    /// <see cref="CssInputLimits.MaxCascadeEntries"/>, selectors nested deeper than
    /// <see cref="CssInputLimits.MaxSelectorNesting"/>) building stops, and the result is the limit's error.</returns>
    public static Result<CssResult<CascadeIndex>, Error> Build(IEnumerable<CascadeSheet> sheets, MediaContext media, StyleSheetLoader? imports)
    {
        var budget = CascadeBudget.ForNextIndex ?? new CascadeBudget();
        CascadeBudget.ForNextIndex = null;
        var builder = new Builder(media, imports, budget);
        foreach (var sheet in sheets)
        {
            if (builder.Failure is not null)
            {
                break;
            }

            builder.Sheet(sheet);
        }

        if (builder.Failure is { } failure)
        {
            return Result<CssResult<CascadeIndex>, Error>.Failure(failure);
        }

        foreach (var root in builder.Roots.Values)
        {
            root.AssignRanks();
        }

        return Result<CssResult<CascadeIndex>, Error>.Success(new CssResult<CascadeIndex>(new CascadeIndex(builder), builder.Diagnostics.ToValueList()));
    }

    /// <summary>The rules worth matching against an element: those whose rightmost compound names its ID, one of its
    /// classes or its type, and those that name none of these. In quirks mode IDs and classes compare
    /// case-insensitively (HTML Standard §4.16.2), so every rule is a candidate.</summary>
    internal IEnumerable<IndexedRule> Candidates(IStyleElement element)
    {
        if (element.IsQuirksMode)
        {
            return AllRules;
        }

        var candidates = new List<IndexedRule>(_universal);
        if (element.Id is { } id && _byId.TryGetValue(id, out var byId))
        {
            candidates.AddRange(byId);
        }

        foreach (var name in element.Classes.Distinct())
        {
            if (_byClass.TryGetValue(name, out var byClass))
            {
                candidates.AddRange(byClass);
            }
        }

        if (_byType.TryGetValue(InfraAscii.ToLower(element.LocalName), out var byType))
        {
            candidates.AddRange(byType);
        }

        return candidates;
    }

    /// <summary>Flattens rules into the index (one cascade's worth of mutable state).</summary>
    private sealed class Builder(MediaContext media, StyleSheetLoader? imports, CascadeBudget budget)
    {
        private readonly HashSet<StyleSheet> _importing = new(ReferenceEqualityComparer.Instance);
        private int _order;

        /// <summary>The cascade's limits.</summary>
        public CascadeBudget Budget { get; } = budget;

        /// <summary>The limit's error once a limit is reached: building stops.</summary>
        public Error? Failure { get; private set; }

        /// <summary>The media <c>@media</c>, <c>@import</c> conditions and <c>media</c> attributes are evaluated
        /// for.</summary>
        public MediaContext MediaContext { get; } = media;

        /// <summary>The rules whose rightmost compound names an id, by that id.</summary>
        public Dictionary<string, List<IndexedRule>> ById { get; } = new(System.StringComparer.Ordinal);

        /// <summary>The rules whose rightmost compound names a class and no id, by that class.</summary>
        public Dictionary<string, List<IndexedRule>> ByClass { get; } = new(System.StringComparer.Ordinal);

        /// <summary>The rules whose rightmost compound names a type and no id or class, by the lowercase
        /// type.</summary>
        public Dictionary<string, List<IndexedRule>> ByType { get; } = new(System.StringComparer.Ordinal);

        /// <summary>The rules whose rightmost compound names no id, class or type: tried on every element.</summary>
        public List<IndexedRule> Universal { get; } = [];

        /// <summary>Every rule, in order of appearance.</summary>
        public List<IndexedRule> All { get; } = [];

        /// <summary>The <c>@font-face</c> rules that apply, in order of appearance.</summary>
        public List<FontFaceRule> FontFaces { get; } = [];

        /// <summary>The <c>@page</c> rules that apply, in order of appearance.</summary>
        public List<CascadedPageRule> Pages { get; } = [];

        /// <summary>The root of the layer tree of each origin (CSS Cascade 5 §6.4: layers order within an origin
        /// only).</summary>
        public Dictionary<CascadeOrigin, LayerNode> Roots { get; } = [];

        /// <summary>The diagnostics of building the index: conditions, imports and rules that are refused or
        /// ignored.</summary>
        public DiagnosticCollector Diagnostics { get; } = new();

        /// <summary>Adds one style sheet of an origin, with the sheets it imports, after the sheets added so
        /// far.</summary>
        public void Sheet(CascadeSheet sheet)
        {
            if (!Budget.TakeStyleSheet())
            {
                Failure ??= new Error(CssErrorCodes.TooLarge, $"The document takes in more than {Budget.MaxStyleSheets} style sheets, its own and imported ones together.")
                    .With(ErrorMetadataKeys.Value, Budget.MaxStyleSheets)
                    .With(ErrorMetadataKeys.SourceFile, sheet.Sheet.SourceName);
                return;
            }

            if (!Roots.TryGetValue(sheet.Origin, out var root))
            {
                root = LayerNode.Root();
                Roots[sheet.Origin] = root;
            }

            _importing.Add(sheet.Sheet);
            Rules(sheet.Sheet.Rules, new Context(sheet.Sheet, sheet.Origin, root, null));
            _importing.Remove(sheet.Sheet);
        }

        /// <summary>Where a rule is: its style sheet, origin, layer, and for a nested rule the parent's selectors.</summary>
        private sealed record Context(StyleSheet Sheet, CascadeOrigin Origin, LayerNode Layer, SelectorList? Parent);

        /// <summary>Flattens a list of rules, with the blocks of conditional and layer rules, nested style rules and
        /// imported sheets, in order of appearance. Without recursion: a chain of imports is as deep as
        /// <see cref="CssInputLimits.MaxStyleSheets"/>, so the lists being walked are kept on a stack of their own.</summary>
        private void Rules(IEnumerable<CssRule> rules, Context context)
        {
            var lists = new Stack<(IEnumerator<CssRule> Rules, Context Context, StyleSheet? Imported)>();
            lists.Push((Walk(rules), context, null));
            while (lists.Count > 0 && Failure is null)
            {
                var (list, current, imported) = lists.Peek();
                if (!list.MoveNext())
                {
                    lists.Pop();
                    list.Dispose();
                    if (imported is not null)
                    {
                        _importing.Remove(imported);
                    }

                    continue;
                }

                switch (list.Current)
                {
                    case StyleRule style when Style(style, current) is { } nested:
                        lists.Push((Walk(nested.Rules), nested.Context, null));
                        break;
                    case NestedDeclarationsRule nested when current.Parent is { } parent:
                        // Same elements, same specificity behaviour as the parent rule (CSS Nesting 1 §3.3).
                        Add(parent, nested.Declarations, current);
                        break;
                    case MediaRule mediaRule when MediaQueries.Matches(mediaRule.Queries, MediaContext):
                        lists.Push((Walk(mediaRule.Rules), current, null));
                        break;
                    case SupportsRule supports when Supports(supports.Condition, current.Sheet.SourceName, supports.Position):
                        lists.Push((Walk(supports.Rules), current, null));
                        break;
                    case LayerBlockRule layer:
                        lists.Push((Walk(layer.Rules), current with { Layer = layer.Name is { } layerName ? current.Layer.Resolve(layerName) : current.Layer.Anonymous() }, null));
                        break;
                    case LayerStatementRule statement:
                        foreach (var name in statement.Names)
                        {
                            current.Layer.Resolve(name);
                        }

                        break;
                    case ImportRule import when Import(import, current) is { } sheet:
                        lists.Push((Walk(sheet.Rules), sheet.Context, sheet.Sheet));
                        break;
                    case FontFaceRule fontFace:
                        FontFaces.Add(fontFace);
                        break;
                    case PageRule page:
                        Pages.Add(new CascadedPageRule(page, current.Origin));
                        break;
                }
            }
        }

        private static IEnumerator<CssRule> Walk(IEnumerable<CssRule> rules) => rules.GetEnumerator();

        /// <summary>Adds a style rule; its nested rules, with the context they are read in, or null when it has none or
        /// is refused.</summary>
        private (IEnumerable<CssRule> Rules, Context Context)? Style(StyleRule rule, Context context)
        {
            var selectors = context.Parent is { } parent ? Nesting.Desugar(rule.Selectors, parent) : rule.Selectors;
            if (Nesting.Depth(selectors) > CssInputLimits.MaxSelectorNesting)
            {
                // Nested rules and the pseudo-class arguments in them together: the depth selector matching recurses to.
                Failure ??= CssDiagnostics.At(CssErrorCodes.TooDeep, $"Style rules and selector lists are nested deeper than {CssInputLimits.MaxSelectorNesting}.", context.Sheet.SourceName, rule.Position)
                    .With(ErrorMetadataKeys.Value, CssInputLimits.MaxSelectorNesting);
                return null;
            }

            Add(selectors, rule.Declarations, context);
            return rule.ChildRules.IsEmpty ? null : (rule.ChildRules, context with { Parent = selectors });
        }

        private void Add(SelectorList selectors, IEnumerable<Declaration> declarations, Context context)
        {
            var block = DeclarationPreparer.Prepare(declarations, context.Sheet.SourceName, context.Origin, ref _order, Diagnostics);
            if (block.Entries.IsEmpty && block.WhenApplied.IsEmpty)
            {
                return;
            }

            if (!Budget.AddEntries(block.Entries.Count + selectors.Selectors.Count))
            {
                Failure ??= CssDiagnostics.At(CssErrorCodes.TooLarge, $"The style sheets make more than {Budget.MaxEntries} selectors and declarations for the cascade (each sheet counted every time it is taken in).", context.Sheet.SourceName, selectors.Selectors[0].Position)
                    .With(ErrorMetadataKeys.Value, Budget.MaxEntries);
                return;
            }

            foreach (var selector in selectors.Selectors)
            {
                var indexed = new IndexedRule(selector, selector.Specificity, block, context.Origin, context.Layer);
                All.Add(indexed);
                Bucket(selector).Add(indexed);
            }
        }

        /// <summary>The bucket of a selector: by the first ID of its rightmost compound, else its first class, else its
        /// type; the universal bucket when it names none.</summary>
        private List<IndexedRule> Bucket(ComplexSelector selector)
        {
            var compound = selector.Steps[^1].Compound.Selectors;
            if (compound.OfType<IdSelector>().FirstOrDefault() is { } id)
            {
                return Get(ById, id.Name);
            }

            if (compound.OfType<ClassSelector>().FirstOrDefault() is { } name)
            {
                return Get(ByClass, name.Name);
            }

            return compound.OfType<TypeSelector>().FirstOrDefault(type => !type.IsUniversal) is { } type ? Get(ByType, InfraAscii.ToLower(type.LocalName)) : Universal;
        }

        private static List<IndexedRule> Get(Dictionary<string, List<IndexedRule>> buckets, string key)
        {
            if (!buckets.TryGetValue(key, out var bucket))
            {
                bucket = [];
                buckets[key] = bucket;
            }

            return bucket;
        }

        /// <summary>@import (CSS Cascade 5 §2): when its conditions hold, the imported sheet's rules take the import's
        /// place, in the import's layer; they are returned to be walked there, the sheet marked as being imported until
        /// they are done. An import of a sheet that is already being imported (a cycle) is skipped, as browsers do.</summary>
        private (IEnumerable<CssRule> Rules, Context Context, StyleSheet Sheet)? Import(ImportRule import, Context context)
        {
            if (!MediaQueries.Matches(import.Media, MediaContext)
                || (import.Supports is { } condition && !Supports(condition, context.Sheet.SourceName, import.Position)))
            {
                return null;
            }

            if (imports is null)
            {
                Diagnostics.Add(CssDiagnostics.At(CssErrorCodes.Invalid.Rule, $"@import of '{CssSerializer.Shortened(import.Url)}' is skipped: the caller follows no imports.", context.Sheet.SourceName, import.Position)
                    .With(ErrorMetadataKeys.Value, import.Url));
                return null;
            }

            if (!Budget.TakeStyleSheet())
            {
                Failure ??= CssDiagnostics.At(CssErrorCodes.TooLarge, $"The document takes in more than {Budget.MaxStyleSheets} style sheets, its own and imported ones together; '{CssSerializer.Shortened(import.Url)}' is one too many.", context.Sheet.SourceName, import.Position)
                    .With(ErrorMetadataKeys.Value, Budget.MaxStyleSheets);
                return null;
            }

            if (imports(import, context.Sheet) is not { } sheet)
            {
                return null;
            }

            if (!_importing.Add(sheet))
            {
                Diagnostics.Add(CssDiagnostics.At(CssErrorCodes.Invalid.Rule, $"@import of '{CssSerializer.Shortened(import.Url)}' is skipped: the sheet imports itself.", context.Sheet.SourceName, import.Position)
                    .With(ErrorMetadataKeys.Value, import.Url));
                return null;
            }

            var layer = import.Layer switch
            {
                null => context.Layer,
                "" => context.Layer.Anonymous(),
                var name => context.Layer.Resolve(name),
            };
            return (sheet.Rules, context with { Sheet = sheet, Layer = layer }, sheet);
        }

        /// <summary>Whether a value holds, at any depth, a ), ] or } that closes nothing, a bad string or a bad URL, or an
        /// env() or attr() without an argument, or at its top level a '!' (CSS Syntax 3 §8.2, &lt;declaration-value&gt;),
        /// without recursion.</summary>
        private static bool IsNoDeclaration(IReadOnlyList<ComponentValue> values)
        {
            if (values.Any(value => value is CssToken { Kind: CssTokenKind.Delim, Text: "!" }))
            {
                return true;
            }

            var pending = new Stack<IReadOnlyList<ComponentValue>>();
            pending.Push(values);
            while (pending.Count > 0)
            {
                foreach (var value in pending.Pop())
                {
                    switch (value)
                    {
                        case CssToken { Kind: CssTokenKind.CloseParen or CssTokenKind.CloseSquare or CssTokenKind.CloseCurly or CssTokenKind.BadString or CssTokenKind.BadUrl }:
                            return true;
                        // env() and attr() without an argument (measured: '@supports (--y: env())' and its negation false).
                        case FunctionValue function when (function.IsNamed("env") || function.IsNamed("attr"))
                                                         && function.Values.All(item => item is CssToken { Kind: CssTokenKind.Whitespace }):
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

        /// <summary>
        /// Evaluates a supports condition as Chromium 159 would (<see cref="SupportsConditions.Evaluate"/>); where the
        /// engine cannot tell what Chromium answers, the condition is refused and taken as false.
        /// </summary>
        private bool Supports(SupportsCondition condition, string sourceName, TextPosition position)
        {
            if (SupportsConditions.Evaluate(condition, Holds) is { } answer)
            {
                return answer;
            }

            Diagnostics.Add(CssDiagnostics.At(CssErrorCodes.Unsupported.AtRule, "The @supports condition asks about CSS this profile does not read, so its answer in a browser is not known; it is taken as false.", sourceName, position));
            return false;
        }

        /// <summary>Whether Chromium 159 supports a declaration: true for a value of a known property the engine reads
        /// and supports, or a well-formed var() or a CSS-wide keyword in one (Chromium accepts those at parse time),
        /// and for a custom property with an ordinary value; false for another engine's property; null where the engine cannot
        /// tell (a value it does not read or support, an ignored property's value, an unknown property, which is
        /// refused).</summary>
        private bool? Holds(Declaration declaration)
        {
            // Text with a '!' outside !important, or an env() or attr() without an argument, is no declaration:
            // Chromium 159 answers false, and its negation true (measured: '@supports not (color: var(--x) !)' and
            // 'not (--y: env())' apply). The engine refuses it, and takes it as false. (An unmatched ), ] or }, a bad
            // string or URL makes Chromium drop the @supports rule whole, which the parser does; in the supports() of
            // an @import it is refused here too.)
            if (IsNoDeclaration(declaration.Value))
            {
                return null;
            }

            var name = InfraAscii.ToLower(declaration.Name);
            if (declaration.IsCustomProperty)
            {
                // A var() that does not match the grammar makes the declaration invalid (Chromium 159: '@supports (--x:
                // var())' is false, measured); so does a '!' at the top level of a var() fallback, or an env() or attr()
                // whose arguments Chromium does not read (measured: '(--x: var(--y, !))', '(--x: f(var(--y, !)))' and
                // '(--x: env(,))' are false, '(--x: var(--y, f(!)))' true); so is the name '--' alone.
                if (name == "--" || (DeclarationPreparer.ContainsFunction(declaration.Value, "var") && !VarSubstitution.IsWellFormed(declaration.Value))
                    || VarSubstitution.HasBangInFallback(declaration.Value) || !VarSubstitution.HasWellFormedEnvAndAttr(declaration.Value))
                {
                    return false;
                }

                return name.Length > 2 && declaration.Value.All(value => value is not CssToken { Kind: CssTokenKind.CloseCurly or CssTokenKind.CloseSquare or CssTokenKind.CloseParen or CssTokenKind.BadString or CssTokenKind.BadUrl } and not CssToken { Kind: CssTokenKind.Delim, Text: "!" })
                    ? true
                    : null;
            }

            var longhand = PropertyRegistry.Find(name);
            var shorthand = longhand is null ? PropertyRegistry.FindShorthand(name) : null;
            if ((longhand is not null || shorthand is not null || name == "all") && DeclarationPreparer.ContainsFunction(declaration.Value, "var"))
            {
                // A value with var() is checked against its property's grammar only after substitution, so it is
                // supported when every var() matches the grammar of CSS Variables 1 §3, whatever the rest and the
                // fallback hold (CSS Conditional 3 §6.1; Chromium 159, measured: var() and var(--x, a;b) false,
                // var(--x, -internal-center) true). A '!' in a fallback makes it false (measured: !, a!b, 5px
                // !important); one deeper in a function the engine cannot answer for.
                return !VarSubstitution.IsWellFormed(declaration.Value) || VarSubstitution.HasBangInFallback(declaration.Value) ? false
                    : DeclarationPreparer.ContainsNestedBang(declaration.Value) ? null
                    : true;
            }

            // Chromium keeps -internal- keywords for its own style sheet: an author's is not supported (measured).
            if (name == "text-align" && DeclarationPreparer.ContainsInternalKeyword(declaration.Value))
            {
                return false;
            }

            if (longhand is not null || shorthand is not null || name == "all")
            {
                if (DeclarationPreparer.WideKeyword(declaration.Value) is not null)
                {
                    return true;
                }

                return longhand is not null ? (longhand.Parse(new ValueReader(declaration.Value)).IsValid ? true : null)
                    : shorthand is not null && shorthand.Parse(new ValueReader(declaration.Value)).Values is not null ? true : null;
            }

            if (name.StartsWith("-moz-", System.StringComparison.Ordinal) || name.StartsWith("-ms-", System.StringComparison.Ordinal) || name.StartsWith("-o-", System.StringComparison.Ordinal))
            {
                return false;
            }

            return null;
        }
    }
}
