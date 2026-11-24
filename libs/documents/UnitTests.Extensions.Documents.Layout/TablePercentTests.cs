using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;

namespace UnitTests.Extensions.Documents.Layout;

/// <summary>
/// Widths of tables and columns as percentages (<see cref="BoxWidth.Percent"/>, <see cref="TrackSize.Percent"/>) against
/// Chromium's print of the same tables (cases <c>tpa-*</c> in <c>tools/reference/chromium/cases</c>): Lato 10.5 pt,
/// reference cells (<c>border: 0.75pt solid; padding: 2pt 4pt</c>) in a page area of 276 pt, "one" beside "text with more
/// words". Widths are compared with <see cref="ChromiumLayout"/>, within n layout units as in <see cref="TableLengthTests"/>;
/// the test of the 1/64 px floor compares to a thousandth of a point.
/// </summary>
public class TablePercentTests
{
    private const double ExactSize = 10.5;

    private const double Units = 4 + ChromiumLayout.FloatSlack;

    private static PageFragment Page(Box table, LayoutProfile? profile = null)
    {
        var document = TableModels.Page(276, 360, table);
        var result = Paginator.Paginate(LayoutEnvironment.Create(TestFonts.Set), profile is null ? document : document with { Profile = profile });
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        return result.Value.Single();
    }

    private static IEnumerable<BoxFragment> Boxes(Fragment fragment, SemanticRole role) =>
        fragment is BoxFragment box
            ? (box.Box.Role == role ? [box] : Enumerable.Empty<BoxFragment>()).Concat(box.Children.SelectMany(child => Boxes(child.Fragment, role)))
            : [];

    private static double TableWidth(PageFragment page) => Boxes(page.Root.Fragment, SemanticRole.Table).Single().Size.Width;

    private static double[] CellWidths(PageFragment page) => [.. Boxes(page.Root.Fragment, SemanticRole.TableCell).Select(cell => cell.Size.Width)];

    private static Box With(Box table, BoxWidth width) => table with { Style = table.Style with { Width = width } };

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

    private static Box OneRow(TableModels t, ValueList<TrackSize> columns, string first = "one") =>
        t.Table(columns, t.Body(t.Row(t.Cell(first), t.Cell("text with more words"))));

    private static void ShouldBe(double[] engine, params double[] chromium) =>
        engine.Zip(chromium).Should().OnlyContain(pair => ChromiumLayout.DifferenceInLayoutUnits(pair.First, pair.Second) <= Units, $"{string.Join(", ", engine)} against {string.Join(", ", chromium)}");

    /// <summary>
    /// A percent column takes its percentage of the width the columns share, and the other column the rest: case
    /// <c>tpa-full</c>, width 100%, a column of 30% (a: 82.570 of 275.25 pt, collapsed; b: 78.293 of 261 pt, separated, less
    /// border, padding and three spacings); <c>tpa-length</c> a, a table of 200 pt (59.766 of 199.242 pt).
    /// </summary>
    [Fact]
    public void A_percent_column_takes_its_share_of_the_width_the_columns_share()
    {
        var t = new TableModels(ExactSize);
        ValueList<TrackSize> columns = [new TrackSize.Percent(30), TrackSize.Auto.Instance];

        ShouldBe(CellWidths(Page(With(OneRow(t, columns), BoxWidth.Stretch))), 82.570, 192.680);
        ShouldBe(CellWidths(Page(With(Separated(OneRow(t, columns)), BoxWidth.Stretch))), 78.293, 182.707);
        ShouldBe(CellWidths(Page(With(OneRow(t, columns), new BoxWidth.Fixed(200.Pt())))), 59.766, 139.477);
    }

    /// <summary>
    /// A table of width auto is as wide as its columns need to have their percentages: case <c>tpa-auto</c> a, a column of
    /// 30% beside "text with more words" (106.887 pt): the columns share 106.887 / 0.7 pt, the table is 153.445 pt; case
    /// <c>tpa-sum</c> d, separated, a column of 10% holding a word of 76.2 pt asks for 762 pt, so the table is as wide as
    /// the page area and the other column takes the rest.
    /// </summary>
    [Fact]
    public void A_table_of_width_auto_grows_until_its_columns_have_their_percentages()
    {
        var t = new TableModels(ExactSize);

        var thirty = Page(OneRow(t, [new TrackSize.Percent(30), TrackSize.Auto.Instance]));
        ShouldBe([TableWidth(thirty)], 153.445);
        ShouldBe(CellWidths(thirty), 45.809, 106.887);

        var ten = Page(Separated(OneRow(t, [new TrackSize.Percent(10), TrackSize.Auto.Instance], "extraordinarily")));
        ShouldBe([TableWidth(ten)], 276);
        ShouldBe(CellWidths(ten), 76.207, 184.793);
    }

