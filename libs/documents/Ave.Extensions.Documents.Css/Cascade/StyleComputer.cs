using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Css.Properties;
using Ave.Extensions.Documents.Css.Selectors;
using Ave.Extensions.Documents.Css.Syntax;
using Ave.Extensions.Documents.Css.Values;
using Ave.Extensions.ErrorPaths;

namespace Ave.Extensions.Documents.Css.Cascade;

/// <summary>The declarations an element carries itself, besides the style sheets' rules.</summary>
/// <param name="StyleAttribute">The declarations of its <c>style</c> attribute (author origin, element-attached, CSS
/// Cascade 5 §6.3).</param>
/// <param name="PresentationalHints">Its presentational hints as declarations (the author presentational hint origin,
/// §6.5).</param>
/// <param name="SourceName">The document's name, for diagnostics.</param>
public sealed record ElementDeclarations(ValueList<Declaration> StyleAttribute, ValueList<Declaration> PresentationalHints, string SourceName)
{
    /// <summary>No declarations.</summary>
    public static ElementDeclarations None { get; } = new([], [], string.Empty);

    /// <summary>Whether text-align -webkit-left, -webkit-center and -webkit-right compute to start on this element: a
    /// rule of the front-end's language, not of CSS (Chromium 159 does it for an HTML table, so a table inside
    /// <c>center</c> is not centred inside).</summary>
    public bool ResetsLegacyAlignment { get; init; }
}

/// <summary>
/// Computes elements' styles from a <see cref="CascadeIndex"/> (CSS Cascade 5 §4, §6, §7; CSS Variables 1): it
/// collects the declarations that apply, sorts them by origin and importance, element attachment, cascade layer,
/// specificity and order, takes each property's winning value (rolling back for <c>revert</c> and
/// <c>revert-layer</c>), substitutes <c>var()</c>, and computes it with the property registry, inheriting from the
/// parent's computed style. One computer serves one document, elements in tree order (parents before children); it
/// collects the diagnostics as it goes, each at most once per declaration.
/// </summary>
/// <remarks>
/// <para>What the engine refuses is reported when it would apply: a refused declaration that wins (it is then
/// skipped, so the next declaration in the cascade is used, as by a browser that does not know the value), a rule
/// with an unsupported or ignored property that matches an element.</para>
/// <para>Bounded work: each element is matched against the rules of its buckets (<see cref="CascadeIndex"/>) by a
/// matcher with a work limit (<see cref="SelectorMatcher"/>); custom properties are resolved in dependency order without
/// recursion; every substitution stops at <see cref="CssInputLimits.MaxSubstitutionLength"/> and all of them together
/// at <see cref="CssInputLimits.MaxSubstitutionWork"/>.</para>
/// </remarks>
public sealed class StyleComputer
{
    private readonly CascadeIndex _index;
    private readonly ValueContext _values;
    private readonly SelectorMatcher _matcher = new();
    private readonly DiagnosticCollector _diagnostics = new();
    private readonly HashSet<object> _reported = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<(PreparedDeclaration, ErrorCode)> _reportedAt = [];

    /// <summary>The user-agent declarations refused, by element: each element they refuse is reported, so a front-end
    /// can name the element (an author's declaration is reported once).</summary>
    private readonly HashSet<(PreparedDeclaration, IStyleElement)> _reportedFor = new(new ReportedForComparer());

    /// <summary>The element being computed.</summary>
    private IStyleElement? _element;
    private readonly Dictionary<IStyleElement, ComputedStyle> _styles = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<(PreparedDeclaration Declaration, object Custom), (VarSubstitution.Outcome Outcome, ValueList<ComponentValue> Values)> _substituted =
        new(new SubstitutionComparer());

    private long _substitutionWork;
    private bool _substitutionWorkReported;
    private bool _matchingReported;
    private double _rootFontSize = PropertyRegistry.InitialFontSizePoints;
    private int _order;

    /// <summary>A computer for one document.</summary>
    /// <param name="index">The document's style sheets.</param>
    /// <param name="values">The viewport and fonts relative units need.</param>
    public StyleComputer(CascadeIndex index, ValueContext values)
    {
        _index = index;
        _values = values;
    }

