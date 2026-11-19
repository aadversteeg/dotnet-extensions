using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;
using static UnitTests.Extensions.Documents.Layout.Models;

namespace UnitTests.Extensions.Documents.Layout;

/// <summary>
/// The four layout algorithms are public, and so are the break tokens a caller hands them: a caller can build a token,
/// change one with <c>with</c>, or pass the token one box returned to another box. No such value makes an algorithm
/// throw. A token of another algorithm's kind, or one whose position the box does not have (a child, line, row or cell
/// outside it, a height shown that is negative, not finite, or the whole height of the sliced image or line or more),
/// is the error <c>Documents.Layout.InvalidBreakToken</c>,
/// naming the box. Pagination never meets it: it hands each box the token that box's own layout returned.
/// </summary>
public class BreakTokenCheckTests
{
    private static readonly LayoutEnvironment Environment = LayoutEnvironment.Create(TestFonts.Set);

    private static ConstraintSpace Space(double height = 80) => new(180, height, IsAtPageStart: true);

    private static Box FlowBox() => Flow(1, [Paragraph(2, "a"), Paragraph(3, "b")]);

    private static Box ImageBox(int id = 1, Length? height = null) =>
        new(new NodeId(id), SemanticRole.Figure("A test image"), LayoutAlgorithmId.Image, BoxStyle.Initial,
            [new ImageNode(new NodeId(id + 1), TestImages.Load("baseline-rgb.jpg"), null, height, At(0))], At(0));

    /// <summary>A table of one column and <paramref name="rows"/> rows of one line each.</summary>
    private static Box TableBox(int rows)
    {
        var t = new TableModels();
        return t.Table([new TrackSize.Fill(1)], t.Body([.. Enumerable.Range(0, rows).Select(row => t.Row(t.Cell($"r{row}")))]));
    }

    private static readonly Dictionary<string, BreakToken> NotForFlow = new()
    {
        ["an inline token"] = new InlineBreakToken(0),
        ["a table token"] = new TracksBreakToken(0, [], RowIsSplit: false),
        ["an image token"] = ImageBreakToken.Instance,
        ["child -1"] = new FlowBreakToken(-1, null),
        ["the smallest child index"] = new FlowBreakToken(int.MinValue, null),
        ["a child past the end"] = new FlowBreakToken(3, null),
        ["inside a child past the last"] = new FlowBreakToken(2, new InlineBreakToken(0)),
    };

    private static readonly Dictionary<string, BreakToken> NotForInline = new()
    {
        ["a flow token"] = new FlowBreakToken(0, null),
        ["a table token"] = new TracksBreakToken(0, [], RowIsSplit: false),
        ["an image token"] = ImageBreakToken.Instance,
        ["line -1"] = new InlineBreakToken(-1),
        ["the smallest line index"] = new InlineBreakToken(int.MinValue),
        ["a negative height shown"] = new InlineBreakToken(0) { Shown = -1, Slices = 1 },
        ["a height shown that is not a number"] = new InlineBreakToken(0) { Shown = double.NaN, Slices = 1 },
        ["an infinite height shown"] = new InlineBreakToken(0) { Shown = double.PositiveInfinity, Slices = 1 },
        ["a height shown in no slice"] = new InlineBreakToken(0) { Shown = 5, Slices = 0 },
        ["slices that showed nothing"] = new InlineBreakToken(0) { Shown = 0, Slices = 2 },
        ["a negative slice count"] = new InlineBreakToken(0) { Shown = 5, Slices = -1 },
    };

    private static readonly Dictionary<string, BreakToken> NotForImage = new()
    {
        ["a flow token"] = new FlowBreakToken(0, null),
        ["an inline token"] = new InlineBreakToken(0),
        ["a table token"] = new TracksBreakToken(0, [], RowIsSplit: false),
    };

