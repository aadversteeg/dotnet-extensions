using System;
using System.Collections.Generic;

namespace Ave.Extensions.Documents.Css.Selectors;

/// <summary>
/// Matches selectors against elements (Selectors 4 §17.3): a complex selector right to left, compound by compound. A
/// construct that never matches in a static document, or that is not supported, matches nothing (so <c>:not(:hover)</c>
/// matches every element, as in a browser where nothing is hovered).
/// </summary>
/// <remarks>
/// Bounded work: the descendant and subsequent-sibling combinators try every ancestor or earlier sibling, and the
/// result for each (step, element) pair is remembered for the call, so matching one complex selector costs at most its
/// steps times the elements it can reach. The <c>of S</c> counts of <c>:nth-child()</c> are computed once per parent
/// and selector, and the argument of <c>:is()</c>, <c>:where()</c> and <c>:not()</c> once per element, for the life of
/// the matcher (one cascade).
/// </remarks>
public sealed class SelectorMatcher
{
    private readonly Dictionary<(IStyleElement Parent, PseudoClassSelector Pseudo), Dictionary<IStyleElement, (int Index, int Count)>> _nthOf =
        new(new ParentPseudoComparer());

    private readonly Dictionary<(IStyleElement Element, PseudoClassSelector Pseudo), bool> _arguments = new(new ParentPseudoComparer());

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
        MatchesFrom(selector, selector.Steps.Count - 1, element, new Dictionary<(int, IStyleElement), bool>(new StepComparer()));

    /// <summary>Whether steps 0..<paramref name="step"/> match with <paramref name="element"/> as the subject of
    /// <paramref name="step"/>.</summary>
    private bool MatchesFrom(ComplexSelector selector, int step, IStyleElement element, Dictionary<(int, IStyleElement), bool> memo)
    {
        if (memo.TryGetValue((step, element), out var known))
        {
            return known;
        }

        var result = MatchesCompound(selector.Steps[step].Compound, element) && MatchesLeft(selector, step, element, memo);
        memo[(step, element)] = result;
        return result;
    }

    private bool MatchesLeft(ComplexSelector selector, int step, IStyleElement element, Dictionary<(int, IStyleElement), bool> memo)
    {
        var combinator = selector.Steps[step].Combinator;
        if (step == 0)
        {
            // A relative selector's leading combinator relates it to the nesting parent, which desugaring made explicit;
            // what is left is satisfied here.
            return true;
        }

        switch (combinator)
        {
            case Combinator.Child:
                return element.Parent is { } parent && MatchesFrom(selector, step - 1, parent, memo);
            case Combinator.NextSibling:
                return element.PreviousSibling is { } previous && MatchesFrom(selector, step - 1, previous, memo);
            case Combinator.SubsequentSibling:
                for (var sibling = element.PreviousSibling; sibling is not null; sibling = sibling.PreviousSibling)
                {
                    if (MatchesFrom(selector, step - 1, sibling, memo))
                    {
                        return true;
                    }
                }

                return false;
            default:
                for (var ancestor = element.Parent; ancestor is not null; ancestor = ancestor.Parent)
                {
                    if (MatchesFrom(selector, step - 1, ancestor, memo))
                    {
                        return true;
                    }
                }

                return false;
        }
    }

    private bool MatchesCompound(CompoundSelector compound, IStyleElement element)
    {
        foreach (var simple in compound.Selectors)
        {
            if (!MatchesSimple(simple, element))
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
    private static bool MatchesAttribute(AttributeSelector selector, IStyleElement element)
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
            AttributeCase.Sensitive => false,
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
            AttributeOperator.Includes => wanted.Length > 0 && !ContainsWhitespace(wanted) && Words(value).Contains(wanted),
            AttributeOperator.DashMatch => string.Equals(value, wanted, comparison) || value.StartsWith(wanted + "-", comparison),
            AttributeOperator.Prefix => wanted.Length > 0 && value.StartsWith(wanted, comparison),
            AttributeOperator.Suffix => wanted.Length > 0 && value.EndsWith(wanted, comparison),
            AttributeOperator.Substring => wanted.Length > 0 && value.Contains(wanted, comparison),
            _ => false,
        };
    }

    private static readonly char[] Whitespace = [' ', '\t', '\n', '\f', '\r'];

    private static bool ContainsWhitespace(string text) => text.IndexOfAny(Whitespace) >= 0;

    private static string[] Words(string value) => value.Split(Whitespace, StringSplitOptions.RemoveEmptyEntries);

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
    private bool MatchesArgument(PseudoClassSelector pseudo, IStyleElement element)
    {
        if (!_arguments.TryGetValue((element, pseudo), out var result))
        {
            result = Matches(pseudo.Selectors!, element);
            _arguments[(element, pseudo)] = result;
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

    /// <summary>Compares (step, element) keys by the element's identity.</summary>
    private sealed class StepComparer : IEqualityComparer<(int Step, IStyleElement Element)>
    {
        public bool Equals((int Step, IStyleElement Element) x, (int Step, IStyleElement Element) y) =>
            x.Step == y.Step && ReferenceEquals(x.Element, y.Element);

        public int GetHashCode((int Step, IStyleElement Element) obj) =>
            HashCode.Combine(obj.Step, System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj.Element));
    }

    /// <summary>Compares (parent, pseudo-class) keys by identity: two equal pseudo-classes in different places are
    /// counted separately, which is the same result.</summary>
    private sealed class ParentPseudoComparer : IEqualityComparer<(IStyleElement Parent, PseudoClassSelector Pseudo)>
    {
        public bool Equals((IStyleElement Parent, PseudoClassSelector Pseudo) x, (IStyleElement Parent, PseudoClassSelector Pseudo) y) =>
            ReferenceEquals(x.Parent, y.Parent) && ReferenceEquals(x.Pseudo, y.Pseudo);

        public int GetHashCode((IStyleElement Parent, PseudoClassSelector Pseudo) obj) =>
            HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj.Parent), System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj.Pseudo));
    }
}
