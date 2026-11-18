using System;
using System.Linq;

namespace Ave.Extensions.Documents.Css.Selectors;

/// <summary>
/// A selector's specificity (Selectors 4 §15): IDs (A), classes, attributes and pseudo-classes (B), types and
/// pseudo-elements (C), compared in that order.
/// </summary>
/// <param name="Ids">A.</param>
/// <param name="Classes">B.</param>
/// <param name="Types">C.</param>
public readonly record struct Specificity(int Ids, int Classes, int Types) : IComparable<Specificity>
{
    /// <summary>Zero, the specificity of <c>*</c> and of <c>:where()</c>.</summary>
    public static Specificity Zero => default;

    /// <summary>The sum of two specificities.</summary>
    public static Specificity operator +(Specificity left, Specificity right) =>
        new(left.Ids + right.Ids, left.Classes + right.Classes, left.Types + right.Types);

    /// <summary>Whether one specificity is less than another.</summary>
    public static bool operator <(Specificity left, Specificity right) => left.CompareTo(right) < 0;

    /// <summary>Whether one specificity is greater than another.</summary>
    public static bool operator >(Specificity left, Specificity right) => left.CompareTo(right) > 0;

    /// <summary>Whether one specificity is at most another.</summary>
    public static bool operator <=(Specificity left, Specificity right) => left.CompareTo(right) <= 0;

    /// <summary>Whether one specificity is at least another.</summary>
    public static bool operator >=(Specificity left, Specificity right) => left.CompareTo(right) >= 0;

    /// <inheritdoc/>
    public int CompareTo(Specificity other) =>
        Ids != other.Ids ? Ids.CompareTo(other.Ids) : Classes != other.Classes ? Classes.CompareTo(other.Classes) : Types.CompareTo(other.Types);

    /// <summary>Formats as <c>(a,b,c)</c>.</summary>
    public override string ToString() => $"({Ids},{Classes},{Types})";

    /// <summary>The specificity of a complex selector: the sum over its simple selectors.</summary>
    public static Specificity Of(ComplexSelector selector) =>
        selector.Steps.Aggregate(Zero, (sum, step) => step.Compound.Selectors.Aggregate(sum, (inner, simple) => inner + Of(simple)));

    /// <summary>The most specific complex selector of a list (§15: "the specificity of the most specific complex
    /// selector in its selector list argument").</summary>
    public static Specificity Max(SelectorList list) =>
        list.Selectors.Select(Of).DefaultIfEmpty(Zero).Max();

    private static Specificity Of(SimpleSelector simple) => simple switch
    {
        IdSelector => new(1, 0, 0),
        ClassSelector or AttributeSelector => new(0, 1, 0),
        TypeSelector type => type.IsUniversal ? Zero : new(0, 0, 1),
        PseudoElementSelector => new(0, 0, 1),
        PseudoClassSelector { Name: "where" } => Zero,
        PseudoClassSelector { Name: "is" or "not" or "has", Selectors: { } list } => Max(list),
        PseudoClassSelector { Name: "nth-child" or "nth-last-child", Selectors: { } of } => new Specificity(0, 1, 0) + Max(of),
        PseudoClassSelector => new(0, 1, 0),
        // The nesting selector is replaced by :is(parent) before anything uses specificity; at the top level it is
        // :scope, a pseudo-class.
        NestingSelector => new(0, 1, 0),
        _ => Zero,
    };
}
