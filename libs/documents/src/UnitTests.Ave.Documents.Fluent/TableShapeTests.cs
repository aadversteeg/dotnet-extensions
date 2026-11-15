using System.Runtime.CompilerServices;
using Ave.Documents.Core;
using Ave.Documents.Fluent;
using Ave.Extensions.ErrorPaths;
using static Ave.Documents.Fluent.Elements;

namespace UnitTests.Ave.Documents.Fluent;

/// <summary>
/// A table's shape is the author's input, so the front-end checks it — with the documented code
/// <c>Documents.Input.Invalid.Table</c>, at the line of the cell or row, in words about the author's element — and a
/// page break is refused wherever it cannot move content to another page, however deep it sits. The slot-filling rule
/// is <see cref="TableSlots"/>, the same one layout uses.
/// </summary>
public class TableShapeTests
{
    private static Error Problems(ValueList<Element> body, PageSetup? page = null) =>
        DocumentTranslator.ToModel(Document(body, page).FontFamily("Lato")).Error;

    private static void ShouldPointAt(Error error, ErrorCode code, int line)
    {
        error.Code.Should().Be(code);
        error.Metadata![ErrorMetadataKeys.SourceFile].Should().Be(ThisFile());
        error.Metadata![ErrorMetadataKeys.SourceLine].Should().Be(line);
        error.Message.Should().NotContain("Box #", "the message names the author's element, not a layout box");
    }

    [Fact]
    public void A_row_with_more_cells_than_columns_is_an_input_error_at_the_extra_cell()
    {
        var line = CurrentLine() + 1;
        var row = TableRow([Cell("a"), Cell("b"), Cell("c")]);

        var error = Problems([Table(Columns(Fill(1), Fill(1)), [row])]);

        ShouldPointAt(error, DocumentErrorCodes.Input.Invalid.Table, line);
        error.Message.Should().Contain("column");
    }

    [Fact]
    public void A_row_span_past_its_row_group_is_an_input_error_at_the_cell()
    {
        var line = CurrentLine() + 1;
        var spanning = Cell("a").RowSpan(3);

        var error = Problems([Table(Columns(Fill(1), Fill(1)), [TableRow([spanning, Cell("b")]), TableRow([Cell("c")])])]);

        ShouldPointAt(error, DocumentErrorCodes.Input.Invalid.Table, line);
        error.Message.Should().Contain("row group");
    }

    [Fact]
    public void A_row_without_cells_is_an_input_error_at_the_row()
    {
        var line = CurrentLine() + 1;
        var empty = TableRow([]);

        ShouldPointAt(Problems([Table(Columns(Fill(1)), [TableRow([Cell("a")]), empty])]), DocumentErrorCodes.Input.Invalid.Table, line);
    }

    [Fact]
    public void A_row_of_blocks_without_items_says_so()
    {
        var line = CurrentLine() + 1;
        var row = Row([]);

        var error = Problems([row]);

        ShouldPointAt(error, DocumentErrorCodes.Input.Invalid.Columns, line);
        error.Message.Should().Contain("item");
    }

    [Fact]
    public void Every_shape_problem_of_a_table_is_reported_together()
    {
        var error = Problems([Table(Columns(Fill(1)), [TableRow([Cell("a"), Cell("b")]), TableRow([]), TableRow([Cell("c").RowSpan(2)])])]);

        error.Parts().Should().HaveCount(3).And.OnlyContain(part => part.Code == DocumentErrorCodes.Input.Invalid.Table);
    }

    [Fact]
    public void A_page_break_in_a_block_inside_a_cell_is_an_input_error_at_the_break()
    {
        var line = CurrentLine() + 1;
        var cell = Cell([Block([Paragraph("a"), PageBreak(), Paragraph("b")])]);

        ShouldPointAt(Problems([Table(Columns(Fill(1)), [TableRow([cell])])]), DocumentErrorCodes.Input.Invalid.PageBreak, line);
    }

    [Fact]
    public void A_page_break_inside_a_header_is_an_input_error_at_the_break()
    {
        var line = CurrentLine() + 1;
        var header = Block([Paragraph("a"), PageBreak(), Paragraph("b")]);

        var error = Problems([Paragraph("body")], PageSetup.A4.Header(header));

        ShouldPointAt(error, DocumentErrorCodes.Input.Invalid.PageBreak, line);
    }

    private static int CurrentLine([CallerLineNumber] int line = 0) => line;

    private static string ThisFile([CallerFilePath] string file = "") => file;
}