    /// <summary>The diagnostics so far, in the order they were found.</summary>
    public ValueList<Error> Diagnostics => _diagnostics.ToValueList();

    /// <summary>The number of diagnostics kept so far: with <see cref="DiagnosticAt"/>, what one call of
    /// <see cref="Compute"/> found, without copying the list.</summary>
    public int DiagnosticCount => _diagnostics.Count;

    /// <summary>A diagnostic kept so far, by its place in <see cref="Diagnostics"/>.</summary>
    /// <param name="index">The place, below <see cref="DiagnosticCount"/>.</param>
    /// <returns>The diagnostic.</returns>
    public Error DiagnosticAt(int index) => _diagnostics[index];

    /// <summary>The order the longhands are computed in: the font first, as <c>em</c>, <c>ex</c> and <c>ch</c> need it
    /// (CSS Values 4 §6.1.1); then the registry order (where a border's style comes before its width).</summary>
    private static readonly PropertyDefinition[] ComputeOrder =
    [
        PropertyRegistry.FontFamily, PropertyRegistry.FontWeight, PropertyRegistry.FontStyle, PropertyRegistry.FontSize,
        .. PropertyRegistry.Longhands.Where(property => property != PropertyRegistry.FontFamily && property != PropertyRegistry.FontWeight
            && property != PropertyRegistry.FontStyle && property != PropertyRegistry.FontSize),
    ];

    /// <summary>The computed style of an element.</summary>
    /// <param name="element">The element; its parent's style must have been computed by this computer first (the root
    /// has none): elements are computed in tree order. A child whose parent was not computed is a programming error,
    /// <see cref="InvalidOperationException"/>, not a style without inheritance (review 39 API-4).</param>
    /// <param name="own">Its style attribute and presentational hints.</param>
    /// <returns>Its computed style.</returns>
    public ComputedStyle Compute(IStyleElement element, ElementDeclarations? own = null)
    {
        _element = element;
        ComputedStyle? parent = null;
        if (element.Parent is { } parentElement && !_styles.TryGetValue(parentElement, out parent))
        {
            throw new InvalidOperationException("The parent's style has not been computed: compute the elements in tree order, parents first.");
        }

        var candidates = Collect(element, own ?? ElementDeclarations.None);
        var context = new ComputeContext(parent, _rootFontSize, _values, element.IsRoot);
        var custom = CustomProperties(candidates.Custom, parent);
        var shorthands = new Dictionary<PreparedDeclaration, ShorthandParse>();
        foreach (var property in ComputeOrder)
        {
            context.Set(property, Value(property, candidates.Longhands[property.Index], context, custom, shorthands));
        }

        Adjust(context, own?.ResetsLegacyAlignment == true);

        var style = new ComputedStyle([.. PropertyRegistry.Longhands.Select(property => context.Own(property)!)], custom, context.FontSizeSource);
        if (element.IsRoot)
        {
            _rootFontSize = ((CssLength)style[PropertyRegistry.FontSize]).Points;
        }

        _styles[element] = style;
        return style;
    }

    // ---- collecting (§6.1) ----

    /// <summary>One declared value competing in the cascade.</summary>
    private readonly record struct Candidate(Entry Entry, Priority Priority, CascadeOrigin Origin, object Layer);

    /// <summary>The cascade's sort key (§6.1), highest wins: origin and importance, element attachment, layer,
    /// specificity, order of appearance.</summary>
    private readonly record struct Priority(int Origin, int Attached, int Layer, Specificity Specificity, int Order) : IComparable<Priority>
    {
        /// <summary>Compares by the criteria of CSS Cascade 5 §6.1 in their order: the first that differs decides.
        /// </summary>
        public int CompareTo(Priority other) =>
            Origin != other.Origin ? Origin.CompareTo(other.Origin)
            : Attached != other.Attached ? Attached.CompareTo(other.Attached)
            : Layer != other.Layer ? Layer.CompareTo(other.Layer)
            : Specificity != other.Specificity ? Specificity.CompareTo(other.Specificity)
            : Order.CompareTo(other.Order);

        /// <summary>§6.2: normal UA, user, presentational hints, author; then important author, user, UA.</summary>
        public static int OriginRank(CascadeOrigin origin, bool important) => (origin, important) switch
        {
            (CascadeOrigin.UserAgent, false) => 0,
            (CascadeOrigin.User, false) => 1,
            (CascadeOrigin.PresentationalHint, _) => 2,
            (CascadeOrigin.Author, false) => 3,
            (CascadeOrigin.Author, true) => 4,
            (CascadeOrigin.User, true) => 5,
            _ => 6,
        };
    }

