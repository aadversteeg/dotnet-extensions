using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Css.Syntax;

namespace Ave.Extensions.Documents.Css.Selectors;

/// <summary>A selector list (Selectors 4 §4.1): it matches an element when one of its complex selectors does.</summary>
/// <param name="Selectors">The complex selectors, in source order.</param>
public sealed record SelectorList(ValueList<ComplexSelector> Selectors);

/// <summary>
/// A complex selector (Selectors 4 §3.1): compound selectors joined by combinators, read left to right. In a nested
/// style rule it may start with a combinator (a relative selector, CSS Nesting 1 §2).
/// </summary>
/// <param name="Steps">The compound selectors, leftmost first, each with the combinator to its left (none for the first
/// one, unless the selector is relative).</param>
/// <param name="Position">Where it starts.</param>
public sealed record ComplexSelector(ValueList<SelectorStep> Steps, TextPosition Position)
{
    /// <summary>The selector's specificity (Selectors 4 §15).</summary>
    public Specificity Specificity => Specificity.Of(this);
}

/// <summary>One compound selector of a complex selector, with the combinator that relates it to the one on its left.</summary>
/// <param name="Combinator">The combinator to the left; null for the first step of an absolute selector.</param>
/// <param name="Compound">The compound selector.</param>
public sealed record SelectorStep(Combinator? Combinator, CompoundSelector Compound);

/// <summary>The combinators (Selectors 4 §16, §14).</summary>
public enum Combinator
{
    /// <summary>White space: a descendant.</summary>
    Descendant,

    /// <summary><c>&gt;</c>: a child.</summary>
    Child,

    /// <summary><c>+</c>: the next sibling.</summary>
    NextSibling,

    /// <summary><c>~</c>: a later sibling.</summary>
    SubsequentSibling,
}

/// <summary>A compound selector (Selectors 4 §3.1): simple selectors that all match the same element, and at the end
/// optionally a pseudo-element with the pseudo-classes after it.</summary>
/// <param name="Selectors">The simple selectors in source order.</param>
public sealed record CompoundSelector(ValueList<SimpleSelector> Selectors);

/// <summary>
/// A simple selector (Selectors 4 §3.1): type, universal, ID, class, attribute, pseudo-class, pseudo-element, or the
/// nesting selector <c>&amp;</c> (CSS Nesting 1 §3).
/// </summary>
public abstract record SimpleSelector;

/// <summary>A type selector or the universal selector (Selectors 4 §5).</summary>
/// <param name="LocalName">The element name as written, or <c>*</c> for the universal selector.</param>
/// <param name="Namespace">The namespace prefix part: null when none was written (the default namespace, which is
/// "any" since <c>@namespace</c> is not supported), <c>*</c> for any namespace, the empty string for no namespace.</param>
public sealed record TypeSelector(string LocalName, string? Namespace) : SimpleSelector
{
    /// <summary>Whether this is the universal selector.</summary>
    public bool IsUniversal => LocalName == "*";
}

/// <summary>An ID selector (Selectors 4 §6.7).</summary>
/// <param name="Name">The ID.</param>
public sealed record IdSelector(string Name) : SimpleSelector;

/// <summary>A class selector (Selectors 4 §6.6).</summary>
/// <param name="Name">The class.</param>
public sealed record ClassSelector(string Name) : SimpleSelector;

/// <summary>An attribute selector (Selectors 4 §6.1–§6.3).</summary>
/// <param name="LocalName">The attribute name as written.</param>
/// <param name="Namespace">The namespace prefix part, as for <see cref="TypeSelector"/>.</param>
/// <param name="Operator">How the value is compared.</param>
/// <param name="Value">The value to compare with; null for <see cref="AttributeOperator.Exists"/>.</param>
/// <param name="Case">The <c>i</c> or <c>s</c> modifier, or none.</param>
public sealed record AttributeSelector(string LocalName, string? Namespace, AttributeOperator Operator, string? Value, AttributeCase Case) : SimpleSelector;

/// <summary>The attribute selector operators (Selectors 4 §6.1, §6.2).</summary>
public enum AttributeOperator
{
    /// <summary><c>[att]</c>.</summary>
    Exists,

