using Ave.Documents.Core;
using Ave.Documents.DocumentModel;
using Ave.Documents.Layout;
using Ave.Documents.TestSupport;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;
using static UnitTests.Ave.Documents.Layout.Models;

namespace UnitTests.Ave.Documents.Layout;

/// <summary>
/// Page templates (header and footer areas, a different first page) and the page counters with the whole-document
/// fixpoint. Pages are 200 × 100 pt with 10 pt margins (a 180 × 80 page area); body words are 12 pt lines.
/// </summary>
public class PageTemplateTests
{
    private static readonly BoxStyle Narrow = BoxStyle.Initial with { Margin = new Edges(0.Pt(), 160.Pt(), 0.Pt(), 0.Pt()) };

    private static Result<ValueList<PageFragment>, Error> Paginate(StyledDocument document, LayoutEnvironment? environment = null) =>
        Paginator.Paginate(environment ?? LayoutEnvironment.Create(TestFonts.Set), document);

    private static StyledDocument WithTemplate(StyledDocument document, PageTemplate template, PageTemplate? first = null) =>
        document with { Page = document.Page with { Template = template, FirstPage = first } };

    private static Box Line(int id, params InlineRun[] runs) =>
        new(new NodeId(id), SemanticRole.Paragraph, LayoutAlgorithmId.Inline, BoxStyle.Initial, [.. runs], At(0));

    private static TextRun Text(int id, string text) => new(new NodeId(id), text, Models.Text(), At(0));

    private static CounterRun Counter(int id, PageCounter counter, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0) =>
        new(new NodeId(id), counter, Models.Text(), At(line));

    private static Box Area(int id, params Node[] content) => Flow(id, [.. content]);

    private static string TextOf(Fragment fragment) => fragment switch
    {
        BoxFragment box => string.Join(box.Children.All(child => child.Fragment is LineFragment) ? " " : string.Empty, box.Children.Select(child => TextOf(child.Fragment))),
        LineFragment line => string.Concat(line.Children.Select(child => TextOf(child.Fragment))),
        TextFragment text => string.Concat(text.Glyphs.Select(glyph => glyph.Text)),
        _ => string.Empty,
    };

    private static int LinesIn(PageFragment page) => ((BoxFragment)page.Root.Fragment).Children.Sum(child => ((BoxFragment)child.Fragment).Children.Count);

    [Fact]
    public void Header_and_footer_take_their_height_from_the_page_area_and_the_body_gets_the_rest()
    {
        var document = WithTemplate(Document(Paragraph(1, Words(14), Narrow)),
            new PageTemplate(new PageArea(Area(50, Line(51, Text(52, "Head"))), null), new PageArea(Area(60, Line(61, Text(62, "Foot"))), null)));

        var pages = Paginate(document).Value;

        pages.Select(LinesIn).Should().Equal([4, 4, 4, 2], "the body area is 80 - 12 - 12 = 56 pt: 4 lines");
        pages[0].Header!.Offset.Should().Be(new Point(10, 10));
        pages[0].Root.Offset.Should().Be(new Point(10, 22), "the body starts below the header");
        pages[0].Footer!.Offset.Should().Be(new Point(10, 78), "the footer ends at the bottom of the page area (90 - 12)");
    }

    [Fact]
    public void A_fixed_height_area_takes_exactly_that_height_and_content_taller_than_it_is_an_error()
    {
        var fixedHeader = new PageTemplate(new PageArea(Area(50, Line(51, Text(52, "Head"))), 30.Pt()), null);
        var tooSmall = new PageTemplate(new PageArea(Area(50, Line(51, Text(52, "Head"))), 5.Pt()), null);

        Paginate(WithTemplate(Document(Paragraph(1, "Body")), fixedHeader)).Value[0].Root.Offset.Y.Should().Be(10 + 30);
        var error = Paginate(WithTemplate(Document(Paragraph(1, "Body")), tooSmall)).Error;
        error.Code.Should().Be(DocumentErrorCodes.Layout.DoesNotFit);
        error.Metadata![ErrorMetadataKeys.Node].Should().Be("#51", "the area's paragraph is what does not fit");
    }

    [Fact]
    public void A_header_and_footer_that_leave_no_room_for_the_body_are_an_error()
    {
        var full = new PageTemplate(new PageArea(Area(50), 40.Pt()), new PageArea(Area(60), 40.Pt()));

        Paginate(WithTemplate(Document(Paragraph(1, "Body")), full)).Error.Code.Should().Be(DocumentErrorCodes.Layout.DoesNotFit);
    }

