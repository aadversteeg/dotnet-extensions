using System;
using System.Collections.Generic;
using Ave.Extensions.Documents.Core;

namespace Ave.Extensions.Documents.Css.Syntax;

/// <summary>
/// The parser of CSS Syntax 3 (§5): style sheets, block contents with nested rules, declarations, component values.
/// It is the generic parser, without any property or rule grammar: a declaration is valid when it has a name, a colon,
/// and either no top-level {}-block or only one (§5.5.6, and the implementation note of §5.5.5); which properties and
/// rules mean something is decided later (<c>Rules/RuleBuilder.cs</c>, the property registry).
/// </summary>
/// <remarks>
/// The text is first turned into component values (blocks and functions with their contents), and the rule algorithms
/// of §5.5 run over that stream: §5.4 allows a token stream of component values as input, and every rule algorithm
/// takes a {}-block whole. That pass is where nesting is counted (<see cref="CssInputLimits.MaxDepth"/>), so nothing
/// after it can recurse deeper.
/// </remarks>
internal sealed class SyntaxParser
{
    private readonly CssSource? _source;
    private readonly Action<string, int> _error;

    private SyntaxParser(CssSource? source, Action<string, int> error)
    {
        _source = source;
        _error = error;
    }

    /// <summary>"Consume a declaration" over component values that are already parsed (the inside of a supports
    /// block): one declaration and nothing after it, or null. Without the source text, a custom property has no
    /// original text.</summary>
    public static Declaration? DeclarationFrom(IReadOnlyList<ComponentValue> values)
    {
        var parser = new SyntaxParser(null, static (_, _) => { });
        var stream = new ComponentStream(values);
        stream.DiscardWhitespace();
        if (stream.IsEmpty)
        {
            return null;
        }

        var declaration = parser.ConsumeDeclaration(stream, nested: false);
        stream.DiscardWhitespace();
        return stream.IsEmpty ? declaration : null;
    }

    /// <summary>The first block or function past the depth limit, as a source index; null when there is none.</summary>
    public int? TooDeepAt { get; private set; }

    /// <summary>"Parse a stylesheet" (§5.4.3).</summary>
    public static (ValueList<SyntaxRule> Rules, int? TooDeepAt) ParseStyleSheet(CssSource source, Action<string, int> error)
    {
        var parser = new SyntaxParser(source, error);
        var values = parser.ComponentValues(new CssTokenizer(source, error));
        return parser.TooDeepAt is { } deep ? ([], deep) : (parser.ConsumeStyleSheetContents(new ComponentStream(values)), null);
    }

    /// <summary>"Parse a list of declarations" (§5.4.5), for a <c>style</c> attribute: at-rules are consumed and dropped.
    /// Each item is a declaration, or null where one did not parse.</summary>
    public static (ValueList<(Declaration? Declaration, TextPosition Position)> Items, int? TooDeepAt) ParseDeclarationList(CssSource source, Action<string, int> error)
    {
        var parser = new SyntaxParser(source, error);
        var values = parser.ComponentValues(new CssTokenizer(source, error));
        if (parser.TooDeepAt is { } deep)
        {
            return ([], deep);
        }

        var stream = new ComponentStream(values);
        var items = new List<(Declaration?, TextPosition)>();
        while (!stream.IsEmpty)
        {
            stream.DiscardWhitespace();
            if (stream.NextIs(CssTokenKind.Semicolon))
            {
                stream.Discard();
            }
            else if (stream.NextIs(CssTokenKind.AtKeyword))
            {
                parser.ConsumeAtRule(stream, nested: false);
            }
            else if (!stream.IsEmpty)
            {
                var start = stream.Next!.Position;
                items.Add((parser.ConsumeDeclaration(stream, nested: false), start));
            }
        }

        return ([.. items], null);
    }

    /// <summary>"Parse a declaration" (§5.4.7): null for a syntax error (nothing, or more after it).</summary>
    public static (Declaration? Declaration, bool Empty) ParseDeclaration(CssSource source, Action<string, int> error)
    {
        var parser = new SyntaxParser(source, error);
        var stream = new ComponentStream(parser.ComponentValues(new CssTokenizer(source, error)));
        stream.DiscardWhitespace();
        if (stream.IsEmpty)
        {
            return (null, true);
        }

        var declaration = parser.ConsumeDeclaration(stream, nested: false);
        stream.DiscardWhitespace();
        return (stream.IsEmpty ? declaration : null, false);
    }

