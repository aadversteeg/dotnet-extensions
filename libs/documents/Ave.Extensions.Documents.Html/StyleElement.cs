using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Css.Selectors;
using Ave.Extensions.Documents.Html.Parsing;

namespace Ave.Extensions.Documents.Html;

/// <summary>
/// An element of the parsed tree as the selector matcher sees it (<see cref="IStyleElement"/>): its parent and
/// siblings among the element children, its position among them, and the HTML Standard's rules for comparing names and
/// attribute values (§4.16.2, case-sensitivity of selectors). Made for a whole document at once by
/// <see cref="Tree"/>.
/// </summary>
internal sealed class StyleElement : IStyleElement
{
    /// <summary>The attributes whose values selectors compare ASCII case-insensitively in HTML documents (HTML Standard
    /// §4.16.2).</summary>
    private static readonly FrozenSet<string> CaseInsensitiveAttributes = FrozenSet.Create(
        StringComparer.Ordinal,
        "accept", "accept-charset", "align", "alink", "axis", "bgcolor", "charset", "checked", "clear", "codetype", "color",
        "compact", "declare", "defer", "dir", "direction", "disabled", "enctype", "face", "frame", "hreflang", "http-equiv",
        "lang", "language", "link", "media", "method", "multiple", "nohref", "noresize", "noshade", "nowrap", "readonly",
        "rel", "rev", "rules", "scope", "scrolling", "selected", "shape", "target", "text", "type", "valign", "valuetype",
        "vlink");

    private bool? _isEmpty;

    private StyleElement(HtmlElement element, StyleElement? parent, bool quirks, AttributeTable attributes)
    {
        Element = element;
        Parent = parent;
        IsQuirksMode = quirks;
        Attributes = attributes;
    }

    /// <summary>The element's attributes looked up by name, shared with every element that has the same attributes (the
    /// parser shares them between the elements it makes for one token).</summary>
    public AttributeTable Attributes { get; }

    /// <summary>The parsed element.</summary>
    public HtmlElement Element { get; }

    /// <summary>The element children, in order.</summary>
    public List<StyleElement> Children { get; } = [];

    /// <inheritdoc/>
    public string LocalName => Element.LocalName;

    /// <inheritdoc/>
    public bool IsHtml => Element.Namespace == ElementNamespace.Html;

    /// <inheritdoc/>
    public bool IsQuirksMode { get; }

    /// <inheritdoc/>
    public string? Id => GetAttribute("id");

    /// <inheritdoc/>
    public ValueList<string> Classes => Attributes.Classes;

    /// <inheritdoc/>
    public bool HasClass(string name) => Attributes.HasClass(name, IsQuirksMode);

    /// <inheritdoc/>
    public bool AttributeHasWord(string localName, string word, bool ignoreCase) => Attributes.HasWord(localName, word, ignoreCase);

    /// <inheritdoc/>
    public string? GetAttribute(string localName) => Attributes.Find(localName)?.Value;

    /// <inheritdoc/>
    public bool IsCaseInsensitiveAttributeValue(string localName) => CaseInsensitiveAttributes.Contains(localName);

    /// <inheritdoc cref="IStyleElement.Parent"/>
    public StyleElement? Parent { get; }

    IStyleElement? IStyleElement.Parent => Parent;

    /// <inheritdoc/>
    public IStyleElement? PreviousSibling => Index > 1 ? Parent!.Children[Index - 2] : null;

    /// <inheritdoc/>
    public IStyleElement? NextSibling => Parent is { } parent && Index < parent.Children.Count ? parent.Children[Index] : null;

    /// <inheritdoc/>
    public int Index { get; private set; } = 1;

    /// <inheritdoc/>
    public int SiblingCount => Parent?.Children.Count ?? 1;

    /// <inheritdoc/>
    public int IndexOfType { get; private set; } = 1;

    /// <inheritdoc/>
    public int CountOfType { get; private set; } = 1;