    // The table of three rows: cell 0 is row 0, cell 1 row 1, cell 2 row 2.
    private static readonly Dictionary<string, BreakToken> NotForTable = new()
    {
        ["a flow token"] = new FlowBreakToken(0, null),
        ["an inline token"] = new InlineBreakToken(0),
        ["an image token"] = ImageBreakToken.Instance,
        ["row -1"] = new TracksBreakToken(-1, [], RowIsSplit: false),
        ["the smallest row index"] = new TracksBreakToken(int.MinValue, [], RowIsSplit: false),
        ["a row past the end"] = new TracksBreakToken(4, [], RowIsSplit: false),
        ["cell -1"] = new TracksBreakToken(1, [new CellBreak(-1, null, IsComplete: false)], RowIsSplit: true),
        ["a cell past the last"] = new TracksBreakToken(1, [new CellBreak(99, null, IsComplete: false)], RowIsSplit: true),
        ["the same cell twice"] = new TracksBreakToken(1, [new CellBreak(1, null, IsComplete: false), new CellBreak(1, null, IsComplete: false)], RowIsSplit: true),
        ["a cell that does not cover the row"] = new TracksBreakToken(2, [new CellBreak(0, null, IsComplete: false)], RowIsSplit: true),
        ["a cell of the row, for a break before the row"] = new TracksBreakToken(1, [new CellBreak(1, null, IsComplete: false)], RowIsSplit: false),
    };

    public static TheoryData<string> FlowCases => [.. NotForFlow.Keys];

    public static TheoryData<string> InlineCases => [.. NotForInline.Keys];

    public static TheoryData<string> ImageCases => [.. NotForImage.Keys];

    public static TheoryData<string> TableCases => [.. NotForTable.Keys];

    private static void ShouldBeInvalid(Func<Result<LayoutResult, Error>> layout, string node, string name)
    {
        var result = layout.Should().NotThrow(name).Subject;

        result.IsFailure.Should().BeTrue(name);
        result.Error.Code.Should().Be(LayoutErrorCodes.InvalidBreakToken, name);
        result.Error.Metadata![ErrorMetadataKeys.Node].Should().Be(node, name);
    }

    [Theory]
    [MemberData(nameof(FlowCases))]
    public void Flow_answers_a_token_it_cannot_have_returned_with_an_error(string name) =>
        ShouldBeInvalid(() => FlowLayout.Layout(Environment, FlowBox(), Space(), NotForFlow[name]), "#1", name);

    [Theory]
    [MemberData(nameof(InlineCases))]
    public void Inline_answers_a_token_it_cannot_have_returned_with_an_error(string name) =>
        ShouldBeInvalid(() => InlineLayout.Layout(Environment, Paragraph(1, "a b"), Space(), NotForInline[name]), "#1", name);

    [Theory]
    [MemberData(nameof(ImageCases))]
    public void Image_answers_a_token_it_cannot_have_returned_with_an_error(string name) =>
        ShouldBeInvalid(() => ImageLayout.Layout(Environment, ImageBox(), Space(), NotForImage[name]), "#1", name);

    [Theory]
    [MemberData(nameof(TableCases))]
    public void Tracks_answers_a_token_it_cannot_have_returned_with_an_error(string name)
    {
        var table = TableBox(3);

        ShouldBeInvalid(() => TracksLayout.Layout(Environment, table, Space(), NotForTable[name]), table.Id.ToString(), name);
    }

    [Fact]
    public void A_token_of_the_wrong_kind_inside_a_child_is_the_error_of_that_child()
    {
        // The flow token is valid for the flow; the inline token inside it is not valid for the paragraph.
        ShouldBeInvalid(() => FlowLayout.Layout(Environment, FlowBox(), Space(), new FlowBreakToken(0, new FlowBreakToken(0, null))), "#2", "a flow token for a paragraph");
    }

