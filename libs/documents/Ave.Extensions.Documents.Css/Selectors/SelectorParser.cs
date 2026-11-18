using System.Collections.Generic;
using System.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Css.Syntax;

namespace Ave.Extensions.Documents.Css.Selectors;

/// <summary>A selector construct the engine does not match as Selectors 4 says: one that never matches in a static
/// document, or one that is not supported (reported with its place).</summary>
/// <param name="Support">Which of the two.</param>
/// <param name="Name">What it is, as written (<c>:hover</c>, <c>::before</c>).</param>
/// <param name="Position">Where.</param>
public sealed record SelectorFinding(SelectorSupport Support, string Name, TextPosition Position);

/// <summary>The result of parsing a selector list: the list, or why it is invalid; and the findings of a valid list.</summary>
/// <param name="List">The selector list, or null when it is invalid.</param>
/// <param name="Error">Why it is invalid, or null.</param>
/// <param name="ErrorPosition">Where it is invalid.</param>
/// <param name="Findings">The never-matching and unsupported constructs in a valid list.</param>
public sealed record SelectorParseResult(SelectorList? List, string? Error, TextPosition ErrorPosition, ValueList<SelectorFinding> Findings);

/// <summary>
/// The selector parser (Selectors 4 §16): the whole grammar, so that a valid selector the engine does not support is
/// told apart from an invalid one (§3.9), with <c>:is()</c> and <c>:where()</c> forgiving (§16.1), <c>:not()</c> taking
/// a complex selector list, <c>:nth-child(An+B of S)</c>, attribute selectors with their case modifiers, and the nesting
/// selector <c>&amp;</c> and relative selectors of CSS Nesting 1. A namespace prefix other than <c>*</c> or none is
/// invalid: no namespace is declared, since <c>@namespace</c> is not supported.
/// </summary>
/// <remarks>Each step returns null when the selector is invalid; the context keeps the first reason.</remarks>
public static class SelectorParser
{
    /// <summary>Parses a selector list from component values (a style rule's prelude).</summary>
    /// <param name="values">The values.</param>
    /// <param name="relative">Whether the selectors may start with a combinator (a nested style rule).</param>
    /// <param name="start">Where the values start, for an empty list.</param>
    /// <returns>The result.</returns>
    public static SelectorParseResult Parse(IReadOnlyList<ComponentValue> values, bool relative, TextPosition start)
    {
        var context = new Context();
        var list = ParseList(values, relative, forgiving: false, allowPseudoElements: true, context, start);
        return list is null
            ? new SelectorParseResult(null, context.Error, context.ErrorPosition, [])
            : new SelectorParseResult(list, null, start, [.. context.Findings]);
    }

    /// <summary>The first reason a selector is invalid, and the findings so far.</summary>
    private sealed class Context
    {
        public string? Error { get; private set; }

        public TextPosition ErrorPosition { get; private set; }

        public List<SelectorFinding> Findings { get; } = [];

        /// <summary>Whether the selector being read is inside the argument of :has(), where another :has() is invalid
        /// (Selectors 4 §4.5).</summary>
        public bool InHas { get; set; }

        /// <summary>Records why the selector is invalid (the first reason wins) and returns null, so a step can
        /// <c>return context.Fail&lt;T&gt;(…)</c>.</summary>
        public T? Fail<T>(string message, TextPosition position)
            where T : class
        {
            if (Error is null)
            {
                Error = message;
                ErrorPosition = position;
            }

            return null;
        }
    }

    private static SelectorList? ParseList(IReadOnlyList<ComponentValue> values, bool relative, bool forgiving, bool allowPseudoElements, Context context, TextPosition start)
    {
        var selectors = new List<ComplexSelector>();
        foreach (var part in SplitOnCommas(values))
        {
            if (!forgiving)
            {
                if (ParseComplex(part, relative, allowPseudoElements, context, start) is not { } selector)
                {
                    return null;
                }

                selectors.Add(selector);
                continue;
            }

            // <forgiving-selector-list>: an item that does not parse is dropped, the others stay (§16.1).
            var own = new Context { InHas = context.InHas };
            if (ParseComplex(part, relative, allowPseudoElements, own, start) is { } kept)
            {
                selectors.Add(kept);
                context.Findings.AddRange(own.Findings);
            }
        }

        return new SelectorList([.. selectors]);
    }

    private static List<List<ComponentValue>> SplitOnCommas(IReadOnlyList<ComponentValue> values)
    {
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

        return parts;
    }

