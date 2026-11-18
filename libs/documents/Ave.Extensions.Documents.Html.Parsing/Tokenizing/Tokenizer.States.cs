using Ave.Extensions.Documents.Core;

namespace Ave.Extensions.Documents.Html.Parsing.Tokenizing;

/// <summary>The states of the tokenizer, one case each, in the order of the HTML Standard §13.2.5.1–§13.2.5.84.</summary>
internal sealed partial class Tokenizer
{
    /// <summary>Runs one step of the state machine.</summary>
    private void Step()
    {
        switch (State)
        {
            case TokenizerState.Data: DataState(); break;
            case TokenizerState.Rcdata: TextState(TokenizerState.RcdataLessThanSign, references: true); break;
            case TokenizerState.Rawtext: TextState(TokenizerState.RawtextLessThanSign, references: false); break;
            case TokenizerState.ScriptData: TextState(TokenizerState.ScriptDataLessThanSign, references: false); break;
            case TokenizerState.Plaintext: PlaintextState(); break;
            case TokenizerState.TagOpen: TagOpenState(); break;
            case TokenizerState.EndTagOpen: EndTagOpenState(); break;
            case TokenizerState.TagName: TagNameState(); break;
            case TokenizerState.RcdataLessThanSign: TextLessThanSignState(TokenizerState.RcdataEndTagOpen, TokenizerState.Rcdata); break;
            case TokenizerState.RcdataEndTagOpen: TextEndTagOpenState(TokenizerState.RcdataEndTagName, TokenizerState.Rcdata); break;
            case TokenizerState.RcdataEndTagName: TextEndTagNameState(TokenizerState.Rcdata); break;
            case TokenizerState.RawtextLessThanSign: TextLessThanSignState(TokenizerState.RawtextEndTagOpen, TokenizerState.Rawtext); break;
            case TokenizerState.RawtextEndTagOpen: TextEndTagOpenState(TokenizerState.RawtextEndTagName, TokenizerState.Rawtext); break;
            case TokenizerState.RawtextEndTagName: TextEndTagNameState(TokenizerState.Rawtext); break;
            case TokenizerState.ScriptDataLessThanSign: ScriptDataLessThanSignState(); break;
            case TokenizerState.ScriptDataEndTagOpen: TextEndTagOpenState(TokenizerState.ScriptDataEndTagName, TokenizerState.ScriptData); break;
            case TokenizerState.ScriptDataEndTagName: TextEndTagNameState(TokenizerState.ScriptData); break;
            case TokenizerState.ScriptDataEscapeStart: ScriptDataEscapeStartState(TokenizerState.ScriptDataEscapeStartDash); break;
            case TokenizerState.ScriptDataEscapeStartDash: ScriptDataEscapeStartState(TokenizerState.ScriptDataEscapedDashDash); break;
            case TokenizerState.ScriptDataEscaped: ScriptDataEscapedState(); break;
            case TokenizerState.ScriptDataEscapedDash: ScriptDataEscapedDashState(); break;
            case TokenizerState.ScriptDataEscapedDashDash: ScriptDataEscapedDashDashState(); break;
            case TokenizerState.ScriptDataEscapedLessThanSign: ScriptDataEscapedLessThanSignState(); break;
            case TokenizerState.ScriptDataEscapedEndTagOpen: TextEndTagOpenState(TokenizerState.ScriptDataEscapedEndTagName, TokenizerState.ScriptDataEscaped); break;
            case TokenizerState.ScriptDataEscapedEndTagName: TextEndTagNameState(TokenizerState.ScriptDataEscaped); break;
            case TokenizerState.ScriptDataDoubleEscapeStart: ScriptDataDoubleEscapeBoundaryState(TokenizerState.ScriptDataDoubleEscaped, TokenizerState.ScriptDataEscaped, TokenizerState.ScriptDataEscaped); break;
            case TokenizerState.ScriptDataDoubleEscaped: ScriptDataDoubleEscapedState(); break;
            case TokenizerState.ScriptDataDoubleEscapedDash: ScriptDataDoubleEscapedDashState(); break;
            case TokenizerState.ScriptDataDoubleEscapedDashDash: ScriptDataDoubleEscapedDashDashState(); break;
            case TokenizerState.ScriptDataDoubleEscapedLessThanSign: ScriptDataDoubleEscapedLessThanSignState(); break;
            case TokenizerState.ScriptDataDoubleEscapeEnd: ScriptDataDoubleEscapeBoundaryState(TokenizerState.ScriptDataEscaped, TokenizerState.ScriptDataDoubleEscaped, TokenizerState.ScriptDataDoubleEscaped); break;
            case TokenizerState.BeforeAttributeName: BeforeAttributeNameState(); break;
            case TokenizerState.AttributeName: AttributeNameState(); break;
            case TokenizerState.AfterAttributeName: AfterAttributeNameState(); break;
            case TokenizerState.BeforeAttributeValue: BeforeAttributeValueState(); break;
            case TokenizerState.AttributeValueDoubleQuoted: QuotedAttributeValueState('"', TokenizerState.AttributeValueDoubleQuoted); break;
            case TokenizerState.AttributeValueSingleQuoted: QuotedAttributeValueState('\'', TokenizerState.AttributeValueSingleQuoted); break;
            case TokenizerState.AttributeValueUnquoted: UnquotedAttributeValueState(); break;
            case TokenizerState.AfterAttributeValueQuoted: AfterAttributeValueQuotedState(); break;
            case TokenizerState.SelfClosingStartTag: SelfClosingStartTagState(); break;
            case TokenizerState.BogusComment: BogusCommentState(); break;
            case TokenizerState.MarkupDeclarationOpen: MarkupDeclarationOpenState(); break;
            case TokenizerState.CommentStart: CommentStartState(); break;
            case TokenizerState.CommentStartDash: CommentStartDashState(); break;
            case TokenizerState.Comment: CommentState(); break;
            case TokenizerState.CommentLessThanSign: CommentLessThanSignState(); break;
            case TokenizerState.CommentLessThanSignBang: CommentLessThanSignBangState(); break;
            case TokenizerState.CommentLessThanSignBangDash: CommentLessThanSignBangDashState(); break;
            case TokenizerState.CommentLessThanSignBangDashDash: CommentLessThanSignBangDashDashState(); break;
            case TokenizerState.CommentEndDash: CommentEndDashState(); break;
            case TokenizerState.CommentEnd: CommentEndState(); break;
            case TokenizerState.CommentEndBang: CommentEndBangState(); break;
            case TokenizerState.Doctype: DoctypeState(); break;
            case TokenizerState.BeforeDoctypeName: BeforeDoctypeNameState(); break;
            case TokenizerState.DoctypeName: DoctypeNameState(); break;
            case TokenizerState.AfterDoctypeName: AfterDoctypeNameState(); break;
            case TokenizerState.AfterDoctypePublicKeyword: AfterDoctypeKeywordState(isPublic: true); break;
            case TokenizerState.BeforeDoctypePublicIdentifier: BeforeDoctypeIdentifierState(isPublic: true); break;
            case TokenizerState.DoctypePublicIdentifierDoubleQuoted: DoctypeIdentifierState('"', isPublic: true); break;
            case TokenizerState.DoctypePublicIdentifierSingleQuoted: DoctypeIdentifierState('\'', isPublic: true); break;
            case TokenizerState.AfterDoctypePublicIdentifier: AfterDoctypePublicIdentifierState(); break;
            case TokenizerState.BetweenDoctypePublicAndSystemIdentifiers: BetweenDoctypePublicAndSystemIdentifiersState(); break;
            case TokenizerState.AfterDoctypeSystemKeyword: AfterDoctypeKeywordState(isPublic: false); break;
            case TokenizerState.BeforeDoctypeSystemIdentifier: BeforeDoctypeIdentifierState(isPublic: false); break;
            case TokenizerState.DoctypeSystemIdentifierDoubleQuoted: DoctypeIdentifierState('"', isPublic: false); break;
            case TokenizerState.DoctypeSystemIdentifierSingleQuoted: DoctypeIdentifierState('\'', isPublic: false); break;
            case TokenizerState.AfterDoctypeSystemIdentifier: AfterDoctypeSystemIdentifierState(); break;
            case TokenizerState.BogusDoctype: BogusDoctypeState(); break;
            case TokenizerState.CdataSection: CdataSectionState(); break;
            case TokenizerState.CdataSectionBracket: CdataSectionBracketState(); break;
            case TokenizerState.CdataSectionEnd: CdataSectionEndState(); break;
            case TokenizerState.ProcessingInstructionOpen: ProcessingInstructionOpenState(); break;
            case TokenizerState.ProcessingInstructionTarget: ProcessingInstructionTargetState(); break;
            case TokenizerState.AfterProcessingInstructionTarget: AfterProcessingInstructionTargetState(); break;
            case TokenizerState.ProcessingInstructionData: ProcessingInstructionDataState(); break;
            case TokenizerState.ProcessingInstructionQuestionable: ProcessingInstructionQuestionableState(); break;
            case TokenizerState.CharacterReference: CharacterReferenceState(); break;
            case TokenizerState.NamedCharacterReference: NamedCharacterReferenceState(); break;
            case TokenizerState.AmbiguousAmpersand: AmbiguousAmpersandState(); break;
            case TokenizerState.NumericCharacterReference: NumericCharacterReferenceState(); break;
            case TokenizerState.HexadecimalCharacterReferenceStart: HexadecimalCharacterReferenceStartState(); break;
            case TokenizerState.HexadecimalCharacterReference: DigitsState(hexadecimal: true); break;
            case TokenizerState.DecimalCharacterReference: DigitsState(hexadecimal: false); break;
            case TokenizerState.NumericCharacterReferenceEnd: NumericCharacterReferenceEndState(); break;
        }
    }

