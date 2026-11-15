using Ave.Documents.Core;
using Ave.Documents.DocumentModel;
using Ave.Documents.Layout;
using Ave.Documents.TestSupport;

namespace UnitTests.Ave.Documents.Layout;

/// <summary>
/// Documented limits of table layout (docs/architecture.md, "Long tables"), pinned so that a change to them is a
/// decision: the test fails, and the limit's documentation changes with it.
/// </summary>
public class TableLimitTests
{
    private static IEnumerable<(BoxFragment Box, Point At)> Boxes(Fragment fragment, Point at, SemanticRole role) =>
        fragment is BoxFragment box
            ? (box.Box.Role == role ? [(box, at)] : Enumerable.Empty<(BoxFragment, Point)>())
                .Concat(box.Children.SelectMany(child => Boxes(child.Fragment, at + child.Offset, role)))
            : [];

    [Fact]
    public void An_auto_column_keeps_the_width_measured_on_the_first_page_so_a_longer_page_number_overflows()
    {
        // A table's columns are computed once per layout pass, on the page where the table starts, and handed from page
        // to page: so an auto column sized for "p1" stays that wide on page 10, where "p10" is wider than the cell.
        var t = new TableModels();
        Box NumberCell()
        {
            var paragraph = new Box(t.Next(), SemanticRole.Paragraph, LayoutAlgorithmId.Inline, BoxStyle.Initial,
                [new TextRun(t.Next(), "p", Models.Text(), Models.At(0)), new CounterRun(t.Next(), PageCounter.PageNumber, Models.Text(), Models.At(0))], Models.At(0));
            return new Box(t.Next(), SemanticRole.TableCell, LayoutAlgorithmId.Flow, TableModels.ReferenceCell, [paragraph], Models.At(0));
        }

        var table = t.Table([TrackSize.Auto.Instance, new TrackSize.Fill(1)], t.Body([.. Enumerable.Range(0, 60).Select(i => t.Row(NumberCell(), t.Cell($"row {i}")))]));
        var pages = Paginator.Paginate(LayoutEnvironment.Create(TestFonts.Set), TableModels.Page(280, 100, table));
        pages.IsSuccess.Should().BeTrue(pages.IsFailure ? pages.Error.ToString() : string.Empty);

        var numberCells = pages.Value.Select(page => Boxes(page.Root.Fragment, new Point(0, 0), SemanticRole.TableCell).First().Box).ToList();
        numberCells.Count.Should().BeGreaterThanOrEqualTo(10);
        numberCells[9].Size.Width.Should().Be(numberCells[0].Size.Width, "the column keeps page 1's width");
        var line = Boxes(numberCells[9], new Point(0, 0), SemanticRole.Paragraph).Single().Box.Children.Single().Fragment;
        var paragraphWidth = Boxes(numberCells[9], new Point(0, 0), SemanticRole.Paragraph).Single().Box.Size.Width;
        line.Size.Width.Should().BeGreaterThan(paragraphWidth, "\"p10\" overflows the cell sized for \"p1\"");
    }
}
