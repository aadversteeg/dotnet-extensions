using System;
using System.Collections.Generic;
using System.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Html.Parsing.Tokenizing;

namespace Ave.Extensions.Documents.Html.Parsing.TreeBuilding;

/// <summary>
/// Tree construction (HTML Standard §13.2.6) with the parser's scripting flag off, no sanitizer and declarative shadow
/// roots not allowed: it pulls tokens from the tokenizer and builds the mutable tree. This file holds the parse state
/// (§13.2.4) and the algorithms the insertion modes share; the modes are in <c>TreeBuilder.Modes.cs</c>, foreign content
/// in <c>TreeBuilder.Foreign.cs</c>.
/// </summary>
/// <remarks>
/// <para>Not implemented, because nothing in a static parse reaches them: scripts (no script runs, so nothing is
/// written into the input), custom elements, the sanitizer, and declarative shadow roots (a <c>template</c> with
/// <c>shadowrootmode</c> stays a template, as for a parser whose "allow declarative shadow roots" is false, §13.2.4.5).
/// Content patching (a <c>template</c> with a <c>for</c> attribute, §13.2.6.4.4) is not implemented either: such a
/// template stays an ordinary template, as when "preparing content patching" fails.</para>
/// <para>Bounded work: the stack of open elements holds at most <see cref="HtmlInputLimits.MaxDepth"/> elements, so
/// every scope check and every search of the stack is bounded; the list of active formatting elements holds no more
/// than one entry per element on the stack plus its markers, because every formatting start tag first reopens the
/// entries that are not open (§13.2.4.3, "reconstruct").</para>
/// </remarks>
internal sealed partial class TreeBuilder
{
    private readonly Tokenizer _tokenizer;
    private readonly Action<string, int, string?> _error;
    private readonly MutableDocument _document = new();
    private readonly List<MutableElement> _stack = [];
    private readonly List<MutableElement?> _formatting = [];
    private readonly List<InsertionMode> _templateModes = [];
    private readonly List<(int Character, int Source)> _pendingTableCharacters = [];

    private InsertionMode _mode = InsertionMode.Initial;
    private InsertionMode _originalMode;
    private MutableElement? _head;
    private MutableElement? _form;

    /// <summary>Where the token being processed starts: the place of the elements it makes the parser create again
    /// (reconstruction, the adoption agency algorithm).</summary>
    private int _inputSource;
    private bool _framesetOk = true;
    private bool _fosterParenting;
    private bool _skipNextLineFeed;
    private bool _stopped;

    private long _elements;
    private long _nodes;

    /// <summary>The most elements the parser makes (<see cref="HtmlInputLimits.MaxElements"/>; smaller in tests).</summary>
    internal long MaxElements { get; init; } = HtmlInputLimits.MaxElements;

    /// <summary>The most nodes the parser makes (<see cref="HtmlInputLimits.MaxNodes"/>; smaller in tests).</summary>
    internal long MaxNodes { get; init; } = HtmlInputLimits.MaxNodes;

    /// <summary>Which limit the document passed, "elements" or "nodes", or null: parsing stopped there.</summary>
    public string? TooMany { get; private set; }

    /// <summary>Counts a node the parser makes (an element made again or cloned counts as one more); past a limit
    /// parsing stops.</summary>
    private void Made(bool element)
    {
        _nodes++;
        if (element)
        {
            _elements++;
        }

        if (TooMany is null && (_elements > MaxElements || _nodes > MaxNodes))
        {
            TooMany = _elements > MaxElements ? "elements" : "nodes";
            _stopped = true;
        }
    }

    /// <summary>Set when the end of the file closed a template and must be processed again (§13.2.6.4.16).</summary>
    private bool _endOfFileAgain;
    private MutableElement? _context;

