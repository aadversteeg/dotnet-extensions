using System.Collections.Generic;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Css.Syntax;
using Ave.Extensions.Documents.Css.Values;

namespace Ave.Extensions.Documents.Css.Properties;

/// <summary>
/// A cursor over a declaration's value for the property grammars: the component values with white space removed (no
/// grammar of a supported property depends on it). Each "read" either takes a value and returns what it is, or takes
/// nothing and returns null, so a grammar can try alternatives.
/// </summary>
internal sealed class ValueReader
{
    private readonly List<ComponentValue> _values;
    private int _index;

    internal ValueReader(IReadOnlyList<ComponentValue> values)
    {
        _values = [];
        foreach (var value in values)
        {
            if (value is not CssToken { Kind: CssTokenKind.Whitespace })
            {
                _values.Add(value);
            }
        }
    }

    /// <summary>Whether every value has been read.</summary>
    internal bool AtEnd => _index >= _values.Count;

    /// <summary>The number of values.</summary>
    internal int Count => _values.Count;

    /// <summary>The next value, or null.</summary>
    internal ComponentValue? Peek => _index < _values.Count ? _values[_index] : null;

    /// <summary>The position to come back to.</summary>
    internal int Mark => _index;

    /// <summary>Goes back to a mark.</summary>
    internal void Reset(int mark) => _index = mark;

    /// <summary>Takes the next value.</summary>
    internal ComponentValue Next() => _values[_index++];

    /// <summary>Takes the next value when it is one of these keywords; returns it lowercase, or null.</summary>
    internal string? Keyword(params string[] keywords)
    {
        if (Peek is CssToken { Kind: CssTokenKind.Ident } ident)
        {
            var lower = Ascii.ToLower(ident.Text);
            if (System.Array.IndexOf(keywords, lower) >= 0)
            {
                _index++;
                return lower;
            }
        }

        return null;
    }

    /// <summary>Takes the next value when it is any identifier; returns it lowercase, or null.</summary>
    internal string? AnyKeyword()
    {
        if (Peek is CssToken { Kind: CssTokenKind.Ident } ident)
        {
            _index++;
            return Ascii.ToLower(ident.Text);
        }

        return null;
    }

    /// <summary>Takes a delim with this code point.</summary>
    internal bool Delim(char c)
    {
        if (Peek is CssToken token && token.IsDelim(c))
        {
            _index++;
            return true;
        }

        return false;
    }

    /// <summary>
    /// A &lt;length&gt; or, with <paramref name="percentages"/>, a &lt;length-percentage&gt; (CSS Values 4 §6, §5.7): an
    /// absolute length (points), a relative one the engine resolves (kept as a dimension), a percentage, a unitless zero,
    /// or a math function of these. Returns null when the next value is none of these; an invalid or refused value
    /// otherwise (a unit CSS defines that the engine refuses, a negative value where none is allowed, a calc() of the
    /// wrong type).
    /// </summary>
    internal ParsedValue? Length(bool percentages, bool nonNegative, string property)
    {
        switch (Peek)
        {
            case CssToken { Kind: CssTokenKind.Number, Number: 0 }:
                _index++;
                return ParsedValue.Of(new CssLength(Ave.Extensions.Documents.Core.Length.Zero));
            case CssToken { Kind: CssTokenKind.Percentage } percentage when percentages:
                _index++;
                return nonNegative && percentage.Number < 0
                    ? ParsedValue.Invalid($"a negative percentage for {property}")
                    : ParsedValue.Of(new CssPercentage(percentage.Number));
            case CssToken { Kind: CssTokenKind.Dimension } dimension:
                var unit = Ascii.ToLower(dimension.Text);
                if (Units.PointsPer(unit) is { } points)
                {
                    _index++;
                    return nonNegative && dimension.Number < 0
                        ? ParsedValue.Invalid($"a negative length for {property}")
                        : ParsedValue.Of(new CssLength(Ave.Extensions.Documents.Core.Length.FromPoints(dimension.Number * points)));
                }

                if (Units.IsResolvedRelative(unit))
                {
                    _index++;
                    return nonNegative && dimension.Number < 0
                        ? ParsedValue.Invalid($"a negative length for {property}")
                        : ParsedValue.Of(new CssDimension(dimension.Number, unit));
                }

                if (Units.IsUnsupportedLength(unit))
                {
                    _index++;
                    return ParsedValue.Refused($"the unit '{dimension.Text}' is not supported");
                }

                return null;
            case FunctionValue function when Calc.IsMathFunction(function):
                _index++;
                if (Calc.Parse(function, out var problem) is not { } node)
                {
                    return problem is not null && problem.StartsWith("unsupported:", System.StringComparison.Ordinal)
                        ? ParsedValue.Refused(problem["unsupported:".Length..]) with { Code = CssErrorCodes.Unsupported.Calc }
                        : ParsedValue.Invalid($"{function.Name}(): {problem}");
                }

                var type = Calc.TypeOf(node);
                var allowed = type == CalcType.Length || (percentages && type is CalcType.Percentage or CalcType.LengthPercentage);
                return allowed ? ParsedValue.Of(new CssCalc(node)) : ParsedValue.Invalid($"{function.Name}() is not a length here");
            default:
                return null;
        }
    }