    /// <inheritdoc/>
    public bool IsRoot => Parent is null;

    /// <summary>Selectors 4 §13.2: no element children and no text, comments and processing instructions apart
    /// (white space is text, as in Chromium).</summary>
    /// <remarks>Found once per element, so many <c>:empty</c> rules against an element with many children cost their sum.</remarks>
    public bool IsEmpty => _isEmpty ??= Element.Children.All(child => child is HtmlComment or HtmlProcessingInstruction || (child is HtmlText text && text.Data.Length == 0));

    /// <summary>HTML Standard §4.16.3: a and area elements with an href attribute (nothing is visited in a new
    /// document).</summary>
    public bool IsLink => IsHtml && Element.LocalName is "a" or "area" && GetAttribute("href") is not null;

    /// <summary>The tree of a document: its root element and every element below it (template contents are a
    /// separate fragment, not children, HTML Standard §4.12.3).</summary>
    /// <param name="document">The document.</param>
    /// <returns>The root.</returns>
    public static StyleElement Tree(HtmlDocument document)
    {
        var quirks = document.Mode == DocumentMode.Quirks;
        var tables = new Dictionary<HtmlAttribute[], AttributeTable>(ReferenceEqualityComparer.Instance);
        AttributeTable TableOf(HtmlElement element)
        {
            var array = System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsArray(element.Attributes.Items) ?? [];
            if (!tables.TryGetValue(array, out var table))
            {
                table = new AttributeTable(element.Attributes);
                tables[array] = table;
            }

            table.Uses++;
            return table;
        }

        var root = new StyleElement(document.DocumentElement!, null, quirks, TableOf(document.DocumentElement!));
        var pending = new Stack<StyleElement>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var element = pending.Pop();
            foreach (var child in element.Element.Children.OfType<HtmlElement>())
            {
                element.Children.Add(new StyleElement(child, element, quirks, TableOf(child)));
            }

            var types = new Dictionary<(ElementNamespace, string), int>();
            for (var i = 0; i < element.Children.Count; i++)
            {
                var child = element.Children[i];
                var key = (child.Element.Namespace, child.LocalName);
                types[key] = types.GetValueOrDefault(key) + 1;
                child.Index = i + 1;
                child.IndexOfType = types[key];
                pending.Push(child);
            }

            foreach (var child in element.Children)
            {
                child.CountOfType = types[(child.Element.Namespace, child.LocalName)];
            }
        }

        return root;
    }
}

/// <summary>The presentational hints of an element (<see cref="PresentationalHints.For"/>), with what they were made
/// from besides the attributes: the element's namespace and name.</summary>
/// <param name="Namespace">The element's namespace.</param>
/// <param name="LocalName">The element's local name.</param>
/// <param name="Declarations">The hint declarations.</param>
/// <param name="Refused">The attributes refused.</param>
internal sealed record SharedHints(ElementNamespace Namespace, string LocalName, ValueList<Css.Syntax.Declaration> Declarations, List<ErrorPaths.Error> Refused);

/// <summary>
/// The attributes of one or more elements, looked up by name in constant time, with what is read from them once: the
/// classes, the words of a value, the event handler attributes, xml:lang. The parser shares one list of attributes
/// between the elements it makes for one token (a formatting element it reopens many times), so a table is made once
/// per list, and an element costs nothing more for its attributes, however many it has.
/// </summary>
internal sealed class AttributeTable
{
    private readonly ValueList<HtmlAttribute> _attributes;
    private Dictionary<string, HtmlAttribute>? _byName;
    private ValueList<string>? _classes;
    private HashSet<string>? _classSet;
    private HashSet<string>? _lowerClassSet;
    private Dictionary<(string Name, bool IgnoreCase), HashSet<string>>? _words;
    private List<HtmlAttribute>? _eventHandlers;

    /// <summary>A table for a list of attributes.</summary>
    public AttributeTable(ValueList<HtmlAttribute> attributes) => _attributes = attributes;

