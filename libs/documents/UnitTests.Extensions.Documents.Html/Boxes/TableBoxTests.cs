using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Html;
using Ave.Extensions.Documents.Html.Boxes;
using Ave.Extensions.Documents.Html.Parsing;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace UnitTests.Extensions.Documents.Html.Boxes;

/// <summary>Tables in box generation (<c>Boxes/TableBoxes.cs</c>): the tracks boxes they become, and their refusal side.</summary>
public class TableBoxTests
{
    private const string Collapsed = "table { border-collapse: collapse; width: 100%; table-layout: fixed } td, th { padding: 0 }";

    private static string Doc(string body, string css = Collapsed) =>
        $"<!doctype html><html lang=\"en\"><head><title>t</title><style>body {{ font-family: Lato; margin: 0 }} {css}</style></head><body>{body}</body></html>";

    private static Result<HtmlBoxes, Error> Boxes(string html, UnsupportedPolicy policy = UnsupportedPolicy.Refuse) =>
        HtmlParser.Parse(html, new HtmlParseOptions("t.html"))
            .OnSuccessBind(parsed => HtmlStyleResolver.Resolve(parsed, new HtmlStyleOptions { Unsupported = policy }))
            .OnSuccessBind(styled => HtmlBoxGenerator.Generate(styled, new HtmlBoxOptions { Fonts = TestFonts.SetWithItalic, Unsupported = policy }));

    /// <summary>The first table box of the body.</summary>
    private static Box Table(string body, string css = Collapsed)
    {
        var result = Boxes(Doc(body, css));
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        return ((Box)result.Value.Document.Root.Children.Single()).Children.OfType<Box>().First(box => box.Role == SemanticRole.Table);
    }

    private static IEnumerable<Box> Parts(Box box) => box.Children.OfType<Box>();

    private static Error FirstRefusal(string body, string css = Collapsed)
    {
        var result = Boxes(Doc(body, css));
        result.IsFailure.Should().BeTrue("the table is refused");
        return result.Error.Parts().First();
    }

    [Fact]
    public void A_table_is_a_tracks_box_of_row_groups_of_rows_of_cells_with_their_structure_roles()
    {
        var table = Table("<table><thead><tr><th>h</th><th>i</th></tr></thead><tbody><tr><td>a</td><td>b</td></tr></tbody></table>");

        (table.Layout, table.Style.DecorationBreak).Should().Be((LayoutAlgorithmId.Tracks, BoxDecorationBreak.Slice));
        Parts(table).Select(group => (group.Role, group.Layout, group.Style.TableDisplay)).Should().Equal(
            (SemanticRole.TableHead, LayoutAlgorithmId.Tracks, TableDisplay.HeaderGroup), (SemanticRole.TableBody, LayoutAlgorithmId.Tracks, TableDisplay.RowGroup));
        var rows = Parts(table).Select(group => Parts(group).Single()).ToList();
        rows.Select(row => (row.Role, row.Layout, row.Style.TableDisplay)).Should().AllBeEquivalentTo((SemanticRole.TableRow, LayoutAlgorithmId.Tracks, TableDisplay.Row));
        Parts(rows[0]).Select(cell => cell.Role).Should().Equal(SemanticRole.TableHeaderCell, SemanticRole.TableHeaderCell);
        Parts(rows[1]).Select(cell => (cell.Role, cell.Layout, cell.Style.TableDisplay)).Should().AllBeEquivalentTo((SemanticRole.TableCell, LayoutAlgorithmId.Inline, TableDisplay.None));
    }

    [Fact]
    public void Header_rows_without_body_rows_are_the_table_s_body()
    {
        // A table head without a body is a structure PDF/UA refuses; the header group stays a header group for layout.
        var group = Parts(Table("<table><thead><tr><th>h</th></tr></thead></table>")).Single();
        (group.Role, group.Style.TableDisplay).Should().Be((SemanticRole.TableBody, TableDisplay.HeaderGroup));
    }

    [Fact]
    public void A_table_lays_out()
    {
        var result = Boxes(Doc("<table><tr><td>a</td><td>b</td></tr><tr><td colspan=2>c</td></tr></table>"));
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        var pages = DocumentLayout.Run(result.Value.Document, result.Value.Fonts);
        pages.IsSuccess.Should().BeTrue(pages.IsFailure ? pages.Error.ToString() : string.Empty);
    }