    /// <summary>Stands for the layer of the style attribute's declarations in <c>revert-layer</c>: they are in no
    /// style sheet's layer.</summary>
    private static readonly object StyleAttributeLayer = new();

    private sealed class Candidates
    {
        /// <summary>The candidates for each longhand, by <see cref="PropertyDefinition.Index"/>; null where none was
        /// declared.</summary>
        public List<Candidate>[] Longhands { get; } = new List<Candidate>[PropertyRegistry.Longhands.Count];

        /// <summary>The candidates for each custom property, by its name as written.</summary>
        public Dictionary<string, List<Candidate>> Custom { get; } = new(StringComparer.Ordinal);

        /// <summary>Adds a declared value to the list of the property it sets.</summary>
        public void Add(Candidate candidate)
        {
            if (candidate.Entry.Longhand is { } longhand)
            {
                (Longhands[longhand.Index] ??= []).Add(candidate);
            }
            else if (!Custom.TryGetValue(candidate.Entry.Custom!, out var list))
            {
                Custom[candidate.Entry.Custom!] = [candidate];
            }
            else
            {
                list.Add(candidate);
            }
        }
    }

    /// <summary>The declared values for an element (CSS Cascade 5 §4.2): the declarations of every rule whose selector
    /// matches it, its presentational hints and its style attribute, each property's sorted by the cascade order, highest
    /// first (§6.1).</summary>
    private Candidates Collect(IStyleElement element, ElementDeclarations own)
    {
        var candidates = new Candidates();
        foreach (var rule in _index.Candidates(element))
        {
            if (!_matcher.Matches(rule.Selector, element))
            {
                if (_matcher.Exhausted && !_matchingReported)
                {
                    _matchingReported = true;
                    _diagnostics.Add(new Error(CssErrorCodes.TooLarge, $"Matching the selectors against the elements needs more than {CssInputLimits.MaxMatchingWork} steps; the styles are incomplete.")
                        .With(ErrorMetadataKeys.Value, CssInputLimits.MaxMatchingWork));
                }

                continue;
            }

            // Collecting is work too: elements times the rules that match them, so it counts against the same limit.
            _matcher.Spend(1 + rule.Block.Entries.Count);
            Applied(rule.Block);
            foreach (var entry in rule.Block.Entries)
            {
                var important = entry.Declaration.Important;
                candidates.Add(new Candidate(
                    entry,
                    new Priority(Priority.OriginRank(rule.Origin, important), 0, important ? -rule.Layer.Rank : rule.Layer.Rank, rule.Specificity, entry.Declaration.Order),
                    rule.Origin,
                    rule.Layer));
            }
        }

        AddOwn(own.PresentationalHints, own.SourceName, CascadeOrigin.PresentationalHint, candidates);
        AddOwn(own.StyleAttribute, own.SourceName, CascadeOrigin.Author, candidates);
        foreach (var list in candidates.Longhands)
        {
            list?.Sort((left, right) => right.Priority.CompareTo(left.Priority));
        }

        foreach (var list in candidates.Custom.Values)
        {
            list.Sort((left, right) => right.Priority.CompareTo(left.Priority));
        }

        return candidates;
    }

    private void AddOwn(ValueList<Declaration> declarations, string sourceName, CascadeOrigin origin, Candidates candidates)
    {
        if (declarations.IsEmpty)
        {
            return;
        }

        var block = DeclarationPreparer.Prepare(declarations, sourceName, origin, ref _order, _diagnostics);
        Applied(block);
        var attached = origin == CascadeOrigin.Author ? 1 : 0;
        foreach (var entry in block.Entries)
        {
            var important = entry.Declaration.Important;
            candidates.Add(new Candidate(entry, new Priority(Priority.OriginRank(origin, important), attached, 0, Specificity.Zero, entry.Declaration.Order), origin, StyleAttributeLayer));
        }
    }

