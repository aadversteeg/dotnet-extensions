using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Css.Cascade;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Ave.Extensions.Documents.Css.Selectors;

/// <summary>
/// Matches selectors against elements (Selectors 4 §17.3). A construct that never matches in a static document, or that
/// is not supported, matches nothing (so <c>:not(:hover)</c> matches every element, as in a browser where nothing is
/// hovered).
/// </summary>
/// <remarks>
/// <para>Bounded work, and no recursion over the input's length. For a complex selector of steps 0..k three facts are
/// computed per step j and element x: M(j, x), "steps 0..j match with x as the subject of step j"; A(j, x) = M(j, x) or
/// A(j, parent of x), some ancestor-or-self matches; S(j, x) = M(j, x) or S(j, previous sibling of x). Then M(j, x) is
/// compound j on x and, by the combinator in front of step j: child M(j-1, parent), descendant A(j-1, parent), next
/// sibling M(j-1, previous), subsequent sibling S(j-1, previous). The facts are evaluated with an explicit stack and
/// remembered for the life of the matcher (one cascade), so every (fact, step, element) is computed once: matching is
/// linear in the selector's steps times the elements, also for <c>x ~ p ~ p</c> and long <c>p + p + … + p</c>
/// chains.</para>
/// <para>Recursion is left only where selectors nest (the argument of :is(), :where(), :not(), :nth-child(of S), and
/// the parent rule of a nested rule): its depth is at most <see cref="CssInputLimits.MaxSelectorNesting"/>, checked
/// where the selector is read.</para>
/// <para>Every fact, every simple selector tested, every 64 characters a test compares and every sibling counted is
/// charged to the cascade's work limit (<see cref="CssInputLimits.MaxCascadeWork"/>); past it <see cref="Exhausted"/>
/// is set, everything matches nothing, and the cascade stops. The facts and tables remembered count towards
/// <see cref="CssInputLimits.MaxCascadeMemo"/>; past that they are computed again, inside the work limit.</para>
/// <para>A chain of next-sibling combinators: a fact asked through <c>+</c> has one asker, so it is not remembered; a
/// run of steps that repeat one compound (<c>p + p + … + p</c>) is matched by the number of consecutive siblings that
/// match the compound, remembered per element, so it is linear. A chain of different compounds costs its steps times
/// the elements: matching a sequence of predicates against the siblings has no linear structure in general (an element
/// can match several compounds), so it is charged for what it costs.</para>
/// </remarks>
internal sealed class SelectorMatcher
{
    private readonly CascadeBudget _budget;

    private readonly Dictionary<(IStyleElement Parent, PseudoClassSelector Pseudo), Dictionary<IStyleElement, (int Index, int Count)>> _nthOf =
        new(new IdentityComparer<IStyleElement, PseudoClassSelector>());

    private readonly Dictionary<(IStyleElement Element, object Argument), bool> _arguments = new(new IdentityComparer<IStyleElement, object>());

    private readonly Dictionary<(ComplexSelector Selector, int Fact, IStyleElement Element), bool> _facts = new(new FactComparer());

    private readonly Dictionary<(IStyleElement Element, CompoundSelector Compound), int> _runs = new(new IdentityComparer<IStyleElement, CompoundSelector>());

    private readonly Dictionary<IStyleElement, IStyleElement[]> _siblings = new(ReferenceEqualityComparer.Instance);

    private readonly Stack<Goal> _goals = new();

    private readonly Dictionary<ComplexSelector, (int[] Needed, int[] RunStart)> _shapes = new(ReferenceEqualityComparer.Instance);

    /// <summary>Lowercase names of type and attribute selectors, computed once per selector.</summary>
    private readonly Dictionary<SimpleSelector, string> _lowered = new(ReferenceEqualityComparer.Instance);

    /// <summary>A matcher with the default limits, for one cascade.</summary>
    public SelectorMatcher()
        : this(new CascadeBudget())
    {
    }

    /// <summary>A matcher that charges a cascade's budget.</summary>
    public SelectorMatcher(CascadeBudget budget) => _budget = budget;

