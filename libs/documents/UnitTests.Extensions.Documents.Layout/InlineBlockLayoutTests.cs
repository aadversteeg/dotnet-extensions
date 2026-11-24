using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;
using static UnitTests.Extensions.Documents.Layout.Models;

namespace UnitTests.Extensions.Documents.Layout;

/// <summary>
/// An inline-block: a flow or inline box among the runs of a paragraph, an atomic inline with its own block layout. Its
/// last line's baseline sits on the line's baseline, its vertical margins count above and below it, and its width is its
/// shrink-to-fit width or its block sizes; Chromium's prints are the reference cases <c>iblk-*</c>.
/// </summary>
public class InlineBlockLayoutTests
{
    private static Box Line(int id, params Node[] atomics) =>
        new(new NodeId(id), SemanticRole.Paragraph, LayoutAlgorithmId.Inline, BoxStyle.Initial,
            [new TextRun(new NodeId(id + 1), "ab ", Text(), At(0)), .. atomics, new TextRun(new NodeId(id + 2), " cd", Text(), At(0))], At(0));

    /// <summary>The first line of the page that holds a box, the box placed on it, and every line inside that box with its
    /// top relative to the box's top.</summary>
    private static (LineFragment Line, PlacedFragment Block, List<(LineFragment Line, double Top)> Inside) Laid(Box paragraph)
    {
        var pages = Paginator.Paginate(LayoutEnvironment.Create(TestFonts.Set), Document(paragraph));
        pages.IsSuccess.Should().BeTrue(pages.IsFailure ? pages.Error.ToString() : string.Empty);
        var line = Lines(pages.Value.Single().Root.Fragment).Select(entry => entry.Line).First(found => found.Children.Any(child => child.Fragment is BoxFragment));
        var block = line.Children.Single(child => child.Fragment is BoxFragment);
        return (line, block, Lines(block.Fragment));
    }

    private static List<(LineFragment Line, double Top)> Lines(Fragment root)
    {
        var lines = new List<(LineFragment, double)>();
        var pending = new Stack<(Fragment Fragment, double Top)>([(root, 0)]);
        while (pending.Count > 0)
        {
            var (fragment, top) = pending.Pop();
            if (fragment is LineFragment line)
            {
                lines.Add((line, top));
                continue;
            }

            if (fragment is BoxFragment box)
            {
                for (var index = box.Children.Count - 1; index >= 0; index--)
                {
                    pending.Push((box.Children[index].Fragment, top + box.Children[index].Offset.Y));
                }
            }
        }

        return lines;
    }

    [Fact]
    public void An_inline_block_has_its_last_line_s_baseline_on_the_line_s_baseline_and_its_margins_count_above_and_below()
    {
        var style = BoxStyle.Initial with { Margin = new Edges(3.Pt(), 0.Pt(), 5.Pt(), 0.Pt()), Padding = Edges.All(2.Pt()) };
        var (line, block, inside) = Laid(Line(1, Flow(10, [Paragraph(20, "one"), Paragraph(30, "two")], style)));

        inside.Should().HaveCount(2);
        var baseline = inside[^1].Top + inside[^1].Line.Baseline;
        (block.Offset.Y + baseline).Should().BeApproximately(line.Baseline, 1e-9, "the last line's baseline, not the first's");
        block.Offset.Y.Should().BeApproximately(3, 1e-9, "the top margin is above the box, and the box reaches above the strut");
        line.Size.Height.Should().BeApproximately(line.Baseline + (block.Fragment.Size.Height - baseline) + 5, 1e-9, "the rest of the box and the bottom margin lie below the baseline");
    }

    [Fact]
    public void An_inline_block_without_a_line_has_its_bottom_margin_edge_on_the_baseline()
    {
        var style = BoxStyle.Initial with { SpecifiedHeight = 8.Pt(), SpecifiedWidth = new SizeValue.Fixed(8.Pt()), Margin = new Edges(0.Pt(), 0.Pt(), 2.Pt(), 0.Pt()), Background = Color.Black };
        var (line, block, inside) = Laid(Line(1, Flow(10, [], style)));

        inside.Should().BeEmpty();
        (block.Offset.Y + 8 + 2).Should().BeApproximately(line.Baseline, 1e-9);
    }

