using Ave.Extensions.Documents.Core;

namespace Ave.Extensions.Documents.Css.Syntax;

/// <summary>
/// A rule as CSS Syntax 3 produces it (§5.2), before any rule grammar is applied: an at-rule, a qualified rule, or a run
/// of declarations between nested rules. The rule builder (<c>Rules/RuleBuilder.cs</c>) turns these into the typed
/// <c>CssRule</c> set.
/// </summary>
/// <param name="Position">Where the rule starts.</param>
internal abstract record SyntaxRule(TextPosition Position);

/// <summary>An at-rule: a name, a prelude and, for a block at-rule, the block and its parsed contents.</summary>
/// <param name="Name">The name without the @, as written.</param>
/// <param name="Prelude">The component values between the name and the block or semicolon.</param>
/// <param name="Block">The {}-block as component values; null for a statement at-rule (ending in a semicolon).</param>
/// <param name="Contents">The block's contents parsed as declarations and rules (§5.5.5); empty without a block.</param>
/// <param name="Position">Where the @ is.</param>
internal sealed record SyntaxAtRule(string Name, ValueList<ComponentValue> Prelude, SimpleBlock? Block, ValueList<SyntaxBlockItem> Contents, TextPosition Position) : SyntaxRule(Position);

/// <summary>A qualified rule: a prelude (for a style rule, its selector) and a block.</summary>
/// <param name="Prelude">The component values before the block.</param>
/// <param name="Block">The {}-block as component values.</param>
/// <param name="Declarations">The declarations before the first nested rule (§5.5.3: "the first item of child
/// rules").</param>
/// <param name="ChildRules">The nested rules, runs of later declarations among them as
/// <see cref="SyntaxNestedDeclarations"/>.</param>
/// <param name="Position">Where the prelude starts.</param>
internal sealed record SyntaxQualifiedRule(ValueList<ComponentValue> Prelude, SimpleBlock Block, ValueList<Declaration> Declarations, ValueList<SyntaxRule> ChildRules, TextPosition Position) : SyntaxRule(Position);

/// <summary>A nested declarations rule (CSS Nesting 1 §3.3): declarations after a nested rule.</summary>
/// <param name="Declarations">The declarations.</param>
/// <param name="Position">Where the first one starts.</param>
internal sealed record SyntaxNestedDeclarations(ValueList<Declaration> Declarations, TextPosition Position) : SyntaxRule(Position);

/// <summary>A place where the parser consumed a rule or declaration and got "nothing" (§5.5.1, §5.5.5): an invalid
/// qualified rule, or a declaration that does not parse. Kept so the drop can be reported; it means nothing else.</summary>
/// <param name="Position">Where the dropped text starts.</param>
internal sealed record SyntaxInvalid(TextPosition Position) : SyntaxRule(Position);

/// <summary>One item of a block's contents (§5.5.5): a rule, or a list of declarations.</summary>
/// <param name="Rule">The rule, or null.</param>
/// <param name="Declarations">The declarations, when <paramref name="Rule"/> is null.</param>
internal sealed record SyntaxBlockItem(SyntaxRule? Rule, ValueList<Declaration> Declarations);
