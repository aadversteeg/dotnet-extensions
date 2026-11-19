using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace UnitTests.Extensions.Documents.Layout;

/// <summary>
/// Whether a table repeats its header rows is decided once, on its first page, from the page area it is laid out in
/// (<see cref="ConstraintSpace.FragmentainerBlockSize"/>), and carried to its later pages in its break token
/// (<see cref="TracksBreakToken.RepeatsHeader"/>), as Chromium decides (<c>TableLayoutAlgorithm::GenerateFragment</c>:
/// a header that did not repeat on the table's first fragment does not start repeating later). The tables here have a
/// header of three lines above 30 one-line rows; a page area of 40 pt is less than four times the header, one of 10 000 pt
/// is more.
/// </summary>
public class HeaderDecisionTests
{
    private static readonly LayoutEnvironment Environment = LayoutEnvironment.Create(TestFonts.Set);

    private static Box HeaderTable()
    {
        var t = new TableModels();
        return t.Table([new TrackSize.Fill(1)], t.Head(t.Row(t.Lines(["h0", "h1", "h2"]))), t.Body([.. Enumerable.Range(0, 30).Select(row => t.Row(t.Cell($"r{row}")))]));
    }

    private static ConstraintSpace Space(double pageArea) => new(180, 100, IsAtPageStart: true) { FragmentainerBlockSize = pageArea };

    private static TracksBreakToken Continuation(Result<LayoutResult, Error> result)
    {
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        return result.Value.Should().BeOfType<LayoutResult.Placed>().Subject.Continuation.Should().BeOfType<TracksBreakToken>().Subject;
    }

    [Theory]
    [InlineData(40, 10_000, false)]
    [InlineData(10_000, 40, true)]
    public void A_later_page_keeps_the_decision_of_the_table_s_first_page_whatever_its_own_page_area(double firstPageArea, double laterPageArea, bool repeats)
    {
        var table = HeaderTable();
        var first = Continuation(TracksLayout.Layout(Environment, table, Space(firstPageArea), null));
        var second = Continuation(TracksLayout.Layout(Environment, table, Space(laterPageArea), first));
        var decidedThere = Continuation(TracksLayout.Layout(Environment, table, Space(laterPageArea), first with { RepeatsHeader = null }));

        first.RepeatsHeader.Should().Be(repeats);
        second.RepeatsHeader.Should().Be(repeats);
        decidedThere.RepeatsHeader.Should().Be(!repeats, "a token not made by the layout is decided on the page it resumes on");
        (second.NextRow - first.NextRow).Should().NotBe(decidedThere.NextRow - first.NextRow, "the repeated header takes the room of rows");
    }

    [Fact]
    public void A_table_decides_from_the_page_area_not_from_the_room_left_where_it_starts()
    {
        // 100 pt left below other content, less than four times the header, on a page area of 10 000 pt: the header repeats.
        var table = HeaderTable();
        var space = new ConstraintSpace(180, 100, IsAtPageStart: false) { FragmentainerBlockSize = 10_000 };

        Continuation(TracksLayout.Layout(Environment, table, space, null)).RepeatsHeader.Should().BeTrue();
    }
}
