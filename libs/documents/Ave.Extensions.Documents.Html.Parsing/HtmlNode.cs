using System;
using System.Linq;
using System.Text;
using Ave.Extensions.Documents.Core;

namespace Ave.Extensions.Documents.Html.Parsing;

/// <summary>
/// A node of the tree the parser builds: the subset of the DOM node types an HTML parser with scripting off can
/// produce (DOM Standard §4.4): <see cref="HtmlDocument"/>, <see cref="HtmlDocumentFragment"/>,
/// <see cref="HtmlDocumentType"/>, <see cref="HtmlElement"/>, <see cref="HtmlText"/>, <see cref="HtmlComment"/> and
/// <see cref="HtmlProcessingInstruction"/>. The tree is immutable and compares by value.
/// </summary>
/// <param name="Position">Where the node starts in the decoded text: the <c>&lt;</c> of its tag, comment or doctype,
/// or the first character of its text. An element the parser inserts without a tag of its own (an implied
/// <c>html</c>, <c>head</c>, <c>body</c> or <c>tbody</c>, or a formatting element the parser reopens) takes the
/// position of the token that made the parser insert it.</param>
public abstract record HtmlNode(TextPosition Position);

/// <summary>A document (DOM Standard §4.5): the result of parsing a whole text.</summary>
/// <param name="Mode">The document's mode, set from its doctype (HTML Standard §13.2.6.4.1).</param>
/// <param name="Children">Its children: comments, at most one doctype, and the <c>html</c> element.</param>
public sealed record HtmlDocument(DocumentMode Mode, ValueList<HtmlNode> Children) : HtmlNode(TextPosition.Start)
{
    /// <summary>The document element: the <c>html</c> element every parsed document has; null for a document made in
    /// code without an element.</summary>
    public HtmlElement? DocumentElement => Children.OfType<HtmlElement>().FirstOrDefault();

    /// <summary>The members for <see cref="object.ToString"/>: the children by their number, so the text of a deep tree
    /// costs no recursion (<see cref="HtmlTreeText"/> writes the whole tree).</summary>
    /// <param name="builder">Where the members are written.</param>
    /// <returns>True: members were written.</returns>
    protected override bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Mode = ").Append(Mode).Append(", Children = ").Append(Children.Count).Append(" nodes");
        return true;
    }
}

/// <summary>A document fragment (DOM Standard §4.7): the content of a <c>template</c> element, or the result of
/// parsing a fragment (HTML Standard §13.4).</summary>
/// <param name="Children">Its children.</param>
/// <param name="Position">Where it starts: the template's start tag, or the start of the fragment's text.</param>
public sealed record HtmlDocumentFragment(ValueList<HtmlNode> Children, TextPosition Position) : HtmlNode(Position)
{
    /// <summary>The members for <see cref="object.ToString"/>: the children by their number, so the text of a deep tree
    /// costs no recursion (<see cref="HtmlTreeText"/> writes the whole tree).</summary>
    /// <param name="builder">Where the members are written.</param>
    /// <returns>True: members were written.</returns>
    protected override bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Children = ").Append(Children.Count).Append(" nodes, Position = ").Append(Position);
        return true;
    }
}

/// <summary>A doctype (DOM Standard §4.6). Missing identifiers are empty strings, as the DOM has them.</summary>
/// <param name="Name">The name, lowercase (for example <c>html</c>); empty when the doctype has none.</param>
/// <param name="PublicId">The public identifier, or empty.</param>
/// <param name="SystemId">The system identifier, or empty.</param>
/// <param name="Position">Where it starts.</param>
public sealed record HtmlDocumentType(string Name, string PublicId, string SystemId, TextPosition Position) : HtmlNode(Position);

