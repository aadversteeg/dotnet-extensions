using System;
using System.Collections.Generic;
using System.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Css.Syntax;

namespace Ave.Extensions.Documents.Css.Values;

/// <summary>A node of a math function's calculation tree (CSS Values 4 §10.8).</summary>
public abstract record CalcNode;

/// <summary>A number, percentage or length leaf.</summary>
/// <param name="Value">The number.</param>
/// <param name="Unit">Empty for a number, "%" for a percentage, else the length unit, lowercase.</param>
public sealed record CalcLeaf(double Value, string Unit) : CalcNode
{
    /// <summary>Formats as written.</summary>
    public override string ToString() => $"{InvariantNumber.Format(Value, 6)}{Unit}";
}

/// <summary>A sum: <c>a + b - c</c>.</summary>
/// <param name="Terms">The terms, each with whether it is subtracted.</param>
public sealed record CalcSum(ValueList<(CalcNode Term, bool Negated)> Terms) : CalcNode
{
    /// <summary>Formats with + and −.</summary>
    public override string ToString() =>
        string.Concat(Terms.Select((term, i) => (i == 0 ? (term.Negated ? "-" : string.Empty) : term.Negated ? " - " : " + ") + term.Term));
}

/// <summary>A product: <c>a * b / c</c>.</summary>
/// <param name="Factors">The factors, each with whether it divides.</param>
public sealed record CalcProduct(ValueList<(CalcNode Factor, bool Inverted)> Factors) : CalcNode
{
    /// <summary>Formats with * and /.</summary>
    public override string ToString() =>
        string.Concat(Factors.Select((factor, i) => (i == 0 ? string.Empty : factor.Inverted ? " / " : " * ") + factor.Factor));
}

/// <summary><c>min()</c>, <c>max()</c> or <c>clamp()</c> (CSS Values 4 §10.2).</summary>
/// <param name="Name">The function, lowercase.</param>
/// <param name="Arguments">Its arguments.</param>
public sealed record CalcFunction(string Name, ValueList<CalcNode> Arguments) : CalcNode
{
    /// <summary>Formats as the function.</summary>
    public override string ToString() => $"{Name}({string.Join(", ", Arguments)})";
}

/// <summary>The type of a calculation (CSS Values 4 §10.9), as far as the engine uses them.</summary>
internal enum CalcType
{
    /// <summary>&lt;number&gt;.</summary>
    Number,

    /// <summary>&lt;length&gt;.</summary>
    Length,

    /// <summary>&lt;percentage&gt;.</summary>
    Percentage,

    /// <summary>A length and a percentage added (&lt;length-percentage&gt;).</summary>
    LengthPercentage,

    /// <summary>No consistent type (invalid).</summary>
    Invalid,

    /// <summary>A product or quotient of dimensions (CSS Values 4 §10.9, typed arithmetic: <c>1em / 1px</c> is a number):
    /// valid, and evaluated by Chromium; not supported here.</summary>
    TypedArithmetic,
}

/// <summary>A resolved calculation: a number, or points plus a percentage of something not yet known.</summary>
/// <param name="Number">The number, for a number calculation.</param>
/// <param name="Points">The length part in points.</param>
/// <param name="Percent">The percentage part.</param>
/// <param name="IsNumber">Whether this is a number.</param>
internal readonly record struct CalcResult(double Number, double Points, double Percent, bool IsNumber);

/// <summary>
/// Math functions: <c>calc()</c>, <c>min()</c>, <c>max()</c>, <c>clamp()</c> over numbers, lengths and percentages
/// (CSS Values 4 §10). Other math functions, other types (angles, times …), a product of two lengths and the
/// keywords infinity and NaN are refused; e and pi are numbers.
/// </summary>
internal static class Calc
{
    /// <summary>The math function names CSS defines (for "valid but unsupported").</summary>
    internal static readonly HashSet<string> OtherMathFunctions =
        ["round", "mod", "rem", "sin", "cos", "tan", "asin", "acos", "atan", "atan2", "pow", "sqrt", "hypot", "log", "exp", "abs", "sign", "progress", "calc-size", "random", "sibling-index", "sibling-count"];