    /// <summary>Whether the work limit was reached: matching stopped, and the result of the cascade is incomplete.</summary>
    public bool Exhausted => _budget.Exhausted;

    /// <summary>Whether a selector list matches an element (one of its selectors does).</summary>
    public bool Matches(SelectorList list, IStyleElement element)
    {
        foreach (var selector in list.Selectors)
        {
            if (Matches(selector, element))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a complex selector matches an element.</summary>
    public bool Matches(ComplexSelector selector, IStyleElement element) =>
        Evaluate(selector, Kind.Match, selector.Steps.Count - 1, element);

    private enum Kind
    {
        Match,
        Ancestor,
        Sibling,
    }

    /// <summary>One fact to compute, and how far its computation is.</summary>
    private struct Goal(Kind kind, int step, IStyleElement element)
    {
        /// <summary>What fact is computed.</summary>
        public Kind Kind = kind;
        /// <summary>The compound of the selector, counted from the right, the fact is about.</summary>
        public int Step = step;
        /// <summary>The element the fact is about.</summary>
        public IStyleElement Element = element;
        /// <summary>0 before the goal is looked up or started; 1 once the facts it waits for are pushed.</summary>
        public int Phase;

        /// <summary>Whether the fact has one asker and is not remembered (asked through +).</summary>
        public bool Once;
    }

    private bool Charge(long work) => _budget.Charge(work);

    /// <summary>Computes one fact with the explicit stack; the facts it needs are pushed above it and their result is
    /// handed back in <c>result</c>.</summary>
    private bool Evaluate(ComplexSelector selector, Kind kind, int step, IStyleElement element)
    {
        var bottom = _goals.Count;
        _goals.Push(new Goal(kind, step, element));
        var result = false;
        var shape = Shape(selector);
        while (_goals.Count > bottom)
        {
            var goal = _goals.Pop();
            var key = (selector, (goal.Step * 3) + (int)goal.Kind, goal.Element);
            if (goal.Phase == 0 && !goal.Once && _facts.TryGetValue(key, out var known))
            {
                result = known;
                continue;
            }

            if (goal.Phase == 0 && !Charge(1))
            {
                result = false;
                continue;
            }

            bool? done = null;
            switch (goal.Kind)
            {
                case Kind.Match when goal.Phase == 0:
                    if (goal.Element.Index - 1 < shape.Needed[goal.Step])
                    {
                        done = false;
                        break;
                    }

                    // The step's compound, or for a run of steps that repeat one compound through +, that many
                    // consecutive siblings that match it; then the combinator in front of the run's first step.
                    var first = shape.RunStart[goal.Step];
                    var subject = goal.Element;
                    if (first < goal.Step)
                    {
                        var length = goal.Step - first + 1;
                        if (Run(selector.Steps[first].Compound, goal.Element) < length)
                        {
                            done = false;
                            break;
                        }

                        subject = SiblingBefore(goal.Element, length - 1);
                    }
                    else if (!MatchesCompound(selector.Steps[goal.Step].Compound, goal.Element))
                    {
                        done = false;
                        break;
                    }

                    if (first == 0)
                    {
                        // A relative selector's leading combinator relates it to the nesting parent, which the cascade
                        // made an explicit step in front; what is left is satisfied here.
                        done = true;
                        break;
                    }

                    var combinator = selector.Steps[first].Combinator;
                    var (needed, other) = combinator switch
                    {
                        Combinator.Child => (Kind.Match, subject.Parent),
                        Combinator.NextSibling => (Kind.Match, subject.PreviousSibling),
                        Combinator.SubsequentSibling => (Kind.Sibling, subject.PreviousSibling),
                        _ => (Kind.Ancestor, subject.Parent),
                    };
                    if (other is null)
                    {
                        done = false;
                        break;
                    }

                    goal.Phase = 1;
                    _goals.Push(goal);
                    // A fact asked through + has one asker (the element after it): remembering it would cost memory only.
                    _goals.Push(new Goal(needed, first - 1, other) { Once = combinator == Combinator.NextSibling });
                    continue;
                case Kind.Match:
                    done = result;
                    break;
                case Kind.Ancestor or Kind.Sibling when goal.Phase == 0:
                    goal.Phase = 1;
                    _goals.Push(goal);
                    _goals.Push(new Goal(Kind.Match, goal.Step, goal.Element));
                    continue;
                case Kind.Ancestor or Kind.Sibling when goal.Phase == 1:
                    var next = goal.Kind == Kind.Ancestor ? goal.Element.Parent : goal.Element.PreviousSibling;
                    if (result || next is null)
                    {
                        done = result;
                        break;
                    }

                    goal.Phase = 2;
                    _goals.Push(goal);
                    _goals.Push(new Goal(goal.Kind, goal.Step, next));
                    continue;
                default:
                    done = result;
                    break;
            }

            result = done!.Value && !Exhausted;

            // Only the facts other facts need are kept: the fact a caller asks for is asked once per (selector,
            // element), so keeping it would cost memory and nothing else.
            if (_goals.Count > bottom && !goal.Once && _budget.Remember(1))
            {
                _facts[key] = result;
            }
        }

        return result;
    }

    /// <summary>For each step, the earlier siblings its subject must have (one per + or ~ between it and the nearest
    /// child or descendant combinator to its left: a subject with fewer fails at once); and the first step of the run of
    /// steps it ends that repeat one compound through + (itself when there is none).</summary>
    private (int[] Needed, int[] RunStart) Shape(ComplexSelector selector)
    {
        if (!_shapes.TryGetValue(selector, out var shape))
        {
            var steps = selector.Steps;
            shape = (new int[steps.Count], new int[steps.Count]);
            for (var step = 1; step < steps.Count; step++)
            {
                shape.Needed[step] = steps[step].Combinator is Combinator.NextSibling or Combinator.SubsequentSibling ? shape.Needed[step - 1] + 1 : 0;
            }

            for (var step = 0; step < steps.Count; step++)
            {
                shape.RunStart[step] = step > 0 && steps[step].Combinator == Combinator.NextSibling && steps[step].Compound == steps[step - 1].Compound
                    ? shape.RunStart[step - 1]
                    : step;
            }

            _shapes[selector] = shape;
        }

        return shape;
    }

    /// <summary>How many consecutive siblings, ending with the element, match a compound: remembered per (compound,
    /// element), so a run of equal steps costs one test of the compound per element.</summary>
    private int Run(CompoundSelector compound, IStyleElement element)
    {
        // Back to an element whose run is known, or that does not match, or the first; then forward, counting.
        var path = new List<IStyleElement>();
        var run = 0;
        for (var current = (IStyleElement?)element; current is not null; current = current.PreviousSibling)
        {
            if (_runs.TryGetValue((current, compound), out var known))
            {
                run = known;
                break;
            }

            if (!MatchesCompound(compound, current))
            {
                RememberRun(current, compound, 0);
                break;
            }

            path.Add(current);
        }

        for (var i = path.Count - 1; i >= 0; i--)
        {
            RememberRun(path[i], compound, ++run);
        }

        return run;
    }

    private void RememberRun(IStyleElement element, CompoundSelector compound, int run)
    {
        if (_budget.Remember(1))
        {
            _runs[(element, compound)] = run;
        }
    }

    /// <summary>The sibling <paramref name="back"/> places before an element (it has at least that many: the caller
    /// counted them): from the parent's table of children, made once per parent.</summary>
    private IStyleElement SiblingBefore(IStyleElement element, int back)
    {
        if (back == 0)
        {
            return element;
        }

        return Siblings(element)[element.Index - 1 - back];
    }

    /// <summary>An element and its siblings in order, made once per parent (remembered within the memo limit; past it
    /// made again, each time charged).</summary>
    private IStyleElement[] Siblings(IStyleElement element)
    {
        if (element.Parent is not { } parent)
        {
            return [element];
        }

        if (_siblings.TryGetValue(parent, out var known))
        {
            return known;
        }

        var siblings = new IStyleElement[element.SiblingCount];
        var at = element.Index - 1;
        for (var sibling = (IStyleElement?)element; sibling is not null && at >= 0; sibling = sibling.PreviousSibling)
        {
            siblings[at--] = sibling;
        }

        at = element.Index;
        for (var sibling = element.NextSibling; sibling is not null && at < siblings.Length; sibling = sibling.NextSibling)
        {
            siblings[at++] = sibling;
        }

        Charge(siblings.Length);
        if (_budget.Remember(siblings.Length))
        {
            _siblings[parent] = siblings;
        }

        return siblings;
    }

    private bool MatchesCompound(CompoundSelector compound, IStyleElement element)
    {
        foreach (var simple in compound.Selectors)
        {
            if (!Charge(1) || !MatchesSimple(simple, element))
            {
                return false;
            }
        }

        return true;
    }

    private bool MatchesSimple(SimpleSelector simple, IStyleElement element) => simple switch
    {
        TypeSelector type => MatchesType(type, element),
        IdSelector id => element.Id is { } value && Charge(Characters(value.Length == id.Name.Length ? value.Length : 0))
            && (element.IsQuirksMode ? InfraAscii.EqualsIgnoreCase(value, id.Name) : value == id.Name),
        // The set lookup hashes the name.
        ClassSelector name => Charge(Characters(name.Name.Length)) && element.HasClass(name.Name),
        AttributeSelector attribute => MatchesAttribute(attribute, element),
        PseudoClassSelector pseudo => MatchesPseudoClass(pseudo, element),
        // & in a nested rule: the parent rule's selector list (CSS Nesting 1 §3), remembered per element.
        ParentSelector parent => MatchesList(parent, parent.Parent, element),
        // At the top level, & is :scope: in a style sheet, the root element (CSS Nesting 1 §3; Selectors 4 §13.6).
        NestingSelector => element.IsRoot,
        _ => false,
    };

    private static long Characters(long count) => CascadeBudget.CharactersCost(count);

    /// <summary>A selector's name in ASCII lowercase, made once per selector.</summary>
    private string Lowered(SimpleSelector selector, string name)
    {
        if (!_lowered.TryGetValue(selector, out var lowered))
        {
            lowered = InfraAscii.ToLower(name);
            _lowered[selector] = lowered;
        }

        return lowered;
    }

    /// <summary>Selectors 4 §5.1: for an HTML element the name matches ASCII case-insensitively; an element always has a
    /// namespace, so "no namespace" (<c>|E</c>) matches nothing.</summary>
    private bool MatchesType(TypeSelector type, IStyleElement element)
    {
        if (type.Namespace is "")
        {
            return false;
        }

        if (type.IsUniversal)
        {
            return true;
        }

        var name = element.IsHtml ? Lowered(type, type.LocalName) : type.LocalName;
        return name.Length == element.LocalName.Length && Charge(Characters(name.Length)) && name == element.LocalName;
    }

    /// <summary>Selectors 4 §6.1–§6.3. Charged for the characters each test compares: the name looked up, and the value
    /// compared (both lengths for a substring search).</summary>
    private bool MatchesAttribute(AttributeSelector selector, IStyleElement element)
    {
        var name = element.IsHtml ? Lowered(selector, selector.LocalName) : selector.LocalName;
        if (!Charge(Characters(name.Length)) || element.GetAttribute(name) is not { } value)
        {
            return false;
        }

        if (selector.Operator == AttributeOperator.Exists)
        {
            return true;
        }

        var wanted = selector.Value!;
        var ignoreCase = selector.Case switch
        {
            AttributeCase.Insensitive => true,
            _ => element.IsHtml && element.IsCaseInsensitiveAttributeValue(name),
        };
        return selector.Operator switch
        {
            AttributeOperator.Equals => value.Length == wanted.Length && Charge(Characters(value.Length)) && Same(value, wanted, ignoreCase),
            // The words of the value are split once per element and attribute (IStyleElement.AttributeHasWord).
            AttributeOperator.Includes => wanted.Length > 0 && Charge(Characters(2 * wanted.Length)) && !ContainsWhitespace(wanted) && element.AttributeHasWord(name, wanted, ignoreCase),
            AttributeOperator.DashMatch => value.Length >= wanted.Length && Charge(Characters(wanted.Length))
                && (value.Length == wanted.Length ? Same(value, wanted, ignoreCase)
                    : value.Length > wanted.Length && value[wanted.Length] == '-' && Same(value.AsSpan(0, wanted.Length), wanted, ignoreCase)),
            AttributeOperator.Prefix => wanted.Length > 0 && value.Length >= wanted.Length && Charge(Characters(wanted.Length)) && Same(value.AsSpan(0, wanted.Length), wanted, ignoreCase),
            AttributeOperator.Suffix => wanted.Length > 0 && value.Length >= wanted.Length && Charge(Characters(wanted.Length)) && Same(value.AsSpan(value.Length - wanted.Length), wanted, ignoreCase),
            AttributeOperator.Substring => wanted.Length > 0 && value.Length >= wanted.Length && Charge(Characters((long)value.Length * wanted.Length)) && Contains(value, wanted, ignoreCase),
            _ => false,
        };
    }

    /// <summary>Ordinal equality, or ASCII case-insensitive (Infra §4.6: only A–Z fold), of two texts of one length,
    /// without a copy.</summary>
    private static bool Same(ReadOnlySpan<char> value, ReadOnlySpan<char> wanted, bool ignoreCase)
    {
        if (!ignoreCase)
        {
            return value.SequenceEqual(wanted);
        }

        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != wanted[i] && !((value[i] ^ wanted[i]) == 0x20 && char.IsAsciiLetter(value[i])))
            {
                return false;
            }
        }

        return true;
    }