/// <summary>An element (DOM Standard §4.9).</summary>
/// <param name="Namespace">Its namespace: HTML, or SVG and MathML for foreign content (HTML Standard §13.2.6.5).</param>
/// <param name="LocalName">Its local name: lowercase for HTML elements; for SVG with the case the standard's table
/// restores (<c>foreignObject</c>, §13.2.6.5).</param>
/// <param name="Attributes">Its attributes in source order, without duplicates (the first of two equal names
/// wins, §13.2.5.33).</param>
/// <param name="Children">Its children.</param>
/// <param name="TemplateContent">For a <c>template</c> element in the HTML namespace, its template contents (where
/// the parser puts what is written inside it, §4.12.3); null for every other element.</param>
/// <param name="Position">Where its start tag starts.</param>
public sealed record HtmlElement(
    ElementNamespace Namespace,
    string LocalName,
    ValueList<HtmlAttribute> Attributes,
    ValueList<HtmlNode> Children,
    HtmlDocumentFragment? TemplateContent,
    TextPosition Position) : HtmlNode(Position)
{
    /// <summary>The value of the attribute in no namespace with this local name, or null when there is none.</summary>
    /// <param name="localName">The attribute's local name, as stored (lowercase for HTML elements).</param>
    /// <returns>The value, or null.</returns>
    public string? Attribute(string localName)
    {
        foreach (var attribute in Attributes)
        {
            if (attribute.Namespace == AttributeNamespace.None && string.Equals(attribute.LocalName, localName, StringComparison.Ordinal))
            {
                return attribute.Value;
            }
        }

        return null;
    }

    /// <summary>True for an element in the HTML namespace with this local name.</summary>
    /// <param name="localName">The local name, lowercase.</param>
    /// <returns>Whether the element is that HTML element.</returns>
    public bool IsHtml(string localName) =>
        Namespace == ElementNamespace.Html && string.Equals(LocalName, localName, StringComparison.Ordinal);

    /// <summary>The members for <see cref="object.ToString"/>: the children and the template contents by their number
    /// of nodes, so the text of a deep tree costs no recursion (<see cref="HtmlTreeText"/> writes the whole tree).</summary>
    /// <param name="builder">Where the members are written.</param>
    /// <returns>True: members were written.</returns>
    protected override bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Namespace = ").Append(Namespace).Append(", LocalName = ").Append(LocalName)
            .Append(", Attributes = ").Append(Attributes).Append(", Children = ").Append(Children.Count).Append(" nodes");
        if (TemplateContent is { } content)
        {
            builder.Append(", TemplateContent = ").Append(content.Children.Count).Append(" nodes");
        }

        builder.Append(", Position = ").Append(Position);
        return true;
    }
}

/// <summary>Text (DOM Standard §4.11). Adjacent character tokens become one text node (HTML Standard §13.2.6.1,
/// "insert a character").</summary>
/// <param name="Data">The characters, with character references resolved and line ends normalised to line feeds.</param>
/// <param name="Position">Where its first character was written.</param>
public sealed record HtmlText(string Data, TextPosition Position) : HtmlNode(Position);

/// <summary>A comment (DOM Standard §4.14).</summary>
/// <param name="Data">The comment's text, between <c>&lt;!--</c> and <c>--&gt;</c>.</param>
/// <param name="Position">Where it starts.</param>
public sealed record HtmlComment(string Data, TextPosition Position) : HtmlNode(Position);

/// <summary>A processing instruction (DOM Standard §4.13), <c>&lt;?target data?&gt;</c>: the HTML parser makes one since
/// the tokenizer states of HTML Standard §13.2.5.72–§13.2.5.76 (targets <c>xml</c> and <c>xml-stylesheet</c> become
/// comments).</summary>
/// <param name="Target">Its target: letters, digits, hyphens and low lines, starting with a letter or low line.</param>
/// <param name="Data">Its data, after the white space that follows the target.</param>
/// <param name="Position">Where it starts.</param>
public sealed record HtmlProcessingInstruction(string Target, string Data, TextPosition Position) : HtmlNode(Position);

