using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;

namespace UnitTests.Extensions.Documents.Layout;

/// <summary>
/// A table as wide as the width it is given (<see cref="BoxWidth.Stretch"/>) against Chromium's layout of the same
/// tables with <c>width: 100%</c> (cases <c>tw-auto</c>, <c>tw-col</c>, <c>tw-span</c>, <c>tw-insets</c>, <c>tw-fixed</c> in
/// <c>tools/reference/chromium/cases</c>): a 300pt (400px) container, collapsed borders, Lato 10.5pt (14px, a size Chromium
/// keeps exact), reference cells (<c>border: 0.75pt solid; padding: 2pt 4pt</c>) unless a case says otherwise.
/// The width left over the columns' maximums goes to the auto columns in proportion to their maximums, or to the fixed
/// columns in proportion to their widths when there is no auto column (<c>TrackSizing.GrowPastMaximum</c>).
/// Widths are compared with <see cref="ChromiumLayout"/>, within n layout units counted from the case's structure (see
/// each check).
/// </summary>
public class TableWidthReferenceTests
{
    private const double ExactSize = 10.5;

    private static ValueList<PageFragment> Paginate(StyledDocument document)
    {
        var result = Paginator.Paginate(LayoutEnvironment.Create(TestFonts.Set), document);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        return result.Value;
    }

    private static IEnumerable<BoxFragment> Boxes(Fragment fragment, SemanticRole role) =>
        fragment is BoxFragment box
            ? (box.Box.Role == role ? [box] : Enumerable.Empty<BoxFragment>()).Concat(box.Children.SelectMany(child => Boxes(child.Fragment, role)))
            : [];

    private static double[] CellWidths(PageFragment page) => [.. Boxes(page.Root.Fragment, SemanticRole.TableCell).Select(cell => cell.Size.Width)];

    private static double TableWidth(PageFragment page) => Boxes(page.Root.Fragment, SemanticRole.Table).Single().Size.Width;

    private static Box Stretched(Box table) => table with { Style = table.Style with { Width = BoxWidth.Stretch } };

    private static StyledDocument Case(TableModels t, ValueList<TrackSize> columns, params Box[] cells) =>
        TableModels.Page(300, 400, Stretched(t.Table(columns, t.Body(t.Row(cells)))));

    private static ValueList<TrackSize> Autos(int count) => [.. Enumerable.Repeat<TrackSize>(TrackSize.Auto.Instance, count)];

    private static BoxStyle Bare => BoxStyle.Initial;

