using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Fluent;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.Functional;
using static Ave.Extensions.Documents.Fluent.Elements;

namespace UnitTests.Extensions.Documents.Fluent;

/// <summary>
/// A box that laid itself out again to one of its early breaks keeps that break, and the box around it compares it with
/// its own breakpoints as perfect, lowered only by the <c>break-inside: avoid</c> of the boxes the break lies inside
/// (<c>BreakAvoidance.Kept</c>, as Chromium records the break it lays itself out again to). So a break against orphans,
/// widows or a keep-with-next inside a box stays inside the box (cases <c>fr-pad-own-break</c>,
/// <c>fr-pad-against-keep</c>, <c>fr-keep-inside-box</c> in <see cref="BottomPaddingTests"/>), and a break inside a box or
/// paragraph kept together loses to a better breakpoint before the box, which then moves whole: the cases <c>kb-*</c> of
/// <c>tools/reference/chromium</c>, in two forms. Form A: the kept content fits on the page and its box's bottom padding
/// or border does not (page area 150 pt, 18 pt lines, <c>orphans</c> and <c>widows</c> 1). Form B: no padding, a kept box
/// whose break lies before a box with keep-with-next inside it (page area 180 pt, <c>orphans</c> and <c>widows</c> 2).
/// </summary>
public class KeptBoxBreakTests
{
    private static ParagraphElement Lines(params string[] lines) =>
        Paragraph([.. lines.SelectMany((line, index) => index == 0 ? new[] { Text(line) } : [LineBreak(), Text(line)])]);

    private static ParagraphElement Numbered(string prefix, int count) => Lines([.. Enumerable.Range(0, count).Select(index => $"{prefix}{index}")]);

    private static TableElement Rows(params string[] rows) =>
        Table([Fill()], [.. rows.Select(row => (TablePart)TableRow([Cell([Paragraph(row)])]))]).GridLines(0.75.Pt(), Color.Black).SplitLinesAtBreaks();

    /// <summary>The engine's pages of the body on a page of the given height, 18 pt lines, with the document's orphans
    /// and widows (<c>body { orphans; widows }</c> in the case).</summary>
    private static string Engine(double height, int orphansAndWidows, params Element[] body)
    {
        var document = Document([.. body], Page(300.Pt(), height.Pt()).Margin(12.Pt()), title: "Title", language: "en")
            .FontFamily("Lato").FontSize(12.Pt()).LineHeight(18.Pt()).Orphans(orphansAndWidows).Widows(orphansAndWidows);
        var result = DocumentTranslator.ToModel(document)
            .OnSuccessBind(model => DocumentLayout.Run(model, TestFonts.SetWithItalic))
            .OnSuccessMap(PrintedPages.Engine);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        return string.Join(" | ", result.Value.PageTexts);
    }

    private static string Chromium(string caseName) => string.Join(" | ", PrintedPages.Chromium(caseName).PageTexts);

    /// <summary>Form A: "i" with a bottom margin, then the kept content, then "z".</summary>
    private static (int OrphansAndWidows, Element[] Body) FormA(string caseName)
    {
        var a = Lines("a0", "a1", "a2");
        Element After(double margin) => Paragraph("i").MarginBottom(margin.Pt());
        return caseName switch
        {
            "kb-n1-kept-box-padding" => (1, [After(63), Block([a]).Padding(9.Pt()).KeepTogether()]),
            "kb-n1c-kept-box-padding-fits-control" => (1, [After(57), Block([a]).Padding(9.Pt()).KeepTogether()]),
            "kb-n2-kept-para-in-padded-box" => (1, [After(63), Block([a.KeepTogether()]).Padding(9.Pt())]),
            "kb-n3-kept-box-padding-bottom-3" => (1, [After(78), Block([a]).PaddingBottom(3.Pt()).KeepTogether()]),
            "kb-n4-kept-box-border-bottom-6" => (1, [After(78), Block([a]).BorderBottom(6.Pt(), Color.Black).KeepTogether()]),
            "kb-n5-kept-padded-box-in-cell" => (1, [After(63), Table([Fill()], [TableRow([Cell([Block([a]).Padding(9.Pt()).KeepTogether()])])]).GridLines(0.75.Pt(), Color.Black).SplitLinesAtBreaks()]),
            "kb-n6-kept-para-in-padded-box-in-cell" => (1, [After(63), Table([Fill()], [TableRow([Cell([Block([a.KeepTogether()]).Padding(9.Pt())])])]).GridLines(0.75.Pt(), Color.Black).SplitLinesAtBreaks()]),
            "kb-n7-kept-box-three-paragraphs" => (1, [After(63), Block([Paragraph("a0"), Paragraph("b0"), Paragraph("c0")]).Padding(9.Pt()).KeepTogether()]),
            "kb-n8-kept-box-around-padded-box" => (1, [After(63), Block([Block([a]).Padding(9.Pt())]).KeepTogether()]),
            "kb-n9-padded-box-not-kept-control" => (1, [After(63), Block([a]).Padding(9.Pt())]),
            "kb-n10-kept-para-padding-bottom" => (1, [After(78), a.KeepTogether().PaddingBottom(3.Pt())]),
            "kb-n11-kept-table-in-padded-box" => (1, [After(63), Block([Rows("r0", "r1").KeepTogether()]).Padding(9.Pt())]),
            "kb-n12-kept-box-orphans2-default" => (2, [After(63), Block([a]).Padding(9.Pt()).KeepTogether()]),
            "kb-v8-padded-box-kept-para" => (1, [After(63), Block([a.KeepTogether()]).Padding(9.Pt()).Border(0.75.Pt(), Color.Black)]),
            _ => (1, [After(63), Block([Rows("r0", "r1", "r2").KeepTogether()]).Padding(9.Pt()).Border(0.75.Pt(), Color.Black)]),
        };
    }

