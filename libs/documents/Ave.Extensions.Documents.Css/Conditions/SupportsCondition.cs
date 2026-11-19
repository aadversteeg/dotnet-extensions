using System.Collections.Generic;
using System.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Css.Selectors;
using Ave.Extensions.Documents.Css.Syntax;

namespace Ave.Extensions.Documents.Css.Conditions;

/// <summary>A supports condition (CSS Conditional 3 §6, CSS Conditional 4 §2): declarations, <c>selector()</c>, and
/// <c>not</c>/<c>and</c>/<c>or</c>.</summary>
public abstract record SupportsCondition;

/// <summary><c>not (…)</c>.</summary>
/// <param name="Operand">The negated condition.</param>
public sealed record SupportsNot(SupportsCondition Operand) : SupportsCondition;

/// <summary><c>(…) and (…)</c>.</summary>
/// <param name="Operands">The conditions.</param>
public sealed record SupportsAnd(ValueList<SupportsCondition> Operands) : SupportsCondition;

/// <summary><c>(…) or (…)</c>.</summary>
/// <param name="Operands">The conditions.</param>
public sealed record SupportsOr(ValueList<SupportsCondition> Operands) : SupportsCondition;

/// <summary><c>(property: value)</c>: true when the engine supports the property with that value.</summary>
/// <param name="Declaration">The declaration.</param>
public sealed record SupportsDeclaration(Declaration Declaration) : SupportsCondition;

/// <summary><c>selector(…)</c> (CSS Conditional 4 §2.1): true when the selector parses and is supported.</summary>
/// <param name="Selector">The parse result.</param>
/// <param name="Position">Where it is.</param>
public sealed record SupportsSelector(SelectorParseResult Selector, TextPosition Position) : SupportsCondition;

/// <summary>Anything else in parentheses or a function (&lt;general-enclosed&gt;, and <c>font-tech()</c>,
/// <c>font-format()</c> of CSS Conditional 5): false.</summary>
/// <param name="Text">What it was.</param>
/// <param name="Position">Where it is.</param>
public sealed record SupportsOther(string Text, TextPosition Position) : SupportsCondition;

/// <summary>Parsing and evaluating supports conditions.</summary>
internal static class SupportsConditions
{
    /// <summary>Parses a supports condition (the prelude of <c>@supports</c>, or the argument of <c>supports()</c> in
    /// <c>@import</c>); null when it does not match the grammar, or when it holds, at any depth, a ), ] or } that closes
    /// nothing, a bad string or a bad URL: Chromium 159 drops such a rule whole, wherever the token is and however the
    /// tests are combined (measured: '(color: red) or (--x: ])', 'not ((-moz-a: b) and (--x: "a&#10;))', '(color:
    /// red) or foo(])' and 'selector(])' apply neither the rule nor its negation).</summary>
    /// <param name="values">The component values.</param>
    /// <returns>The condition, or null.</returns>
    public static SupportsCondition? Parse(IReadOnlyList<ComponentValue> values)
    {
        if (HasBrokenToken(values))
        {
            return null;
        }

        var tokens = new List<ComponentValue>();
        foreach (var value in values)
        {
            if (value is not CssToken { Kind: CssTokenKind.Whitespace })
            {
                tokens.Add(value);
            }
        }

        var index = 0;
        var condition = Condition(tokens, ref index);
        return condition is not null && index == tokens.Count ? condition : null;
    }

