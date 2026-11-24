using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;

namespace UnitTests.Extensions.Documents.Layout;

/// <summary>
/// Widths of tables and cells as lengths (<see cref="BoxWidth.Fixed"/>, CSS 2.1 §17.5.2) against Chromium's print of the
/// same tables (cases <c>twd-*</c> in <c>tools/reference/chromium/cases</c>): Lato 10.5 pt, reference cells
/// (<c>border: 0.75pt solid; padding: 2pt 4pt</c>), "one" beside "text with more words". Widths are compared with
/// <see cref="ChromiumLayout"/>, within n layout units: Chromium floors every length to a layout unit (a width of 80 pt
/// is 79.992 pt, a padding of 4 pt 3.996 pt), so a cell's width, its two paddings and its text's width each give up to
/// one, and a table's width one.
/// </summary>
public class TableLengthTests
{
    private const double ExactSize = 10.5;

    private const double Units = 4 + ChromiumLayout.FloatSlack;

    private static readonly ValueList<TrackSize> TwoAutos = [TrackSize.Auto.Instance, TrackSize.Auto.Instance];

    private static readonly ValueList<TrackSize> TwoFills = [new TrackSize.Fill(1), new TrackSize.Fill(1)];

    private static PageFragment Page(Box table)
    {
        var result = Paginator.Paginate(LayoutEnvironment.Create(TestFonts.Set), TableModels.Page(276, 360, table));
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        return result.Value.Single();
    }

    private static IEnumerable<BoxFragment> Boxes(Fragment fragment, SemanticRole role) =>
        fragment is BoxFragment box
            ? (box.Box.Role == role ? [box] : Enumerable.Empty<BoxFragment>()).Concat(box.Children.SelectMany(child => Boxes(child.Fragment, role)))
            : [];

    private static double TableWidth(PageFragment page) => Boxes(page.Root.Fragment, SemanticRole.Table).Single().Size.Width;

    private static double[] CellWidths(PageFragment page) => [.. Boxes(page.Root.Fragment, SemanticRole.TableCell).Select(cell => cell.Size.Width)];

    private static Box Wide(Box table, double width) => table with { Style = table.Style with { Width = new BoxWidth.Fixed(width.Pt()) } };

    private static Box Separated(Box table) =>
        table with
        {
            Style = table.Style with
            {
                BorderModel = TableBorderModel.Separate,
                BorderSpacing = new BorderSpacing(3.Pt(), 3.Pt()),
                Border = Border.All(new BorderSide(1.5.Pt(), Color.Black)),
                Padding = Edges.All(1.5.Pt()),
            },
        };

    private static Box OneRow(TableModels t, ValueList<TrackSize> columns, double? first = null) =>
        t.Table(columns, t.Body(t.Row(t.Cell("one", Sized(first)), t.Cell("text with more words"))));

    private static BoxStyle Sized(double? width) =>
        width is { } given ? TableModels.ReferenceCell with { Width = new BoxWidth.Fixed(given.Pt()) } : TableModels.ReferenceCell;

    private static void ShouldBe(double[] engine, params double[] chromium) =>
        engine.Zip(chromium).Should().OnlyContain(pair => ChromiumLayout.DifferenceInLayoutUnits(pair.First, pair.Second) <= Units, $"{string.Join(", ", engine)} against {string.Join(", ", chromium)}");

    /// <summary>
    /// A table of a given width is that wide, and its columns share the width less the table's outer insets as at
    /// <see cref="BoxWidth.Stretch"/>: case <c>twd-table-wide</c>, <c>width: 200pt</c> (Chromium 199.992): automatic
    /// layout, collapsed (a: 39.105 and 160.137 pt, in proportion to the max-content widths) and separated (c: 36.938 and
    /// 148.055 pt); fixed layout, equal shares (b: 99.621 pt each; d: 92.496 pt each, less 1.5 pt of border, 1.5 pt of
    /// padding and three spacings of 3 pt).
    /// </summary>
    [Fact]
    public void A_table_of_a_given_width_shares_that_width_over_its_columns()
    {
        var t = new TableModels(ExactSize);
        var cases = new (Box Table, double[] Chromium)[]
        {
            (OneRow(t, TwoAutos), [39.105, 160.137]),
            (OneRow(t, TwoFills), [99.621, 99.621]),
            (Separated(OneRow(t, TwoAutos)), [36.938, 148.055]),
            (Separated(OneRow(t, TwoFills)), [92.496, 92.496]),
        };

        foreach (var (table, chromium) in cases)
        {
            var page = Page(Wide(table, 200));
            TableWidth(page).Should().BeApproximately(200, 1e-9);
            ShouldBe(CellWidths(page), chromium);
        }
    }

    /// <summary>
    /// A table of a given width is never narrower than its columns need: case <c>twd-table-narrow</c>, <c>width: 40pt</c>
    /// in the automatic layout, collapsed (a: 63.773 pt) and separated (c: 79.523 pt), the content's min-content; case
    /// <c>twd-sum</c> c, <c>width: 150pt</c> in the fixed layout over two first-row cells of <c>width: 100pt</c>: 218.227 pt,
    /// the two fixed columns of 108.738 pt and the outer half lines.
    /// </summary>
    [Fact]
    public void A_table_of_a_given_width_is_never_narrower_than_its_columns_need()
    {
        var t = new TableModels(ExactSize);
        ShouldBe([TableWidth(Page(Wide(OneRow(t, TwoAutos), 40)))], 63.773);
        ShouldBe([TableWidth(Page(Wide(Separated(OneRow(t, TwoAutos)), 40)))], 79.523);
        var sum = t.Table(TwoFills, t.Body(t.Row(t.Cell("one", Sized(100)), t.Cell("text with more words", Sized(100)))));
        var page = Page(Wide(sum, 150));
        ShouldBe([TableWidth(page)], 218.227);
        ShouldBe(CellWidths(page), 108.738, 108.738);
    }

