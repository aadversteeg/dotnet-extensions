using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Ave.Extensions.Functional;
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
/// matcher, and the matching, collecting, sorting, cascading and the dependency walk of custom properties are charged
/// to one work limit (<see cref="CssInputLimits.MaxCascadeWork"/>); custom properties are resolved in dependency order
/// without recursion; every substitution stops at <see cref="CssInputLimits.MaxSubstitutionLength"/> and all of them
/// together at <see cref="CssInputLimits.MaxSubstitutionWork"/>. A limit that is reached ends the cascade: that
/// <see cref="Compute"/> and every later one return the limit's error, never a partly computed style.</para>
/// </remarks>
public sealed class StyleComputer
{
    private readonly CascadeIndex _index;
    private readonly ValueContext _values;
    private readonly CascadeBudget _budget;
    private readonly SelectorMatcher _matcher;
    private readonly DiagnosticCollector _diagnostics = new();

    /// <summary>The declarations whose notice (<see cref="PreparedBlock.WhenApplied"/>) has been reported: a declaration,
    /// not a message text, so a declaration written in two places is reported at both, and one declaration that applies
    /// to many elements once.</summary>
    private readonly HashSet<PreparedDeclaration> _reported = [];
    private readonly HashSet<(PreparedDeclaration, ErrorCode)> _reportedAt = [];

    /// <summary>The user-agent declarations refused, by element: each element they refuse is reported, so a front-end
    /// can name the element (an author's declaration is reported once).</summary>
    private readonly HashSet<(PreparedDeclaration, IStyleElement)> _reportedFor = new(new ReportedForComparer());

    /// <summary>The element being computed.</summary>
    private IStyleElement? _element;
    private readonly Dictionary<IStyleElement, ComputedStyle> _styles = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<(PreparedDeclaration Declaration, object Custom), (VarSubstitution.Outcome Outcome, ValueList<ComponentValue> Values)> _substituted =
        new(new SubstitutionComparer());

    private readonly Dictionary<(object Parent, PreparedDeclaration?[] Winners), ImmutableDictionary<string, CssTokens>> _customTables = new(new CustomTableComparer());

    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<Declaration[], PreparedBlock> _ownBlocks = new();
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<Declaration[], PreparedBlock> _hintBlocks = new();

    /// <summary>The limit's error once a limit is reached; from then on every computation returns it.</summary>
    private Error? _failure;
    private double _rootFontSize = PropertyRegistry.InitialFontSizePoints;
    private int _order;

