using System;
using System.Collections.Generic;
using System.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Html.Parsing;

namespace Ave.Extensions.Documents.Html;

/// <summary>
/// THE RULE FOR A DOCUMENT'S METADATA IN HTML: the title of a document comes from its first <c>title</c> element, the
/// author and the description from its <c>meta</c> elements (the HTML Standard's standard metadata names <c>author</c>
/// and <c>description</c>).
/// <list type="number">
/// <item>The HTML <c>meta</c> elements are taken in tree order, wherever they stand; one whose <c>name</c> is the name
/// in any ASCII case counts. The first of them whose <c>content</c>, with ASCII white space stripped from both ends, is
/// not empty gives the value; the ones after it are not read.</item>
/// <item>That value is refused (<see cref="HtmlStyleErrorCodes.Unsupported.Metadata"/>, at the <c>meta</c>'s start tag),
/// and the document has no author or description, when THE RULE FOR A DOCUMENT'S METADATA TEXT
/// (<see cref="MetadataTexts"/>) does not accept it: longer than <see cref="MetadataTexts.MaxLength"/> UTF-16 code units,
/// or a control character other than tab, line feed and carriage return, or a character XML 1.0 does not allow.</item>
/// <item>A title with such a character is refused the same way, at the <c>title</c>'s start tag, and the document has no
/// title.</item>
/// </list>
/// Refused, not cleaned: the metadata is then the text the author wrote or none at all. No other <c>meta</c> is read: not <c>keywords</c>, <c>generator</c>,
/// <c>application-name</c> or any other name, not <c>http-equiv</c> (but for the language, which the style resolver
/// reads), not <c>charset</c>. Chromium 159's print writes neither the author nor the description; neither is printed.
/// </summary>
internal static class HtmlMetadata
{
    /// <summary>The first <c>meta</c> element with a non-empty content for a name, and that content stripped; null
    /// when there is none. One pass over the tree, with an explicit stack.</summary>
    /// <param name="root">The document element.</param>
    /// <param name="name">The metadata name, in lower case.</param>
    /// <returns>The element and its content, or null.</returns>
    public static (HtmlElement Meta, string Content)? First(StyledElement root, string name)
    {
        var pending = new Stack<StyledElement>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var element = pending.Pop();
            var html = element.Element;
            if (html.Namespace == ElementNamespace.Html && html.LocalName == "meta"
                && html.Attribute("name") is { } metaName && InfraAscii.EqualsIgnoreCase(metaName, name)
                && html.Attribute("content") is { } content && Stripped(content) is { Length: > 0 } value)
            {
                return (html, value);
            }

            foreach (var child in element.Children.OfType<StyledElement>().Reverse())
            {
                pending.Push(child);
            }
        }

        return null;
    }

    private static string Stripped(string text) => text.Trim([' ', '\t', '\n', '\f', '\r']);
}