    private static bool Contains(string value, string wanted, bool ignoreCase)
    {
        if (!ignoreCase)
        {
            return value.Contains(wanted, StringComparison.Ordinal);
        }

        for (var start = 0; start + wanted.Length <= value.Length; start++)
        {
            if (Same(value.AsSpan(start, wanted.Length), wanted, ignoreCase: true))
            {
                return true;
            }
        }

        return false;
    }

    private static readonly char[] Whitespace = [' ', '\t', '\n', '\f', '\r'];

    private static bool ContainsWhitespace(string text) => text.IndexOfAny(Whitespace) >= 0;

    private bool MatchesPseudoClass(PseudoClassSelector pseudo, IStyleElement element)
    {
        if (pseudo.Support != SelectorSupport.Supported)
        {
            return false;
        }

        return pseudo.Name switch
        {
            "root" => element.IsRoot,
            "empty" => element.IsEmpty,
            "first-child" => element.Index == 1,
            "last-child" => element.Index == element.SiblingCount,
            "only-child" => element.SiblingCount == 1,
            "first-of-type" => element.IndexOfType == 1,
            "last-of-type" => element.IndexOfType == element.CountOfType,
            "only-of-type" => element.CountOfType == 1,
            "nth-child" => NthChild(pseudo, element, fromEnd: false),
            "nth-last-child" => NthChild(pseudo, element, fromEnd: true),
            "nth-of-type" => pseudo.Nth!.Value.Matches(element.IndexOfType),
            "nth-last-of-type" => pseudo.Nth!.Value.Matches(element.CountOfType - element.IndexOfType + 1),
            "not" => !MatchesArgument(pseudo, element),
            "is" or "where" => MatchesArgument(pseudo, element),
            "link" or "any-link" or "-webkit-any-link" => element.IsLink,
            _ => false,
        };
    }

