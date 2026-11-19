using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Ave.Extensions.Documents.Css.Syntax;

/// <summary>
/// Writes component values back as CSS text (CSS Syntax 3 §10; CSSOM §2.1 for identifiers and strings), so that reading
/// the text again gives the same values: an empty comment goes between two tokens that would otherwise run together.
/// Used for the computed value of a custom property after <c>var()</c> substitution and for the values diagnostics
/// quote.
/// </summary>
internal static class CssSerializer
{
    /// <summary>The text of component values.</summary>
    public static string Serialize(IEnumerable<ComponentValue> values)
    {
        var text = new StringBuilder();
        var previous = Piece.None;
        foreach (var value in values)
        {
            Write(value, text, ref previous);
        }

        return text.ToString();
    }

    /// <summary>The most characters of one input name or value a diagnostic quotes.</summary>
    public const int MaxQuotedCharacters = 100;

    /// <summary>The text of component values for a message: trimmed, at most <see cref="MaxQuotedCharacters"/>
    /// characters (a value can be megabytes).</summary>
    public static string Shortened(IEnumerable<ComponentValue> values) => Shortened(Serialize(values).Trim());

    /// <summary>Text from the input for a message: itself, or its first <see cref="MaxQuotedCharacters"/> characters
    /// followed by <c>…</c>; a cut never splits a surrogate pair.</summary>
    public static string Shortened(string text)
    {
        if (text.Length <= MaxQuotedCharacters)
        {
            return text;
        }

        var length = char.IsHighSurrogate(text[MaxQuotedCharacters - 1]) ? MaxQuotedCharacters - 1 : MaxQuotedCharacters;
        return text.Substring(0, length) + "…";
    }

    /// <summary>What kind of text the last write ended with, for the comment rule of §10.</summary>
    private enum Piece
    {
        None,
        Ident,
        Function,
        Url,
        AtKeyword,
        Hash,
        Number,
        Percentage,
        Dimension,
        Cdc,
        OpenParen,
        Hyphen,
        NumberSign,
        At,
        DotOrPlus,
        Slash,
        Asterisk,
        Percent,
        Other,
    }

    private static void Write(ComponentValue value, StringBuilder text, ref Piece previous)
    {
        switch (value)
        {
            case FunctionValue function:
                Emit(text, ref previous, Piece.Function, Identifier(function.Name) + "(");
                foreach (var inner in function.Values)
                {
                    Write(inner, text, ref previous);
                }

                Emit(text, ref previous, Piece.Other, ")");
                break;
            case SimpleBlock block:
                var (open, close) = block.Opening switch
                {
                    CssTokenKind.OpenParen => ("(", ")"),
                    CssTokenKind.OpenSquare => ("[", "]"),
                    _ => ("{", "}"),
                };
                Emit(text, ref previous, open == "(" ? Piece.OpenParen : Piece.Other, open);
                foreach (var inner in block.Values)
                {
                    Write(inner, text, ref previous);
                }

                Emit(text, ref previous, Piece.Other, close);
                break;
            case CssToken token:
                WriteToken(token, text, ref previous);
                break;
        }
    }

    private static void WriteToken(CssToken token, StringBuilder text, ref Piece previous)
    {
        switch (token.Kind)
        {
            case CssTokenKind.Ident:
                Emit(text, ref previous, Piece.Ident, Identifier(token.Text));
                break;
            case CssTokenKind.AtKeyword:
                Emit(text, ref previous, Piece.AtKeyword, "@" + Identifier(token.Text));
                break;
            case CssTokenKind.Hash:
                Emit(text, ref previous, Piece.Hash, "#" + Name(token.Text));
                break;
            case CssTokenKind.String:
                Emit(text, ref previous, Piece.Other, String(token.Text));
                break;
            case CssTokenKind.BadString:
                Emit(text, ref previous, Piece.Other, "\"" + token.Text);
                break;
            case CssTokenKind.Url:
                Emit(text, ref previous, Piece.Url, "url(" + Url(token.Text) + ")");
                break;
            case CssTokenKind.BadUrl:
                Emit(text, ref previous, Piece.Url, "url(" + token.Text + ")");
                break;
            case CssTokenKind.Number:
                Emit(text, ref previous, Piece.Number, NumberText(token));
                break;
            case CssTokenKind.Percentage:
                Emit(text, ref previous, Piece.Percentage, NumberText(token) + "%");
                break;
            case CssTokenKind.Dimension:
                Emit(text, ref previous, Piece.Dimension, NumberText(token) + Unit(token.Text));
                break;
            case CssTokenKind.Whitespace:
                Emit(text, ref previous, Piece.Other, " ");
                break;
            case CssTokenKind.Cdo:
                Emit(text, ref previous, Piece.Other, "<!--");
                break;
            case CssTokenKind.Cdc:
                Emit(text, ref previous, Piece.Cdc, "-->");
                break;
            case CssTokenKind.Colon:
                Emit(text, ref previous, Piece.Other, ":");
                break;
            case CssTokenKind.Semicolon:
                Emit(text, ref previous, Piece.Other, ";");
                break;
            case CssTokenKind.Comma:
                Emit(text, ref previous, Piece.Other, ",");
                break;
            case CssTokenKind.UnicodeRange:
                Emit(text, ref previous, Piece.Ident, token.RangeStart == token.RangeEnd
                    ? $"U+{token.RangeStart:X}"
                    : $"U+{token.RangeStart:X}-{token.RangeEnd:X}");
                break;
            case CssTokenKind.Delim:
                var piece = token.Text switch
                {
                    "-" => Piece.Hyphen,
                    "#" => Piece.NumberSign,
                    "@" => Piece.At,
                    "." or "+" => Piece.DotOrPlus,
                    "/" => Piece.Slash,
                    "*" => Piece.Asterisk,
                    "%" => Piece.Percent,
                    _ => Piece.Other,
                };
                Emit(text, ref previous, piece, token.Text == "\\" ? "\\\n" : token.Text);
                break;
            case CssTokenKind.CloseParen:
                Emit(text, ref previous, Piece.Other, ")");
                break;
            case CssTokenKind.CloseSquare:
                Emit(text, ref previous, Piece.Other, "]");
                break;
            case CssTokenKind.CloseCurly:
                Emit(text, ref previous, Piece.Other, "}");
                break;
            default:
                break;
        }
    }