    private static ComplexSelector? ParseComplex(List<ComponentValue> values, bool relative, bool allowPseudoElements, Context context, TextPosition start)
    {
        var cursor = new Cursor(values);
        cursor.SkipWhitespace();
        if (cursor.AtEnd)
        {
            return context.Fail<ComplexSelector>("an empty selector", values.Count > 0 ? values[0].Position : start);
        }

        var position = cursor.Peek!.Position;
        var steps = new List<SelectorStep>();
        Combinator? combinator = null;
        if (relative && CombinatorAt(cursor) is { } leading)
        {
            combinator = leading;
            cursor.Next();
            cursor.SkipWhitespace();
        }

        while (true)
        {
            if (ParseCompound(cursor, allowPseudoElements, context) is not { } compound)
            {
                return null;
            }

            steps.Add(new SelectorStep(combinator, compound));
            var sawSpace = cursor.SkipWhitespace();
            if (cursor.AtEnd)
            {
                return new ComplexSelector([.. steps], position);
            }

            if (compound.Selectors.Any(simple => simple is PseudoElementSelector))
            {
                // A pseudo-element is in the last compound selector (Selectors 4 §3.6.4).
                return context.Fail<ComplexSelector>("a combinator after a pseudo-element", cursor.Peek!.Position);
            }

            if (CombinatorAt(cursor) is { } explicitCombinator)
            {
                combinator = explicitCombinator;
                cursor.Next();
                cursor.SkipWhitespace();
                if (cursor.AtEnd)
                {
                    return context.Fail<ComplexSelector>("a combinator at the end of a selector", position);
                }
            }
            else if (sawSpace)
            {
                combinator = Combinator.Descendant;
            }
            else
            {
                return context.Fail<ComplexSelector>("an unexpected token in a selector", cursor.Peek!.Position);
            }
        }
    }

    private static Combinator? CombinatorAt(Cursor cursor) => cursor.Peek switch
    {
        CssToken token when token.IsDelim('>') => Combinator.Child,
        CssToken token when token.IsDelim('+') => Combinator.NextSibling,
        CssToken token when token.IsDelim('~') => Combinator.SubsequentSibling,
        _ => null,
    };

    /// <summary>A compound selector: an optional type selector, subclass selectors, and optionally a pseudo-element with
    /// pseudo-classes after it. Nothing between them may be white space.</summary>
    private static CompoundSelector? ParseCompound(Cursor cursor, bool allowPseudoElements, Context context)
    {
        var simple = new List<SimpleSelector>();
        var start = cursor.Peek?.Position ?? TextPosition.Start;
        if (!ParseTypeSelector(cursor, context, out var type))
        {
            return null;
        }

        if (type is not null)
        {
            simple.Add(type);
        }

        string? pseudoElement = null;
        while (!cursor.AtEnd)
        {
            var value = cursor.Peek!;
            if (value is CssToken { Kind: CssTokenKind.Whitespace } || CombinatorAt(cursor) is not null)
            {
                break;
            }

            var isPseudoClass = value is CssToken { Kind: CssTokenKind.Colon } && cursor.PeekAt(1) is not CssToken { Kind: CssTokenKind.Colon };
            var isPseudoElement = value is CssToken { Kind: CssTokenKind.Colon } && !isPseudoClass;
            if (pseudoElement is not null && !isPseudoClass && !isPseudoElement)
            {
                return context.Fail<CompoundSelector>("only pseudo-classes may follow a pseudo-element", value.Position);
            }

            SimpleSelector? next;
            switch (value)
            {
                case CssToken { Kind: CssTokenKind.Hash } hash:
                    cursor.Next();
                    next = hash.IsIdHash ? new IdSelector(hash.Text) : context.Fail<SimpleSelector>("an ID selector that is not an identifier", hash.Position);
                    break;
                case CssToken dot when dot.IsDelim('.'):
                    cursor.Next();
                    next = cursor.Peek is CssToken { Kind: CssTokenKind.Ident } name
                        ? new ClassSelector(((CssToken)cursor.Next()).Text)
                        : context.Fail<SimpleSelector>("a class selector without a name", dot.Position);
                    break;
                case SimpleBlock { Opening: CssTokenKind.OpenSquare } block:
                    cursor.Next();
                    next = ParseAttribute(block, context);
                    break;
                case CssToken amp when amp.IsDelim('&'):
                    cursor.Next();
                    next = new NestingSelector();
                    break;
                case CssToken { Kind: CssTokenKind.Colon } colon when !isPseudoClass:
                    cursor.Next();
                    cursor.Next();
                    next = allowPseudoElements
                        ? ParsePseudoElement(cursor, colon.Position, context)
                        : context.Fail<SimpleSelector>("a pseudo-element where none is allowed", colon.Position);
                    if (next is PseudoElementSelector element)
                    {
                        if (pseudoElement is not null && !PseudoClasses.AllowedAfterPseudoElement(pseudoElement, element.Name))
                        {
                            return context.Fail<CompoundSelector>($"'::{element.Name}' cannot follow '::{pseudoElement}'", colon.Position);
                        }

                        pseudoElement = element.Name;
                    }

                    break;
                case CssToken { Kind: CssTokenKind.Colon } colon:
                    cursor.Next();
                    next = ParsePseudoClass(cursor, colon.Position, context);
                    if (next is PseudoElementSelector legacy)
                    {
                        if (!allowPseudoElements || (pseudoElement is not null && !PseudoClasses.AllowedAfterPseudoElement(pseudoElement, legacy.Name)))
                        {
                            return context.Fail<CompoundSelector>("a pseudo-element where none is allowed", colon.Position);
                        }

                        pseudoElement = legacy.Name;
                    }
                    else if (next is PseudoClassSelector pseudoClass && pseudoElement is not null && !PseudoClasses.AllowedAfter(pseudoElement, pseudoClass.Name))
                    {
                        return context.Fail<CompoundSelector>($"':{pseudoClass.Name}' cannot follow '::{pseudoElement}'", colon.Position);
                    }

                    break;
                default:
                    next = context.Fail<SimpleSelector>("an unexpected token in a selector", value.Position);
                    break;
            }

            if (next is null)
            {
                return null;
            }

            simple.Add(next);
        }

        return simple.Count == 0
            ? context.Fail<CompoundSelector>("an empty compound selector", start)
            : new CompoundSelector([.. simple]);
    }

