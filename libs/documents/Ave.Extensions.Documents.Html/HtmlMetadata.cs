using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Html.Parsing;

namespace Ave.Extensions.Documents.Html;

/// <summary>
/// THE RULE FOR A DOCUMENT'S METADATA IN HTML: the author and the description of a document come from its <c>meta</c>
/// elements (the HTML Standard's standard metadata names <c>author</c> and <c>description</c>).
/// <list type="number">
/// <item>The HTML <c>meta</c> elements are taken in tree order, wherever they stand; one whose <c>name</c> is the name
/// in any ASCII case counts. The first of them whose <c>content</c>, with ASCII white space stripped from both ends, is
/// not empty gives the value; the ones after it are not read.</item>
/// <item>That value is refused (<see cref="HtmlStyleErrorCodes.Unsupported.Metadata"/>, at the <c>meta</c>'s start tag),
/// and the document has no author or description, when it is longer than <see cref="MaxLength"/> UTF-16 code units or
/// holds a character this rule does not let through: a character XML 1.0 does not allow (<see cref="XmlCharacters"/>,
/// the rule every metadata text of the model keeps, as the XMP metadata is XML) or a control character U+007F to
/// U+009F. Inside the value only tab, line feed and carriage return remain of the control characters.</item>
/// </list>
/// A control character is refused, not removed: the metadata is then the text the author wrote or none at all, never a
/// text the engine made from it, and a hidden control character in a document's description is more likely a fault of
/// whatever made the HTML than meant. No other <c>meta</c> is read: not <c>keywords</c>, <c>generator</c>,
/// <c>application-name</c> or any other name, not <c>http-equiv</c> (but for the language, which the style resolver
/// reads), not <c>charset</c>. Chromium 159's print writes neither the author nor the description; neither is printed.
/// </summary>
internal static class HtmlMetadata
{
    /// <summary>The longest author or description taken, in UTF-16 code units after stripping: a fixed bound far inside
    /// the longest string PDF/A allows (32 767 bytes; as UTF-16 this value takes 8 194), longer than any name or
    /// summary of a document needs.</summary>
    internal const int MaxLength = 4_096;

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

    /// <summary>Why a stripped content is not taken, or null when it is.</summary>
    /// <param name="content">The content.</param>
    /// <returns>The reason, for the message.</returns>
    public static string? Problem(string content)
    {
        if (content.Length > MaxLength)
        {
            return $"it is {content.Length.ToString(CultureInfo.InvariantCulture)} characters long; at most {MaxLength.ToString(CultureInfo.InvariantCulture)} are taken";
        }

        var offset = XmlCharacters.IndexOfForbidden(content) ?? IndexOfControl(content);
        return offset is { } at
            ? $"it holds U+{(int)content[at]:X4} at position {at.ToString(CultureInfo.InvariantCulture)}, a control character or a character XML does not allow"
            : null;
    }

    private static int? IndexOfControl(string text)
    {
        var index = text.AsSpan().IndexOfAnyInRange('\u007F', '\u009F');
        return index < 0 ? null : index;
    }

    private static string Stripped(string text) => text.Trim([' ', '\t', '\n', '\f', '\r']);
}
