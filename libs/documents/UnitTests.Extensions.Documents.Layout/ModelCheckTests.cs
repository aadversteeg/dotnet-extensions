using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.ErrorPaths;
using static UnitTests.Extensions.Documents.Layout.Models;

namespace UnitTests.Extensions.Documents.Layout;

/// <summary>
/// A model built by hand is not trusted: <see cref="DocumentLayout.Run(StyledDocument, Ave.Extensions.Documents.Fonts.FontSet)"/>
/// checks it against the value ranges and the nesting limit the code front-end enforces (<see cref="ModelCheck"/>,
/// <see cref="ModelLimits"/>) and answers with an error, where such values made it throw, overflow the stack or lay out
/// overlapping lines.
/// </summary>
public class ModelCheckTests
{
    private static Error Refused(StyledDocument document)
    {
        var result = DocumentLayout.Run(document, TestFonts.Set);
        result.IsFailure.Should().BeTrue("the model breaks a limit of ModelLimits");
        return result.Error;
    }

    private static StyledDocument WithStyle(BoxStyle style) => Document(Paragraph(1, "one two three", style));

    private static StyledDocument WithText(TextStyle text) =>
        Document(new Box(new NodeId(1), SemanticRole.Paragraph, LayoutAlgorithmId.Inline, BoxStyle.Initial, [new TextRun(new NodeId(2), "x", text, At(0))], At(0)));

    [Theory]
    [InlineData(0, 2)]
    [InlineData(-1, 2)]
    [InlineData(int.MinValue, 2)]
    [InlineData(2, 0)]
    public void Orphans_or_widows_below_one_are_refused(int orphans, int widows)
    {
        // These made Run throw an IndexOutOfRangeException or "no progress".
        var error = Refused(WithStyle(BoxStyle.Initial with { Orphans = orphans, Widows = widows }));

        error.Code.Should().Be(orphans < 1 ? DocumentErrorCodes.Input.Invalid.Orphans : DocumentErrorCodes.Input.Invalid.Widows);
        error.Metadata![ErrorMetadataKeys.Node].Should().Be("#1");
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(14_400.5)]
    public void A_line_height_that_is_negative_not_finite_or_longer_than_the_limit_is_refused(double points)
    {
        var error = Refused(WithText(Text() with { LineHeight = new LineHeight.Fixed(points.Pt()) }));

        error.Code.Should().Be(DocumentErrorCodes.Input.Invalid.LineHeight);
    }

    [Theory]
    [InlineData(-0.5)]
    [InlineData(double.NaN)]
    [InlineData(14_400.5)]
    public void A_line_height_number_that_is_negative_not_finite_or_beyond_the_limit_is_refused(double factor)
    {
        Refused(WithText(Text() with { LineHeight = new LineHeight.Factor(factor) })).Code.Should().Be(DocumentErrorCodes.Input.Invalid.LineHeight);
    }