    /// <summary>Creates a tree builder for a whole document.</summary>
    /// <param name="tokenizer">The tokenizer to pull tokens from.</param>
    /// <param name="error">Receives every tree construction parse error: its name, the source index and a detail (the
    /// tag name), or null.</param>
    public TreeBuilder(Tokenizer tokenizer, Action<string, int, string?> error)
    {
        _tokenizer = tokenizer;
        _error = error;
        _tokenizer.InForeignContent = () => AdjustedCurrentNode is { Namespace: not ElementNamespace.Html };
    }

    /// <summary>The element at which the parse stopped because the stack grew past <see cref="HtmlInputLimits.MaxDepth"/>,
    /// or null.</summary>
    public MutableElement? TooDeepAt { get; private set; }

    /// <summary>Runs the parser to the end of the input and returns the document.</summary>
    /// <returns>The document.</returns>
    public MutableDocument ParseDocument()
    {
        Run();
        return _document;
    }

    /// <summary>
    /// The HTML fragment parsing algorithm (§13.4) for a context element: the tokenizer state comes from the context,
    /// the stack starts with an <c>html</c> root, and the result is the root's children.
    /// </summary>
    /// <param name="contextNamespace">The context element's namespace.</param>
    /// <param name="contextName">Its local name.</param>
    /// <param name="mode">The document mode of the context element's document.</param>
    /// <returns>The fragment.</returns>
    public MutableFragment ParseFragment(ElementNamespace contextNamespace, string contextName, DocumentMode mode)
    {
        _document.Mode = mode;
        var contextToken = new Token { Kind = TokenKind.StartTag, Name = contextName };
        _context = new MutableElement { Namespace = contextNamespace, LocalName = contextName, Token = contextToken };
        if (contextNamespace == ElementNamespace.Html)
        {
            _tokenizer.State = contextName switch
            {
                "title" or "textarea" => TokenizerState.Rcdata,
                "style" or "xmp" or "iframe" or "noembed" or "noframes" => TokenizerState.Rawtext,
                "script" => TokenizerState.ScriptData,
                "plaintext" => TokenizerState.Plaintext,
                _ => TokenizerState.Data,
            };
        }

        var root = new MutableElement { Namespace = ElementNamespace.Html, LocalName = "html", Token = new Token { Kind = TokenKind.StartTag, Name = "html" } };
        _document.Insert(root, null);
        Push(root);
        if (_context.IsHtml("template"))
        {
            _templateModes.Add(InsertionMode.InTemplate);
        }

        ResetInsertionMode();
        // §13.4 step 13: the form element pointer is the nearest form at or above the context element; the context has
        // no ancestors here, so it is the context itself when that is a form (review 39 HD-8).
        if (_context.IsHtml("form"))
        {
            _form = _context;
        }

        Run();

        var fragment = new MutableFragment();
        foreach (var child in root.Children())
        {
            fragment.Insert(child, null);
        }

        return fragment;
    }

    private void Run()
    {
        while (!_stopped)
        {
            var token = _tokenizer.Next();
            if (token.Kind == TokenKind.Characters)
            {
                ProcessCharacters(token);
            }
            else
            {
                _skipNextLineFeed = false;
                Process(Input.Of(token));
                while (_endOfFileAgain)
                {
                    _endOfFileAgain = false;
                    Process(Input.Of(token));
                }

                if (token is { Kind: TokenKind.StartTag, SelfClosing: true, SelfClosingAcknowledged: false })
                {
                    Error("non-void-html-element-start-tag-with-trailing-solidus", token.Start, token.Name);
                }

                if (token.Kind == TokenKind.EndOfFile)
                {
                    _stopped = true;
                }
            }
        }

        // "The end" (§13.2.7): pop all the nodes off the stack of open elements (each finishes parsing its children).
        if (TooDeepAt is null && !CloneBudgetExceeded && TooMany is null)
        {
            while (_stack.Count > 0)
            {
                Pop();
            }

            FlushSelects();
        }
    }

