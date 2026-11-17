using Ave.Documents.DocumentModel;
using Ave.Documents.Layout;
using Ave.Documents.TestSupport;

namespace UnitTests.Ave.Documents.Layout;

/// <summary>
/// A long table lays out with work that grows with its rows, not with their square: its grid and columns are prepared
/// once per layout pass and handed from page to page, and a page lays out and measures the cells of its own rows only.
/// The test counts work, not time, so it holds on a loaded machine: it counts every call of the flow algorithm (each
/// table cell is a flow box) and every intrinsic-width measurement of one, through counting wrappers around the
/// registered algorithm. Four times the rows may cost at most five times the calls (linear growth gives about four,
/// quadratic growth sixteen — the cost before this was fixed).
/// </summary>
public class TableScalingTests
{
    private static StyledDocument Rows(int count)
    {
        var t = new TableModels();
        return TableModels.Page(500, 700, t.Table(
            [new TrackSize.Fill(1), new TrackSize.Fill(3), new TrackSize.Fill(1)],
            t.Body([.. Enumerable.Range(0, count).Select(i => t.Row(t.Cell($"2026-01-{i % 28 + 1:00}"), t.Cell($"Work item {i}"), t.Cell("8.00")))])));
    }

    /// <summary>The flow layout and measurement calls the pagination of the document makes.</summary>
    private static (int Layouts, int Measures) Work(StyledDocument document)
    {
        var environment = LayoutEnvironment.Create(TestFonts.Set);
        var flow = environment.Algorithms[LayoutAlgorithmId.Flow];
        var (layouts, measures) = (0, 0);
        var counting = environment.WithAlgorithm(
            LayoutAlgorithmId.Flow,
            (env, box, space, incoming) => { layouts++; return flow.Layout(env, box, space, incoming); },
            (env, box) => { measures++; return flow.Measure(env, box); });

        Paginator.Paginate(counting, document).IsSuccess.Should().BeTrue();
        return (layouts, measures);
    }

    [Fact]
    public void Four_times_the_rows_cost_at_most_five_times_the_work()
    {
        var small = Work(Rows(250));
        var large = Work(Rows(1000));

        ((double)large.Layouts / small.Layouts).Should().BeLessThan(5, $"flow layouts: 250 rows {small.Layouts}, 1000 rows {large.Layouts}");
        ((double)large.Measures / small.Measures).Should().BeLessThan(5, $"measurements: 250 rows {small.Measures}, 1000 rows {large.Measures}");
    }

    [Fact]
    public void The_counting_sees_the_cells()
    {
        // Positive control: 250 rows of three cells are at least 750 flow layouts.
        Work(Rows(250)).Layouts.Should().BeGreaterThanOrEqualTo(750);
    }
}