    /// <summary>A computer for one document.</summary>
    /// <param name="index">The document's style sheets.</param>
    /// <param name="values">The viewport and fonts relative units need.</param>
    public StyleComputer(CascadeIndex index, ValueContext values)
    {
        _index = index;
        _values = values;
        _budget = index.Budget;
        _matcher = new SelectorMatcher(_budget);
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

    /// <summary>The computed style of an element; or, once a limit of the cascade is reached
    /// (<see cref="CssInputLimits.MaxCascadeWork"/>, <see cref="CssInputLimits.MaxSubstitutionWork"/>, the depth of a
    /// substituted value), the limit's error, for this element and every later one: the cascade stops there.</summary>
    /// <param name="element">The element; its parent's style must have been computed by this computer first (the root
    /// has none): elements are computed in tree order. A child whose parent was not computed is a programming error,
    /// <see cref="InvalidOperationException"/>, not a style without inheritance.</param>
    /// <param name="own">Its style attribute and presentational hints.</param>
    /// <returns>Its computed style, or the limit's error.</returns>
    public Result<ComputedStyle, Error> Compute(IStyleElement element, ElementDeclarations? own = null)
    {
        _element = element;
        ComputedStyle? parent = null;
        if (element.Parent is { } parentElement && !_styles.TryGetValue(parentElement, out parent) && _failure is null)
        {
            throw new InvalidOperationException("The parent's style has not been computed: compute the elements in tree order, parents first.");
        }

        if (_failure is not null)
        {
            return Result<ComputedStyle, Error>.Failure(_failure.Value);
        }

        var candidates = Collect(element, own ?? ElementDeclarations.None);
        var context = new ComputeContext(parent, _rootFontSize, _values, element.IsRoot);
        var custom = _failure is null ? CustomProperties(candidates.Custom, parent) : ImmutableDictionary<string, CssTokens>.Empty;
        _custom = custom;
        var shorthands = new Dictionary<PreparedDeclaration, ShorthandParse>();
        foreach (var property in ComputeOrder)
        {
            if (_failure is not null)
            {
                break;
            }

            var all = property.Name is "direction" or "unicode-bidi" ? null : candidates.All;
            context.Set(property, Value(property, candidates.Longhands[property.Index], all, context, custom, shorthands));
        }

        if (_failure is null && _budget.Exhausted)
        {
            Fail(new Error(CssErrorCodes.TooLarge, $"Styling the elements needs more than {_budget.MaxWork} steps of selector matching and cascading.")
                .With(ErrorMetadataKeys.Value, _budget.MaxWork));
        }

        if (_failure is not null)
        {
            return Result<ComputedStyle, Error>.Failure(_failure.Value);
        }

        Adjust(context, own?.ResetsLegacyAlignment == true);

        var style = new ComputedStyle([.. PropertyRegistry.Longhands.Select(property => context.Own(property)!)], custom, context.FontSizeSource);
        if (element.IsRoot)
        {
            _rootFontSize = ((CssLength)style[PropertyRegistry.FontSize]).Points;
        }

        _styles[element] = style;
        return Result<ComputedStyle, Error>.Success(style);
    }

    /// <summary>The custom properties of the element being computed, for <see cref="RollsBack"/>.</summary>
    private IReadOnlyDictionary<string, CssTokens> _custom = ImmutableDictionary<string, CssTokens>.Empty;

    /// <summary>The CSS-wide keyword that rolls back (<c>revert</c> or <c>revert-layer</c>) a candidate's value is
    /// after var() substitution, or null: a var() that substitutes to one of them rolls the cascade back as the keyword
    /// itself would (CSS Cascade 5 §7.3; Chromium 159, measured: <c>display: var(--nope, revert)</c> on a p is block,
    /// <c>margin-top: var(--nope, revert-layer)</c> takes the previous layer's value).</summary>
    private string? RollsBack(Candidate candidate)
    {
        if (candidate.Entry.Value is not PendingEntry)
        {
            return null;
        }

        var declaration = candidate.Entry.Declaration;
        if (Substitute(declaration.Source.Value, _custom, declaration, out var values) != VarSubstitution.Outcome.Done)
        {
            return null;
        }

        var keyword = DeclarationPreparer.WideKeyword(values);
        return keyword is "revert" or "revert-layer" ? keyword : null;
    }

    /// <summary>Ends the cascade at a limit: the error is kept, and every computation from now on returns it.</summary>
    private void Fail(Error error) => _failure ??= error;

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

        /// <summary>The candidates of <c>all</c> (CSS Cascade 5 §3.2): one per declaration, not one per longhand; the
        /// cascade takes them with each longhand's own.</summary>
        public List<Candidate>? All { get; private set; }

        /// <summary>Adds a declared value to the list of the property it sets.</summary>
        public void Add(Candidate candidate)
        {
            if (candidate.Entry.Longhand is { } longhand)
            {
                (Longhands[longhand.Index] ??= []).Add(candidate);
            }
            else if (candidate.Entry.Custom is null)
            {
                (All ??= []).Add(candidate);
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
            if (_matcher.Exhausted)
            {
                return candidates;
            }

            if (!_matcher.Matches(rule.Selector, element))
            {
                continue;
            }

            // Collecting is work too: elements times the rules that match them, so it counts against the same limit.
            _budget.Charge(1 + rule.Block.Entries.Count);
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
        return candidates;
    }

    /// <summary>Sorts a property's candidates by the cascade order, highest first (§6.1), charged n log2 n.</summary>
    private void Sort(List<Candidate>? list)
    {
        if (list is not { Count: > 1 })
        {
            return;
        }

        _budget.Charge(list.Count * (long)Math.Ceiling(Math.Log2(list.Count)));
        list.Sort((left, right) => right.Priority.CompareTo(left.Priority));
    }

    /// <summary>The candidate highest in the cascade order (§6.1), found by one look at each (charged one per
    /// candidate); null for none.</summary>
    private Candidate? Highest(List<Candidate>? list)
    {
        if (list is not { Count: > 0 })
        {
            return null;
        }

        _budget.Charge(list.Count);
        var best = list[0];
        for (var i = 1; i < list.Count; i++)
        {
            if (list[i].Priority.CompareTo(best.Priority) > 0)
            {
                best = list[i];
            }
        }

        return best;
    }

    private void AddOwn(ValueList<Declaration> declarations, string sourceName, CascadeOrigin origin, Candidates candidates)
    {
        if (declarations.IsEmpty)
        {
            return;
        }

        // Prepared once per list: elements the parser makes again for one token share their style attribute's list.
        // (A weak table: a list no element shares any more does not keep its prepared block alive.)
        var key = System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsArray(declarations.Items)!;
        var blocks = origin == CascadeOrigin.Author ? _ownBlocks : _hintBlocks;
        if (!blocks.TryGetValue(key, out var block))
        {
            block = DeclarationPreparer.Prepare(declarations, sourceName, origin, ref _order, _diagnostics);
            _budget.Charge(declarations.Count);
            blocks.AddOrUpdate(key, block);
        }

        Applied(block);
        var attached = origin == CascadeOrigin.Author ? 1 : 0;
        foreach (var entry in block.Entries)
        {
            var important = entry.Declaration.Important;
            candidates.Add(new Candidate(entry, new Priority(Priority.OriginRank(origin, important), attached, 0, Specificity.Zero, entry.Declaration.Order), origin, StyleAttributeLayer));
        }
    }

    /// <summary>A rule applies: what it says about properties the engine ignores or does not know is reported, once per
    /// declaration.</summary>
    private void Applied(PreparedBlock block)
    {
        foreach (var notice in block.WhenApplied)
        {
            if (_reported.Add(notice.Declaration))
            {
                _diagnostics.Add(notice.Diagnostic);
            }
        }
    }

    // ---- the cascaded value (§6, §7.3.3, §7.3.4) ----

    /// <summary>The winning candidate (CSS Cascade 5 §6.1), or null when none is left: refused values are reported and
    /// skipped; <c>revert</c> skips the rest of its origin (presentational hints count as author), <c>revert-layer</c> the
    /// rest of its layer (§7.3.4, §7.3.5). An important <c>revert-layer</c> rolls back as if no rules were in its layer
    /// or between the layer's normal and important levels (§7.3.5): it skips every candidate down to and including the
    /// layer's normal declarations, so the lower layers of the normal order are next. In the style attribute it skips
    /// the attribute's declarations only, not the important rules between (§7.3.5, element-attached styles). The
    /// candidates of <c>all</c> are taken in the cascade order together with the property's own. Each candidate looked
    /// at costs one unit, and whether it is rolled back is one look-up, so k <c>revert-layer</c> in k layers cost k, not
    /// k².</summary>
    private Candidate? Winner(List<Candidate>? candidates, List<Candidate>? all = null)
    {
        if (candidates is null && all is null)
        {
            return null;
        }

        // Most often the highest candidate wins: one look at each finds it. Only when it is refused or rolls back does
        // the cascade sort the candidates and walk them in order.
        var (ownHighest, sharedHighest) = (Highest(candidates), Highest(all));
        var highest = sharedHighest is null || (ownHighest is { } o && o.Priority.CompareTo(sharedHighest.Value.Priority) >= 0) ? ownHighest : sharedHighest;
        if (highest is { Entry.Value: not (RefusedEntry or WideKeywordEntry { Keyword: "revert" or "revert-layer" }) } winner && RollsBack(winner) is null)
        {
            return winner;
        }

        Sort(candidates);
        Sort(all);

        var reverted = 0;
        HashSet<(CascadeOrigin Origin, bool Important, object Layer)>? layers = null;
        (int Origin, int Attached, int Layer)? floor = null;
        int own = 0, shared = 0;
        while (true)
        {
            Candidate candidate;
            var hasOwn = candidates is not null && own < candidates.Count;
            var hasShared = all is not null && shared < all.Count;
            if (!hasOwn && !hasShared)
            {
                return null;
            }

            // Both lists are sorted highest first; the higher of their heads is next (the property's own on a tie, which
            // cannot happen: two declarations never share their order).
            candidate = hasOwn && (!hasShared || candidates![own].Priority.CompareTo(all![shared].Priority) >= 0) ? candidates![own++] : all![shared++];
            if (!_budget.Charge(1))
            {
                return null;
            }

            if ((reverted & (1 << RevertGroup(candidate.Origin))) != 0
                || (layers is not null && layers.Contains((candidate.Origin, candidate.Entry.Declaration.Important, candidate.Layer)))
                || (floor is { } level && Level(candidate.Priority).CompareTo(level) >= 0))
            {
                continue;
            }

            switch (candidate.Entry.Value is WideKeywordEntry ? candidate.Entry.Value : RollsBack(candidate) is { } keyword ? new WideKeywordEntry(keyword) : candidate.Entry.Value)
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
                    reverted |= 1 << RevertGroup(candidate.Origin);
                    continue;
                case WideKeywordEntry { Keyword: "revert-layer" } when candidate.Entry.Declaration.Important && !ReferenceEquals(candidate.Layer, StyleAttributeLayer):
                    // The layer's normal level, the lowest the roll-back reaches: an important layer's Layer is minus its rank.
                    var normal = (Priority.OriginRank(candidate.Origin, important: false), 0, -candidate.Priority.Layer);
                    floor = floor is { } lower && lower.CompareTo(normal) < 0 ? lower : normal;
                    continue;
                case WideKeywordEntry { Keyword: "revert-layer" }:
                    layers ??= new(new LayerKeyComparer());
                    layers.Add((candidate.Origin, candidate.Entry.Declaration.Important, candidate.Layer));
                    if (candidate.Entry.Declaration.Important)
                    {
                        layers.Add((candidate.Origin, false, candidate.Layer));
                    }

                    continue;
                default:
                    return candidate;
            }
        }
    }

    /// <summary>Where a candidate stands in the cascade above specificity and order: origin and importance, element
    /// attachment, layer (CSS Cascade 5 §6.1).</summary>
    private static (int Origin, int Attached, int Layer) Level(Priority priority) => (priority.Origin, priority.Attached, priority.Layer);

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
    /// substituted in dependency order (CSS Variables 1 §2.3). The order and the cycles come from an iterative walk over
    /// the references substitution needs (<see cref="Resolve"/>), so a chain of any length costs its length and no stack;
    /// a property in a cycle is refused, and it and the values whose substitution fails have the guaranteed-invalid value
    /// (they are absent). The tables are immutable maps that share their structure: an
    /// element that declares none shares its parent's table, one that declares a few costs those few, and elements with
    /// the same parent table and the same winning declarations share one table, so the substitutions of their
    /// declarations can be remembered (<see cref="_substituted"/>).</summary>
    private ImmutableDictionary<string, CssTokens> CustomProperties(Dictionary<string, List<Candidate>> declared, ComputedStyle? parent)
    {
        var inherited = parent?.CustomProperties as ImmutableDictionary<string, CssTokens> ?? NoCustomProperties;
        if (declared.Count == 0)
        {
            return inherited;
        }

        var names = new List<string>(declared.Count);
        var winners = new PreparedDeclaration?[declared.Count];
        var entries = new EntryValue?[declared.Count];
        foreach (var (name, candidates) in declared)
        {
            var winner = Winner(candidates);
            winners[names.Count] = winner?.Entry.Declaration;
            entries[names.Count] = winner?.Entry.Value;
            names.Add(name);
        }

        _budget.Charge(declared.Count);
        var key = ((object)inherited, winners);
        if (_customTables.TryGetValue(key, out var shared))
        {
            return shared;
        }

        var result = inherited.ToBuilder();
        var pending = new Dictionary<string, (CustomEntry Value, PreparedDeclaration Declaration)>(StringComparer.Ordinal);
        for (var i = 0; i < names.Count; i++)
        {
            var name = names[i];
            switch (entries[i])
            {
                case null:
                case WideKeywordEntry { Keyword: "inherit" or "unset" }:
                    // Custom properties are inherited (§2): unset and a rolled-back revert keep the parent's value.
                    break;
                case WideKeywordEntry:
                    result.Remove(name);
                    break;
                case CustomEntry { HasVar: false } value:
                    if (!inherited.TryGetValue(name, out var same) || same.Text != value.Text)
                    {
                        result[name] = new CssTokens(value.Values, value.Text);
                    }

                    break;
                case CustomEntry value:
                    pending[name] = (value, winners[i]!);
                    result.Remove(name);
                    break;
            }
        }

        Resolve(pending, result, inherited);

        // Immutable: a computed style's custom properties cannot change, also through a cast.
        var table = result.ToImmutable();
        if (_budget.Remember(1))
        {
            _customTables[key] = table;
        }

        return table;
    }

    private static readonly ImmutableDictionary<string, CssTokens> NoCustomProperties = ImmutableDictionary.Create<string, CssTokens>(StringComparer.Ordinal);

    /// <summary>
    /// Substitutes the pending custom properties of an element in an order where each comes after those it needs (CSS
    /// Variables 1 §2.3), without recursion. What a property needs is found as substitution needs it: the property each
    /// var() names, and the properties in a fallback only once the var()'s own property is known to have no value (a
    /// fallback that is not used makes no dependency, as in Chromium 159: '--a: a1 var(--b, var(--c)); --b: b1; --c: c1
    /// var(--a)' gives '--a' the value 'a1 b1', measured). A property found again while it waits for what it needs is
    /// in a cycle: CSS Variables 1 makes every member invalid, Chromium 159 does not always (measured), so a cycle is
    /// refused and its property left without a value. Each look at a value is charged to the work limit; a property is
    /// looked at once more for each level of fallbacks that turns out to be used.
    /// </summary>
    private void Resolve(Dictionary<string, (CustomEntry Value, PreparedDeclaration Declaration)> pending, ImmutableDictionary<string, CssTokens>.Builder result, ImmutableDictionary<string, CssTokens> inherited)
    {
        var state = new Dictionary<string, bool>(StringComparer.Ordinal);  // false: waiting for what it needs; true: done
        var waiting = new Stack<string>();
        var needed = new List<string>();
        var neededSet = new HashSet<string>(StringComparer.Ordinal);
        foreach (var start in pending.Keys)
        {
            if (state.ContainsKey(start))
            {
                continue;
            }

            waiting.Push(start);
            while (waiting.Count > 0 && _failure is null && !_budget.Exhausted)
            {
                var name = waiting.Peek();
                state[name] = false;
                var (entry, declaration) = pending[name];
                needed.Clear();
                neededSet.Clear();
                var cycle = !Needs(entry.Values, pending, state, result, needed, neededSet);
                if (cycle)
                {
                    waiting.Pop();
                    state[name] = true;
                    result.Remove(name);
                    Report(declaration, CssErrorCodes.Unsupported.Value, $"The custom property '{name}' is part of a cycle of var() references; Chromium does not resolve every such cycle as CSS Variables 1 says, so it is refused and has no value.");
                    continue;
                }

                if (needed.Count > 0)
                {
                    for (var i = needed.Count - 1; i >= 0; i--)
                    {
                        waiting.Push(needed[i]);
                    }

                    continue;
                }

                waiting.Pop();
                state[name] = true;
                var outcome = Substitute(entry.Values, result, declaration, out var substituted);
                if (outcome != VarSubstitution.Outcome.Done)
                {
                    Failed(declaration, outcome);
                    continue;
                }

                // A CSS-wide keyword that substitution makes is that keyword (CSS Variables 1 §3; Chromium 159,
                // measured: 'var(--nope, initial)' makes the property guaranteed-invalid, 'var(--e) inherit' with --e
                // empty inherits).
                switch (DeclarationPreparer.WideKeyword(substituted))
                {
                    case null:
                        result[name] = new CssTokens(substituted, CssSerializer.Serialize(substituted));
                        break;
                    case "inherit" or "unset":
                        if (inherited.TryGetValue(name, out var parentValue))
                        {
                            result[name] = parentValue;
                        }

                        break;
                    case "initial":
                        break;
                    default:
                        Report(declaration, CssErrorCodes.Unsupported.Value, $"'{name}' becomes '{DeclarationPreparer.WideKeyword(substituted)}' by var() substitution, which rolls the custom property back in the cascade; that is not supported, and it has no value.");
                        break;
                }
            }
        }
    }

    /// <summary>The pending properties a value needs before it can be substituted, in order (see <see cref="Resolve"/>);
    /// false when it needs one that is itself waiting: a cycle.</summary>
    private bool Needs(IReadOnlyList<ComponentValue> values, Dictionary<string, (CustomEntry Value, PreparedDeclaration Declaration)> pending, Dictionary<string, bool> state, ImmutableDictionary<string, CssTokens>.Builder result, List<string> needed, HashSet<string> neededSet)
    {
        var lists = new Stack<IReadOnlyList<ComponentValue>>();
        lists.Push(values);
        while (lists.Count > 0)
        {
            var list = lists.Pop();
            _budget.Charge(1 + list.Count);
            foreach (var value in list)
            {
                switch (value)
                {
                    case FunctionValue function when function.IsNamed("var") && VarSubstitution.ArgumentsOf(function) is var (name, fallback):
                        if (pending.ContainsKey(name) && state.TryGetValue(name, out var done) && !done)
                        {
                            return false;
                        }

                        if (pending.ContainsKey(name) && !state.ContainsKey(name))
                        {
                            if (neededSet.Add(name))
                            {
                                needed.Add(name);
                            }

                            break;
                        }

                        // Known: the fallback is used only when the property has no value.
                        if (!result.ContainsKey(name) && fallback is not null)
                        {
                            lists.Push(fallback);
                        }

                        break;
                    case FunctionValue function:
                        lists.Push(function.Values);
                        break;
                    case SimpleBlock block:
                        lists.Push(block.Values);
                        break;
                }
            }
        }

        return true;
    }

    /// <summary>A substitution, remembered per declaration and custom-property table (tables are shared, see
    /// <see cref="CustomProperties"/>), and charged to the cascade's substitution work; past that limit the cascade
    /// stops.</summary>
    private VarSubstitution.Outcome Substitute(IReadOnlyList<ComponentValue> values, IReadOnlyDictionary<string, CssTokens> custom, PreparedDeclaration declaration, out ValueList<ComponentValue> result)
    {
        var key = (declaration, (object)custom);
        if (_substituted.TryGetValue(key, out var known))
        {
            result = known.Values;
            return known.Outcome;
        }

        var outcome = VarSubstitution.Substitute(values, name => custom.TryGetValue(name, out var value) ? value : null, out result, out var length);
        if (!_budget.Substitute(length))
        {
            Fail(new Error(CssErrorCodes.TooLarge, $"The var() substitutions of the document make more than {_budget.MaxSubstitution} characters of text together.")
                .With(ErrorMetadataKeys.Value, _budget.MaxSubstitution));
            result = [];
            return VarSubstitution.Outcome.Uncertain;
        }

        if (_budget.Remember(1))
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
                Fail(DeclarationPreparer.Diagnostic(CssErrorCodes.TooDeep, $"The var() substitution in '{declaration.Name}' nests blocks and functions deeper than {CssInputLimits.MaxDepth}.", declaration)
                    .With(ErrorMetadataKeys.Value, CssInputLimits.MaxDepth));
                break;
            case VarSubstitution.Outcome.Uncertain when _failure is not null:
                break;
            case VarSubstitution.Outcome.Uncertain:
                Report(declaration, CssErrorCodes.Unsupported.Value, $"The var() substitution in '{declaration.Name}' is within a few characters of {CssInputLimits.MaxSubstitutionLength}, Chromium's limit; whether Chromium keeps it cannot be told, so it is refused.");
                break;
            default:
                Report(declaration, CssErrorCodes.Invalid.ComputedValue, $"A var() in '{declaration.Name}' names a custom property with no value and has no fallback; the property is unset (CSS Variables 1 §3.1).");
                break;
        }
    }

