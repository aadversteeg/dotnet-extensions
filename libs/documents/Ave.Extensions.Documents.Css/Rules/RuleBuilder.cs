using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Css.Conditions;
using Ave.Extensions.Documents.Css.Media;
using Ave.Extensions.Documents.Css.Selectors;
using Ave.Extensions.Documents.Css.Syntax;
using Ave.Extensions.ErrorPaths;

namespace Ave.Extensions.Documents.Css.Rules;

/// <summary>
/// Turns the generic rules of CSS Syntax 3 into the typed rules the engine understands: each at-rule's grammar and
/// place (CSS Cascade 5 §2 for <c>@import</c> and §6.4.2 for <c>@layer</c>, CSS Conditional 3 for <c>@media</c> and
/// <c>@supports</c>, CSS Fonts 4 §4 for <c>@font-face</c>, CSS Paged Media 3 §4 for <c>@page</c>), and the selectors of
/// style rules, nested rules relative (CSS Nesting 1 §2). Everything dropped is reported: as invalid (CSS drops it, a
/// browser does the same), ignored, or unsupported.
/// </summary>
internal sealed class RuleBuilder(string sourceName, DiagnosticCollector diagnostics)
{
    /// <summary>At-rules CSS defines that the engine does not implement: refused.</summary>
    internal static readonly FrozenSet<string> UnsupportedAtRules = FrozenSet.Create(
        "namespace", "property", "container", "scope", "starting-style", "counter-style", "font-feature-values",
        "font-palette-values", "view-transition", "position-try", "function", "color-profile", "document");

    /// <summary>At-rules that only drive what the engine ignores by rule (animations): ignored.</summary>
    internal static readonly FrozenSet<string> IgnoredAtRules = FrozenSet.Create("keyframes", "-webkit-keyframes");

    /// <summary>The page-margin rules of CSS Paged Media 3 §4.2.</summary>
    private static readonly FrozenSet<string> MarginRules = FrozenSet.Create(
        "top-left-corner", "top-left", "top-center", "top-right", "top-right-corner", "bottom-left-corner", "bottom-left",
        "bottom-center", "bottom-right", "bottom-right-corner", "left-top", "left-middle", "left-bottom", "right-top",
        "right-middle", "right-bottom");

    /// <summary>The rules of a style sheet (top level).</summary>
    public ValueList<CssRule> StyleSheet(ValueList<SyntaxRule> rules)
    {
        var result = new List<CssRule>();
        var importsAllowed = true;
        foreach (var rule in rules)
        {
            switch (rule)
            {
                case SyntaxAtRule at when Ascii.EqualsIgnoreCase(at.Name, "import"):
                    if (!importsAllowed)
                    {
                        Report(CssErrorCodes.Invalid.Rule, "An @import after another rule is invalid (CSS Cascade 5 §2).", at.Position);
                        continue;
                    }

                    if (Import(at) is { } import)
                    {
                        result.Add(import);
                    }

                    continue;
                case SyntaxAtRule at when Ascii.EqualsIgnoreCase(at.Name, "charset"):
                    // @charset is read when bytes are decoded (CSS Syntax 3 §3.2); as a rule it is nothing.
                    continue;
                case SyntaxAtRule at when Ascii.EqualsIgnoreCase(at.Name, "layer") && at.Block is null:
                    // Layer statements may come before @import (CSS Cascade 5 §2).
                    if (LayerStatement(at) is { } statement)
                    {
                        result.Add(statement);
                    }

                    continue;
                case SyntaxInvalid:
                    continue;
            }

            importsAllowed = false;
            if (Rule(rule, nested: false) is { } built)
            {
                result.Add(built);
            }
        }

        return [.. result];
    }

    /// <summary>One rule, at the top level or nested in a style rule (where declarations become nested declarations
    /// and selectors are relative).</summary>
    private CssRule? Rule(SyntaxRule rule, bool nested)
    {
        switch (rule)
        {
            case SyntaxQualifiedRule qualified:
                return Style(qualified, nested);
            case SyntaxNestedDeclarations declarations:
                return nested ? new NestedDeclarationsRule(declarations.Declarations, declarations.Position) : null;
            case SyntaxAtRule at:
                return AtRule(at, nested);
            default:
                return null;
        }
    }