    // §13.2.5.1
    private void DataState()
    {
        var c = Consume();
        switch (c)
        {
            case '&':
                StartReference(TokenizerState.Data);
                break;
            case '<':
                _markupStart = _currentStart;
                State = TokenizerState.TagOpen;
                break;
            case 0:
                Error("unexpected-null-character");
                EmitCurrent();
                break;
            case EndOfFile:
                EmitEndOfFile();
                break;
            default:
                EmitCurrent();
                break;
        }
    }

    // §13.2.5.2 RCDATA, §13.2.5.3 RAWTEXT, §13.2.5.4 script data: the same but for references.
    private void TextState(TokenizerState lessThanSign, bool references)
    {
        var c = Consume();
        switch (c)
        {
            case '&' when references:
                StartReference(State);
                break;
            case '<':
                _markupStart = _currentStart;
                State = lessThanSign;
                break;
            case 0:
                Error("unexpected-null-character");
                EmitCharacter(0xFFFD, _currentStart);
                break;
            case EndOfFile:
                EmitEndOfFile();
                break;
            default:
                EmitCurrent();
                break;
        }
    }

    // §13.2.5.5
    private void PlaintextState()
    {
        var c = Consume();
        switch (c)
        {
            case 0:
                Error("unexpected-null-character");
                EmitCharacter(0xFFFD, _currentStart);
                break;
            case EndOfFile:
                EmitEndOfFile();
                break;
            default:
                EmitCurrent();
                break;
        }
    }

    // §13.2.5.6
    private void TagOpenState()
    {
        var c = Consume();
        switch (c)
        {
            case '!':
                State = TokenizerState.MarkupDeclarationOpen;
                break;
            case '/':
                State = TokenizerState.EndTagOpen;
                break;
            case '?':
                _buffer.Clear();
                State = TokenizerState.ProcessingInstructionOpen;
                break;
            case EndOfFile:
                Error("eof-before-tag-name");
                EmitCharacter('<', _markupStart);
                EmitEndOfFile();
                break;
            default:
                if (InfraAscii.IsAlpha(c))
                {
                    CreateTag(endTag: false);
                    Reconsume(TokenizerState.TagName);
                }
                else if (c == 0)
                {
                    // Chromium 159 keeps a U+0000 right after '<' as U+FFFD text (HD-2, measured), where the
                    // standard reconsumes it in the data state, which drops it.
                    Error("invalid-first-character-of-tag-name");
                    EmitCharacter('<', _markupStart);
                    Error("unexpected-null-character");
                    EmitCharacter(0xFFFD, _currentStart);
                    State = TokenizerState.Data;
                }
                else
                {
                    Error("invalid-first-character-of-tag-name");
                    EmitCharacter('<', _markupStart);
                    Reconsume(TokenizerState.Data);
                }

                break;
        }
    }

    // §13.2.5.7
    private void EndTagOpenState()
    {
        var c = Consume();
        if (InfraAscii.IsAlpha(c))
        {
            CreateTag(endTag: true);
            Reconsume(TokenizerState.TagName);
        }
        else if (c == '>')
        {
            Error("missing-end-tag-name");
            State = TokenizerState.Data;
        }
        else if (c == EndOfFile)
        {
            Error("eof-before-tag-name");
            EmitCharacter('<', _markupStart);
            EmitCharacter('/', _markupStart + 1);
            EmitEndOfFile();
        }
        else
        {
            Error("invalid-first-character-of-tag-name");
            CreateComment(string.Empty);
            Reconsume(TokenizerState.BogusComment);
        }
    }

