using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;
using Xunit.Abstractions;
using static UnitTests.Extensions.Documents.Layout.Models;

namespace UnitTests.Extensions.Documents.Layout;

/// <summary>
/// A paragraph of many decorated runs is laid out in work linear in its runs: one pass over each decorated piece's glyphs
/// and one rectangle per decoration per piece (THE RULE FOR TEXT DECORATION LINES). Counted: the bytes the layout allocates,
/// at two sizes (the form of TextCostTests).
/// </summary>
public class TextDecorationCostTests(ITestOutputHelper output)
{
    /// <summary>The factor a doubled input may cost at most: twice the work, with room for noise, well below quadratic's four.</summary>
    private const double Linear = 2.5;

    private static Box Runs(int count)
    {
        ValueList<TextDecoration> lines =
        [
            new(TextDecorationLine.Underline, Color.Black, "Lato", 10.Pt(), FontWeight.Regular),
            new(TextDecorationLine.Overline, Color.Black, "Lato", 10.Pt(), FontWeight.Regular),
            new(TextDecorationLine.LineThrough, Color.Black, "Lato", 10.Pt(), FontWeight.Regular),
        ];
        return new(new NodeId(1), SemanticRole.Paragraph, LayoutAlgorithmId.Inline, BoxStyle.Initial,
            [.. Enumerable.Range(0, count).Select(index => (Node)new TextRun(new NodeId(10 + index), "word ", Text() with { Decorations = lines }, At(0)))], At(0));
    }

    private static long Bytes(int count)
    {
        var paragraph = Runs(count);
        var environment = LayoutEnvironment.Create(TestFonts.Set);
        var before = GC.GetAllocatedBytesForCurrentThread();
        environment.Layout(paragraph, new ConstraintSpace(100, double.PositiveInfinity, IsAtPageStart: true), null).IsSuccess.Should().BeTrue();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [Fact]
    public void A_paragraph_of_many_decorated_runs_costs_work_linear_in_the_runs()
    {
        Bytes(100);
        var (small, large) = (Bytes(2000), Bytes(4000));
        output.WriteLine($"decorated runs: 2000 -> {small} bytes; 4000 -> {large} bytes");

        large.Should().BeLessThanOrEqualTo((long)(Linear * small));
    }
}