    private StyleRule? Style(SyntaxQualifiedRule rule, bool nested)
    {
        var parsed = SelectorParser.Parse(rule.Prelude, relative: nested, rule.Position);
        if (parsed.List is not { } selectors)
        {
            if (parsed.TooDeep)
            {
                Report(CssErrorCodes.TooDeep, $"The selector nests selector lists deeper than {CssInputLimits.MaxSelectorNesting}; the rule is not read.", parsed.ErrorPosition);
            }
            else
            {
                Report(CssErrorCodes.Invalid.Selector, $"The selector is invalid ({parsed.Error}); the rule is dropped (Selectors 4 §3.9).", parsed.ErrorPosition);
            }

            return null;
        }

        foreach (var finding in parsed.Findings)
        {
            if (finding.Support == SelectorSupport.Unsupported)
            {
                Report(CssErrorCodes.Unsupported.Selector, $"The selector '{finding.Name}' is not supported.", finding.Position);
            }
            else
            {
                Report(CssErrorCodes.Ignored.Selector, $"The selector '{finding.Name}' never matches in a static document.", finding.Position);
            }
        }

        return new StyleRule(selectors, rule.Declarations, Children(rule.ChildRules), rule.Position);
    }

    private ValueList<CssRule> Children(ValueList<SyntaxRule> rules)
    {
        var children = new List<CssRule>();
        foreach (var child in rules)
        {
            if (Rule(child, nested: true) is { } built)
            {
                children.Add(built);
            }
        }

        return [.. children];
    }

    /// <summary>The contents of a conditional group or layer block: at the top level its rules; nested in a style rule
    /// its declarations and rules (CSS Nesting 1 §3.2).</summary>
    private ValueList<CssRule> GroupContents(SyntaxAtRule at, bool nested)
    {
        var (declarations, rules) = SyntaxParser.Materialise(at.Contents);
        var result = new List<CssRule>();
        if (!declarations.IsEmpty)
        {
            if (nested)
            {
                result.Add(new NestedDeclarationsRule(declarations, declarations[0].Position));
            }
            else
            {
                Report(CssErrorCodes.Invalid.Rule, $"Declarations directly in a top-level @{at.Name} are invalid; they are dropped.", declarations[0].Position);
            }
        }

        foreach (var rule in rules)
        {
            if (rule is SyntaxNestedDeclarations && !nested)
            {
                Report(CssErrorCodes.Invalid.Rule, $"Declarations directly in a top-level @{at.Name} are invalid; they are dropped.", rule.Position);
                continue;
            }

            if (Rule(rule, nested) is { } built)
            {
                result.Add(built);
            }
        }

        return [.. result];
    }

    private CssRule? AtRule(SyntaxAtRule at, bool nested)
    {
        var name = Ascii.ToLower(at.Name);
        switch (name)
        {
            case "media" when at.Block is not null:
                return new MediaRule(MediaQueries.Parse(at.Prelude, sourceName, diagnostics), GroupContents(at, nested), at.Position);
            case "supports" when at.Block is not null:
                if (SupportsConditions.Parse(at.Prelude) is not { } condition)
                {
                    Report(CssErrorCodes.Invalid.Rule, "The @supports condition does not parse; the rule is dropped.", at.Position);
                    return null;
                }

                return new SupportsRule(condition, GroupContents(at, nested), at.Position);
            case "layer" when at.Block is not null:
                var names = LayerNames(at);
                if (names is null || names.Count > 1)
                {
                    Report(CssErrorCodes.Invalid.Rule, "A @layer block takes one layer name or none.", at.Position);
                    return null;
                }

                return new LayerBlockRule(names.Count == 0 ? null : names[0], GroupContents(at, nested), at.Position);
            case "layer" when !nested:
                return LayerStatement(at);
            case "font-face" when !nested && at.Block is not null:
                return FontFace(at);
            case "page" when !nested && at.Block is not null:
                return Page(at);
            case "import" or "charset":
                Report(CssErrorCodes.Invalid.Rule, $"@{name} is invalid here.", at.Position);
                return null;
        }

        if (UnsupportedAtRules.Contains(name))
        {
            Report(CssErrorCodes.Unsupported.AtRule, $"@{at.Name} is not supported.", at.Position);
        }
        else if (IgnoredAtRules.Contains(name))
        {
            Report(CssErrorCodes.Ignored.AtRule, $"@{at.Name} only drives animations, which have no effect in a static document.", at.Position);
        }
        else
        {
            Report(CssErrorCodes.Invalid.Rule, $"@{at.Name} is not a valid at-rule here; it is dropped.", at.Position);
        }

        return null;
    }