    /// <summary>A rule applies: what it says about properties the engine ignores or does not know is reported, once.</summary>
    private void Applied(PreparedBlock block)
    {
        foreach (var notice in block.WhenApplied)
        {
            if (_reported.Add(notice))
            {
                _diagnostics.Add(notice);
            }
        }
    }

    // ---- the cascaded value (§6, §7.3.3, §7.3.4) ----

    /// <summary>The winning candidate (CSS Cascade 5 §6.1), or null when none is left: refused values are reported and
    /// skipped; <c>revert</c> skips the rest of its origin (presentational hints count as author), <c>revert-layer</c> the
    /// rest of its layer (§7.3).</summary>
    private Candidate? Winner(List<Candidate>? candidates)
    {
        if (candidates is null)
        {
            return null;
        }

        var skips = new List<Func<Candidate, bool>>();
        foreach (var candidate in candidates)
        {
            if (skips.Any(skip => skip(candidate)))
            {
                continue;
            }

            switch (candidate.Entry.Value)
            {
                case RefusedEntry refused when candidate.Origin == CascadeOrigin.UserAgent:
                    if (_reportedFor.Add((candidate.Entry.Declaration, _element!)))
                    {
                        _diagnostics.Add(DeclarationPreparer.Diagnostic(refused.Code, $"{refused.Problem}; the declaration is skipped.", candidate.Entry.Declaration));
                    }

                    continue;
                case RefusedEntry refused:
                    Report(candidate.Entry.Declaration, refused.Code, $"{refused.Problem}; the declaration is skipped.");
                    continue;
                case WideKeywordEntry { Keyword: "revert" }:
                    var group = RevertGroup(candidate.Origin);
                    skips.Add(other => RevertGroup(other.Origin) == group);
                    continue;
                case WideKeywordEntry { Keyword: "revert-layer" }:
                    var (origin, important, layer) = (candidate.Origin, candidate.Entry.Declaration.Important, candidate.Layer);
                    skips.Add(other => other.Origin == origin && other.Entry.Declaration.Important == important && ReferenceEquals(other.Layer, layer));
                    continue;
                default:
                    return candidate;
            }
        }

        return null;
    }

    /// <summary>The origin <c>revert</c> rolls back (CSS Cascade 5 §7.3): user agent, user, or author with the
    /// presentational hints, which count as author-level (§6.2).</summary>
    private static int RevertGroup(CascadeOrigin origin) => origin switch
    {
        CascadeOrigin.UserAgent => 0,
        CascadeOrigin.User => 1,
        _ => 2,
    };

    // ---- custom properties (CSS Variables 1 §2, §3) ----