    /// <summary>"Parse a rule" (§5.4.6): the rule, or null with "empty" or "extra-input" or "invalid".</summary>
    public static (SyntaxRule? Rule, string? Error) ParseRule(CssSource source, Action<string, int> error)
    {
        var parser = new SyntaxParser(source, error);
        var stream = new ComponentStream(parser.ComponentValues(new CssTokenizer(source, error)));
        stream.DiscardWhitespace();
        if (stream.IsEmpty)
        {
            return (null, "empty");
        }

        SyntaxRule? rule = stream.NextIs(CssTokenKind.AtKeyword)
            ? parser.ConsumeAtRule(stream, nested: false)
            : parser.ConsumeQualifiedRule(stream, stopAtSemicolon: false, nested: false);
        if (rule is null)
        {
            return (null, "invalid");
        }

        stream.DiscardWhitespace();
        return stream.IsEmpty ? (rule, null) : (null, "extra-input");
    }

    /// <summary>"Parse a block's contents" over a whole text (the css-parsing-tests entry point blocks_contents).</summary>
    public static ValueList<SyntaxBlockItem> ParseBlockContents(CssSource source, Action<string, int> error)
    {
        var parser = new SyntaxParser(source, error);
        return parser.ConsumeBlockContents(new ComponentStream(parser.ComponentValues(new CssTokenizer(source, error))));
    }

    /// <summary>"Parse a list of component values" (§5.4.9): for media query lists and other microsyntaxes.</summary>
    public static (ValueList<ComponentValue> Values, int? TooDeepAt) ParseComponentValues(CssSource source, Action<string, int> error, bool unicodeRangesAllowed = false)
    {
        var parser = new SyntaxParser(source, error);
        var values = parser.ComponentValues(new CssTokenizer(source, error, unicodeRangesAllowed));
        return parser.TooDeepAt is { } deep ? ([], deep) : ([.. values], null);
    }

    // ---- the pass to component values (§5.5.8–§5.5.10) ----

    private List<ComponentValue> ComponentValues(CssTokenizer tokenizer)
    {
        var values = new List<ComponentValue>();
        while (TooDeepAt is null)
        {
            var token = tokenizer.Next();
            if (token.Kind == CssTokenKind.EndOfFile)
            {
                break;
            }

            values.Add(ComponentValue(tokenizer, token, 1));
        }

        return values;
    }

    /// <summary>"Consume a component value" given its first token; blocks and functions take their contents.</summary>
    private ComponentValue ComponentValue(CssTokenizer tokenizer, CssToken token, int depth)
    {
        var closing = token.Kind switch
        {
            CssTokenKind.OpenCurly => CssTokenKind.CloseCurly,
            CssTokenKind.OpenSquare => CssTokenKind.CloseSquare,
            CssTokenKind.OpenParen or CssTokenKind.Function => CssTokenKind.CloseParen,
            _ => (CssTokenKind?)null,
        };
        if (closing is null)
        {
            return token;
        }

        if (depth > CssInputLimits.MaxDepth)
        {
            TooDeepAt ??= token.SourceStart;
            return token;
        }

        var contents = new List<ComponentValue>();
        var end = token.SourceEnd;
        while (TooDeepAt is null)
        {
            var next = tokenizer.Next();
            end = next.SourceEnd;
            if (next.Kind == CssTokenKind.EndOfFile || next.Kind == closing)
            {
                break;
            }

            contents.Add(ComponentValue(tokenizer, next, depth + 1));
        }

        return token.Kind == CssTokenKind.Function
            ? new FunctionValue(token.Text, [.. contents], token.Position) { SourceStart = token.SourceStart, SourceEnd = end }
            : new SimpleBlock(token.Kind, [.. contents], token.Position) { SourceStart = token.SourceStart, SourceEnd = end };
    }

    // ---- the rule algorithms (§5.5.1–§5.5.7) ----