    /// <summary>A <c>@layer</c> statement: one or more layer names separated by commas.</summary>
    private LayerStatementRule? LayerStatement(SyntaxAtRule at)
    {
        var names = LayerNames(at);
        if (names is null || names.Count == 0)
        {
            Report(CssErrorCodes.Invalid.Rule, "A @layer statement needs layer names.", at.Position);
            return null;
        }

        return new LayerStatementRule([.. names], at.Position);
    }

    /// <summary>The layer names of a prelude: &lt;layer-name&gt; = &lt;ident&gt; ['.' &lt;ident&gt;]*, no white space inside,
    /// comma-separated; the CSS-wide keywords are reserved (CSS Cascade 5 §6.4.2). Null when invalid.</summary>
    private static List<string>? LayerNames(SyntaxAtRule at)
    {
        var names = new List<string>();
        var current = new System.Text.StringBuilder();
        var expectIdent = true;
        var previousEnd = -1;
        foreach (var value in at.Prelude)
        {
            switch (value)
            {
                case CssToken { Kind: CssTokenKind.Whitespace }:
                    previousEnd = -1;
                    continue;
                case CssToken { Kind: CssTokenKind.Comma }:
                    if (expectIdent || current.Length == 0)
                    {
                        return null;
                    }

                    names.Add(current.ToString());
                    current.Clear();
                    expectIdent = true;
                    continue;
                case CssToken { Kind: CssTokenKind.Ident } ident when expectIdent:
                    if (current.Length > 0 && ident.SourceStart != previousEnd)
                    {
                        return null;
                    }

                    if (ident.Text is var text && (Ascii.EqualsIgnoreCase(text, "initial") || Ascii.EqualsIgnoreCase(text, "inherit") || Ascii.EqualsIgnoreCase(text, "unset") || Ascii.EqualsIgnoreCase(text, "revert") || Ascii.EqualsIgnoreCase(text, "revert-layer") || Ascii.EqualsIgnoreCase(text, "default")))
                    {
                        return null;
                    }

                    current.Append(ident.Text);
                    expectIdent = false;
                    previousEnd = ident.SourceEnd;
                    continue;
                case CssToken dot when dot.IsDelim('.') && !expectIdent && dot.SourceStart == previousEnd:
                    current.Append('.');
                    expectIdent = true;
                    previousEnd = dot.SourceEnd;
                    continue;
                default:
                    return null;
            }
        }

        if (current.Length > 0)
        {
            if (expectIdent)
            {
                return null;
            }

            names.Add(current.ToString());
        }
        else if (names.Count > 0)
        {
            return null;
        }

        return names;
    }

    /// <summary>@import [ &lt;url&gt; | &lt;string&gt; ] [ layer | layer(&lt;layer-name&gt;) ]? [supports(…)]? &lt;media-query-list&gt;?
    /// (CSS Cascade 5 §2).</summary>
    private ImportRule? Import(SyntaxAtRule at)
    {
        var values = at.Prelude.Items;
        var index = 0;
        void SkipSpace()
        {
            while (index < values.Length && values[index] is CssToken { Kind: CssTokenKind.Whitespace })
            {
                index++;
            }
        }

        SkipSpace();
        string? url = values.Length > index ? values[index] switch
        {
            CssToken { Kind: CssTokenKind.String or CssTokenKind.Url } token => token.Text,
            FunctionValue function when function.IsNamed("url") && function.Values.FirstOrDefault(v => v is CssToken { Kind: CssTokenKind.String }) is CssToken inner => inner.Text,
            _ => null,
        } : null;
        if (url is null || at.Block is not null)
        {
            Report(CssErrorCodes.Invalid.Rule, "An @import needs a URL or a string, and no block.", at.Position);
            return null;
        }

        index++;
        SkipSpace();
        string? layer = null;
        if (index < values.Length && values[index] is CssToken layerWord && layerWord.IsIdent("layer"))
        {
            layer = string.Empty;
            index++;
        }
        else if (index < values.Length && values[index] is FunctionValue layerFunction && layerFunction.IsNamed("layer"))
        {
            var names = LayerNames(at with { Prelude = layerFunction.Values });
            if (names is not { Count: 1 })
            {
                Report(CssErrorCodes.Invalid.Rule, "layer() in @import takes one layer name.", layerFunction.Position);
                return null;
            }

            layer = names[0];
            index++;
        }

        SkipSpace();
        SupportsCondition? supports = null;
        if (index < values.Length && values[index] is FunctionValue supportsFunction && supportsFunction.IsNamed("supports"))
        {
            supports = SupportsConditions.Parse(supportsFunction.Values)
                ?? (SyntaxParser.DeclarationFrom(supportsFunction.Values) is { } declaration ? new SupportsDeclaration(declaration) : null);
            if (supports is null)
            {
                Report(CssErrorCodes.Invalid.Rule, "supports() in @import does not parse.", supportsFunction.Position);
                return null;
            }

            index++;
        }

        var media = MediaQueries.Parse(values[index..], sourceName, diagnostics);
        return new ImportRule(url, media, supports, layer, at.Position);
    }

