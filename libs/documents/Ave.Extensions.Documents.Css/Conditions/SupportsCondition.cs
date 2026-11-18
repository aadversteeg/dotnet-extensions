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
public static class SupportsConditions
{
    /// <summary>Parses a supports condition (the prelude of <c>@supports</c>, or the argument of <c>supports()</c> in
    /// <c>@import</c>); null when it does not match the grammar.</summary>
    /// <param name="values">The component values.</param>
    /// <returns>The condition, or null.</returns>
    public static SupportsCondition? Parse(IReadOnlyList<ComponentValue> values)
    {
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

    /// <summary>Evaluates a condition. <paramref name="declaration"/> answers for a declaration: true when the engine
    /// supports it.</summary>
    /// <param name="condition">The condition.</param>
    /// <param name="declaration">The answer for a declaration.</param>
    /// <returns>Whether it holds.</returns>
    public static bool Evaluate(SupportsCondition condition, System.Func<Declaration, bool> declaration) => condition switch
    {
        SupportsNot not => !Evaluate(not.Operand, declaration),
        SupportsAnd and => and.Operands.All(operand => Evaluate(operand, declaration)),
        SupportsOr or => or.Operands.Any(operand => Evaluate(operand, declaration)),
        SupportsDeclaration test => declaration(test.Declaration),
        // As Chromium answers: a selector that parses holds, also one the engine refuses or that never matches here
        // (the rules inside are then refused or never match, which is visible; the other answer would silently take
        // the author's fallback where Chromium does not).
        SupportsSelector selector => selector.Selector.List is not null,
        _ => false,
    };

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

        var word = index < tokens.Count && tokens[index] is CssToken token && (token.IsIdent("and") || token.IsIdent("or")) ? Ascii.ToLower(token.Text) : null;
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