    /// <summary>
    /// A cell's width is of its content box, and in the automatic layout every row's counts, the widest in a column: case
    /// <c>twd-cell-row</c>, <c>width: 80pt</c> in the first row (a, collapsed: 88.734 pt, the width, 8 pt of padding and
    /// half of each 0.75 pt line) or the second (b, separated: 89.484 pt, with its own borders); case <c>twd-cell-two</c> a,
    /// 60 pt then 90 pt in one column: 98.742 pt; case <c>twd-cell-col</c> a, a column element of 60 pt beside a cell of
    /// 90 pt: 98.742 pt, and c, separated, a column element of 90 pt beside a cell of 60 pt: 90 pt, the column element's
    /// length, a border box.
    /// </summary>
    [Fact]
    public void In_the_automatic_layout_the_widest_cell_width_of_a_column_counts_in_every_row()
    {
        var t = new TableModels(ExactSize);
        Box TwoRows(ValueList<TrackSize> columns, double? first, double? second) => t.Table(columns, t.Body(
            t.Row(t.Cell("one", Sized(first)), t.Cell("text with more words")),
            t.Row(t.Cell("two", Sized(second)), t.Cell("text with more words"))));

        CellWidths(Page(TwoRows(TwoAutos, 80, null)))[0].Should().BeApproximately(88.75, 1e-9);
        ShouldBe([CellWidths(Page(TwoRows(TwoAutos, 80, null)))[0]], 88.734);
        ShouldBe([CellWidths(Page(Separated(TwoRows(TwoAutos, null, 80))))[0]], 89.484);
        ShouldBe([CellWidths(Page(TwoRows(TwoAutos, 60, 90)))[0]], 98.742);
        ShouldBe([CellWidths(Page(TwoRows([new TrackSize.Preferred(60.Pt()), TrackSize.Auto.Instance], null, 90)))[0]], 98.742);
        ShouldBe([CellWidths(Page(Separated(TwoRows([new TrackSize.Preferred(90.Pt()), TrackSize.Auto.Instance], 60, null))))[0]], 90);
    }

    /// <summary>
    /// In the fixed layout only the first row's cell widths count (CSS 2.1 §17.5.2.1), and a column element's length wins:
    /// case <c>twd-cell-row</c> c, <c>width: 80pt</c> in the first row of a 200 pt table: 88.734 and 110.508 pt; d, the
    /// same in the second row of a separated table: not read, both columns 92.496 pt; case <c>twd-cell-two</c> b, 60 pt
    /// then 90 pt, separated: 69.492 pt, the first row's; case <c>twd-cell-col</c> b, a column element of 60 pt beside a
    /// cell of 90 pt, separated: 60 pt.
    /// </summary>
    [Fact]
    public void In_the_fixed_layout_only_the_first_row_s_cell_width_counts()
    {
        var t = new TableModels(ExactSize);
        Box TwoRows(ValueList<TrackSize> columns, double? first, double? second) => Wide(t.Table(columns, t.Body(
            t.Row(t.Cell("one", Sized(first)), t.Cell("text with more words")),
            t.Row(t.Cell("two", Sized(second)), t.Cell("text with more words")))), 200);

        ShouldBe(CellWidths(Page(TwoRows(TwoFills, 80, null))).Take(2).ToArray(), 88.734, 110.508);
        ShouldBe(CellWidths(Page(Separated(TwoRows(TwoFills, null, 80)))).Take(2).ToArray(), 92.496, 92.496);
        ShouldBe([CellWidths(Page(Separated(TwoRows(TwoFills, 60, 90))))[0]], 69.492);
        ShouldBe([CellWidths(Page(Separated(TwoRows([new TrackSize.Fixed(60.Pt()), new TrackSize.Fill(1)], 90, null))))[0]], 60);
    }

    /// <summary>
    /// A cell over two columns with a width asks it of them in the automatic layout, as its maximum: case
    /// <c>twd-cell-span</c> a, <c>width: 150pt</c> over "one" and "two": the cell 158.742 pt, its columns 78.574 and
    /// 80.168 pt, shared in proportion to their own maximums.
    /// </summary>
    [Fact]
    public void A_cell_over_two_columns_asks_its_width_of_them()
    {
        var t = new TableModels(ExactSize);
        var table = t.Table(TwoAutos, t.Body(
            t.Row(t.Cell("span", Sized(150) with { Span = new CellSpan(2, 1) })),
            t.Row(t.Cell("one"), t.Cell("two"))));

        ShouldBe(CellWidths(Page(table)), 158.742, 78.574, 80.168);
    }

    /// <summary>The model's check: a width is not negative (CSS 2.1 §10.2), and the text form names it.</summary>
    [Fact]
    public void A_negative_width_is_refused_and_the_text_form_names_the_width()
    {
        var t = new TableModels(ExactSize);
        ModelCheck.Check(TableModels.Page(276, 360, Wide(OneRow(t, TwoAutos), -1))).Error.Code.Should().Be(DocumentErrorCodes.Input.Invalid.Length);
        ModelCheck.Check(TableModels.Page(276, 360, OneRow(t, TwoAutos, -1))).Error.Code.Should().Be(DocumentErrorCodes.Input.Invalid.Length);
        DocumentModelText.Write(TableModels.Page(276, 360, Wide(OneRow(t, TwoAutos, 80), 200))).Should().Contain("width=200pt").And.Contain("width=80pt");
    }
}