    /// <summary>The computed custom properties: inherited from the parent, overridden by the element's own, with var()
    /// substituted in dependency order (CSS Variables 1 §2.3). The order and the cycles come from an iterative
    /// strongly-connected-components walk (Tarjan) over the references of the element's declared values, so a chain of
    /// any length costs its length and no stack; the members of a cycle, and the values whose substitution fails, have
    /// the guaranteed-invalid value (they are absent). An element that declares none shares its parent's table, so the
    /// substitutions of its declarations can be remembered (<see cref="_substituted"/>).</summary>
    private IReadOnlyDictionary<string, CssTokens> CustomProperties(Dictionary<string, List<Candidate>> declared, ComputedStyle? parent)
    {
        IReadOnlyDictionary<string, CssTokens> inherited = parent?.CustomProperties ?? NoCustomProperties;
        if (declared.Count == 0)
        {
            return inherited;
        }

        var result = new Dictionary<string, CssTokens>(inherited, StringComparer.Ordinal);
        var pending = new Dictionary<string, (CustomEntry Value, PreparedDeclaration Declaration)>(StringComparer.Ordinal);
        foreach (var (name, candidates) in declared)
        {
            var winner = Winner(candidates);
            switch (winner?.Entry.Value)
            {
                case null:
                case WideKeywordEntry { Keyword: "inherit" or "unset" }:
                    // Custom properties are inherited (§2): unset and a rolled-back revert keep the parent's value.
                    break;
                case WideKeywordEntry:
                    result.Remove(name);
                    break;
                case CustomEntry { HasVar: false } value:
                    result[name] = new CssTokens(value.Values, value.Text);
                    break;
                case CustomEntry value:
                    pending[name] = (value, winner.Value.Entry.Declaration);
                    result.Remove(name);
                    break;
            }
        }

        foreach (var component in ResolutionOrder(pending))
        {
            if (component.Cyclic)
            {
                foreach (var name in component.Names)
                {
                    Report(pending[name].Declaration, CssErrorCodes.Invalid.ComputedValue, $"The custom property '{name}' refers to itself through var(); it has no value.");
                }

                continue;
            }

            var name0 = component.Names[0];
            var (entry, declaration) = pending[name0];
            var outcome = Substitute(entry.Values, result, declaration, out var substituted);
            if (outcome == VarSubstitution.Outcome.Done)
            {
                result[name0] = new CssTokens(substituted, CssSerializer.Serialize(substituted));
            }
            else
            {
                Failed(declaration, outcome);
            }
        }

        // Frozen: a computed style's custom properties cannot change, also through a cast (review 39 R-4).
        return result.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private static readonly IReadOnlyDictionary<string, CssTokens> NoCustomProperties = FrozenDictionary<string, CssTokens>.Empty;

    /// <summary>A strongly connected component of the dependency graph: one property, or the properties of a cycle.</summary>
    private sealed record Component(List<string> Names, bool Cyclic);

    /// <summary>The pending custom properties in an order where every property comes after those it refers to (Tarjan's
    /// algorithm with an explicit stack: it emits the components dependencies first), with the cycles marked.</summary>
    private static List<Component> ResolutionOrder(Dictionary<string, (CustomEntry Value, PreparedDeclaration Declaration)> pending)
    {
        var edges = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (name, (value, _)) in pending)
        {
            edges[name] = [.. VarSubstitution.References(value.Values).Where(pending.ContainsKey)];
        }

        var order = new List<Component>();
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        var low = new Dictionary<string, int>(StringComparer.Ordinal);
        var onStack = new HashSet<string>(StringComparer.Ordinal);
        var path = new Stack<string>();
        var work = new Stack<(string Name, int Edge)>();
        var next = 0;
        foreach (var start in pending.Keys)
        {
            if (index.ContainsKey(start))
            {
                continue;
            }

            work.Push((start, 0));
            while (work.Count > 0)
            {
                var (name, edge) = work.Pop();
                if (edge == 0)
                {
                    index[name] = low[name] = next++;
                    path.Push(name);
                    onStack.Add(name);
                }

                var targets = edges[name];
                if (edge < targets.Count)
                {
                    work.Push((name, edge + 1));
                    var target = targets[edge];
                    if (!index.ContainsKey(target))
                    {
                        work.Push((target, 0));
                    }
                    else if (onStack.Contains(target))
                    {
                        low[name] = Math.Min(low[name], index[target]);
                    }

                    continue;
                }

                // All edges done: hand the low link to the caller, and emit a component at its root.
                if (work.Count > 0 && work.Peek() is var (caller, _) && edges[caller].Contains(name))
                {
                    low[caller] = Math.Min(low[caller], low[name]);
                }

                if (low[name] == index[name])
                {
                    var names = new List<string>();
                    string member;
                    do
                    {
                        member = path.Pop();
                        onStack.Remove(member);
                        names.Add(member);
                    }
                    while (member != name);

                    order.Add(new Component(names, names.Count > 1 || targets.Contains(name)));
                }
            }
        }

        return order;
    }

