using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Fluent;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.Functional;
using static Ave.Extensions.Documents.Fluent.Elements;

namespace UnitTests.Extensions.Documents.Fluent;

/// <summary>
/// Keep rules that reach back: when every breakpoint between the boxes at the page end violates a keep rule, the page
/// breaks at the best breakpoint inside an earlier box — between the lines of a paragraph, between the children of a
/// block, between the rows of a table — by laying that box out once more with an early break (Chromium's
/// <c>EarlyBreak</c>). Against Chromium's print of the same content (<c>tools/reference/chromium</c>, cases <c>b-*</c>;
/// <c>orphans</c> and <c>widows</c> are 1 there, so only the keep rules decide). Page area 276 × 150 pt. And the work
/// it costs: each box is laid out at most twice on a page, however deep the early break lies.
/// </summary>
public class ReachBackTests
{
    private static PageSetup Area => Page(300.Pt(), 174.Pt()).Margin(12.Pt());

    private static Document Doc(params Element[] body) => Document([.. body], Area, title: "Title", language: "en").FontFamily("Lato");

    /// <summary>A paragraph of lines, with orphans and widows 1 as in the cases.</summary>
    private static ParagraphElement Lines(int count, string prefix) =>
        Paragraph([.. Enumerable.Range(0, count).SelectMany(i => i == 0 ? new[] { Text($"{prefix}{i}") } : [LineBreak(), Text($"{prefix}{i}")])]).Orphans(1).Widows(1);

    private static void ShouldPrintLikeChromium(string caseName, Document document)
    {
        var engine = DocumentTranslator.ToModel(document)
            .OnSuccessBind(model => DocumentLayout.Run(model, TestFonts.SetWithItalic))
            .OnSuccessMap(PrintedPages.Engine);

        engine.IsSuccess.Should().BeTrue(engine.IsFailure ? engine.Error.ToString() : string.Empty);
        engine.Value.PageTexts.Should().Equal(PrintedPages.Chromium(caseName).PageTexts, $"the words of each page as Chromium prints {caseName}");
    }

    /// <summary>Nine lines kept with a head, the head kept with three lines: both breaks between the boxes violate a keep,
    /// so the page breaks between the paragraph's lines, and its last line goes with the head.</summary>
    private static Document IntoParagraph() =>
        Doc(Lines(9, "p").KeepWithNext(), Paragraph("head").KeepWithNext(), Lines(3, "q"));

    /// <summary>A block whose last child, the head, is kept with the lines after the block (a keep on a last child
    /// propagates to its container's edge, CSS Fragmentation 3 §3.1.1), at <paramref name="depth"/> levels of blocks.</summary>
    private static Document IntoBlock(int depth)
    {
        Element inner = Block([Lines(8, "p"), Paragraph("head").KeepWithNext()]);
        for (var level = 1; level < depth; level++)
        {
            inner = Block([inner]);
        }

        return Doc(inner, Lines(2, "q").KeepTogether());
    }

    [Fact]
    public void A_keep_on_every_break_between_the_boxes_moves_the_break_between_the_lines_of_an_earlier_paragraph() =>
        ShouldPrintLikeChromium("b-into-paragraph", IntoParagraph());

    [Fact]
    public void A_keep_from_the_last_child_of_a_block_moves_the_break_into_the_block() =>
        ShouldPrintLikeChromium("b-into-block", IntoBlock(1));

    [Fact]
    public void An_early_break_three_blocks_deep_is_found_and_taken() =>
        ShouldPrintLikeChromium("b-three-deep", IntoBlock(3));

    [Fact]
    public void A_table_kept_with_the_paragraph_after_it_breaks_between_its_rows()
    {
        var table = Table(Columns(Fill(1)), [.. Enumerable.Range(0, 9).Select(i => (TablePart)TableRow([Cell($"r{i}")]))])
            .GridLines(0.75.Pt(), Color.Black).SplitLinesAtBreaks().KeepWithNext();

        ShouldPrintLikeChromium("b-into-table", Doc(table, Lines(2, "q").KeepTogether()));
    }

    /// <summary>The flow layouts of every box on every page, through a counting wrapper around the registered algorithm
    /// (as <c>TableScalingTests</c> counts).</summary>
    private static Dictionary<(int Page, NodeId Box), int> Layouts(Document document)
    {
        var environment = LayoutEnvironment.Create(TestFonts.SetWithItalic);
        var flow = environment.Algorithms[LayoutAlgorithmId.Flow];
        var inline = environment.Algorithms[LayoutAlgorithmId.Inline];
        var counts = new Dictionary<(int Page, NodeId Box), int>();
        Result<LayoutResult, global::Ave.Extensions.ErrorPaths.Error> Count(RegisteredAlgorithm algorithm, LayoutEnvironment env, Box box, ConstraintSpace space, BreakToken? incoming)
        {
            var key = (env.Page.Number, box.Id);
            counts[key] = counts.GetValueOrDefault(key) + 1;
            return algorithm.Layout(env, box, space, incoming);
        }

        var counting = environment with
        {
            Algorithms = environment.Algorithms
                .SetItem(LayoutAlgorithmId.Flow, new RegisteredAlgorithm((env, box, space, incoming) => Count(flow, env, box, space, incoming), flow.Measure))
                .SetItem(LayoutAlgorithmId.Inline, new RegisteredAlgorithm((env, box, space, incoming) => Count(inline, env, box, space, incoming), inline.Measure)),
        };
        DocumentTranslator.ToModel(document).OnSuccessBind(model => Paginator.Paginate(counting, model)).IsSuccess.Should().BeTrue();
        return counts;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(6)]
    [InlineData(12)]
    public void An_early_break_lays_each_box_out_at_most_twice_on_a_page_however_deep_it_lies(int depth)
    {
        // The bound on repeated layout: a box reports its best breakpoint with a complete fragment; a parent that takes it
        // lays that one child out once more with the early break, and the child follows it down without searching again,
        // so every box on the way is laid out twice and no box more (no doubling per level).
        var counts = Layouts(IntoBlock(depth));

        counts.Values.Max().Should().Be(2, $"depth {depth}: the boxes on the way to the early break, laid out once more");
        counts.Where(count => count.Key.Page == 1).Sum(count => count.Value).Should().BeLessThanOrEqualTo(2 * (depth + 3),
            "page 1: the root, the blocks, the paragraph and the head twice at most, the kept lines once");
    }

    [Fact]
    public void Without_a_keep_rule_no_box_is_laid_out_twice()
    {
        // The positive control of the count: the same document without the keep rule breaks where it runs out of room.
        var counts = Layouts(Doc(Block([Block([Lines(8, "p"), Paragraph("head")])]), Lines(2, "q").KeepTogether()));

        counts.Values.Should().AllSatisfy(count => count.Should().Be(1));
    }

    [Fact]
    public void The_early_break_is_found_inside_the_paragraph_on_a_real_relayout()
    {
        // Positive control for the paragraph case: the paragraph is laid out twice on page 1.
        var counts = Layouts(IntoParagraph());

        counts.Where(count => count.Key.Page == 1).Max(count => count.Value).Should().Be(2);
    }
}