    private void ProcessCharacters(Token token)
    {
        var data = token.Data;
        for (var offset = 0; offset < data.Length && !_stopped;)
        {
            int c = data[offset];
            var length = 1;
            if (char.IsHighSurrogate(data[offset]) && offset + 1 < data.Length && char.IsLowSurrogate(data[offset + 1]))
            {
                c = char.ConvertToUtf32(data[offset], data[offset + 1]);
                length = 2;
            }

            var source = token.SourceIndexOf(offset);
            offset += length;
            // A NUL of the data state is dropped before the content rules see it (see Process), so the line feed after
            // it is still the first character after <pre> or <listing> (Chromium 159, measured: <pre>\0\ny is "y").
            if (_skipNextLineFeed && c != 0)
            {
                _skipNextLineFeed = false;
                if (c == '\n')
                {
                    continue;
                }
            }

            Process(Input.Character(c, source));
        }
    }

    // ---- the dispatcher (§13.2.6) ----

    /// <summary>The tree construction dispatcher: HTML content rules or foreign content rules.</summary>
    private void Process(Input t)
    {
        if (_stopped)
        {
            return;
        }

        _inputSource = t.Source;
        var node = AdjustedCurrentNode;
        if (node is null
            || node.Namespace == ElementNamespace.Html
            || (ElementTables.IsMathMlTextIntegrationPoint(node) && t.Kind == TokenKind.StartTag && t.Name is not ("mglyph" or "malignmark"))
            || (ElementTables.IsMathMlTextIntegrationPoint(node) && t.Kind == TokenKind.Characters)
            || (node.Is(ElementNamespace.MathMl, "annotation-xml") && t.IsStart("svg"))
            || (ElementTables.IsHtmlIntegrationPoint(node) && t.Kind is TokenKind.StartTag or TokenKind.Characters)
            || t.Kind == TokenKind.EndOfFile)
        {
            // Chromium 159 drops a U+0000 of the data state before the HTML content rules see it (the tokenizer has
            // reported it), so it never counts as text: a NUL before the doctype keeps the doctype and the document's
            // mode (review 39 HD-1, measured). Every mode of the standard that is not before or after the body ignores it
            // anyway; the others would take it as text. Other states make U+FFFD of it, and foreign content keeps its rule.
            if (t.Kind == TokenKind.Characters && t.Char == 0)
            {
                return;
            }

            ProcessIn(_mode, t);
        }
        else
        {
            ForeignContent(t);
        }
    }

    // ---- the stack of open elements (§13.2.4.2) ----

    /// <summary>The current node: the bottommost element of the stack, or null when it is empty.</summary>
    private MutableElement? CurrentNode => _stack.Count == 0 ? null : _stack[^1];

    /// <summary>The adjusted current node: the context element when the stack holds only the root (fragment case),
    /// otherwise the current node.</summary>
    private MutableElement? AdjustedCurrentNode => _context is not null && _stack.Count == 1 ? _context : CurrentNode;

    private void Push(MutableElement element)
    {
        _stack.Add(element);
        element.OnStack = true;
        if (_stack.Count > HtmlInputLimits.MaxDepth && TooDeepAt is null)
        {
            TooDeepAt = element;
            _stopped = true;
        }
    }

    private MutableElement Pop()
    {
        var element = _stack[^1];
        _stack.RemoveAt(_stack.Count - 1);
        element.OnStack = false;
        FinishChildren(element);
        return element;
    }

    /// <summary>"Remove an element from the stack of open elements" given an element anywhere in it.</summary>
    private void RemoveFromStack(MutableElement element)
    {
        var index = _stack.LastIndexOf(element);
        if (index >= 0)
        {
            _stack.RemoveAt(index);
            element.OnStack = false;
            FinishChildren(element);
        }
    }

    /// <summary>Pops until an element that matches has been popped.</summary>
    private void PopUntil(Func<MutableElement, bool> match)
    {
        while (_stack.Count > 0)
        {
            if (match(Pop()))
            {
                return;
            }
        }
    }

