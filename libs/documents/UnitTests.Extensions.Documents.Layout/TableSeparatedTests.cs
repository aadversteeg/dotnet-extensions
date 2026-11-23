using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace UnitTests.Extensions.Documents.Layout;

/// <summary>
/// A table in the separated borders model (<see cref="TableBorderModel.Separate"/>, CSS 2.1 §17.6.1) against Chromium's
/// print of the same tables (cases <c>ts-*</c> in <c>tools/reference/chromium/cases</c>): Lato 10.5 pt, reference cells
/// (<c>border: 0.75pt solid; padding: 2pt 4pt</c>) unless a test says otherwise. Each cell paints its own border, the
/// table its own border, padding and background, and <see cref="BoxStyle.BorderSpacing"/> lies between the cells and
/// between the outer cells and the table's padding edge. Widths are compared with <see cref="ChromiumLayout"/>, within n
/// layout units counted from the case's structure; the spacing, borders and padding are exact in both.
/// </summary>
public class TableSeparatedTests
{
    private const double ExactSize = 10.5;

    private static readonly ValueList<TrackSize> TwoAutos = [TrackSize.Auto.Instance, TrackSize.Auto.Instance];

    private static Result<ValueList<PageFragment>, Error> Layout(StyledDocument document) =>
        Paginator.Paginate(LayoutEnvironment.Create(TestFonts.Set), document);

    private static PageFragment Page(StyledDocument document)
    {
        var result = Layout(document);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        return result.Value.Single();
    }

    /// <summary>Every box fragment of a role, with its top-left corner on the page.</summary>
    private static IEnumerable<(BoxFragment Box, Point At)> Boxes(Fragment fragment, Point at, SemanticRole role) =>
        fragment is BoxFragment box
            ? (box.Box.Role == role ? [(box, at)] : Enumerable.Empty<(BoxFragment, Point)>())
                .Concat(box.Children.SelectMany(child => Boxes(child.Fragment, at + child.Offset, role)))
            : [];

    private static (BoxFragment Box, Point At) Table(PageFragment page) => Boxes(page.Root.Fragment, page.Root.Offset, SemanticRole.Table).Single();

    private static List<(BoxFragment Box, Point At)> Cells(PageFragment page) => [.. Boxes(page.Root.Fragment, page.Root.Offset, SemanticRole.TableCell)];

    private static Box Separated(Box table, double horizontal, double vertical, Border border, Edges padding = default, Color? background = null) =>
        table with
        {
            Style = table.Style with
            {
                BorderModel = TableBorderModel.Separate,
                BorderSpacing = new BorderSpacing(horizontal.Pt(), vertical.Pt()),
                Border = border,
                Padding = padding,
                Background = background,
            },
        };

    private static Border Red(double width) => Border.All(new BorderSide(width.Pt(), Color.Black));

    /// <summary>The two-by-two table of cases <c>ts-spacing-one</c> and <c>ts-spacing-two</c>.</summary>
    private static StyledDocument TwoByTwo(double horizontal, double vertical)
    {
        var t = new TableModels(ExactSize);
        var table = t.Table(TwoAutos, t.Body(t.Row(t.Cell("one"), t.Cell("two")), t.Row(t.Cell("three"), t.Cell("four"))));
        return TableModels.Page(276, 216, Separated(table, horizontal, vertical, Red(1.5)));
    }