    /// <summary>Whether the selector list argument of :is(), :where() or :not() matches; remembered per element for the
    /// life of the matcher, so the :is() chains that nesting desugars into (CSS Nesting 1 §3) cost one match per level
    /// and element rather than one per path.</summary>
    private bool MatchesArgument(PseudoClassSelector pseudo, IStyleElement element) => Matches(pseudo.Selectors!, element);

    /// <summary>Whether a parent rule's selector list matches, remembered per (parent reference, element) for the life
    /// of the matcher: every rule nested in it asks again, so nested rules cost one match per level and element. (An
    /// :is() argument needs no such memo: the fact of the step it is in is remembered when another step needs it.)</summary>
    private bool MatchesList(object owner, SelectorList list, IStyleElement element)
    {
        if (!_arguments.TryGetValue((element, owner), out var result))
        {
            result = Matches(list, element);
            if (!Exhausted && _budget.Remember(1))
            {
                _arguments[(element, owner)] = result;
            }
        }

        return result;
    }

    /// <summary>:nth-child() and :nth-last-child() (Selectors 4 §13.4.1, §13.4.2): with <c>of S</c>, the element must
    /// match S and is counted among the siblings that match S.</summary>
    private bool NthChild(PseudoClassSelector pseudo, IStyleElement element, bool fromEnd)
    {
        var nth = pseudo.Nth!.Value;
        if (pseudo.Selectors is not { } of)
        {
            return nth.Matches(fromEnd ? element.SiblingCount - element.Index + 1 : element.Index);
        }

        if (!Matches(of, element))
        {
            return false;
        }

        return IndexAmongMatching(pseudo, of, element) is var (index, count) && index > 0
            && nth.Matches(fromEnd ? count - index + 1 : index);
    }

