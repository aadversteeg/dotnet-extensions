using System;
using System.Runtime.CompilerServices;
using Ave.Documents.Core;
using Ave.Documents.DocumentModel;

namespace Ave.Documents.Fluent;

/// <summary>
/// The functions a document is written with. Import them with <c>using static Ave.Documents.Fluent.Elements;</c>
/// and write a document as nested calls; children go in collection expressions, formatting goes in modifiers,
/// and a component is just a method that returns an element:
/// <code>
/// static readonly Style Note = Style.Empty.Padding(4.Mm()).Background(PaleBlue);
///
/// var letter = Document(
///     [
///         Heading(1, "Invoice").MarginBottom(12.Pt()).Color(Navy),
///         Paragraph([Text("Total: "), Bold("€ 120")]),
///         Block([Paragraph("Thank you.")]).With(Note),
///     ],
///     page: PageSetup.A4 with { Margins = Edges.All(2.Cm()) })
///     .FontFamily("Lato").FontSize(11.Pt());
/// </code>
/// </summary>
/// <remarks>
/// Nothing here validates or throws: values are only checked when the document is translated
/// (<see cref="DocumentTranslator.ToModel"/>), and every problem is reported at once with the file and line of
/// the element that caused it. The last two parameters of every function are filled in by the compiler
/// (<c>[CallerFilePath]</c>, <c>[CallerLineNumber]</c>); do not pass them. Modifiers do not record a location of
/// their own: a problem with a value is reported at the element it was set on.
/// </remarks>
public static class Elements
{
    /// <summary>A document. Set its text formatting with modifiers; it must name a font family
    /// (<c>.FontFamily("Lato")</c>). Unset values get the CSS initial values: 12pt (<c>medium</c>), weight 400,
    /// black.</summary>
    /// <param name="body">The content, top to bottom.</param>
    /// <param name="page">The page size and margins; A4 without margins when null.</param>
    /// <param name="title">The title, or null.</param>
    /// <param name="author">The author, or null.</param>
    /// <param name="language">The document's language as a language tag such as <c>nl-NL</c> or <c>en</c>, or null.
    /// Screen readers use it to choose a voice; PDF/UA requires it.</param>
    /// <param name="callerFile">Filled in by the compiler.</param>
    /// <param name="callerLine">Filled in by the compiler.</param>
    /// <returns>The document.</returns>
    public static Document Document(
        ValueList<Element> body,
        PageSetup? page = null,
        string? title = null,
        string? author = null,
        string? language = null,
        [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0) =>
        new(body, page ?? PageSetup.A4, Style.Empty, title, author, new SourceLocation(callerFile, callerLine)) { Language = language };

    /// <summary>
    /// A file to embed in a document with <see cref="Fluent.Document.Attach"/>. The bytes are copied. Set its
    /// relationship to the document, description and modification date with <see cref="AttachmentModifiers"/>.
    /// </summary>
    /// <param name="fileName">The name readers show and save it under, for example <c>timesheet.xml</c>.</param>
    /// <param name="content">The file's bytes.</param>
    /// <param name="mediaType">Its MIME media type, for example <c>application/xml</c>; <c>application/octet-stream</c>
    /// when not known.</param>
    /// <param name="callerFile">Filled in by the compiler.</param>
    /// <param name="callerLine">Filled in by the compiler.</param>
    /// <returns>The attachment.</returns>
    public static Attachment Attachment(
        string fileName,
        ReadOnlySpan<byte> content,
        string mediaType,
        [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0) =>
        new(fileName, [.. content], mediaType, new SourceLocation(callerFile, callerLine));

    /// <summary>A block that stacks its children vertically. Its text formatting is inherited by the children.</summary>
    /// <param name="children">The child elements.</param>
    /// <param name="callerFile">Filled in by the compiler.</param>
    /// <param name="callerLine">Filled in by the compiler.</param>
    /// <returns>The block.</returns>
    public static BlockElement Block(
        ValueList<Element> children,
        [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0) =>
        new(children, Style.Empty, Style.Empty, new SourceLocation(callerFile, callerLine));

    /// <summary>A paragraph of plain text in one format.</summary>
    /// <param name="text">The text. Runs of spaces, tabs and line breaks show as one space.</param>
    /// <param name="callerFile">Filled in by the compiler.</param>
    /// <param name="callerLine">Filled in by the compiler.</param>
    /// <returns>The paragraph.</returns>
    public static ParagraphElement Paragraph(
        string text,
        [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0)
    {
        var source = new SourceLocation(callerFile, callerLine);
        return new([new Span(text, Style.Empty, Style.Empty, source)], SemanticRole.Paragraph, Style.Empty, Style.Empty, source);
    }

    /// <summary>A paragraph made of spans, for text in mixed formats.</summary>
    /// <param name="spans">The spans, made with <see cref="Text"/> and <see cref="Bold"/>.</param>
    /// <param name="callerFile">Filled in by the compiler.</param>
    /// <param name="callerLine">Filled in by the compiler.</param>
    /// <returns>The paragraph.</returns>
    public static ParagraphElement Paragraph(
        ValueList<Span> spans,
        [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0) =>
        new(spans, SemanticRole.Paragraph, Style.Empty, Style.Empty, new SourceLocation(callerFile, callerLine));

    /// <summary>
    /// A heading. Bold, and sized relative to the inherited font size like the HTML user-agent style sheet does
    /// for h1–h6 (see <see cref="UserAgentStyles"/>); modifiers and styles override both.
    /// </summary>
    /// <param name="level">1 (most important) to 6.</param>
    /// <param name="text">The heading text.</param>
    /// <param name="callerFile">Filled in by the compiler.</param>
    /// <param name="callerLine">Filled in by the compiler.</param>
    /// <returns>The heading.</returns>
    public static ParagraphElement Heading(
        int level,
        string text,
        [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0)
    {
        var source = new SourceLocation(callerFile, callerLine);
        return new([new Span(text, Style.Empty, Style.Empty, source)], SemanticRole.Heading(level), Style.Empty, Style.Empty, source);
    }

    /// <summary>A span of text inside a paragraph; it inherits the paragraph's formatting.</summary>
    /// <param name="text">The text.</param>
    /// <param name="callerFile">Filled in by the compiler.</param>
    /// <param name="callerLine">Filled in by the compiler.</param>
    /// <returns>The span.</returns>
    public static Span Text(
        string text,
        [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0) =>
        new(text, Style.Empty, Style.Empty, new SourceLocation(callerFile, callerLine));

    /// <summary>
    /// A forced line break inside a paragraph, as HTML's <c>br</c>: the text after it starts a new line. It is the line
    /// separator U+2028, a mandatory break (UAX #14, class BK), which white-space collapsing keeps and which draws
    /// nothing; a line that ends with it is not justified (CSS Text 3 §6.3).
    /// </summary>
    /// <param name="callerFile">Filled in by the compiler.</param>
    /// <param name="callerLine">Filled in by the compiler.</param>
    /// <returns>The span.</returns>
    public static Span LineBreak(
        [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0) =>
        new(char.ConvertFromUtf32(0x2028), Style.Empty, Style.Empty, new SourceLocation(callerFile, callerLine));

    /// <summary>A span of bold text (weight 700) inside a paragraph: <c>Text(text).Bold()</c>.</summary>
    /// <param name="text">The text.</param>
    /// <param name="callerFile">Filled in by the compiler.</param>
    /// <param name="callerLine">Filled in by the compiler.</param>
    /// <returns>The span.</returns>
    public static Span Bold(
        string text,
        [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0) =>
        Text(text, callerFile, callerLine).Bold();

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

    /// <summary>
    /// The number of the page the text lands on, as a span (CSS <c>counter(page)</c>, decimal). Usable in any
    /// paragraph: in the body, a header or a footer.
    /// </summary>
    /// <param name="callerFile">Filled in by the compiler.</param>
    /// <param name="callerLine">Filled in by the compiler.</param>
    /// <returns>The span.</returns>
    public static Span PageNumber(
        [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0) =>
        new(string.Empty, Style.Empty, Style.Empty, new SourceLocation(callerFile, callerLine)) { Counter = DocumentModel.PageCounter.PageNumber };

    /// <summary>
    /// The number of pages of the document, as a span (CSS <c>counter(pages)</c>). Layout runs again until the count
    /// it shows is the count it produces, at most five times (error <c>Documents.Layout.NotConverging</c>).
    /// </summary>
    /// <param name="callerFile">Filled in by the compiler.</param>
    /// <param name="callerLine">Filled in by the compiler.</param>
    /// <returns>The span.</returns>
    public static Span PageCount(
        [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0) =>
        new(string.Empty, Style.Empty, Style.Empty, new SourceLocation(callerFile, callerLine)) { Counter = DocumentModel.PageCounter.PageCount };

    /// <summary>A page size without margins, for sizes other than <see cref="PageSetup.A4"/>.</summary>
    /// <param name="width">The page width.</param>
    /// <param name="height">The page height.</param>
    /// <returns>The page setup.</returns>
    public static PageSetup Page(Length width, Length height) => new(width, height, Edges.Zero);

    /// <summary>
    /// A table. The columns come first, then the rows: header rows (repeated on every page), body rows and row groups.
    /// <code>
    /// Table(Columns(3.Cm(), Fill(1), Auto),
    ///     [
    ///         HeaderRow([Cell("Date"), Cell("Task"), Cell("Hours")]),
    ///         .. entries.Select(entry => TableRow([Cell(entry.Date), Cell(entry.Task), Cell(entry.Hours)])),
    ///     ])
    ///     .GridLines(1.Pt(), grey).CellPadding(4.Pt(), 2.Pt())
    /// </code>
    /// </summary>
    /// <param name="columns">The column sizes, made with <see cref="Columns"/>.</param>
    /// <param name="rows">The rows and row groups.</param>
    /// <param name="callerFile">Filled in by the compiler.</param>
    /// <param name="callerLine">Filled in by the compiler.</param>
    /// <returns>The table.</returns>
    public static TableElement Table(
        ValueList<TrackSize> columns,
        ValueList<TablePart> rows,
        [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0) =>
        new(columns, rows, Style.Empty, Style.Empty, BoxFormat.None, new SourceLocation(callerFile, callerLine));

    /// <summary>The column sizes of a table: fixed lengths (<c>2.5.Cm()</c>), <see cref="Auto"/> (as wide as the
    /// content needs) and <see cref="Fill"/> (a share of the width left over).</summary>
    /// <param name="sizes">One size per column, left to right.</param>
    /// <returns>The column list.</returns>
    public static ValueList<TrackSize> Columns(params TrackSize[] sizes) => [.. sizes];

    /// <summary>A column that takes a share of the width left over after the other columns, in proportion to its
    /// weight (CSS Grid's <c>fr</c>); never narrower than its narrowest content.</summary>
    /// <param name="weight">The share, a positive number; 1 by default.</param>
    /// <returns>The column size.</returns>
    public static TrackSize Fill(double weight = 1) => new TrackSize.Fill(weight);

    /// <summary>A column as wide as its content needs, up to its widest content.</summary>
    public static TrackSize Auto => TrackSize.Auto.Instance;

    /// <summary>A header row: repeated at the top of every page the table continues on; its cells are header cells.</summary>
    /// <param name="cells">The cells.</param>
    /// <param name="callerFile">Filled in by the compiler.</param>
    /// <param name="callerLine">Filled in by the compiler.</param>
    /// <returns>The row.</returns>
    public static TableRowElement HeaderRow(
        ValueList<CellElement> cells,
        [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0) =>
        new(cells, IsHeader: true, Style.Empty, Style.Empty, new SourceLocation(callerFile, callerLine));

    /// <summary>A body row. By default a row may break between the lines of its cells; <c>.KeepTogether()</c> moves it
    /// to the next page whole instead.</summary>
    /// <param name="cells">The cells.</param>
    /// <param name="callerFile">Filled in by the compiler.</param>
    /// <param name="callerLine">Filled in by the compiler.</param>
    /// <returns>The row.</returns>
    public static TableRowElement TableRow(
        ValueList<CellElement> cells,
        [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0) =>
        new(cells, IsHeader: false, Style.Empty, Style.Empty, new SourceLocation(callerFile, callerLine));

    /// <summary>A group of body rows that belong together; <c>.KeepTogether()</c> keeps them on one page.</summary>
    /// <param name="rows">The rows.</param>
    /// <param name="callerFile">Filled in by the compiler.</param>
    /// <param name="callerLine">Filled in by the compiler.</param>
    /// <returns>The group.</returns>
    public static RowGroupElement RowGroup(
        ValueList<TableRowElement> rows,
        [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0) =>
        new(rows, Style.Empty, Style.Empty, new SourceLocation(callerFile, callerLine));

    /// <summary>A cell with one paragraph of text.</summary>
    /// <param name="text">The text.</param>
    /// <param name="callerFile">Filled in by the compiler.</param>
    /// <param name="callerLine">Filled in by the compiler.</param>
    /// <returns>The cell.</returns>
    public static CellElement Cell(
        string text,
        [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0) =>
        Cell([Paragraph(text, callerFile, callerLine)], callerFile, callerLine);

    /// <summary>A cell with any content, stacked vertically.</summary>
    /// <param name="content">The content.</param>
    /// <param name="callerFile">Filled in by the compiler.</param>
    /// <param name="callerLine">Filled in by the compiler.</param>
    /// <returns>The cell.</returns>
    public static CellElement Cell(
        ValueList<Element> content,
        [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0) =>
        new(content, CellSpan.Single, null, Style.Empty, Style.Empty, new SourceLocation(callerFile, callerLine));

    /// <summary>Elements side by side, one column each (equal shares unless <see cref="RowElement.Columns"/> is set).</summary>
    /// <param name="items">The elements, left to right.</param>
    /// <param name="callerFile">Filled in by the compiler.</param>
    /// <param name="callerLine">Filled in by the compiler.</param>
    /// <returns>The row.</returns>
    public static RowElement Row(
        ValueList<Element> items,
        [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0) =>
        new(items, [], null, Style.Empty, Style.Empty, new SourceLocation(callerFile, callerLine));

    /// <summary>
    /// An image from the bytes of a PNG or JPEG file. The bytes are checked when the document is translated. Say what
    /// it shows with <see cref="ImageElement.Alt"/>, or mark it <see cref="ImageElement.Decorative"/>; PDF/UA refuses
    /// an image with neither.
    /// </summary>
    /// <param name="file">The file's bytes.</param>
    /// <param name="callerFile">Filled in by the compiler.</param>
    /// <param name="callerLine">Filled in by the compiler.</param>
    /// <returns>The image.</returns>
    public static ImageElement Image(
        ReadOnlySpan<byte> file,
        [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0) =>
        new(new ImageFile(file), null, null, Style.Empty, Style.Empty, new SourceLocation(callerFile, callerLine));
}