    [Fact]
    public void The_first_page_can_have_its_own_template()
    {
        var normal = new PageTemplate(new PageArea(Area(50, Line(51, Text(52, "Every"))), null), null);
        var first = new PageTemplate(new PageArea(Area(60, Line(61, Text(62, "First"))), 24.Pt()), null);

        var pages = Paginate(WithTemplate(Document(Paragraph(1, Words(10), Narrow)), normal, first)).Value;

        pages.Select(page => TextOf(page.Header!.Fragment)).Should().Equal("First", "Every", "Every");
        pages.Select(LinesIn).Should().Equal(4, 5, 1);
    }

    [Fact]
    public void Page_number_and_page_count_show_in_every_footer()
    {
        var footer = new PageTemplate(null, new PageArea(Area(60, Line(61, Text(62, "Page "), Counter(63, PageCounter.PageNumber), Text(64, " of "), Counter(65, PageCounter.PageCount))), null));

        var pages = Paginate(WithTemplate(Document(Paragraph(1, Words(14), Narrow)), footer)).Value;

        pages.Select(page => TextOf(page.Footer!.Fragment)).Should().Equal("Page 1 of 3", "Page 2 of 3", "Page 3 of 3");
    }

    [Fact]
    public void A_page_count_whose_digits_change_the_wrapping_still_settles()
    {
        // The footer box is wide enough for "of 9" but not for "of 10", which wraps to two lines and leaves the body
        // one line less per page. 48 body lines: assumed count 1 → 10 pages; count 10 → two-line footers → 12 pages;
        // count 12 → 12 pages: settled, and every footer shows the real count.
        var environment = LayoutEnvironment.Create(TestFonts.Set);
        var nine = environment.Measure(Line(1, Text(2, "of 9"))).Value.MaxContent;
        var ten = environment.Measure(Line(1, Text(2, "of 10"))).Value.MaxContent;
        var width = (nine + ten) / 2;
        var footerStyle = BoxStyle.Initial with { Margin = new Edges(0.Pt(), (180 - width).Pt(), 0.Pt(), 0.Pt()) };
        var footer = new PageTemplate(null, new PageArea(Area(60, Line(61, Text(62, "of "), Counter(63, PageCounter.PageCount)) with { Style = footerStyle }), null));

        var pages = Paginate(WithTemplate(Document(Paragraph(1, Words(48), Narrow)), footer), environment).Value;

        pages.Should().HaveCount(12);
        pages.Select(page => TextOf(page.Footer!.Fragment)).Should().AllBe("of 12", "lines are joined with a space here");
        pages.Select(page => page.Footer!.Fragment.Size.Height).Should().AllBeEquivalentTo(2 * LineHeight, "\"of 12\" wraps to two lines");
    }

    [Fact]
    public void A_page_count_that_never_settles_is_an_error_at_the_count_s_source_line()
    {
        // A test algorithm whose number of pages depends on the page count: 2 pages when told 1 or 3, 3 pages when
        // told 2. Pass by pass: 1 → 2 → 3 → 2 → 3: no pass ever produces the count it assumed.
        LayoutAlgorithm flip = (environment, box, space, incoming) =>
        {
            var produced = incoming is FlipToken token ? token.Pages + 1 : 1;
            var wanted = environment.Page.Count == 2 ? 3 : 2;
            return LayoutResult.Place(new BoxFragment(box, new Size(10, 10), []), produced < wanted ? new FlipToken(produced) : null);
        };
        var environment = LayoutEnvironment.Create(TestFonts.Set).WithAlgorithm(new LayoutAlgorithmId("flip"), flip);
        var line = CurrentLine() + 1;
        var footer = new PageTemplate(null, new PageArea(Area(60, Line(61, Counter(62, PageCounter.PageCount))), null));
        var document = WithTemplate(Document(Flow(1, []) with { Layout = new LayoutAlgorithmId("flip") }), footer);

        var error = Paginate(document, environment).Error;

        error.Code.Should().Be(DocumentErrorCodes.Layout.NotConverging);
        error.Metadata![ErrorMetadataKeys.Value].Should().Be("2,3,2,3,2");
        error.Metadata![ErrorMetadataKeys.SourceLine].Should().Be(line);
        error.Metadata![ErrorMetadataKeys.Node].Should().Be("#62");
    }

    [Fact]
    public void A_one_page_document_settles_in_one_pass_and_its_count_is_one()
    {
        var footer = new PageTemplate(null, new PageArea(Area(60, Line(61, Counter(62, PageCounter.PageNumber), Text(63, "/"), Counter(64, PageCounter.PageCount))), null));

        TextOf(Paginate(WithTemplate(Document(Paragraph(1, "Body")), footer)).Value.Single().Footer!.Fragment).Should().Be("1/1");
    }

    private static int CurrentLine([System.Runtime.CompilerServices.CallerLineNumber] int line = 0) => line;

    private sealed record FlipToken(int Pages) : BreakToken;
}