    /// <summary>"Consume a stylesheet's contents" (§5.5.1).</summary>
    private ValueList<SyntaxRule> ConsumeStyleSheetContents(ComponentStream input)
    {
        var rules = new List<SyntaxRule>();
        while (true)
        {
            if (input.IsEmpty)
            {
                return [.. rules];
            }

            if (input.NextIs(CssTokenKind.Whitespace) || input.NextIs(CssTokenKind.Cdo) || input.NextIs(CssTokenKind.Cdc))
            {
                input.Discard();
            }
            else if (input.NextIs(CssTokenKind.AtKeyword))
            {
                rules.Add(ConsumeAtRule(input, nested: false));
            }
            else
            {
                var start = input.Next!;
                var rule = ConsumeQualifiedRule(input, stopAtSemicolon: false, nested: false);
                rules.Add((SyntaxRule?)rule ?? new SyntaxInvalid(start.Position));
            }
        }
    }

    /// <summary>"Consume an at-rule" (§5.5.2). At this level every at-rule is valid; its grammar is checked later.</summary>
    private SyntaxAtRule ConsumeAtRule(ComponentStream input, bool nested)
    {
        var keyword = (CssToken)input.Consume();
        var prelude = new List<ComponentValue>();
        while (true)
        {
            if (input.IsEmpty || input.NextIs(CssTokenKind.Semicolon))
            {
                input.Discard();
                return new SyntaxAtRule(keyword.Text, [.. prelude], null, [], keyword.Position);
            }

            if (input.NextIs(CssTokenKind.CloseCurly))
            {
                if (nested)
                {
                    return new SyntaxAtRule(keyword.Text, [.. prelude], null, [], keyword.Position);
                }

                prelude.Add(input.Consume());
                continue;
            }

            if (input.Next is SimpleBlock { Opening: CssTokenKind.OpenCurly } block)
            {
                input.Discard();
                return new SyntaxAtRule(keyword.Text, [.. prelude], block, ConsumeBlockContents(new ComponentStream([.. block.Values])), keyword.Position);
            }

            prelude.Add(input.Consume());
        }
    }

    /// <summary>"Consume a qualified rule" (§5.5.3), with an optional stop token (a semicolon) and nested flag; null for
    /// "nothing" (and for an invalid rule, which at this level only means one that looks like a custom property).</summary>
    private SyntaxQualifiedRule? ConsumeQualifiedRule(ComponentStream input, bool stopAtSemicolon, bool nested)
    {
        var prelude = new List<ComponentValue>();
        var start = input.Next?.Position;
        while (true)
        {
            if (input.IsEmpty || (stopAtSemicolon && input.NextIs(CssTokenKind.Semicolon)))
            {
                _error("rule-without-block", prelude.Count > 0 ? prelude[0].SourceStart : input.Next?.SourceStart ?? _source?.Text.Length ?? 0);
                return null;
            }

            if (input.NextIs(CssTokenKind.CloseCurly))
            {
                _error("unexpected-close-bracket", input.Next!.SourceStart);
                if (nested)
                {
                    return null;
                }

                prelude.Add(input.Consume());
                continue;
            }

            if (input.Next is SimpleBlock { Opening: CssTokenKind.OpenCurly } block)
            {
                if (LooksLikeCustomProperty(prelude))
                {
                    _error("rule-like-custom-property", prelude[0].SourceStart);
                    if (nested)
                    {
                        ConsumeBadDeclarationRemnants(input, nested: true);
                    }
                    else
                    {
                        input.Discard();
                    }

                    return null;
                }

                input.Discard();
                var (declarations, children) = Materialise(ConsumeBlockContents(new ComponentStream([.. block.Values])));
                return new SyntaxQualifiedRule([.. prelude], block, declarations, children, start ?? block.Position);
            }

            prelude.Add(input.Consume());
        }
    }

