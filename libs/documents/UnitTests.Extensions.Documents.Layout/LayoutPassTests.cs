using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;
using static UnitTests.Extensions.Documents.Layout.Models;

namespace UnitTests.Extensions.Documents.Layout;

/// <summary>
/// The page-count fixpoint only runs when something shows the page count: a document without one is laid out once,
/// however many pages it has. Counted with a root algorithm that delegates to flow and counts its calls (once per page
/// per pass).
/// </summary>
public class LayoutPassTests
{
    private static int RootCalls(params Node[] children)
    {
        var calls = 0;
        var counting = new LayoutAlgorithmId("counted-flow");
        var environment = LayoutEnvironment.Create(TestFonts.Set).WithAlgorithm(counting, (env, box, space, token) =>
        {
            calls++;
            return FlowLayout.Layout(env, box, space, token);
        });
        var document = Document(children) with { Root = Flow(0, [.. children]) with { Layout = counting } };

        var pages = Paginator.Paginate(environment, document);

        pages.IsSuccess.Should().BeTrue(pages.IsFailure ? pages.Error.ToString() : string.Empty);
        pages.Value.Count.Should().Be(3);
        return calls;
    }

    private static Node[] ThreePages(params Node[] extra) =>
        [.. Enumerable.Range(0, 16).Select(i => (Node)Paragraph(10 + i, $"line {i}")), .. extra];

    [Fact]
    public void A_document_without_a_page_count_is_laid_out_once() =>
        RootCalls(ThreePages()).Should().Be(3);

    [Fact]
    public void A_document_that_shows_its_page_count_is_laid_out_until_the_count_settles()
    {
        var total = new Box(new NodeId(90), SemanticRole.Paragraph, LayoutAlgorithmId.Inline, BoxStyle.Initial,
            [new CounterRun(new NodeId(91), PageCounter.PageCount, Text(), At(0))], At(0));

        RootCalls(ThreePages(total)).Should().Be(6, "pass 1 assumes 1 page and finds 3, pass 2 assumes 3 and finds 3");
    }
}