    /// <summary>@font-face (CSS Fonts 4 §4): font-family and src are required; other descriptors take their initial
    /// values. A <c>local()</c> source is refused: the engine never looks at the fonts installed on a machine.</summary>
    private FontFaceRule? FontFace(SyntaxAtRule at)
    {
        var (declarations, _) = SyntaxParser.Materialise(at.Contents);
        string? family = null;
        var sources = new List<FontFaceSource>();
        var weight = (400, 400);
        var style = "normal";
        var ranges = new List<(int, int)>();
        foreach (var declaration in declarations)
        {
            var values = declaration.Value.Items.RemoveAll(value => value is CssToken { Kind: CssTokenKind.Whitespace });
            switch (Ascii.ToLower(declaration.Name))
            {
                case "font-family":
                    family = values.Length == 1 && values[0] is CssToken { Kind: CssTokenKind.String } quoted
                        ? quoted.Text
                        : values.All(value => value is CssToken { Kind: CssTokenKind.Ident }) && values.Length > 0
                            ? string.Join(' ', values.Select(value => ((CssToken)value).Text))
                            : null;
                    break;
                case "src":
                    FontSources(declaration, sources);
                    break;
                case "font-weight":
                    weight = FontWeights(values) ?? weight;
                    break;
                case "font-style":
                    style = values.Length > 0 && values[0] is CssToken { Kind: CssTokenKind.Ident } styleName ? Ascii.ToLower(styleName.Text) : style;
                    break;
                case "unicode-range":
                    foreach (var value in values)
                    {
                        if (value is CssToken { Kind: CssTokenKind.UnicodeRange } range)
                        {
                            ranges.Add((range.RangeStart, range.RangeEnd));
                        }
                    }

                    break;
                case "font-display" or "font-stretch" or "font-feature-settings" or "font-variation-settings" or "ascent-override" or "descent-override" or "line-gap-override" or "size-adjust" or "font-named-instance" or "font-language-override":
                    Report(CssErrorCodes.Unsupported.Value, $"The @font-face descriptor '{declaration.Name}' is not supported.", declaration.Position);
                    break;
                default:
                    Report(CssErrorCodes.Invalid.Value, $"'{declaration.Name}' is not a @font-face descriptor.", declaration.Position);
                    break;
            }
        }

        if (family is null || sources.Count == 0)
        {
            Report(CssErrorCodes.Invalid.Rule, "A @font-face needs font-family and a url() source; it is dropped.", at.Position);
            return null;
        }

        return new FontFaceRule(family, [.. sources], weight, style, [.. ranges], at.Position);
    }

    private void FontSources(Declaration declaration, List<FontFaceSource> sources)
    {
        var current = new List<ComponentValue>();
        void Flush()
        {
            string? url = null;
            string? format = null;
            foreach (var value in current)
            {
                switch (value)
                {
                    case CssToken { Kind: CssTokenKind.Url } token:
                        url = token.Text;
                        break;
                    case FunctionValue function when function.IsNamed("url"):
                        url = function.Values.OfType<CssToken>().FirstOrDefault(token => token.Kind == CssTokenKind.String)?.Text;
                        break;
                    case FunctionValue function when function.IsNamed("format"):
                        format = function.Values.OfType<CssToken>().FirstOrDefault(token => token.Kind is CssTokenKind.String or CssTokenKind.Ident) is { } hint ? Ascii.ToLower(hint.Text) : null;
                        break;
                    case FunctionValue function when function.IsNamed("local"):
                        Report(CssErrorCodes.Unsupported.Value, "local() in @font-face src is not supported: the engine never looks at installed fonts.", function.Position);
                        break;
                }
            }

            if (url is not null)
            {
                sources.Add(new FontFaceSource(url, format));
            }

            current.Clear();
        }

        foreach (var value in declaration.Value)
        {
            if (value is CssToken { Kind: CssTokenKind.Comma })
            {
                Flush();
            }
            else
            {
                current.Add(value);
            }
        }

        Flush();
    }