    [Fact]
    public void An_inline_block_holding_only_a_table_has_its_bottom_margin_edge_on_the_baseline()
    {
        var cell = new Box(new NodeId(13), SemanticRole.TableCell, LayoutAlgorithmId.Flow, BoxStyle.Initial, [Paragraph(14, "cell")], At(0));
        var row = new Box(new NodeId(12), SemanticRole.TableRow, LayoutAlgorithmId.Flow, BoxStyle.Initial with { TableDisplay = TableDisplay.Row }, [cell], At(0));
        var body = new Box(new NodeId(15), SemanticRole.TableBody, LayoutAlgorithmId.Flow, BoxStyle.Initial with { TableDisplay = TableDisplay.RowGroup }, [row], At(0));
        var table = new Box(new NodeId(11), SemanticRole.Table, LayoutAlgorithmId.Tracks, BoxStyle.Initial with { Columns = [TrackSize.Auto.Instance] }, [body], At(0));
        var (line, block, inside) = Laid(Line(1, Flow(10, [table], BoxStyle.Initial with { Margin = new Edges(0.Pt(), 0.Pt(), 2.Pt(), 0.Pt()) })));

        inside.Should().ContainSingle("the cell's line is inside the box");
        (block.Offset.Y + block.Fragment.Size.Height + 2).Should().BeApproximately(line.Baseline, 1e-9, "a table's lines are not the inline-block's (case iblk-align)");
    }

    [Theory]
    [InlineData(null, null, null, false)]
    [InlineData(30.0, null, null, true)]
    [InlineData(null, 20.0, null, true)]
    [InlineData(null, null, 150.0, true)]
    public void An_inline_block_is_its_shrink_to_fit_width_unless_its_sizes_say_otherwise(double? width, double? max, double? min, bool sized)
    {
        var style = BoxStyle.Initial with
        {
            Padding = new Edges(0.Pt(), 1.Pt(), 0.Pt(), 1.Pt()),
            SpecifiedWidth = width is { } given ? new SizeValue.Fixed(given.Pt()) : null,
            MaxWidth = max is { } most ? new SizeValue.Fixed(most.Pt()) : null,
            MinWidth = min is { } least ? new SizeValue.Fixed(least.Pt()) : null,
        };
        var (_, block, inside) = Laid(Line(1, Flow(10, [Paragraph(20, "one two")], style)));

        var widest = inside.Max(entry => entry.Line.Size.Width);
        block.Fragment.Size.Width.Should().BeApproximately(sized ? (width ?? max ?? min)!.Value + 2 : widest + 2, 1e-9);
        if (!sized)
        {
            widest.Should().BeLessThan(100, "the box shrinks to its text, not to the line");
        }
    }

    [Fact]
    public void An_inline_block_s_percentage_width_is_refused_before_layout()
    {
        var style = BoxStyle.Initial with { SpecifiedWidth = new SizeValue.Percentage(50) };
        var result = DocumentLayout.Run(Document(Line(1, Flow(10, [Paragraph(20, "one")], style))), TestFonts.Set);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DocumentErrorCodes.Input.Unsupported.AtomicInline);
        result.Error.Metadata![ErrorMetadataKeys.Node].Should().Be("#10");
    }

    [Fact]
    public void An_inline_block_taller_than_the_page_is_refused_at_its_box()
    {
        var tall = Flow(10, [.. Enumerable.Range(0, 12).Select(index => (Node)Paragraph(20 + index, "row"))]);
        var result = DocumentLayout.Run(Document(Line(1, tall)), TestFonts.Set);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DocumentErrorCodes.Input.Unsupported.InlineBox);
        result.Error.Metadata![ErrorMetadataKeys.Node].Should().Be("#10");
        result.Error.Message.Should().Contain("taller than a page");
    }
}