    // §13.2.5.8
    private void TagNameState()
    {
        var c = Consume();
        switch (c)
        {
            case '\t' or '\n' or '\f' or ' ':
                State = TokenizerState.BeforeAttributeName;
                break;
            case '/':
                State = TokenizerState.SelfClosingStartTag;
                break;
            case '>':
                State = TokenizerState.Data;
                EmitTag();
                break;
            case 0:
                Error("unexpected-null-character");
                _name.Append('�');
                break;
            case EndOfFile:
                Error("eof-in-tag");
                EmitEndOfFile();
                break;
            default:
                AppendCodePoint(_name, InfraAscii.ToLower(c));
                break;
        }
    }

    // §13.2.5.9, §13.2.5.12
    private void TextLessThanSignState(TokenizerState endTagOpen, TokenizerState text)
    {
        var c = Consume();
        if (c == '/')
        {
            _buffer.Clear();
            State = endTagOpen;
        }
        else
        {
            EmitCharacter('<', _markupStart);
            Reconsume(text);
        }
    }

    // §13.2.5.10, §13.2.5.13, §13.2.5.16, §13.2.5.24
    private void TextEndTagOpenState(TokenizerState endTagName, TokenizerState text)
    {
        var c = Consume();
        if (InfraAscii.IsAlpha(c))
        {
            CreateTag(endTag: true);
            Reconsume(endTagName);
        }
        else
        {
            EmitCharacter('<', _markupStart);
            EmitCharacter('/', _markupStart + 1);
            Reconsume(text);
        }
    }

    // §13.2.5.11, §13.2.5.14, §13.2.5.17, §13.2.5.25
    private void TextEndTagNameState(TokenizerState text)
    {
        var c = Consume();
        switch (c)
        {
            case '\t' or '\n' or '\f' or ' ' when IsAppropriateEndTag():
                State = TokenizerState.BeforeAttributeName;
                return;
            case '/' when IsAppropriateEndTag():
                State = TokenizerState.SelfClosingStartTag;
                return;
            case '>' when IsAppropriateEndTag():
                State = TokenizerState.Data;
                EmitTag();
                return;
        }

        if (InfraAscii.IsAlpha(c))
        {
            _name.Append((char)InfraAscii.ToLower(c));
            _buffer.Append((char)c);
            return;
        }

        _tag = null;
        EmitLessThanSolidusAndBuffer();
        Reconsume(text);
    }

    // §13.2.5.15
    private void ScriptDataLessThanSignState()
    {
        var c = Consume();
        if (c == '/')
        {
            _buffer.Clear();
            State = TokenizerState.ScriptDataEndTagOpen;
        }
        else if (c == '!')
        {
            State = TokenizerState.ScriptDataEscapeStart;
            EmitCharacter('<', _markupStart);
            EmitCurrent();
        }
        else
        {
            EmitCharacter('<', _markupStart);
            Reconsume(TokenizerState.ScriptData);
        }
    }

    // §13.2.5.18, §13.2.5.19
    private void ScriptDataEscapeStartState(TokenizerState next)
    {
        var c = Consume();
        if (c == '-')
        {
            State = next;
            EmitCurrent();
        }
        else
        {
            Reconsume(TokenizerState.ScriptData);
        }
    }

    // §13.2.5.20
    private void ScriptDataEscapedState()
    {
        var c = Consume();
        switch (c)
        {
            case '-':
                State = TokenizerState.ScriptDataEscapedDash;
                EmitCurrent();
                break;
            case '<':
                _markupStart = _currentStart;
                State = TokenizerState.ScriptDataEscapedLessThanSign;
                break;
            case 0:
                Error("unexpected-null-character");
                EmitCharacter(0xFFFD, _currentStart);
                break;
            case EndOfFile:
                Error("eof-in-script-html-comment-like-text");
                EmitEndOfFile();
                break;
            default:
                EmitCurrent();
                break;
        }
    }

    // §13.2.5.21
    private void ScriptDataEscapedDashState()
    {
        var c = Consume();
        switch (c)
        {
            case '-':
                State = TokenizerState.ScriptDataEscapedDashDash;
                EmitCurrent();
                break;
            case '<':
                _markupStart = _currentStart;
                State = TokenizerState.ScriptDataEscapedLessThanSign;
                break;
            case 0:
                Error("unexpected-null-character");
                State = TokenizerState.ScriptDataEscaped;
                EmitCharacter(0xFFFD, _currentStart);
                break;
            case EndOfFile:
                Error("eof-in-script-html-comment-like-text");
                EmitEndOfFile();
                break;
            default:
                State = TokenizerState.ScriptDataEscaped;
                EmitCurrent();
                break;
        }
    }

    // §13.2.5.22
    private void ScriptDataEscapedDashDashState()
    {
        var c = Consume();
        switch (c)
        {
            case '-':
                EmitCurrent();
                break;
            case '<':
                _markupStart = _currentStart;
                State = TokenizerState.ScriptDataEscapedLessThanSign;
                break;
            case '>':
                State = TokenizerState.ScriptData;
                EmitCurrent();
                break;
            case 0:
                Error("unexpected-null-character");
                State = TokenizerState.ScriptDataEscaped;
                EmitCharacter(0xFFFD, _currentStart);
                break;
            case EndOfFile:
                Error("eof-in-script-html-comment-like-text");
                EmitEndOfFile();
                break;
            default:
                State = TokenizerState.ScriptDataEscaped;
                EmitCurrent();
                break;
        }
    }

    // §13.2.5.23
    private void ScriptDataEscapedLessThanSignState()
    {
        var c = Consume();
        if (c == '/')
        {
            _buffer.Clear();
            State = TokenizerState.ScriptDataEscapedEndTagOpen;
        }
        else if (InfraAscii.IsAlpha(c))
        {
            _buffer.Clear();
            EmitCharacter('<', _markupStart);
            Reconsume(TokenizerState.ScriptDataDoubleEscapeStart);
        }
        else
        {
            EmitCharacter('<', _markupStart);
            Reconsume(TokenizerState.ScriptDataEscaped);
        }
    }

    // §13.2.5.26 (double escape start) and §13.2.5.31 (double escape end): the same shape, mirrored.
    private void ScriptDataDoubleEscapeBoundaryState(TokenizerState whenScript, TokenizerState otherwise, TokenizerState reconsumeIn)
    {
        var c = Consume();
        if (c is '\t' or '\n' or '\f' or ' ' or '/' or '>')
        {
            State = _buffer.ToString() == "script" ? whenScript : otherwise;
            EmitCurrent();
        }
        else if (InfraAscii.IsAlpha(c))
        {
            _buffer.Append((char)InfraAscii.ToLower(c));
            EmitCurrent();
        }
        else
        {
            Reconsume(reconsumeIn);
        }
    }

