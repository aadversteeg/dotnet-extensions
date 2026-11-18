using System;
using Ave.Extensions.Documents.Html.Parsing.Tokenizing;

namespace Ave.Extensions.Documents.Html.Parsing.TreeBuilding;

/// <summary>The rules for parsing tokens in HTML content: one method per insertion mode (HTML Standard §13.2.6.4).</summary>
internal sealed partial class TreeBuilder
{
    private static readonly string[] Headings = ["h1", "h2", "h3", "h4", "h5", "h6"];

    private void ProcessIn(InsertionMode mode, Input t)
    {
        switch (mode)
        {
            case InsertionMode.Initial: Initial(t); break;
            case InsertionMode.BeforeHtml: BeforeHtml(t); break;
            case InsertionMode.BeforeHead: BeforeHead(t); break;
            case InsertionMode.InHead: InHead(t); break;
            case InsertionMode.InHeadNoscript: InHeadNoscript(t); break;
            case InsertionMode.AfterHead: AfterHead(t); break;
            case InsertionMode.InBody: InBody(t); break;
            case InsertionMode.Text: InText(t); break;
            case InsertionMode.InTable: InTable(t); break;
            case InsertionMode.InTableText: InTableText(t); break;
            case InsertionMode.InCaption: InCaption(t); break;
            case InsertionMode.InColumnGroup: InColumnGroup(t); break;
            case InsertionMode.InTableBody: InTableBody(t); break;
            case InsertionMode.InRow: InRow(t); break;
            case InsertionMode.InCell: InCell(t); break;
            case InsertionMode.InTemplate: InTemplate(t); break;
            case InsertionMode.AfterBody: AfterBody(t); break;
            case InsertionMode.InFrameset: InFrameset(t); break;
            case InsertionMode.AfterFrameset: AfterFrameset(t); break;
            case InsertionMode.AfterAfterBody: AfterAfterBody(t); break;
            case InsertionMode.AfterAfterFrameset: AfterAfterFrameset(t); break;
        }
    }

    /// <summary>Switches the mode and reprocesses the token.</summary>
    private void Reprocess(InsertionMode mode, Input t)
    {
        _mode = mode;
        Process(t);
    }

    // §13.2.6.4.1
    private void Initial(Input t)
    {
        if (t.IsWhitespace)
        {
            return;
        }

        switch (t.Kind)
        {
            case TokenKind.Comment:
                InsertComment(t.Token!, _document);
                return;
            case TokenKind.ProcessingInstruction:
                InsertProcessingInstruction(t.Token!, _document);
                return;
            case TokenKind.Doctype:
                Doctype(t.Token!);
                _mode = InsertionMode.BeforeHtml;
                return;
        }

        Error("missing-doctype", t);
        _document.Mode = DocumentMode.Quirks;
        Reprocess(InsertionMode.BeforeHtml, t);
    }

    /// <summary>The DOCTYPE token in the initial mode: the parse error, the doctype node and the document's mode.</summary>
    private void Doctype(Token token)
    {
        var name = token.Name;
        if (name != "html" || token.PublicId is not null || (token.SystemId is not null && token.SystemId != "about:legacy-compat"))
        {
            Error("unknown-doctype", token.Start, name);
        }

        if (!_document.HasElementChild() && !HasDoctypeChild())
        {
            _document.Insert(new MutableDocumentType { Name = name ?? string.Empty, PublicId = token.PublicId ?? string.Empty, SystemId = token.SystemId ?? string.Empty, Start = token.Start }, null);
        }

        _document.Mode = DoctypeMode(token);
    }

