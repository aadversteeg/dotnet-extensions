using System;
using System.Globalization;
using System.Text;

namespace Ave.Extensions.Documents.Css.Syntax;

/// <summary>
/// The tokenizer of CSS Syntax 3 (§4.3): "consume a token", one at a time. It reads the text through the rest of
/// "filter code points" (§3.3): a form feed is a line feed, and NULL and surrogates are U+FFFD. Parse errors go to the
/// error sink with a name of this reader (the standard names none) and the index where they happened.
/// </summary>
internal sealed class CssTokenizer
{
    private const int EndOfFile = -1;

    private readonly CssSource _source;
    private readonly string _text;
    private readonly Action<string, int> _error;
    private readonly bool _unicodeRangesAllowed;
    private int _index;

    /// <summary>Creates a tokenizer at the start of a text.</summary>
    /// <param name="source">The style sheet.</param>
    /// <param name="error">Receives the parse errors: a name and a source index.</param>
    /// <param name="unicodeRangesAllowed">Whether <c>U+…</c> is a unicode-range token (only for the unicode-range
    /// descriptor, §5.5.11).</param>
    /// <param name="start">The index to start at.</param>
    /// <param name="end">The index to stop at (the end of the text by default).</param>
    public CssTokenizer(CssSource source, Action<string, int> error, bool unicodeRangesAllowed = false, int start = 0, int? end = null)
    {
        _source = source;
        _text = end is null ? source.Text : source.Text[..end.Value];
        _error = error;
        _unicodeRangesAllowed = unicodeRangesAllowed;
        _index = start;
    }

    /// <summary>The code point at an index after filtering, or <see cref="EndOfFile"/>; and how many UTF-16 units it took.</summary>
    private (int CodePoint, int Length) At(int index)
    {
        if (index >= _text.Length)
        {
            return (EndOfFile, 0);
        }

        var c = _text[index];
        if (char.IsHighSurrogate(c) && index + 1 < _text.Length && char.IsLowSurrogate(_text[index + 1]))
        {
            return (char.ConvertToUtf32(c, _text[index + 1]), 2);
        }

        return c switch
        {
            '\f' => ('\n', 1),
            '\0' => (0xFFFD, 1),
            _ when char.IsSurrogate(c) => (0xFFFD, 1),
            _ => (c, 1),
        };
    }

    private int Peek(int ahead = 0)
    {
        var index = _index;
        for (var i = 0; i < ahead; i++)
        {
            var (codePoint, length) = At(index);
            if (codePoint == EndOfFile)
            {
                return EndOfFile;
            }

            index += length;
        }

        return At(index).CodePoint;
    }

    private int Consume()
    {
        var (codePoint, length) = At(_index);
        _index += length;
        return codePoint;
    }