    /// <summary>Evaluates a condition as Chromium 159 would, or says it cannot: true, false, or null when one of its
    /// tests is one the engine cannot answer for Chromium (CSS Conditional 3 §6, in three-valued logic: not null is
    /// null; and is false when one operand is false, else null when one is null; or is true when one is true).
    /// <paramref name="declaration"/> answers for a declaration.</summary>
    /// <param name="condition">The condition.</param>
    /// <param name="declaration">The answer for a declaration: true, false, or null when the engine cannot tell.</param>
    /// <returns>Whether it holds, or null.</returns>
    public static bool? Evaluate(SupportsCondition condition, System.Func<Declaration, bool?> declaration)
    {
        switch (condition)
        {
            case SupportsNot not:
                return !Evaluate(not.Operand, declaration);
            case SupportsAnd and:
                var all = (bool?)true;
                foreach (var operand in and.Operands)
                {
                    var value = Evaluate(operand, declaration);
                    if (value == false)
                    {
                        return false;
                    }

                    all = value is null ? null : all;
                }

                return all;
            case SupportsOr or:
                var any = (bool?)false;
                foreach (var operand in or.Operands)
                {
                    var value = Evaluate(operand, declaration);
                    if (value == true)
                    {
                        return true;
                    }

                    any = value is null ? null : any;
                }

                return any;
            case SupportsDeclaration test:
                return declaration(test.Declaration);
            case SupportsSelector { Selector: var selector }:
                // selector() takes one complex selector: a list is false. One the engine reads, with no item forgiven
                // and nothing it does not support, holds; for the rest Chromium 159 may answer either way (measured:
                // :is(p, :bogus) is false), so the engine cannot tell.
                if (selector.List is { Selectors.Count: > 1 })
                {
                    return false;
                }

                return selector.List is not null && !selector.Forgave && selector.Findings.All(finding => finding.Support != SelectorSupport.Unsupported)
                    ? true
                    : null;
            default:
                // font-tech(), font-format() and anything in parentheses the engine does not read: Chromium answers
                // some of them true, so the engine cannot tell.
                return null;
        }
    }

    /// <summary>Whether the values hold, at any depth, a ), ] or } that closes nothing, a bad string or a bad URL;
    /// without recursion.</summary>
    private static bool HasBrokenToken(IReadOnlyList<ComponentValue> values)
    {
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

    private static SupportsCondition? Condition(List<ComponentValue> tokens, ref int index)
    {
        if (index < tokens.Count && tokens[index] is CssToken not && not.IsIdent("not"))
        {
            index++;
            return InParens(tokens, ref index) is { } operand ? new SupportsNot(operand) : null;
        }

        if (InParens(tokens, ref index) is not { } first)
        {
            return null;
        }

        var word = index < tokens.Count && tokens[index] is CssToken token && (token.IsIdent("and") || token.IsIdent("or")) ? InfraAscii.ToLower(token.Text) : null;
        if (word is null)
        {
            return first;
        }

        var operands = new List<SupportsCondition> { first };
        while (index < tokens.Count && tokens[index] is CssToken next && next.IsIdent(word))
        {
            index++;
            if (InParens(tokens, ref index) is not { } operand)
            {
                return null;
            }

            operands.Add(operand);
        }

        return word == "and" ? new SupportsAnd([.. operands]) : new SupportsOr([.. operands]);
    }

    /// <summary>&lt;supports-in-parens&gt; = ( &lt;supports-condition&gt; ) | &lt;supports-feature&gt; | &lt;general-enclosed&gt;.</summary>
    private static SupportsCondition? InParens(List<ComponentValue> tokens, ref int index)
    {
        if (index >= tokens.Count)
        {
            return null;
        }

        var value = tokens[index];
        switch (value)
        {
            case SimpleBlock { Opening: CssTokenKind.OpenParen } block:
                index++;
                if (Parse(block.Values) is { } nested)
                {
                    return nested;
                }

                // ( <declaration> ): parsed as CSS Syntax parses one declaration (§5.4.7).
                if (SyntaxParser.DeclarationFrom(block.Values) is { } declaration)
                {
                    return new SupportsDeclaration(declaration);
                }

                return new SupportsOther("(…)", block.Position);
            case FunctionValue function when function.IsNamed("selector"):
                index++;
                return new SupportsSelector(SelectorParser.Parse(function.Values, relative: false, function.Position), function.Position);
            case FunctionValue function:
                index++;
                return new SupportsOther(function.Name + "()", function.Position);
            default:
                return null;
        }
    }
}
