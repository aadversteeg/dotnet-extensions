using System.Diagnostics;
using Ave.Documents.DocumentModel;
using Ave.Documents.Layout;
using Ave.Documents.TestSupport;

namespace UnitTests.Ave.Documents.Layout;

/// <summary>
/// A long table lays out in time that grows with its rows, not with their square: its grid and columns are prepared
/// once per layout pass and handed from page to page, and a page looks up the cells of its own rows only. The test
/// compares growth, not absolute time, so it holds on any machine: four times the rows may take at most eight times
/// as long (linear growth gives about four, quadratic growth sixteen — the cost before this was fixed).
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

    private static double Milliseconds(StyledDocument document)
    {
        var environment = LayoutEnvironment.Create(TestFonts.Set);
        var times = new List<double>();
        for (var run = 0; run < 3; run++)
        {
            var watch = Stopwatch.StartNew();
            Paginator.Paginate(environment, document).IsSuccess.Should().BeTrue();
            times.Add(watch.Elapsed.TotalMilliseconds);
        }

        return times.Order().ElementAt(1);
    }

    [Fact]
    public void Four_times_the_rows_take_less_than_eight_times_as_long()
    {
        Milliseconds(Rows(50));

        var small = Milliseconds(Rows(250));
        var large = Milliseconds(Rows(1000));

        (large / small).Should().BeLessThan(8, $"250 rows took {small:F0} ms, 1000 rows {large:F0} ms");
    }
}