    /// <summary>"Consume a token" (§4.3.1). Returns an end-of-file token at the end, every time.</summary>
    public CssToken Next()
    {
        ConsumeComments();
        var start = _index;
        var c = Consume();
        switch (c)
        {
            case EndOfFile:
                return Token(CssTokenKind.EndOfFile, string.Empty, start);
            case '\n' or '\t' or ' ':
                while (IsWhitespace(Peek()))
                {
                    Consume();
                }

                return Token(CssTokenKind.Whitespace, " ", start);
            case '"' or '\'':
                return ConsumeString(c, start);
            case '#':
                if (IsIdentCodePoint(Peek()) || IsValidEscape(Peek(), Peek(1)))
                {
                    var isId = WouldStartIdent(Peek(), Peek(1), Peek(2));
                    return Token(CssTokenKind.Hash, ConsumeIdentSequence(), start) with { IsIdHash = isId };
                }

                return Delim(c, start);
            case '(':
                return Token(CssTokenKind.OpenParen, string.Empty, start);
            case ')':
                return Token(CssTokenKind.CloseParen, string.Empty, start);
            case '+' or '.':
                if (WouldStartNumber(c, Peek(), Peek(1)))
                {
                    _index = start;
                    return ConsumeNumeric(start);
                }

                return Delim(c, start);
            case ',':
                return Token(CssTokenKind.Comma, string.Empty, start);
            case '-':
                if (WouldStartNumber(c, Peek(), Peek(1)))
                {
                    _index = start;
                    return ConsumeNumeric(start);
                }

                if (Peek() == '-' && Peek(1) == '>')
                {
                    Consume();
                    Consume();
                    return Token(CssTokenKind.Cdc, string.Empty, start);
                }

                if (WouldStartIdent(c, Peek(), Peek(1)))
                {
                    _index = start;
                    return ConsumeIdentLike(start);
                }

                return Delim(c, start);
            case ':':
                return Token(CssTokenKind.Colon, string.Empty, start);
            case ';':
                return Token(CssTokenKind.Semicolon, string.Empty, start);
            case '<':
                if (Peek() == '!' && Peek(1) == '-' && Peek(2) == '-')
                {
                    Consume();
                    Consume();
                    Consume();
                    return Token(CssTokenKind.Cdo, string.Empty, start);
                }

                return Delim(c, start);
            case '@':
                if (WouldStartIdent(Peek(), Peek(1), Peek(2)))
                {
                    return Token(CssTokenKind.AtKeyword, ConsumeIdentSequence(), start);
                }

                return Delim(c, start);
            case '[':
                return Token(CssTokenKind.OpenSquare, string.Empty, start);
            case '\\':
                if (IsValidEscape(c, Peek()))
                {
                    _index = start;
                    return ConsumeIdentLike(start);
                }

                _error("invalid-escape", start);
                return Delim(c, start);
            case ']':
                return Token(CssTokenKind.CloseSquare, string.Empty, start);
            case '{':
                return Token(CssTokenKind.OpenCurly, string.Empty, start);
            case '}':
                return Token(CssTokenKind.CloseCurly, string.Empty, start);
            default:
                if (Ascii.IsDigit(c))
                {
                    _index = start;
                    return ConsumeNumeric(start);
                }

                if ((c is 'U' or 'u') && _unicodeRangesAllowed && Peek() == '+' && (Peek(1) == '?' || Ascii.IsHexDigit(Peek(1))))
                {
                    return ConsumeUnicodeRange(start);
                }

                if (IsIdentStart(c))
                {
                    _index = start;
                    return ConsumeIdentLike(start);
                }

                return Delim(c, start);
        }
    }

    private CssToken Token(CssTokenKind kind, string text, int start) =>
        new(kind, text, _source.PositionOf(start)) { SourceStart = start, SourceEnd = _index };

    private CssToken Delim(int c, int start) => Token(CssTokenKind.Delim, char.ConvertFromUtf32(c), start);

    // §4.3.2
    private void ConsumeComments()
    {
        while (Peek() == '/' && Peek(1) == '*')
        {
            var start = _index;
            Consume();
            Consume();
            while (true)
            {
                var c = Consume();
                if (c == EndOfFile)
                {
                    _error("eof-in-comment", start);
                    return;
                }

                if (c == '*' && Peek() == '/')
                {
                    Consume();
                    break;
                }
            }
        }
    }

    // §4.3.3
    private CssToken ConsumeNumeric(int start)
    {
        var (value, isInteger, sign, representation) = ConsumeNumber();
        if (WouldStartIdent(Peek(), Peek(1), Peek(2)))
        {
            var unit = ConsumeIdentSequence();
            return Token(CssTokenKind.Dimension, unit, start) with { Number = value, IsInteger = isInteger, Sign = sign, Representation = representation };
        }

        if (Peek() == '%')
        {
            Consume();
            return Token(CssTokenKind.Percentage, string.Empty, start) with { Number = value, IsInteger = isInteger, Sign = sign, Representation = representation };
        }

        return Token(CssTokenKind.Number, string.Empty, start) with { Number = value, IsInteger = isInteger, Sign = sign, Representation = representation };
    }

    // §4.3.4
    private CssToken ConsumeIdentLike(int start)
    {
        var name = ConsumeIdentSequence();
        if (Ascii.EqualsIgnoreCase(name, "url") && Peek() == '(')
        {
            Consume();
            while (IsWhitespace(Peek()) && IsWhitespace(Peek(1)))
            {
                Consume();
            }

            var next = Peek();
            if (next is '"' or '\'' || (IsWhitespace(next) && Peek(1) is '"' or '\''))
            {
                return Token(CssTokenKind.Function, name, start);
            }

            return ConsumeUrl(start);
        }

        if (Peek() == '(')
        {
            Consume();
            return Token(CssTokenKind.Function, name, start);
        }

        return Token(CssTokenKind.Ident, name, start);
    }