    /// <summary>The attributes, in order.</summary>
    public ValueList<HtmlAttribute> All => _attributes;

    /// <summary>How many elements have these attributes (more than one: the parser made elements again for one token).</summary>
    public int Uses { get; set; }

    /// <summary>The declarations of the style attribute, kept when several elements share it (read once for all of
    /// them); null until read.</summary>
    public ValueList<Css.Syntax.Declaration>? StyleDeclarations { get; set; }

    /// <summary>The presentational hints made for an element with these attributes, kept when several elements share
    /// them (made once for all of them); null until made.</summary>
    public SharedHints? Hints { get; set; }

    /// <summary>The attribute in no namespace with this local name (the first, as the parser keeps it), or null.</summary>
    public HtmlAttribute? Find(string localName)
    {
        if (_attributes.Count <= 8)
        {
            foreach (var attribute in _attributes)
            {
                if (attribute.Namespace == AttributeNamespace.None && attribute.LocalName == localName)
                {
                    return attribute;
                }
            }

            return null;
        }

        if (_byName is null)
        {
            _byName = new Dictionary<string, HtmlAttribute>(_attributes.Count, StringComparer.Ordinal);
            foreach (var attribute in _attributes)
            {
                if (attribute.Namespace == AttributeNamespace.None)
                {
                    _byName.TryAdd(attribute.LocalName, attribute);
                }
            }
        }

        return _byName.GetValueOrDefault(localName);
    }

    /// <summary>The classes of the class attribute.</summary>
    public ValueList<string> Classes => _classes ??= Find("class") is { } value
        ? [.. value.Value.Split([' ', '\t', '\n', '\f', '\r'], StringSplitOptions.RemoveEmptyEntries)]
        : [];

    /// <summary>Whether a class is among the classes (ASCII case-insensitively in quirks mode, HTML Standard §4.16.2):
    /// a set made once, so many classes against many class selectors cost their sum.</summary>
    public bool HasClass(string name, bool quirks)
    {
        if (quirks)
        {
            _lowerClassSet ??= [.. Classes.Select(InfraAscii.ToLower)];
            return _lowerClassSet.Contains(InfraAscii.ToLower(name));
        }

        _classSet ??= [.. Classes];
        return _classSet.Contains(name);
    }

    /// <summary>Whether a word is among the white-space-separated words of an attribute's value: split once per
    /// attribute and case.</summary>
    public bool HasWord(string localName, string word, bool ignoreCase)
    {
        _words ??= [];
        if (!_words.TryGetValue((localName, ignoreCase), out var words))
        {
            var value = Find(localName)?.Value ?? string.Empty;
            words = [.. value.Split([' ', '\t', '\n', '\f', '\r'], StringSplitOptions.RemoveEmptyEntries).Select(item => ignoreCase ? InfraAscii.ToLower(item) : item)];
            _words[(localName, ignoreCase)] = words;
        }

        return words.Contains(ignoreCase ? InfraAscii.ToLower(word) : word);
    }

    /// <summary>The event handler attributes (on…, in no namespace), in order.</summary>
    public IReadOnlyList<HtmlAttribute> EventHandlers => _eventHandlers ??= [.. _attributes.Where(attribute =>
        attribute.Namespace == AttributeNamespace.None && attribute.LocalName.Length > 2 && attribute.LocalName.StartsWith("on", StringComparison.Ordinal))];

    /// <summary>The value of xml:lang (in the XML namespace), or null.</summary>
    public string? XmlLang
    {
        get
        {
            foreach (var attribute in XmlAttributes)
            {
                if (attribute.LocalName == "lang")
                {
                    return attribute.Value;
                }
            }

            return null;
        }
    }

    private List<HtmlAttribute>? _xml;

    private List<HtmlAttribute> XmlAttributes => _xml ??= [.. _attributes.Where(attribute => attribute.Namespace == AttributeNamespace.Xml)];
}