    /// <summary>
    /// §5.5.3 after "consume a block": the declarations before the first rule are the rule's own; later runs of
    /// declarations become nested declarations rules. Runs separated only by a dropped item
    /// (<see cref="SyntaxInvalid"/>) are one run, as in the standard, where "nothing" does not end the list of
    /// declarations; the markers are left out.
    /// </summary>
    public static (ValueList<Declaration> Declarations, ValueList<SyntaxRule> Rules) Materialise(ValueList<SyntaxBlockItem> contents)
    {
        var own = new List<Declaration>();
        var rules = new List<SyntaxRule>();
        var pending = new List<Declaration>();
        var seenRule = false;
        void Flush()
        {
            if (pending.Count == 0)
            {
                return;
            }

            if (seenRule)
            {
                rules.Add(new SyntaxNestedDeclarations([.. pending], pending[0].Position));
            }
            else
            {
                own.AddRange(pending);
            }

            pending = [];
        }

        foreach (var item in contents)
        {
            switch (item.Rule)
            {
                case null:
                    pending.AddRange(item.Declarations);
                    break;
                case SyntaxInvalid:
                    break;
                default:
                    Flush();
                    seenRule = true;
                    rules.Add(item.Rule);
                    break;
            }
        }

        Flush();
        return ([.. own], [.. rules]);
    }

    /// <summary>§5.5.3: the first two non-whitespace values are an ident starting with "--" and a colon.</summary>
    private static bool LooksLikeCustomProperty(List<ComponentValue> prelude)
    {
        var meaningful = prelude.FindAll(value => value is not CssToken { Kind: CssTokenKind.Whitespace });
        return meaningful.Count >= 2
            && meaningful[0] is CssToken { Kind: CssTokenKind.Ident } name && name.Text.StartsWith("--", StringComparison.Ordinal)
            && meaningful[1] is CssToken { Kind: CssTokenKind.Colon };
    }

    /// <summary>"Consume a block's contents" (§5.5.5): declarations and rules, in order. The standard's text returns
    /// at the end without the last list of declarations; every implementation and css-parsing-tests keep it, and so
    /// does this one.</summary>
    private ValueList<SyntaxBlockItem> ConsumeBlockContents(ComponentStream input)
    {
        var rules = new List<SyntaxBlockItem>();
        var declarations = new List<Declaration>();
        void FlushDeclarations()
        {
            if (declarations.Count > 0)
            {
                rules.Add(new SyntaxBlockItem(null, [.. declarations]));
                declarations = [];
            }
        }

        while (true)
        {
            if (input.NextIs(CssTokenKind.Whitespace) || input.NextIs(CssTokenKind.Semicolon))
            {
                input.Discard();
                continue;
            }

            if (input.IsEmpty || input.NextIs(CssTokenKind.CloseCurly))
            {
                FlushDeclarations();
                return [.. rules];
            }

            if (input.NextIs(CssTokenKind.AtKeyword))
            {
                FlushDeclarations();
                rules.Add(new SyntaxBlockItem(ConsumeAtRule(input, nested: true), []));
                continue;
            }

            input.Mark();
            if (ConsumeDeclaration(input, nested: true) is { } declaration)
            {
                declarations.Add(declaration);
                input.DiscardMark();
                continue;
            }

            input.RestoreMark();
            var start = input.Next!;
            var rule = ConsumeQualifiedRule(input, stopAtSemicolon: true, nested: true);
            FlushDeclarations();
            rules.Add(new SyntaxBlockItem((SyntaxRule?)rule ?? new SyntaxInvalid(start.Position), []));
        }
    }