    /// <summary>
    /// The spacing lies around the outer cells and between the cells, on both axes: cases <c>ts-spacing-one</c>
    /// (<c>border-spacing: 6pt</c>: the first cell 7.5 pt right of and below the table's edge, 1.5 pt of border and 6 pt of
    /// spacing; the second column 6 pt after the first; the table 83.449 pt wide over columns of 33.820 and 28.629 pt, 21 pt
    /// more) and <c>ts-spacing-two</c> (<c>3pt 9pt</c>: 3 pt between and beside the columns, 9 pt between and around the
    /// rows).
    /// </summary>
    [Theory]
    [InlineData(6.0, 6.0)]
    [InlineData(3.0, 9.0)]
    public void The_spacing_lies_between_the_cells_and_between_the_outer_cells_and_the_table_s_border(double horizontal, double vertical)
    {
        var page = Page(TwoByTwo(horizontal, vertical));
        var (table, at) = Table(page);
        var cells = Cells(page);

        (cells[0].At.X - at.X).Should().BeApproximately(1.5 + horizontal, 1e-9, "one border and one spacing left of the first column");
        (cells[0].At.Y - at.Y).Should().BeApproximately(1.5 + vertical, 1e-9, "one border and one spacing above the first row");
        (cells[1].At.X - (cells[0].At.X + cells[0].Box.Size.Width)).Should().BeApproximately(horizontal, 1e-9, "one spacing between the columns");
        (cells[2].At.Y - (cells[0].At.Y + cells[0].Box.Size.Height)).Should().BeApproximately(vertical, 1e-9, "one spacing between the rows");
        (table.Size.Width - cells[0].Box.Size.Width - cells[1].Box.Size.Width).Should().BeApproximately((2 * 1.5) + (3 * horizontal), 1e-9, "two borders and three spacings beside two columns");
        (table.Size.Height - cells[0].Box.Size.Height - cells[2].Box.Size.Height).Should().BeApproximately((2 * 1.5) + (3 * vertical), 1e-9, "two borders and three spacings beside two rows");

        // n: each column is its widest cell's text, ceiled to a layout unit, plus padding and border (exact): 1 each.
        ChromiumLayout.DifferenceInLayoutUnits(cells[0].Box.Size.Width, 33.820).Should().BeLessThanOrEqualTo(1 + ChromiumLayout.FloatSlack);
        ChromiumLayout.DifferenceInLayoutUnits(cells[1].Box.Size.Width, 28.629).Should().BeLessThanOrEqualTo(1 + ChromiumLayout.FloatSlack);
    }

    /// <summary>
    /// A cell over two columns covers the spacing between them, and gives the columns its width less that spacing: case
    /// <c>ts-span</c>, table <c>t</c> (<c>border-spacing: 6pt</c>): the spanning cell 139.969 pt wide, its columns 66.328 and
    /// 67.641 pt (139.969 = 66.328 + 6 + 67.641), grown from their own cells' widths in proportion to them. A cell over two
    /// rows covers the spacing between them: table <c>u</c>, 42.469 pt over two rows of 18.234 pt and 6 pt.
    /// </summary>
    [Fact]
    public void A_spanning_cell_covers_the_spacing_between_its_columns_and_its_rows()
    {
        var t = new TableModels(ExactSize);
        var wide = t.Table(TwoAutos, t.Body(
            t.Row(t.Cell("Spanning both columns here", TableModels.ReferenceCell with { Span = new CellSpan(2, 1) })),
            t.Row(t.Cell("one"), t.Cell("two"))));
        var tall = t.Table(TwoAutos, t.Body(
            t.Row(t.Cell("one", TableModels.ReferenceCell with { Span = new CellSpan(1, 2) }), t.Cell("two")),
            t.Row(t.Cell("three"))));
        var page = Page(TableModels.Page(276, 216, Separated(wide, 6, 6, Red(1.5)), Separated(tall, 6, 6, Red(1.5))));
        var cells = Cells(page);

        cells[0].Box.Size.Width.Should().BeApproximately(cells[1].Box.Size.Width + 6 + cells[2].Box.Size.Width, 1e-9, "the spanning cell covers the spacing between its columns");
        // n: the spanning cell's text ceiled (1), and each column's share of it cut to a layout unit (1).
        ChromiumLayout.DifferenceInLayoutUnits(cells[1].Box.Size.Width, 66.328).Should().BeLessThanOrEqualTo(2 + ChromiumLayout.FloatSlack);
        ChromiumLayout.DifferenceInLayoutUnits(cells[2].Box.Size.Width, 67.641).Should().BeLessThanOrEqualTo(2 + ChromiumLayout.FloatSlack);
        cells[3].Box.Size.Height.Should().BeApproximately(cells[4].Box.Size.Height + 6 + cells[5].Box.Size.Height, 1e-9, "the cell over two rows covers the spacing between them");
    }