    private static void Emit(StringBuilder text, ref Piece previous, Piece next, string written)
    {
        if (NeedsComment(previous, next))
        {
            text.Append("/**/");
        }

        text.Append(written);
        previous = next;
    }

    /// <summary>The table of CSS Syntax 3 §10: the pairs that would tokenize differently written next to each other.</summary>
    private static bool NeedsComment(Piece first, Piece second)
    {
        var identLike = second is Piece.Ident or Piece.Function or Piece.Url or Piece.Hyphen or Piece.Number or Piece.Percentage or Piece.Dimension;
        return first switch
        {
            Piece.Ident => identLike || second is Piece.Cdc or Piece.OpenParen,
            Piece.AtKeyword or Piece.Hash or Piece.Dimension => identLike || second == Piece.Cdc,
            Piece.NumberSign or Piece.Hyphen => identLike,
            Piece.Number => second is Piece.Ident or Piece.Function or Piece.Url or Piece.Number or Piece.Percentage or Piece.Dimension or Piece.Percent,
            Piece.At => second is Piece.Ident or Piece.Function or Piece.Url or Piece.Hyphen,
            Piece.DotOrPlus => second is Piece.Number or Piece.Percentage or Piece.Dimension,
            Piece.Slash => second == Piece.Asterisk,
            _ => false,
        };
    }

    /// <summary>The number as written (its representation), so <c>1.0</c> stays <c>1.0</c>.</summary>
    private static string NumberText(CssToken token) =>
        token.Representation.Length > 0 ? token.Representation : token.Number.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>A unit: an identifier, with a leading e escaped when it would read as an exponent.</summary>
    private static string Unit(string unit)
    {
        var written = Identifier(unit);
        return written.Length > 1 && written[0] is 'e' or 'E' && (char.IsAsciiDigit(written[1]) || written[1] is '-' or '+')
            ? "\\65 " + written[1..]
            : written;
    }

    /// <summary>"Serialize an identifier" (CSSOM §2.1).</summary>
    public static string Identifier(string name)
    {
        var text = new StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (c == '\0')
            {
                text.Append('�');
            }
            else if (c is >= '\u0001' and <= '\u001F' or '\u007F' || (char.IsAsciiDigit(c) && (i == 0 || (i == 1 && name[0] == '-'))))
            {
                text.Append('\\').Append(((int)c).ToString("x", CultureInfo.InvariantCulture)).Append(' ');
            }
            else if (i == 0 && c == '-' && name.Length == 1)
            {
                text.Append("\\-");
            }
            else if (c >= 0x80 || c is '-' or '_' || char.IsAsciiLetterOrDigit(c))
            {
                text.Append(c);
            }
            else
            {
                text.Append('\\').Append(c);
            }
        }

        return text.ToString();
    }

    /// <summary>A hash's name: an identifier, except that it may start with a digit or a hyphen.</summary>
    private static string Name(string name)
    {
        var text = new StringBuilder();
        foreach (var c in name)
        {
            if (c >= 0x80 || c is '-' or '_' || char.IsAsciiLetterOrDigit(c))
            {
                text.Append(c);
            }
            else if (c is >= '\u0001' and <= '\u001F' or '\u007F')
            {
                text.Append('\\').Append(((int)c).ToString("x", CultureInfo.InvariantCulture)).Append(' ');
            }
            else
            {
                text.Append('\\').Append(c);
            }
        }

        return text.ToString();
    }

    /// <summary>"Serialize a string" (CSSOM §2.1).</summary>
    public static string String(string value)
    {
        var text = new StringBuilder("\"");
        foreach (var c in value)
        {
            if (c == '\0')
            {
                text.Append('�');
            }
            else if (c is >= '\u0001' and <= '\u001F' or '\u007F')
            {
                text.Append('\\').Append(((int)c).ToString("x", CultureInfo.InvariantCulture)).Append(' ');
            }
            else if (c is '"' or '\\')
            {
                text.Append('\\').Append(c);
            }
            else
            {
                text.Append(c);
            }
        }

        return text.Append('"').ToString();
    }

    private static string Url(string url)
    {
        var text = new StringBuilder();
        foreach (var c in url)
        {
            if (c is ' ' or '\t' or '\n' or '"' or '\'' or '(' or ')' or '\\' || c is >= '\u0000' and <= '\u001F' or '\u007F')
            {
                text.Append('\\').Append(((int)c).ToString("x", CultureInfo.InvariantCulture)).Append(' ');
            }
            else
            {
                text.Append(c);
            }
        }

        return text.ToString();
    }
}