    [Theory]
    // Fixed layout (a table of width 100%): a column element's length is exact, the rest shares what is left equally
    // (CSS 2.1 §17.5.2.1); with every column of a length the table's width left over goes to them (Stretch).
    [InlineData("<colgroup><col style=\"width:80pt\"><col></colgroup><tr><td>a</td><td>b</td></tr>", "fixed", "", "80pt 1fr", BoxWidth.Stretch)]
    [InlineData("<col span=2 style=\"width:10pt\"><tr><td>a</td><td>b</td><td>c</td></tr>", "fixed", "", "10pt 10pt 1fr", BoxWidth.Stretch)]
    [InlineData("<colgroup span=\"2\"></colgroup><col style=\"width:5pt\"><tr><td>a</td><td>b</td><td>c</td></tr>", "fixed", "", "1fr 1fr 5pt", BoxWidth.Stretch)]
    [InlineData("<col style=\"width:30pt\"><col style=\"width:0pt\"><tr><td>a</td><td>b</td></tr>", "fixed", "", "30pt 0pt", BoxWidth.Stretch)]
    [InlineData("<tr><td colspan=2>a</td></tr><tr><td>b</td><td>c</td></tr>", "fixed", "", "1fr 1fr", BoxWidth.Stretch)]
    // Automatic layout: a column element's length is the column's preferred width, a column without one is sized by
    // its content; width auto (also with table-layout: fixed, which Chromium takes only at a width other than auto)
    // is as wide as the columns need, width 100% takes the container's width.
    [InlineData("<tr><td>a</td></tr>", "auto", "", "auto", BoxWidth.Stretch)]
    [InlineData("<tr><td>a</td><td>b</td></tr>", "auto", "", "auto auto", BoxWidth.Stretch)]
    [InlineData("<col style=\"width:80pt\"><col><tr><td>a</td><td>b</td></tr>", "auto", "", "preferred(80pt) auto", BoxWidth.Stretch)]
    [InlineData("<col style=\"width:80pt\"><col><tr><td>a</td><td>b</td></tr>", "auto", "width: auto", "preferred(80pt) auto", BoxWidth.Auto)]
    [InlineData("<col style=\"width:80pt\"><col><tr><td>a</td><td>b</td></tr>", "fixed", "width: auto", "preferred(80pt) auto", BoxWidth.Auto)]
    [InlineData("<col style=\"width:30pt\"><col style=\"width:40pt\"><tr><td colspan=2>a</td></tr><tr><td>b</td><td>c</td></tr>", "auto", "width: auto", "preferred(30pt) preferred(40pt)", BoxWidth.Auto)]
    public void Column_widths_are_the_model_s_column_sizes(string rows, string layout, string width, string columns, BoxWidth tableWidth)
    {
        var table = Table($"<table>{rows}</table>", $"{Collapsed} table {{ table-layout: {layout}; {width} }}");
        (string.Join(" ", table.Style.Columns), table.Style.Width).Should().Be((columns, tableWidth));
    }

    [Theory]
    [InlineData("colspan=2", 2, 1)]
    [InlineData("colspan=\" +3x\"", 3, 1)]
    [InlineData("colspan=0", 1, 1)]
    [InlineData("colspan=x", 1, 1)]
    [InlineData("rowspan=2", 1, 2)]
    // HTML Standard §4.9.12.1: a row span ends with its row group.
    [InlineData("rowspan=9", 1, 3)]
    public void Spans_are_read_from_the_attributes(string attribute, int columns, int rows)
    {
        var table = Table($"<table><tr><td {attribute}>a</td><td>b</td><td>c</td><td>d</td></tr><tr><td>e</td><td>e</td><td>e</td><td>e</td><td>e</td><td>e</td></tr><tr><td>f</td></tr></table>");
        Parts(Parts(Parts(table).Single()).First()).First().Style.Span.Should().Be(new CellSpan(columns, rows));
    }

    [Fact]
    public void Cells_carry_padding_border_background_and_alignment_and_internal_parts_no_margins_or_padding()
    {
        var table = Table(
            "<table style=\"border:1.5pt solid #000\"><tbody style=\"break-inside:avoid;padding:9pt;border-top:0.75pt solid #f00\"><tr style=\"break-inside:avoid;margin:5pt;border-bottom:1.5pt solid #00f\">"
            + "<td style=\"padding:3pt 6pt;border-left:3pt solid #0f0;background:#ccc;margin:4pt;vertical-align:middle\">a</td><td style=\"vertical-align:bottom\">b</td><td style=\"vertical-align:top\">c</td></tr></tbody></table>");
        var group = Parts(table).Single();
        var row = Parts(group).Single();
        var cells = Parts(row).ToList();

        table.Style.Border.Top.Should().Be(new BorderSide(Length.FromPoints(1.5), new Color(0, 0, 0)));
        table.Style.Padding.Should().Be(default(Edges));
        (group.Style.BreakInside, group.Style.Padding, group.Style.Border.Top).Should().Be((BreakInside.Avoid, default(Edges), new BorderSide(Length.FromPoints(0.75), new Color(255, 0, 0))));
        (row.Style.BreakInside, row.Style.Margin, row.Style.Border.Bottom).Should().Be((BreakInside.Avoid, default(Edges), new BorderSide(Length.FromPoints(1.5), new Color(0, 0, 255))));
        cells[0].Style.Padding.Should().Be(new Edges(Length.FromPoints(3), Length.FromPoints(6), Length.FromPoints(3), Length.FromPoints(6)));
        (cells[0].Style.Margin, cells[0].Style.Border.Left, cells[0].Style.Background).Should().Be((default(Edges), new BorderSide(Length.FromPoints(3), new Color(0, 255, 0)), new Color(0xCC, 0xCC, 0xCC)));
        cells.Select(cell => cell.Style.VerticalAlign).Should().Equal(VerticalAlign.Middle, VerticalAlign.Bottom, VerticalAlign.Top);
    }