    // §13.2.5.27
    private void ScriptDataDoubleEscapedState()
    {
        var c = Consume();
        switch (c)
        {
            case '-':
                State = TokenizerState.ScriptDataDoubleEscapedDash;
                EmitCurrent();
                break;
            case '<':
                State = TokenizerState.ScriptDataDoubleEscapedLessThanSign;
                EmitCurrent();
                break;
            case 0:
                Error("unexpected-null-character");
                EmitCharacter(0xFFFD, _currentStart);
                break;
            case EndOfFile:
                Error("eof-in-script-html-comment-like-text");
                EmitEndOfFile();
                break;
            default:
                EmitCurrent();
                break;
        }
    }

    // §13.2.5.28
    private void ScriptDataDoubleEscapedDashState()
    {
        var c = Consume();
        switch (c)
        {
            case '-':
                State = TokenizerState.ScriptDataDoubleEscapedDashDash;
                EmitCurrent();
                break;
            case '<':
                State = TokenizerState.ScriptDataDoubleEscapedLessThanSign;
                EmitCurrent();
                break;
            case 0:
                Error("unexpected-null-character");
                State = TokenizerState.ScriptDataDoubleEscaped;
                EmitCharacter(0xFFFD, _currentStart);
                break;
            case EndOfFile:
                Error("eof-in-script-html-comment-like-text");
                EmitEndOfFile();
                break;
            default:
                State = TokenizerState.ScriptDataDoubleEscaped;
                EmitCurrent();
                break;
        }
    }

    // §13.2.5.29
    private void ScriptDataDoubleEscapedDashDashState()
    {
        var c = Consume();
        switch (c)
        {
            case '-':
                EmitCurrent();
                break;
            case '<':
                State = TokenizerState.ScriptDataDoubleEscapedLessThanSign;
                EmitCurrent();
                break;
            case '>':
                State = TokenizerState.ScriptData;
                EmitCurrent();
                break;
            case 0:
                Error("unexpected-null-character");
                State = TokenizerState.ScriptDataDoubleEscaped;
                EmitCharacter(0xFFFD, _currentStart);
                break;
            case EndOfFile:
                Error("eof-in-script-html-comment-like-text");
                EmitEndOfFile();
                break;
            default:
                State = TokenizerState.ScriptDataDoubleEscaped;
                EmitCurrent();
                break;
        }
    }

    // §13.2.5.30
    private void ScriptDataDoubleEscapedLessThanSignState()
    {
        var c = Consume();
        if (c == '/')
        {
            _buffer.Clear();
            State = TokenizerState.ScriptDataDoubleEscapeEnd;
            EmitCurrent();
        }
        else
        {
            Reconsume(TokenizerState.ScriptDataDoubleEscaped);
        }
    }

    // §13.2.5.32
    private void BeforeAttributeNameState()
    {
        var c = Consume();
        switch (c)
        {
            case '\t' or '\n' or '\f' or ' ':
                break;
            case '/' or '>' or EndOfFile:
                Reconsume(TokenizerState.AfterAttributeName);
                break;
            case '=':
                Error("unexpected-equals-sign-before-attribute-name");
                StartAttribute(_currentStart);
                _attributeName.Append('=');
                State = TokenizerState.AttributeName;
                break;
            default:
                StartAttribute(_currentStart);
                Reconsume(TokenizerState.AttributeName);
                break;
        }
    }

    // §13.2.5.33
    private void AttributeNameState()
    {
        var c = Consume();
        switch (c)
        {
            case '\t' or '\n' or '\f' or ' ' or '/' or '>' or EndOfFile:
                LeaveAttributeName();
                Reconsume(TokenizerState.AfterAttributeName);
                break;
            case '=':
                LeaveAttributeName();
                State = TokenizerState.BeforeAttributeValue;
                break;
            case 0:
                Error("unexpected-null-character");
                _attributeName.Append('�');
                break;
            case '"' or '\'' or '<':
                Error("unexpected-character-in-attribute-name");
                _attributeName.Append((char)c);
                break;
            default:
                AppendCodePoint(_attributeName, InfraAscii.ToLower(c));
                break;
        }
    }

    // §13.2.5.34
    private void AfterAttributeNameState()
    {
        var c = Consume();
        switch (c)
        {
            case '\t' or '\n' or '\f' or ' ':
                break;
            case '/':
                State = TokenizerState.SelfClosingStartTag;
                break;
            case '=':
                State = TokenizerState.BeforeAttributeValue;
                break;
            case '>':
                State = TokenizerState.Data;
                EmitTag();
                break;
            case EndOfFile:
                Error("eof-in-tag");
                EmitEndOfFile();
                break;
            default:
                StartAttribute(_currentStart);
                Reconsume(TokenizerState.AttributeName);
                break;
        }
    }

    // §13.2.5.35
    private void BeforeAttributeValueState()
    {
        var c = Consume();
        switch (c)
        {
            case '\t' or '\n' or '\f' or ' ':
                break;
            case '"':
                State = TokenizerState.AttributeValueDoubleQuoted;
                break;
            case '\'':
                State = TokenizerState.AttributeValueSingleQuoted;
                break;
            case '>':
                Error("missing-attribute-value");
                State = TokenizerState.Data;
                EmitTag();
                break;
            default:
                Reconsume(TokenizerState.AttributeValueUnquoted);
                break;
        }
    }

    // §13.2.5.36, §13.2.5.37
    private void QuotedAttributeValueState(char quote, TokenizerState self)
    {
        var c = Consume();
        if (c == quote)
        {
            State = TokenizerState.AfterAttributeValueQuoted;
            return;
        }

        switch (c)
        {
            case '&':
                StartReference(self);
                break;
            case 0:
                Error("unexpected-null-character");
                AppendValue('�', _currentStart);
                break;
            case EndOfFile:
                Error("eof-in-tag");
                EmitEndOfFile();
                break;
            default:
                AppendValue(c, _currentStart);
                break;
        }
    }

    // §13.2.5.38
    private void UnquotedAttributeValueState()
    {
        var c = Consume();
        switch (c)
        {
            case '\t' or '\n' or '\f' or ' ':
                State = TokenizerState.BeforeAttributeName;
                break;
            case '&':
                StartReference(TokenizerState.AttributeValueUnquoted);
                break;
            case '>':
                State = TokenizerState.Data;
                EmitTag();
                break;
            case 0:
                Error("unexpected-null-character");
                AppendValue('�', _currentStart);
                break;
            case '"' or '\'' or '<' or '=' or '`':
                Error("unexpected-character-in-unquoted-attribute-value");
                AppendValue(c, _currentStart);
                break;
            case EndOfFile:
                Error("eof-in-tag");
                EmitEndOfFile();
                break;
            default:
                AppendValue(c, _currentStart);
                break;
        }
    }