    [Theory]
    [InlineData("kb-n1-kept-box-padding")]
    [InlineData("kb-n1c-kept-box-padding-fits-control")]
    [InlineData("kb-n2-kept-para-in-padded-box")]
    [InlineData("kb-n3-kept-box-padding-bottom-3")]
    [InlineData("kb-n4-kept-box-border-bottom-6")]
    [InlineData("kb-n5-kept-padded-box-in-cell")]
    [InlineData("kb-n6-kept-para-in-padded-box-in-cell")]
    [InlineData("kb-n7-kept-box-three-paragraphs")]
    [InlineData("kb-n8-kept-box-around-padded-box")]
    [InlineData("kb-n9-padded-box-not-kept-control")]
    [InlineData("kb-n10-kept-para-padding-bottom")]
    [InlineData("kb-n11-kept-table-in-padded-box")]
    [InlineData("kb-n12-kept-box-orphans2-default")]
    [InlineData("kb-v8-padded-box-kept-para")]
    [InlineData("kb-v9-padded-box-kept-table-row")]
    public void Kept_content_whose_box_s_bottom_inset_does_not_fit_moves_whole_where_a_better_break_lies_before_it(string caseName)
    {
        // The box's content fits on page 1 and its bottom padding or border does not. Kept together (the box, its paragraph,
        // a box around it, in a cell or not): the break inside it violates the keep, the break before it after "i" is
        // perfect, and the box moves whole (i | a0 a1 a2 z). Not kept (n9), or fitting (n1c), or a kept table that ends
        // before the padding (n11): the box breaks inside as before.
        var (orphansAndWidows, body) = FormA(caseName);

        Engine(174, orphansAndWidows, [.. body, Paragraph("z")]).Should().Be(Chromium(caseName), $"the pages as Chromium prints {caseName}");
    }

    /// <summary>Form B: the content before the kept box, then the box around [a box with keep-with-next around b and c],
    /// d (kept, maybe with keep-with-next) and 12 lines y; then "z".</summary>
    private static Element[] FormB(string caseName)
    {
        var bKept = caseName is "kb-s0-base" or "kb-s1-outer-box-not-kept";
        var dKeepsWithNext = caseName is "kb-s0-base" or "kb-s1-outer-box-not-kept" or "kb-s2-b-not-kept";
        var outerKept = caseName is not ("kb-s1-outer-box-not-kept" or "kb-s8-no-keeps-but-inner-keep-with-next" or "kb-s9-a-orphans-no-kept-box");
        var b = Numbered("b", 3);
        var d = Numbered("d", 4).KeepTogether();
        Element outer = Block([
            Block([bKept ? b.KeepTogether() : b, Numbered("c", 3)]).KeepWithNext(),
            dKeepsWithNext ? d.KeepWithNext() : d,
            Numbered("y", 12)]);
        outer = outerKept ? ((BlockElement)outer).KeepTogether() : outer;
        Element[] before = caseName switch
        {
            "kb-s0-base" or "kb-s1-outer-box-not-kept" or "kb-s2-b-not-kept" or "kb-s3-d-kept-only" => [Numbered("a", 3).KeepWithNext().Orphans(5)],
            "kb-s4-a-keep-with-next-only" => [Numbered("a", 3).KeepWithNext()],
            "kb-s5-perfect-break-two-before" => [Lines("i0", "i1"), Paragraph("j").KeepWithNext()],
            "kb-s9-a-orphans-no-kept-box" => [Numbered("a", 3).Orphans(5)],
            _ => [Numbered("i", 3)],
        };
        return [.. before, outer, Paragraph("z")];
    }

    [Theory]
    [InlineData("kb-s0-base")]
    [InlineData("kb-s1-outer-box-not-kept")]
    [InlineData("kb-s2-b-not-kept")]
    [InlineData("kb-s3-d-kept-only")]
    [InlineData("kb-s4-a-keep-with-next-only")]
    [InlineData("kb-s5-perfect-break-two-before")]
    [InlineData("kb-s7-perfect-break-before-kept-box")]
    [InlineData("kb-s8-no-keeps-but-inner-keep-with-next")]
    [InlineData("kb-s9-a-orphans-no-kept-box")]
    public void A_kept_box_whose_early_break_lies_before_a_keep_with_next_inside_it_loses_to_a_better_break_before_it(string caseName)
    {
        // Page 1 runs out inside the kept box; its best breakpoint inside lies before the keep-with-next box (b and c
        // with d), and the box lays itself out again to it. Kept together, that break is worse than the break before the
        // box (or, with "a" kept with the box and its orphans at 5, the break inside "a"), and the box moves whole: s7
        // prints i0 i1 i2 | b0 … d3 | y0 … y9 | y10 y11 z. The outer box not kept (s1, s8, s9): it keeps its break.
        Engine(204, 2, FormB(caseName)).Should().Be(Chromium(caseName), $"the pages as Chromium prints {caseName}");
    }
}