    [Fact]
    public void A_separated_table_is_refused_with_a_message_that_names_border_collapse()
    {
        var error = FirstRefusal("<table><tr><td>a</td></tr></table>", "table { width: 100% }");
        (error.Code, error.Metadata![ErrorMetadataKeys.Property]).Should().Be((HtmlStyleErrorCodes.Unsupported.Value, "border-collapse"));
        error.Message.Should().Contain("border-collapse: collapse");
    }

    /// <summary>One row per refusal of table structure: the body, extra CSS, the refused value (null: nothing refused).</summary>
    public static TheoryData<string, string, string?> StructureRefusals { get; } = new()
    {
        // Structure the model's table does not have (CSS 2.1 §17.2.1 anonymous boxes; captions, footers).
        { "<table><caption>c</caption><tr><td>a</td></tr></table>", "", "caption" },
        { "<table><tfoot><tr><td>a</td></tr></tfoot><tr><td>b</td></tr></table>", "", "tfoot" },
        { "<table><tbody><tr><td>a</td></tr></tbody><thead><tr><td>h</td></tr></thead></table>", "", "thead after a row group" },
        { "<table><thead><tr><td>h</td></tr></thead><thead><tr><td>i</td></tr></thead><tr><td>a</td></tr></table>", "", "thead after a row group" },
        { "<div style=\"display:table\"><div style=\"display:table-row\">a</div></div>", "", "display: table on div" },
        { "<div style=\"display:table-cell\">a</div>", "", "div outside a table" },
        { "<table><tbody><tr style=\"display:block\"><td>a</td></tr></tbody></table>", "", "tr in tbody" },
        { "<table><tr><td style=\"display:table-row\">a</td></tr></table>", "", "td in tr" },
        { "<table><thead style=\"display:table-row-group\"><tr><td>a</td></tr></thead></table>", "", "display: table-row-group on thead" },
        { "<table><tr><span style=\"display:table-cell\">a</span></tr></table>", "", "span outside a table" },
        { "<table><tr><td rowspan=0>a</td></tr></table>", "", "rowspan=0" },
        { "<table><tbody></tbody><tr><td>a</td></tr></table>", "", "tbody without rows" },
        { "<table></table>", "", "table without rows" },
        { "<table><tr></tr></table>", "", "table without cells" },
        { "<table><tr><td>a</td><td rowspan=2>b</td></tr><tr><td colspan=2>c</td></tr></table>", "", "cells that overlap" },
        // Column sizes the model cannot give.
        { "<col><col><col><table><tr><td>a</td><td>b</td></tr></table>", "", null },
        { "<table><col><col><col><tr><td>a</td><td>b</td></tr></table>", "", "3 column elements for 2 columns" },
        { "<table><tr><td colspan=2>a</td></tr></table>", "", "column 2 without a cell that starts in it" },
        { "<table><tr><td>a</td><td colspan=2>b</td></tr></table>", "table { table-layout: auto }", "column 3 without a cell that starts in it" },
    };

    [Theory]
    [MemberData(nameof(StructureRefusals))]
    public void Table_structure_the_model_does_not_have_is_refused(string body, string css, string? value)
    {
        if (value is null)
        {
            // A column outside a table is not rendered at all (the parser drops a col start tag outside a table).
            Boxes(Doc(body, Collapsed + css)).IsSuccess.Should().BeTrue();
            return;
        }

        var error = FirstRefusal(body, Collapsed + css);
        (error.Code, error.Metadata![ErrorMetadataKeys.Value]).Should().Be((HtmlStyleErrorCodes.Unsupported.Table, value));
    }