    // §13.2.5.39
    private void AfterAttributeValueQuotedState()
    {
        var c = Consume();
        switch (c)
        {
            case '\t' or '\n' or '\f' or ' ':
                State = TokenizerState.BeforeAttributeName;
                break;
            case '/':
                State = TokenizerState.SelfClosingStartTag;
                break;
            case '>':
                State = TokenizerState.Data;
                EmitTag();
                break;
            case EndOfFile:
                Error("eof-in-tag");
                EmitEndOfFile();
                break;
            default:
                Error("missing-whitespace-between-attributes");
                Reconsume(TokenizerState.BeforeAttributeName);
                break;
        }
    }

    // §13.2.5.40
    private void SelfClosingStartTagState()
    {
        var c = Consume();
        switch (c)
        {
            case '>':
                _selfClosing = true;
                State = TokenizerState.Data;
                EmitTag();
                break;
            case EndOfFile:
                Error("eof-in-tag");
                EmitEndOfFile();
                break;
            default:
                Error("unexpected-solidus-in-tag");
                Reconsume(TokenizerState.BeforeAttributeName);
                break;
        }
    }

    // §13.2.5.41
    private void BogusCommentState()
    {
        var c = Consume();
        switch (c)
        {
            case '>':
                State = TokenizerState.Data;
                EmitComment();
                break;
            case EndOfFile:
                EmitComment();
                EmitEndOfFile();
                break;
            case 0:
                Error("unexpected-null-character");
                _data.Append('�');
                break;
            default:
                AppendCodePoint(_data, c);
                break;
        }
    }

    // §13.2.5.42: looks at the next characters without consuming the current one again.
    private void MarkupDeclarationOpenState()
    {
        CheckNextCharacter();
        if (Matches(_index, "--", ignoreCase: false))
        {
            Skip(2);
            CreateComment(string.Empty);
            State = TokenizerState.CommentStart;
        }
        else if (Matches(_index, "DOCTYPE", ignoreCase: true))
        {
            Skip(7);
            State = TokenizerState.Doctype;
        }
        else if (Matches(_index, "[CDATA[", ignoreCase: false))
        {
            Skip(7);
            if (InForeignContent())
            {
                State = TokenizerState.CdataSection;
            }
            else
            {
                Error("cdata-in-html-content");
                CreateComment("[CDATA[");
                State = TokenizerState.BogusComment;
            }
        }
        else
        {
            // The error is at the first character that does not match: the next one.
            _error("incorrectly-opened-comment", _index);
            CreateComment(string.Empty);
            State = TokenizerState.BogusComment;
        }
    }

    // §13.2.5.43
    private void CommentStartState()
    {
        var c = Consume();
        switch (c)
        {
            case '-':
                State = TokenizerState.CommentStartDash;
                break;
            case '>':
                Error("abrupt-closing-of-empty-comment");
                State = TokenizerState.Data;
                EmitComment();
                break;
            default:
                Reconsume(TokenizerState.Comment);
                break;
        }
    }

    // §13.2.5.44
    private void CommentStartDashState()
    {
        var c = Consume();
        switch (c)
        {
            case '-':
                State = TokenizerState.CommentEnd;
                break;
            case '>':
                Error("abrupt-closing-of-empty-comment");
                State = TokenizerState.Data;
                EmitComment();
                break;
            case EndOfFile:
                Error("eof-in-comment");
                EmitComment();
                EmitEndOfFile();
                break;
            default:
                _data.Append('-');
                Reconsume(TokenizerState.Comment);
                break;
        }
    }

    // §13.2.5.45
    private void CommentState()
    {
        var c = Consume();
        switch (c)
        {
            case '<':
                _data.Append('<');
                State = TokenizerState.CommentLessThanSign;
                break;
            case '-':
                State = TokenizerState.CommentEndDash;
                break;
            case 0:
                Error("unexpected-null-character");
                _data.Append('�');
                break;
            case EndOfFile:
                Error("eof-in-comment");
                EmitComment();
                EmitEndOfFile();
                break;
            default:
                AppendCodePoint(_data, c);
                break;
        }
    }

    // §13.2.5.46
    private void CommentLessThanSignState()
    {
        var c = Consume();
        switch (c)
        {
            case '!':
                _data.Append('!');
                State = TokenizerState.CommentLessThanSignBang;
                break;
            case '<':
                _data.Append('<');
                break;
            default:
                Reconsume(TokenizerState.Comment);
                break;
        }
    }

    // §13.2.5.47
    private void CommentLessThanSignBangState()
    {
        var c = Consume();
        if (c == '-')
        {
            State = TokenizerState.CommentLessThanSignBangDash;
        }
        else
        {
            Reconsume(TokenizerState.Comment);
        }
    }

    // §13.2.5.48
    private void CommentLessThanSignBangDashState()
    {
        var c = Consume();
        if (c == '-')
        {
            State = TokenizerState.CommentLessThanSignBangDashDash;
        }
        else
        {
            Reconsume(TokenizerState.CommentEndDash);
        }
    }

    // §13.2.5.49
    private void CommentLessThanSignBangDashDashState()
    {
        var c = Consume();
        if (c is not ('>' or EndOfFile))
        {
            Error("nested-comment");
        }

        Reconsume(TokenizerState.CommentEnd);
    }

    // §13.2.5.50
    private void CommentEndDashState()
    {
        var c = Consume();
        switch (c)
        {
            case '-':
                State = TokenizerState.CommentEnd;
                break;
            case EndOfFile:
                Error("eof-in-comment");
                EmitComment();
                EmitEndOfFile();
                break;
            default:
                _data.Append('-');
                Reconsume(TokenizerState.Comment);
                break;
        }
    }

    // §13.2.5.51
    private void CommentEndState()
    {
        var c = Consume();
        switch (c)
        {
            case '>':
                State = TokenizerState.Data;
                EmitComment();
                break;
            case '!':
                State = TokenizerState.CommentEndBang;
                break;
            case '-':
                _data.Append('-');
                break;
            case EndOfFile:
                Error("eof-in-comment");
                EmitComment();
                EmitEndOfFile();
                break;
            default:
                _data.Append("--");
                Reconsume(TokenizerState.Comment);
                break;
        }
    }

    // §13.2.5.52
    private void CommentEndBangState()
    {
        var c = Consume();
        switch (c)
        {
            case '-':
                _data.Append("--!");
                State = TokenizerState.CommentEndDash;
                break;
            case '>':
                Error("incorrectly-closed-comment");
                State = TokenizerState.Data;
                EmitComment();
                break;
            case EndOfFile:
                Error("eof-in-comment");
                EmitComment();
                EmitEndOfFile();
                break;
            default:
                _data.Append("--!");
                Reconsume(TokenizerState.Comment);
                break;
        }
    }