    /// <summary>Whether a function is a math function (supported or not).</summary>
    public static bool IsMathFunction(FunctionValue function)
    {
        var name = InfraAscii.ToLower(function.Name);
        return name is "calc" or "min" or "max" or "clamp" || OtherMathFunctions.Contains(name);
    }

    /// <summary>Parses a math function. Returns the tree, or null with the reason: <c>invalid</c> text when it does not
    /// match the grammar, or an <c>unsupported:</c> text for valid CSS the engine refuses.</summary>
    public static CalcNode? Parse(FunctionValue function, out string? problem)
    {
        problem = null;
        var name = InfraAscii.ToLower(function.Name);
        if (OtherMathFunctions.Contains(name))
        {
            problem = $"unsupported:the math function {name}() is not supported";
            return null;
        }

        var arguments = SplitArguments(function.Values);
        switch (name)
        {
            case "calc":
                return arguments.Count == 1 ? Sum(arguments[0], ref problem) : Fail("calc() takes one argument", ref problem);
            case "min" or "max":
                return arguments.Count >= 1 ? Function(name, arguments, ref problem) : Fail($"{name}() needs an argument", ref problem);
            case "clamp":
                return arguments.Count == 3 ? Function(name, arguments, ref problem) : Fail("clamp() takes three arguments", ref problem);
            default:
                return Fail($"{function.Name}() is not a math function", ref problem);
        }
    }

    private static CalcNode? Fail(string message, ref string? problem)
    {
        problem ??= message;
        return null;
    }

    private static List<List<ComponentValue>> SplitArguments(IReadOnlyList<ComponentValue> values)
    {
        var arguments = new List<List<ComponentValue>> { new() };
        foreach (var value in values)
        {
            if (value is CssToken { Kind: CssTokenKind.Comma })
            {
                arguments.Add([]);
            }
            else
            {
                arguments[^1].Add(value);
            }
        }

        return arguments;
    }

    private static CalcNode? Function(string name, List<List<ComponentValue>> arguments, ref string? problem)
    {
        var nodes = new List<CalcNode>();
        foreach (var argument in arguments)
        {
            if (name == "clamp" && argument.Where(value => value is not CssToken { Kind: CssTokenKind.Whitespace }).ToList() is [CssToken none] && none.IsIdent("none"))
            {
                return Fail("unsupported:clamp() with none is not supported", ref problem);
            }

            if (Sum(argument, ref problem) is not { } node)
            {
                return null;
            }

            nodes.Add(node);
        }

        return new CalcFunction(name, [.. nodes]);
    }

    /// <summary>&lt;calc-sum&gt; = &lt;calc-product&gt; [ ['+' | '-'] &lt;calc-product&gt; ]*, white space required around +
    /// and -.</summary>
    private static CalcNode? Sum(List<ComponentValue> values, ref string? problem)
    {
        var terms = new List<(CalcNode, bool)>();
        var product = new List<ComponentValue>();
        var negated = false;
        for (var i = 0; i < values.Count; i++)
        {
            if (values[i] is CssToken sign && (sign.IsDelim('+') || sign.IsDelim('-')))
            {
                var spaceBefore = i > 0 && values[i - 1] is CssToken { Kind: CssTokenKind.Whitespace };
                var spaceAfter = i + 1 < values.Count && values[i + 1] is CssToken { Kind: CssTokenKind.Whitespace };
                if (!spaceBefore || !spaceAfter)
                {
                    return Fail("+ and - need white space on both sides", ref problem);
                }

                if (Product(product, ref problem) is not { } term)
                {
                    return null;
                }

                terms.Add((term, negated));
                product = [];
                negated = sign.IsDelim('-');
                continue;
            }

            product.Add(values[i]);
        }

        if (Product(product, ref problem) is not { } last)
        {
            return null;
        }

        terms.Add((last, negated));
        return terms.Count == 1 && !terms[0].Item2 ? terms[0].Item1 : new CalcSum([.. terms]);
    }

