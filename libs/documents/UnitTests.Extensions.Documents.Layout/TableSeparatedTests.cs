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

    /// <summary>The pages of a document that lays out.</summary>
    private static ValueList<PageFragment> Pages(StyledDocument document)
    {
        var result = Layout(document);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        return result.Value;
    }

    /// <summary>The table of the <c>ts-break-*</c> cases: spacing 6 pt, padding 3 pt, border 3 pt, a background.</summary>
    private static Box Broken(Box table) => Separated(table, 6, 6, Red(3), Edges.All(3.Pt()), Color.Black);

    private static Box[] Rows(TableModels t, int from, int to) =>
        [.. Enumerable.Range(from, to - from + 1).Select(row => t.Row(t.Cell($"row {row}"), t.Cell($"text {row}")))];

    /// <summary>
    /// At a break between rows the table is sliced as a block box is, and the next page starts with its row: case
    /// <c>ts-break-rows</c> (a page area of 136 pt, eight rows): five rows on page 1, where the table's fragment runs to
    /// the page end with its side borders and background and without its bottom border (Chromium's background and side
    /// borders reach 147.996 pt, the page end); on page 2 the first row's cell starts at the fragment's top, with no
    /// border, padding or spacing above it (its top border at 12 pt, the page area's top), and the fragment ends 12 pt
    /// below the last row: spacing, padding and the bottom border.
    /// </summary>
    [Fact]
    public void At_a_break_between_rows_the_table_is_sliced_and_the_next_page_starts_with_its_row()
    {
        var t = new TableModels(ExactSize);
        var pages = Pages(TableModels.Page(276, 136, Broken(t.Table(TwoAutos, t.Body(Rows(t, 1, 8))))));

        pages.Should().HaveCount(2);
        Cells(pages[0]).Should().HaveCount(10, "five rows on page 1");
        Cells(pages[1]).Should().HaveCount(6, "three rows on page 2");
        var (first, _) = Table(pages[0]);
        first.Size.Height.Should().BeApproximately(136, 1e-9, "the fragment that breaks runs to the page end");
        first.Border.Bottom.Should().Be(BorderSide.None, "no bottom border at the break");
        first.Border.Top.Width.Points.Should().Be(3);
        var (second, at) = Table(pages[1]);
        var cells = Cells(pages[1]);
        (cells[0].At.Y - at.Y).Should().BeApproximately(0, 1e-9, "no border, padding or spacing above the first row of a later page");
        second.Border.Top.Should().Be(BorderSide.None, "no top border after the break");
        second.Border.Bottom.Width.Points.Should().Be(3);
        (at.Y + second.Size.Height - (cells[^1].At.Y + cells[^1].Box.Size.Height)).Should().BeApproximately(6 + 3 + 3, 1e-9, "spacing, padding and border below the last row");
    }

    /// <summary>
    /// A repeated header row starts a later page's fragment, and one spacing lies between it and the first body row:
    /// case <c>ts-break-header</c> (a header row and twelve rows, a page area of 136 pt): four body rows on each of three
    /// pages; on pages 2 and 3 the header's top border at 12 pt, the page area's top, and the first body row's 6 pt below
    /// the header (Chromium: the header's bottom border ends at 30, the next row's top border starts at 36).
    /// </summary>
    [Fact]
    public void A_repeated_header_row_starts_a_later_page_and_one_spacing_lies_below_it()
    {
        var t = new TableModels(ExactSize);
        var table = t.Table(TwoAutos, t.Head(t.Row(t.Cell("head"), t.Cell("other"))), t.Body(Rows(t, 1, 12)));
        var pages = Pages(TableModels.Page(276, 136, Broken(table)));

        pages.Select(page => Cells(page).Count).Should().Equal([10, 10, 10], "the header and four body rows on each page");
        foreach (var page in pages.Skip(1))
        {
            var (_, at) = Table(page);
            var cells = Cells(page);
            (cells[0].At.Y - at.Y).Should().BeApproximately(0, 1e-9, "the repeated header at the fragment's top");
            (cells[2].At.Y - (cells[0].At.Y + cells[0].Box.Size.Height)).Should().BeApproximately(6, 1e-9, "one spacing below the header");
        }
    }

    /// <summary>
    /// The table's last row fits only with the spacing, padding and border below it: case <c>ts-break-end</c> (five rows,
    /// a page area of 128.004 pt, in which the fifth row ends at 127.17 pt and its 12 pt below do not fit): Chromium moves
    /// the fifth row to page 2.
    /// </summary>
    [Fact]
    public void A_last_row_that_fits_only_without_the_spacing_padding_and_border_below_it_moves_to_the_next_page()
    {
        var t = new TableModels(ExactSize);
        var pages = Pages(TableModels.Page(276, 128, Broken(t.Table(TwoAutos, t.Body(Rows(t, 1, 5))))));

        pages.Select(page => Cells(page).Count).Should().Equal([8, 2]);
    }

    /// <summary>
    /// A row cut inside itself slices each of its cells as a block box is sliced, and a cell whose content ended above
    /// the cut goes on, empty, as tall as the row: case <c>ts-break-cut</c> (a cell of ten lines beside "short"): on page 1
    /// both cells paint their top border and no bottom border, their sides down to the page end; on page 2 both start at
    /// the fragment's top without a top border and end with their bottom border, equally tall (Chromium: both bottom
    /// borders at 52.5 to 53.25 pt).
    /// </summary>
    [Fact]
    public void A_cut_row_slices_its_cells_and_the_cell_that_ended_is_closed_on_the_next_page()
    {
        var t = new TableModels(ExactSize);
        var lines = t.Lines([.. Enumerable.Range(1, 10).Select(line => $"line {line}")]);
        var table = t.Table(TwoAutos, t.Body(t.Row(t.Cell("one"), t.Cell("text 1")), t.Row(lines, t.Cell("short")), t.Row(t.Cell("three"), t.Cell("text 3"))));
        var pages = Pages(TableModels.Page(276, 136, Broken(table)));

        pages.Should().HaveCount(2);
        var before = Cells(pages[0]).Skip(2).ToList();
        before.Select(cell => cell.Box.Border.Bottom).Should().Equal([BorderSide.None, BorderSide.None], "no bottom border at the cut");
        before.Select(cell => cell.Box.Border.Top.Width.Points).Should().Equal([0.75, 0.75]);
        var (_, at) = Table(pages[1]);
        var after = Cells(pages[1]).Take(2).ToList();
        after.Select(cell => cell.At.Y - at.Y).Should().Equal([0.0, 0.0], "the cut row goes on at the fragment's top");
        after.Select(cell => cell.Box.Border.Top).Should().Equal([BorderSide.None, BorderSide.None], "no top border after the cut");
        after.Select(cell => cell.Box.Border.Bottom.Width.Points).Should().Equal([0.75, 0.75], "each closed on the page where it ends");
        after[1].Box.Size.Height.Should().BeApproximately(after[0].Box.Size.Height, 1e-9, "the cell that ended is as tall as the row");
    }

    /// <summary>
    /// The cells of a cut row start at the top of their part whatever their alignment, as in the collapsing model
    /// (<see cref="CutRowAlignment.TopOnEveryPage"/>, the HTML front-end's): cases <c>ts-break-cut-middle</c> (every cell at
    /// the user agent's <c>middle</c>) and <c>ts-break-cut-bottom</c> (the one-line cell at the bottom): beside the cell of
    /// ten lines, "short" and the three lines of "mid" start on the cut row's first line (Chromium: all three at 61.5 pt on
    /// page 1), where the same row on one page would put them in its middle or at its bottom.
    /// </summary>
    [Theory]
    [InlineData(VerticalAlign.Middle, VerticalAlign.Middle)]
    [InlineData(VerticalAlign.Bottom, VerticalAlign.Middle)]
    public void The_cells_of_a_cut_row_start_at_the_top_of_their_part_whatever_their_alignment(VerticalAlign shortCell, VerticalAlign others)
    {
        var t = new TableModels(ExactSize);
        Box Aligned(Box cell, VerticalAlign align) => cell with { Style = cell.Style with { VerticalAlign = align } };
        Box Cell(string text) => Aligned(t.Cell(text), others);
        var lines = t.Lines([.. Enumerable.Range(1, 10).Select(line => $"line {line}")]);
        var cut = t.Row(Aligned(lines, others), Aligned(t.Cell("short"), shortCell), Aligned(t.Lines(["mid", "mid 2", "mid 3"]), others));
        var table = t.Table([TrackSize.Auto.Instance, TrackSize.Auto.Instance, TrackSize.Auto.Instance], t.Body(
            t.Row(Cell("one"), Cell("text 1"), Cell("more 1")), cut, t.Row(Cell("three"), Cell("text 3"), Cell("more 3"))));
        var document = TableModels.Page(276, 136, Broken(table)) with { Profile = LayoutProfile.Default with { CutRowAlignment = CutRowAlignment.TopOnEveryPage } };

        var pages = Pages(document);

        pages.Should().HaveCount(2);
        var row = Cells(pages[0]).Skip(3).ToList();
        row.Select(cell => cell.Box.Children[0].Offset.Y).Should().Equal(
            Enumerable.Repeat(row[0].Box.Children[0].Offset.Y, 3), "every cell of the cut row starts at its top");
        Layout(TableModels.Page(276, 136, Broken(table))).Error.Code.Should().Be(LayoutErrorCodes.TableBreak, "not measured under RestAlignedInItsPart");
    }

    /// <summary>
    /// Where no measured case says how Chromium breaks a table with separated borders, the table is refused with
    /// <see cref="LayoutErrorCodes.TableBreak"/> at its source line: a cell over three rows across the break (case
    /// <c>ts-break-span</c>: Chromium starts the next row 6 pt below the page's top while the spanning cell's rest ends
    /// at 27 pt), a cut row with a cell aligned at the middle under <see cref="CutRowAlignment.RestAlignedInItsPart"/>, and a
    /// table that clones its decoration at a break. The
    /// same tables with collapsed borders break, on a smaller page.
    /// </summary>
    [Fact]
    public void A_break_that_no_measured_case_shows_is_refused()
    {
        var t = new TableModels(ExactSize);
        var spanning = t.Table(TwoAutos, t.Body([
            .. Rows(t, 1, 3),
            t.Row(t.Cell("span", TableModels.ReferenceCell with { Span = new CellSpan(1, 3) }), t.Cell("text 4")),
            t.Row(t.Cell("text 5")),
            t.Row(t.Cell("text 6")),
            .. Rows(t, 7, 7)]));
        var lines = t.Lines([.. Enumerable.Range(1, 10).Select(line => $"line {line}")]);
        var middle = t.Table(TwoAutos, t.Body(t.Row(t.Cell("one"), t.Cell("text 1")), t.Row(lines, t.Cell("short", TableModels.ReferenceCell with { VerticalAlign = VerticalAlign.Middle }))));
        var rows = t.Table(TwoAutos, t.Body(Rows(t, 1, 8)));
        var cloned = rows with { Style = rows.Style with { DecorationBreak = BoxDecorationBreak.Clone } };

        foreach (var table in new[] { spanning, middle, cloned })
        {
            var refused = Layout(TableModels.Page(276, 136, Broken(table)));
            refused.IsFailure.Should().BeTrue();
            refused.Error.Code.Should().Be(LayoutErrorCodes.TableBreak);
            refused.Error.Metadata![ErrorMetadataKeys.SourceLine].Should().Be(table.Source.Line);
            Layout(TableModels.Page(276, 60, table)).IsSuccess.Should().BeTrue("with collapsed borders the table breaks");
        }
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