    /// <summary>&lt;type-selector&gt; = &lt;wq-name&gt; | &lt;ns-prefix&gt;? '*', with &lt;ns-prefix&gt; = [ident | '*']? '|'.
    /// Returns false for an invalid one (an undeclared prefix); <paramref name="type"/> is null when there is none.</summary>
    private static bool ParseTypeSelector(Cursor cursor, Context context, out TypeSelector? type)
    {
        type = null;
        var first = cursor.Peek as CssToken;
        var second = cursor.PeekAt(1) as CssToken;
        var third = cursor.PeekAt(2) as CssToken;
        static bool IsName(CssToken? token) => token is { Kind: CssTokenKind.Ident } || (token is not null && token.IsDelim('*'));
        static string NameOf(CssToken token) => token.Kind == CssTokenKind.Ident ? token.Text : "*";

        if (first is not null && first.IsDelim('|') && IsName(second))
        {
            cursor.Next();
            cursor.Next();
            type = new TypeSelector(NameOf(second!), string.Empty);
            return true;
        }

        if (IsName(first) && second is not null && second.IsDelim('|') && IsName(third))
        {
            if (first!.Kind == CssTokenKind.Ident)
            {
                context.Fail<TypeSelector>($"the namespace prefix '{first.Text}' is not declared", first.Position);
                return false;
            }

            cursor.Next();
            cursor.Next();
            cursor.Next();
            type = new TypeSelector(NameOf(third!), "*");
            return true;
        }

        if (IsName(first))
        {
            cursor.Next();
            type = new TypeSelector(NameOf(first!), null);
        }

        return true;
    }

