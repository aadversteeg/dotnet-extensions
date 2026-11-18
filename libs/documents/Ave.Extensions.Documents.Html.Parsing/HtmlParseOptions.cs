using Ave.Extensions.Documents.Core;
using Ave.Extensions.ErrorPaths;

namespace Ave.Extensions.Documents.Html.Parsing;

/// <summary>How to parse: the name of the text, which every diagnostic carries as its SourceFile.</summary>
/// <param name="SourceName">The text's name (a file name or URL), for diagnostics; empty when it has none. Only a name:
/// the parser opens nothing.</param>
public sealed record HtmlParseOptions(string SourceName = "")
{
    /// <summary>No name.</summary>
    public static HtmlParseOptions Default { get; } = new();
}

/// <summary>The result of parsing a document: the tree, and every parse error the parser recovered from.</summary>
/// <param name="Document">The document.</param>
/// <param name="Diagnostics">The parse errors in the order the parser met them, each <see cref="HtmlErrorCodes.Parse.Syntax"/>
/// with its name and place.</param>
/// <param name="SourceName">The name the text was parsed under (<see cref="HtmlParseOptions.SourceName"/>), so a later
/// stage reports its own problems under the same name.</param>
public sealed record HtmlParseResult(HtmlDocument Document, ValueList<Error> Diagnostics, string SourceName);

/// <summary>The context element of a fragment parse (HTML Standard §13.4): the element whose content the text is.</summary>
/// <param name="Namespace">Its namespace.</param>
/// <param name="LocalName">Its local name (lowercase for HTML).</param>
/// <param name="Mode">The mode of the document it is in.</param>
public sealed record HtmlFragmentContext(ElementNamespace Namespace, string LocalName, DocumentMode Mode = DocumentMode.NoQuirks)
{
    /// <summary>The body element of a no-quirks document: the usual context of a fragment.</summary>
    public static HtmlFragmentContext Body { get; } = new(ElementNamespace.Html, "body");
}

/// <summary>The result of parsing a fragment.</summary>
/// <param name="Fragment">The nodes, as children of a fragment.</param>
/// <param name="Diagnostics">The parse errors in the order the parser met them.</param>
public sealed record HtmlFragmentResult(HtmlDocumentFragment Fragment, ValueList<Error> Diagnostics);