    // §13.2.5.53
    private void DoctypeState()
    {
        var c = Consume();
        switch (c)
        {
            case '\t' or '\n' or '\f' or ' ':
                State = TokenizerState.BeforeDoctypeName;
                break;
            case '>':
                Reconsume(TokenizerState.BeforeDoctypeName);
                break;
            case EndOfFile:
                Error("eof-in-doctype");
                CreateDoctype();
                EmitDoctype(forceQuirks: true);
                EmitEndOfFile();
                break;
            default:
                Error("missing-whitespace-before-doctype-name");
                Reconsume(TokenizerState.BeforeDoctypeName);
                break;
        }
    }

    // §13.2.5.54
    private void BeforeDoctypeNameState()
    {
        var c = Consume();
        switch (c)
        {
            case '\t' or '\n' or '\f' or ' ':
                break;
            case 0:
                Error("unexpected-null-character");
                CreateDoctype();
                _name.Append('�');
                State = TokenizerState.DoctypeName;
                break;
            case '>':
                Error("missing-doctype-name");
                CreateDoctype();
                State = TokenizerState.Data;
                EmitDoctype(forceQuirks: true);
                break;
            case EndOfFile:
                Error("eof-in-doctype");
                CreateDoctype();
                EmitDoctype(forceQuirks: true);
                EmitEndOfFile();
                break;
            default:
                CreateDoctype();
                AppendCodePoint(_name, InfraAscii.ToLower(c));
                State = TokenizerState.DoctypeName;
                break;
        }
    }

    // §13.2.5.55
    private void DoctypeNameState()
    {
        var c = Consume();
        switch (c)
        {
            case '\t' or '\n' or '\f' or ' ':
                _doctype!.Name = _name.ToString();
                State = TokenizerState.AfterDoctypeName;
                break;
            case '>':
                _doctype!.Name = _name.ToString();
                State = TokenizerState.Data;
                EmitDoctype();
                break;
            case 0:
                Error("unexpected-null-character");
                _name.Append('�');
                break;
            case EndOfFile:
                Error("eof-in-doctype");
                _doctype!.Name = _name.ToString();
                EmitDoctype(forceQuirks: true);
                EmitEndOfFile();
                break;
            default:
                AppendCodePoint(_name, InfraAscii.ToLower(c));
                break;
        }
    }

    // §13.2.5.56
    private void AfterDoctypeNameState()
    {
        var c = Consume();
        switch (c)
        {
            case '\t' or '\n' or '\f' or ' ':
                break;
            case '>':
                State = TokenizerState.Data;
                EmitDoctype();
                break;
            case EndOfFile:
                Error("eof-in-doctype");
                EmitDoctype(forceQuirks: true);
                EmitEndOfFile();
                break;
            default:
                // "the six characters starting from the current input character"
                if (Matches(_currentStart, "PUBLIC", ignoreCase: true))
                {
                    Skip(5);
                    State = TokenizerState.AfterDoctypePublicKeyword;
                }
                else if (Matches(_currentStart, "SYSTEM", ignoreCase: true))
                {
                    Skip(5);
                    State = TokenizerState.AfterDoctypeSystemKeyword;
                }
                else
                {
                    Error("invalid-character-sequence-after-doctype-name");
                    _doctype!.ForceQuirks = true;
                    Reconsume(TokenizerState.BogusDoctype);
                }

                break;
        }
    }

    // §13.2.5.57 (public) and §13.2.5.63 (system)
    private void AfterDoctypeKeywordState(bool isPublic)
    {
        var c = Consume();
        var which = isPublic ? "public" : "system";
        switch (c)
        {
            case '\t' or '\n' or '\f' or ' ':
                State = isPublic ? TokenizerState.BeforeDoctypePublicIdentifier : TokenizerState.BeforeDoctypeSystemIdentifier;
                break;
            case '"' or '\'':
                Error($"missing-whitespace-after-doctype-{which}-keyword");
                StartIdentifier(isPublic, (char)c);
                break;
            case '>':
                Error($"missing-doctype-{which}-identifier");
                State = TokenizerState.Data;
                EmitDoctype(forceQuirks: true);
                break;
            case EndOfFile:
                Error("eof-in-doctype");
                EmitDoctype(forceQuirks: true);
                EmitEndOfFile();
                break;
            default:
                Error($"missing-quote-before-doctype-{which}-identifier");
                _doctype!.ForceQuirks = true;
                Reconsume(TokenizerState.BogusDoctype);
                break;
        }
    }

    // §13.2.5.58 (public) and §13.2.5.64 (system)
    private void BeforeDoctypeIdentifierState(bool isPublic)
    {
        var c = Consume();
        var which = isPublic ? "public" : "system";
        switch (c)
        {
            case '\t' or '\n' or '\f' or ' ':
                break;
            case '"' or '\'':
                StartIdentifier(isPublic, (char)c);
                break;
            case '>':
                Error($"missing-doctype-{which}-identifier");
                State = TokenizerState.Data;
                EmitDoctype(forceQuirks: true);
                break;
            case EndOfFile:
                Error("eof-in-doctype");
                EmitDoctype(forceQuirks: true);
                EmitEndOfFile();
                break;
            default:
                Error($"missing-quote-before-doctype-{which}-identifier");
                _doctype!.ForceQuirks = true;
                Reconsume(TokenizerState.BogusDoctype);
                break;
        }
    }

    /// <summary>Sets an identifier to the empty string (not missing) and enters its quoted state.</summary>
    private void StartIdentifier(bool isPublic, char quote)
    {
        if (isPublic)
        {
            _hasPublicId = true;
            _publicId.Clear();
            State = quote == '"' ? TokenizerState.DoctypePublicIdentifierDoubleQuoted : TokenizerState.DoctypePublicIdentifierSingleQuoted;
        }
        else
        {
            _hasSystemId = true;
            _systemId.Clear();
            State = quote == '"' ? TokenizerState.DoctypeSystemIdentifierDoubleQuoted : TokenizerState.DoctypeSystemIdentifierSingleQuoted;
        }
    }

    // §13.2.5.59, §13.2.5.60, §13.2.5.65, §13.2.5.66
    private void DoctypeIdentifierState(char quote, bool isPublic)
    {
        var c = Consume();
        var identifier = isPublic ? _publicId : _systemId;
        if (c == quote)
        {
            State = isPublic ? TokenizerState.AfterDoctypePublicIdentifier : TokenizerState.AfterDoctypeSystemIdentifier;
            return;
        }

        switch (c)
        {
            case 0:
                Error("unexpected-null-character");
                identifier.Append('�');
                break;
            case '>':
                Error(isPublic ? "abrupt-doctype-public-identifier" : "abrupt-doctype-system-identifier");
                State = TokenizerState.Data;
                EmitDoctype(forceQuirks: true);
                break;
            case EndOfFile:
                Error("eof-in-doctype");
                EmitDoctype(forceQuirks: true);
                EmitEndOfFile();
                break;
            default:
                AppendCodePoint(identifier, c);
                break;
        }
    }