    /// <summary>&lt;calc-product&gt; = &lt;calc-value&gt; [ ['*' | '/'] &lt;calc-value&gt; ]*.</summary>
    private static CalcNode? Product(List<ComponentValue> values, ref string? problem)
    {
        var meaningful = values.Where(value => value is not CssToken { Kind: CssTokenKind.Whitespace }).ToList();
        if (meaningful.Count == 0)
        {
            return Fail("an empty term", ref problem);
        }

        var factors = new List<(CalcNode, bool)>();
        var inverted = false;
        var expectValue = true;
        foreach (var value in meaningful)
        {
            if (!expectValue)
            {
                if (value is CssToken op && (op.IsDelim('*') || op.IsDelim('/')))
                {
                    inverted = op.IsDelim('/');
                    expectValue = true;
                    continue;
                }

                return Fail("two values without an operator", ref problem);
            }

            if (Value(value, ref problem) is not { } node)
            {
                return null;
            }

            factors.Add((node, inverted));
            expectValue = false;
        }

        if (expectValue)
        {
            return Fail("an operator without a value", ref problem);
        }

        return factors.Count == 1 && !factors[0].Item2 ? factors[0].Item1 : new CalcProduct([.. factors]);
    }

    /// <summary>&lt;calc-value&gt; = &lt;number&gt; | &lt;dimension&gt; | &lt;percentage&gt; | &lt;calc-keyword&gt; | ( &lt;calc-sum&gt; ),
    /// and nested math functions.</summary>
    private static CalcNode? Value(ComponentValue value, ref string? problem)
    {
        switch (value)
        {
            case CssToken { Kind: CssTokenKind.Number } number:
                return new CalcLeaf(number.Number, string.Empty);
            case CssToken { Kind: CssTokenKind.Percentage } percentage:
                return new CalcLeaf(percentage.Number, "%");
            case CssToken { Kind: CssTokenKind.Dimension } dimension:
                var unit = InfraAscii.ToLower(dimension.Text);
                if (Units.PointsPer(unit) is not null || Units.IsResolvedRelative(unit))
                {
                    return new CalcLeaf(dimension.Number, unit);
                }

                return Units.IsUnsupportedLength(unit) || unit is "deg" or "rad" or "grad" or "turn" or "s" or "ms" or "hz" or "khz" or "dpi" or "dpcm" or "dppx" or "x" or "fr"
                    ? Fail($"unsupported:the unit {unit} in a math function is not supported", ref problem)
                    : Fail($"the unknown unit {unit}", ref problem);
            case CssToken keyword when keyword.IsIdent("e"):
                return new CalcLeaf(Math.E, string.Empty);
            case CssToken keyword when keyword.IsIdent("pi"):
                return new CalcLeaf(Math.PI, string.Empty);
            case CssToken keyword when keyword.IsIdent("infinity") || keyword.IsIdent("-infinity") || keyword.IsIdent("nan"):
                return Fail($"unsupported:the keyword {keyword.Text} in a math function is not supported", ref problem);
            case SimpleBlock { Opening: CssTokenKind.OpenParen } block:
                return Sum([.. block.Values], ref problem);
            case FunctionValue function when IsMathFunction(function):
                return Parse(function, out problem);
            default:
                return Fail("an unexpected value in a math function", ref problem);
        }
    }

    /// <summary>The type of a calculation (§10.9), with percentages left as percentages.</summary>
    public static CalcType TypeOf(CalcNode node)
    {
        switch (node)
        {
            case CalcLeaf leaf:
                return leaf.Unit switch
                {
                    "" => CalcType.Number,
                    "%" => CalcType.Percentage,
                    _ => CalcType.Length,
                };
            case CalcSum sum:
                return sum.Terms.Select(term => TypeOf(term.Term)).Aggregate(Add);
            case CalcProduct product:
                var type = CalcType.Number;
                foreach (var (factor, inverted) in product.Factors)
                {
                    var factorType = TypeOf(factor);
                    if (factorType is CalcType.Invalid or CalcType.TypedArithmetic)
                    {
                        return factorType;
                    }

                    if (inverted && factorType != CalcType.Number)
                    {
                        return CalcType.TypedArithmetic;
                    }

                    if (factorType == CalcType.Number)
                    {
                        continue;
                    }

                    if (type != CalcType.Number)
                    {
                        return CalcType.TypedArithmetic;
                    }

                    type = factorType;
                }

                return type;
            case CalcFunction function:
                return function.Arguments.Select(TypeOf).Aggregate(Add);
            default:
                return CalcType.Invalid;
        }
    }

