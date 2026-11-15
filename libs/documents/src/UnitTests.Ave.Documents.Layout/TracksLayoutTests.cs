using Ave.Documents.Core;
using Ave.Documents.DocumentModel;
using Ave.Documents.Layout;
using Ave.Documents.TestSupport;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace UnitTests.Ave.Documents.Layout;

/// <summary>The tracks algorithm on its own: collapsed borders, spans, alignment, keep rules, errors and termination.</summary>
public class TracksLayoutTests
{
    private static readonly Color Grey = new(0xD3, 0xD3, 0xD3);
    private static readonly Color Red = new(0xFF, 0, 0);

    private static Result<ValueList<PageFragment>, Error> Paginate(StyledDocument document) =>
        Paginator.Paginate(LayoutEnvironment.Create(TestFonts.Set), document);

    private static BoxFragment TableOn(PageFragment page) => (BoxFragment)((BoxFragment)page.Root.Fragment).Children[0].Fragment;

    private static IEnumerable<(Rect Area, Color Color)> Rules(BoxFragment table) =>
        table.Children.Where(child => child.Fragment is RuleFragment).Select(child => (new Rect(child.Offset, child.Fragment.Size), ((RuleFragment)child.Fragment).Color));

    private static IEnumerable<BoxFragment> Cells(BoxFragment table) =>
        table.Children.Select(child => child.Fragment).OfType<BoxFragment>()
            .SelectMany(group => group.Children.Select(row => (BoxFragment)row.Fragment))
            .SelectMany(row => row.Children.Select(cell => (BoxFragment)cell.Fragment));

    private static BoxStyle Lines(double width, Color color) =>
        BoxStyle.Initial with { Border = Border.All(new BorderSide(width.Pt(), color)), Padding = new Edges(2.Pt(), 4.Pt(), 2.Pt(), 4.Pt()) };

    [Fact]
    public void Two_adjacent_cells_share_one_grid_line_centred_between_them()
    {
        var t = new TableModels();
        var grid = Lines(1, Grey);
        var table = t.Table([new TrackSize.Fixed(50.Pt()), new TrackSize.Fixed(50.Pt())], t.Body(t.Row(t.Cell("a", grid), t.Cell("b", grid))));

        var fragment = TableOn(Paginate(TableModels.Page(300, 300, table)).Value.Single());

        fragment.Size.Width.Should().Be(100 + 1, "two 50 pt columns plus half of each outer line (CSS 2.1 §17.6.2)");
        var vertical = Rules(fragment).Where(rule => rule.Area.Size.Width == 1).Select(rule => rule.Area.Origin.X);
        vertical.Should().Equal([0, 50, 100], "one line per column boundary, centred on it (x = 0.5, 50.5, 100.5 minus half the width)");
        Cells(fragment).Select(cell => cell.Border).Should().AllBeEquivalentTo(Border.None, "in the collapsing model the table paints the lines, not the cells");
    }

    [Fact]
    public void Border_conflicts_resolve_by_width_then_origin_then_position()
    {
        // §17.6.2.1: wider wins; equal width: cell over row; two cells: the upper (or left) one wins.
        var t = new TableModels();
        var thin = Lines(1, Grey);
        var red = Lines(1, Red);
        var wide = Lines(2, Color.Black);
        var table = t.Table(
            [new TrackSize.Fixed(50.Pt()), new TrackSize.Fixed(50.Pt())],
            t.Body(t.Row(t.Cell("a", thin), t.Cell("b", thin)), t.Row(t.Cell("c", red), t.Cell("d", wide))));

        var rules = Rules(TableOn(Paginate(TableModels.Page(300, 300, table)).Value.Single())).ToArray();

        // The line between the rows: over column 1 grey (upper cell wins over the equally wide red one), over column 2
        // black (2 pt beats 1 pt).
        rules.Where(rule => rule.Area.Size.Height == 1 && rule.Area.Origin.Y > 1 && rule.Area.Origin.Y < 20).Select(rule => rule.Color).Should().Equal(Grey);
        rules.Should().Contain(rule => rule.Color == Color.Black && rule.Area.Size.Height == 2);
    }