    /// <summary>The 1-based index of an element among its siblings that match S, and their number; computed once for all
    /// siblings of a parent and remembered within the memo limit (one entry per sibling; past the limit counted again,
    /// charged one unit per sibling). (0, 0) when the element is not among them: matching was cut off by the work limit
    /// while the siblings were counted, and from there on nothing matches.</summary>
    private (int Index, int Count) IndexAmongMatching(PseudoClassSelector pseudo, SelectorList of, IStyleElement element)
    {
        if (element.Parent is not { } parent || element.SiblingCount == 1)
        {
            // The only child, which matches S (the caller checked): first of one.
            return (1, 1);
        }

        var key = (parent, pseudo);
        if (!_nthOf.TryGetValue(key, out var indexes))
        {
            var matching = new List<IStyleElement>();
            foreach (var sibling in Siblings(element))
            {
                if (!Charge(1))
                {
                    return (0, 0);
                }

                if (Matches(of, sibling))
                {
                    matching.Add(sibling);
                }
            }

            if (Exhausted)
            {
                return (0, 0);
            }

            indexes = new Dictionary<IStyleElement, (int, int)>(ReferenceEqualityComparer.Instance);
            for (var i = 0; i < matching.Count; i++)
            {
                indexes[matching[i]] = (i + 1, matching.Count);
            }

            // A table costs about eight entries of its own besides one per sibling counted.
            if (_budget.Remember(8 + matching.Count))
            {
                _nthOf[key] = indexes;
            }
        }

        return indexes.TryGetValue(element, out var place) ? place : (0, 0);
    }