    /// <summary>&lt;attribute-selector&gt; (§6): <c>[wq-name]</c> or <c>[wq-name matcher value modifier?]</c>.</summary>
    private static AttributeSelector? ParseAttribute(SimpleBlock block, Context context)
    {
        var cursor = new Cursor([.. block.Values]);
        cursor.SkipWhitespace();
        string? ns = null;
        var first = cursor.Peek as CssToken;
        var second = cursor.PeekAt(1) as CssToken;
        var third = cursor.PeekAt(2) as CssToken;
        CssToken name;
        if (first is not null && first.IsDelim('|') && second is { Kind: CssTokenKind.Ident })
        {
            ns = string.Empty;
            cursor.Next();
            name = (CssToken)cursor.Next();
        }
        else if (first is not null && (first.Kind == CssTokenKind.Ident || first.IsDelim('*')) && second is not null && second.IsDelim('|') && third is { Kind: CssTokenKind.Ident })
        {
            if (first.Kind == CssTokenKind.Ident)
            {
                return context.Fail<AttributeSelector>($"the namespace prefix '{first.Text}' is not declared", first.Position);
            }

            ns = "*";
            cursor.Next();
            cursor.Next();
            name = (CssToken)cursor.Next();
        }
        else if (first is { Kind: CssTokenKind.Ident })
        {
            name = (CssToken)cursor.Next();
        }
        else
        {
            return context.Fail<AttributeSelector>("an attribute selector without a name", block.Position);
        }

        cursor.SkipWhitespace();
        if (cursor.AtEnd)
        {
            return new AttributeSelector(name.Text, ns, AttributeOperator.Exists, null, AttributeCase.Default);
        }

        if (ParseMatcher(cursor) is not { } op)
        {
            return context.Fail<AttributeSelector>("an attribute selector with an unknown operator", cursor.Peek!.Position);
        }

        cursor.SkipWhitespace();
        if (cursor.Peek is not CssToken { Kind: CssTokenKind.String or CssTokenKind.Ident } value)
        {
            return context.Fail<AttributeSelector>("an attribute selector without a value", block.Position);
        }

        cursor.Next();
        cursor.SkipWhitespace();
        var modifier = AttributeCase.Default;
        if (cursor.Peek is CssToken { Kind: CssTokenKind.Ident } flag)
        {
            switch (Ascii.ToLower(flag.Text))
            {
                case "i":
                    modifier = AttributeCase.Insensitive;
                    break;
                case "s":
                    modifier = AttributeCase.Sensitive;
                    break;
                default:
                    return context.Fail<AttributeSelector>($"an unknown attribute selector modifier '{flag.Text}'", flag.Position);
            }

            cursor.Next();
            cursor.SkipWhitespace();
        }

        return cursor.AtEnd
            ? new AttributeSelector(name.Text, ns, op, value.Text, modifier)
            : context.Fail<AttributeSelector>("an unexpected token in an attribute selector", cursor.Peek!.Position);
    }

    private static AttributeOperator? ParseMatcher(Cursor cursor)
    {
        if (cursor.Peek is not CssToken token || token.Kind != CssTokenKind.Delim)
        {
            return null;
        }

        if (token.IsDelim('='))
        {
            cursor.Next();
            return AttributeOperator.Equals;
        }

        if (cursor.PeekAt(1) is not CssToken equals || !equals.IsDelim('='))
        {
            return null;
        }

        AttributeOperator? op = token.Text switch
        {
            "~" => AttributeOperator.Includes,
            "|" => AttributeOperator.DashMatch,
            "^" => AttributeOperator.Prefix,
            "$" => AttributeOperator.Suffix,
            "*" => AttributeOperator.Substring,
            _ => null,
        };
        if (op is not null)
        {
            cursor.Next();
            cursor.Next();
        }

        return op;
    }

    /// <summary>A pseudo-class after its colon, or a legacy pseudo-element written with one colon (§16).</summary>
    private static SimpleSelector? ParsePseudoClass(Cursor cursor, TextPosition position, Context context)
    {
        switch (cursor.Peek)
        {
            case CssToken { Kind: CssTokenKind.Ident } ident:
                cursor.Next();
                var lower = Ascii.ToLower(ident.Text);
                if (lower is "before" or "after" or "first-line" or "first-letter")
                {
                    context.Findings.Add(new SelectorFinding(SelectorSupport.Unsupported, ":" + lower, position));
                    return new PseudoElementSelector(lower, []);
                }

                if (PseudoClasses.FindClass(lower, functional: false) is not { } entry)
                {
                    return context.Fail<SimpleSelector>($"the unknown pseudo-class ':{ident.Text}'", position);
                }

                Note(entry, ":" + lower, position, context);
                return new PseudoClassSelector(lower, entry.Support);
            case FunctionValue function:
                cursor.Next();
                var name = Ascii.ToLower(function.Name);
                if (PseudoClasses.FindClass(name, functional: true) is not { } known)
                {
                    return context.Fail<SimpleSelector>($"the unknown pseudo-class ':{function.Name}()'", position);
                }

                Note(known, $":{name}()", position, context);
                return FunctionalPseudoClass(name, known, function, position, context);
            default:
                return context.Fail<SimpleSelector>("a colon without a pseudo-class", position);
        }
    }

