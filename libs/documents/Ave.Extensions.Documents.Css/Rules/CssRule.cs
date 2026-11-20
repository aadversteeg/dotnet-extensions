using System.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Css.Conditions;
using Ave.Extensions.Documents.Css.Media;
using Ave.Extensions.Documents.Css.Selectors;
using Ave.Extensions.Documents.Css.Syntax;

namespace Ave.Extensions.Documents.Css.Rules;

/// <summary>A style sheet: its rules in order, and its name for diagnostics.</summary>
/// <param name="Rules">The rules.</param>
/// <param name="SourceName">The name the caller gave it (a file name or URL).</param>
public sealed record StyleSheet(ValueList<CssRule> Rules, string SourceName);

/// <summary>
/// A rule the engine understands. One closed set: style rules (with nested rules, CSS Nesting 1), nested declarations,
/// <c>@media</c>, <c>@supports</c>, <c>@layer</c> (block and statement), <c>@import</c>, <c>@font-face</c> and
/// <c>@page</c>. Every other at-rule is dropped with a diagnostic when the style sheet is read.
/// </summary>
/// <param name="Position">Where the rule starts.</param>
public abstract record CssRule(TextPosition Position);

/// <summary>A style rule (CSS Syntax 3 §9; CSS Nesting 1 §2): a selector list, its declarations, and nested rules.</summary>
/// <param name="Selectors">The selectors; in a nested rule they are relative and may hold <c>&amp;</c>.</param>
/// <param name="Declarations">The declarations before the first nested rule.</param>
/// <param name="ChildRules">Nested style rules, conditional rules and nested declarations, in order.</param>
/// <param name="Position">Where the selector starts.</param>
public sealed record StyleRule(SelectorList Selectors, ValueList<Declaration> Declarations, ValueList<CssRule> ChildRules, TextPosition Position) : CssRule(Position);

/// <summary>Declarations that follow a nested rule (CSS Nesting 1 §3.3): they apply as the parent's own declarations
/// do, at their place in the order.</summary>
/// <param name="Declarations">The declarations.</param>
/// <param name="Position">Where the first starts.</param>
public sealed record NestedDeclarationsRule(ValueList<Declaration> Declarations, TextPosition Position) : CssRule(Position);

/// <summary><c>@media</c> (CSS Conditional 3 §3).</summary>
/// <param name="Queries">The media query list.</param>
/// <param name="Rules">The rules inside.</param>
/// <param name="Position">Where the @ is.</param>
public sealed record MediaRule(MediaQueryList Queries, ValueList<CssRule> Rules, TextPosition Position) : CssRule(Position);

/// <summary><c>@supports</c> (CSS Conditional 3 §6).</summary>
/// <param name="Condition">The condition.</param>
/// <param name="Rules">The rules inside.</param>
/// <param name="Position">Where the @ is.</param>
public sealed record SupportsRule(SupportsCondition Condition, ValueList<CssRule> Rules, TextPosition Position) : CssRule(Position);

/// <summary>A <c>@layer</c> block (CSS Cascade 5 §6.4.2).</summary>
/// <param name="Name">The layer's name (dotted parts joined with '.'), or null for an anonymous layer.</param>
/// <param name="Rules">The rules inside.</param>
/// <param name="Position">Where the @ is.</param>
public sealed record LayerBlockRule(string? Name, ValueList<CssRule> Rules, TextPosition Position) : CssRule(Position);

/// <summary>A <c>@layer</c> statement (CSS Cascade 5 §6.4.2): it declares layers, fixing their order.</summary>
/// <param name="Names">The layer names.</param>
/// <param name="Position">Where the @ is.</param>
public sealed record LayerStatementRule(ValueList<string> Names, TextPosition Position) : CssRule(Position);

/// <summary><c>@import</c> (CSS Cascade 5 §2): the caller resolves the URL and the cascade puts the imported sheet's
/// rules here, under the import's conditions and layer.</summary>
/// <param name="Url">The URL as written.</param>
/// <param name="Media">The media query list (all when none).</param>
/// <param name="Supports">The supports condition, or null.</param>
/// <param name="Layer">The layer: null for none, the empty string for an anonymous layer (<c>layer</c>), else its
/// name.</param>
/// <param name="Position">Where the @ is.</param>
public sealed record ImportRule(string Url, MediaQueryList Media, SupportsCondition? Supports, string? Layer, TextPosition Position) : CssRule(Position);

/// <summary><c>@font-face</c> (CSS Fonts 4 §4): the font's descriptors.</summary>
/// <param name="Family">The font-family descriptor.</param>
/// <param name="Sources">The url() sources with their format hint, in order.</param>
/// <param name="WeightRange">The font-weight descriptor as a range (one weight is a range of one).</param>
/// <param name="Style">The font-style descriptor: normal, italic or oblique.</param>
/// <param name="UnicodeRanges">The unicode-range descriptor; empty for all of Unicode.</param>
/// <param name="Position">Where the @ is.</param>
public sealed record FontFaceRule(string Family, ValueList<FontFaceSource> Sources, (int Low, int High) WeightRange, string Style, ValueList<(int First, int Last)> UnicodeRanges, TextPosition Position) : CssRule(Position)
{
    /// <summary>The URL its sources are resolved against (CSS Values 4 §4.5.1: the URL of the style sheet that declares
    /// it; for a style element, the document's base URL), set by the cascade (<see cref="Cascade.CascadeIndex"/>); null
    /// when the caller gives none.</summary>
    public string? BaseUrl { get; init; }
}

/// <summary>One source of a <c>@font-face</c> src descriptor.</summary>
/// <param name="Url">The URL as written (the caller resolves it).</param>
/// <param name="Format">The format() hint, lowercase, or null.</param>
public sealed record FontFaceSource(string Url, string? Format);

/// <summary><c>@page</c> (CSS Paged Media 3 §4.1): page selectors, declarations, margin rules.</summary>
/// <param name="Selectors">The page selectors; empty matches every page.</param>
/// <param name="Declarations">The declarations (size, margin, page properties).</param>
/// <param name="Margins">The page-margin rules (§4.2: <c>@top-center</c> …).</param>
/// <param name="Position">Where the @ is.</param>
public sealed record PageRule(ValueList<PageSelector> Selectors, ValueList<Declaration> Declarations, ValueList<PageMarginRule> Margins, TextPosition Position) : CssRule(Position);

/// <summary>A page selector (CSS Paged Media 3 §4.1): an optional page name and pseudo-classes.</summary>
/// <param name="Name">The page type name, or null.</param>
/// <param name="PseudoClasses">The pseudo-classes, lowercase: first, left, right, blank.</param>
public sealed record PageSelector(string? Name, ValueList<string> PseudoClasses)
{
    /// <summary>The specificity (§4.1): page name, then <c>:first</c> and <c>:blank</c>, then <c>:left</c> and
    /// <c>:right</c>.</summary>
    public Specificity Specificity => new(
        Name is null ? 0 : 1,
        PseudoClasses.Count(pseudo => pseudo is "first" or "blank"),
        PseudoClasses.Count(pseudo => pseudo is "left" or "right"));
}

/// <summary>A page-margin rule (CSS Paged Media 3 §4.2).</summary>
/// <param name="Name">The margin box, lowercase: top-left-corner, top-left, top-center … bottom-right-corner.</param>
/// <param name="Declarations">The declarations.</param>
/// <param name="Position">Where the @ is.</param>
public sealed record PageMarginRule(string Name, ValueList<Declaration> Declarations, TextPosition Position);
