using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Fluent;
using static Ave.Extensions.Documents.Fluent.Elements;

namespace UnitTests.Extensions.Documents.Fluent;

/// <summary>
/// A table's cell defaults (<see cref="TableElement.CellDefaults"/>) fill every box value a cell does not set itself —
/// the background too, although only padding and border have a modifier. The record is public, so a caller can set a
/// background there with <c>with</c>.
/// </summary>
public class CellDefaultTests
{
    private static readonly Color Pale = new(0xEE, 0xEE, 0xFF);

    [Fact]
    public void A_background_in_the_cell_defaults_fills_every_cell_that_has_none_of_its_own()
    {
        var table = Table(Columns(Fill(1), Fill(1)), [TableRow([Cell("plain"), Cell("own").Background(Color.Black)])]);
        table = table with { CellDefaults = table.CellDefaults with { Background = Pale } };

        var result = DocumentTranslator.ToModel(Document([table]).FontFamily("Lato"));

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        var row = (Box)((Box)((Box)result.Value.Root.Children[0]).Children[0]).Children[0];
        row.Children.Cast<Box>().Select(cell => cell.Style.Background).Should().Equal(Pale, Color.Black);
    }
}