    private static void Note(PseudoClasses.Entry entry, string written, TextPosition position, Context context)
    {
        if (entry.Support != SelectorSupport.Supported)
        {
            context.Findings.Add(new SelectorFinding(entry.Support, written, position));
        }
    }

    private static PseudoClassSelector? FunctionalPseudoClass(string name, PseudoClasses.Entry entry, FunctionValue function, TextPosition position, Context context)
    {
        var arguments = function.Values;
        switch (name)
        {
            case "is" or "where":
                return ParseList(arguments, relative: false, forgiving: true, allowPseudoElements: false, context, position) is { } forgiving
                    ? new PseudoClassSelector(name, entry.Support) { Selectors = forgiving }
                    : null;
            case "not":
                return ParseList(arguments, relative: false, forgiving: false, allowPseudoElements: false, context, position) is { } negated
                    ? new PseudoClassSelector(name, entry.Support) { Selectors = negated }
                    : null;
            case "has":
                if (context.InHas)
                {
                    return context.Fail<PseudoClassSelector>("':has()' inside ':has()'", position);
                }

                context.InHas = true;
                var relative = ParseList(arguments, relative: true, forgiving: false, allowPseudoElements: false, context, position);
                context.InHas = false;
                return relative is not null ? new PseudoClassSelector(name, entry.Support) { Selectors = relative } : null;
            case "host" or "host-context":
                return Compound(arguments, context, position) is not null
                    ? new PseudoClassSelector(name, entry.Support) { Arguments = arguments }
                    : context.Fail<PseudoClassSelector>($"':{name}()' takes a compound selector", position);
            case "state" or "dir":
                return Identifiers(arguments, commas: false) == 1
                    ? new PseudoClassSelector(name, entry.Support) { Arguments = arguments }
                    : context.Fail<PseudoClassSelector>($"':{name}()' takes one identifier", position);
            case "lang":
                return LanguageRanges(arguments)
                    ? new PseudoClassSelector(name, entry.Support) { Arguments = arguments }
                    : context.Fail<PseudoClassSelector>("':lang()' takes language ranges", position);
            case "active-view-transition-type":
                return Identifiers(arguments, commas: true) > 0
                    ? new PseudoClassSelector(name, entry.Support) { Arguments = arguments }
                    : context.Fail<PseudoClassSelector>($"':{name}()' takes identifiers", position);
            case "nth-child" or "nth-last-child" or "nth-of-type" or "nth-last-of-type":
                var ofIndex = -1;
                for (var i = 0; i < arguments.Count && name is "nth-child" or "nth-last-child"; i++)
                {
                    if (arguments[i] is CssToken token && token.IsIdent("of"))
                    {
                        ofIndex = i;
                        break;
                    }
                }

                var anb = ofIndex < 0 ? arguments.Items : arguments.Items[..ofIndex];
                if (AnPlusBParser.Parse(anb) is not { } nth)
                {
                    return context.Fail<PseudoClassSelector>($"':{name}()' needs An+B", position);
                }

                if (ofIndex < 0)
                {
                    return new PseudoClassSelector(name, entry.Support) { Nth = nth };
                }

                return ParseList(arguments.Items[(ofIndex + 1)..], relative: false, forgiving: false, allowPseudoElements: false, context, position) is { } of
                    ? new PseudoClassSelector(name, entry.Support) { Nth = nth, Selectors = of }
                    : null;
            default:
                return HasMeaningful(arguments)
                    ? new PseudoClassSelector(name, entry.Support) { Arguments = arguments }
                    : context.Fail<PseudoClassSelector>($"':{name}()' needs an argument", position);
        }
    }