    private bool HasDoctypeChild()
    {
        for (var child = _document.First; child is not null; child = child.Next)
        {
            if (child is MutableDocumentType)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The quirks and limited-quirks conditions of §13.2.6.4.1 (compared ASCII case-insensitively).</summary>
    private static DocumentMode DoctypeMode(Token token)
    {
        var publicId = token.PublicId is null ? null : Ascii.ToLower(token.PublicId);
        var systemId = token.SystemId is null ? null : Ascii.ToLower(token.SystemId);
        var systemMissingOrEmpty = string.IsNullOrEmpty(systemId);
        bool PublicStarts(string prefix) => publicId is not null && publicId.StartsWith(prefix, StringComparison.Ordinal);

        if (token.ForceQuirks
            || token.Name != "html"
            || publicId is "-//w3o//dtd w3 html strict 3.0//en//" or "-/w3c/dtd html 4.0 transitional/en" or "html"
            || systemId == "http://www.ibm.com/data/dtd/v11/ibmxhtml1-transitional.dtd"
            || Array.Exists(ElementTables.QuirksPublicIdPrefixes, PublicStarts)
            || (systemMissingOrEmpty && (PublicStarts("-//w3c//dtd html 4.01 frameset//") || PublicStarts("-//w3c//dtd html 4.01 transitional//"))))
        {
            return DocumentMode.Quirks;
        }

        if (PublicStarts("-//w3c//dtd xhtml 1.0 frameset//") || PublicStarts("-//w3c//dtd xhtml 1.0 transitional//")
            || (!systemMissingOrEmpty && (PublicStarts("-//w3c//dtd html 4.01 frameset//") || PublicStarts("-//w3c//dtd html 4.01 transitional//"))))
        {
            return DocumentMode.LimitedQuirks;
        }

        return DocumentMode.NoQuirks;
    }

    // §13.2.6.4.2
    private void BeforeHtml(Input t)
    {
        switch (t.Kind)
        {
            case TokenKind.Doctype:
                Error("unexpected-doctype", t);
                return;
            case TokenKind.Comment:
                InsertComment(t.Token!, _document);
                return;
            case TokenKind.ProcessingInstruction:
                InsertProcessingInstruction(t.Token!, _document);
                return;
        }

        if (t.IsWhitespace)
        {
            return;
        }

        if (t.IsStart("html"))
        {
            var html = CreateElement(t.Token!, ElementNamespace.Html);
            InsertAtPlace(html, (_document, null));
            Push(html);
            _mode = InsertionMode.BeforeHead;
            return;
        }

        if (t.Kind == TokenKind.EndTag && !t.IsEnd("head", "body", "html", "br"))
        {
            Error("unexpected-end-tag", t);
            return;
        }

        var root = CreateElement(new Token { Kind = TokenKind.StartTag, Name = "html", Start = t.Source }, ElementNamespace.Html);
        InsertAtPlace(root, (_document, null));
        Push(root);
        Reprocess(InsertionMode.BeforeHead, t);
    }

    // §13.2.6.4.3
    private void BeforeHead(Input t)
    {
        if (t.IsWhitespace)
        {
            return;
        }

        switch (t.Kind)
        {
            case TokenKind.Comment:
                InsertComment(t.Token!);
                return;
            case TokenKind.ProcessingInstruction:
                InsertProcessingInstruction(t.Token!);
                return;
            case TokenKind.Doctype:
                Error("unexpected-doctype", t);
                return;
        }

        if (t.IsStart("html"))
        {
            InBody(t);
            return;
        }

        if (t.IsStart("head"))
        {
            _head = InsertHtmlElement(t.Token!);
            _mode = InsertionMode.InHead;
            return;
        }

        if (t.Kind == TokenKind.EndTag && !t.IsEnd("head", "body", "html", "br"))
        {
            Error("unexpected-end-tag", t);
            return;
        }

        _head = InsertImpliedElement("head", t.Source);
        Reprocess(InsertionMode.InHead, t);
    }

    // §13.2.6.4.4
    private void InHead(Input t)
    {
        if (t.IsWhitespace)
        {
            InsertCharacter(t.Char, t.Source);
            return;
        }

        switch (t.Kind)
        {
            case TokenKind.Comment:
                InsertComment(t.Token!);
                return;
            case TokenKind.ProcessingInstruction:
                InsertProcessingInstruction(t.Token!);
                return;
            case TokenKind.Doctype:
                Error("unexpected-doctype", t);
                return;
        }

        if (t.IsStart("html"))
        {
            InBody(t);
        }
        else if (t.IsStart("base", "basefont", "bgsound", "link"))
        {
            InsertHtmlElement(t.Token!);
            Pop();
            t.Token!.SelfClosingAcknowledged = true;
        }
        else if (t.IsStart("meta"))
        {
            var meta = InsertHtmlElement(t.Token!);
            Pop();
            t.Token!.SelfClosingAcknowledged = true;
            MetaEncoding?.Invoke(meta);
        }
        else if (t.IsStart("title"))
        {
            GenericTextElement(t.Token!, TokenizerState.Rcdata);
        }
        else if (t.IsStart("noframes", "style"))
        {
            GenericTextElement(t.Token!, TokenizerState.Rawtext);
        }
        else if (t.IsStart("noscript"))
        {
            // The parser's scripting flag is off ("parser scripting disabled" is true).
            InsertHtmlElement(t.Token!);
            _mode = InsertionMode.InHeadNoscript;
        }
        else if (t.IsStart("script"))
        {
            var place = AppropriatePlace();
            var script = CreateElement(t.Token!, ElementNamespace.Html);
            InsertAtPlace(script, place);
            Push(script);
            _tokenizer.State = TokenizerState.ScriptData;
            _originalMode = _mode;
            _mode = InsertionMode.Text;
        }
        else if (t.IsEnd("head"))
        {
            Pop();
            _mode = InsertionMode.AfterHead;
        }
        else if (t.IsStart("template"))
        {
            InsertMarker();
            _framesetOk = false;
            _mode = InsertionMode.InTemplate;
            _templateModes.Add(InsertionMode.InTemplate);
            var place = AppropriatePlace();
            var template = CreateElement(t.Token!, ElementNamespace.Html);
            Push(template);
            InsertAtPlace(template, place);
        }
        else if (t.IsEnd("template"))
        {
            if (!HasOnStack("template"))
            {
                Error("unexpected-end-tag", t);
                return;
            }

            GenerateAllImpliedEndTagsThoroughly();
            if (!CurrentIsHtml("template"))
            {
                Error("misnested-end-tag", t);
            }

            PopUntilHtml("template");
            ClearFormattingToLastMarker();
            _templateModes.RemoveAt(_templateModes.Count - 1);
            ResetInsertionMode();
        }
        else if (t.IsStart("head") || (t.Kind == TokenKind.EndTag && !t.IsEnd("body", "html", "br")))
        {
            Error(t.Kind == TokenKind.StartTag ? "unexpected-start-tag" : "unexpected-end-tag", t);
        }
        else
        {
            Pop();
            Reprocess(InsertionMode.AfterHead, t);
        }
    }

    /// <summary>Called for every meta element inserted in the "in head" rules, so the parser can apply its encoding
    /// rule (§13.2.6.4.4: a charset that changes the encoding while the confidence is tentative).</summary>
    public Action<MutableElement>? MetaEncoding { get; init; }

    /// <summary>"The generic raw text element parsing algorithm" and "the generic RCDATA element parsing algorithm"
    /// (§13.2.6.2).</summary>
    private void GenericTextElement(Token token, TokenizerState state)
    {
        InsertHtmlElement(token);
        _tokenizer.State = state;
        _originalMode = _mode;
        _mode = InsertionMode.Text;
    }

    // §13.2.6.4.5
    private void InHeadNoscript(Input t)
    {
        if (t.Kind == TokenKind.Doctype)
        {
            Error("unexpected-doctype", t);
        }
        else if (t.IsStart("html"))
        {
            InBody(t);
        }
        else if (t.IsEnd("noscript"))
        {
            Pop();
            _mode = InsertionMode.InHead;
        }
        else if (t.IsWhitespace || t.Kind is TokenKind.Comment or TokenKind.ProcessingInstruction
                 || t.IsStart("basefont", "bgsound", "link", "meta", "noframes", "style"))
        {
            InHead(t);
        }
        else if (t.IsStart("head", "noscript") || (t.Kind == TokenKind.EndTag && !t.IsEnd("br")))
        {
            Error(t.Kind == TokenKind.StartTag ? "unexpected-start-tag" : "unexpected-end-tag", t);
        }
        else
        {
            Error(t.Kind == TokenKind.Characters ? "unexpected-character" : "unexpected-start-tag", t);
            Pop();
            Reprocess(InsertionMode.InHead, t);
        }
    }

    // §13.2.6.4.6
    private void AfterHead(Input t)
    {
        if (t.IsWhitespace)
        {
            InsertCharacter(t.Char, t.Source);
            return;
        }

        switch (t.Kind)
        {
            case TokenKind.Comment:
                InsertComment(t.Token!);
                return;
            case TokenKind.ProcessingInstruction:
                InsertProcessingInstruction(t.Token!);
                return;
            case TokenKind.Doctype:
                Error("unexpected-doctype", t);
                return;
        }

        if (t.IsStart("html"))
        {
            InBody(t);
        }
        else if (t.IsStart("body"))
        {
            InsertHtmlElement(t.Token!);
            _framesetOk = false;
            _mode = InsertionMode.InBody;
        }
        else if (t.IsStart("frameset"))
        {
            InsertHtmlElement(t.Token!);
            _mode = InsertionMode.InFrameset;
        }
        else if (t.IsStart("base", "basefont", "bgsound", "link", "meta", "noframes", "script", "style", "template", "title"))
        {
            Error("unexpected-start-tag", t);
            var head = _head!;
            Push(head);
            InHead(t);
            RemoveFromStack(head);
        }
        else if (t.IsEnd("template"))
        {
            InHead(t);
        }
        else if (t.IsStart("head") || (t.Kind == TokenKind.EndTag && !t.IsEnd("body", "html", "br")))
        {
            Error(t.Kind == TokenKind.StartTag ? "unexpected-start-tag" : "unexpected-end-tag", t);
        }
        else
        {
            InsertImpliedElement("body", t.Source);
            _framesetOk = true;
            Reprocess(InsertionMode.InBody, t);
        }
    }

    // §13.2.6.4.7
    private void InBody(Input t)
    {
        switch (t.Kind)
        {
            case TokenKind.Characters:
                InBodyCharacter(t);
                return;
            case TokenKind.Comment:
                InsertComment(t.Token!);
                return;
            case TokenKind.ProcessingInstruction:
                InsertProcessingInstruction(t.Token!);
                return;
            case TokenKind.Doctype:
                Error("unexpected-doctype", t);
                return;
            case TokenKind.EndOfFile:
                InBodyEndOfFile(t);
                return;
            case TokenKind.StartTag:
                InBodyStartTag(t);
                return;
            default:
                InBodyEndTag(t);
                return;
        }
    }

    private void InBodyCharacter(Input t)
    {
        if (t.Char == 0)
        {
            Error("unexpected-null-character", t);
            return;
        }

        ReconstructFormatting();
        InsertCharacter(t.Char, t.Source);
        if (!t.IsWhitespace)
        {
            _framesetOk = false;
        }
    }

    private void InBodyEndOfFile(Input t)
    {
        if (_templateModes.Count > 0)
        {
            InTemplate(t);
            return;
        }

        ReportUnclosed(t);
    }

    /// <summary>The parse error of §13.2.6.4.7 at the end of the body: an element other than those that may stay open.</summary>
    private void ReportUnclosed(Input t)
    {
        foreach (var element in _stack)
        {
            if (!(element.Namespace == ElementNamespace.Html && ElementTables.MayStayOpen.Contains(element.LocalName)))
            {
                Error("unclosed-element", t.Source, element.LocalName);
                return;
            }
        }
    }

    private void InBodyStartTag(Input t)
    {
        var token = t.Token!;
        switch (token.Name)
        {
            case "html":
                Error("unexpected-start-tag", t);
                if (!HasOnStack("template"))
                {
                    AddMissingAttributes(_stack[0], token);
                }

                return;
            case "base" or "basefont" or "bgsound" or "link" or "meta" or "noframes" or "script" or "style" or "template" or "title":
                InHead(t);
                return;
            case "body":
                Error("unexpected-start-tag", t);
                if (_stack.Count == 1 || !_stack[1].IsHtml("body") || HasOnStack("template"))
                {
                    return;
                }

                _framesetOk = false;
                AddMissingAttributes(_stack[1], token);
                return;
            case "frameset":
                Error("unexpected-start-tag", t);
                if (_stack.Count == 1 || !_stack[1].IsHtml("body") || !_framesetOk)
                {
                    return;
                }

                _stack[1].Remove();
                while (_stack.Count > 1)
                {
                    Pop();
                }

                InsertHtmlElement(token);
                _mode = InsertionMode.InFrameset;
                return;
            case "address" or "article" or "aside" or "blockquote" or "center" or "details" or "dialog" or "dir" or "div"
                or "dl" or "fieldset" or "figcaption" or "figure" or "footer" or "header" or "hgroup" or "main" or "menu"
                or "nav" or "ol" or "p" or "search" or "section" or "summary" or "ul":
                CloseParagraphInButtonScope();
                InsertHtmlElement(token);
                return;
            case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                CloseParagraphInButtonScope();
                if (CurrentNode is { Namespace: ElementNamespace.Html } current && Array.IndexOf(Headings, current.LocalName) >= 0)
                {
                    Error("nested-heading", t);
                    Pop();
                }

                InsertHtmlElement(token);
                return;
            case "pre" or "listing":
                CloseParagraphInButtonScope();
                InsertHtmlElement(token);
                _skipNextLineFeed = true;
                _framesetOk = false;
                return;
            case "form":
                if (_form is not null && !ParsingTemplateContents)
                {
                    Error("unexpected-start-tag", t);
                    return;
                }

                CloseParagraphInButtonScope();
                var form = InsertHtmlElement(token);
                if (!ParsingTemplateContents)
                {
                    _form = form;
                }

                return;
            case "li":
                ListItemStart(t, ["li"]);
                return;
            case "dd" or "dt":
                ListItemStart(t, ["dd", "dt"]);
                return;
            case "plaintext":
                CloseParagraphInButtonScope();
                InsertHtmlElement(token);
                _tokenizer.State = TokenizerState.Plaintext;
                return;
            case "button":
                if (HasInScope(ElementTables.Scope.Default, "button"))
                {
                    Error("unexpected-start-tag", t);
                    GenerateImpliedEndTags();
                    PopUntilHtml("button");
                }

                ReconstructFormatting();
                InsertHtmlElement(token);
                _framesetOk = false;
                return;
            case "a":
                if (FormattingAfterLastMarker("a") is { } existing)
                {
                    Error("unexpected-start-tag", t);
                    AdoptionAgency(t);
                    RemoveFormatting(existing);
                    RemoveFromStack(existing);
                }

                ReconstructFormatting();
                PushFormatting(InsertHtmlElement(token));
                return;
            case "b" or "big" or "code" or "em" or "font" or "i" or "s" or "small" or "strike" or "strong" or "tt" or "u":
                ReconstructFormatting();
                PushFormatting(InsertHtmlElement(token));
                return;
            case "nobr":
                ReconstructFormatting();
                if (HasInScope(ElementTables.Scope.Default, "nobr"))
                {
                    Error("unexpected-start-tag", t);
                    AdoptionAgency(t);
                    ReconstructFormatting();
                }

                PushFormatting(InsertHtmlElement(token));
                return;
            case "applet" or "marquee" or "object":
                ReconstructFormatting();
                InsertHtmlElement(token);
                InsertMarker();
                _framesetOk = false;
                return;
            case "table":
                if (_document.Mode != DocumentMode.Quirks)
                {
                    CloseParagraphInButtonScope();
                }

                InsertHtmlElement(token);
                _framesetOk = false;
                _mode = InsertionMode.InTable;
                return;
            case "area" or "br" or "embed" or "img" or "keygen" or "wbr":
                VoidElement(token);
                _framesetOk = false;
                return;
            case "input":
                if (_context is not null && _context.IsHtml("select"))
                {
                    Error("unexpected-start-tag", t);
                    return;
                }

                if (HasInScope(ElementTables.Scope.Default, "select"))
                {
                    Error("unexpected-start-tag", t);
                    PopUntilHtml("select");
                }

                VoidElement(token);
                if (token.Attribute("type") is not { } type || !Ascii.EqualsIgnoreCase(type, "hidden"))
                {
                    _framesetOk = false;
                }

                return;
            case "param" or "source" or "track":
                InsertHtmlElement(token);
                Pop();
                token.SelfClosingAcknowledged = true;
                return;
            case "hr":
                CloseParagraphInButtonScope();
                if (HasInScope(ElementTables.Scope.Default, "select"))
                {
                    GenerateImpliedEndTags();
                    if (HasInScope(ElementTables.Scope.Default, "option") || HasInScope(ElementTables.Scope.Default, "optgroup"))
                    {
                        Error("unexpected-start-tag", t);
                    }
                }

                InsertHtmlElement(token);
                Pop();
                token.SelfClosingAcknowledged = true;
                _framesetOk = false;
                return;
            case "image":
                Error("unexpected-start-tag", t);
                token.Name = "img";
                Process(t);
                return;
            case "textarea":
                InsertHtmlElement(token);
                _skipNextLineFeed = true;
                _tokenizer.State = TokenizerState.Rcdata;
                _originalMode = _mode;
                _framesetOk = false;
                _mode = InsertionMode.Text;
                return;
            case "xmp":
                CloseParagraphInButtonScope();
                ReconstructFormatting();
                _framesetOk = false;
                GenericTextElement(token, TokenizerState.Rawtext);
                return;
            case "iframe":
                _framesetOk = false;
                GenericTextElement(token, TokenizerState.Rawtext);
                return;
            case "noembed":
                GenericTextElement(token, TokenizerState.Rawtext);
                return;
            case "select":
                if (_context is not null && _context.IsHtml("select"))
                {
                    Error("unexpected-start-tag", t);
                    return;
                }

                if (HasInScope(ElementTables.Scope.Default, "select"))
                {
                    Error("unexpected-start-tag", t);
                    PopUntilHtml("select");
                    return;
                }

                ReconstructFormatting();
                InsertHtmlElement(token);
                _framesetOk = false;
                return;
            case "option":
                if (HasInScope(ElementTables.Scope.Default, "select"))
                {
                    GenerateImpliedEndTags(except: "optgroup");
                    if (HasInScope(ElementTables.Scope.Default, "option"))
                    {
                        Error("unexpected-start-tag", t);
                    }
                }
                else if (CurrentIsHtml("option"))
                {
                    Pop();
                }

                ReconstructFormatting();
                InsertHtmlElement(token);
                return;
            case "optgroup":
                if (HasInScope(ElementTables.Scope.Default, "select"))
                {
                    GenerateImpliedEndTags();
                    if (HasInScope(ElementTables.Scope.Default, "option") || HasInScope(ElementTables.Scope.Default, "optgroup"))
                    {
                        Error("unexpected-start-tag", t);
                    }
                }
                else if (CurrentIsHtml("option"))
                {
                    Pop();
                }

                ReconstructFormatting();
                InsertHtmlElement(token);
                return;
            case "rb" or "rtc":
                if (HasInScope(ElementTables.Scope.Default, "ruby"))
                {
                    GenerateImpliedEndTags();
                    if (!CurrentIsHtml("ruby"))
                    {
                        Error("unexpected-start-tag", t);
                    }
                }

                InsertHtmlElement(token);
                return;
            case "rp" or "rt":
                if (HasInScope(ElementTables.Scope.Default, "ruby"))
                {
                    GenerateImpliedEndTags(except: "rtc");
                    if (!CurrentIsHtml("rtc") && !CurrentIsHtml("ruby"))
                    {
                        Error("unexpected-start-tag", t);
                    }
                }

                InsertHtmlElement(token);
                return;
            case "math":
                ReconstructFormatting();
                AdjustMathMlAttributes(token);
                InsertForeignElement(token, ElementNamespace.MathMl);
                if (token.SelfClosing)
                {
                    Pop();
                    token.SelfClosingAcknowledged = true;
                }

                return;
            case "svg":
                ReconstructFormatting();
                AdjustSvgAttributes(token);
                InsertForeignElement(token, ElementNamespace.Svg);
                if (token.SelfClosing)
                {
                    Pop();
                    token.SelfClosingAcknowledged = true;
                }

                return;
            case "caption" or "col" or "colgroup" or "frame" or "head" or "tbody" or "td" or "tfoot" or "th" or "thead" or "tr":
                Error("unexpected-start-tag", t);
                return;
            default:
                // "noscript" too: the parser's scripting flag is off.
                ReconstructFormatting();
                InsertHtmlElement(token);
                return;
        }
    }

    private void VoidElement(Token token)
    {
        ReconstructFormatting();
        InsertHtmlElement(token);
        Pop();
        token.SelfClosingAcknowledged = true;
    }

    /// <summary>The html and body start tags in the body: attributes the element lacks are added to it.</summary>
    private void AddMissingAttributes(MutableElement element, Token token)
    {
        foreach (var attribute in token.Attributes)
        {
            if (element.Attribute(attribute.Name) is null)
            {
                element.AddAttribute(new HtmlAttribute(AttributeNamespace.None, null, attribute.Name, attribute.Value, Position(attribute.Start)));
            }
        }
    }

    /// <summary>The li, dd and dt start tags (§13.2.6.4.7): close an open item of the same kind first.</summary>
    private void ListItemStart(Input t, string[] kinds)
    {
        _framesetOk = false;
        for (var i = _stack.Count - 1; i >= 0; i--)
        {
            var node = _stack[i];
            if (node.Namespace == ElementNamespace.Html && Array.IndexOf(kinds, node.LocalName) >= 0)
            {
                GenerateImpliedEndTags(except: node.LocalName);
                if (!CurrentIsHtml(node.LocalName))
                {
                    Error("unexpected-start-tag", t);
                }

                PopUntilHtml(node.LocalName);
                break;
            }

            if (ElementTables.IsSpecial(node) && !(node.Namespace == ElementNamespace.Html && node.LocalName is "address" or "div" or "p"))
            {
                break;
            }
        }

        CloseParagraphInButtonScope();
        InsertHtmlElement(t.Token!);
    }

    /// <summary>"If the stack of open elements has a p element in button scope, then close a p element."</summary>
    private void CloseParagraphInButtonScope()
    {
        if (HasInScope(ElementTables.Scope.Button, "p"))
        {
            CloseParagraph();
        }
    }

    /// <summary>"Close a p element".</summary>
    private void CloseParagraph()
    {
        GenerateImpliedEndTags(except: "p");
        if (!CurrentIsHtml("p"))
        {
            Error("unclosed-element", CurrentNode!.Start, CurrentNode.LocalName);
        }

        PopUntilHtml("p");
    }

    private void InBodyEndTag(Input t)
    {
        var token = t.Token!;
        switch (token.Name)
        {
            case "template":
                InHead(t);
                return;
            case "body":
                if (!HasInScope(ElementTables.Scope.Default, "body"))
                {
                    Error("unexpected-end-tag", t);
                    return;
                }

                ReportUnclosed(t);
                _mode = InsertionMode.AfterBody;
                return;
            case "html":
                if (!HasInScope(ElementTables.Scope.Default, "body"))
                {
                    Error("unexpected-end-tag", t);
                    return;
                }

                ReportUnclosed(t);
                Reprocess(InsertionMode.AfterBody, t);
                return;
            case "address" or "article" or "aside" or "blockquote" or "button" or "center" or "details" or "dialog"
                or "dir" or "div" or "dl" or "fieldset" or "figcaption" or "figure" or "footer" or "header" or "hgroup"
                or "listing" or "main" or "menu" or "nav" or "ol" or "pre" or "search" or "section" or "select"
                or "summary" or "ul":
                CloseByName(t, token.Name!);
                return;
            case "form":
                FormEndTag(t);
                return;
            case "p":
                if (!HasInScope(ElementTables.Scope.Button, "p"))
                {
                    Error("unexpected-end-tag", t);
                    InsertImpliedElement("p", t.Source);
                }

                CloseParagraph();
                return;
            case "li":
                if (!HasInScope(ElementTables.Scope.ListItem, "li"))
                {
                    Error("unexpected-end-tag", t);
                    return;
                }

                GenerateImpliedEndTags(except: "li");
                if (!CurrentIsHtml("li"))
                {
                    Error("misnested-end-tag", t);
                }

                PopUntilHtml("li");
                return;
            case "dd" or "dt":
                if (!HasInScope(ElementTables.Scope.Default, token.Name!))
                {
                    Error("unexpected-end-tag", t);
                    return;
                }

                GenerateImpliedEndTags(except: token.Name);
                if (!CurrentIsHtml(token.Name!))
                {
                    Error("misnested-end-tag", t);
                }

                PopUntilHtml(token.Name!);
                return;
            case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                if (!HasInScope(ElementTables.Scope.Default, Headings))
                {
                    Error("unexpected-end-tag", t);
                    return;
                }

                GenerateImpliedEndTags();
                if (!CurrentIsHtml(token.Name!))
                {
                    Error("misnested-end-tag", t);
                }

                PopUntil(element => element.Namespace == ElementNamespace.Html && Array.IndexOf(Headings, element.LocalName) >= 0);
                return;
            case "a" or "b" or "big" or "code" or "em" or "font" or "i" or "nobr" or "s" or "small" or "strike" or "strong" or "tt" or "u":
                AdoptionAgency(t);
                return;
            case "applet" or "marquee" or "object":
                if (!HasInScope(ElementTables.Scope.Default, token.Name!))
                {
                    Error("unexpected-end-tag", t);
                    return;
                }

                GenerateImpliedEndTags();
                if (!CurrentIsHtml(token.Name!))
                {
                    Error("misnested-end-tag", t);
                }

                PopUntilHtml(token.Name!);
                ClearFormattingToLastMarker();
                return;
            case "br":
                // "Drop the attributes from the token, and act as if this was a br start tag with no attributes."
                Error("unexpected-end-tag", t);
                var br = new Token { Kind = TokenKind.StartTag, Name = "br", Start = token.Start };
                VoidElement(br);
                _framesetOk = false;
                return;
            default:
                AnyOtherEndTag(t);
                return;
        }
    }

    /// <summary>The block end tags of §13.2.6.4.7: close the element of that name when it is in scope.</summary>
    private void CloseByName(Input t, string name)
    {
        if (!HasInScope(ElementTables.Scope.Default, name))
        {
            Error("unexpected-end-tag", t);
            return;
        }

        GenerateImpliedEndTags();
        if (!CurrentIsHtml(name))
        {
            Error("misnested-end-tag", t);
        }

        PopUntilHtml(name);
    }

    private void FormEndTag(Input t)
    {
        if (!ParsingTemplateContents)
        {
            var node = _form;
            _form = null;
            if (node is null || !HasElementInScope(node))
            {
                Error("unexpected-end-tag", t);
                return;
            }

            GenerateImpliedEndTags();
            if (CurrentNode != node)
            {
                Error("misnested-end-tag", t);
            }

            RemoveFromStack(node);
            return;
        }

        if (!HasInScope(ElementTables.Scope.Default, "form"))
        {
            Error("unexpected-end-tag", t);
            return;
        }

        GenerateImpliedEndTags();
        if (!CurrentIsHtml("form"))
        {
            Error("misnested-end-tag", t);
        }

        PopUntilHtml("form");
    }

    /// <summary>"Any other end tag" of §13.2.6.4.7.</summary>
    private void AnyOtherEndTag(Input t)
    {
        var name = t.Name!;
        for (var i = _stack.Count - 1; i >= 0; i--)
        {
            var node = _stack[i];
            if (node.IsHtml(name))
            {
                GenerateImpliedEndTags(except: name);
                if (node != CurrentNode)
                {
                    Error("misnested-end-tag", t);
                }

                while (Pop() != node)
                {
                }

                return;
            }

            if (ElementTables.IsSpecial(node))
            {
                Error("unexpected-end-tag", t);
                return;
            }
        }
    }

    /// <summary>"The parser is parsing template contents" (§13.2.4.4): a template on the stack, or a template as the
    /// fragment's context element.</summary>
    private bool ParsingTemplateContents => HasOnStack("template") || (_context is not null && _context.IsHtml("template"));

    /// <summary>The adoption agency algorithm (§13.2.6.4.7), without the sanitizer steps.</summary>
    private void AdoptionAgency(Input t)
    {
        var subject = t.Name!;
        if (CurrentNode is { } current && current.IsHtml(subject) && !current.InFormatting)
        {
            Pop();
            return;
        }

        for (var outer = 0; outer < 8; outer++)
        {
            var formattingElement = FormattingAfterLastMarker(subject);
            if (formattingElement is null)
            {
                AnyOtherEndTag(t);
                return;
            }

            if (!formattingElement.OnStack)
            {
                Error("misnested-end-tag", t);
                RemoveFormatting(formattingElement);
                return;
            }

            if (!HasElementInScope(formattingElement))
            {
                Error("misnested-end-tag", t);
                return;
            }

            if (formattingElement != CurrentNode)
            {
                Error("misnested-end-tag", t);
            }

            var formattingIndex = _stack.IndexOf(formattingElement);
            MutableElement? furthestBlock = null;
            for (var i = formattingIndex + 1; i < _stack.Count; i++)
            {
                if (ElementTables.IsSpecial(_stack[i]))
                {
                    furthestBlock = _stack[i];
                    break;
                }
            }

            if (furthestBlock is null)
            {
                while (Pop() != formattingElement)
                {
                }

                RemoveFormatting(formattingElement);
                return;
            }

            var commonAncestor = _stack[formattingIndex - 1];
            var bookmark = FormattingIndexOf(formattingElement);
            var node = furthestBlock;
            MutableNode lastNode = furthestBlock;
            var nodeIndex = _stack.IndexOf(node);
            for (var inner = 1; ; inner++)
            {
                nodeIndex--;
                node = _stack[nodeIndex];
                if (node == formattingElement)
                {
                    break;
                }

                var inList = FormattingIndexOf(node);
                if (inner > 3 && inList >= 0)
                {
                    RemoveFormattingAt(inList);
                    if (inList < bookmark)
                    {
                        bookmark--;
                    }

                    inList = -1;
                }

                if (inList < 0)
                {
                    RemoveFromStack(node);
                    continue;
                }

                var replacement = CreateElement(node.Token, ElementNamespace.Html);
                SetFormatting(inList, replacement);
                _stack[nodeIndex] = replacement;
                replacement.OnStack = true;
                node.OnStack = false;
                FinishChildren(node);
                node = replacement;
                if (lastNode == furthestBlock)
                {
                    bookmark = inList + 1;
                }

                node.Insert(lastNode, null);
                lastNode = node;
            }

            var (target, before) = AppropriatePlace(commonAncestor);
            if (Refresh(target) && before?.Parent != target)
            {
                before = null;
            }

            target.Insert(lastNode, before);

            var newElement = CreateElement(formattingElement.Token, ElementNamespace.Html);
            foreach (var child in furthestBlock.Children())
            {
                newElement.Insert(child, null);
            }

            furthestBlock.Insert(newElement, null);

            var oldIndex = FormattingIndexOf(formattingElement);
            InsertFormatting(bookmark, newElement);
            RemoveFormattingAt(oldIndex < bookmark ? oldIndex : oldIndex + 1);

            RemoveFromStack(formattingElement);
            _stack.Insert(_stack.IndexOf(furthestBlock) + 1, newElement);
            newElement.OnStack = true;
        }
    }

    // §13.2.6.4.8
    private void InText(Input t)
    {
        switch (t.Kind)
        {
            case TokenKind.Characters:
                InsertCharacter(t.Char, t.Source);
                return;
            case TokenKind.EndOfFile:
                Error("unexpected-end-of-file", t.Source, CurrentNode!.LocalName);
                Pop();
                Reprocess(_originalMode, t);
                return;
            default:
                // An end tag: "script" and any other alike, since no script runs.
                Pop();
                _mode = _originalMode;
                return;
        }
    }

    // §13.2.6.4.9
    private void InTable(Input t)
    {
        if (t.Kind == TokenKind.Characters && CurrentNode is { Namespace: ElementNamespace.Html, LocalName: "table" or "tbody" or "template" or "tfoot" or "thead" or "tr" })
        {
            _pendingTableCharacters.Clear();
            _originalMode = _mode;
            Reprocess(InsertionMode.InTableText, t);
            return;
        }

        switch (t.Kind)
        {
            case TokenKind.Comment:
                InsertComment(t.Token!);
                return;
            case TokenKind.ProcessingInstruction:
                InsertProcessingInstruction(t.Token!);
                return;
            case TokenKind.Doctype:
                Error("unexpected-doctype", t);
                return;
            case TokenKind.EndOfFile:
                InBody(t);
                return;
        }

        if (t.IsStart("caption"))
        {
            ClearStackBackTo("table", "template", "html");
            InsertMarker();
            InsertHtmlElement(t.Token!);
            _mode = InsertionMode.InCaption;
        }
        else if (t.IsStart("colgroup"))
        {
            ClearStackBackTo("table", "template", "html");
            InsertHtmlElement(t.Token!);
            _mode = InsertionMode.InColumnGroup;
        }
        else if (t.IsStart("col"))
        {
            ClearStackBackTo("table", "template", "html");
            InsertImpliedElement("colgroup", t.Source);
            Reprocess(InsertionMode.InColumnGroup, t);
        }
        else if (t.IsStart("tbody", "tfoot", "thead"))
        {
            ClearStackBackTo("table", "template", "html");
            InsertHtmlElement(t.Token!);
            _mode = InsertionMode.InTableBody;
        }
        else if (t.IsStart("td", "th", "tr"))
        {
            ClearStackBackTo("table", "template", "html");
            InsertImpliedElement("tbody", t.Source);
            Reprocess(InsertionMode.InTableBody, t);
        }
        else if (t.IsStart("table"))
        {
            Error("unexpected-start-tag", t);
            if (!HasInScope(ElementTables.Scope.Table, "table"))
            {
                return;
            }

            PopUntilHtml("table");
            ResetInsertionMode();
            Process(t);
        }
        else if (t.IsEnd("table"))
        {
            if (!HasInScope(ElementTables.Scope.Table, "table"))
            {
                Error("unexpected-end-tag", t);
                return;
            }

            PopUntilHtml("table");
            ResetInsertionMode();
        }
        else if (t.IsEnd("body", "caption", "col", "colgroup", "html", "tbody", "td", "tfoot", "th", "thead", "tr"))
        {
            Error("unexpected-end-tag", t);
        }
        else if (t.IsStart("style", "script", "template") || t.IsEnd("template"))
        {
            InHead(t);
        }
        else if (t.IsStart("input") && t.Token!.Attribute("type") is { } type && Ascii.EqualsIgnoreCase(type, "hidden"))
        {
            Error("unexpected-start-tag", t);
            InsertHtmlElement(t.Token!);
            Pop();
            t.Token!.SelfClosingAcknowledged = true;
        }
        else if (t.IsStart("form"))
        {
            Error("unexpected-start-tag", t);
            if (_form is not null && !ParsingTemplateContents)
            {
                return;
            }

            var form = InsertHtmlElement(t.Token!);
            if (!ParsingTemplateContents)
            {
                _form = form;
            }

            Pop();
        }
        else
        {
            InTableAnythingElse(t);
        }
    }

    /// <summary>"Anything else" in table: a parse error, then the in body rules with foster parenting.</summary>
    private void InTableAnythingElse(Input t)
    {
        Error(t.Kind == TokenKind.Characters ? "unexpected-character-in-table" : (t.Kind == TokenKind.StartTag ? "unexpected-start-tag-in-table" : "unexpected-end-tag-in-table"), t);
        _fosterParenting = true;
        InBody(t);
        _fosterParenting = false;
    }

    /// <summary>"Clear the stack back to a … context": pop until the current node is one of these HTML elements.</summary>
    private void ClearStackBackTo(params string[] names)
    {
        while (CurrentNode is { } node && !(node.Namespace == ElementNamespace.Html && Array.IndexOf(names, node.LocalName) >= 0))
        {
            Pop();
        }
    }

    // §13.2.6.4.10
    private void InTableText(Input t)
    {
        if (t.Kind == TokenKind.Characters)
        {
            if (t.Char == 0)
            {
                Error("unexpected-null-character", t);
                return;
            }

            _pendingTableCharacters.Add((t.Char, t.Source));
            return;
        }

        if (_pendingTableCharacters.Exists(pending => !Ascii.IsWhitespace(pending.Character)))
        {
            foreach (var (character, source) in _pendingTableCharacters)
            {
                InTableAnythingElse(Input.Character(character, source));
            }
        }
        else
        {
            foreach (var (character, source) in _pendingTableCharacters)
            {
                InsertCharacter(character, source);
            }
        }

        _pendingTableCharacters.Clear();
        Reprocess(_originalMode, t);
    }

    // §13.2.6.4.11
    private void InCaption(Input t)
    {
        if (t.IsEnd("caption") || t.IsStart("caption", "col", "colgroup", "tbody", "td", "tfoot", "th", "thead", "tr") || t.IsEnd("table"))
        {
            if (!HasInScope(ElementTables.Scope.Table, "caption"))
            {
                Error(t.Kind == TokenKind.StartTag ? "unexpected-start-tag" : "unexpected-end-tag", t);
                return;
            }

            GenerateImpliedEndTags();
            if (!CurrentIsHtml("caption"))
            {
                Error("misnested-end-tag", t);
            }

            PopUntilHtml("caption");
            ClearFormattingToLastMarker();
            _mode = InsertionMode.InTable;
            if (!t.IsEnd("caption"))
            {
                Process(t);
            }
        }
        else if (t.IsEnd("body", "col", "colgroup", "html", "tbody", "td", "tfoot", "th", "thead", "tr"))
        {
            Error("unexpected-end-tag", t);
        }
        else
        {
            InBody(t);
        }
    }

    // §13.2.6.4.12
    private void InColumnGroup(Input t)
    {
        if (t.IsWhitespace)
        {
            InsertCharacter(t.Char, t.Source);
            return;
        }

        switch (t.Kind)
        {
            case TokenKind.Comment:
                InsertComment(t.Token!);
                return;
            case TokenKind.ProcessingInstruction:
                InsertProcessingInstruction(t.Token!);
                return;
            case TokenKind.Doctype:
                Error("unexpected-doctype", t);
                return;
            case TokenKind.EndOfFile:
                InBody(t);
                return;
        }

        if (t.IsStart("html"))
        {
            InBody(t);
        }
        else if (t.IsStart("col"))
        {
            InsertHtmlElement(t.Token!);
            Pop();
            t.Token!.SelfClosingAcknowledged = true;
        }
        else if (t.IsEnd("colgroup"))
        {
            if (!CurrentIsHtml("colgroup"))
            {
                Error("unexpected-end-tag", t);
                return;
            }

            Pop();
            _mode = InsertionMode.InTable;
        }
        else if (t.IsEnd("col"))
        {
            Error("unexpected-end-tag", t);
        }
        else if (t.IsStart("template") || t.IsEnd("template"))
        {
            InHead(t);
        }
        else
        {
            if (!CurrentIsHtml("colgroup"))
            {
                Error(t.Kind == TokenKind.Characters ? "unexpected-character" : "unexpected-tag-in-column-group", t);
                return;
            }

            Pop();
            Reprocess(InsertionMode.InTable, t);
        }
    }

    // §13.2.6.4.13
    private void InTableBody(Input t)
    {
        if (t.IsStart("tr"))
        {
            ClearStackBackTo("tbody", "tfoot", "thead", "template", "html");
            InsertHtmlElement(t.Token!);
            _mode = InsertionMode.InRow;
        }
        else if (t.IsStart("th", "td"))
        {
            Error("unexpected-start-tag", t);
            ClearStackBackTo("tbody", "tfoot", "thead", "template", "html");
            InsertImpliedElement("tr", t.Source);
            Reprocess(InsertionMode.InRow, t);
        }
        else if (t.IsEnd("tbody", "tfoot", "thead"))
        {
            if (!HasInScope(ElementTables.Scope.Table, t.Name!))
            {
                Error("unexpected-end-tag", t);
                return;
            }

            ClearStackBackTo("tbody", "tfoot", "thead", "template", "html");
            Pop();
            _mode = InsertionMode.InTable;
        }
        else if (t.IsStart("caption", "col", "colgroup", "tbody", "tfoot", "thead") || t.IsEnd("table"))
        {
            if (!HasInScope(ElementTables.Scope.Table, "tbody", "thead", "tfoot"))
            {
                Error(t.Kind == TokenKind.StartTag ? "unexpected-start-tag" : "unexpected-end-tag", t);
                return;
            }

            ClearStackBackTo("tbody", "tfoot", "thead", "template", "html");
            Pop();
            Reprocess(InsertionMode.InTable, t);
        }
        else if (t.IsEnd("body", "caption", "col", "colgroup", "html", "td", "th", "tr"))
        {
            Error("unexpected-end-tag", t);
        }
        else
        {
            InTable(t);
        }
    }

    // §13.2.6.4.14
    private void InRow(Input t)
    {
        if (t.IsStart("th", "td"))
        {
            ClearStackBackTo("tr", "template", "html");
            InsertHtmlElement(t.Token!);
            _mode = InsertionMode.InCell;
            InsertMarker();
        }
        else if (t.IsEnd("tr"))
        {
            if (!HasInScope(ElementTables.Scope.Table, "tr"))
            {
                Error("unexpected-end-tag", t);
                return;
            }

            ClearStackBackTo("tr", "template", "html");
            Pop();
            _mode = InsertionMode.InTableBody;
        }
        else if (t.IsStart("caption", "col", "colgroup", "tbody", "tfoot", "thead", "tr") || t.IsEnd("table"))
        {
            if (!HasInScope(ElementTables.Scope.Table, "tr"))
            {
                Error(t.Kind == TokenKind.StartTag ? "unexpected-start-tag" : "unexpected-end-tag", t);
                return;
            }

            ClearStackBackTo("tr", "template", "html");
            Pop();
            Reprocess(InsertionMode.InTableBody, t);
        }
        else if (t.IsEnd("tbody", "tfoot", "thead"))
        {
            if (!HasInScope(ElementTables.Scope.Table, t.Name!))
            {
                Error("unexpected-end-tag", t);
                return;
            }

            if (!HasInScope(ElementTables.Scope.Table, "tr"))
            {
                return;
            }

            ClearStackBackTo("tr", "template", "html");
            Pop();
            Reprocess(InsertionMode.InTableBody, t);
        }
        else if (t.IsEnd("body", "caption", "col", "colgroup", "html", "td", "th"))
        {
            Error("unexpected-end-tag", t);
        }
        else
        {
            InTable(t);
        }
    }

    // §13.2.6.4.15
    private void InCell(Input t)
    {
        if (t.IsEnd("td", "th"))
        {
            if (!HasInScope(ElementTables.Scope.Table, t.Name!))
            {
                Error("unexpected-end-tag", t);
                return;
            }

            GenerateImpliedEndTags();
            if (!CurrentIsHtml(t.Name!))
            {
                Error("misnested-end-tag", t);
            }

            PopUntilHtml(t.Name!);
            ClearFormattingToLastMarker();
            _mode = InsertionMode.InRow;
        }
        else if (t.IsStart("caption", "col", "colgroup", "tbody", "td", "tfoot", "th", "thead", "tr"))
        {
            if (!HasInScope(ElementTables.Scope.Table, "td", "th"))
            {
                // Only in the fragment case; the standard asserts it cannot happen otherwise.
                Error("unexpected-start-tag", t);
                return;
            }

            CloseCell();
            Process(t);
        }
        else if (t.IsEnd("body", "caption", "col", "colgroup", "html"))
        {
            Error("unexpected-end-tag", t);
        }
        else if (t.IsEnd("table", "tbody", "tfoot", "thead", "tr"))
        {
            if (!HasInScope(ElementTables.Scope.Table, t.Name!))
            {
                Error("unexpected-end-tag", t);
                return;
            }

            CloseCell();
            Process(t);
        }
        else
        {
            InBody(t);
        }
    }

    /// <summary>"Close the cell".</summary>
    private void CloseCell()
    {
        GenerateImpliedEndTags();
        if (!CurrentIsHtml("td") && !CurrentIsHtml("th"))
        {
            Error("unclosed-element", CurrentNode!.Start, CurrentNode.LocalName);
        }

        PopUntil(element => element.IsHtml("td") || element.IsHtml("th"));
        ClearFormattingToLastMarker();
        _mode = InsertionMode.InRow;
    }

    // §13.2.6.4.16
    private void InTemplate(Input t)
    {
        switch (t.Kind)
        {
            case TokenKind.Characters or TokenKind.Comment or TokenKind.ProcessingInstruction or TokenKind.Doctype:
                InBody(t);
                return;
            case TokenKind.EndOfFile:
                if (!HasOnStack("template"))
                {
                    return;
                }

                Error("unexpected-end-of-file", t.Source, "template");
                PopUntilHtml("template");
                ClearFormattingToLastMarker();
                _templateModes.RemoveAt(_templateModes.Count - 1);
                ResetInsertionMode();
                Process(t);
                return;
            case TokenKind.EndTag:
                if (t.IsEnd("template"))
                {
                    InHead(t);
                }
                else
                {
                    Error("unexpected-end-tag", t);
                }

                return;
        }

        if (t.IsStart("base", "basefont", "bgsound", "link", "meta", "noframes", "script", "style", "template", "title"))
        {
            InHead(t);
            return;
        }

        var next = t.Name switch
        {
            "caption" or "colgroup" or "tbody" or "tfoot" or "thead" => InsertionMode.InTable,
            "col" => InsertionMode.InColumnGroup,
            "tr" => InsertionMode.InTableBody,
            "td" or "th" => InsertionMode.InRow,
            _ => InsertionMode.InBody,
        };
        _templateModes[^1] = next;
        Reprocess(next, t);
    }

    // §13.2.6.4.17
    private void AfterBody(Input t)
    {
        if (t.IsWhitespace)
        {
            InBody(t);
            return;
        }

        switch (t.Kind)
        {
            case TokenKind.Comment:
                InsertComment(t.Token!, _stack[0]);
                return;
            case TokenKind.ProcessingInstruction:
                InsertProcessingInstruction(t.Token!, _stack[0]);
                return;
            case TokenKind.Doctype:
                Error("unexpected-doctype", t);
                return;
            case TokenKind.EndOfFile:
                return;
        }

        if (t.IsStart("html"))
        {
            InBody(t);
        }
        else if (t.IsEnd("html"))
        {
            if (_context is not null)
            {
                Error("unexpected-end-tag", t);
                return;
            }

            _mode = InsertionMode.AfterAfterBody;
        }
        else
        {
            Error(t.Kind == TokenKind.Characters ? "unexpected-character-after-body" : "unexpected-tag-after-body", t);
            Reprocess(InsertionMode.InBody, t);
        }
    }

    // §13.2.6.4.18
    private void InFrameset(Input t)
    {
        if (t.IsWhitespace)
        {
            InsertCharacter(t.Char, t.Source);
            return;
        }

        switch (t.Kind)
        {
            case TokenKind.Comment:
                InsertComment(t.Token!);
                return;
            case TokenKind.ProcessingInstruction:
                InsertProcessingInstruction(t.Token!);
                return;
            case TokenKind.Doctype:
                Error("unexpected-doctype", t);
                return;
            case TokenKind.EndOfFile:
                if (CurrentNode != _stack[0])
                {
                    Error("unexpected-end-of-file", t.Source, CurrentNode!.LocalName);
                }

                return;
        }

        if (t.IsStart("html"))
        {
            InBody(t);
        }
        else if (t.IsStart("frameset"))
        {
            InsertHtmlElement(t.Token!);
        }
        else if (t.IsEnd("frameset"))
        {
            if (CurrentNode == _stack[0])
            {
                Error("unexpected-end-tag", t);
                return;
            }

            Pop();
            if (_context is null && !CurrentIsHtml("frameset"))
            {
                _mode = InsertionMode.AfterFrameset;
            }
        }
        else if (t.IsStart("frame"))
        {
            InsertHtmlElement(t.Token!);
            Pop();
            t.Token!.SelfClosingAcknowledged = true;
        }
        else if (t.IsStart("noframes"))
        {
            InHead(t);
        }
        else
        {
            Error(t.Kind == TokenKind.Characters ? "unexpected-character-in-frameset" : "unexpected-tag-in-frameset", t);
        }
    }

    // §13.2.6.4.19
    private void AfterFrameset(Input t)
    {
        if (t.IsWhitespace)
        {
            InsertCharacter(t.Char, t.Source);
            return;
        }

        switch (t.Kind)
        {
            case TokenKind.Comment:
                InsertComment(t.Token!);
                return;
            case TokenKind.ProcessingInstruction:
                InsertProcessingInstruction(t.Token!);
                return;
            case TokenKind.Doctype:
                Error("unexpected-doctype", t);
                return;
            case TokenKind.EndOfFile:
                return;
        }

        if (t.IsStart("html"))
        {
            InBody(t);
        }
        else if (t.IsEnd("html"))
        {
            _mode = InsertionMode.AfterAfterFrameset;
        }
        else if (t.IsStart("noframes"))
        {
            InHead(t);
        }
        else
        {
            Error(t.Kind == TokenKind.Characters ? "unexpected-character-after-frameset" : "unexpected-tag-after-frameset", t);
        }
    }

    // §13.2.6.4.20
    private void AfterAfterBody(Input t)
    {
        switch (t.Kind)
        {
            case TokenKind.Comment:
                InsertComment(t.Token!, _document);
                return;
            case TokenKind.ProcessingInstruction:
                InsertProcessingInstruction(t.Token!, _document);
                return;
            case TokenKind.EndOfFile:
                return;
        }

        if (t.Kind == TokenKind.Doctype || t.IsWhitespace || t.IsStart("html"))
        {
            InBody(t);
            return;
        }

        Error(t.Kind == TokenKind.Characters ? "unexpected-character-after-body" : "unexpected-tag-after-body", t);
        Reprocess(InsertionMode.InBody, t);
    }

    // §13.2.6.4.21
    private void AfterAfterFrameset(Input t)
    {
        switch (t.Kind)
        {
            case TokenKind.Comment:
                InsertComment(t.Token!, _document);
                return;
            case TokenKind.ProcessingInstruction:
                InsertProcessingInstruction(t.Token!, _document);
                return;
            case TokenKind.EndOfFile:
                return;
        }

        if (t.Kind == TokenKind.Doctype || t.IsWhitespace || t.IsStart("html"))
        {
            InBody(t);
        }
        else if (t.IsStart("noframes"))
        {
            InHead(t);
        }
        else
        {
            Error(t.Kind == TokenKind.Characters ? "unexpected-character-after-frameset" : "unexpected-tag-after-frameset", t);
        }
    }
}