    /// <summary>
    /// A cell's percentage makes its column a percent column, in a later row too: case <c>tpa-auto</c> e, a cell of 30% in
    /// the second row gives the same 45.809 and 106.887 pt as one in the first.
    /// </summary>
    [Fact]
    public void A_cell_percentage_in_a_later_row_makes_its_column_a_percent_column()
    {
        var t = new TableModels(ExactSize);
        var table = t.Table([TrackSize.Auto.Instance, TrackSize.Auto.Instance], t.Body(
            t.Row(t.Cell("one"), t.Cell("text with more words")),
            t.Row(t.Cell("two", TableModels.ReferenceCell with { Width = new BoxWidth.Percent(30) }), t.Cell("text with more words"))));

        ShouldBe(CellWidths(Page(table)), 45.809, 106.887, 45.809, 106.887);
    }

    /// <summary>
    /// Percentages past 100 get only what is left, and the width left goes to a length column before a percent column:
    /// case <c>tpa-sum</c> a, width 100%, columns of 60% and 70% (165.141 and 110.109 pt, 60% and 40%); case
    /// <c>tpa-beside</c> c, width auto, a column of 60 pt beside one of 30% (192.680 and 82.570 pt: the table fills the page
    /// area for the percent column's text, and the length column takes the 70%).
    /// </summary>
    [Fact]
    public void Percentages_past_100_get_what_is_left_and_a_length_column_takes_the_rest()
    {
        var t = new TableModels(ExactSize);

        ShouldBe(CellWidths(Page(With(OneRow(t, [new TrackSize.Percent(60), new TrackSize.Percent(70)]), BoxWidth.Stretch))), 165.141, 110.109);
        ShouldBe(CellWidths(Page(OneRow(t, [new TrackSize.Preferred(60.Pt()), new TrackSize.Percent(30)]))), 192.680, 82.570);
    }

    /// <summary>
    /// A table's percentage is of the width it is given, its border box: case <c>tpa-table</c> a, 80% of 276 pt
    /// (Chromium 220.793 pt), its columns sharing it as for a length (43.195 and 176.848 pt).
    /// </summary>
    [Fact]
    public void A_table_percentage_is_of_its_container()
    {
        var t = new TableModels(ExactSize);
        var page = Page(With(OneRow(t, [TrackSize.Auto.Instance, TrackSize.Auto.Instance]), new BoxWidth.Percent(80)));

        ShouldBe([TableWidth(page)], 220.793);
        ShouldBe(CellWidths(page), 43.195, 176.848);
    }

    /// <summary>
    /// Under <see cref="LengthRounding.SixtyFourthPixels"/> a percentage is floored to 1/64 px, under
    /// <see cref="LengthRounding.Exact"/> it is not: case <c>tpa-fraction</c> a, width 100%, columns of 33.3% and 17.17% of
    /// 275.25 pt are 91.652 and 47.250 pt in Chromium, 91.658 and 47.260 pt exactly; and <c>tpa-table</c> a, 80% of 276 pt
    /// is 220.793 pt.
    /// </summary>
    [Fact]
    public void Under_sixty_fourth_pixels_a_percentage_is_floored_to_a_layout_unit()
    {
        var t = new TableModels(ExactSize);
        var table = With(t.Table([new TrackSize.Percent(33.3), new TrackSize.Percent(17.17), TrackSize.Auto.Instance],
            t.Body(t.Row(t.Cell("one"), t.Cell("two"), t.Cell("text with more words")))), BoxWidth.Stretch);
        var rounded = LayoutProfile.Default with { LengthRounding = LengthRounding.SixtyFourthPixels };

        CellWidths(Page(table, rounded)).Take(2).Should().Equal([91.652, 47.25], (engine, chromium) => Math.Abs(engine - chromium) < 0.001);
        CellWidths(Page(table)).Take(2).Should().Equal([91.658, 47.260], (engine, exact) => Math.Abs(engine - exact) < 0.001);
        TableWidth(Page(With(OneRow(t, [TrackSize.Auto.Instance, TrackSize.Auto.Instance]), new BoxWidth.Percent(80)), rounded)).Should().BeApproximately(220.793, 0.001);
    }

    /// <summary>The model's check: a percentage is more than zero and bounded, and the text form names it.</summary>
    [Fact]
    public void A_percentage_of_zero_or_less_is_refused_and_the_text_form_names_it()
    {
        var t = new TableModels(ExactSize);

        ModelCheck.Check(TableModels.Page(276, 360, With(OneRow(t, [TrackSize.Auto.Instance, TrackSize.Auto.Instance]), new BoxWidth.Percent(0)))).Error.Code.Should().Be(DocumentErrorCodes.Input.Invalid.Length);
        ModelCheck.Check(TableModels.Page(276, 360, OneRow(t, [new TrackSize.Percent(-5), TrackSize.Auto.Instance]))).Error.Code.Should().Be(DocumentErrorCodes.Input.Invalid.Columns);
        DocumentModelText.Write(TableModels.Page(276, 360, With(OneRow(t, [new TrackSize.Percent(30), TrackSize.Auto.Instance]), new BoxWidth.Percent(80)))).Should().Contain("width=80%").And.Contain("30%");
    }
}