    [Fact]
    public void A_cell_spanning_two_rows_makes_the_last_row_grow_when_it_is_taller_than_both()
    {
        var t = new TableModels();
        var plain = BoxStyle.Initial;
        var span = plain with { Span = new CellSpan(1, 2) };
        var tall = t.Lines(["1", "2", "3", "4", "5"]) with { Style = span };
        var table = t.Table([new TrackSize.Fixed(50.Pt()), new TrackSize.Fixed(50.Pt())], t.Body(t.Row(tall, t.Cell("a", plain)), t.Row(t.Cell("b", plain))));

        var fragment = TableOn(Paginate(TableModels.Page(300, 300, table)).Value.Single());

        var rows = ((BoxFragment)fragment.Children[0].Fragment).Children.Select(row => row.Fragment.Size.Height);
        rows.Should().Equal([12, 48], "row 1 holds 'a' (12 pt); row 2 grows by what the 60 pt spanning cell still needs (CSS 2.1 §17.5.3)");
        Cells(fragment).First().Size.Height.Should().Be(60);
    }

    [Theory]
    [InlineData(VerticalAlign.Top, 0)]
    [InlineData(VerticalAlign.Middle, 18)]
    [InlineData(VerticalAlign.Bottom, 36)]
    public void A_short_cell_places_its_content_by_vertical_align(VerticalAlign align, double shift)
    {
        var t = new TableModels();
        var plain = BoxStyle.Initial;
        var table = t.Table(
            [new TrackSize.Fixed(50.Pt()), new TrackSize.Fixed(50.Pt())],
            t.Body(t.Row(t.Lines(["1", "2", "3", "4"]) with { Style = plain }, t.Cell("short", plain with { VerticalAlign = align }))));

        var cell = Cells(TableOn(Paginate(TableModels.Page(300, 300, table)).Value.Single())).Last();

        cell.Size.Height.Should().Be(48, "a cell is as tall as its row");
        cell.Children[0].Offset.Y.Should().Be(shift, "CSS 2.1 §17.5.3");
    }

    [Fact]
    public void Columns_that_cannot_fit_are_an_error_at_the_table_s_source_line()
    {
        var t = new TableModels();
        var table = t.Table([new TrackSize.Fixed(200.Pt()), TrackSize.Auto.Instance], t.Body(t.Row(t.Cell("a"), t.Cell("Supercalifragilistic"))));

        var error = Paginate(TableModels.Page(250, 300, table)).Error;

        error.Code.Should().Be(DocumentErrorCodes.Layout.ColumnsDoNotFit);
        error.Metadata![ErrorMetadataKeys.Node].Should().Be(table.Id.ToString());
        error.Metadata!.Should().ContainKey(ErrorMetadataKeys.SourceLine);
    }

    [Fact]
    public void A_row_group_kept_together_but_taller_than_a_page_breaks_anyway_and_ends()
    {
        // Ten 16.75 pt rows in one kept group on a page area 100 pt high: it moves off page 1 (after the first row)
        // and then breaks by the appeal rule — never an endless loop.
        var t = new TableModels();
        var kept = BoxStyle.Initial with { BreakInside = BreakInside.Avoid };
        var table = t.Table(
            [new TrackSize.Fixed(80.Pt())],
            t.Body(t.Row(t.Cell("first"))),
            t.Body(kept, [.. Enumerable.Range(1, 10).Select(i => t.Row(t.Cell($"k{i}")))]));

        var pages = Paginate(TableModels.Page(200, 100, table)).Value;

        pages.Select(page => Cells(TableOn(page)).Count()).Should().Equal(1, 5, 5);
    }

