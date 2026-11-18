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
/// <para>Every fact and every simple selector tested is charged to a work limit
/// (<see cref="CssInputLimits.MaxMatchingWork"/>); past it <see cref="Exhausted"/> is set, everything matches nothing,
/// and the cascade refuses the document. The facts remembered are capped at
/// <see cref="CssInputLimits.MaxMatchingMemo"/>; past that they are computed again, inside the work limit.</para>
/// </remarks>
public sealed class SelectorMatcher
{
    private readonly Dictionary<(IStyleElement Parent, PseudoClassSelector Pseudo), Dictionary<IStyleElement, (int Index, int Count)>> _nthOf =
        new(new IdentityComparer<IStyleElement, PseudoClassSelector>());

    private readonly Dictionary<(IStyleElement Element, object Argument), bool> _arguments = new(new IdentityComparer<IStyleElement, object>());

    private readonly Dictionary<(ComplexSelector Selector, int Fact, IStyleElement Element), bool> _facts = new(new FactComparer());

    private readonly Stack<Goal> _goals = new();

    private readonly Dictionary<ComplexSelector, int[]> _siblingsNeeded = new(ReferenceEqualityComparer.Instance);

    private long _work;

    /// <summary>Whether the work limit was reached: matching stopped, and the result of the cascade is incomplete.</summary>
    public bool Exhausted { get; private set; }

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
        public Kind Kind = kind;
        public int Step = step;
        public IStyleElement Element = element;
        public int Phase;
    }

    private bool Charge(long work)
    {
        _work += work;
        if (_work > CssInputLimits.MaxMatchingWork)
        {
            Exhausted = true;
        }

        return !Exhausted;
    }

    /// <summary>Computes one fact with the explicit stack; the facts it needs are pushed above it and their result is
    /// handed back in <c>result</c>.</summary>
    private bool Evaluate(ComplexSelector selector, Kind kind, int step, IStyleElement element)
    {
        var bottom = _goals.Count;
        _goals.Push(new Goal(kind, step, element));
        var result = false;
        while (_goals.Count > bottom)
        {
            var goal = _goals.Pop();
            var key = (selector, (goal.Step * 3) + (int)goal.Kind, goal.Element);
            if (goal.Phase == 0 && _facts.TryGetValue(key, out var known))
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
                    if (goal.Element.Index - 1 < SiblingsNeeded(selector)[goal.Step]
                        || !MatchesCompound(selector.Steps[goal.Step].Compound, goal.Element))
                    {
                        done = false;
                        break;
                    }

                    if (goal.Step == 0)
                    {
                        // A relative selector's leading combinator relates it to the nesting parent, which the cascade
                        // made an explicit step in front; what is left is satisfied here.
                        done = true;
                        break;
                    }

                    var (needed, other) = selector.Steps[goal.Step].Combinator switch
                    {
                        Combinator.Child => (Kind.Match, goal.Element.Parent),
                        Combinator.NextSibling => (Kind.Match, goal.Element.PreviousSibling),
                        Combinator.SubsequentSibling => (Kind.Sibling, goal.Element.PreviousSibling),
                        _ => (Kind.Ancestor, goal.Element.Parent),
                    };
                    if (other is null)
                    {
                        done = false;
                        break;
                    }

                    goal.Phase = 1;
                    _goals.Push(goal);
                    _goals.Push(new Goal(needed, goal.Step - 1, other));
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
            if (_goals.Count > bottom && _facts.Count < CssInputLimits.MaxMatchingMemo)
            {
                _facts[key] = result;
            }
        }

        return result;
    }

    /// <summary>For each step, the earlier siblings its subject must have: one per + or ~ between it and the nearest
    /// child or descendant combinator to its left. A subject with fewer fails at once, which keeps long chains of
    /// sibling combinators (<c>p + p + … + p</c>) linear.</summary>
    private int[] SiblingsNeeded(ComplexSelector selector)
    {
        if (!_siblingsNeeded.TryGetValue(selector, out var needed))
        {
            needed = new int[selector.Steps.Count];
            for (var step = 1; step < needed.Length; step++)
            {
                needed[step] = selector.Steps[step].Combinator is Combinator.NextSibling or Combinator.SubsequentSibling ? needed[step - 1] + 1 : 0;
            }

            _siblingsNeeded[selector] = needed;
        }

        return needed;
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
        IdSelector id => element.Id is { } value && (element.IsQuirksMode ? Ascii.EqualsIgnoreCase(value, id.Name) : value == id.Name),
        ClassSelector name => element.HasClass(name.Name),
        AttributeSelector attribute => MatchesAttribute(attribute, element),
        PseudoClassSelector pseudo => MatchesPseudoClass(pseudo, element),
        // & in a nested rule: the parent rule's selector list (CSS Nesting 1 §3), remembered per element.
        ParentSelector parent => MatchesList(parent, parent.Parent, element),
        // At the top level, & is :scope: in a style sheet, the root element (CSS Nesting 1 §3; Selectors 4 §13.6).
        NestingSelector => element.IsRoot,
        _ => false,
    };

    /// <summary>Selectors 4 §5.1: for an HTML element the name matches ASCII case-insensitively; an element always has a
    /// namespace, so "no namespace" (<c>|E</c>) matches nothing.</summary>
    private static bool MatchesType(TypeSelector type, IStyleElement element)
    {
        if (type.Namespace is "")
        {
            return false;
        }

        if (type.IsUniversal)
        {
            return true;
        }

        return element.IsHtml ? Ascii.ToLower(type.LocalName) == element.LocalName : type.LocalName == element.LocalName;
    }

    /// <summary>Selectors 4 §6.1–§6.3.</summary>
    private bool MatchesAttribute(AttributeSelector selector, IStyleElement element)
    {
        var name = element.IsHtml ? Ascii.ToLower(selector.LocalName) : selector.LocalName;
        if (element.GetAttribute(name) is not { } value)
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
        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (ignoreCase)
        {
            // ASCII case-insensitive (Infra §4.6): only A–Z fold, so compare the ASCII lowercase forms ordinally.
            (value, wanted, comparison) = (Ascii.ToLower(value), Ascii.ToLower(wanted), StringComparison.Ordinal);
        }

        return selector.Operator switch
        {
            AttributeOperator.Equals => string.Equals(value, wanted, comparison),
            // The words of the value are split once per element and attribute (IStyleElement.AttributeHasWord).
            AttributeOperator.Includes => wanted.Length > 0 && !ContainsWhitespace(wanted) && element.AttributeHasWord(name, wanted, ignoreCase),
            AttributeOperator.DashMatch => string.Equals(value, wanted, comparison) || value.StartsWith(wanted + "-", comparison),
            AttributeOperator.Prefix => wanted.Length > 0 && value.StartsWith(wanted, comparison),
            AttributeOperator.Suffix => wanted.Length > 0 && value.EndsWith(wanted, comparison),
            AttributeOperator.Substring => wanted.Length > 0 && Charge(value.Length / 64) && value.Contains(wanted, comparison),
            _ => false,
        };
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
            if (_arguments.Count < CssInputLimits.MaxMatchingMemo)
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

        var (index, count) = IndexAmongMatching(pseudo, of, element);
        return nth.Matches(fromEnd ? count - index + 1 : index);
    }

    /// <summary>The 1-based index of an element among its siblings that match S, and their number; computed once for all
    /// siblings of a parent.</summary>
    private (int Index, int Count) IndexAmongMatching(PseudoClassSelector pseudo, SelectorList of, IStyleElement element)
    {
        if (element.Parent is not { } parent)
        {
            return (1, 1);
        }

        var key = (parent, pseudo);
        if (!_nthOf.TryGetValue(key, out var indexes))
        {
            var first = element;
            while (first.PreviousSibling is { } previous)
            {
                first = previous;
            }

            var matching = new List<IStyleElement>();
            for (var sibling = (IStyleElement?)first; sibling is not null; sibling = sibling.NextSibling)
            {
                if (Matches(of, sibling))
                {
                    matching.Add(sibling);
                }
            }

            indexes = new Dictionary<IStyleElement, (int, int)>(ReferenceEqualityComparer.Instance);
            for (var i = 0; i < matching.Count; i++)
            {
                indexes[matching[i]] = (i + 1, matching.Count);
            }

            _nthOf[key] = indexes;
        }

        return indexes[element];
    }

    /// <summary>Compares pairs by the identity of both parts.</summary>
    private sealed class IdentityComparer<TFirst, TSecond> : IEqualityComparer<(TFirst First, TSecond Second)>
        where TFirst : class
        where TSecond : class
    {
        public bool Equals((TFirst First, TSecond Second) x, (TFirst First, TSecond Second) y) =>
            ReferenceEquals(x.First, y.First) && ReferenceEquals(x.Second, y.Second);

        public int GetHashCode((TFirst First, TSecond Second) obj) =>
            HashCode.Combine(RuntimeHelpers.GetHashCode(obj.First), RuntimeHelpers.GetHashCode(obj.Second));
    }

    /// <summary>Compares fact keys by the identity of the selector and the element.</summary>
    private sealed class FactComparer : IEqualityComparer<(ComplexSelector Selector, int Fact, IStyleElement Element)>
    {
        public bool Equals((ComplexSelector Selector, int Fact, IStyleElement Element) x, (ComplexSelector Selector, int Fact, IStyleElement Element) y) =>
            x.Fact == y.Fact && ReferenceEquals(x.Selector, y.Selector) && ReferenceEquals(x.Element, y.Element);

        public int GetHashCode((ComplexSelector Selector, int Fact, IStyleElement Element) obj) =>
            HashCode.Combine(RuntimeHelpers.GetHashCode(obj.Selector), obj.Fact, RuntimeHelpers.GetHashCode(obj.Element));
    }
}