    // §13.2.5.61
    private void AfterDoctypePublicIdentifierState()
    {
        var c = Consume();
        switch (c)
        {
            case '\t' or '\n' or '\f' or ' ':
                State = TokenizerState.BetweenDoctypePublicAndSystemIdentifiers;
                break;
            case '>':
                State = TokenizerState.Data;
                EmitDoctype();
                break;
            case '"' or '\'':
                Error("missing-whitespace-between-doctype-public-and-system-identifiers");
                StartIdentifier(isPublic: false, (char)c);
                break;
            case EndOfFile:
                Error("eof-in-doctype");
                EmitDoctype(forceQuirks: true);
                EmitEndOfFile();
                break;
            default:
                Error("missing-quote-before-doctype-system-identifier");
                _doctype!.ForceQuirks = true;
                Reconsume(TokenizerState.BogusDoctype);
                break;
        }
    }

    // §13.2.5.62
    private void BetweenDoctypePublicAndSystemIdentifiersState()
    {
        var c = Consume();
        switch (c)
        {
            case '\t' or '\n' or '\f' or ' ':
                break;
            case '>':
                State = TokenizerState.Data;
                EmitDoctype();
                break;
            case '"' or '\'':
                StartIdentifier(isPublic: false, (char)c);
                break;
            case EndOfFile:
                Error("eof-in-doctype");
                EmitDoctype(forceQuirks: true);
                EmitEndOfFile();
                break;
            default:
                Error("missing-quote-before-doctype-system-identifier");
                _doctype!.ForceQuirks = true;
                Reconsume(TokenizerState.BogusDoctype);
                break;
        }
    }

    // §13.2.5.67
    private void AfterDoctypeSystemIdentifierState()
    {
        var c = Consume();
        switch (c)
        {
            case '\t' or '\n' or '\f' or ' ':
                break;
            case '>':
                State = TokenizerState.Data;
                EmitDoctype();
                break;
            case EndOfFile:
                Error("eof-in-doctype");
                EmitDoctype(forceQuirks: true);
                EmitEndOfFile();
                break;
            default:
                // This does not set the force-quirks flag.
                Error("unexpected-character-after-doctype-system-identifier");
                Reconsume(TokenizerState.BogusDoctype);
                break;
        }
    }

    // §13.2.5.68
    private void BogusDoctypeState()
    {
        var c = Consume();
        switch (c)
        {
            case '>':
                State = TokenizerState.Data;
                EmitDoctype();
                break;
            case 0:
                Error("unexpected-null-character");
                break;
            case EndOfFile:
                EmitDoctype();
                EmitEndOfFile();
                break;
        }
    }

    // §13.2.5.69
    private void CdataSectionState()
    {
        var c = Consume();
        switch (c)
        {
            case ']':
                _markupStart = _currentStart;
                State = TokenizerState.CdataSectionBracket;
                break;
            case EndOfFile:
                Error("eof-in-cdata");
                EmitEndOfFile();
                break;
            case 0:
                // Chromium 159 reads a U+0000 of a CDATA section as U+FFFD, also where an HTML integration point would
                // drop a U+0000 (HD-1, measured on plain-text-unsafe.dat:397); the standard passes it on.
                EmitCharacter(0xFFFD, _currentStart);
                break;
            default:
                EmitCurrent();
                break;
        }
    }

    // §13.2.5.70
    private void CdataSectionBracketState()
    {
        var c = Consume();
        if (c == ']')
        {
            State = TokenizerState.CdataSectionEnd;
        }
        else
        {
            EmitCharacter(']', _markupStart);
            Reconsume(TokenizerState.CdataSection);
        }
    }

    // §13.2.5.71: _markupStart is the first of the brackets not yet emitted.
    private void CdataSectionEndState()
    {
        var c = Consume();
        switch (c)
        {
            case ']':
                EmitCharacter(']', _markupStart);
                _markupStart++;
                break;
            case '>':
                State = TokenizerState.Data;
                break;
            default:
                EmitCharacter(']', _markupStart);
                EmitCharacter(']', _markupStart + 1);
                Reconsume(TokenizerState.CdataSection);
                break;
        }
    }

    // §13.2.5.72
    private void ProcessingInstructionOpenState()
    {
        var c = Consume();
        if (InfraAscii.IsAlpha(c) || c == '_')
        {
            Reconsume(TokenizerState.ProcessingInstructionTarget);
        }
        else if (c == EndOfFile)
        {
            Error("eof-in-processing-instruction");
            EmitEndOfFile();
        }
        else
        {
            Error("invalid-first-character-of-processing-instruction-target");
            BufferToComment();
            Reconsume(TokenizerState.BogusComment);
        }
    }

    // §13.2.5.73
    private void ProcessingInstructionTargetState()
    {
        var c = Consume();
        if (c is '\t' or '\n' or '\f' or ' ' or '?' or '>')
        {
            var target = _buffer.ToString();
            if (InfraAscii.EqualsIgnoreCase(target, "xml") || InfraAscii.EqualsIgnoreCase(target, "xml-stylesheet"))
            {
                Error("disallowed-processing-instruction-target");
                BufferToComment();
                Reconsume(TokenizerState.BogusComment);
            }
            else
            {
                _data.Clear();
                Reconsume(TokenizerState.AfterProcessingInstructionTarget);
            }
        }
        else if (InfraAscii.IsAlphanumeric(c) || c is '-' or '_')
        {
            _buffer.Append((char)c);
        }
        else if (c == EndOfFile)
        {
            Error("eof-in-processing-instruction");
            EmitEndOfFile();
        }
        else
        {
            Error("invalid-processing-instruction-target");
            BufferToComment();
            Reconsume(TokenizerState.BogusComment);
        }
    }

    // §13.2.5.74
    private void AfterProcessingInstructionTargetState()
    {
        var c = Consume();
        if (!(c is '\t' or '\n' or '\f' or ' '))
        {
            Reconsume(TokenizerState.ProcessingInstructionData);
        }
    }

    // §13.2.5.75
    private void ProcessingInstructionDataState()
    {
        var c = Consume();
        switch (c)
        {
            case '?':
                State = TokenizerState.ProcessingInstructionQuestionable;
                break;
            case '>':
                State = TokenizerState.Data;
                EmitProcessingInstruction();
                break;
            case EndOfFile:
                Error("eof-in-processing-instruction");
                EmitEndOfFile();
                break;
            case 0:
                // As in the other data-holding states; Chromium 159 makes it U+FFFD too (HD-9, measured).
                Error("unexpected-null-character");
                _data.Append('\uFFFD');
                break;
            default:
                AppendCodePoint(_data, c);
                break;
        }
    }