    /// <summary>The document of one measured table, written in code as Chromium has it in its case file.</summary>
    private static StyledDocument Document(string name)
    {
        var t = new TableModels(ExactSize);
        var cell = TableModels.ReferenceCell;
        return name switch
        {
            "two" => Case(t, Autos(2), t.Cell("short"), t.Cell("a longer text")),
            "three" => Case(t, Autos(3), t.Cell("one"), t.Cell("two words"), t.Cell("three words here")),
            "wrap" => Case(t, Autos(3), t.Cell("alpha beta gamma delta epsilon zeta eta theta"), t.Cell("one two"), t.Cell("iota kappa lambda mu nu xi omicron pi rho")),
            "empty" => Case(t, Autos(3), t.Cell("short"), t.Empty("no children", cell), t.Cell("a longer text")),
            "zero" => Case(t, Autos(3), t.Cell("short", Bare), t.Empty("no children", Bare), t.Cell("a longer text", Bare)),
            "allzero" => Case(t, Autos(3), t.Empty("no children", Bare), t.Empty("no children", Bare), t.Empty("no children", Bare)),
            "col-auto" => Case(t, [new TrackSize.Fixed(60.Pt()), TrackSize.Auto.Instance, TrackSize.Auto.Instance], t.Cell("fixed"), t.Cell("short"), t.Cell("a longer text")),
            "col-all" or "fixed" => Case(t, [new TrackSize.Fixed(60.Pt()), new TrackSize.Fixed(90.Pt())], t.Cell("short"), t.Cell("a longer text")),
            "col-all-three" or "fixed-three" => Case(t, [new TrackSize.Fixed(45.Pt()), new TrackSize.Fixed(60.Pt()), new TrackSize.Fixed(75.Pt())], t.Cell("a"), t.Cell("b"), t.Cell("c")),
            "fixed-zero" => Case(t, [new TrackSize.Fixed(60.Pt()), new TrackSize.Fixed(Length.Zero)], t.Cell("short"), t.Cell("a longer text")),
            "pad" => Case(t, Autos(2),
                t.Cell("short", cell with { Padding = new Edges(1.5.Pt(), 9.Pt(), 1.5.Pt(), 9.Pt()) }),
                t.Cell("a longer text", cell with { Padding = new Edges(1.5.Pt(), 3.Pt(), 1.5.Pt(), 3.Pt()) })),
            "border" => Case(t, Autos(3),
                t.Cell("short", cell with { Border = cell.Border with { Left = new BorderSide(3.Pt(), Color.Black) } }),
                t.Cell("a longer text", cell with { Border = cell.Border with { Right = new BorderSide(6.Pt(), Color.Black) } }),
                t.Cell("mid")),
            "span" or "span-wide" => TableModels.Page(300, 400, Stretched(t.Table(Autos(3), t.Body(
                t.Row(t.Cell(name == "span" ? "a heading over both" : "a much wider heading that spans both columns", cell with { Span = new CellSpan(2, 1) }), t.Cell("third")),
                t.Row(t.Cell("one"), t.Cell(name == "span" ? "two words" : "two"), t.Cell(name == "span" ? "third column" : "x")))))),
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, "no such case"),
        };
    }

    /// <summary>
    /// One measured table, column by column. A column's width is the target times its weight over the weights of the
    /// receiving columns (or its maximum below the target), so it depends on every column's maximum. n counts: one ceiled
    /// text item per column with text (Chromium ceils each item, README "The comparison rule"; paddings are truncated
    /// alike in every column and a 0.75pt border is a whole pixel), the column's own truncated share, and for the last
    /// receiving column the other receivers' truncations too (it takes the remainder). Fixed widths of whole pixels
    /// (45, 60, 75, 90 pt) are exact.
    /// </summary>
    [Theory]
    [InlineData("two", new[] { 98.766, 200.484 }, 2, new[] { true, true })]
    [InlineData("three", new[] { 45.867, 100.617, 152.766 }, 3, new[] { true, true, true })]
    [InlineData("wrap", new[] { 133.980, 37.043, 128.227 }, 6, new[] { true, true, true })]
    [InlineData("empty", new[] { 90.750, 24.258, 184.242 }, 2, new[] { true, true, true })]
    [InlineData("zero", new[] { 88.090, 0.000, 211.910 }, 2, new[] { true, true, true })]
    [InlineData("allzero", new[] { 99.996, 99.996, 100.008 }, 0, new[] { true, true, true })]
    [InlineData("col-auto", new[] { 60.000, 78.961, 160.289 }, 3, new[] { false, true, true })]
    [InlineData("col-all", new[] { 119.695, 179.555 }, 0, new[] { true, true })]
    [InlineData("col-all-three", new[] { 74.813, 99.750, 124.688 }, 0, new[] { true, true, true })]
    [InlineData("pad", new[] { 119.344, 179.906 }, 2, new[] { true, true })]
    [InlineData("border", new[] { 76.840, 156.738, 64.547 }, 3, new[] { true, true, true })]
    [InlineData("fixed", new[] { 119.695, 179.555 }, 0, new[] { true, true })]
    [InlineData("fixed-three", new[] { 74.813, 99.750, 124.688 }, 0, new[] { true, true, true })]
    [InlineData("fixed-zero", new[] { 299.250, 0.000 }, 0, new[] { true, false })]
    public void A_table_as_wide_as_its_container_shares_the_width_left_over_its_columns_like_Chromium(
        string name, double[] chromium, int textItems, bool[] receives)
    {
        // Chromium (tw-auto, tw-col, tw-insets, tw-fixed): the table is 300pt, the container's width (300pt = 400px,
        // nothing snapped), in every case.
        var page = Paginate(Document(name)).Single();

        var cells = CellWidths(page);
        cells.Should().HaveSameCount(chromium);
        var receivers = receives.Count(flag => flag);
        var last = Array.LastIndexOf(receives, true);
        for (var index = 0; index < cells.Length; index++)
        {
            var n = textItems + (receives[index] ? 1 : 0) + (index == last ? receivers - 1 : 0);
            ChromiumLayout.DifferenceInLayoutUnits(cells[index], chromium[index]).Should().BeLessThanOrEqualTo(
                n + ChromiumLayout.FloatSlack,
                $"{name} column {index}: ours {cells[index]} must be within {n} layout units of Chromium's {chromium[index]} "
                + $"({textItems} ceiled text items, {(receives[index] ? "its own truncated share" : "no share")}{(index == last ? $", the {receivers - 1} other truncations" : string.Empty)})");
        }

        TableWidth(page).Should().BeApproximately(300, 1e-9, "the table is as wide as its container");
    }

    /// <summary>
    /// A cell spanning the first two of three auto columns, above a row of three cells (case <c>tw-span</c>). Chromium
    /// shares the spanning cell's widths over the spanned columns with the same distribution as the table's width
    /// (<c>TrackSizing.SpreadOverSpan</c>), not in equal amounts. In <c>span</c> the heading is wider than the two
    /// columns' maximums together, so they grow in proportion to their maximums; in <c>span-wide</c> the heading's maximum
    /// is wider than the table, so the table's width lies between the columns' minimums and maximums and each grows in
    /// proportion to max − min. n: the table's five ceiled text items, the column's own truncated share, and the other
    /// two columns' truncations (a column may take a remainder at two levels: the span's and the table's).
    /// </summary>
    [Theory]
    [InlineData("span", new[] { 179.402, 119.848, 56.156, 123.246, 119.848 })]
    [InlineData("span-wide", new[] { 263.531, 35.719, 130.441, 133.090, 35.719 })]
    public void A_cell_spanning_two_columns_shares_its_width_over_them_like_Chromium(string name, double[] chromium)
    {
        var page = Paginate(Document(name)).Single();

        var cells = CellWidths(page);
        cells.Should().HaveSameCount(chromium);
        for (var index = 0; index < cells.Length; index++)
        {
            // The spanning cell (index 0) is two columns wide: twice the shares.
            var n = 5 + (index == 0 ? 2 : 1) * 3;
            ChromiumLayout.DifferenceInLayoutUnits(cells[index], chromium[index]).Should().BeLessThanOrEqualTo(
                n + ChromiumLayout.FloatSlack,
                $"{name} cell {index}: ours {cells[index]} must be within {n} layout units of Chromium's {chromium[index]} (5 ceiled text items, own share and two truncations per column)");
        }

        TableWidth(page).Should().BeApproximately(300, 1e-9, "the table is as wide as its container");
    }

    [Fact]
    public void Without_stretch_the_same_table_stays_as_wide_as_its_columns_need()
    {
        // The initial value: CSS width: auto. The two-column case is then the columns' maximums plus the outer half
        // lines, far narrower than the container.
        var t = new TableModels(ExactSize);
        var page = Paginate(TableModels.Page(300, 400, t.Table(Autos(2), t.Body(t.Row(t.Cell("short"), t.Cell("a longer text")))))).Single();

        BoxStyle.Initial.Width.Should().Be(BoxWidth.Auto);
        TableWidth(page).Should().BeLessThan(150);
    }

    [Fact]
    public void A_stretched_table_with_a_fill_column_is_laid_out_as_without_stretch()
    {
        // With a fill column the table takes the width it is given either way: both values mean the same.
        ValueList<TrackSize> columns = [new TrackSize.Fixed(60.Pt()), TrackSize.Auto.Instance, new TrackSize.Fill(1)];
        double[] Widths(bool stretch)
        {
            var t = new TableModels(ExactSize);
            var table = t.Table(columns, t.Body(t.Row(t.Cell("fixed"), t.Cell("short"), t.Cell("fill"))));
            return CellWidths(Paginate(TableModels.Page(300, 400, stretch ? Stretched(table) : table)).Single());
        }

        Widths(true).Should().Equal(Widths(false));
    }

    [Fact]
    public void A_stretched_table_whose_columns_do_not_fit_still_fails_with_the_width_needed()
    {
        var t = new TableModels(ExactSize);
        var document = TableModels.Page(300, 400, Stretched(t.Table([new TrackSize.Fixed(200.Pt()), new TrackSize.Fixed(200.Pt())], t.Body(t.Row(t.Cell("a"), t.Cell("b"))))));

        var result = Paginator.Paginate(LayoutEnvironment.Create(TestFonts.Set), document);

        result.IsFailure.Should().BeTrue();
        result.Error.ToString().Should().Contain("400");
    }
}