    [Fact]
    public void A_header_too_tall_to_repeat_is_left_out_on_later_pages_so_the_table_ends()
    {
        // The header is 76.75 pt (six lines): under it a later page (80 pt high) has no room for even one body line, so
        // page 1 holds the header alone (the start of an empty page) and later pages go on without it.
        var t = new TableModels();
        var table = t.Table(
            [new TrackSize.Fixed(80.Pt())],
            t.Head(t.Row(t.Lines(["h1", "h2", "h3", "h4", "h5", "h6"]))),
            t.Body([.. Enumerable.Range(1, 4).Select(i => t.Row(t.Lines(["a", "b", "c", "d", "e"])))]));

        var pages = Paginate(TableModels.Page(200, 80, table));

        pages.IsSuccess.Should().BeTrue(pages.IsFailure ? pages.Error.ToString() : string.Empty);
        pages.Value.Should().HaveCount(5, "the header page, then the four body rows one per page");
        pages.Value.Skip(1).SelectMany(page => TableOn(page).Children.Select(child => child.Fragment).OfType<BoxFragment>())
            .Should().NotContain(group => group.IsRepeated);
    }

    [Fact]
    public void A_spanning_cell_cut_by_the_page_end_continues_on_the_next_page()
    {
        // The date-and-hours shape of the timesheet, taller than a page: rows 1 and 2 are joined by a cell spanning
        // both; at the start of a page the unit must break inside, and the spanning cell continues.
        var t = new TableModels();
        var span = TableModels.ReferenceCell with { Span = new CellSpan(1, 2) };
        var table = t.Table(
            [new TrackSize.Fixed(40.Pt()), new TrackSize.Fixed(80.Pt())],
            t.Body(t.Row(t.Cell("date", span), t.Lines([.. Enumerable.Range(1, 8).Select(i => $"x{i}")])), t.Row(t.Lines([.. Enumerable.Range(1, 8).Select(i => $"y{i}")]))));

        var pages = Paginate(TableModels.Page(200, 100, table)).Value;

        pages.Count.Should().BeGreaterThan(1);
        pages.All(page => Cells(TableOn(page)).Any()).Should().BeTrue("every page shows a part of the unit");
    }

    [Fact]
    public void A_forced_page_break_inside_a_cell_is_invalid_content()
    {
        var t = new TableModels();
        var inner = Models.Paragraph(900, "after", BoxStyle.Initial with { BreakBefore = BreakBetween.Page });
        var cell = t.Cell("before") with { Children = [Models.Paragraph(901, "before"), inner] };
        var table = t.Table([new TrackSize.Fixed(80.Pt())], t.Body(t.Row(cell)));

        Paginate(TableModels.Page(200, 100, table)).Error.Code.Should().Be(DocumentErrorCodes.Layout.InvalidContent);
    }

    [Theory]
    [InlineData(3, 1, "does not fit in the 2 columns")]
    [InlineData(1, 2, "runs past the end of its row group")]
    public void A_cell_that_does_not_fit_the_grid_is_invalid_content(int columnSpan, int rowSpan, string message)
    {
        var t = new TableModels();
        var cell = t.Cell("x", TableModels.ReferenceCell with { Span = new CellSpan(columnSpan, rowSpan) });
        var table = t.Table([new TrackSize.Fixed(40.Pt()), new TrackSize.Fixed(40.Pt())], t.Body(t.Row(cell)));

        var error = Paginate(TableModels.Page(200, 100, table)).Error;

        error.Code.Should().Be(DocumentErrorCodes.Layout.InvalidContent);
        error.Message.Should().Contain(message);
    }

    [Fact]
    public void A_table_in_an_auto_column_is_measured_by_its_columns()
    {
        // Nested tables fall out of the design: a tracks box is measured like any box (min = sum of column minimums).
        var t = new TableModels();
        var plain = BoxStyle.Initial;
        var inner = t.Table([new TrackSize.Fixed(30.Pt()), new TrackSize.Fixed(20.Pt())], t.Body(t.Row(t.Cell("a", plain), t.Cell("b", plain))));
        var holder = t.Cell("unused", plain) with { Children = [inner] };
        var outer = t.Table([TrackSize.Auto.Instance, new TrackSize.Fill(1)], t.Body(t.Row(holder, t.Cell("rest", plain))));

        var cells = Cells(TableOn(Paginate(TableModels.Page(300, 300, outer)).Value.Single())).ToArray();

        cells[0].Size.Width.Should().Be(50);
        cells[^1].Size.Width.Should().Be(250);
    }
}