    /// <summary>A substitution, remembered per declaration and custom-property table (one table is shared by all
    /// elements that declare no custom property of their own), and charged to the cascade's substitution work.</summary>
    private VarSubstitution.Outcome Substitute(IReadOnlyList<ComponentValue> values, IReadOnlyDictionary<string, CssTokens> custom, PreparedDeclaration declaration, out ValueList<ComponentValue> result)
    {
        var key = (declaration, (object)custom);
        if (_substituted.TryGetValue(key, out var known))
        {
            result = known.Values;
            return known.Outcome;
        }

        if (_substitutionWork > CssInputLimits.MaxSubstitutionWork)
        {
            result = [];
            return VarSubstitution.Outcome.Uncertain;
        }

        var outcome = VarSubstitution.Substitute(values, name => custom.TryGetValue(name, out var value) ? value : null, out result, out var length);
        _substitutionWork += length;
        if (_substitutionWork > CssInputLimits.MaxSubstitutionWork && !_substitutionWorkReported)
        {
            _substitutionWorkReported = true;
            _diagnostics.Add(new Error(CssErrorCodes.TooLarge, $"The var() substitutions of the document make more than {CssInputLimits.MaxSubstitutionWork} characters of text together; the rest are not made.")
                .With(ErrorMetadataKeys.Value, CssInputLimits.MaxSubstitutionWork));
        }

        if (_substituted.Count < CssInputLimits.MaxMatchingMemo)
        {
            _substituted[key] = (outcome, result);
        }

        return outcome;
    }

    private void Failed(PreparedDeclaration declaration, VarSubstitution.Outcome outcome)
    {
        switch (outcome)
        {
            case VarSubstitution.Outcome.TooLong:
                Report(declaration, CssErrorCodes.Invalid.ComputedValue, $"The var() substitution in '{declaration.Name}' is longer than {CssInputLimits.MaxSubstitutionLength} characters, Chromium's limit; the property is unset, as in Chromium (CSS Variables 1 §3.1).");
                break;
            case VarSubstitution.Outcome.TooDeep:
                Report(declaration, CssErrorCodes.TooDeep, $"The var() substitution in '{declaration.Name}' nests blocks and functions deeper than {CssInputLimits.MaxDepth}; it is refused.");
                break;
            case VarSubstitution.Outcome.Uncertain:
                Report(declaration, CssErrorCodes.TooLarge, $"The var() substitution in '{declaration.Name}' is within a few characters of {CssInputLimits.MaxSubstitutionLength}, Chromium's limit, or past the document's substitution work; whether Chromium keeps it cannot be told, so it is refused.");
                break;
            default:
                Report(declaration, CssErrorCodes.Invalid.ComputedValue, $"A var() in '{declaration.Name}' names a custom property with no value and has no fallback; the property is unset (CSS Variables 1 §3.1).");
                break;
        }
    }

    // ---- longhands (§4.4, §7) ----

    /// <summary>A longhand's computed value: the winner's value computed, the CSS-wide keywords applied, or the
    /// inherited or initial value when nothing is declared.</summary>
    private CssValue Value(PropertyDefinition property, List<Candidate>? candidates, ComputeContext context, IReadOnlyDictionary<string, CssTokens> custom, Dictionary<PreparedDeclaration, ShorthandParse> shorthands)
    {
        CssValue Unset() => property == PropertyRegistry.FontSize
            ? PropertyRegistry.InheritFontSize(context)
            : property.Inherited ? context.Inherited(property) : InitialOf(property, context);
        _declaredBy[property.Index] = null;
        if (Winner(candidates) is not { } winner)
        {
            return Unset();
        }

        var declaration = winner.Entry.Declaration;
        _declaredBy[property.Index] = declaration;
        switch (winner.Entry.Value)
        {
            case WideKeywordEntry wide:
                return Keyword(wide.Keyword, property, context) ?? Unset();
            case SpecifiedEntry specified:
                return Computed(property, specified.Value, context, declaration) ?? Unset();
            case PendingEntry pending:
                return Substituted(property, pending, declaration, context, custom, shorthands) ?? Unset();
            default:
                return Unset();
        }
    }

    /// <summary>The declaration that won the cascade for each longhand of the element being computed, or null.</summary>
    private readonly PreparedDeclaration?[] _declaredBy = new PreparedDeclaration?[PropertyRegistry.Longhands.Count];

