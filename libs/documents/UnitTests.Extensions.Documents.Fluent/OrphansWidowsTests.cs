using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Fluent;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.Functional;
using static Ave.Extensions.Documents.Fluent.Elements;

namespace UnitTests.Extensions.Documents.Fluent;

/// <summary>
/// <c>orphans</c> and <c>widows</c> (CSS Fragmentation 3 §3.3, initial value 2, inherited), against Chromium's print of
/// the same paragraphs (<c>tools/reference/chromium</c>, cases <c>c-*</c>). The page area holds ten lines (276 × 150 pt;
/// Chromium's line is 14.25 pt, the engine's 14.4, and both fit ten). Which words are on which page is compared exactly.
/// </summary>
public class OrphansWidowsTests
{
    private static PageSetup Area => Page(300.Pt(), 174.Pt()).Margin(12.Pt());

    private static Element[] Intro(int count) => [.. Enumerable.Range(0, count).Select(i => (Element)Paragraph($"i{i}"))];

    private static ParagraphElement Lines(int count, string prefix) =>
        Paragraph([.. Enumerable.Range(0, count).SelectMany(i => i == 0 ? new[] { Text($"{prefix}{i}") } : [LineBreak(), Text($"{prefix}{i}")])]);

    private static Result<global::Ave.Extensions.Documents.DocumentModel.StyledDocument, global::Ave.Extensions.ErrorPaths.Error> Model(params Element[] body) =>
        DocumentTranslator.ToModel(Document([.. body], Area, title: "Title", language: "en").FontFamily("Lato"));

    private static void ShouldPrintLikeChromium(string caseName, params Element[] body)
    {
        var engine = Model(body).OnSuccessBind(model => DocumentLayout.Run(model, TestFonts.SetWithItalic)).OnSuccessMap(PrintedPages.Engine);

        engine.IsSuccess.Should().BeTrue(engine.IsFailure ? engine.Error.ToString() : string.Empty);
        engine.Value.PageTexts.Should().Equal(PrintedPages.Chromium(caseName).PageTexts, $"the words of each page as Chromium prints {caseName}");
    }

    [Fact]
    public void A_paragraph_of_which_one_line_would_be_left_on_the_page_moves_to_the_next_page() =>
        ShouldPrintLikeChromium("c-orphan", [.. Intro(9), Lines(4, "p")]);

    [Fact]
    public void With_orphans_and_widows_1_that_one_line_stays() =>
        ShouldPrintLikeChromium("c-orphans-one", [.. Intro(9), Lines(4, "p").Orphans(1).Widows(1)]);

    [Fact]
    public void A_paragraph_of_which_one_line_would_go_to_the_next_page_takes_a_second_line_with_it() =>
        ShouldPrintLikeChromium("c-widow", [.. Intro(5), Lines(6, "p")]);

    [Fact]
    public void A_paragraph_too_short_for_both_keeps_its_orphans_and_leaves_one_line_for_the_next_page()
    {
        // Three lines, two of which fit: no break satisfies both values. Chromium keeps the orphans (two lines) and moves
        // one, at a perfect appeal, instead of moving the whole paragraph; the engine follows it (InlineLayout,
        // BreakWhereFull).
        ShouldPrintLikeChromium("c-few-lines", [.. Intro(8), Lines(3, "p")]);
    }

    [Fact]
    public void Orphans_and_widows_of_three_move_three_lines() =>
        ShouldPrintLikeChromium("c-custom", [.. Intro(2), Lines(10, "p").Orphans(3).Widows(3)]);

    private static TableElement Row(ParagraphElement lines) =>
        Table(Columns(Fill(1), Fill(1)), [TableRow([Cell("a"), Cell([lines])])]).GridLines(0.75.Pt(), Color.Black).SplitLinesAtBreaks();

    [Fact]
    public void A_row_whose_cell_would_leave_one_line_on_the_page_moves_to_the_next_page()
    {
        // The cell is a flow of its own: a cut of the row that leaves one of its four lines on the page violates its
        // orphans, so the break before the row wins. With two lines on the page the row is cut.
        ShouldPrintLikeChromium("c-cell-orphan-moves", [.. Intro(9), Row(Lines(4, "b"))]);
        ShouldPrintLikeChromium("c-cell-orphan", [.. Intro(8), Row(Lines(4, "b"))]);
    }

    [Fact]
    public void A_row_whose_cell_has_no_line_on_the_page_yet_moves_whole()
    {
        // A cut where the text cell would hold nothing of itself is a last resort, even though the padded block beside it
        // fits; the row moves whole, as Chromium prints it. In c-cell-first-line neither the line nor the 6 pt block
        // fits below the intro; in fr-cell-first-line-block-fits (4 pt higher) the block fits and the line does not, which
        // is what this test is about (the first case alone does not show it).
        var table = Table(Columns(Fill(1), Fill(1)), [TableRow([Cell("x"), Cell([Block([]).PaddingTop(3.Pt()).PaddingBottom(3.Pt()).Background(new Color(0x22, 0x44, 0xCC))])])])
            .GridLines(0.75.Pt(), Color.Black).SplitLinesAtBreaks();

        ShouldPrintLikeChromium("c-cell-first-line", Paragraph("intro").MarginBottom(130.Pt()), table, Paragraph("after"));
        ShouldPrintLikeChromium("fr-cell-first-line-block-fits", Paragraph("intro").MarginBottom(126.Pt()), table, Paragraph("after"));
    }

    [Fact]
    public void Orphans_and_widows_are_inherited_and_appear_in_the_model()
    {
        var model = Model(Block([Paragraph("inner")]).Orphans(3).Widows(4), Paragraph("outer"));

        model.IsSuccess.Should().BeTrue();
        var block = (global::Ave.Extensions.Documents.DocumentModel.Box)model.Value.Root.Children[0];
        var inner = (global::Ave.Extensions.Documents.DocumentModel.Box)block.Children[0];
        var outer = (global::Ave.Extensions.Documents.DocumentModel.Box)model.Value.Root.Children[1];
        (inner.Style.Orphans, inner.Style.Widows).Should().Be((3, 4));
        (outer.Style.Orphans, outer.Style.Widows).Should().Be((2, 2), "the CSS initial values");
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(2, 0)]
    [InlineData(-1, -1)]
    public void Orphans_and_widows_below_one_are_refused_at_the_author_s_line(int orphans, int widows)
    {
        var result = Model(Paragraph("p").Orphans(orphans).Widows(widows));

        result.IsFailure.Should().BeTrue();
        var codes = result.Error.Parts().Select(part => part.Code).ToList();
        codes.Contains(DocumentErrorCodes.Input.Invalid.Orphans).Should().Be(orphans < 1);
        codes.Contains(DocumentErrorCodes.Input.Invalid.Widows).Should().Be(widows < 1);
        result.Error.Parts().Should().AllSatisfy(part => part.Metadata![ErrorMetadataKeys.SourceFile].ToString().Should().EndWith("OrphansWidowsTests.cs"));
    }
}
