using System;
using System.Linq;
using System.Text;

namespace Ave.Extensions.Documents.Html.Parsing;

/// <summary>
/// The stable text form of a parsed tree: the format of the tree-construction tests of html5lib and web-platform-tests
/// (<c>html/syntax/parsing/resources/README.md</c>), so a tree can be compared with those suites and read in a diff.
/// </summary>
/// <remarks>
/// One node per line, each line <c>| </c> and two spaces per level: <c>&lt;name&gt;</c> for an HTML element,
/// <c>&lt;svg name&gt;</c> and <c>&lt;math name&gt;</c> for foreign ones; its attributes on the lines after it, one level
/// deeper and sorted by name (<c>name="value"</c>, a namespaced one as <c>xlink href="…"</c>); text as <c>"…"</c>;
/// comments as <c>&lt;!-- … --&gt;</c>; processing instructions as <c>&lt;?target data?&gt;</c>; a doctype as
/// <c>&lt;!DOCTYPE name&gt;</c>, with <c>"public" "system"</c> when either is not empty; a template's contents under a
/// line <c>content</c>. Lines end with a line feed.
/// </remarks>
public static class HtmlTreeText
{
    /// <summary>The text form of a node and everything under it (a document or fragment: its children).</summary>
    /// <param name="node">The node.</param>
    /// <returns>The text.</returns>
    public static string Format(HtmlNode node)
    {
        var text = new StringBuilder();
        switch (node)
        {
            case HtmlDocument document:
                foreach (var child in document.Children)
                {
                    Write(text, child, 0);
                }

                break;
            case HtmlDocumentFragment fragment:
                foreach (var child in fragment.Children)
                {
                    Write(text, child, 0);
                }

                break;
            default:
                Write(text, node, 0);
                break;
        }

        return text.ToString();
    }

    private static void Write(StringBuilder text, HtmlNode node, int depth)
    {
        var indent = "| " + new string(' ', depth * 2);
        switch (node)
        {
            case HtmlElement element:
                text.Append(indent).Append('<').Append(Prefix(element.Namespace)).Append(element.LocalName).Append(">\n");
                foreach (var attribute in element.Attributes.OrderBy(Name, StringComparer.Ordinal))
                {
                    text.Append(indent).Append("  ").Append(Name(attribute)).Append("=\"").Append(attribute.Value).Append("\"\n");
                }

                if (element.TemplateContent is { } content)
                {
                    text.Append(indent).Append("  content\n");
                    foreach (var child in content.Children)
                    {
                        Write(text, child, depth + 2);
                    }
                }

                foreach (var child in element.Children)
                {
                    Write(text, child, depth + 1);
                }

                break;
            case HtmlText textNode:
                text.Append(indent).Append('"').Append(textNode.Data).Append("\"\n");
                break;
            case HtmlComment comment:
                text.Append(indent).Append("<!-- ").Append(comment.Data).Append(" -->\n");
                break;
            case HtmlProcessingInstruction instruction:
                text.Append(indent).Append("<?").Append(instruction.Target).Append(' ').Append(instruction.Data).Append("?>\n");
                break;
            case HtmlDocumentType doctype:
                text.Append(indent).Append("<!DOCTYPE ").Append(doctype.Name);
                if (doctype.PublicId.Length > 0 || doctype.SystemId.Length > 0)
                {
                    text.Append(" \"").Append(doctype.PublicId).Append("\" \"").Append(doctype.SystemId).Append('"');
                }

                text.Append(">\n");
                break;
        }
    }

    private static string Prefix(ElementNamespace ns) => ns switch
    {
        ElementNamespace.Svg => "svg ",
        ElementNamespace.MathMl => "math ",
        _ => string.Empty,
    };

    private static string Name(HtmlAttribute attribute) => attribute.Namespace switch
    {
        AttributeNamespace.XLink => "xlink " + attribute.LocalName,
        AttributeNamespace.Xml => "xml " + attribute.LocalName,
        AttributeNamespace.Xmlns => "xmlns " + attribute.LocalName,
        _ => attribute.LocalName,
    };
}
