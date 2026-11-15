using System.Runtime.CompilerServices;
using Ave.Documents.Core;
using Ave.Documents.DocumentModel;
using Ave.Documents.Fluent;
using Ave.Documents.TestSupport;
using Ave.Extensions.ErrorPaths;
using static Ave.Documents.Fluent.Elements;

namespace UnitTests.Ave.Documents.Fluent;

/// <summary>
/// The stage-2 elements and modifiers of the code front-end: tables, rows of blocks, images, page header and footer,
/// page counters, borders and keep rules — their translation, their order-free modifiers and their input errors.
/// </summary>
public class Stage2FrontEndTests
{
    private static readonly Color Grey = new(0xD3, 0xD3, 0xD3);

    private static Document Doc(params Element[] body) => Document([.. body]).FontFamily("Lato");

    private static StyledDocument Model(Document document)
    {
        var result = DocumentTranslator.ToModel(document);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        return result.Value;
    }

    private static Error Fail(Document document)
    {
        var result = DocumentTranslator.ToModel(document);
        result.IsFailure.Should().BeTrue();
        return result.Error;
    }

    private static Box First(StyledDocument model) => (Box)model.Root.Children[0];

    [Fact]
    public void A_table_becomes_table_row_groups_rows_and_cells_with_their_roles_and_layouts()
    {
        var table = Table(
                Columns(2.Cm(), Fill(1), Auto),
                [
                    HeaderRow([Cell("Date"), Cell("Task"), Cell("Hours")]).Bold(),
                    TableRow([Cell("1"), Cell("a"), Cell("2,00")]),
                    RowGroup([TableRow([Cell("2").RowSpan(2), Cell("b"), Cell("1,00")]), TableRow([Cell("desc").ColumnSpan(2)])]).KeepTogether(),
                ])
            .GridLines(1.Pt(), Grey)
            .CellPadding(4.Pt(), 2.Pt());

        var box = First(Model(Doc(table)));

        (box.Layout, box.Role).Should().Be((LayoutAlgorithmId.Tracks, SemanticRole.Table));
        box.Style.Columns.Should().Equal(new TrackSize.Fixed(2.Cm()), new TrackSize.Fill(1), TrackSize.Auto.Instance);
        var groups = box.Children.Cast<Box>().ToArray();
        groups.Select(group => (group.Layout, group.Role)).Should().Equal(
            (LayoutAlgorithmId.TableHeaderGroup, SemanticRole.TableHead),
            (LayoutAlgorithmId.TableRowGroup, SemanticRole.TableBody),
            (LayoutAlgorithmId.TableRowGroup, SemanticRole.TableBody));
        groups[2].Style.BreakInside.Should().Be(BreakInside.Avoid);
        var headerCell = (Box)((Box)groups[0].Children[0]).Children[0];
        headerCell.Role.Should().Be(SemanticRole.TableHeaderCell);
        headerCell.Style.Padding.Should().Be(new Edges(4.Pt(), 2.Pt(), 4.Pt(), 2.Pt()), "the table's cell padding default");
        headerCell.Style.Border.Should().Be(Border.All(new BorderSide(1.Pt(), Grey)), "the table's grid lines");
        ((TextRun)((Box)headerCell.Children[0]).Children[0]).Style.FontWeight.Should().Be(FontWeight.Bold, "the row's text is inherited by its cells");
        var spanning = (Box)((Box)groups[2].Children[0]).Children[0];
        spanning.Style.Span.Should().Be(new CellSpan(1, 2));
    }

    [Fact]
    public void A_cell_s_own_border_and_padding_win_over_the_table_defaults()
    {
        var table = Table(Columns(Fill(1)), [TableRow([Cell("x").Border(2.Pt(), Color.Black).PaddingTop(0.Pt())])]).GridLines(1.Pt(), Grey).CellPadding(4.Pt());

        var cell = (Box)((Box)((Box)First(Model(Doc(table))).Children[0]).Children[0]).Children[0];

        cell.Style.Border.Should().Be(Border.All(new BorderSide(2.Pt(), Color.Black)));
        cell.Style.Padding.Should().Be(new Edges(0.Pt(), 4.Pt(), 4.Pt(), 4.Pt()));
    }

