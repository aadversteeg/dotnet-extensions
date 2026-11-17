using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Fluent;
using static Ave.Extensions.Documents.Fluent.Elements;

namespace UnitTests.Extensions.Documents.Fluent;

/// <summary>
/// A table that continues on the next page repeats its whole grid line there by default (clone, as word processors print
/// business documents); <c>SplitLinesAtBreaks()</c> chooses the browser's way (slice). A row of blocks has no grid lines
/// and keeps the CSS initial value.
/// </summary>
public class TableLinesAtBreaksTests
{
    private static BoxDecorationBreak ModelOf(Element element)
    {
        var model = DocumentTranslator.ToModel(Document([element]).FontFamily("Lato")).Value;
        return model.Root.Children.OfType<Box>().Single().Style.DecorationBreak;
    }

    private static TableElement Grid() => Table(Columns(Fill()), [TableRow([Cell("a")])]);

    [Fact]
    public void A_table_repeats_whole_lines_at_a_break_by_default() =>
        ModelOf(Grid()).Should().Be(BoxDecorationBreak.Clone);

    [Fact]
    public void SplitLinesAtBreaks_chooses_slice() =>
        ModelOf(Grid().SplitLinesAtBreaks()).Should().Be(BoxDecorationBreak.Slice);

    [Fact]
    public void RepeatLinesAtBreaks_chooses_clone_again() =>
        ModelOf(Grid().SplitLinesAtBreaks().RepeatLinesAtBreaks()).Should().Be(BoxDecorationBreak.Clone);

    [Fact]
    public void A_row_of_blocks_keeps_the_initial_value() =>
        ModelOf(Row([Paragraph("a"), Paragraph("b")])).Should().Be(BoxDecorationBreak.Slice);
}