    /// <summary>"Consume a declaration" (§5.5.6); null for "nothing".</summary>
    private Declaration? ConsumeDeclaration(ComponentStream input, bool nested)
    {
        if (input.Next is not CssToken { Kind: CssTokenKind.Ident } name)
        {
            ConsumeBadDeclarationRemnants(input, nested);
            return null;
        }

        input.Discard();
        input.DiscardWhitespace();
        if (!input.NextIs(CssTokenKind.Colon))
        {
            ConsumeBadDeclarationRemnants(input, nested);
            return null;
        }

        input.Discard();
        input.DiscardWhitespace();
        var value = ConsumeComponentValueList(input, nested);

        var important = false;
        var meaningful = new List<int>();
        for (var i = 0; i < value.Count; i++)
        {
            if (value[i] is not CssToken { Kind: CssTokenKind.Whitespace })
            {
                meaningful.Add(i);
            }
        }

        if (meaningful.Count >= 2 && value[meaningful[^2]] is CssToken bang && bang.IsDelim('!') && value[meaningful[^1]] is CssToken word && word.IsIdent("important"))
        {
            value.RemoveRange(meaningful[^2], value.Count - meaningful[^2]);
            important = true;
        }

        while (value.Count > 0 && value[^1] is CssToken { Kind: CssTokenKind.Whitespace })
        {
            value.RemoveAt(value.Count - 1);
        }

        if (name.Text.StartsWith("--", StringComparison.Ordinal))
        {
            var original = value.Count == 0 ? string.Empty : _source?.Text[value[0].SourceStart..value[^1].SourceEnd];
            return new Declaration(name.Text, [.. value], important, name.Position) { OriginalText = original };
        }

        var hasCurly = value.Exists(item => item is SimpleBlock { Opening: CssTokenKind.OpenCurly });
        if (hasCurly && value.Exists(item => item is not SimpleBlock { Opening: CssTokenKind.OpenCurly } && item is not CssToken { Kind: CssTokenKind.Whitespace }))
        {
            return null;
        }

        if (InfraAscii.EqualsIgnoreCase(name.Text, "unicode-range") && value.Count > 0 && _source is not null)
        {
            // "Consume the value of a unicode-range descriptor" from the original text (§5.5.11).
            var ranges = ComponentValues(new CssTokenizer(_source, _error, unicodeRangesAllowed: true, value[0].SourceStart, value[^1].SourceEnd));
            return new Declaration(name.Text, [.. ranges], important, name.Position);
        }

        return new Declaration(name.Text, [.. value], important, name.Position);
    }

    /// <summary>"Consume the remnants of a bad declaration" (§5.5.6).</summary>
    private static void ConsumeBadDeclarationRemnants(ComponentStream input, bool nested)
    {
        while (true)
        {
            if (input.IsEmpty || input.NextIs(CssTokenKind.Semicolon))
            {
                input.Discard();
                return;
            }

            if (input.NextIs(CssTokenKind.CloseCurly))
            {
                if (nested)
                {
                    return;
                }

                input.Discard();
                continue;
            }

            input.Discard();
        }
    }

    /// <summary>"Consume a list of component values" (§5.5.7) up to a semicolon.</summary>
    private List<ComponentValue> ConsumeComponentValueList(ComponentStream input, bool nested)
    {
        var values = new List<ComponentValue>();
        while (!input.IsEmpty && !input.NextIs(CssTokenKind.Semicolon))
        {
            if (input.NextIs(CssTokenKind.CloseCurly))
            {
                if (nested)
                {
                    return values;
                }

                _error("unexpected-close-bracket", input.Next!.SourceStart);
            }

            values.Add(input.Consume());
        }

        return values;
    }

    /// <summary>A token stream of component values (§5.3), with marks.</summary>
    private sealed class ComponentStream(IReadOnlyList<ComponentValue> values)
    {
        private readonly Stack<int> _marks = new();
        private int _index;

        /// <summary>"The next token" of §5.3: null at the end, where the standard has an EOF token.</summary>
        public ComponentValue? Next => _index < values.Count ? values[_index] : null;

        /// <summary>"Empty" of §5.3.</summary>
        public bool IsEmpty => _index >= values.Count;

        /// <summary>Whether the next token is a token of <paramref name="kind"/>.</summary>
        public bool NextIs(CssTokenKind kind) => Next is CssToken token && token.Kind == kind;

        /// <summary>"Consume a token" of §5.3.</summary>
        public ComponentValue Consume() => values[_index++];

        /// <summary>"Discard a token" of §5.3: nothing at the end.</summary>
        public void Discard()
        {
            if (!IsEmpty)
            {
                _index++;
            }
        }

        /// <summary>"Discard whitespace" of §5.3.</summary>
        public void DiscardWhitespace()
        {
            while (NextIs(CssTokenKind.Whitespace))
            {
                _index++;
            }
        }

        /// <summary>"Mark" of §5.3.</summary>
        public void Mark() => _marks.Push(_index);

        /// <summary>"Restore a mark" of §5.3.</summary>
        public void RestoreMark() => _index = _marks.Pop();

        /// <summary>"Discard a mark" of §5.3.</summary>
        public void DiscardMark() => _marks.Pop();
    }
}
