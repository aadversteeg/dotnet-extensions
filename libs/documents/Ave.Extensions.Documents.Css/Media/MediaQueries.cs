using System.Collections.Frozen;
using System.Collections.Generic;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Css.Syntax;
using Ave.Extensions.Documents.Css.Values;
using Ave.Extensions.ErrorPaths;

namespace Ave.Extensions.Documents.Css.Media;

/// <summary>
/// Media Queries 4: parsing (§3, with the error recovery of §3.2: a query that does not match the grammar becomes
/// <c>not all</c>) and evaluation (§3.1, three-valued). The engine evaluates the media type and the width and height
/// features (§4.1, §4.2, with their <c>min-</c>/<c>max-</c> forms and the range syntax); every other feature CSS defines
/// is refused (<see cref="CssErrorCodes.Unsupported.MediaFeature"/>) and evaluates as unknown, and an unknown feature
/// makes the query <c>not all</c>, as in a browser.
/// </summary>
public static class MediaQueries
{
    /// <summary>The media features of Media Queries 4 and 5 and those Chromium adds, other than width and height: valid,
    /// not evaluated by the engine. The range ones take min-/max- prefixes.</summary>
    private static readonly FrozenSet<string> OtherRangeFeatures = FrozenSet.Create(
        "aspect-ratio", "device-width", "device-height", "device-aspect-ratio", "resolution", "color", "color-index",
        "monochrome", "-webkit-device-pixel-ratio", "horizontal-viewport-segments", "vertical-viewport-segments");

    private static readonly FrozenSet<string> OtherDiscreteFeatures = FrozenSet.Create(
        "orientation", "scan", "grid", "update", "overflow-block", "overflow-inline", "color-gamut", "pointer", "hover",
        "any-pointer", "any-hover", "display-mode", "dynamic-range", "video-dynamic-range", "prefers-reduced-motion",
        "prefers-reduced-transparency", "prefers-reduced-data", "prefers-contrast", "prefers-color-scheme",
        "forced-colors", "scripting", "inverted-colors", "environment-blending", "device-posture",
        "-webkit-transform-3d", "shape");

    /// <summary>Parses a media query list from component values (the prelude of <c>@media</c>, the <c>media</c>
    /// attribute, the end of <c>@import</c>).</summary>
    /// <param name="values">The values.</param>
    /// <param name="sourceName">The style sheet's name, for diagnostics.</param>
    /// <param name="diagnostics">Receives the diagnostics: a query that became <c>not all</c>, a feature that is not
    /// supported.</param>
    /// <returns>The list.</returns>
    public static MediaQueryList Parse(IReadOnlyList<ComponentValue> values, string sourceName, List<Error> diagnostics)
    {
        var queries = new List<MediaQuery>();
        var parts = new List<List<ComponentValue>> { new() };
        foreach (var value in values)
        {
            if (value is CssToken { Kind: CssTokenKind.Comma })
            {
                parts.Add([]);
            }
            else
            {
                parts[^1].Add(value);
            }
        }

        // An empty list (only white space) is valid and matches (§3: "intentionally accepts an empty list").
        if (parts.Count == 1 && parts[0].TrueForAll(IsWhitespace))
        {
            return MediaQueryList.All;
        }

        foreach (var part in parts)
        {
            var tokens = part.FindAll(value => !IsWhitespace(value));
            var position = tokens.Count > 0 ? tokens[0].Position : TextPosition.Start;
            var parser = new Parser(tokens, sourceName, diagnostics);
            if (parser.Query() is { } query && parser.AtEnd)
            {
                queries.Add(query);
            }
            else
            {
                diagnostics.Add(CssDiagnostics.At(CssErrorCodes.Invalid.Rule, "A media query that does not parse; it is 'not all' (Media Queries 4 §3.2).", sourceName, position));
                queries.Add(MediaQuery.NotAll(position));
            }
        }

        return new MediaQueryList([.. queries]);
    }