    private void PopUntilHtml(string localName) => PopUntil(element => element.IsHtml(localName));

    /// <summary>"Has an element in scope" for an HTML element with one of these names (§13.2.4.2).</summary>
    private bool HasInScope(ElementTables.Scope scope, params string[] localNames)
    {
        for (var i = _stack.Count - 1; i >= 0; i--)
        {
            var node = _stack[i];
            if (node.Namespace == ElementNamespace.Html && Array.IndexOf(localNames, node.LocalName) >= 0)
            {
                return true;
            }

            if (ElementTables.EndsScope(node, scope))
            {
                return false;
            }
        }

        return false;
    }

    /// <summary>"Has an element in scope" for one particular element.</summary>
    private bool HasElementInScope(MutableElement target)
    {
        for (var i = _stack.Count - 1; i >= 0; i--)
        {
            var node = _stack[i];
            if (node == target)
            {
                return true;
            }

            if (ElementTables.EndsScope(node, ElementTables.Scope.Default))
            {
                return false;
            }
        }

        return false;
    }

    private bool HasOnStack(string htmlName)
    {
        for (var i = _stack.Count - 1; i >= 0; i--)
        {
            if (_stack[i].IsHtml(htmlName))
            {
                return true;
            }
        }

        return false;
    }

    private bool CurrentIsHtml(string localName) => CurrentNode is { } node && node.IsHtml(localName);

    // ---- implied end tags (§13.2.6.3) ----

    private void GenerateImpliedEndTags(string? except = null)
    {
        while (CurrentNode is { Namespace: ElementNamespace.Html } node && ElementTables.ImpliedEndTags.Contains(node.LocalName) && node.LocalName != except)
        {
            Pop();
        }
    }

    private void GenerateAllImpliedEndTagsThoroughly()
    {
        while (CurrentNode is { Namespace: ElementNamespace.Html } node && ElementTables.ImpliedEndTagsThoroughly.Contains(node.LocalName))
        {
            Pop();
        }
    }

    // ---- the list of active formatting elements (§13.2.4.3); null is a marker ----

    private void InsertMarker() => _formatting.Add(null);

    // The list is changed through these, so each element knows whether it is on the list (MutableElement.InFormatting)
    // and a membership test is constant work, however many markers the list holds (markers can stay on it for ever,
    // as the standard says). The elements looked up by position are after the last marker, searched from the end.

    private void AddFormatting(MutableElement element)
    {
        element.InFormatting = true;
        _formatting.Add(element);
    }

    private void RemoveFormattingAt(int index)
    {
        if (_formatting[index] is { } entry)
        {
            entry.InFormatting = false;
        }

        _formatting.RemoveAt(index);
    }

    private void SetFormatting(int index, MutableElement element)
    {
        if (_formatting[index] is { } old)
        {
            old.InFormatting = false;
        }

        element.InFormatting = true;
        _formatting[index] = element;
    }

    private void InsertFormatting(int index, MutableElement element)
    {
        element.InFormatting = true;
        _formatting.Insert(index, element);
    }

    /// <summary>The index of an element on the list, searched from the end; -1 when it is not on it.</summary>
    private int FormattingIndexOf(MutableElement element)
    {
        if (!element.InFormatting)
        {
            return -1;
        }

        for (var i = _formatting.Count - 1; i >= 0; i--)
        {
            if (_formatting[i] == element)
            {
                return i;
            }
        }

        return -1;
    }

    private void RemoveFormatting(MutableElement element)
    {
        if (FormattingIndexOf(element) is var index and >= 0)
        {
            RemoveFormattingAt(index);
        }
    }

