namespace Ave.Extensions.Documents.Css.Selectors;

/// <summary>
/// The element abstraction selectors are matched against (Selectors 4 Appendix A: what a document language must
/// provide). The CSS library knows nothing about HTML; a document tree implements this, and the answers that depend on
/// the document language (which elements are links, which attribute values are case-insensitive, quirks mode) are its
/// own. The sibling counts are precomputed by the tree, so the <c>:nth-*</c> pseudo-classes cost constant work.
/// </summary>
public interface IStyleElement
{
    /// <summary>The element's local name, as the document stores it (lowercase for HTML elements).</summary>
    string LocalName { get; }

    /// <summary>Whether it is an HTML element in an HTML document: its type selectors and attribute names then match
    /// ASCII case-insensitively (Selectors 4 §5.1, §6.1; HTML Standard §4.16.2).</summary>
    bool IsHtml { get; }

    /// <summary>Whether the document is in quirks mode: class and ID selectors then match ASCII case-insensitively
    /// (Selectors 4 §6.6, §6.7; HTML Standard §4.16.2).</summary>
    bool IsQuirksMode { get; }

    /// <summary>The element's ID, or null.</summary>
    string? Id { get; }

    /// <summary>Whether the element has this class (its class attribute split on ASCII white space), compared as the
    /// document's mode says.</summary>
    /// <param name="name">The class.</param>
    /// <returns>Whether it has it.</returns>
    bool HasClass(string name);

    /// <summary>The element's classes (its class attribute split on ASCII white space), as written; the cascade uses them
    /// to find the rules worth matching.</summary>
    Ave.Extensions.Documents.Core.ValueList<string> Classes { get; }

    /// <summary>The value of the attribute in no namespace with this local name, or null. The caller lowercases the
    /// name for an HTML element.</summary>
    /// <param name="localName">The attribute's local name.</param>
    /// <returns>The value, or null.</returns>
    string? GetAttribute(string localName);

    /// <summary>Whether this attribute's value is compared ASCII case-insensitively by default (HTML Standard §4.16.2,
    /// the list of attributes whose values are case-insensitive in HTML documents).</summary>
    /// <param name="localName">The attribute's local name, lowercase.</param>
    /// <returns>Whether it is case-insensitive.</returns>
    bool IsCaseInsensitiveAttributeValue(string localName);

    /// <summary>The parent element, or null for the root.</summary>
    IStyleElement? Parent { get; }

    /// <summary>The previous sibling element, or null.</summary>
    IStyleElement? PreviousSibling { get; }

    /// <summary>The next sibling element, or null.</summary>
    IStyleElement? NextSibling { get; }

    /// <summary>The 1-based index among the parent's element children.</summary>
    int Index { get; }

    /// <summary>The number of the parent's element children.</summary>
    int SiblingCount { get; }

    /// <summary>The 1-based index among the siblings of the same type (namespace and local name).</summary>
    int IndexOfType { get; }

    /// <summary>The number of siblings of the same type, the element included.</summary>
    int CountOfType { get; }

    /// <summary>Whether it is the root element of its document (Selectors 4 §13.1).</summary>
    bool IsRoot { get; }

    /// <summary>Whether it has no children that keep <c>:empty</c> from matching (Selectors 4 §13.2): no element child and
    /// no text; comments and processing instructions do not count.</summary>
    bool IsEmpty { get; }

    /// <summary>Whether it is a link (Selectors 4 §8.2; HTML Standard §16.2: an a or area element with an href).</summary>
    bool IsLink { get; }
}