    [Fact]
    public void A_row_of_blocks_is_a_one_row_tracks_box_with_generic_roles_and_equal_fill_columns_by_default()
    {
        var row = Row([Paragraph("left"), Paragraph("right")]).VerticalAlign(VerticalAlign.Middle);

        var box = First(Model(Doc(row)));

        (box.Layout, box.Role).Should().Be((LayoutAlgorithmId.Tracks, SemanticRole.Generic));
        box.Style.Columns.Should().Equal(new TrackSize.Fill(1), new TrackSize.Fill(1));
        var cells = ((Box)((Box)box.Children[0]).Children[0]).Children.Cast<Box>().ToArray();
        cells.Should().AllSatisfy(cell => (cell.Role, cell.Style.VerticalAlign).Should().Be((SemanticRole.Generic, VerticalAlign.Middle)));
    }

    [Fact]
    public void An_image_becomes_a_figure_box_with_the_decoded_image()
    {
        var image = Image(TestImages.Bytes("rgba8.png")).Width(3.Cm()).MarginBottom(4.Pt());

        var box = First(Model(Doc(image)));

        (box.Layout, box.Role).Should().Be((LayoutAlgorithmId.Image, SemanticRole.Figure));
        var node = (ImageNode)box.Children.Single();
        (node.Image.PixelWidth, node.Width, node.Height).Should().Be((7, (Length?)3.Cm(), (Length?)null));
    }

    [Fact]
    public void Page_counters_become_counter_runs_and_header_and_footer_get_ids_after_the_body()
    {
        var footer = Paragraph([Text("Page "), PageNumber(), Text(" of "), PageCount()]);
        var page = PageSetup.A4.Margin(1.Cm()).Header(Paragraph("Head")).Footer(footer, 1.Cm()).FirstPageHeader(Block([]));

        var model = Model(Document([Paragraph("Body")], page).FontFamily("Lato"));

        model.Page.Margins.Should().Be(Edges.All(1.Cm()));
        var runs = ((Box)model.Page.Template.Footer!.Content.Children[0]).Children;
        runs.OfType<CounterRun>().Select(run => run.Counter).Should().Equal(PageCounter.PageNumber, PageCounter.PageCount);
        model.Page.Template.Footer.Height.Should().Be(1.Cm());
        model.Page.FirstPage!.Header!.Content.Children.Single().Should().BeOfType<Box>().Which.Children.Should().BeEmpty();
        model.Page.FirstPage.Footer.Should().Be(model.Page.Template.Footer, "the first page keeps the normal footer when it sets none");
        model.Page.Template.Header!.Content.Id.Value.Should().BeGreaterThan(2, "the body has ids 0 to 2");
    }

    [Fact]
    public void Border_italic_and_keep_modifiers_are_order_free()
    {
        var x = Paragraph("x");
        var a = x.Border(1.Pt(), Grey).BorderTop(2.Pt(), Color.Black).Italic().KeepTogether().KeepWithNext().KeepWithPrevious();
        var b = x.KeepWithPrevious().KeepWithNext().KeepTogether().Italic().BorderTop(2.Pt(), Color.Black).Border(1.Pt(), Grey);

        a.Should().Be(b);
        var style = First(Model(Doc(a))).Style;
        style.Border.Should().Be(new Border(new BorderSide(2.Pt(), Color.Black), new BorderSide(1.Pt(), Grey), new BorderSide(1.Pt(), Grey), new BorderSide(1.Pt(), Grey)));
        (style.BreakBefore, style.BreakAfter, style.BreakInside).Should().Be((BreakBetween.Avoid, BreakBetween.Avoid, BreakInside.Avoid));
        ((TextRun)First(Model(Doc(a))).Children[0]).Style.FontStyle.Should().Be(FontStyle.Italic);
    }

    [Fact]
    public void A_page_break_wins_over_keep_with_previous_on_the_same_element()
    {
        var style = ((Box)Model(Doc(Paragraph("a"), PageBreak(), Paragraph("b").KeepWithPrevious())).Root.Children[1]).Style;

        style.BreakBefore.Should().Be(BreakBetween.Page);
    }

    [Fact]
    public void Invalid_columns_are_reported_at_the_table()
    {
        var line = CurrentLine() + 1;
        var error = Fail(Doc(Table(Columns(Fill(0), (-1).Pt()), [TableRow([Cell("a"), Cell("b")])])));

        var parts = ResultCollectionsParts(error);
        parts.Should().HaveCount(2).And.AllSatisfy(part => ShouldPointAt(part, DocumentErrorCodes.Input.Invalid.Columns, line));
    }