    private static readonly PropertyDefinition OverflowX = PropertyRegistry.Find("overflow-x")!;
    private static readonly PropertyDefinition OverflowY = PropertyRegistry.Find("overflow-y")!;

    /// <summary>The computed values that depend on other computed values of the element, after the cascade:
    /// <list type="bullet">
    /// <item>CSS Overflow 3 §3.1, as Chromium 159 applies it: when one axis is neither visible nor clip, visible on the
    /// other computes to auto (which the engine does not support: refused). Chromium keeps clip beside hidden, where
    /// the specification says hidden (measured, corpus case properties), and so does the engine.</item>
    /// <item>An element the front-end marks (<see cref="ElementDeclarations.ResetsLegacyAlignment"/>): text-align
    /// -webkit-left, -webkit-center and -webkit-right compute to start.</item>
    /// </list></summary>
    private void Adjust(ComputeContext context, bool resetsLegacyAlignment)
    {
        var x = (context.Own(OverflowX) as CssKeyword)?.Name;
        var y = (context.Own(OverflowY) as CssKeyword)?.Name;
        static bool Scrolls(string? value) => value is not ("visible" or "clip");
        if (Scrolls(x) != Scrolls(y))
        {
            var (scrolling, other, otherValue) = Scrolls(x) ? (OverflowX, OverflowY, y) : (OverflowY, OverflowX, x);
            if (otherValue == "visible")
            {
                context.Set(other, new CssKeyword("auto"));
                var by = _declaredBy[scrolling.Index];
                var message = $"'{scrolling.Name}: {(context.Own(scrolling) as CssKeyword)?.Name}' makes {other.Name} compute to auto (CSS Overflow 3 §3.1), which is not supported; the property is kept.";
                if (by is not null)
                {
                    Report(by, CssErrorCodes.Unsupported.Value, message);
                }
                else
                {
                    _diagnostics.Add(new Error(CssErrorCodes.Unsupported.Value, message).With(ErrorMetadataKeys.Property, other.Name));
                }
            }
        }

        if (resetsLegacyAlignment
            && context.Own(PropertyRegistry.TextAlign) is CssKeyword { Name: "-webkit-left" or "-webkit-center" or "-webkit-right" })
        {
            context.Set(PropertyRegistry.TextAlign, new CssKeyword("start"));
        }
    }

    /// <summary>initial, inherit and unset (§7.3.1, §7.3.2); null for unset. font-size goes through its compute
    /// function either way, because a keyword-derived size depends on the element's own family.</summary>
    private static CssValue? Keyword(string keyword, PropertyDefinition property, ComputeContext context) => (keyword, property == PropertyRegistry.FontSize) switch
    {
        ("initial", true) => PropertyRegistry.ComputeFontSize(new CssKeyword("medium"), context).Value,
        ("inherit", true) => PropertyRegistry.InheritFontSize(context),
        ("initial", false) => InitialOf(property, context),
        ("inherit", false) => context.Inherited(property),
        _ => null,
    };

    /// <summary>The initial value, computed for the element: most initial values are computed values already, but some
    /// computed values depend on others (a border width is zero while its style is none, CSS Backgrounds 3 §3.3; the root
    /// is blockified, CSS Display 3 §2.7).</summary>
    private static CssValue InitialOf(PropertyDefinition property, ComputeContext context) =>
        property.Compute(property.Initial, context).Value ?? property.Initial;

