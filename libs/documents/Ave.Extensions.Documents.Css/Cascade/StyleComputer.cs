using System;
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
/// <para>Bounded work: each element is matched against the rules of its buckets
/// (<see cref="CascadeIndex"/>), and every value substitution has its limit
/// (<see cref="CssInputLimits.MaxSubstitutionValues"/>).</para>
/// </remarks>
public sealed class StyleComputer
{
    private readonly CascadeIndex _index;
    private readonly ValueContext _values;
    private readonly SelectorMatcher _matcher = new();
    private readonly List<Error> _diagnostics = [];
    private readonly HashSet<object> _reported = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<(PreparedDeclaration, ErrorCode)> _reportedAt = [];
    private readonly Dictionary<IStyleElement, ComputedStyle> _styles = new(ReferenceEqualityComparer.Instance);
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
    public ValueList<Error> Diagnostics => [.. _diagnostics];

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
    /// has none).</param>
    /// <param name="own">Its style attribute and presentational hints.</param>
    /// <returns>Its computed style.</returns>
    public ComputedStyle Compute(IStyleElement element, ElementDeclarations? own = null)
    {
        var parent = element.Parent is { } parentElement && _styles.TryGetValue(parentElement, out var parentStyle) ? parentStyle : null;
        var candidates = Collect(element, own ?? ElementDeclarations.None);
        var context = new ComputeContext(parent, _rootFontSize, _values, element.IsRoot);
        var custom = CustomProperties(candidates.Custom, parent);
        var shorthands = new Dictionary<PreparedDeclaration, ShorthandParse>();
        foreach (var property in ComputeOrder)
        {
            context.Set(property, Value(property, candidates.Longhands[property.Index], context, custom, shorthands));
        }

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
        public List<Candidate>[] Longhands { get; } = new List<Candidate>[PropertyRegistry.Longhands.Count];

        public Dictionary<string, List<Candidate>> Custom { get; } = new(StringComparer.Ordinal);

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

    private Candidates Collect(IStyleElement element, ElementDeclarations own)
    {
        var candidates = new Candidates();
        foreach (var rule in _index.Candidates(element))
        {
            if (!_matcher.Matches(rule.Selector, element))
            {
                continue;
            }

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

    /// <summary>The winning candidate, or null when none is left: refused values are reported and skipped;
    /// <c>revert</c> skips the rest of its origin (presentational hints count as author), <c>revert-layer</c> the rest of
    /// its layer.</summary>
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

    private static int RevertGroup(CascadeOrigin origin) => origin switch
    {
        CascadeOrigin.UserAgent => 0,
        CascadeOrigin.User => 1,
        _ => 2,
    };

    // ---- custom properties (CSS Variables 1 §2, §3) ----

    /// <summary>The computed custom properties: inherited from the parent, overridden by the element's own, with
    /// var() substituted in dependency order; the properties of a cycle (§2.3) and those whose substitution fails
    /// have the guaranteed-invalid value (they are absent).</summary>
    private Dictionary<string, CssTokens> CustomProperties(Dictionary<string, List<Candidate>> declared, ComputedStyle? parent)
    {
        var result = parent is null
            ? new Dictionary<string, CssTokens>(StringComparer.Ordinal)
            : new Dictionary<string, CssTokens>(parent.CustomProperties, StringComparer.Ordinal);
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
                    break;
            }
        }

        var state = new Dictionary<string, int>(StringComparer.Ordinal);
        var stack = new List<string>();
        var cyclic = new HashSet<string>(StringComparer.Ordinal);

        ValueList<ComponentValue>? Lookup(string name)
        {
            if (pending.ContainsKey(name))
            {
                switch (state.GetValueOrDefault(name))
                {
                    case 0:
                        Resolve(name);
                        break;
                    case 1:
                        for (var i = stack.IndexOf(name); i < stack.Count; i++)
                        {
                            cyclic.Add(stack[i]);
                        }

                        return null;
                }
            }

            return result.TryGetValue(name, out var value) ? value.Tokens : null;
        }

        void Resolve(string name)
        {
            state[name] = 1;
            stack.Add(name);
            var (value, declaration) = pending[name];
            var outcome = VarSubstitution.Substitute(value.Values, Lookup, out var substituted);
            stack.RemoveAt(stack.Count - 1);
            state[name] = 2;
            if (cyclic.Contains(name))
            {
                result.Remove(name);
                Report(declaration, CssErrorCodes.Invalid.ComputedValue, $"The custom property '{name}' refers to itself through var(); it has no value.");
            }
            else if (outcome == VarSubstitution.Outcome.Done)
            {
                result[name] = new CssTokens(substituted, CssSerializer.Serialize(substituted));
            }
            else
            {
                result.Remove(name);
                Failed(declaration, outcome);
            }
        }

        foreach (var name in pending.Keys)
        {
            if (state.GetValueOrDefault(name) == 0)
            {
                Resolve(name);
            }
        }

        return result;
    }

    private void Failed(PreparedDeclaration declaration, VarSubstitution.Outcome outcome)
    {
        if (outcome == VarSubstitution.Outcome.TooLarge)
        {
            Report(declaration, CssErrorCodes.TooLarge, $"The var() substitution in '{declaration.Name}' is longer than {CssInputLimits.MaxSubstitutionValues} values; the property is unset.");
        }
        else
        {
            Report(declaration, CssErrorCodes.Invalid.ComputedValue, $"A var() in '{declaration.Name}' names a custom property with no value and has no fallback; the property is unset (CSS Variables 1 §3.1).");
        }
    }

    // ---- longhands (§4.4, §7) ----

    /// <summary>A longhand's computed value: the winner's value computed, the CSS-wide keywords applied, or the
    /// inherited or initial value when nothing is declared.</summary>
    private CssValue Value(PropertyDefinition property, List<Candidate>? candidates, ComputeContext context, Dictionary<string, CssTokens> custom, Dictionary<PreparedDeclaration, ShorthandParse> shorthands)
    {
        CssValue Unset() => property == PropertyRegistry.FontSize
            ? PropertyRegistry.InheritFontSize(context)
            : property.Inherited ? context.Inherited(property) : InitialOf(property, context);
        if (Winner(candidates) is not { } winner)
        {
            return Unset();
        }

        var declaration = winner.Entry.Declaration;
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
    private CssValue? Substituted(PropertyDefinition property, PendingEntry pending, PreparedDeclaration declaration, ComputeContext context, Dictionary<string, CssTokens> custom, Dictionary<PreparedDeclaration, ShorthandParse> shorthands)
    {
        var outcome = VarSubstitution.Substitute(declaration.Source.Value, name => custom.TryGetValue(name, out var value) ? value.Tokens : null, out var values);
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
            Report(declaration, CssErrorCodes.Invalid.ComputedValue, "'all' takes only a CSS-wide keyword; after var() substitution it is not one, so every property is unset.");
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
            Report(declaration, CssErrorCodes.Invalid.ComputedValue, $"'{declaration.Name}' after var() substitution is invalid ({parsed.Problem}); the property is unset (CSS Variables 1 §3.1).");
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
}
