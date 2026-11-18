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

    private readonly ValueList<string> _classes;
    private HashSet<string>? _classSet;
    private Dictionary<(string Name, bool IgnoreCase), HashSet<string>>? _words;

    private StyleElement(HtmlElement element, StyleElement? parent, bool quirks)
    {
        Element = element;
        Parent = parent;
        IsQuirksMode = quirks;
        _classes = element.Attribute("class") is { } value
            ? [.. value.Split([' ', '\t', '\n', '\f', '\r'], StringSplitOptions.RemoveEmptyEntries)]
            : [];
    }

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
    public string? Id => Element.Attribute("id");

    /// <inheritdoc/>
    public ValueList<string> Classes => _classes;

    /// <inheritdoc/>
    public bool HasClass(string name)
    {
        // A set built once per element: an element with many classes against many class selectors costs their sum.
        _classSet ??= IsQuirksMode
            ? [.. _classes.Select(Ascii.ToLower)]
            : [.. _classes];
        return _classSet.Contains(IsQuirksMode ? Ascii.ToLower(name) : name);
    }

    /// <inheritdoc/>
    public bool AttributeHasWord(string localName, string word, bool ignoreCase)
    {
        _words ??= [];
        if (!_words.TryGetValue((localName, ignoreCase), out var words))
        {
            var value = Element.Attribute(localName) ?? string.Empty;
            words = [.. value.Split([' ', '\t', '\n', '\f', '\r'], StringSplitOptions.RemoveEmptyEntries).Select(item => ignoreCase ? Ascii.ToLower(item) : item)];
            _words[(localName, ignoreCase)] = words;
        }

        return words.Contains(ignoreCase ? Ascii.ToLower(word) : word);
    }

    /// <inheritdoc/>
    public string? GetAttribute(string localName) => Element.Attribute(localName);

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
    public bool IsEmpty => Element.Children.All(child => child is HtmlComment or HtmlProcessingInstruction || (child is HtmlText text && text.Data.Length == 0));

    /// <summary>HTML Standard §4.16.3: a and area elements with an href attribute (nothing is visited in a new
    /// document).</summary>
    public bool IsLink => IsHtml && Element.LocalName is "a" or "area" && Element.Attribute("href") is not null;

    /// <summary>The tree of a document: its root element and every element below it (template contents are a
    /// separate fragment, not children, HTML Standard §4.12.3).</summary>
    /// <param name="document">The document.</param>
    /// <returns>The root.</returns>
    public static StyleElement Tree(HtmlDocument document)
    {
        var quirks = document.Mode == DocumentMode.Quirks;
        var root = new StyleElement(document.DocumentElement, null, quirks);
        var pending = new Stack<StyleElement>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var element = pending.Pop();
            foreach (var child in element.Element.Children.OfType<HtmlElement>())
            {
                element.Children.Add(new StyleElement(child, element, quirks));
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
