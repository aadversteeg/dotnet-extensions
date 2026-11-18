using Ave.Extensions.Documents.Core;
using System;
using System.Collections.Generic;
using System.Text;

namespace Ave.Extensions.Documents.Html.Parsing.Tokenizing;

/// <summary>
/// The tokenizer of the HTML Standard (§13.2.5): a state machine over the preprocessed input stream that emits tokens
/// one at a time. Tree construction pulls them with <see cref="Next"/> and changes <see cref="State"/> where the standard
/// says so (after <c>title</c>, <c>style</c>, <c>plaintext</c> …). Parse errors go to the error sink with the name the
/// standard gives them and the index in the source where they happened.
/// </summary>
/// <remarks>
/// <para>Where an error happens: at the current input character (the one just consumed; at the end of the input, the
/// end), which is what the html5lib tokenizer tests count; for <c>missing-semicolon-after-character-reference</c> after
/// a named reference, at the next input character, which is the one the standard looks at to decide (§13.2.5.78).</para>
/// <para>Input stream errors (surrogates, noncharacters, controls, §13.2.3.5) are reported the first time the tokenizer
/// consumes the character, so a character that is reconsumed is reported once.</para>
/// <para>Each state is one case of the switch in <c>Tokenizer.States.cs</c>; the work per input character is bounded
/// (the longest step is the named reference lookup, at most 32 table lookups).</para>
/// </remarks>
internal sealed partial class Tokenizer
{
    private const int EndOfFile = -1;

    private readonly string _text;
    private readonly Action<string, int> _error;
    private readonly Queue<Token> _output = new();

    private int _index;
    private int _current;
    private int _currentStart;
    private bool _reconsume;
    private int _highWater;

    private TokenizerState _returnState;
    private readonly StringBuilder _buffer = new();
    private long _characterReferenceCode;
    private int _referenceStart;

    private int _markupStart;
    private readonly StringBuilder _name = new();
    private Token? _tag;
    private bool _endTag;
    private bool _selfClosing;
    private readonly StringBuilder _attributeName = new();
    private readonly StringBuilder _attributeValue = new();
    private readonly List<(int Offset, int Source, bool Contiguous)> _valueAnchors = [];
    private int _valueNext = -1;
    private bool _attributeOpen;
    private bool _attributeDropped;
    private int _attributeStart;
    private string _finishedAttributeName = string.Empty;

    private readonly StringBuilder _data = new();
    private Token? _doctype;
    private readonly StringBuilder _publicId = new();
    private readonly StringBuilder _systemId = new();
    private bool _hasPublicId;
    private bool _hasSystemId;

    private readonly StringBuilder _characters = new();
    private int _charactersStart = -1;
    private int _charactersNext;

    /// <summary>Creates a tokenizer at the start of a text, in the data state.</summary>
    /// <param name="source">The preprocessed input.</param>
    /// <param name="error">Receives every parse error: its name and the source index where it happened.</param>
    public Tokenizer(SourceText source, Action<string, int> error)
    {
        _text = source.Text;
        _error = error;
    }

    /// <summary>The current state; tree construction switches it (§13.2.6.2, and the start tags that change it).</summary>
    public TokenizerState State { get; set; } = TokenizerState.Data;

    /// <summary>The tag name of the last start tag emitted, or null when none was (§13.2.5, "appropriate end tag
    /// token"). The fragment parsing algorithm and the tests may set it.</summary>
    public string? LastStartTagName { get; set; }

    /// <summary>Whether there is an adjusted current node that is not an element in the HTML namespace (§13.2.5.42,
    /// <c>[CDATA[</c>); asked by the tokenizer, answered by tree construction.</summary>
    public Func<bool> InForeignContent { get; set; } = static () => false;

    /// <summary>The index of the next character to consume: where the tokenizer is in the source.</summary>
    public int Position => _index;