    // §4.3.5
    private CssToken ConsumeString(int ending, int start)
    {
        var value = new StringBuilder();
        while (true)
        {
            var c = Consume();
            if (c == ending)
            {
                return Token(CssTokenKind.String, value.ToString(), start);
            }

            switch (c)
            {
                case EndOfFile:
                    _error("eof-in-string", start);
                    return Token(CssTokenKind.String, value.ToString(), start);
                case '\n':
                    _error("newline-in-string", _index - 1);
                    _index--;
                    return Token(CssTokenKind.BadString, string.Empty, start);
                case '\\':
                    var next = Peek();
                    if (next == EndOfFile)
                    {
                        break;
                    }

                    if (next == '\n')
                    {
                        Consume();
                        break;
                    }

                    Append(value, ConsumeEscape());
                    break;
                default:
                    Append(value, c);
                    break;
            }
        }
    }

    // §4.3.6: "url(" and any white space after it are consumed.
    private CssToken ConsumeUrl(int start)
    {
        var value = new StringBuilder();
        while (IsWhitespace(Peek()))
        {
            Consume();
        }

        while (true)
        {
            var c = Consume();
            switch (c)
            {
                case ')':
                    return Token(CssTokenKind.Url, value.ToString(), start);
                case EndOfFile:
                    _error("eof-in-url", start);
                    return Token(CssTokenKind.Url, value.ToString(), start);
                case '\n' or '\t' or ' ':
                    while (IsWhitespace(Peek()))
                    {
                        Consume();
                    }

                    if (Peek() is ')' or EndOfFile)
                    {
                        if (Consume() == EndOfFile)
                        {
                            _error("eof-in-url", start);
                        }

                        return Token(CssTokenKind.Url, value.ToString(), start);
                    }

                    _error("bad-url", start);
                    ConsumeBadUrlRemnants();
                    return Token(CssTokenKind.BadUrl, string.Empty, start);
                case '"' or '\'' or '(':
                    _error("bad-url", start);
                    ConsumeBadUrlRemnants();
                    return Token(CssTokenKind.BadUrl, string.Empty, start);
                case '\\':
                    if (IsValidEscape(c, Peek()))
                    {
                        Append(value, ConsumeEscape());
                        break;
                    }

                    _error("bad-url", start);
                    ConsumeBadUrlRemnants();
                    return Token(CssTokenKind.BadUrl, string.Empty, start);
                default:
                    if (IsNonPrintable(c))
                    {
                        _error("bad-url", start);
                        ConsumeBadUrlRemnants();
                        return Token(CssTokenKind.BadUrl, string.Empty, start);
                    }

                    Append(value, c);
                    break;
            }
        }
    }

    // §4.3.7: the backslash is consumed and the escape is valid.
    private int ConsumeEscape()
    {
        var start = _index;
        var c = Consume();
        if (c == EndOfFile)
        {
            _error("eof-in-escape", start);
            return 0xFFFD;
        }

        if (!Ascii.IsHexDigit(c))
        {
            return c;
        }

        var value = HexValue(c);
        for (var digits = 1; digits < 6 && Ascii.IsHexDigit(Peek()); digits++)
        {
            value = (value * 16) + HexValue(Consume());
        }

        if (IsWhitespace(Peek()))
        {
            Consume();
        }

        return value == 0 || value is >= 0xD800 and <= 0xDFFF || value > 0x10FFFF ? 0xFFFD : value;
    }

    private static int HexValue(int c) => Ascii.IsDigit(c) ? c - '0' : Ascii.ToLower(c) - 'a' + 10;

    // §4.3.12
    private string ConsumeIdentSequence()
    {
        var result = new StringBuilder();
        while (true)
        {
            var c = Peek();
            if (IsIdentCodePoint(c))
            {
                Append(result, Consume());
            }
            else if (IsValidEscape(c, Peek(1)))
            {
                Consume();
                Append(result, ConsumeEscape());
            }
            else
            {
                return result.ToString();
            }
        }
    }