    /// <summary>A &lt;number&gt; (or a math function of numbers); null when the next value is not one.</summary>
    internal double? Number()
    {
        switch (Peek)
        {
            case CssToken { Kind: CssTokenKind.Number } number:
                _index++;
                return number.Number;
            case FunctionValue function when Calc.IsMathFunction(function) && Calc.Parse(function, out _) is { } node && Calc.TypeOf(node) == CalcType.Number
                                             && Calc.Evaluate(node, (_, _) => null, null) is { } result:
                _index++;
                return result.Number;
            default:
                return null;
        }
    }

    /// <summary>An &lt;integer&gt;; null when the next value is not one (a math function of numbers rounds, §10.9).</summary>
    internal long? Integer()
    {
        if (Peek is CssToken { Kind: CssTokenKind.Number, IsInteger: true } integer)
        {
            _index++;
            return (long)integer.Number;
        }

        if (Peek is FunctionValue && Number() is { } number)
        {
            return (long)System.Math.Round(number, System.MidpointRounding.AwayFromZero);
        }

        return null;
    }

    /// <summary>A &lt;color&gt;; null when the next value is not one.</summary>
    internal ParsedValue? Color()
    {
        if (Peek is not { } value)
        {
            return null;
        }

        switch (Colors.Read(value, out var color, out var message))
        {
            case ColorOutcome.Color:
                _index++;
                return ParsedValue.Of(new CssColorValue(color));
            case ColorOutcome.CurrentColor:
                _index++;
                return ParsedValue.Of(new CssKeyword("currentcolor"));
            case ColorOutcome.Unsupported:
                _index++;
                return ParsedValue.Refused(message!) with { Code = CssErrorCodes.Unsupported.ColorFunction };
            default:
                return null;
        }
    }

    /// <summary>A &lt;string&gt;; null when the next value is not one.</summary>
    internal string? String()
    {
        if (Peek is CssToken { Kind: CssTokenKind.String } text)
        {
            _index++;
            return text.Text;
        }

        return null;
    }

    /// <summary>A &lt;url&gt;; null when the next value is not one.</summary>
    internal string? Url()
    {
        switch (Peek)
        {
            case CssToken { Kind: CssTokenKind.Url } url:
                _index++;
                return url.Text;
            case FunctionValue function when function.IsNamed("url") || function.IsNamed("src"):
                foreach (var argument in function.Values)
                {
                    if (argument is CssToken { Kind: CssTokenKind.String } quoted)
                    {
                        _index++;
                        return quoted.Text;
                    }
                }

                return null;
            default:
                return null;
        }
    }

    /// <summary>A reader over the values not read yet (they stay unread here).</summary>
    internal ValueReader Rest() => new(_values.GetRange(_index, _values.Count - _index));

    /// <summary>The values not read yet.</summary>
    internal IReadOnlyList<ComponentValue> Remaining => _values.GetRange(_index, _values.Count - _index);

    /// <summary>Reads to the end.</summary>
    internal void SkipToEnd() => _index = _values.Count;

    /// <summary>The position of the value at the mark, for diagnostics.</summary>
    internal TextPosition? PositionAt(int mark) => mark < _values.Count ? _values[mark].Position : null;
}