    /// <summary>The next token. After the end-of-file token, every call returns another one.</summary>
    /// <returns>The token.</returns>
    public Token Next()
    {
        while (_output.Count == 0)
        {
            Step();
        }

        return _output.Dequeue();
    }

    /// <summary>"Consume the next input character": the next code point, or <see cref="EndOfFile"/>. A surrogate pair is
    /// one code point; a lone surrogate is its own value.</summary>
    private int Consume()
    {
        if (_reconsume)
        {
            _reconsume = false;
            return _current;
        }

        _currentStart = _index;
        if (_index >= _text.Length)
        {
            _current = EndOfFile;
            return EndOfFile;
        }

        int c = _text[_index];
        if (char.IsHighSurrogate((char)c) && _index + 1 < _text.Length && char.IsLowSurrogate(_text[_index + 1]))
        {
            c = char.ConvertToUtf32((char)c, _text[_index + 1]);
            _index += 2;
        }
        else
        {
            _index++;
        }

        if (_currentStart >= _highWater)
        {
            _highWater = _index;
            CheckInputCharacter(c, _currentStart);
        }

        _current = c;
        return c;
    }

    /// <summary>"Reconsume in the … state": the next <see cref="Consume"/> returns the current input character again.</summary>
    private void Reconsume(TokenizerState state)
    {
        _reconsume = true;
        State = state;
    }

    /// <summary>Consumes <paramref name="count"/> characters that a lookahead matched (all ASCII, so no input stream
    /// error is possible). Only called when nothing is to be reconsumed.</summary>
    private void Skip(int count)
    {
        _currentStart = _index + count - 1;
        _index += count;
        _current = _text[_index - 1];
        _highWater = Math.Max(_highWater, _index);
    }