    /// <summary><c>[att=val]</c>.</summary>
    Equals,

    /// <summary><c>[att~=val]</c>: one of the white-space-separated words.</summary>
    Includes,

    /// <summary><c>[att|=val]</c>: the value, or the value followed by a hyphen.</summary>
    DashMatch,

    /// <summary><c>[att^=val]</c>: starts with.</summary>
    Prefix,

    /// <summary><c>[att$=val]</c>: ends with.</summary>
    Suffix,

    /// <summary><c>[att*=val]</c>: contains.</summary>
    Substring,
}

/// <summary>The case modifier of an attribute selector (Selectors 4 §6.3).</summary>
public enum AttributeCase
{
    /// <summary>No modifier: the document language decides (in HTML, some attributes' values are ASCII
    /// case-insensitive, <see cref="IStyleElement.IsCaseInsensitiveAttributeValue"/>).</summary>
    Default,

    /// <summary><c>i</c>: ASCII case-insensitive.</summary>
    Insensitive,

    /// <summary><c>s</c>: case-sensitive.</summary>
    Sensitive,
}

/// <summary>A pseudo-class (Selectors 4 §3.5), with its argument for a functional one.</summary>
/// <param name="Name">The name, lowercase.</param>
/// <param name="Support">What the engine does with it (<see cref="PseudoClasses"/>).</param>
public sealed record PseudoClassSelector(string Name, SelectorSupport Support) : SimpleSelector
{
    /// <summary>The selector list argument of <c>:is()</c>, <c>:where()</c>, <c>:not()</c>, <c>:has()</c>, and the
    /// <c>of S</c> part of <c>:nth-child()</c> and <c>:nth-last-child()</c>; null otherwise.</summary>
    public SelectorList? Selectors { get; init; }

    /// <summary>The An+B argument of the <c>:nth-*</c> pseudo-classes; null otherwise.</summary>
    public AnPlusB? Nth { get; init; }

    /// <summary>The raw argument of another functional pseudo-class (<c>:lang()</c>, <c>:dir()</c> …); empty otherwise.</summary>
    public ValueList<ComponentValue> Arguments { get; init; }
}

/// <summary>A pseudo-element (Selectors 4 §3.6). None is supported in this version: a rule with one is refused, or never
/// matches when the caller asked for warnings only.</summary>
/// <param name="Name">The name, lowercase.</param>
/// <param name="Arguments">The arguments of a functional pseudo-element.</param>
public sealed record PseudoElementSelector(string Name, ValueList<ComponentValue> Arguments) : SimpleSelector;

/// <summary>The nesting selector <c>&amp;</c> (CSS Nesting 1 §3): the parent rule's selector list, as by
/// <c>:is()</c>. Replaced when the rule builder desugars nested rules; at the top level it is <c>:scope</c>, which is the
/// root element in a style sheet.</summary>
public sealed record NestingSelector : SimpleSelector;

/// <summary>An An+B value (CSS Syntax 3 §6): the indexes A×n+B for n ≥ 0 that are positive.</summary>
/// <param name="A">The step.</param>
/// <param name="B">The offset.</param>
public readonly record struct AnPlusB(long A, long B)
{
    /// <summary>Whether a 1-based index is A×n+B for some integer n ≥ 0.</summary>
    /// <param name="index">The index, from 1.</param>
    /// <returns>Whether it is selected.</returns>
    public bool Matches(long index)
    {
        if (A == 0)
        {
            return index == B;
        }

        var difference = index - B;
        return difference % A == 0 && difference / A >= 0;
    }
}

/// <summary>What the engine does with a selector construct.</summary>
public enum SelectorSupport
{
    /// <summary>Implemented and matched as Selectors 4 says.</summary>
    Supported,

    /// <summary>Valid, and never true in a static document (<c>:hover</c>, <c>:focus</c>, <c>:visited</c> …): it is matched
    /// as false, as in a browser that prints a page nobody touches. Listed, not refused.</summary>
    NeverMatches,

    /// <summary>Valid but not implemented (<c>:has()</c>, <c>:lang()</c>, form states, pseudo-elements): refused by
    /// default; matched as false when the caller asked for warnings only.</summary>
    Unsupported,
}
