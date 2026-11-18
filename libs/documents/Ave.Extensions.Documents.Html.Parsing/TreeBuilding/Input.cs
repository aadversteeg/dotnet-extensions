using Ave.Extensions.Documents.Html.Parsing.Tokenizing;

namespace Ave.Extensions.Documents.Html.Parsing.TreeBuilding;

/// <summary>
/// One token as tree construction sees it: a single character token (the tokenizer emits runs; tree construction takes
/// them one character at a time, as the standard describes), or any other token.
/// </summary>
/// <param name="Kind">The kind of token.</param>
/// <param name="Token">The token; null for a character.</param>
/// <param name="Char">For a character: its code point.</param>
/// <param name="Source">Where it was written: the character's index, or the token's start.</param>
internal readonly record struct Input(TokenKind Kind, Token? Token, int Char, int Source)
{
    /// <summary>A token other than a character.</summary>
    public static Input Of(Token token) => new(token.Kind, token, 0, token.Start);

    /// <summary>A character token.</summary>
    public static Input Character(int c, int source) => new(TokenKind.Characters, null, c, source);

    /// <summary>The tag name of a tag token, or null.</summary>
    public string? Name => Token?.Name;

    /// <summary>A start tag with one of these names.</summary>
    public bool IsStart(params string[] names) => Kind == TokenKind.StartTag && System.Array.IndexOf(names, Token!.Name) >= 0;

    /// <summary>An end tag with one of these names.</summary>
    public bool IsEnd(params string[] names) => Kind == TokenKind.EndTag && System.Array.IndexOf(names, Token!.Name) >= 0;

    /// <summary>A character token that is ASCII whitespace (tab, line feed, form feed, carriage return, space).</summary>
    public bool IsWhitespace => Kind == TokenKind.Characters && Ascii.IsWhitespace(Char);
}