    // §4.3.13
    private (double Value, bool IsInteger, char? Sign, string Representation) ConsumeNumber()
    {
        var start = _index;
        var isInteger = true;
        char? sign = null;
        var number = new StringBuilder();
        if (Peek() is '+' or '-')
        {
            sign = (char)Consume();
            number.Append(sign.Value);
        }

        while (Ascii.IsDigit(Peek()))
        {
            number.Append((char)Consume());
        }

        if (Peek() == '.' && Ascii.IsDigit(Peek(1)))
        {
            number.Append((char)Consume());
            while (Ascii.IsDigit(Peek()))
            {
                number.Append((char)Consume());
            }

            isInteger = false;
        }

        var exponent = new StringBuilder();
        if (Peek() is 'e' or 'E' && (Ascii.IsDigit(Peek(1)) || (Peek(1) is '+' or '-' && Ascii.IsDigit(Peek(2)))))
        {
            Consume();
            if (Peek() is '+' or '-')
            {
                exponent.Append((char)Consume());
            }

            while (Ascii.IsDigit(Peek()))
            {
                exponent.Append((char)Consume());
            }

            isInteger = false;
        }

        // The numeric value: the number part times 10 to the exponent, computed by the runtime's parser, which rounds
        // correctly. A number too large for a double is the largest one, with its sign (CSS Values 4 §5: a value past
        // the supported range becomes the closest supported value), so no token is infinite or NaN; the value
        // computation refuses what is past its own range (CssInputLimits.MaxLengthPixels).
        var text = exponent.Length == 0 ? number.ToString() : $"{number}e{exponent}";
        var value = Math.Clamp(double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture), -double.MaxValue, double.MaxValue);
        return (value, isInteger, sign, _text[start.._index]);
    }

    // §4.3.14: the U and + are next.
    private CssToken ConsumeUnicodeRange(int start)
    {
        Consume();
        var first = new StringBuilder();
        while (first.Length < 6 && Ascii.IsHexDigit(Peek()))
        {
            first.Append((char)Consume());
        }

        while (first.Length < 6 && Peek() == '?')
        {
            first.Append((char)Consume());
        }

        var segment = first.ToString();
        if (segment.Contains('?', StringComparison.Ordinal))
        {
            var low = int.Parse(segment.Replace('?', '0'), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var high = int.Parse(segment.Replace('?', 'F'), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return Token(CssTokenKind.UnicodeRange, string.Empty, start) with { RangeStart = low, RangeEnd = high };
        }

        var rangeStart = int.Parse(segment, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        if (Peek() == '-' && Ascii.IsHexDigit(Peek(1)))
        {
            Consume();
            var last = new StringBuilder();
            while (last.Length < 6 && Ascii.IsHexDigit(Peek()))
            {
                last.Append((char)Consume());
            }

            var rangeEnd = int.Parse(last.ToString(), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return Token(CssTokenKind.UnicodeRange, string.Empty, start) with { RangeStart = rangeStart, RangeEnd = rangeEnd };
        }

        return Token(CssTokenKind.UnicodeRange, string.Empty, start) with { RangeStart = rangeStart, RangeEnd = rangeStart };
    }

    // §4.3.15
    private void ConsumeBadUrlRemnants()
    {
        while (true)
        {
            var c = Consume();
            if (c is ')' or EndOfFile)
            {
                return;
            }

            if (IsValidEscape(c, Peek()))
            {
                ConsumeEscape();
            }
        }
    }

    // ---- definitions (§4.2) and checks (§4.3.8–§4.3.10) ----

    private static bool IsWhitespace(int c) => c is '\n' or '\t' or ' ';

    /// <summary>
    /// A non-ASCII ident code point: every code point from U+0080, as in the 2021 Candidate Recommendation and in
    /// Chromium 159. The current draft (§4.2) narrows the set to the code points of HTML custom element names, but
    /// Chromium does not: in the pinned container <c>document.querySelector('.a\u0080')</c> is a valid class selector.
    /// The engine follows Chromium, so a selector that matches there matches here.
    /// </summary>
    private static bool IsNonAsciiIdent(int c) => c >= 0x80;

    private static bool IsIdentStart(int c) => Ascii.IsAlpha(c) || c == '_' || IsNonAsciiIdent(c);

    private static bool IsIdentCodePoint(int c) => IsIdentStart(c) || Ascii.IsDigit(c) || c == '-';

    private static bool IsNonPrintable(int c) => c is >= 0 and <= 8 or 0x0B or >= 0x0E and <= 0x1F or 0x7F;

    private static bool IsValidEscape(int first, int second) => first == '\\' && second != '\n';

    private static bool WouldStartIdent(int first, int second, int third) => first switch
    {
        '-' => IsIdentStart(second) || second == '-' || IsValidEscape(second, third),
        '\\' => IsValidEscape(first, second),
        _ => IsIdentStart(first),
    };

    private static bool WouldStartNumber(int first, int second, int third) => first switch
    {
        '+' or '-' => Ascii.IsDigit(second) || (second == '.' && Ascii.IsDigit(third)),
        '.' => Ascii.IsDigit(second),
        _ => Ascii.IsDigit(first),
    };

    private static void Append(StringBuilder builder, int codePoint)
    {
        if (codePoint > 0xFFFF)
        {
            builder.Append(char.ConvertFromUtf32(codePoint));
        }
        else
        {
            builder.Append((char)codePoint);
        }
    }
}