    private static PseudoElementSelector? ParsePseudoElement(Cursor cursor, TextPosition position, Context context)
    {
        switch (cursor.Peek)
        {
            case CssToken { Kind: CssTokenKind.Ident } ident:
                cursor.Next();
                if (PseudoClasses.FindElement(ident.Text, functional: false) is not { } entry)
                {
                    return context.Fail<PseudoElementSelector>($"the unknown pseudo-element '::{ident.Text}'", position);
                }

                context.Findings.Add(new SelectorFinding(entry.Support, "::" + entry.Name, position));
                return new PseudoElementSelector(entry.Name, []);
            case FunctionValue function:
                cursor.Next();
                if (PseudoClasses.FindElement(function.Name, functional: true) is not { } known)
                {
                    return context.Fail<PseudoElementSelector>($"the unknown pseudo-element '::{function.Name}()'", position);
                }

                var valid = known.Name switch
                {
                    "slotted" => Compound(function.Values, context, position) is not null,
                    "part" => Identifiers(function.Values, commas: false) > 0,
                    "highlight" or "picker" => Identifiers(function.Values, commas: false) == 1,
                    _ => true,
                };
                if (!valid)
                {
                    return context.Fail<PseudoElementSelector>($"the argument of '::{known.Name}()' is invalid", position);
                }

                context.Findings.Add(new SelectorFinding(known.Support, $"::{known.Name}()", position));
                return new PseudoElementSelector(known.Name, function.Values);
            default:
                return context.Fail<PseudoElementSelector>("'::' without a pseudo-element", position);
        }
    }

    /// <summary>The argument as one compound selector without a pseudo-element (:host(), :host-context(),
    /// ::slotted()), or null.</summary>
    private static CompoundSelector? Compound(ValueList<ComponentValue> values, Context context, TextPosition position)
    {
        var own = new Context { InHas = context.InHas };
        return ParseList(values, relative: false, forgiving: false, allowPseudoElements: false, own, position) is { Selectors: [{ Steps: [var only] }] }
               && only.Combinator is null && OnlyCompounds(only.Compound)
            ? only.Compound
            : null;
    }

    /// <summary>Whether the selector lists inside a compound's pseudo-classes hold compound selectors only, and no
    /// :has(): Chromium's rule for the arguments of :host(), :host-context() and ::slotted().</summary>
    private static bool OnlyCompounds(CompoundSelector compound) =>
        compound.Selectors.All(simple => simple switch
        {
            PseudoClassSelector { Name: "has" } => false,
            PseudoClassSelector { Selectors: { } list } => list.Selectors.All(selector => selector.Steps.Count == 1 && OnlyCompounds(selector.Steps[0].Compound)),
            _ => true,
        });

    /// <summary>The number of identifiers when the values are only identifiers (separated by white space, or with
    /// <paramref name="commas"/> by commas); 0 otherwise.</summary>
    private static int Identifiers(ValueList<ComponentValue> values, bool commas)
    {
        var count = 0;
        var expectIdent = true;
        foreach (var value in values)
        {
            switch (value)
            {
                case CssToken { Kind: CssTokenKind.Whitespace }:
                    continue;
                case CssToken { Kind: CssTokenKind.Ident } when expectIdent || !commas:
                    count++;
                    expectIdent = !commas;
                    continue;
                case CssToken { Kind: CssTokenKind.Comma } when commas && !expectIdent:
                    expectIdent = true;
                    continue;
                default:
                    return 0;
            }
        }

        return commas && expectIdent ? 0 : count;
    }

    /// <summary>:lang() (Selectors 4 §7.2): a comma-separated list of identifiers or strings.</summary>
    private static bool LanguageRanges(ValueList<ComponentValue> values)
    {
        var expectRange = true;
        foreach (var value in values)
        {
            switch (value)
            {
                case CssToken { Kind: CssTokenKind.Whitespace }:
                    continue;
                case CssToken { Kind: CssTokenKind.Ident or CssTokenKind.String } when expectRange:
                    expectRange = false;
                    continue;
                case CssToken { Kind: CssTokenKind.Comma } when !expectRange:
                    expectRange = true;
                    continue;
                default:
                    return false;
            }
        }

        return !expectRange;
    }

    private static bool HasMeaningful(ValueList<ComponentValue> values)
    {
        foreach (var value in values)
        {
            if (value is not CssToken { Kind: CssTokenKind.Whitespace })
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A position in a list of component values.</summary>
    private sealed class Cursor(IReadOnlyList<ComponentValue> values)
    {
        private int _index;

        public bool AtEnd => _index >= values.Count;

        public ComponentValue? Peek => PeekAt(0);

        public ComponentValue? PeekAt(int ahead) => _index + ahead < values.Count ? values[_index + ahead] : null;

        public ComponentValue Next() => values[_index++];

        /// <summary>Skips white space; returns whether there was any.</summary>
        public bool SkipWhitespace()
        {
            var start = _index;
            while (Peek is CssToken { Kind: CssTokenKind.Whitespace })
            {
                _index++;
            }

            return _index > start;
        }
    }
}