    // ---- longhands (§4.4, §7) ----

    /// <summary>A longhand's computed value: the winner's value computed, the CSS-wide keywords applied, or the
    /// inherited or initial value when nothing is declared.</summary>
    private CssValue Value(PropertyDefinition property, List<Candidate>? candidates, List<Candidate>? all, ComputeContext context, IReadOnlyDictionary<string, CssTokens> custom, Dictionary<PreparedDeclaration, ShorthandParse> shorthands)
    {
        CssValue Unset() => property == PropertyRegistry.FontSize
            ? PropertyRegistry.InheritFontSize(context)
            : property.Inherited ? context.Inherited(property) : InitialOf(property, context);
        _declaredBy[property.Index] = null;
        if (Winner(candidates, all) is not { } winner)
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
                return Substituted(property, pending, declaration, winner.Origin, context, custom, shorthands) ?? Unset();
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
    /// declaration and element); invalid at computed-value time when it does not parse (CSS Variables 1 §3.1). A
    /// -internal- keyword the substitution gives an author's text-align is invalid there too, as it is written directly
    /// (<see cref="DeclarationPreparer"/>; Chromium 159, measured: <c>text-align: var(--k)</c> with
    /// <c>--k: -internal-ave-th-center</c> is start).</summary>
    private CssValue? Substituted(PropertyDefinition property, PendingEntry pending, PreparedDeclaration declaration, CascadeOrigin origin, ComputeContext context, IReadOnlyDictionary<string, CssTokens> custom, Dictionary<PreparedDeclaration, ShorthandParse> shorthands)
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

        if (origin != CascadeOrigin.UserAgent && declaration.Name == "text-align" && DeclarationPreparer.ContainsInternalKeyword(values))
        {
            Report(declaration, CssErrorCodes.Invalid.Value, $"A value starting with -internal- is for the user agent only; '{declaration.Name}' after var() substitution is invalid and the property is unset.");
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

    /// <summary>Compares the rolled-back layers of revert-layer: origin, importance and the layer's identity.</summary>
    private sealed class LayerKeyComparer : IEqualityComparer<(CascadeOrigin Origin, bool Important, object Layer)>
    {
        /// <inheritdoc />
        public bool Equals((CascadeOrigin Origin, bool Important, object Layer) x, (CascadeOrigin Origin, bool Important, object Layer) y) =>
            x.Origin == y.Origin && x.Important == y.Important && ReferenceEquals(x.Layer, y.Layer);

        /// <inheritdoc />
        public int GetHashCode((CascadeOrigin Origin, bool Important, object Layer) obj) =>
            HashCode.Combine(obj.Origin, obj.Important, System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj.Layer));
    }

    /// <summary>Compares custom-table keys: the parent's table and each winning declaration by identity.</summary>
    private sealed class CustomTableComparer : IEqualityComparer<(object Parent, PreparedDeclaration?[] Winners)>
    {
        /// <inheritdoc />
        public bool Equals((object Parent, PreparedDeclaration?[] Winners) x, (object Parent, PreparedDeclaration?[] Winners) y)
        {
            if (!ReferenceEquals(x.Parent, y.Parent) || x.Winners.Length != y.Winners.Length)
            {
                return false;
            }

            for (var i = 0; i < x.Winners.Length; i++)
            {
                if (!ReferenceEquals(x.Winners[i], y.Winners[i]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <inheritdoc />
        public int GetHashCode((object Parent, PreparedDeclaration?[] Winners) obj)
        {
            var hash = new HashCode();
            hash.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj.Parent));
            foreach (var winner in obj.Winners)
            {
                hash.Add(winner is null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(winner));
            }

            return hash.ToHashCode();
        }
    }
}