    /// <summary>
    /// Each cell keeps and paints its own border, and the table its own: case <c>ts-paint</c> (table border 3 pt, padding
    /// 3 pt, spacing 6 pt, background; cells 1.5 pt): a cell without a border takes no room for one, its text 4 pt (its
    /// padding) inside its box, which starts 12 pt inside the table (border, padding, spacing); a cell with a 6 pt border
    /// on every side makes its column as wide as its own border box (44.320 pt beside the borderless cell's 44.320) and its
    /// row 9 pt taller than the row of 1.5 pt borders (28.734 against 19.734). The fragments paint those borders: the
    /// table's border and each cell's own, no grid lines.
    /// </summary>
    [Fact]
    public void Each_cell_paints_its_own_border_and_the_table_its_own_border_and_background()
    {
        var t = new TableModels(ExactSize);
        var blue = TableModels.ReferenceCell with { Border = Border.All(new BorderSide(1.5.Pt(), Color.Black)) };
        var table = t.Table(TwoAutos, t.Body(
            t.Row(t.Cell("one", blue with { Border = Border.None }), t.Cell("two", blue)),
            t.Row(t.Cell("three", blue with { Border = Border.All(new BorderSide(6.Pt(), Color.Black)) }), t.Cell("four", blue))));
        var page = Page(TableModels.Page(276, 216, Separated(table, 6, 6, Red(3), Edges.All(3.Pt()), Color.Black)));
        var (fragment, at) = Table(page);
        var cells = Cells(page);

        fragment.Border.Should().Be(Red(3), "the table paints its own border");
        fragment.Children.Select(child => child.Fragment).OfType<RuleFragment>().Should().BeEmpty("no grid lines");
        cells.Select(cell => cell.Box.Border.Widths.Top.Points).Should().Equal([0, 1.5, 6, 1.5], "each cell paints its own border");
        (cells[0].At.X - at.X).Should().BeApproximately(3 + 3 + 6, 1e-9, "border, padding and spacing left of the first column");
        cells[0].Box.Size.Width.Should().BeApproximately(cells[2].Box.Size.Width, 1e-9, "one column");
        (cells[2].Box.Size.Height - cells[0].Box.Size.Height).Should().BeApproximately(2 * (6 - 1.5), 1e-9, "the 6 pt border heightens its own row");
        // n: "three" ceiled (1); padding and borders exact.
        ChromiumLayout.DifferenceInLayoutUnits(cells[2].Box.Size.Width, 44.320).Should().BeLessThanOrEqualTo(1 + ChromiumLayout.FloatSlack);
    }

    /// <summary>
    /// A table with separated borders is laid out on one page only in this version: one that does not fit whole in the
    /// room it has is refused with <see cref="LayoutErrorCodes.TableBreak"/> at its source line, where Chromium breaks it
    /// (CSS Fragmentation 3); the same table with collapsed borders breaks.
    /// </summary>
    [Fact]
    public void A_table_with_separated_borders_that_does_not_fit_its_page_is_refused()
    {
        var t = new TableModels(ExactSize);
        var table = t.Table(TwoAutos, t.Body([.. Enumerable.Range(0, 12).Select(row => t.Row(t.Cell($"row {row}"), t.Cell("two")))]));

        var refused = Layout(TableModels.Page(276, 120, Separated(table, 6, 6, Red(1.5))));
        refused.IsFailure.Should().BeTrue();
        refused.Error.Code.Should().Be(LayoutErrorCodes.TableBreak);
        refused.Error.Metadata![ErrorMetadataKeys.SourceLine].Should().Be(table.Source.Line);

        Layout(TableModels.Page(276, 120, table)).Value.Should().HaveCountGreaterThan(1, "with collapsed borders the table breaks");
    }

    /// <summary>The model's check: a border spacing is not negative (CSS 2.1 §17.6.1: "Negative values are not
    /// allowed"), and the text form names the model and the spacing.</summary>
    [Fact]
    public void A_negative_border_spacing_is_refused_and_the_text_form_names_the_model()
    {
        var t = new TableModels(ExactSize);
        var table = t.Table(TwoAutos, t.Body(t.Row(t.Cell("one"), t.Cell("two"))));

        ModelCheck.Check(TableModels.Page(276, 216, Separated(table, -1, 0, Border.None))).Error.Code.Should().Be(DocumentErrorCodes.Input.Invalid.Length);
        var text = DocumentModelText.Write(TableModels.Page(276, 216, Separated(table, 3, 9, Border.None)));
        text.Should().Contain("border-collapse=separate").And.Contain("border-spacing=3pt 9pt");
    }
}