/// <summary>An attribute (DOM Standard §4.9.2).</summary>
/// <param name="Namespace">Its namespace: none for every attribute of an HTML element; XLink, XML or XMLNS for the
/// few attributes of foreign elements the standard adjusts (§13.2.6.1, "adjust foreign attributes").</param>
/// <param name="Prefix">Its prefix (<c>xlink</c>, <c>xml</c>, <c>xmlns</c>) for an adjusted foreign attribute; null
/// otherwise.</param>
/// <param name="LocalName">Its local name: lowercase as the tokenizer made it, except the SVG and MathML attributes
/// whose case the standard restores (<c>viewBox</c>, <c>definitionURL</c>).</param>
/// <param name="Value">Its value, with character references resolved.</param>
/// <param name="Position">Where its name starts.</param>
public sealed record HtmlAttribute(AttributeNamespace Namespace, string? Prefix, string LocalName, string Value, TextPosition Position)
{
    /// <summary>Where the value's characters were written, from the parser (empty for an attribute made in code): see
    /// <see cref="PositionOf"/>.</summary>
    internal ValueList<HtmlValueAnchor> ValueAnchors { get; init; } = [];

    /// <summary>Where a character of <see cref="Value"/> was written in the source: its own place for a character
    /// written as itself, the ampersand for the characters of a character reference; an offset at the end of the
    /// value is just after its last character. For an attribute made in code, its <see cref="Position"/>.</summary>
    /// <param name="offset">An offset in <see cref="Value"/>, from 0 to its length.</param>
    /// <returns>The line and column.</returns>
    public TextPosition PositionOf(int offset)
    {
        var anchors = ValueAnchors;
        int low = 0, high = anchors.Count - 1, found = -1;
        while (low <= high)
        {
            var middle = (low + high) / 2;
            if (anchors[middle].Offset <= offset)
            {
                found = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        if (found < 0)
        {
            return Position;
        }

        var anchor = anchors[found];
        return anchor.Contiguous
            ? new TextPosition(anchor.Position.Line, anchor.Position.Column + offset - anchor.Offset)
            : anchor.Position;
    }
}

/// <summary>From <paramref name="Offset"/> in an attribute value on, where its characters were written: at
/// <paramref name="Position"/> and one by one after it on the same line, or all at that place.</summary>
/// <param name="Offset">The offset in the value.</param>
/// <param name="Position">The place in the source.</param>
/// <param name="Contiguous">Whether the characters follow one by one (else they are one character reference).</param>
internal readonly record struct HtmlValueAnchor(int Offset, TextPosition Position, bool Contiguous);

/// <summary>The namespace of an element (Infra Standard §8, namespaces).</summary>
public enum ElementNamespace
{
    /// <summary>The HTML namespace, <c>http://www.w3.org/1999/xhtml</c>.</summary>
    Html,

    /// <summary>The SVG namespace, <c>http://www.w3.org/2000/svg</c>.</summary>
    Svg,

    /// <summary>The MathML namespace, <c>http://www.w3.org/1998/Math/MathML</c>.</summary>
    MathMl,
}

/// <summary>The namespace of an attribute (Infra Standard §8; HTML Standard §13.2.6.1, "adjust foreign attributes").</summary>
public enum AttributeNamespace
{
    /// <summary>No namespace: every attribute of an HTML element, and most of foreign elements.</summary>
    None,

    /// <summary>The XLink namespace, <c>http://www.w3.org/1999/xlink</c>.</summary>
    XLink,

    /// <summary>The XML namespace, <c>http://www.w3.org/XML/1998/namespace</c>.</summary>
    Xml,

    /// <summary>The XMLNS namespace, <c>http://www.w3.org/2000/xmlns/</c>.</summary>
    Xmlns,
}

/// <summary>The mode of a document (DOM Standard §4.5, "mode"), set by the parser from the doctype.</summary>
public enum DocumentMode
{
    /// <summary>No-quirks mode: the doctype is <c>&lt;!DOCTYPE html&gt;</c> or another one the standard does not list.</summary>
    NoQuirks,

    /// <summary>Limited-quirks mode (the "almost standards" doctypes, HTML Standard §13.2.6.4.1).</summary>
    LimitedQuirks,

    /// <summary>Quirks mode: no doctype, or a legacy one.</summary>
    Quirks,
}