    /// <summary>"Push onto the list of active formatting elements", with the Noah's Ark clause. Chromium 159 leaves an
    /// element with an <c>is</c> attribute (a customized built-in element) off the list, so it is never reconstructed
    /// nor found by the adoption agency algorithm (review 39 HD-5, measured).</summary>
    private void PushFormatting(MutableElement element)
    {
        if (element.Token.HasAttribute("is"))
        {
            return;
        }

        var same = 0;
        var earliest = -1;
        for (var i = _formatting.Count - 1; i >= 0 && _formatting[i] is { } entry; i--)
        {
            if (entry.Namespace == element.Namespace && entry.LocalName == element.LocalName && SameAttributes(entry.Token, element.Token))
            {
                same++;
                earliest = i;
            }
        }

        if (same >= 3)
        {
            RemoveFormattingAt(earliest);
        }

        AddFormatting(element);
    }

    private static bool SameAttributes(Token left, Token right)
    {
        if (left.Attributes.Count != right.Attributes.Count)
        {
            return false;
        }

        foreach (var attribute in left.Attributes)
        {
            if (right.Attribute(attribute.Name) != attribute.Value)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>"Reconstruct the active formatting elements" (§13.2.4.3).</summary>
    private void ReconstructFormatting()
    {
        if (_formatting.Count == 0 || _formatting[^1] is null or { OnStack: true })
        {
            return;
        }

        var index = _formatting.Count - 1;
        while (index > 0 && _formatting[index - 1] is { OnStack: false })
        {
            index--;
        }

        // A reopened element is placed where the token that reopened it is (HtmlNode: "the position of the token that
        // made the parser insert it"), not at the tag it repeats (review 39 HR-P1).
        for (; index < _formatting.Count; index++)
        {
            var entry = _formatting[index]!;
            SetFormatting(index, InsertForeignElement(entry.Token, ElementNamespace.Html, null, _inputSource));
        }
    }

    private void ClearFormattingToLastMarker()
    {
        while (_formatting.Count > 0)
        {
            var entry = _formatting[^1];
            RemoveFormattingAt(_formatting.Count - 1);
            if (entry is null)
            {
                return;
            }
        }
    }

    /// <summary>The last formatting element with this name after the last marker, or null.</summary>
    private MutableElement? FormattingAfterLastMarker(string localName)
    {
        for (var i = _formatting.Count - 1; i >= 0 && _formatting[i] is { } entry; i--)
        {
            if (entry.LocalName == localName)
            {
                return entry;
            }
        }

        return null;
    }

    // ---- creating and inserting nodes (§13.2.6.1) ----

    /// <summary>"The appropriate place for inserting a node", with foster parenting and template contents.</summary>
    private (MutableParent Parent, MutableNode? Before) AppropriatePlace(MutableParent? overrideTarget = null)
    {
        MutableParent target = overrideTarget ?? CurrentNode!;
        MutableNode? before = null;
        if (_fosterParenting && target is MutableElement { Namespace: ElementNamespace.Html, LocalName: "table" or "tbody" or "tfoot" or "thead" or "tr" })
        {
            var lastTemplate = _stack.FindLastIndex(element => element.IsHtml("template"));
            var lastTable = _stack.FindLastIndex(element => element.IsHtml("table"));
            if (lastTemplate >= 0 && lastTemplate > lastTable)
            {
                target = _stack[lastTemplate];
            }
            else if (lastTable < 0)
            {
                return (_stack[0], null);
            }
            else if (_stack[lastTable].Parent is { } tableParent)
            {
                target = tableParent;
                before = _stack[lastTable];
            }
            else
            {
                target = _stack[lastTable - 1];
            }
        }

        return target is MutableElement { Content: { } content } ? (content, null) : (target, before);
    }

    /// <summary>"Create an element for a token" in a namespace, with the attributes as the token has them (adjusted
    /// for foreign content where the caller adjusted the token).</summary>
    private MutableElement CreateElement(Token token, ElementNamespace ns, string? localName = null, int start = -1)
    {
        var name = localName ?? token.Name!;
        start = start >= 0 ? start : token.Start;
        var element = new MutableElement
        {
            Namespace = ns,
            LocalName = name,
            Token = token,
            Start = start,
            Content = ns == ElementNamespace.Html && name == "template" ? new MutableFragment { Start = start } : null,
        };
        Made(element: true);
        if (ns == ElementNamespace.Html && token.MadeElement is { } first)
        {
            // Made again for the same token (a reopened formatting element, the adoption agency algorithm): the
            // attributes are shared, not copied.
            element.ShareAttributesOf(first);
        }
        else
        {
            foreach (var attribute in token.Attributes)
            {
                element.AddAttribute(ForeignAttribute(attribute, ns));
            }

            if (ns == ElementNamespace.Html)
            {
                token.MadeElement = element;
            }
        }

        if (ns != ElementNamespace.Html)
        {
            CheckXmlnsAttributes(element);
        }

        return element;
    }

    /// <summary>The attribute of an element: "adjust foreign attributes" (§13.2.6.1) applies to foreign elements only;
    /// the SVG and MathML case adjustments were made on the token by the caller.</summary>
    private HtmlAttribute ForeignAttribute(TokenAttribute attribute, ElementNamespace ns)
    {
        var position = Position(attribute.Start);
        if (ns != ElementNamespace.Html && ElementTables.ForeignAttributes.TryGetValue(attribute.Name, out var adjusted))
        {
            return new HtmlAttribute(adjusted.Namespace, adjusted.Prefix, adjusted.LocalName, attribute.Value, position) { ValueAnchors = Anchors(attribute) };
        }

        return Attribute(attribute);
    }

    /// <summary>An attribute in no namespace, with the places of its value's characters.</summary>
    private HtmlAttribute Attribute(TokenAttribute attribute) =>
        new(AttributeNamespace.None, null, attribute.Name, attribute.Value, Position(attribute.Start)) { ValueAnchors = Anchors(attribute) };

    private ValueList<HtmlValueAnchor> Anchors(TokenAttribute attribute) =>
        attribute.Anchors.Length == 0 ? [] : [.. attribute.Anchors.Select(anchor => new HtmlValueAnchor(anchor.Offset, Position(anchor.Source), anchor.Contiguous))];

    /// <summary>§13.2.6.1, "create an element for a token": an xmlns attribute whose value is not the element's
    /// namespace, or an xmlns:xlink that is not the XLink namespace, is a parse error.</summary>
    private void CheckXmlnsAttributes(MutableElement element)
    {
        var expected = element.Namespace == ElementNamespace.Svg ? "http://www.w3.org/2000/svg" : "http://www.w3.org/1998/Math/MathML";
        foreach (var attribute in element.Attributes)
        {
            if (attribute.Namespace != AttributeNamespace.Xmlns)
            {
                continue;
            }

            if ((attribute.LocalName == "xmlns" && attribute.Value != expected)
                || (attribute.LocalName == "xlink" && attribute.Value != "http://www.w3.org/1999/xlink"))
            {
                Error("invalid-xmlns-attribute", element.Start, element.LocalName);
            }
        }
    }

    /// <summary>"Insert an element at the adjusted insertion location" (HTML Standard §13.2.6.1), or at
    /// <paramref name="place"/> when the caller has found it already.</summary>
    private void InsertAtPlace(MutableElement element, (MutableParent Parent, MutableNode? Before)? place = null)
    {
        var (parent, before) = place ?? AppropriatePlace();
        if (parent is MutableDocument document && document.HasElementChild())
        {
            return;
        }

        if (Refresh(parent) && before?.Parent != parent)
        {
            before = null;
        }

        parent.Insert(element, before);
        ElementInserted(element);
    }

    /// <summary>"Insert a foreign element" (and "insert an HTML element"): create, insert, push.</summary>
    private MutableElement InsertForeignElement(Token token, ElementNamespace ns, string? localName = null, int start = -1)
    {
        var element = CreateElement(token, ns, localName, start);
        InsertAtPlace(element);
        Push(element);
        return element;
    }

    private MutableElement InsertHtmlElement(Token token) => InsertForeignElement(token, ElementNamespace.Html);

    /// <summary>Inserts an HTML element for a start tag the parser makes up ("a 'head' start tag token with no
    /// attributes"), at the position of the token that made it necessary.</summary>
    private MutableElement InsertImpliedElement(string localName, int position) =>
        InsertHtmlElement(new Token { Kind = TokenKind.StartTag, Name = localName, Start = position });

    /// <summary>"Insert a character": appends to the text node just before the insertion location, or makes one.</summary>
    private void InsertCharacter(int c, int source)
    {
        var (parent, before) = AppropriatePlace();
        if (parent is MutableDocument)
        {
            return;
        }

        if (Refresh(parent) && before?.Parent != parent)
        {
            before = null;
        }

        var previous = before is null ? parent.Last : before.Previous;
        if (previous is not MutableText text)
        {
            text = new MutableText { Start = source };
            Made(element: false);
            parent.Insert(text, before);
        }

        if (c > 0xFFFF)
        {
            text.Data.Append(char.ConvertFromUtf32(c));
        }
        else
        {
            text.Data.Append((char)c);
        }
    }

    /// <summary>"Insert a comment", at the appropriate place or at the place given.</summary>
    private void InsertComment(Token token, MutableParent? parent = null)
    {
        var (target, before) = parent is null ? AppropriatePlace() : (parent, null);
        if (Refresh(target) && before?.Parent != target)
        {
            before = null;
        }

        Made(element: false);
        target.Insert(new MutableComment { Data = token.Data, Start = token.Start }, before);
    }

    /// <summary>"Insert a processing instruction", at the appropriate place or at the place given.</summary>
    private void InsertProcessingInstruction(Token token, MutableParent? parent = null)
    {
        var (target, before) = parent is null ? AppropriatePlace() : (parent, null);
        if (Refresh(target) && before?.Parent != target)
        {
            before = null;
        }

        Made(element: false);
        target.Insert(new MutableProcessingInstruction { Target = token.Name!, Data = token.Data, Start = token.Start }, before);
    }

    // ---- reset the insertion mode appropriately (§13.2.4.1) ----

    private void ResetInsertionMode()
    {
        for (var i = _stack.Count - 1; i >= 0; i--)
        {
            var last = i == 0;
            var node = last && _context is not null ? _context : _stack[i];
            if (node.Namespace != ElementNamespace.Html)
            {
                if (last)
                {
                    _mode = InsertionMode.InBody;
                    return;
                }

                continue;
            }

            switch (node.LocalName)
            {
                case "td" or "th" when !last:
                    _mode = InsertionMode.InCell;
                    return;
                case "tr":
                    _mode = InsertionMode.InRow;
                    return;
                case "tbody" or "thead" or "tfoot":
                    _mode = InsertionMode.InTableBody;
                    return;
                case "caption":
                    _mode = InsertionMode.InCaption;
                    return;
                case "colgroup":
                    _mode = InsertionMode.InColumnGroup;
                    return;
                case "table":
                    _mode = InsertionMode.InTable;
                    return;
                case "template":
                    _mode = _templateModes[^1];
                    return;
                case "head" when !last:
                    _mode = InsertionMode.InHead;
                    return;
                case "body":
                    _mode = InsertionMode.InBody;
                    return;
                case "frameset":
                    _mode = InsertionMode.InFrameset;
                    return;
                case "html":
                    _mode = _head is null ? InsertionMode.BeforeHead : InsertionMode.AfterHead;
                    return;
            }

            if (last)
            {
                _mode = InsertionMode.InBody;
                return;
            }
        }
    }

    // ---- errors and positions ----

    private void Error(string name, int source, string? detail = null) => _error(name, source, detail);

    private void Error(string name, Input t) => _error(name, t.Source, t.Name);

    /// <summary>Converts a source index to a position; set by the parser, which owns the source text.</summary>
    public Func<int, Core.TextPosition> Position { get; init; } = static _ => Core.TextPosition.Start;
}
