using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Ave.Documents.Core;

namespace Ave.Documents.Fluent;

/// <summary>
/// The functions a document is written with. Import them with <c>using static Ave.Documents.Fluent.Elements;</c>
/// and write a document as nested calls; children go in collection expressions, and a component is just a
/// method that returns an element:
/// <code>
/// static Element Note(string text) => Block([Paragraph(text)], new BoxFormat(Padding: Edges.All(4.Mm())));
///
/// var letter = Document(
///     [Heading(1, "Invoice"), Paragraph([Text("Total: "), Bold("€ 120")]), Note("Thank you.")],
///     page: PageSetup.A4 with { Margins = Edges.All(2.Cm()) },
///     format: new TextFormat(FontFamily: "Lato", FontSize: 11.Pt()));
/// </code>
/// </summary>
/// <remarks>
/// Nothing here validates or throws: values are only checked when the document is translated
/// (<see cref="DocumentTranslator.ToModel"/>), and every problem is reported at once with the file and line of
/// the call that caused it. The last two parameters of every function are filled in by the compiler
/// (<c>[CallerFilePath]</c>, <c>[CallerLineNumber]</c>); do not pass them.
/// </remarks>
public static class Elements
{
    /// <summary>A document.</summary>
    /// <param name="body">The content, top to bottom.</param>
    /// <param name="format">The document's text formatting; must name a font family. Unset values get the CSS
    /// initial values: 12pt (<c>medium</c>), weight 400, black.</param>
    /// <param name="page">The page size and margins; A4 without margins when null.</param>
    /// <param name="title">The title, or null.</param>
    /// <param name="author">The author, or null.</param>
    /// <param name="callerFile">Filled in by the compiler.</param>
    /// <param name="callerLine">Filled in by the compiler.</param>
    /// <returns>The document.</returns>
    public static Document Document(
        ImmutableArray<Element> body,
        TextFormat format,
        PageSetup? page = null,
        string? title = null,
        string? author = null,
        [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0) =>
        new(body, page ?? PageSetup.A4, format, title, author, new SourceLocation(callerFile, callerLine));

    /// <summary>A block that stacks its children vertically.</summary>
    /// <param name="children">The child elements.</param>
    /// <param name="box">Margin, padding and background.</param>
    /// <param name="format">Text formatting the children inherit.</param>
    /// <param name="callerFile">Filled in by the compiler.</param>
    /// <param name="callerLine">Filled in by the compiler.</param>
    /// <returns>The block.</returns>
    public static BlockElement Block(
        ImmutableArray<Element> children,
        BoxFormat? box = null,
        TextFormat? format = null,
        [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0) =>
        new(children, box ?? BoxFormat.None, format ?? TextFormat.Inherit, new SourceLocation(callerFile, callerLine));

    /// <summary>A paragraph of plain text in one format.</summary>
    /// <param name="text">The text. Runs of spaces, tabs and line breaks show as one space.</param>
    /// <param name="box">Margin, padding and background.</param>
    /// <param name="format">Text formatting.</param>
    /// <param name="callerFile">Filled in by the compiler.</param>
    /// <param name="callerLine">Filled in by the compiler.</param>
    /// <returns>The paragraph.</returns>
    public static ParagraphElement Paragraph(
        string text,
        BoxFormat? box = null,
        TextFormat? format = null,
        [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0)
    {
        var source = new SourceLocation(callerFile, callerLine);
        return new([new Span(text, TextFormat.Inherit, source)], SemanticRole.Paragraph, box ?? BoxFormat.None, format ?? TextFormat.Inherit, source);
    }

    /// <summary>A paragraph made of spans, for text in mixed formats.</summary>
    /// <param name="spans">The spans, made with <see cref="Text"/> and <see cref="Bold"/>.</param>
    /// <param name="box">Margin, padding and background.</param>
    /// <param name="format">Text formatting the spans inherit.</param>
    /// <param name="callerFile">Filled in by the compiler.</param>
    /// <param name="callerLine">Filled in by the compiler.</param>
    /// <returns>The paragraph.</returns>
    public static ParagraphElement Paragraph(
        ImmutableArray<Span> spans,
        BoxFormat? box = null,
        TextFormat? format = null,
        [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0) =>
        new(spans, SemanticRole.Paragraph, box ?? BoxFormat.None, format ?? TextFormat.Inherit, new SourceLocation(callerFile, callerLine));

    /// <summary>
    /// A heading. Bold, and sized relative to the inherited font size like the HTML user-agent style sheet does
    /// for h1–h6 (see <see cref="UserAgentStyles"/>); <paramref name="format"/> overrides both.
    /// </summary>
    /// <param name="level">1 (most important) to 6.</param>
    /// <param name="text">The heading text.</param>
    /// <param name="box">Margin, padding and background.</param>
    /// <param name="format">Text formatting.</param>
    /// <param name="callerFile">Filled in by the compiler.</param>
    /// <param name="callerLine">Filled in by the compiler.</param>
    /// <returns>The heading.</returns>
    public static ParagraphElement Heading(
        int level,
        string text,
        BoxFormat? box = null,
        TextFormat? format = null,
        [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0)
    {
        var source = new SourceLocation(callerFile, callerLine);
        return new([new Span(text, TextFormat.Inherit, source)], SemanticRole.Heading(level), box ?? BoxFormat.None, format ?? TextFormat.Inherit, source);
    }

    /// <summary>A span of text inside a paragraph.</summary>
    /// <param name="text">The text.</param>
    /// <param name="format">Its formatting; inherits from the paragraph when null.</param>
    /// <param name="callerFile">Filled in by the compiler.</param>
    /// <param name="callerLine">Filled in by the compiler.</param>
    /// <returns>The span.</returns>
    public static Span Text(
        string text,
        TextFormat? format = null,
        [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0) =>
        new(text, format ?? TextFormat.Inherit, new SourceLocation(callerFile, callerLine));

    /// <summary>A span of bold text (weight 700) inside a paragraph.</summary>
    /// <param name="text">The text.</param>
    /// <param name="callerFile">Filled in by the compiler.</param>
    /// <param name="callerLine">Filled in by the compiler.</param>
    /// <returns>The span.</returns>
    public static Span Bold(
        string text,
        [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0) =>
        new(text, new TextFormat(FontWeight: FontWeight.Bold), new SourceLocation(callerFile, callerLine));

    /// <summary>
    /// Starts the next element on a new page (CSS <c>break-before: page</c> on that element). Several page breaks
    /// in a row give one break: they all apply to the same next element. A page break must be followed by an
    /// element in the same list.
    /// </summary>
    /// <param name="callerFile">Filled in by the compiler.</param>
    /// <param name="callerLine">Filled in by the compiler.</param>
    /// <returns>The page break.</returns>
    public static PageBreakElement PageBreak(
        [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0) =>
        new(new SourceLocation(callerFile, callerLine));
}
