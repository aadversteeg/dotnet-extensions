using Ave.Extensions.Documents.Core;

namespace Ave.Extensions.Documents.Css.Syntax;

/// <summary>
/// A component value (CSS Syntax 3 §5.2): a preserved token (<see cref="CssToken"/>), a simple block
/// (<see cref="SimpleBlock"/>) or a function (<see cref="FunctionValue"/>). Declarations, rule preludes and the
/// computed value of a custom property are lists of them.
/// </summary>
/// <param name="Position">Where it starts in its document.</param>
public abstract record ComponentValue(TextPosition Position)
{
    /// <summary>The index in the style sheet's text where it starts (for the original text of a custom property).</summary>
    internal int SourceStart { get; init; }

    /// <summary>The index just past its end.</summary>
    internal int SourceEnd { get; init; }
}

/// <summary>
/// A token (CSS Syntax 3 §4). Only the preserved tokens appear in component values: never <c>function</c>,
/// <c>{</c>, <c>[</c>, <c>(</c> or the end of the input, which the parser turns into functions and blocks.
/// </summary>
/// <param name="Kind">The kind of token.</param>
/// <param name="Text">The value: of an ident, at-keyword (without @), hash (without #), string, url or function (its
/// name); the code point of a delim; the unit of a dimension; empty for the others.</param>
/// <param name="Position">Where it starts.</param>
public sealed record CssToken(CssTokenKind Kind, string Text, TextPosition Position) : ComponentValue(Position)
{
    /// <summary>The numeric value of a number, percentage or dimension (for a percentage: 50 for 50%).</summary>
    public double Number { get; init; }

    /// <summary>For a number or dimension: whether its type flag is "integer" (no decimal point, no exponent).</summary>
    public bool IsInteger { get; init; }

    /// <summary>For a number, percentage or dimension: the sign it was written with, '+', '-', or null.</summary>
    public char? Sign { get; init; }

    /// <summary>For a hash: whether its type flag is "id" (its value is an identifier, so it can be an ID selector).</summary>
    public bool IsIdHash { get; init; }

    /// <summary>For a number, percentage or dimension: the number as written (for example <c>+.5e1</c>).</summary>
    public string Representation { get; init; } = string.Empty;

    /// <summary>For a unicode-range: the first code point.</summary>
    public int RangeStart { get; init; }

    /// <summary>For a unicode-range: the last code point.</summary>
    public int RangeEnd { get; init; }

    /// <summary>Whether this is an ident whose value is <paramref name="name"/>, ASCII case-insensitively.</summary>
    public bool IsIdent(string name) => Kind == CssTokenKind.Ident && Ascii.EqualsIgnoreCase(Text, name);

    /// <summary>Whether this is a delim with this code point.</summary>
    public bool IsDelim(char c) => Kind == CssTokenKind.Delim && Text.Length == 1 && Text[0] == c;
}

/// <summary>A simple block (CSS Syntax 3 §5.2): <c>{…}</c>, <c>[…]</c> or <c>(…)</c>.</summary>
/// <param name="Opening">Which: <see cref="CssTokenKind.OpenCurly"/>, <see cref="CssTokenKind.OpenSquare"/> or
/// <see cref="CssTokenKind.OpenParen"/>.</param>
/// <param name="Values">Its content.</param>
/// <param name="Position">Where its opening bracket is.</param>
public sealed record SimpleBlock(CssTokenKind Opening, ValueList<ComponentValue> Values, TextPosition Position) : ComponentValue(Position);

/// <summary>A function (CSS Syntax 3 §5.2): a name and its arguments as component values.</summary>
/// <param name="Name">The name, as written (compare it ASCII case-insensitively).</param>
/// <param name="Values">Its arguments, commas and white space included.</param>
/// <param name="Position">Where its name starts.</param>
public sealed record FunctionValue(string Name, ValueList<ComponentValue> Values, TextPosition Position) : ComponentValue(Position)
{
    /// <summary>Whether its name is <paramref name="name"/>, ASCII case-insensitively.</summary>
    public bool IsNamed(string name) => Ascii.EqualsIgnoreCase(Name, name);
}

/// <summary>The kinds of token of CSS Syntax 3 §4.</summary>
public enum CssTokenKind
{
    /// <summary>&lt;ident-token&gt;.</summary>
    Ident,

    /// <summary>&lt;function-token&gt; (never in a component value: the parser makes a <see cref="FunctionValue"/>).</summary>
    Function,

    /// <summary>&lt;at-keyword-token&gt;.</summary>
    AtKeyword,

    /// <summary>&lt;hash-token&gt;.</summary>
    Hash,

    /// <summary>&lt;string-token&gt;.</summary>
    String,

    /// <summary>&lt;bad-string-token&gt;: a string with a line break in it.</summary>
    BadString,

    /// <summary>&lt;url-token&gt;: an unquoted <c>url(…)</c>.</summary>
    Url,

    /// <summary>&lt;bad-url-token&gt;.</summary>
    BadUrl,

    /// <summary>&lt;delim-token&gt;: one code point.</summary>
    Delim,

    /// <summary>&lt;number-token&gt;.</summary>
    Number,

    /// <summary>&lt;percentage-token&gt;.</summary>
    Percentage,

    /// <summary>&lt;dimension-token&gt;.</summary>
    Dimension,

    /// <summary>&lt;unicode-range-token&gt; (only in the value of a unicode-range descriptor).</summary>
    UnicodeRange,

    /// <summary>&lt;whitespace-token&gt;.</summary>
    Whitespace,

    /// <summary>&lt;CDO-token&gt;: <c>&lt;!--</c>.</summary>
    Cdo,

    /// <summary>&lt;CDC-token&gt;: <c>--&gt;</c>.</summary>
    Cdc,

    /// <summary>&lt;colon-token&gt;.</summary>
    Colon,

    /// <summary>&lt;semicolon-token&gt;.</summary>
    Semicolon,

    /// <summary>&lt;comma-token&gt;.</summary>
    Comma,

    /// <summary>&lt;[-token&gt; (never in a component value: the parser makes a block).</summary>
    OpenSquare,

    /// <summary>&lt;]-token&gt;.</summary>
    CloseSquare,

    /// <summary>&lt;(-token&gt; (never in a component value).</summary>
    OpenParen,

    /// <summary>&lt;)-token&gt;.</summary>
    CloseParen,

    /// <summary>&lt;{-token&gt; (never in a component value).</summary>
    OpenCurly,

    /// <summary>&lt;}-token&gt;.</summary>
    CloseCurly,

    /// <summary>&lt;EOF-token&gt; (never in a component value).</summary>
    EndOfFile,
}
