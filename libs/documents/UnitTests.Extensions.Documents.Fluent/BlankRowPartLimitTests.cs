using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Fluent;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.Functional;
using static Ave.Extensions.Documents.Fluent.Elements;

namespace UnitTests.Extensions.Documents.Fluent;

/// <summary>
/// LIMIT (review 36, reading (b); docs/architecture.md, Known limits): a part of a table row that is as tall as the
/// page area and draws nothing cannot be cut, and the document is refused. The rule for a cut inside a row
/// (<c>TableBreaks.CutsInsideRow</c>) allows a cut only where some cell draws something below the row's top on that
/// page; a page that would hold only blank lines or empty padded blocks of the row offers no cut, and at a page start
/// nothing can move on. This is not the leading-space limit (NF-1, <see cref="LeadingSpaceLimitTests"/>): there is no
/// leading space here, and the row could be broken. Cutting anyway when the only other outcome is a refusal is next
/// milestone's rule. Page area 280 × 180 pt.
/// </summary>
public class BlankRowPartLimitTests
{
    private static Result<ValueList<PageFragment>, global::Ave.Extensions.ErrorPaths.Error> Paginate(CellElement blank) =>
        DocumentTranslator.ToModel(Document(
                [Table(Columns(Fill(1), Fill(1)), [TableRow([Cell("before"), Cell("b")]), TableRow([Cell("x"), blank]), TableRow([Cell("after"), Cell("a")])]).GridLines(1.Pt(), Color.Black)],
                Page(300.Pt(), 200.Pt()).Margin(10.Pt()), title: "Title", language: "en").FontFamily("Lato"))
            .OnSuccessBind(model => Paginator.Paginate(LayoutEnvironment.Create(TestFonts.SetWithItalic), model));

    private static CellElement TopBlankEnd(int blankLines) =>
        Cell([Paragraph("top"), Paragraph([.. Enumerable.Range(0, blankLines).Select(_ => LineBreak())]), Paragraph("end")]);

    [Fact]
    public void Limit_a_part_of_a_row_as_tall_as_the_page_area_that_draws_nothing_is_not_cut_and_the_document_is_refused()
    {
        // The reviewer's case (probe Q2): "top", 30 blank lines (432 pt), "end", beside a cell with text. Page 1 is cut
        // after "top" and some blank lines; page 2 would hold only blank lines of the row, so no cut is allowed there,
        // and the row cannot move: the document is refused. Round 4 gave three pages, the middle one blank.
        var result = Paginate(TopBlankEnd(30));

        result.IsFailure.Should().BeTrue("LIMIT: a row part as tall as the page area that draws nothing has no cut");
        result.Error.Code.Should().Be(LayoutErrorCodes.DoesNotFit);
        result.Error.Message.Should().StartWith("The table made at BlankRowPartLimitTests.cs:")
            .And.EndWith(" does not fit in the 180pt left for it on the page and cannot be broken; nothing before it on the page can move to a later page.");
    }

    [Fact]
    public void A_row_whose_blank_rest_fits_the_next_page_is_cut_once_and_laid_out()
    {
        // The edge of the limit: with 15 blank lines the row does not fit page 1, but what is left of it after the cut
        // (blank lines and "end") fits page 2 whole, so no page is a blank part of the row that must be cut again.
        var result = Paginate(TopBlankEnd(15));

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        result.Value.Should().HaveCount(2);
    }
}