    /// <summary>A pending value after var() substitution: parsed as the longhand, or as its shorthand (once per
    /// declaration and element); invalid at computed-value time when it does not parse (CSS Variables 1 §3.1).</summary>
    private CssValue? Substituted(PropertyDefinition property, PendingEntry pending, PreparedDeclaration declaration, ComputeContext context, IReadOnlyDictionary<string, CssTokens> custom, Dictionary<PreparedDeclaration, ShorthandParse> shorthands)
    {
        var outcome = Substitute(declaration.Source.Value, custom, declaration, out var values);
        if (outcome != VarSubstitution.Outcome.Done)
        {
            Failed(declaration, outcome);
            return null;
        }

        if (DeclarationPreparer.WideKeyword(values) is { } wide)
        {
            return wide is "revert" or "revert-layer" ? null : Keyword(wide, property, context);
        }

        if (pending.IsAll)
        {
            Report(declaration, CssErrorCodes.Unsupported.Value, $"'all' after var() substitution ('{CssSerializer.Shortened(values)}') is not valid or not supported in this profile ('all' takes only a CSS-wide keyword); every property is unset.");
            return null;
        }

        ParsedValue parsed;
        if (pending.Shorthand is { } shorthand)
        {
            if (!shorthands.TryGetValue(declaration, out var expanded))
            {
                expanded = shorthand.Parse(new ValueReader(values));
                shorthands[declaration] = expanded;
            }

            parsed = expanded.Values is { } longhands
                ? ParsedValue.Of(longhands.TryGetValue(property.Name, out var value) ? value : new CssKeyword("initial"))
                : new ParsedValue(null, expanded.Problem, expanded.Unsupported) { Code = expanded.Code };
        }
        else
        {
            parsed = property.Parse(new ValueReader(values));
        }

        if (parsed.IsValid)
        {
            return parsed.Value is CssKeyword { Name: "initial" } ? Keyword("initial", property, context) : Computed(property, parsed.Value!, context, declaration);
        }

        if (parsed.Unsupported)
        {
            Report(declaration, parsed.Code ?? CssErrorCodes.Unsupported.Value, $"'{declaration.Name}' after var() substitution: {parsed.Problem}; the property is unset.");
        }
        else
        {
            // The engine cannot tell a value a browser finds invalid at computed-value time from a valid one beyond this
            // profile (env(), a vendor keyword): the one answer for both is a refusal.
            Report(declaration, CssErrorCodes.Unsupported.Value, $"'{declaration.Name}' after var() substitution ('{CssSerializer.Shortened(values)}') is not valid or not supported in this profile ({parsed.Problem}); the property is unset.");
        }

        return null;
    }

    /// <summary>The computed value of a specified value; null (and reported) when the engine cannot compute it.</summary>
    private CssValue? Computed(PropertyDefinition property, CssValue specified, ComputeContext context, PreparedDeclaration declaration)
    {
        var computed = property.Compute(specified, context);
        if (computed.Value is { } value)
        {
            return value;
        }

        Report(declaration, specified is CssCalc ? CssErrorCodes.Unsupported.Calc : CssErrorCodes.Unsupported.Value, $"'{property.Name}': {computed.Unsupported}; the property is unset.");
        return null;
    }

    private void Report(PreparedDeclaration declaration, ErrorCode code, string message)
    {
        if (_reportedAt.Add((declaration, code)))
        {
            _diagnostics.Add(DeclarationPreparer.Diagnostic(code, message, declaration));
        }
    }

    /// <summary>Compares (declaration, element) keys by identity.</summary>
    private sealed class ReportedForComparer : IEqualityComparer<(PreparedDeclaration Declaration, IStyleElement Element)>
    {
        /// <inheritdoc />
        public bool Equals((PreparedDeclaration Declaration, IStyleElement Element) x, (PreparedDeclaration Declaration, IStyleElement Element) y) =>
            ReferenceEquals(x.Declaration, y.Declaration) && ReferenceEquals(x.Element, y.Element);

        /// <inheritdoc />
        public int GetHashCode((PreparedDeclaration Declaration, IStyleElement Element) obj) =>
            HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj.Declaration), System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj.Element));
    }

    /// <summary>Compares substitution keys by the identity of the declaration and the table.</summary>
    private sealed class SubstitutionComparer : IEqualityComparer<(PreparedDeclaration Declaration, object Custom)>
    {
        /// <inheritdoc />
        public bool Equals((PreparedDeclaration Declaration, object Custom) x, (PreparedDeclaration Declaration, object Custom) y) =>
            ReferenceEquals(x.Declaration, y.Declaration) && ReferenceEquals(x.Custom, y.Custom);

        /// <inheritdoc />
        public int GetHashCode((PreparedDeclaration Declaration, object Custom) obj) =>
            HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj.Declaration), System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj.Custom));
    }
}