    /// <summary>Whether the characters from <paramref name="start"/> are <paramref name="word"/>, optionally ASCII
    /// case-insensitively.</summary>
    private bool Matches(int start, string word, bool ignoreCase)
    {
        for (var i = 0; i < word.Length; i++)
        {
            if (start + i >= _text.Length)
            {
                return false;
            }

            var c = _text[start + i];
            if (ignoreCase ? InfraAscii.ToLower(c) != InfraAscii.ToLower(word[i]) : c != word[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Reports the input stream error of the next character, if it has one and was not reported yet: a
    /// lookahead reads that character (the one every alternative of the markup declaration open state starts with).</summary>
    private void CheckNextCharacter()
    {
        if (_index < _text.Length && _index >= _highWater)
        {
            int c = _text[_index];
            var length = 1;
            if (char.IsHighSurrogate(_text[_index]) && _index + 1 < _text.Length && char.IsLowSurrogate(_text[_index + 1]))
            {
                c = char.ConvertToUtf32(_text[_index], _text[_index + 1]);
                length = 2;
            }

            _highWater = _index + length;
            CheckInputCharacter(c, _index);
        }
    }

    /// <summary>The index of the next input character: the one that will be consumed next, which is the current one
    /// when it is to be reconsumed.</summary>
    private int NextIndex => _reconsume ? _currentStart : _index;

    /// <summary>The input stream errors of §13.2.3.5.</summary>
    private void CheckInputCharacter(int c, int index)
    {
        if (c is >= 0xD800 and <= 0xDFFF)
        {
            _error("surrogate-in-input-stream", index);
        }
        else if (IsNoncharacter(c))
        {
            _error("noncharacter-in-input-stream", index);
        }
        else if (IsControl(c) && !InfraAscii.IsWhitespace(c) && c != 0)
        {
            _error("control-character-in-input-stream", index);
        }
    }

    /// <summary>A noncharacter (Infra §4.5): U+FDD0–U+FDEF, and the last two code points of every plane.</summary>
    internal static bool IsNoncharacter(long c) => c is >= 0xFDD0 and <= 0xFDEF || (c <= 0x10FFFF && (c & 0xFFFE) == 0xFFFE);

    /// <summary>A control (Infra §4.5): a C0 control (U+0000–U+001F) or U+007F–U+009F.</summary>
    internal static bool IsControl(long c) => c is >= 0 and <= 0x1F or >= 0x7F and <= 0x9F;

    private void Error(string name) => _error(name, _currentStart);

    // ---- emitting ----

    /// <summary>Emits a character token for a code point written at <paramref name="sourceIndex"/>. Consecutive
    /// characters written one after another become one run.</summary>
    private void EmitCharacter(int codePoint, int sourceIndex)
    {
        if (_charactersStart >= 0 && sourceIndex != _charactersNext)
        {
            FlushCharacters();
        }

        if (_charactersStart < 0)
        {
            _charactersStart = sourceIndex;
            _charactersNext = sourceIndex;
        }

        AppendCodePoint(_characters, codePoint);
        _charactersNext += codePoint > 0xFFFF ? 2 : 1;
    }

    /// <summary>Emits the current input character as a character token.</summary>
    private void EmitCurrent() => EmitCharacter(_current, _currentStart);

    /// <summary>Emits the characters a character reference stands for, all written at its ampersand.</summary>
    private void EmitReference(string characters, int ampersand)
    {
        FlushCharacters();
        _output.Enqueue(new Token { Kind = TokenKind.Characters, Start = ampersand, Data = characters, Contiguous = false });
    }

    private void FlushCharacters()
    {
        if (_charactersStart < 0)
        {
            return;
        }

        _output.Enqueue(new Token { Kind = TokenKind.Characters, Start = _charactersStart, Data = _characters.ToString() });
        _characters.Clear();
        _charactersStart = -1;
    }

    private void Emit(Token token)
    {
        FlushCharacters();
        _output.Enqueue(token);
    }

    private void EmitEndOfFile() => Emit(new Token { Kind = TokenKind.EndOfFile, Start = _text.Length });

    /// <summary>Emits "&lt;/" and the temporary buffer as character tokens (the end tag name states' "anything else").</summary>
    private void EmitLessThanSolidusAndBuffer()
    {
        EmitCharacter('<', _markupStart);
        EmitCharacter('/', _markupStart + 1);
        for (var i = 0; i < _buffer.Length; i++)
        {
            EmitCharacter(_buffer[i], _markupStart + 2 + i);
        }
    }

    // ---- tags and attributes ----

    private void CreateTag(bool endTag)
    {
        _endTag = endTag;
        _selfClosing = false;
        _name.Clear();
        _tag = new Token { Kind = endTag ? TokenKind.EndTag : TokenKind.StartTag, Start = _markupStart };
        _attributeOpen = false;
    }

    private void StartAttribute(int start)
    {
        CommitAttribute();
        _attributeName.Clear();
        _attributeValue.Clear();
        _valueAnchors.Clear();
        _valueNext = -1;
        _attributeOpen = true;
        _attributeDropped = false;
        _attributeStart = start;
    }

    /// <summary>"When the user agent leaves the attribute name state" (§13.2.5.33): a name the token already has is a
    /// duplicate-attribute error, and the attribute is dropped (but stays the current attribute).</summary>
    private void LeaveAttributeName()
    {
        _finishedAttributeName = _attributeName.ToString();
        if (_tag!.HasAttribute(_finishedAttributeName))
        {
            Error("duplicate-attribute");
            _attributeDropped = true;
        }
    }

    private void CommitAttribute()
    {
        if (_attributeOpen && !_attributeDropped)
        {
            _tag!.AddAttribute(new TokenAttribute { Name = _finishedAttributeName, Value = _attributeValue.ToString(), Start = _attributeStart, Anchors = [.. _valueAnchors] });
        }

        _attributeOpen = false;
    }

    /// <summary>Whether the current end tag is an appropriate end tag token (§13.2.5).</summary>
    private bool IsAppropriateEndTag() => LastStartTagName is not null && _name.ToString() == LastStartTagName;

    private void EmitTag()
    {
        // Every way out of the attribute name state goes through LeaveAttributeName first, so the name is complete here.
        CommitAttribute();
        var tag = _tag!;
        tag.Name = _name.ToString();
        tag.SelfClosing = _selfClosing;
        if (_endTag)
        {
            if (tag.Attributes.Count > 0)
            {
                Error("end-tag-with-attributes");
            }

            if (_selfClosing)
            {
                Error("end-tag-with-trailing-solidus");
            }
        }
        else
        {
            LastStartTagName = tag.Name;
        }

        _tag = null;
        Emit(tag);
    }

    // ---- comments, processing instructions, doctypes ----

    private void CreateComment(string data)
    {
        _data.Clear();
        _data.Append(data);
    }

    private void EmitComment() => Emit(new Token { Kind = TokenKind.Comment, Start = _markupStart, Data = _data.ToString() });

    /// <summary>"Convert the temporary buffer to a comment" (§13.2.5): "?" and the buffer.</summary>
    private void BufferToComment() => CreateComment("?" + _buffer);

    private void EmitProcessingInstruction() =>
        Emit(new Token { Kind = TokenKind.ProcessingInstruction, Start = _markupStart, Name = _buffer.ToString(), Data = _data.ToString() });

    private void CreateDoctype()
    {
        _doctype = new Token { Kind = TokenKind.Doctype, Start = _markupStart };
        _name.Clear();
        _publicId.Clear();
        _systemId.Clear();
        _hasPublicId = false;
        _hasSystemId = false;
    }

    private void EmitDoctype(bool forceQuirks = false)
    {
        var doctype = _doctype!;
        doctype.ForceQuirks |= forceQuirks;
        doctype.PublicId = _hasPublicId ? _publicId.ToString() : null;
        doctype.SystemId = _hasSystemId ? _systemId.ToString() : null;
        _doctype = null;
        Emit(doctype);
    }

    // ---- character references ----

    private bool ReferenceInAttribute =>
        _returnState is TokenizerState.AttributeValueDoubleQuoted or TokenizerState.AttributeValueSingleQuoted or TokenizerState.AttributeValueUnquoted;

    /// <summary>"Flush code points consumed as a character reference" (§13.2.5) when the buffer holds what was written
    /// (no reference was resolved): to the attribute value, or as characters at their own places.</summary>
    private void FlushBufferAsWritten()
    {
        if (ReferenceInAttribute)
        {
            for (var i = 0; i < _buffer.Length; i++)
            {
                AppendValue(_buffer[i], _referenceStart + i);
            }
        }
        else
        {
            for (var i = 0; i < _buffer.Length; i++)
            {
                EmitCharacter(_buffer[i], _referenceStart + i);
            }
        }
    }

    /// <summary>"Flush code points consumed as a character reference" when the buffer holds what a reference stands
    /// for.</summary>
    private void FlushResolved(string characters)
    {
        if (ReferenceInAttribute)
        {
            _valueAnchors.Add((_attributeValue.Length, _referenceStart, false));
            _attributeValue.Append(characters);
            _valueNext = -1;
        }
        else
        {
            EmitReference(characters, _referenceStart);
        }
    }

    /// <summary>Appends a character of the source, written at <paramref name="source"/>, to the attribute value, with an
    /// anchor when it does not follow the last one (after a reference, after a line feed, at the start).</summary>
    private void AppendValue(int codePoint, int source)
    {
        if (source != _valueNext)
        {
            _valueAnchors.Add((_attributeValue.Length, source, true));
        }

        AppendCodePoint(_attributeValue, codePoint);
        _valueNext = codePoint == '\n' ? -1 : source + (codePoint > 0xFFFF ? 2 : 1);
    }

    private static void AppendCodePoint(StringBuilder builder, int codePoint)
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