    [Fact]
    public void A_line_height_at_the_limit_is_laid_out()
    {
        // 14 400 pt is allowed; such a line does not fit the page, which is a layout answer, not a model problem.
        DocumentLayout.Run(WithText(Text() with { LineHeight = new LineHeight.Fixed(14_400.Pt()) }), TestFonts.Set).Error.Code.Should().Be(LayoutErrorCodes.DoesNotFit);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-10.0)]
    [InlineData(double.NaN)]
    [InlineData(14_400.5)]
    public void A_font_size_that_is_not_positive_finite_and_within_the_limit_is_refused(double points)
    {
        Refused(WithText(Text() with { FontSize = points.Pt() })).Code.Should().Be(DocumentErrorCodes.Input.Invalid.FontSize);
    }

    [Fact]
    public void A_blank_font_family_and_a_weight_outside_1_to_1000_are_refused()
    {
        Refused(WithText(Text() with { FontFamily = " " })).Code.Should().Be(DocumentErrorCodes.Input.Invalid.FontFamily);
        Refused(WithText(Text() with { FontWeight = new FontWeight(0) })).Code.Should().Be(DocumentErrorCodes.Input.Invalid.FontWeight);
    }

    [Fact]
    public void A_strut_is_checked_as_a_run_is()
    {
        Refused(WithStyle(BoxStyle.Initial with { Text = Text() with { LineHeight = new LineHeight.Factor(-1) } })).Code.Should().Be(DocumentErrorCodes.Input.Invalid.LineHeight);
    }

    [Fact]
    public void Negative_margins_are_unsupported_and_negative_or_infinite_padding_and_borders_are_invalid()
    {
        Refused(WithStyle(BoxStyle.Initial with { Margin = new Edges((-1.0).Pt(), 0.Pt(), 0.Pt(), 0.Pt()) })).Code.Should().Be(DocumentErrorCodes.Input.Unsupported.NegativeMargin);
        Refused(WithStyle(BoxStyle.Initial with { Padding = Edges.All((-1.0).Pt()) })).Code.Should().Be(DocumentErrorCodes.Input.Invalid.Length);
        Refused(WithStyle(BoxStyle.Initial with { Padding = Edges.All(double.PositiveInfinity.Pt()) })).Code.Should().Be(DocumentErrorCodes.Input.Invalid.Length);
        Refused(WithStyle(BoxStyle.Initial with { Border = Border.All(new BorderSide(14_401.Pt(), Color.Black)) })).Code.Should().Be(DocumentErrorCodes.Input.Invalid.Length);
    }

    [Fact]
    public void A_page_size_out_of_range_and_a_negative_page_margin_are_refused()
    {
        var document = Document(Paragraph(1, "x"));

        Refused(document with { Page = new PageStyle(0.Pt(), 100.Pt(), Edges.Zero) }).Code.Should().Be(DocumentErrorCodes.Input.Invalid.PageSize);
        Refused(document with { Page = new PageStyle(20_000.Pt(), 100.Pt(), Edges.Zero) }).Code.Should().Be(DocumentErrorCodes.Input.Invalid.PageSize);
        Refused(document with { Page = new PageStyle(200.Pt(), 100.Pt(), Edges.All((-1.0).Pt())) }).Code.Should().Be(DocumentErrorCodes.Input.Invalid.Length);
    }

    [Fact]
    public void Text_with_a_character_text_cannot_hold_is_refused()
    {
        Refused(Document(Paragraph(1, "a\u0001b"))).Code.Should().Be(DocumentErrorCodes.Input.Invalid.Text);
    }

    [Fact]
    public void A_heading_level_outside_1_to_6_is_refused()
    {
        var heading = Paragraph(1, "x") with { Role = new SemanticRole.HeadingRole(7) };

        Refused(Document(heading)).Code.Should().Be(DocumentErrorCodes.Input.Invalid.HeadingLevel);
    }

    [Fact]
    public void Boxes_nested_to_the_limit_are_laid_out_and_one_level_more_is_refused()
    {
        static Box Nested(int levels)
        {
            var box = Paragraph(1_000_000, "x");
            for (var level = 1; level < levels; level++)
            {
                box = Flow(level, [box]);
            }

            return box;
        }

        DocumentLayout.Run(Document(Nested(ModelLimits.MaxNestingDepth)), TestFonts.Set).IsSuccess.Should().BeTrue();
        Refused(Document(Nested(ModelLimits.MaxNestingDepth + 1))).Code.Should().Be(DocumentErrorCodes.Input.Invalid.TooDeep);
    }

    [Fact]
    public void A_model_nested_far_deeper_than_the_limit_is_refused_without_running_out_of_stack()
    {
        // 512 levels overflowed a 1 MB stack. Built and checked with loops only.
        Box box = Paragraph(1, "x");
        for (var level = 0; level < 100_000; level++)
        {
            box = new Box(new NodeId(level + 2), SemanticRole.Generic, LayoutAlgorithmId.Flow, BoxStyle.Initial, [box], At(0));
        }

        Refused(Document(box)).Code.Should().Be(DocumentErrorCodes.Input.Invalid.TooDeep);
    }

    [Fact]
    public void A_table_counts_as_one_level_with_its_groups_rows_and_cells()
    {
        // 63 blocks, then a table at level 64 whose cell holds a paragraph at level 65: refused as the front-end refuses
        // a table at level 64 with content in a cell. The same table at level 63 is laid out.
        static Box Table(Box content) =>
            new(new NodeId(500), SemanticRole.Table, LayoutAlgorithmId.Tracks, BoxStyle.Initial with { Columns = [new TrackSize.Fill(1)] },
                [new Box(new NodeId(501), SemanticRole.TableBody, LayoutAlgorithmId.Flow, BoxStyle.Initial with { TableDisplay = TableDisplay.RowGroup },
                    [new Box(new NodeId(502), SemanticRole.TableRow, LayoutAlgorithmId.Flow, BoxStyle.Initial with { TableDisplay = TableDisplay.Row },
                        [new Box(new NodeId(503), SemanticRole.TableCell, LayoutAlgorithmId.Flow, BoxStyle.Initial, [content], At(0))], At(0))], At(0))], At(0));
        static Box Inside(int blocks, Box innermost)
        {
            var box = innermost;
            for (var level = 0; level < blocks; level++)
            {
                box = Flow(level + 600, [box]);
            }

            return box;
        }

        DocumentLayout.Run(Document(Inside(ModelLimits.MaxNestingDepth - 2, Table(Paragraph(1, "x")))), TestFonts.Set).IsSuccess.Should().BeTrue();
        Refused(Document(Inside(ModelLimits.MaxNestingDepth - 1, Table(Paragraph(1, "x"))))).Code.Should().Be(DocumentErrorCodes.Input.Invalid.TooDeep);
    }
}