    /// <summary>The font-weight descriptor (CSS Fonts 4 §4.4): normal, bold, a number, or two numbers as a range.</summary>
    private static (int, int)? FontWeights(System.Collections.Immutable.ImmutableArray<ComponentValue> values)
    {
        int? One(ComponentValue value) => value switch
        {
            CssToken token when token.IsIdent("normal") => 400,
            CssToken token when token.IsIdent("bold") => 700,
            CssToken { Kind: CssTokenKind.Number } number when number.Number is >= 1 and <= 1000 => (int)number.Number,
            _ => null,
        };

        return values.Length switch
        {
            1 when One(values[0]) is { } weight => (weight, weight),
            2 when One(values[0]) is { } low && One(values[1]) is { } high => (System.Math.Min(low, high), System.Math.Max(low, high)),
            _ => null,
        };
    }

    /// <summary>@page (CSS Paged Media 3 §4.1): page selectors, declarations and margin rules.</summary>
    private PageRule? Page(SyntaxAtRule at)
    {
        var selectors = new List<PageSelector>();
        var parts = new List<List<ComponentValue>> { new() };
        foreach (var value in at.Prelude)
        {
            if (value is CssToken { Kind: CssTokenKind.Comma })
            {
                parts.Add([]);
            }
            else if (value is not CssToken { Kind: CssTokenKind.Whitespace })
            {
                parts[^1].Add(value);
            }
        }

        if (!(parts.Count == 1 && parts[0].Count == 0))
        {
            foreach (var part in parts)
            {
                if (PageSelectorOf(part) is not { } selector)
                {
                    Report(CssErrorCodes.Invalid.Selector, "The @page selector is invalid; the rule is dropped.", at.Position);
                    return null;
                }

                selectors.Add(selector);
            }
        }

        var declarations = new List<Declaration>();
        var margins = new List<PageMarginRule>();
        foreach (var item in at.Contents)
        {
            switch (item.Rule)
            {
                case null:
                    declarations.AddRange(item.Declarations);
                    break;
                case SyntaxAtRule margin when MarginRules.Contains(Ascii.ToLower(margin.Name)) && margin.Block is not null:
                    margins.Add(new PageMarginRule(Ascii.ToLower(margin.Name), SyntaxParser.Materialise(margin.Contents).Declarations, margin.Position));
                    break;
                case SyntaxAtRule other:
                    Report(CssErrorCodes.Invalid.Rule, $"@{other.Name} is not a page-margin rule.", other.Position);
                    break;
            }
        }

        return new PageRule([.. selectors], [.. declarations], [.. margins], at.Position);
    }

    /// <summary>One page selector: an optional ident, then :first, :left, :right, :blank, no white space inside.</summary>
    private static PageSelector? PageSelectorOf(List<ComponentValue> tokens)
    {
        var index = 0;
        string? name = null;
        if (index < tokens.Count && tokens[index] is CssToken { Kind: CssTokenKind.Ident } ident)
        {
            name = ident.Text;
            index++;
        }

        var pseudo = new List<string>();
        while (index < tokens.Count)
        {
            if (tokens[index] is not CssToken { Kind: CssTokenKind.Colon } || index + 1 >= tokens.Count || tokens[index + 1] is not CssToken { Kind: CssTokenKind.Ident } pseudoName)
            {
                return null;
            }

            var lower = Ascii.ToLower(pseudoName.Text);
            if (lower is not ("first" or "left" or "right" or "blank"))
            {
                return null;
            }

            pseudo.Add(lower);
            index += 2;
        }

        return name is null && pseudo.Count == 0 ? null : new PageSelector(name, [.. pseudo]);
    }

    private void Report(ErrorCode code, string message, TextPosition position) =>
        diagnostics.Add(CssDiagnostics.At(code, message, sourceName, position));
}
