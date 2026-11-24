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
    public void The_first_footer_group_is_drawn_after_the_body_rows_and_a_later_one_in_its_place()
    {
        // CSS 2.1 §17.2: a footer group is displayed after all other row groups; Chromium takes only the first as the
        // table's footer (cases tf-before, tf-two). The model's table has no footer group: its rows are body rows.
        var table = Table("<table><thead><tr><th>h</th></tr></thead><tfoot><tr><td>f</td></tr></tfoot><tbody><tr><td>a</td></tr></tbody>"
            + "<tfoot><tr><td>g</td></tr></tfoot><tbody><tr><td>b</td></tr></tbody></table>");

        Parts(table).Select(group => (group.Role, group.Style.TableDisplay)).Should().Equal(
            (SemanticRole.TableHead, TableDisplay.HeaderGroup), (SemanticRole.TableBody, TableDisplay.RowGroup), (SemanticRole.TableBody, TableDisplay.RowGroup),
            (SemanticRole.TableBody, TableDisplay.RowGroup), (SemanticRole.TableBody, TableDisplay.RowGroup));
        Parts(table).Select(group => Parts(Parts(group).Single()).Single().Children.OfType<TextRun>().Single().Text).Should().Equal("h", "a", "g", "b", "f");
    }

    [Fact]
    public void A_table_lays_out()
    {
        var result = Boxes(Doc("<table><tr><td>a</td><td>b</td></tr><tr><td colspan=2>c</td></tr></table>"));
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        var pages = DocumentLayout.Run(result.Value.Document, result.Value.Fonts);
        pages.IsSuccess.Should().BeTrue(pages.IsFailure ? pages.Error.ToString() : string.Empty);
    }

    [Fact]
    public void Fixed_columns_wider_than_the_table_are_refused_by_layout()
    {
        // The fixed layout with column lengths that add up to more than the 595.28 pt page: the columns are the model's
        // fixed sizes as given, and layout refuses the table, which needs more than its container's width.
        var result = Boxes(Doc("<table><col style=\"width:400pt\"><col style=\"width:300pt\"><tr><td>a</td><td>b</td></tr></table>"));
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        var pages = DocumentLayout.Run(result.Value.Document, result.Value.Fonts);
        pages.IsFailure.Should().BeTrue();
        pages.Error.Code.Should().Be(LayoutErrorCodes.ColumnsDoNotFit);
    }

    [Theory]
    // Fixed layout (a table of width 100%): a column element's length is exact, the rest shares what is left equally
    // (CSS 2.1 §17.5.2.1); with every column of a length the table's width left over goes to them (Stretch).
    [InlineData("<colgroup><col style=\"width:80pt\"><col></colgroup><tr><td>a</td><td>b</td></tr>", "fixed", "", "80pt 1fr", "stretch")]
    [InlineData("<col span=2 style=\"width:10pt\"><tr><td>a</td><td>b</td><td>c</td></tr>", "fixed", "", "10pt 10pt 1fr", "stretch")]
    [InlineData("<colgroup span=\"2\"></colgroup><col style=\"width:5pt\"><tr><td>a</td><td>b</td><td>c</td></tr>", "fixed", "", "1fr 1fr 5pt", "stretch")]
    [InlineData("<col style=\"width:30pt\"><col style=\"width:0pt\"><tr><td>a</td><td>b</td></tr>", "fixed", "", "30pt 0pt", "stretch")]
    [InlineData("<tr><td colspan=2>a</td></tr><tr><td>b</td><td>c</td></tr>", "fixed", "", "1fr 1fr", "stretch")]
    // Percent columns of the fixed layout: shares of the width (without other columns scaled to fill it), and beside
    // columns without a width the rest shared equally (Chromium, cases tp-*).
    [InlineData("<col style=\"width:14.29%\"><col style=\"width:85.71%\"><tr><td>a</td><td>b</td></tr>", "fixed", "", "14.29fr 85.71fr", "stretch")]
    [InlineData("<col style=\"width:20%\"><col style=\"width:30%\"><tr><td>a</td><td>b</td></tr>", "fixed", "", "40fr 60fr", "stretch")]
    [InlineData("<col style=\"width:30%\"><col><col style=\"width:20%\"><col><tr><td>a</td><td>b</td><td>c</td><td>d</td></tr>", "fixed", "", "30fr 25fr 20fr 25fr", "stretch")]
    [InlineData("<col span=2 style=\"width:25%\"><col style=\"width:50%\"><tr><td>a</td><td>b</td><td>c</td></tr>", "fixed", "", "25fr 25fr 50fr", "stretch")]
    // Automatic layout: a column element's length is the column's preferred width, a column without one is sized by
    // its content; width auto (also with table-layout: fixed, which Chromium takes only at a width other than auto)
    // is as wide as the columns need, width 100% takes the container's width.
    [InlineData("<tr><td>a</td></tr>", "auto", "", "auto", "stretch")]
    [InlineData("<tr><td>a</td><td>b</td></tr>", "auto", "", "auto auto", "stretch")]
    [InlineData("<col style=\"width:80pt\"><col><tr><td>a</td><td>b</td></tr>", "auto", "", "preferred(80pt) auto", "stretch")]
    [InlineData("<col style=\"width:80pt\"><col><tr><td>a</td><td>b</td></tr>", "auto", "width: auto", "preferred(80pt) auto", "auto")]
    [InlineData("<col style=\"width:80pt\"><col><tr><td>a</td><td>b</td></tr>", "fixed", "width: auto", "preferred(80pt) auto", "auto")]
    [InlineData("<col style=\"width:30pt\"><col style=\"width:40pt\"><tr><td colspan=2>a</td></tr><tr><td>b</td><td>c</td></tr>", "auto", "width: auto", "preferred(30pt) preferred(40pt)", "auto")]
    // A table width as a length is the model's given width, in both layouts (cases twd-table-wide, twd-sum).
    [InlineData("<col style=\"width:80pt\"><col><tr><td>a</td><td>b</td></tr>", "fixed", "width: 200pt", "80pt 1fr", "200pt")]
    [InlineData("<tr><td>a</td><td>b</td></tr>", "auto", "width: 150pt", "auto auto", "150pt")]
    public void Column_widths_are_the_model_s_column_sizes(string rows, string layout, string width, string columns, string tableWidth)
    {
        var table = Table($"<table>{rows}</table>", $"{Collapsed} table {{ table-layout: {layout}; {width} }}");
        (string.Join(" ", table.Style.Columns), table.Style.Width.ToString()).Should().Be((columns, tableWidth));
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

    /// <summary>A table with separated borders (the user agent's <c>border-collapse: separate</c> and <c>border-spacing:
    /// 2px</c>, CSS 2.1 §17.6.1) keeps its border model, spacing, padding and background (cases <c>ts-plain</c>,
    /// <c>ts-table-padding</c>, <c>ts-paint</c>); with collapsed borders the table has no padding (§17.6.2).</summary>
    [Fact]
    public void A_separated_table_keeps_its_border_model_spacing_padding_and_background()
    {
        var table = Table("<table><tr><td>a</td></tr></table>", "table { padding: 3pt; background: #ffff00; border-spacing: 3pt 9pt }");
        (table.Style.BorderModel, table.Style.BorderSpacing).Should().Be((TableBorderModel.Separate, new BorderSpacing(Length.FromPoints(3), Length.FromPoints(9))));
        (table.Style.Padding, table.Style.Background).Should().Be((Edges.All(Length.FromPoints(3)), new Color(255, 255, 0)));
        Table("<table><tr><td>a</td></tr></table>", "").Style.BorderSpacing.Should().Be(new BorderSpacing(Length.FromPoints(1.5), Length.FromPoints(1.5)), "the user agent's 2px");

        var collapsed = Table("<table><tr><td>a</td></tr></table>", Collapsed + "table { padding: 3pt }");
        (collapsed.Style.BorderModel, collapsed.Style.BorderSpacing, collapsed.Style.Padding).Should().Be((TableBorderModel.Collapse, BorderSpacing.Zero, Edges.Zero));
    }

    /// <summary>What a table with separated borders does not draw in this version, each refused with what to do: a
    /// spacing that is not a whole number of CSS pixels, a border on a row or a row group.</summary>
    [Theory]
    [InlineData("<table><tr><td>a</td></tr></table>", "table { border-spacing: 1pt }", "border-spacing")]
    [InlineData("<table><tr style=\"border-top: 1pt solid red\"><td>a</td></tr></table>", "", "border-top-width")]
    [InlineData("<table><tbody style=\"border-left: 1pt solid red\"><tr><td>a</td></tr></tbody></table>", "", "border-left-width")]
    public void A_separated_table_refuses_what_it_does_not_draw(string body, string css, string property)
    {
        var error = FirstRefusal(body, css);
        (error.Code, error.Metadata![ErrorMetadataKeys.Property]).Should().Be((HtmlStyleErrorCodes.Unsupported.Value, property));
    }

    /// <summary>One row per refusal of table structure: the body, extra CSS, the refused value (null: nothing refused).</summary>
    public static TheoryData<string, string, string?> StructureRefusals { get; } = new()
    {
        // Structure the model's table does not have (CSS 2.1 §17.2.1 anonymous boxes; a second caption, a block in a
        // caption, footers).
        { "<table><caption>c</caption><caption>d</caption><tr><td>a</td></tr></table>", "", "second caption" },
        { "<table><caption><p>c</p></caption><tr><td>a</td></tr></table>", "", "block in caption" },
        { "<table><caption>c</caption><tr><td>a</td></tr><caption>d</caption></table>", "", "second caption" },
        { "<table><tbody><tr><td>a</td></tr></tbody><tfoot></tfoot></table>", "", "tfoot without rows" },
        { "<table><tfoot><tr><td>f</td></tr></tfoot><thead><tr><td>h</td></tr></thead><tr><td>a</td></tr></table>", "", "thead after a row group" },
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
        { "<table><col span=1000><colgroup span=1000></colgroup><tr><td>a</td></tr></table>", "", "2000 column elements for 1 columns" },
        { "<table><colgroup><col span=2><col span=99999></colgroup><tr><td>a</td><td>b</td></tr></table>", "", "1002 column elements for 2 columns" },
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
    [InlineData("<table style=\"width:0%\"><tr><td>a</td></tr></table>", "", "width", "0%")]
    // A width on a cell over two columns in the fixed layout shows no rule (case twd-cell-span, table b), and lengths beside
    // percent columns are not measured.
    [InlineData("<table style=\"width:200pt\"><tr><td colspan=2 style=\"width:100pt\">a</td></tr><tr><td>b</td><td>c</td></tr></table>", "table { table-layout: fixed }", "width", "100pt")]
    [InlineData("<table style=\"width:200pt\"><tr><td style=\"width:30%\">a</td><td>b</td></tr></table>", "table { table-layout: fixed }", "width", "30%")]
    [InlineData("<table style=\"margin-left:10pt\"><tr><td>a</td></tr></table>", "", "margin-left", "10pt")]
    [InlineData("<table style=\"width:auto;margin-right:10pt\"><tr><td>a</td></tr></table>", "", "margin-right", "10pt")]
    [InlineData("<table style=\"width:auto;margin-left:auto\"><tr><td>a</td></tr></table>", "", "margin-left", "auto")]
    [InlineData("<table><col style=\"width:50pt\"><col style=\"width:50%\"><tr><td>a</td><td>b</td></tr></table>", "", "width", "50%")]
    [InlineData("<table><col style=\"width:60%\"><col style=\"width:40%\"><col><tr><td>a</td><td>b</td><td>c</td></tr></table>", "", "width", "60%")]
    [InlineData("<table><col style=\"width:0%\"><col><tr><td>a</td><td>b</td></tr></table>", "", "width", "0%")]
    [InlineData("<table><col style=\"width:60%\"><col style=\"width:50%\"><col><tr><td>a</td><td>b</td><td>c</td></tr></table>", "table { width: auto }", "width", "60%")]
    [InlineData("<table><tr><td colspan=2 style=\"width:30%\">a</td></tr><tr><td>b</td><td>c</td></tr></table>", "table { table-layout: auto }", "width", "30%")]
    [InlineData("<table><col style=\"width:30%\"><col><tr><td style=\"width:40pt\">a</td><td>b</td></tr></table>", "table { table-layout: auto }", "width", "40pt")]
    [InlineData("<table style=\"width:100%\"><col style=\"width:50%\"><col><tr><td style=\"width:10pt\">a</td><td>b</td></tr></table>", "table { table-layout: fixed }", "width", "10pt")]
    [InlineData("<table><tr style=\"background:rgba(204, 204, 204, 0.5)\"><td>a</td></tr></table>", "", "background-color", "rgba(204, 204, 204, 0.5)")]
    [InlineData("<table><col style=\"border-left:1pt solid #000\"><tr><td>a</td></tr></table>", "", "border-left-width", "0.75pt")]
    [InlineData("<table><colgroup style=\"width:10pt\"><col></colgroup><tr><td>a</td></tr></table>", "", "width", "10pt")]
    [InlineData("<table><tr><td style=\"width:50%\">a</td></tr></table>", "", "width", "50%")]
    [InlineData("<table><caption style=\"width:40pt\">c</caption><tr><td>a</td></tr></table>", "", "width", "40pt")]
    [InlineData("<table><tbody style=\"height:40pt\"><tr><td>a</td></tr></tbody></table>", "", "height", "40pt")]
    [InlineData("<table><caption style=\"height:40pt\">c</caption><tr><td>a</td></tr></table>", "", "height", "40pt")]
    [InlineData("<table><tr style=\"height:50%\"><td>a</td></tr></table>", "", "height", "50%")]
    [InlineData("<table><tr><td style=\"height:30pt; box-sizing:border-box\">a</td></tr></table>", "", "box-sizing", "border-box")]
    [InlineData("<table style=\"height:90pt; box-sizing:content-box\"><tr><td>a</td></tr></table>", "", "height", "90pt")]
    [InlineData("<table style=\"height:90pt\"><caption>c</caption><tr><td>a</td></tr></table>", "", "height", "90pt")]
    [InlineData("<table style=\"height:90pt\"><tr><td rowspan=2>a</td><td>b</td></tr><tr><td>c</td></tr></table>", "", "height", "90pt")]
    [InlineData("<table style=\"height:90pt\"><tr style=\"height:20pt\"><td>a</td></tr><tr><td style=\"height:20pt\">b</td></tr></table>", "", "height", "90pt")]
    [InlineData("<table><caption style=\"margin-top:10%\">c</caption><tr><td>a</td></tr></table>", "", "margin-top", "10%")]
    public void Table_values_the_model_does_not_draw_are_refused(string body, string css, string property, string value)
    {
        var error = FirstRefusal(body, Collapsed + css);
        (error.Code, error.Metadata![ErrorMetadataKeys.Property], error.Metadata![ErrorMetadataKeys.Value]).Should().Be((HtmlStyleErrorCodes.Unsupported.Value, property, value));
    }

    [Theory]
    // No effect with collapsed borders (CSS 2.1 §17.6.1, §17.6.1.1), on a box that is no caption, or on an internal part
    // (§17.5).
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
    public void Under_warn_a_second_caption_is_left_out_and_the_table_drawn_with_the_first()
    {
        var result = Boxes(Doc("<table><caption>c</caption><caption>d</caption><tr><td>a</td></tr></table>"), UnsupportedPolicy.Warn);
        result.IsSuccess.Should().BeTrue();
        result.Value.Diagnostics.Select(error => error.Code).Should().Contain(HtmlStyleErrorCodes.Unsupported.Table);
        var table = ((Box)result.Value.Document.Root.Children.Single()).Children.OfType<Box>().Single();
        Parts(table).Select(group => group.Style.TableDisplay).Should().Equal(TableDisplay.Caption, TableDisplay.RowGroup);
    }

    /// <summary>The caption is the table's first child with the caption role, wherever it stands among the row groups,
    /// and draws on the side its <c>caption-side</c> or its <c>align</c> attribute gives.</summary>
    [Theory]
    [InlineData("<table><caption>c</caption><tr><td>a</td></tr></table>", "", CaptionSide.Top)]
    [InlineData("<table><thead><tr><th>h</th></tr></thead><caption>c</caption><tr><td>a</td></tr></table>", "", CaptionSide.Top)]
    [InlineData("<table><caption>c</caption><tr><td>a</td></tr></table>", "caption { caption-side: bottom }", CaptionSide.Bottom)]
    [InlineData("<table style=\"caption-side: bottom\"><caption>c</caption><tr><td>a</td></tr></table>", "", CaptionSide.Bottom)]
    [InlineData("<table><caption align=bottom>c</caption><tr><td>a</td></tr></table>", "", CaptionSide.Bottom)]
    public void A_caption_is_the_tables_first_child_on_its_side(string body, string css, CaptionSide side)
    {
        var result = Boxes(Doc(body, Collapsed + css));
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        var table = ((Box)result.Value.Document.Root.Children.Single()).Children.OfType<Box>().Single();
        var caption = Parts(table).First();
        (caption.Style.TableDisplay, caption.Role, caption.Style.CaptionSide).Should().Be((TableDisplay.Caption, SemanticRole.Caption, side));
        Parts(table).Skip(1).Should().OnlyContain(group => group.Style.TableDisplay != TableDisplay.Caption);
    }

    /// <summary>The heights the table draws, as the model's least heights (<see cref="BoxStyle.TableHeight"/>): a row's, a
    /// cell's content height, which under <c>box-sizing: border-box</c> in a separated table is the height less the cell's
    /// padding and border (case <c>tg-cell</c> c), and the table's border-box height.</summary>
    [Fact]
    public void Heights_on_the_table_its_rows_and_its_cells_are_least_heights_in_the_model()
    {
        var result = Boxes(Doc("<table style=\"height:120pt\"><tr style=\"height:40pt\"><td style=\"height:30pt\">a</td><td style=\"height:30pt; box-sizing:border-box; padding:6pt; border:0.75pt solid\">b</td></tr><tr><td>c</td><td>d</td></tr></table>",
            "table { border-collapse: separate; width: 100%; table-layout: fixed }"));
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        var table = ((Box)result.Value.Document.Root.Children.Single()).Children.OfType<Box>().Single();
        var row = Parts(Parts(table).Single()).First();
        var cells = Parts(row).ToList();

        (table.Style.TableHeight, row.Style.TableHeight, cells[0].Style.TableHeight).Should().Be((120.Pt(), 40.Pt(), 30.Pt()));
        cells[1].Style.TableHeight!.Value.Points.Should().BeApproximately(30 - 12 - 1.5, 1e-9);
    }

    /// <summary>A row's height and a table's are floored to 1/64 px, as Chromium's print has them (case <c>tg-row</c> e:
    /// 40.1 pt is 53.453125 px, 40.08984375 pt; <c>tg-fraction</c> c: a table of 100.1 pt is 100.08984375 pt), a cell's
    /// content height too (<c>tg-fraction</c> b: 21.37 pt is 21.36328125 pt), below what the comparison of a print can tell
    /// from the height as given.</summary>
    [Theory]
    [InlineData("<table><tr style=\"height:40.1pt\"><td>a</td></tr></table>", 40.08984375)]
    [InlineData("<table><tr><td style=\"height:21.37pt\">a</td></tr></table>", 21.36328125)]
    [InlineData("<table style=\"height:100.1pt\"><tr><td>a</td></tr></table>", 100.08984375)]
    public void Heights_in_a_table_are_floored_to_a_sixty_fourth_pixel(string body, double expected)
    {
        var result = Boxes(Doc(body, "table { border-collapse: separate; border-spacing: 0; table-layout: fixed; width: 100% } td { padding: 0; border: none }"));
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        var pages = Paginator.Paginate(LayoutEnvironment.Create(result.Value.Fonts), result.Value.Document);
        pages.IsSuccess.Should().BeTrue(pages.IsFailure ? pages.Error.ToString() : string.Empty);

        var fragments = new List<BoxFragment>();
        var pending = new Stack<Fragment>([pages.Value[0].Root.Fragment]);
        while (pending.Count > 0)
        {
            if (pending.Pop() is BoxFragment box)
            {
                fragments.Add(box);
                foreach (var child in box.Children)
                {
                    pending.Push(child.Fragment);
                }
            }
        }

        var height = body.Contains("<table style", StringComparison.Ordinal)
            ? fragments.Single(box => box.Box.Role == SemanticRole.Table).Size.Height
            : fragments.Single(box => box.Box.Style.TableDisplay == TableDisplay.Row).Size.Height;
        height.Should().BeApproximately(expected, 1e-9);
    }

    /// <summary>The backgrounds of row groups, rows, columns and column groups go to the cells: a cell without its own takes
    /// its row's, else its row group's, else its column's, else its column group's; a cell over several rows or columns the
    /// one it starts in (cases <c>tk-separated</c>, <c>tk-collapsed</c>, <c>tk-span</c>). The groups and rows keep none.</summary>
    [Fact]
    public void Backgrounds_of_groups_rows_and_columns_are_the_cells_backgrounds()
    {
        var table = Table("<table><colgroup style=\"background:#00ff00\"><col style=\"background:#0000ff\"><col><col></colgroup>"
            + "<tbody style=\"background:#ffff00\"><tr style=\"background:#ff0000\"><td style=\"background:#00ffff\">a</td><td rowspan=2>b</td><td>c</td></tr>"
            + "<tr><td>d</td><td>e</td></tr></tbody><tbody><tr><td colspan=2>f</td><td>g</td></tr><tr><td>h</td><td>i</td><td>j</td></tr></tbody></table>");
        var groups = Parts(table).ToList();
        var cells = groups.SelectMany(Parts).SelectMany(Parts).Select(cell => cell.Style.Background).ToList();
        Color red = new(255, 0, 0), yellow = new(255, 255, 0), blue = new(0, 0, 255), green = new(0, 255, 0);

        cells.Should().Equal(new Color(0, 255, 255), red, red, yellow, yellow, blue, green, blue, green, green);
        groups.Concat(groups.SelectMany(Parts)).Should().OnlyContain(part => part.Style.Background == null);
    }

    private const string SmallPage = "@page { size: 300pt 200pt; margin: 12pt } ";

    /// <summary>The pages of a document, or the error.</summary>
    private static Result<ValueList<PageFragment>, Error> Pages(string body, string css)
    {
        var result = Boxes(Doc(body, SmallPage + css));
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        return Paginator.Paginate(LayoutEnvironment.Create(result.Value.Fonts), result.Value.Document);
    }

    /// <summary>The heights of every row fragment of a page, in the order the page holds them.</summary>
    private static List<double> RowHeights(PageFragment page)
    {
        var heights = new List<double>();
        var pending = new Stack<Fragment>([page.Root.Fragment]);
        while (pending.Count > 0)
        {
            if (pending.Pop() is BoxFragment box)
            {
                if (box.Box.Style.TableDisplay == TableDisplay.Row)
                {
                    heights.Add(box.Size.Height);
                }

                foreach (var child in box.Children.Reverse())
                {
                    pending.Push(child.Fragment);
                }
            }
        }

        return heights;
    }

    /// <summary>A row whose height does not fit at the page end is cut there although its content fits, and its part on
    /// the next page is the rest of its height, in both border models and for a table's height shared over its rows (cases
    /// <c>tg-break</c>: a row of 60 pt cut after 43.125 pt goes on 16.875 pt; <c>tg-break-cut-two</c>,
    /// <c>tg-break-table</c>).</summary>
    [Theory]
    [InlineData("<tr style=\"height:60pt\"><td>a</td></tr>", "", 60)]
    [InlineData("<tr style=\"height:60pt\"><td>a</td></tr>", "table { border-collapse: separate; border-spacing: 3pt } ", 60)]
    [InlineData("<tr><td style=\"height:60pt\">a</td></tr>", "", 60)]
    [InlineData("<tr><td>a</td></tr>", "table { height: 120pt } ", 120)]
    public void A_row_with_a_height_cut_at_the_page_end_goes_on_with_the_rest_of_its_height(string row, string css, double height)
    {
        var pages = Pages($"<p style=\"padding-top:120pt\">first</p><table>{row}</table>", Collapsed + css);
        pages.IsSuccess.Should().BeTrue(pages.IsFailure ? pages.Error.ToString() : string.Empty);

        var first = RowHeights(pages.Value[0]);
        var second = RowHeights(pages.Value[1]);
        (first.Count, second.Count).Should().Be((1, 1));
        first[0].Should().BePositive();
        second[0].Should().BePositive();
        (first[0] + second[0]).Should().BeApproximately(height, 1e-9);
    }

    /// <summary>A row taller than the page by its height runs over as many pages as it needs, each part the rest of the
    /// height the page before left (case <c>tg-break-tall</c>: rows of 250 and 400 pt).</summary>
    [Fact]
    public void A_row_taller_than_the_page_by_its_height_runs_over_pages()
    {
        var pages = Pages("<table><tr style=\"height:400pt\"><td>a</td></tr></table>", Collapsed);
        pages.IsSuccess.Should().BeTrue(pages.IsFailure ? pages.Error.ToString() : string.Empty);

        var parts = pages.Value.SelectMany(RowHeights).ToList();
        parts.Should().HaveCount(3);
        parts.Sum().Should().BeApproximately(399.99609375, 1e-9, "400 pt is 533.328125 px on the 1/64 px step");
    }

    /// <summary>Over a page break no case measured a table with heights where a cell over several rows is cut: it is
    /// refused, with the room the table had.</summary>
    [Fact]
    public void A_table_with_heights_whose_break_cuts_a_row_span_is_refused()
    {
        var pages = Pages("<table><tr style=\"height:20pt\"><td rowspan=2>a</td><td>b</td></tr><tr style=\"height:200pt\"><td>c</td></tr></table>", Collapsed);

        pages.IsFailure.Should().BeTrue();
        pages.Error.Code.Should().Be(LayoutErrorCodes.TableBreak);
    }

    /// <summary>A table with collapsed borders keeps its background, which it paints under its border box as a block box
    /// does, and on a page where it breaks its fragment runs down to the page end, below its last row there (cases
    /// <c>tk-table-collapsed</c>, <c>tk-table-caption</c>, <c>tk-table-break</c>: the background to the page end at
    /// 188.004 pt, 13.25 pt below the last row).</summary>
    [Fact]
    public void A_collapsed_table_keeps_its_background_and_runs_to_the_page_end_where_it_breaks()
    {
        Table("<table><tr><td>a</td></tr></table>", Collapsed + "table { background: #ffff00 }").Style.Background.Should().Be(new Color(255, 255, 0));

        var rows = string.Concat(Enumerable.Range(1, 16).Select(row => $"<tr><td>row{row}</td></tr>"));
        var pages = Pages($"<table style=\"background:#ffff00\">{rows}</table>", Collapsed);
        pages.IsSuccess.Should().BeTrue(pages.IsFailure ? pages.Error.ToString() : string.Empty);
        pages.Value.Should().HaveCount(2);

        var (top, table) = TableOf(pages.Value[0]);
        var lastRow = table.Children.Where(child => child.Fragment is BoxFragment).Max(child => child.Offset.Y + child.Fragment.Size.Height);
        (top + table.Size.Height).Should().BeApproximately(188.25, 1e-6, "the page area of 176 pt is 235 px, from 12 pt");
        table.Size.Height.Should().BeGreaterThan(lastRow + 1);
        var (_, last) = TableOf(pages.Value[1]);
        last.Size.Height.Should().BeApproximately(last.Children.Where(child => child.Fragment is BoxFragment).Max(child => child.Offset.Y + child.Fragment.Size.Height), 1e-6);
    }

    /// <summary>The table's fragment of a page and its top on the page.</summary>
    private static (double Top, BoxFragment Table) TableOf(PageFragment page)
    {
        var pending = new Stack<(double Top, Fragment Fragment)>([(page.Root.Offset.Y, page.Root.Fragment)]);
        while (pending.Count > 0)
        {
            var (top, fragment) = pending.Pop();
            if (fragment is BoxFragment box)
            {
                if (box.Box.Role == SemanticRole.Table)
                {
                    return (top, box);
                }

                foreach (var child in box.Children)
                {
                    pending.Push((top + child.Offset.Y, child.Fragment));
                }
            }
        }

        throw new InvalidOperationException("the page holds no table");
    }

    /// <summary>A cell's <c>vertical-align: baseline</c>, on the cell, from <c>valign</c> or from its row, is drawn as top, as
    /// Chromium prints it (cases <c>tv-baseline-sizes</c>, <c>tv-baseline-height</c>).</summary>
    [Theory]
    [InlineData("<table><tr><td style=\"vertical-align:baseline\">a</td></tr></table>")]
    [InlineData("<table><tr><td valign=baseline>a</td></tr></table>")]
    [InlineData("<table><tr valign=baseline><td>a</td></tr></table>")]
    [InlineData("<table><tbody style=\"vertical-align:baseline\"><tr><td>a</td></tr></tbody></table>")]
    public void A_cell_aligned_on_the_baseline_is_drawn_at_the_top(string body)
    {
        var cell = Parts(Parts(Parts(Table(body)).First()).First()).First();

        cell.Style.VerticalAlign.Should().Be(VerticalAlign.Top);
    }

    private const string Hide = "table { border-collapse: separate; empty-cells: hide } td { border: 0.75pt solid #0000ff; padding: 2pt; background: #00ff00 }";

    /// <summary>Under <c>empty-cells: hide</c> with separated borders a cell without content, of white space or of a comment
    /// paints no border and no background of its own and keeps its size, its border widths as padding; a no-break space and
    /// an empty inline element are content (case <c>tm-empty</c>).</summary>
    [Theory]
    [InlineData("", true)]
    [InlineData(" \n\t ", true)]
    [InlineData("<!-- c -->", true)]
    [InlineData("&nbsp;", false)]
    [InlineData("<span></span>", false)]
    public void Under_empty_cells_hide_an_empty_cell_paints_no_border_or_background_and_keeps_its_size(string content, bool hidden)
    {
        var cells = Parts(Parts(Parts(Table($"<table><tr><td>a</td><td>{content}</td></tr></table>", Collapsed + Hide)).First()).First()).ToList();
        var (shown, cell) = (cells[0].Style, cells[1].Style);
        var room = shown.Padding.Top + shown.Border.Top.Width;

        (cell.Border == Border.None, cell.Background, cell.Padding).Should().Be(hidden
            ? (true, null, Edges.All(room))
            : (false, new Color(0, 255, 0), shown.Padding));
    }

    /// <summary>A hidden empty cell shows its row's background, not its own (cases <c>tk-empty</c>, <c>tm-empty</c>); with
    /// collapsed borders <c>empty-cells</c> has no effect (<c>tm-collapse</c>).</summary>
    [Fact]
    public void A_hidden_empty_cell_shows_its_rows_background_and_collapsed_borders_hide_nothing()
    {
        var cell = Parts(Parts(Parts(Table("<table><tr style=\"background:#00ffff\"><td></td></tr></table>", Collapsed + Hide)).First()).First()).First();
        cell.Style.Background.Should().Be(new Color(0, 255, 255));

        var collapsed = Parts(Parts(Parts(Table("<table><tr><td></td></tr></table>", Collapsed + Hide + " table { border-collapse: collapse }")).First()).First()).First();
        (collapsed.Style.Background, collapsed.Style.Border.Top.Width).Should().Be((new Color(0, 255, 0), Length.FromPoints(0.75)));
    }

    /// <summary>Under <c>empty-cells: hide</c> no case measures whether a cell of only elements that are not displayed, or of
    /// white space that is preserved, is empty: both are refused at the cell.</summary>
    [Theory]
    [InlineData("<td><span style=\"display:none\">a</span></td>")]
    [InlineData("<td style=\"white-space:pre\">   </td>")]
    public void Under_empty_cells_hide_a_cell_not_measured_is_refused(string cell)
    {
        var error = FirstRefusal($"<table><tr>{cell}</tr></table>", Collapsed + Hide);

        (error.Code, error.Metadata![ErrorMetadataKeys.Property]).Should().Be((HtmlStyleErrorCodes.Unsupported.Value, "empty-cells"));
    }

    /// <summary>The number of lines of a page.</summary>
    private static int LineCount(PageFragment page)
    {
        var count = 0;
        var pending = new Stack<Fragment>([page.Root.Fragment]);
        while (pending.Count > 0)
        {
            switch (pending.Pop())
            {
                case BoxFragment box:
                    foreach (var child in box.Children)
                    {
                        pending.Push(child.Fragment);
                    }

                    break;
                case SliceFragment slice:
                    pending.Push(slice.Content.Fragment);
                    break;
                case LineFragment:
                    count++;
                    break;
            }
        }

        return count;
    }

    /// <summary>Where the page end cuts the text of a cell in a row with a height, the row is cut between two lines and
    /// goes on with the rest of its height (cases <c>tg-text-late</c>, <c>tg-text-three</c>), or with the rest of its text
    /// where that is taller (<c>tg-text-equal</c>, <c>tg-text-header</c>), in both border models. 80 pt is 79.9921875 pt on the
    /// 1/64 px step.</summary>
    [Theory]
    [InlineData("height:80pt", "", 79.9921875)]
    [InlineData("height:80pt", "table { border-collapse: separate; border-spacing: 3pt } ", 79.9921875)]
    [InlineData("height:20pt", "", 0)]
    [InlineData("height:20pt", "table { border-collapse: separate; border-spacing: 3pt } ", 0)]
    public void A_row_with_a_height_whose_text_the_page_end_cuts_goes_on_with_the_rest_of_its_height_or_text(string height, string css, double least)
    {
        var pages = Pages($"<p style=\"padding-top:104pt\">first</p><table><tr style=\"{height}\"><td>one<br>more<br>lines<br>here</td><td>two</td></tr></table>", Collapsed + css);
        pages.IsSuccess.Should().BeTrue(pages.IsFailure ? pages.Error.ToString() : string.Empty);

        (LineCount(pages.Value[0]), LineCount(pages.Value[1])).Should().Be((4, 2), "first, one, two and more on the first page");
        var first = RowHeights(pages.Value[0]);
        var second = RowHeights(pages.Value[1]);
        (first.Count, second.Count).Should().Be((1, 1));
        var text = Pages("<table><tr><td>lines<br>here</td></tr></table>", Collapsed + css).Value.SelectMany(RowHeights).Single();
        second[0].Should().BeGreaterThanOrEqualTo(Math.Max(least - first[0], text) - 1);
        if (least > 0)
        {
            (first[0] + second[0]).Should().BeApproximately(least, 1e-9);
        }
    }

    /// <summary>A cell of two lines whose second line does not fit at the page end would leave one line on the page, which
    /// its orphans forbid: the row with a height moves whole to the next page, as a row without one does (cases
    /// <c>tg-text-two</c>, <c>tg-text-align</c>).</summary>
    [Fact]
    public void A_row_with_a_height_whose_cell_would_leave_one_line_moves_whole()
    {
        var pages = Pages("<p style=\"padding-top:116pt\">first</p><table><tr style=\"height:60pt\"><td>one<br>more</td><td>two</td></tr></table>", Collapsed);
        pages.IsSuccess.Should().BeTrue(pages.IsFailure ? pages.Error.ToString() : string.Empty);

        RowHeights(pages.Value[0]).Should().BeEmpty();
        RowHeights(pages.Value[1]).Should().ContainSingle().Which.Should().BeApproximately(60, 1e-9);
    }

    [Theory]
    [InlineData("left")]
    [InlineData("block-end")]
    public void A_caption_side_other_than_top_or_bottom_is_refused(string side) =>
        FirstRefusal("<table><caption>c</caption><tr><td>a</td></tr></table>", $"{Collapsed}caption {{ caption-side: {side} }}").Metadata![ErrorMetadataKeys.Property].Should().Be("caption-side");
}