    /// <summary>Whether a media query list matches (§2.1: one of its queries matches; an empty list matches).</summary>
    public static bool Matches(MediaQueryList list, MediaContext context)
    {
        if (list.Queries.IsEmpty)
        {
            return true;
        }

        foreach (var query in list.Queries)
        {
            if (Matches(query, context))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether one media query matches; unknown is false (§3.1).</summary>
    public static bool Matches(MediaQuery query, MediaContext context)
    {
        var typeMatches = query.Type is null or "all" || query.Type == Ascii.ToLower(context.Type);
        var condition = query.Condition is null ? Kleene.True : Evaluate(query.Condition, context);
        var result = typeMatches ? condition : Kleene.False;
        if (query.Negated)
        {
            result = Not(result);
        }

        return result == Kleene.True;
    }

    private enum Kleene
    {
        False,
        True,
        Unknown,
    }

    private static Kleene Not(Kleene value) => value switch
    {
        Kleene.True => Kleene.False,
        Kleene.False => Kleene.True,
        _ => Kleene.Unknown,
    };

    private static Kleene Evaluate(MediaCondition condition, MediaContext context)
    {
        switch (condition)
        {
            case MediaNot not:
                return Not(Evaluate(not.Operand, context));
            case MediaAnd and:
                var all = Kleene.True;
                foreach (var operand in and.Operands)
                {
                    var value = Evaluate(operand, context);
                    if (value == Kleene.False)
                    {
                        return Kleene.False;
                    }

                    if (value == Kleene.Unknown)
                    {
                        all = Kleene.Unknown;
                    }
                }

                return all;
            case MediaOr or:
                var any = Kleene.False;
                foreach (var operand in or.Operands)
                {
                    var value = Evaluate(operand, context);
                    if (value == Kleene.True)
                    {
                        return Kleene.True;
                    }

                    if (value == Kleene.Unknown)
                    {
                        any = Kleene.Unknown;
                    }
                }

                return any;
            case MediaFeatureTest test:
                return Test(test, context);
            default:
                return Kleene.Unknown;
        }
    }

    private static Kleene Test(MediaFeatureTest test, MediaContext context)
    {
        var actual = test.Feature == "width" ? context.Width.Points : context.Height.Points;
        if (test.Boolean)
        {
            return actual != 0 ? Kleene.True : Kleene.False;
        }

        if (test.Lower is { } lower && Points(lower.Length, context) is var low && (lower.Inclusive ? actual < low : actual <= low))
        {
            return Kleene.False;
        }

        if (test.Upper is { } upper && Points(upper.Length, context) is var high && (upper.Inclusive ? actual > high : actual >= high))
        {
            return Kleene.False;
        }

        return Kleene.True;
    }

    /// <summary>A media feature length in points (§1.3): em and rem are the initial font size, 16px = 12pt.</summary>
    private static double Points(MediaLength length, MediaContext context) => length.Unit switch
    {
        "" => 0,
        "em" or "rem" => length.Value * 12,
        "vw" => length.Value * context.Width.Points / 100,
        "vh" => length.Value * context.Height.Points / 100,
        _ => length.Value * (Units.PointsPer(length.Unit) ?? double.NaN),
    };

    private static bool IsWhitespace(ComponentValue value) => value is CssToken { Kind: CssTokenKind.Whitespace };

    /// <summary>A recursive-descent parser over one query's tokens, white space removed (no rule of the grammar depends
    /// on it, except that '&lt;=' and '&gt;=' have no space inside, which the parser checks by position).</summary>
    private sealed class Parser(List<ComponentValue> tokens, string sourceName, List<Error> diagnostics)
    {
        private int _index;

        public bool AtEnd => _index >= tokens.Count;

        private ComponentValue? Peek => _index < tokens.Count ? tokens[_index] : null;

        private bool PeekIdent(string name) => Peek is CssToken token && token.IsIdent(name);

        /// <summary>&lt;media-query&gt; = &lt;media-condition&gt; | [not | only]? &lt;media-type&gt; [and &lt;media-condition-without-or&gt;]?;
        /// null when the tokens do not match (the query is then "not all").</summary>
        public MediaQuery? Query()
        {
            var position = Peek?.Position ?? TextPosition.Start;
            if (Peek is SimpleBlock or FunctionValue || PeekIdent("not") && tokens.Count > 1 && tokens[1] is SimpleBlock or FunctionValue)
            {
                return Condition(allowOr: true) is { } condition ? new MediaQuery(false, null, condition, position) : null;
            }

            var negated = false;
            if (PeekIdent("not"))
            {
                negated = true;
                _index++;
            }
            else if (PeekIdent("only"))
            {
                _index++;
            }

            if (Peek is not CssToken { Kind: CssTokenKind.Ident } typeToken)
            {
                return null;
            }

            // An unknown or deprecated media type is valid and matches nothing (§2.3, §3.2); these words are not types.
            var type = Ascii.ToLower(typeToken.Text);
            if (type is "only" or "not" or "and" or "or" or "layer")
            {
                return null;
            }

            _index++;
            if (AtEnd)
            {
                return new MediaQuery(negated, type, null, position);
            }

            if (!PeekIdent("and"))
            {
                return null;
            }

            _index++;
            return Condition(allowOr: false) is { } rest ? new MediaQuery(negated, type, rest, position) : null;
        }

        /// <summary>&lt;media-condition&gt;, or without <c>or</c> &lt;media-condition-without-or&gt;.</summary>
        private MediaCondition? Condition(bool allowOr)
        {
            if (PeekIdent("not"))
            {
                _index++;
                return InParens() is { } operand ? new MediaNot(operand) : null;
            }

            if (InParens() is not { } first)
            {
                return null;
            }

            var word = PeekIdent("and") ? "and" : allowOr && PeekIdent("or") ? "or" : null;
            if (word is null)
            {
                return first;
            }

            var operands = new List<MediaCondition> { first };
            while (PeekIdent(word))
            {
                _index++;
                if (InParens() is not { } next)
                {
                    return null;
                }

                operands.Add(next);
            }

            return word == "and" ? new MediaAnd([.. operands]) : new MediaOr([.. operands]);
        }

        /// <summary>&lt;media-in-parens&gt; = ( &lt;media-condition&gt; ) | ( &lt;media-feature&gt; ) | &lt;general-enclosed&gt;.
        /// Parentheses always give a condition: what does not match a condition or a feature is &lt;general-enclosed&gt;,
        /// unknown.</summary>
        private MediaCondition? InParens()
        {
            switch (Peek)
            {
                case SimpleBlock { Opening: CssTokenKind.OpenParen } block:
                    _index++;
                    var inner = block.Values.Items.RemoveAll(IsWhitespace);
                    var nested = new Parser([.. inner], sourceName, diagnostics);
                    if (nested.Condition(allowOr: true) is { } condition && nested.AtEnd)
                    {
                        return condition;
                    }

                    return Feature([.. inner], block.Position);
                case FunctionValue function:
                    _index++;
                    return new MediaUnknown(function.Name + "()");
                default:
                    return null;
            }
        }

        /// <summary>&lt;media-feature&gt; = &lt;mf-plain&gt; | &lt;mf-boolean&gt; | &lt;mf-range&gt;, for width and height; any
        /// other feature, a value that does not match, or tokens that are not a feature are unknown (§3.2).</summary>
        private MediaCondition Feature(List<ComponentValue> inner, TextPosition position)
        {
            var nameIndex = inner.FindIndex(value => value is CssToken { Kind: CssTokenKind.Ident });
            if (nameIndex < 0)
            {
                return Unknown("a parenthesised condition that is not a media feature", position);
            }

            var nameToken = (CssToken)inner[nameIndex];
            var lower = Ascii.ToLower(nameToken.Text);
            var prefix = lower.StartsWith("min-", System.StringComparison.Ordinal) ? "min-" : lower.StartsWith("max-", System.StringComparison.Ordinal) ? "max-" : string.Empty;
            var name = lower[prefix.Length..];
            if (name is not ("width" or "height"))
            {
                if (OtherDiscreteFeatures.Contains(lower) || OtherRangeFeatures.Contains(name))
                {
                    diagnostics.Add(CssDiagnostics.At(CssErrorCodes.Unsupported.MediaFeature, $"The media feature '{nameToken.Text}' is not supported; only width and height are.", sourceName, nameToken.Position));
                    return new MediaUnknown(lower);
                }

                return Unknown($"the unknown media feature '{nameToken.Text}'", nameToken.Position);
            }

            // <mf-boolean>
            if (inner.Count == 1)
            {
                return prefix.Length == 0 ? new MediaFeatureTest(name, null, null, Boolean: true) : Unknown("a min-/max- feature without a value", position);
            }

            // <mf-plain> = <mf-name> : <mf-value>
            if (nameIndex == 0 && inner[1] is CssToken { Kind: CssTokenKind.Colon })
            {
                if (inner.Count != 3 || Length(inner[2]) is not { } value)
                {
                    return Unknown($"a value of '{nameToken.Text}' that is not a length", position);
                }

                return prefix switch
                {
                    "min-" => new MediaFeatureTest(name, (value, true), null, Boolean: false),
                    "max-" => new MediaFeatureTest(name, null, (value, true), Boolean: false),
                    _ => new MediaFeatureTest(name, (value, true), (value, true), Boolean: false),
                };
            }

            // <mf-range>: the range form takes no prefix.
            return prefix.Length == 0 && Range(inner, nameIndex, name) is { } range ? range : Unknown($"a range of '{nameToken.Text}' that does not parse", position);
        }

        /// <summary>&lt;mf-range&gt; = name op value | value op name | value lt name lt value | value gt name gt value.</summary>
        private MediaFeatureTest? Range(List<ComponentValue> inner, int nameIndex, string name)
        {
            (MediaLength, bool)? lower = null;
            (MediaLength, bool)? upper = null;
            if (nameIndex == 0)
            {
                if (Comparison(inner, 1) is not { } op || op.Next != inner.Count - 1 || Length(inner[^1]) is not { } value)
                {
                    return null;
                }

                Bound(op.Op, value, nameOnLeft: true, ref lower, ref upper);
                return new MediaFeatureTest(name, lower, upper, Boolean: false);
            }

            if (Length(inner[0]) is not { } first || Comparison(inner, 1) is not { } firstOp || firstOp.Next != nameIndex)
            {
                return null;
            }

            Bound(firstOp.Op, first, nameOnLeft: false, ref lower, ref upper);
            if (nameIndex == inner.Count - 1)
            {
                return new MediaFeatureTest(name, lower, upper, Boolean: false);
            }

            if (Comparison(inner, nameIndex + 1) is not { } secondOp || secondOp.Next != inner.Count - 1 || Length(inner[^1]) is not { } second
                || firstOp.Op is '=' || secondOp.Op is '=' || IsLess(firstOp.Op) != IsLess(secondOp.Op))
            {
                return null;
            }

            Bound(secondOp.Op, second, nameOnLeft: true, ref lower, ref upper);
            return new MediaFeatureTest(name, lower, upper, Boolean: false);
        }

        private static bool IsLess(char op) => op is '<' or '≤';

        /// <summary>Sets the bound a comparison gives: <c>width &lt; v</c> and <c>v &gt; width</c> are upper bounds.</summary>
        private static void Bound(char op, MediaLength value, bool nameOnLeft, ref (MediaLength, bool)? lower, ref (MediaLength, bool)? upper)
        {
            if (op == '=')
            {
                lower = (value, true);
                upper = (value, true);
                return;
            }

            var inclusive = op is '≤' or '≥';
            if (IsLess(op) == nameOnLeft)
            {
                upper = (value, inclusive);
            }
            else
            {
                lower = (value, inclusive);
            }
        }

        /// <summary>&lt;mf-comparison&gt; at an index: '&lt;', '&lt;=', '&gt;', '&gt;=' (no space before the '='), '='. The
        /// inclusive ones are returned as '≤' and '≥'.</summary>
        private static (char Op, int Next)? Comparison(List<ComponentValue> inner, int index)
        {
            if (index >= inner.Count || inner[index] is not CssToken { Kind: CssTokenKind.Delim } first)
            {
                return null;
            }

            if (first.IsDelim('='))
            {
                return ('=', index + 1);
            }

            if (!first.IsDelim('<') && !first.IsDelim('>'))
            {
                return null;
            }

            var less = first.IsDelim('<');
            if (index + 1 < inner.Count && inner[index + 1] is CssToken equals && equals.IsDelim('=') && equals.SourceStart == first.SourceEnd)
            {
                return (less ? '≤' : '≥', index + 2);
            }

            return (less ? '<' : '>', index + 1);
        }

        /// <summary>A &lt;length&gt; media feature value: a dimension with a length unit, or a unitless zero. Units the
        /// engine does not resolve here are reported.</summary>
        private MediaLength? Length(ComponentValue value)
        {
            switch (value)
            {
                case CssToken { Kind: CssTokenKind.Number, Number: 0 }:
                    return new MediaLength(0, string.Empty);
                case CssToken { Kind: CssTokenKind.Dimension } dimension:
                    var unit = Ascii.ToLower(dimension.Text);
                    if (Units.PointsPer(unit) is not null || unit is "em" or "rem" or "vw" or "vh")
                    {
                        return new MediaLength(dimension.Number, unit);
                    }

                    if (unit is "ex" or "ch" || Units.IsUnsupportedLength(unit))
                    {
                        diagnostics.Add(CssDiagnostics.At(CssErrorCodes.Unsupported.MediaFeature, $"The unit '{dimension.Text}' in a media feature is not supported.", sourceName, dimension.Position));
                    }

                    return null;
                case FunctionValue function:
                    diagnostics.Add(CssDiagnostics.At(CssErrorCodes.Unsupported.MediaFeature, $"'{function.Name}()' in a media feature is not supported.", sourceName, function.Position));
                    return null;
                default:
                    return null;
            }
        }

        private MediaUnknown Unknown(string what, TextPosition position)
        {
            diagnostics.Add(CssDiagnostics.At(CssErrorCodes.Invalid.Rule, $"Media query: {what}; its value is unknown (Media Queries 4 §3.2).", sourceName, position));
            return new MediaUnknown(what);
        }
    }
}