    // §13.2.5.76
    private void ProcessingInstructionQuestionableState()
    {
        var c = Consume();
        switch (c)
        {
            case '>':
                State = TokenizerState.Data;
                EmitProcessingInstruction();
                break;
            case EndOfFile:
                Error("eof-in-processing-instruction");
                EmitEndOfFile();
                break;
            default:
                _data.Append('?');
                Reconsume(TokenizerState.ProcessingInstructionData);
                break;
        }
    }

    /// <summary>"Set the return state to … Switch to the character reference state."</summary>
    private void StartReference(TokenizerState returnState)
    {
        _returnState = returnState;
        _referenceStart = _currentStart;
        State = TokenizerState.CharacterReference;
    }

    // §13.2.5.77
    private void CharacterReferenceState()
    {
        _buffer.Clear();
        _buffer.Append('&');
        var c = Consume();
        if (InfraAscii.IsAlphanumeric(c))
        {
            Reconsume(TokenizerState.NamedCharacterReference);
        }
        else if (c == '#')
        {
            _buffer.Append('#');
            State = TokenizerState.NumericCharacterReference;
        }
        else
        {
            FlushBufferAsWritten();
            Reconsume(_returnState);
        }
    }

    // §13.2.5.78: the current input character (reconsumed) is the first of the name.
    private void NamedCharacterReferenceState()
    {
        var start = _currentStart;
        var (length, characters) = NamedCharacterReferences.LongestMatch(_text, start);
        if (characters is null)
        {
            // Nothing consumed: the ambiguous ampersand state consumes the reconsumed character again.
            FlushBufferAsWritten();
            State = TokenizerState.AmbiguousAmpersand;
            return;
        }

        _reconsume = false;
        _index = start;
        Skip(length);
        _buffer.Append(_text, start, length);
        var endsWithSemicolon = _text[start + length - 1] == ';';
        var next = _index < _text.Length ? _text[_index] : EndOfFile;
        if (ReferenceInAttribute && !endsWithSemicolon && (next == '=' || InfraAscii.IsAlphanumeric(next)))
        {
            FlushBufferAsWritten();
            State = _returnState;
            return;
        }

        if (!endsWithSemicolon)
        {
            _error("missing-semicolon-after-character-reference", _index);
        }

        FlushResolved(characters);
        State = _returnState;
    }

    // §13.2.5.79
    private void AmbiguousAmpersandState()
    {
        var c = Consume();
        if (InfraAscii.IsAlphanumeric(c))
        {
            if (ReferenceInAttribute)
            {
                AppendValue(c, _currentStart);
            }
            else
            {
                EmitCurrent();
            }
        }
        else
        {
            if (c == ';')
            {
                Error("unknown-named-character-reference");
            }

            Reconsume(_returnState);
        }
    }

    // §13.2.5.80
    private void NumericCharacterReferenceState()
    {
        _characterReferenceCode = 0;
        var c = Consume();
        if (c is 'x' or 'X')
        {
            _buffer.Append((char)c);
            State = TokenizerState.HexadecimalCharacterReferenceStart;
        }
        else if (InfraAscii.IsDigit(c))
        {
            Reconsume(TokenizerState.DecimalCharacterReference);
        }
        else
        {
            Error("absence-of-digits-in-numeric-character-reference");
            FlushBufferAsWritten();
            Reconsume(_returnState);
        }
    }

    // §13.2.5.81
    private void HexadecimalCharacterReferenceStartState()
    {
        var c = Consume();
        if (InfraAscii.IsHexDigit(c))
        {
            Reconsume(TokenizerState.HexadecimalCharacterReference);
        }
        else
        {
            Error("absence-of-digits-in-numeric-character-reference");
            FlushBufferAsWritten();
            Reconsume(_returnState);
        }
    }

    // §13.2.5.82 (hexadecimal) and §13.2.5.83 (decimal). The code stops growing past U+10FFFF, so it cannot overflow.
    private void DigitsState(bool hexadecimal)
    {
        var c = Consume();
        var isDigit = hexadecimal ? InfraAscii.IsHexDigit(c) : InfraAscii.IsDigit(c);
        if (isDigit)
        {
            var value = InfraAscii.IsDigit(c) ? c - '0' : InfraAscii.ToLower(c) - 'a' + 10;
            _characterReferenceCode = System.Math.Min(_characterReferenceCode * (hexadecimal ? 16 : 10) + value, 0x110000);
        }
        else if (c == ';')
        {
            State = TokenizerState.NumericCharacterReferenceEnd;
        }
        else
        {
            Error("missing-semicolon-after-character-reference");
            Reconsume(TokenizerState.NumericCharacterReferenceEnd);
        }
    }

    // §13.2.5.84: this state consumes nothing; its errors are at the next input character.
    private void NumericCharacterReferenceEndState()
    {
        void Error(string name) => _error(name, NextIndex);
        var code = _characterReferenceCode;
        if (code == 0)
        {
            Error("null-character-reference");
            code = 0xFFFD;
        }
        else if (code > 0x10FFFF)
        {
            Error("character-reference-outside-unicode-range");
            code = 0xFFFD;
        }
        else if (code is >= 0xD800 and <= 0xDFFF)
        {
            Error("surrogate-character-reference");
            code = 0xFFFD;
        }
        else if (IsNoncharacter(code))
        {
            Error("noncharacter-character-reference");
        }
        else if (code == 0x0D || (IsControl(code) && !InfraAscii.IsWhitespace((int)code)))
        {
            Error("control-character-reference");
            code = Windows1252Replacement((int)code);
        }

        FlushResolved(char.ConvertFromUtf32((int)code));
        State = _returnState;
    }

    /// <summary>The table of §13.2.5.84: C1 controls that windows-1252 maps to printable characters.</summary>
    private static int Windows1252Replacement(int code) => code switch
    {
        0x80 => 0x20AC,
        0x82 => 0x201A,
        0x83 => 0x0192,
        0x84 => 0x201E,
        0x85 => 0x2026,
        0x86 => 0x2020,
        0x87 => 0x2021,
        0x88 => 0x02C6,
        0x89 => 0x2030,
        0x8A => 0x0160,
        0x8B => 0x2039,
        0x8C => 0x0152,
        0x8E => 0x017D,
        0x91 => 0x2018,
        0x92 => 0x2019,
        0x93 => 0x201C,
        0x94 => 0x201D,
        0x95 => 0x2022,
        0x96 => 0x2013,
        0x97 => 0x2014,
        0x98 => 0x02DC,
        0x99 => 0x2122,
        0x9A => 0x0161,
        0x9B => 0x203A,
        0x9C => 0x0153,
        0x9E => 0x017E,
        0x9F => 0x0178,
        _ => code,
    };
}