    [Fact]
    public void A_header_row_after_a_body_row_is_a_table_error_at_the_header_row()
    {
        var line = CurrentLine() + 2;
        var error = Fail(Doc(Table(Columns(Fill(1)), [TableRow([Cell("a")]),
            HeaderRow([Cell("h")])])));

        ShouldPointAt(error, DocumentErrorCodes.Input.Invalid.Table, line);
    }

    [Fact]
    public void A_cell_span_below_one_is_a_table_error_at_the_cell()
    {
        var line = CurrentLine() + 1;
        var error = Fail(Doc(Table(Columns(Fill(1)), [TableRow([Cell("a").ColumnSpan(0)])])));

        ShouldPointAt(error, DocumentErrorCodes.Input.Invalid.Table, line);
    }

    [Fact]
    public void Padding_on_a_table_and_margin_on_a_row_do_not_apply()
    {
        var error = Fail(Doc(Table(Columns(Fill(1)), [TableRow([Cell("a")]).MarginTop(2.Pt())]).Padding(3.Pt())));

        ResultCollectionsParts(error).Select(part => part.Code).Should().Equal(DocumentErrorCodes.Input.Invalid.NotApplicable, DocumentErrorCodes.Input.Invalid.NotApplicable);
    }

    [Fact]
    public void A_page_break_inside_a_cell_is_reported_at_the_page_break()
    {
        var line = CurrentLine() + 1;
        var error = Fail(Doc(Table(Columns(Fill(1)), [TableRow([Cell([Paragraph("a"), PageBreak(), Paragraph("b")])])])));

        ShouldPointAt(error, DocumentErrorCodes.Input.Invalid.PageBreak, line);
    }

    [Fact]
    public void A_row_with_column_sizes_needs_one_per_item()
    {
        Fail(Doc(Row([Paragraph("a"), Paragraph("b")]).Columns(Fill(1)))).Code.Should().Be(DocumentErrorCodes.Input.Invalid.Columns);
    }

    [Fact]
    public void An_image_that_cannot_be_decoded_is_reported_at_the_image_call()
    {
        var line = CurrentLine() + 1;
        var error = Fail(Doc(Image("not an image"u8)));

        ShouldPointAt(error, DocumentErrorCodes.Image.Unsupported, line);
    }

    [Fact]
    public void An_unsupported_image_variant_names_the_variant_at_the_image_call()
    {
        var line = CurrentLine() + 1;
        var error = Fail(Doc(Image(TestImages.Bytes("grey16.png"))));

        ShouldPointAt(error, DocumentErrorCodes.Image.Unsupported, line);
        error.Metadata![ErrorMetadataKeys.Value].Should().Be("PNG greyscale with bit depth 16");
    }

    [Fact]
    public void An_image_size_must_be_positive()
    {
        var line = CurrentLine() + 1;
        var error = Fail(Doc(Image(TestImages.Bytes("rgb8.png")).Height(0.Pt())));

        ShouldPointAt(error, DocumentErrorCodes.Input.Invalid.ImageSize, line);
    }

    [Fact]
    public void A_negative_border_width_is_an_invalid_length()
    {
        var line = CurrentLine() + 1;
        var error = Fail(Doc(Block([]).BorderLeft((-1).Pt(), Grey)));

        ShouldPointAt(error, DocumentErrorCodes.Input.Invalid.Length, line);
    }

    [Fact]
    public void Two_documents_with_the_same_image_bytes_are_equal_values()
    {
        Document Build() => Doc(Image(TestImages.Bytes("rgb8.png")));

        Build().Body[0].Should().Be(Build().Body[0] with { });
        ((ImageElement)Build().Body[0]).File.Should().Be(((ImageElement)Build().Body[0]).File);
    }

    private static IReadOnlyList<Error> ResultCollectionsParts(Error error) => ResultCollections.Parts(error);

    private static void ShouldPointAt(Error error, ErrorCode code, int line)
    {
        error.Code.Should().Be(code);
        error.Metadata![ErrorMetadataKeys.SourceFile].Should().Be(ThisFile());
        error.Metadata![ErrorMetadataKeys.SourceLine].Should().Be(line);
    }

    private static int CurrentLine([CallerLineNumber] int line = 0) => line;

    private static string ThisFile([CallerFilePath] string file = "") => file;
}