    /// <summary>Compares pairs by the identity of both parts.</summary>
    private sealed class IdentityComparer<TFirst, TSecond> : IEqualityComparer<(TFirst First, TSecond Second)>
        where TFirst : class
        where TSecond : class
    {
        /// <inheritdoc />
        public bool Equals((TFirst First, TSecond Second) x, (TFirst First, TSecond Second) y) =>
            ReferenceEquals(x.First, y.First) && ReferenceEquals(x.Second, y.Second);

        /// <inheritdoc />
        public int GetHashCode((TFirst First, TSecond Second) obj) =>
            HashCode.Combine(RuntimeHelpers.GetHashCode(obj.First), RuntimeHelpers.GetHashCode(obj.Second));
    }

    /// <summary>Compares fact keys by the identity of the selector and the element.</summary>
    private sealed class FactComparer : IEqualityComparer<(ComplexSelector Selector, int Fact, IStyleElement Element)>
    {
        /// <inheritdoc />
        public bool Equals((ComplexSelector Selector, int Fact, IStyleElement Element) x, (ComplexSelector Selector, int Fact, IStyleElement Element) y) =>
            x.Fact == y.Fact && ReferenceEquals(x.Selector, y.Selector) && ReferenceEquals(x.Element, y.Element);

        /// <inheritdoc />
        public int GetHashCode((ComplexSelector Selector, int Fact, IStyleElement Element) obj) =>
            HashCode.Combine(RuntimeHelpers.GetHashCode(obj.Selector), obj.Fact, RuntimeHelpers.GetHashCode(obj.Element));
    }
}