    [Fact]
    public void A_token_of_the_wrong_kind_inside_a_cell_is_the_error_of_that_cell()
    {
        var table = TableBox(3);
        var cell = ((Box)((Box)((Box)table.Children[0]).Children[1]).Children[0]).Id.ToString();

        ShouldBeInvalid(
            () => TracksLayout.Layout(Environment, table, Space(), new TracksBreakToken(1, [new CellBreak(1, new InlineBreakToken(0), IsComplete: false)], RowIsSplit: true)),
            cell,
            "an inline token for a cell's flow");
    }

    [Fact]
    public void The_token_of_another_table_is_checked_against_this_table()
    {
        // The token carries the other table's prepared grid; this table prepares its own and finds no row 2.
        var other = TracksLayout.Layout(Environment, TableBox(3), Space(40), null).Value.Should().BeOfType<LayoutResult.Placed>().Subject;
        other.Continuation.Should().BeOfType<TracksBreakToken>().Which.NextRow.Should().Be(2, "two rows of 17.5 pt fit in 40 pt");
        var table = TableBox(1);

        ShouldBeInvalid(() => TracksLayout.Layout(Environment, table, Space(), other.Continuation), table.Id.ToString(), "the token of a table of three rows");
    }

    private static readonly LayoutEnvironment Slicing = Environment with { Profile = LayoutProfile.Default with { MonolithicOverflow = MonolithicOverflow.Slice } };

    /// <summary>The token a 300 pt image returns after its first slice of 80 pt.</summary>
    private static BreakToken TallImageToken()
    {
        var tall = ImageLayout.Layout(Slicing, ImageBox(1, 300.Pt()), Space(), null).Value.Should().BeOfType<LayoutResult.Placed>().Subject;
        tall.Continuation.Should().BeOfType<ImageBreakToken>().Which.Shown.Should().Be(80);
        return tall.Continuation!;
    }

    [Fact]
    public void The_slice_token_of_a_taller_image_is_the_error_of_a_shorter_image()
    {
        // 80 pt shown of the image, which is less high, would leave a last slice of negative height.
        ShouldBeInvalid(() => ImageLayout.Layout(Slicing, ImageBox(), Space(), TallImageToken()), "#1", "the token of a 300 pt image");
    }

    [Fact]
    public void A_height_shown_of_a_whole_line_or_more_is_the_error_of_the_paragraph()
    {
        // The line of "a b" is 12 pt high: nothing of it is left after 12 pt.
        ShouldBeInvalid(() => InlineLayout.Layout(Slicing, Paragraph(1, "a b"), Space(), new InlineBreakToken(0) { Shown = 1_000_000_000, Slices = 99 }), "#1", "a billion points shown");
        ShouldBeInvalid(() => InlineLayout.Layout(Slicing, Paragraph(1, "a b"), Space(), new InlineBreakToken(0) { Shown = LineBoxHeight, Slices = 1 }), "#1", "the line's height shown");
    }

    [Fact]
    public void A_flow_token_that_holds_the_slice_token_of_a_taller_image_is_the_error_of_the_image()
    {
        var box = Flow(10, [ImageBox()]);

        ShouldBeInvalid(() => FlowLayout.Layout(Slicing, box, Space(), new FlowBreakToken(0, TallImageToken())), "#1", "a flow token around the token of a 300 pt image");
    }

    [Fact]
    public void A_slice_token_that_leaves_part_of_the_content_is_accepted()
    {
        var image = ImageLayout.Layout(Slicing, ImageBox(1, 300.Pt()), Space(), TallImageToken()).Value.Should().BeOfType<LayoutResult.Placed>().Subject;
        image.Continuation.Should().BeOfType<ImageBreakToken>().Which.Shown.Should().Be(160);

        var line = InlineLayout.Layout(Slicing, Paragraph(1, "a b"), Space(), new InlineBreakToken(0) { Shown = LineBoxHeight - 1, Slices = 1 });
        line.Value.Should().BeOfType<LayoutResult.Placed>().Which.Fragment.Size.Height.Should().BeApproximately(1, 1e-9, "the last 1 pt of the line");
    }