    private static CalcType Add(CalcType left, CalcType right)
    {
        if (left == CalcType.Invalid || right == CalcType.Invalid)
        {
            return CalcType.Invalid;
        }

        if (left == CalcType.TypedArithmetic || right == CalcType.TypedArithmetic)
        {
            return CalcType.TypedArithmetic;
        }

        if (left == right)
        {
            return left;
        }

        if (left == CalcType.Number || right == CalcType.Number)
        {
            return CalcType.Invalid;
        }

        return CalcType.LengthPercentage;
    }

    /// <summary>Evaluates a calculation. <paramref name="points"/> converts a length leaf to points (null when its unit
    /// cannot be resolved); <paramref name="percentBasis"/>, when known, turns percentages into points. Returns null
    /// when a min/max/clamp would have to compare a length with a percentage of something unknown.</summary>
    public static CalcResult? Evaluate(CalcNode node, Func<double, string, double?> points, double? percentBasis)
    {
        switch (node)
        {
            case CalcLeaf leaf:
                if (leaf.Unit == string.Empty)
                {
                    return new CalcResult(leaf.Value, 0, 0, IsNumber: true);
                }

                if (leaf.Unit == "%")
                {
                    return percentBasis is { } basis ? new CalcResult(0, leaf.Value * basis / 100, 0, false) : new CalcResult(0, 0, leaf.Value, false);
                }

                return points(leaf.Value, leaf.Unit) is { } length ? new CalcResult(0, length, 0, false) : null;
            case CalcSum sum:
                CalcResult? total = null;
                foreach (var (term, negated) in sum.Terms)
                {
                    if (Evaluate(term, points, percentBasis) is not { } value)
                    {
                        return null;
                    }

                    var signed = negated ? new CalcResult(-value.Number, -value.Points, -value.Percent, value.IsNumber) : value;
                    total = total is { } t ? new CalcResult(t.Number + signed.Number, t.Points + signed.Points, t.Percent + signed.Percent, t.IsNumber && signed.IsNumber) : signed;
                }

                return total;
            case CalcProduct product:
                var factor = 1.0;
                CalcResult? dimensioned = null;
                foreach (var (item, inverted) in product.Factors)
                {
                    if (Evaluate(item, points, percentBasis) is not { } value)
                    {
                        return null;
                    }

                    if (value.IsNumber)
                    {
                        factor = inverted ? factor / value.Number : factor * value.Number;
                    }
                    else
                    {
                        dimensioned = value;
                    }
                }

                return dimensioned is { } d ? new CalcResult(0, d.Points * factor, d.Percent * factor, false) : new CalcResult(factor, 0, 0, true);
            case CalcFunction function:
                var values = new List<CalcResult>();
                foreach (var argument in function.Arguments)
                {
                    if (Evaluate(argument, points, percentBasis) is not { } value)
                    {
                        return null;
                    }

                    values.Add(value);
                }

                // Comparable only when every argument has one kind: all numbers, all lengths, or all percentages.
                var lengths = values.Exists(value => !value.IsNumber && value.Points != 0);
                var percents = values.Exists(value => !value.IsNumber && value.Percent != 0);
                if (lengths && percents)
                {
                    return null;
                }

                double Key(CalcResult value) => value.IsNumber ? value.Number : value.Points + value.Percent;
                var chosen = function.Name switch
                {
                    "min" => values.MinBy(Key),
                    "max" => values.MaxBy(Key),
                    _ => Key(values[1]) < Key(values[0]) ? values[0] : Key(values[1]) > Key(values[2]) ? (Key(values[0]) > Key(values[2]) ? values[0] : values[2]) : values[1],
                };
                return chosen;
            default:
                return null;
        }
    }
}