    [Theory]
    [InlineData("<table style=\"width:50%\"><tr><td>a</td></tr></table>", "", "width", "50%")]
    [InlineData("<table style=\"width:300pt\"><tr><td>a</td></tr></table>", "", "width", "300pt")]
    [InlineData("<table style=\"margin-left:10pt\"><tr><td>a</td></tr></table>", "", "margin-left", "10pt")]
    [InlineData("<table style=\"width:auto;margin-right:10pt\"><tr><td>a</td></tr></table>", "", "margin-right", "10pt")]
    [InlineData("<table style=\"width:auto;margin-left:auto\"><tr><td>a</td></tr></table>", "", "margin-left", "auto")]
    [InlineData("<table><col style=\"width:50%\"><col><tr><td>a</td><td>b</td></tr></table>", "", "width", "50%")]
    [InlineData("<table><col style=\"width:50%\"><col><tr><td>a</td><td>b</td></tr></table>", "table { table-layout: auto }", "width", "50%")]
    [InlineData("<table><tr><td style=\"width:10pt\">a</td></tr></table>", "", "width", "10pt")]
    [InlineData("<table><tr><td style=\"width:10pt\">a</td></tr></table>", "table { table-layout: auto }", "width", "10pt")]
    [InlineData("<table style=\"background:#ccc\"><tr><td>a</td></tr></table>", "", "background-color", "rgb(204, 204, 204)")]
    [InlineData("<table><tbody style=\"background:#ccc\"><tr><td>a</td></tr></tbody></table>", "", "background-color", "rgb(204, 204, 204)")]
    [InlineData("<table><tr style=\"background:#ccc\"><td>a</td></tr></table>", "", "background-color", "rgb(204, 204, 204)")]
    [InlineData("<table><col style=\"background:#ccc\"><tr><td>a</td></tr></table>", "", "background-color", "rgb(204, 204, 204)")]
    [InlineData("<table><col style=\"border-left:1pt solid #000\"><tr><td>a</td></tr></table>", "", "border-left-width", "0.75pt")]
    [InlineData("<table><colgroup style=\"width:10pt\"><col></colgroup><tr><td>a</td></tr></table>", "", "width", "10pt")]
    [InlineData("<table><tr><td style=\"width:50%\">a</td></tr></table>", "", "width", "50%")]
    [InlineData("<table><tr><td style=\"vertical-align:baseline\">a</td></tr></table>", "", "vertical-align", "baseline")]
    public void Table_values_the_model_does_not_draw_are_refused(string body, string css, string property, string value)
    {
        var error = FirstRefusal(body, Collapsed + css);
        (error.Code, error.Metadata![ErrorMetadataKeys.Property], error.Metadata![ErrorMetadataKeys.Value]).Should().Be((HtmlStyleErrorCodes.Unsupported.Value, property, value));
    }

    [Theory]
    // No effect with collapsed borders (CSS 2.1 §17.6.1, §17.6.1.1), without a caption, or on an internal part (§17.5).
    [InlineData("table { border-spacing: 5pt; empty-cells: show; caption-side: top }")]
    [InlineData("tr { width: 50pt; padding: 3pt } tbody { padding: 2pt; margin: 4pt }")]
    [InlineData("td { margin: 3pt }")]
    [InlineData("table { padding: 6pt; margin: 4pt 0 }")]
    [InlineData("table { margin: 0 auto }")]
    public void Table_values_without_effect_are_drawn_as_nothing(string css) =>
        Boxes(Doc("<table><tr><td>a</td></tr></table>", Collapsed + css)).IsSuccess.Should().BeTrue();

    [Theory]
    [MemberData(nameof(StructureRefusals))]
    public void Under_warn_a_table_with_refused_structure_is_laid_out(string body, string css, string? value)
    {
        _ = value;
        var result = Boxes(Doc(body, Collapsed + css), UnsupportedPolicy.Warn);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        var pages = DocumentLayout.Run(result.Value.Document, result.Value.Fonts);
        pages.IsSuccess.Should().BeTrue(pages.IsFailure ? pages.Error.ToString() : string.Empty);
    }

    [Fact]
    public void An_inline_table_is_refused_by_style_resolution() =>
        FirstRefusal("<table style=\"display:inline-table\"><tr><td>a</td></tr></table>").Metadata![ErrorMetadataKeys.Property].Should().Be("display");

    [Fact]
    public void Under_warn_a_refused_caption_is_left_out_and_the_table_drawn()
    {
        var result = Boxes(Doc("<table><caption>c</caption><tr><td>a</td></tr></table>"), UnsupportedPolicy.Warn);
        result.IsSuccess.Should().BeTrue();
        result.Value.Diagnostics.Select(error => error.Code).Should().Contain(HtmlStyleErrorCodes.Unsupported.Table);
        var table = ((Box)result.Value.Document.Root.Children.Single()).Children.OfType<Box>().Single();
        Parts(table).Select(group => group.Style.TableDisplay).Should().Equal(TableDisplay.RowGroup);
    }
}