    /// <summary>A table without header rows whose grid lines are split at a break (<see cref="BoxDecorationBreak.Slice"/>)
    /// given the token "continue at row 0": there is no row above the page's first row, so the page starts with the
    /// table's top line, as at the table's start. The token is laid out as no token is, at every height, at the page
    /// start and below it, with and without grid lines.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_token_for_row_0_of_a_table_without_header_rows_lays_the_table_out_from_its_top(bool gridLines)
    {
        foreach (var rows in new[] { 1, 2 })
        {
            foreach (var height in new[] { 150, 40, 30, 14, 10, 1 })
            {
                foreach (var atStart in new[] { true, false })
                {
                    var name = $"{rows} rows, {(gridLines ? "grid lines" : "no lines")}, {height} pt, {(atStart ? "at the page start" : "below it")}";
                    var space = new ConstraintSpace(180, height, IsAtPageStart: atStart);
                    var t = new TableModels();
                    var cell = gridLines ? TableModels.ReferenceCell : BoxStyle.Initial;
                    var table = t.Table([new TrackSize.Fill(1)], t.Body([.. Enumerable.Range(0, rows).Select(row => t.Row(t.Cell($"r{row}", cell)))]));
                    table = table with { Style = table.Style with { DecorationBreak = BoxDecorationBreak.Slice } };

                    var resumed = ((Func<Result<LayoutResult, Error>>)(() => TracksLayout.Layout(Environment, table, space, new TracksBreakToken(0, [], RowIsSplit: false))))
                        .Should().NotThrow(name).Subject;
                    var started = TracksLayout.Layout(Environment, table, space, null);

                    resumed.IsSuccess.Should().Be(started.IsSuccess, name);
                    if (started.IsFailure)
                    {
                        resumed.Error.Code.Should().Be(started.Error.Code, name);
                    }
                    else if (started.Value is LayoutResult.Placed placed)
                    {
                        var other = resumed.Value.Should().BeOfType<LayoutResult.Placed>(name).Subject;
                        other.Fragment.Size.Should().Be(placed.Fragment.Size, name);
                        (other.Continuation as TracksBreakToken)?.NextRow.Should().Be((placed.Continuation as TracksBreakToken)?.NextRow, name);
                    }
                    else
                    {
                        resumed.Value.Should().BeOfType(started.Value.GetType(), name);
                    }
                }
            }
        }
    }

    /// <summary>Positions a box may legitimately be given are not refused: the next page of a paragraph breaks its
    /// lines again in that page's width, so a line index past its last line is a paragraph that is complete.</summary>
    [Fact]
    public void Tokens_a_layout_can_return_are_accepted()
    {
        FlowLayout.Layout(Environment, FlowBox(), Space(), new FlowBreakToken(1, null)).IsSuccess.Should().BeTrue();
        FlowLayout.Layout(Environment, FlowBox(), Space(), new FlowBreakToken(2, null)).IsSuccess.Should().BeTrue();
        FlowLayout.Layout(Environment, FlowBox(), Space(), new FlowBreakToken(0, new InlineBreakToken(1))).IsSuccess.Should().BeTrue();
        InlineLayout.Layout(Environment, Paragraph(1, "a b"), Space(), new InlineBreakToken(5)).IsSuccess.Should().BeTrue();
        ImageLayout.Layout(Environment, ImageBox(), Space(), ImageBreakToken.Instance).IsSuccess.Should().BeTrue();
        TracksLayout.Layout(Environment, TableBox(3), Space(), new TracksBreakToken(1, [], RowIsSplit: false)).IsSuccess.Should().BeTrue();
        TracksLayout.Layout(Environment, TableBox(3), Space(), new TracksBreakToken(1, [new CellBreak(1, null, IsComplete: false)], RowIsSplit: true)).IsSuccess.Should().BeTrue();
        TracksLayout.Layout(Environment, TableBox(3), Space(), new TracksBreakToken(3, [], RowIsSplit: false)).IsSuccess.Should().BeTrue();
    }
}
