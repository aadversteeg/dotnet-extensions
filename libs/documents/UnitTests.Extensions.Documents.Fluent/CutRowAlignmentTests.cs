using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Fluent;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.Functional;
using static Ave.Extensions.Documents.Fluent.Elements;
using Align = Ave.Extensions.Documents.DocumentModel.VerticalAlign;

namespace UnitTests.Extensions.Documents.Fluent;

/// <summary>
/// The cells of a table row that a page break cuts, aligned <c>middle</c> or <c>bottom</c> (<see cref="CutRowAlignment"/>):
/// under <see cref="CutRowAlignment.TopOnEveryPage"/> the rest of every cell starts at the top of its part on every page,
/// and the rows around the cut row stay aligned, as Chromium prints it (<c>tools/reference/chromium</c>, cases
/// <c>fr-cut-row-*</c>, <c>fr-old-cut-cell-*</c>). Page area 276 × 150 pt, Lato 10.5 pt, line height 12 pt, 0.75 pt grid
/// lines split at breaks.
/// </summary>
public class CutRowAlignmentTests
{
    private static ParagraphElement Lines(string prefix, int count) =>
        Paragraph([.. Enumerable.Range(0, count).SelectMany(i => i == 0 ? new[] { Text($"{prefix}{i}") } : [LineBreak(), Text($"{prefix}{i}")])]);

    private static TableElement Grid(params TablePart[] rows) =>
        Table(Columns(Fill(1), Fill(1)), [.. rows]).GridLines(0.75.Pt(), Color.Black).SplitLinesAtBreaks();

    private static CellElement Bottom(ParagraphElement content) => Cell([content]).VerticalAlign(Align.Bottom);

    /// <summary>The body of each case, as its HTML file builds it.</summary>
    private static Element[] Body(string caseName) => caseName switch
    {
        "fr-cut-row-uncut-bottom-cell" =>
            [Paragraph("i").MarginBottom(72.Pt()), Grid(TableRow([Bottom(Lines("a", 2)), Cell([Lines("b", 8)])])), Paragraph("z")],
        "fr-cut-row-three-pages-bottom" =>
            [Paragraph("i").MarginBottom(72.Pt()), Grid(TableRow([Bottom(Lines("a", 19)), Cell([Lines("b", 25)])])), Paragraph("z")],
        "fr-cut-row-between-aligned-rows" =>
        [
            Grid(
                TableRow([Bottom(Paragraph("c0")), Cell([Lines("d", 3)])]),
                TableRow([Bottom(Lines("a", 10)), Cell([Lines("b", 12)])]),
                TableRow([Bottom(Paragraph("e0")), Cell([Lines("f", 3)])])),
            Paragraph("z"),
        ],
        _ => throw new ArgumentOutOfRangeException(nameof(caseName)),
    };

    private static PrintedPages Printed(string caseName, CutRowAlignment alignment)
    {
        var document = Document([.. Body(caseName)], Page(300.Pt(), 174.Pt()).Margin(12.Pt()), title: "Title", language: "en")
            .FontFamily("Lato").FontSize(10.5.Pt()).LineHeight(12.Pt()).Orphans(1).Widows(1)
            .EmptyRowParts(EmptyRowParts.Allow).FontMetricRounding(FontMetricRounding.WholePixels).CutRowAlignment(alignment);
        var result = DocumentTranslator.ToModel(document).OnSuccessBind(model => DocumentLayout.Run(model, TestFonts.SetWithItalic)).OnSuccessMap(PrintedPages.Engine);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : string.Empty);
        return result.Value;
    }

    private static double Baseline(PrintedPages pages, int page, string text) => pages.Pages[page].Words.Single(word => word.Text == text).Baseline;

    [Theory]
    [InlineData("fr-cut-row-uncut-bottom-cell")]
    [InlineData("fr-cut-row-three-pages-bottom")]
    [InlineData("fr-cut-row-between-aligned-rows")]
    public void Under_TopOnEveryPage_every_word_is_where_Chromium_prints_it(string caseName)
    {
        // uncut-bottom-cell: a bottom-aligned cell that fits beside a cut cell starts at the top (a0 beside b0).
        // three-pages: the rest on page 3 starts at the top (a17 beside b17). between-aligned-rows: the rest of the cut row
        // starts at the top of page 2 (a9 beside b9), the rows before and after it stay bottom-aligned (c0 beside d2,
        // e0 beside f2).
        var chromium = PrintedPages.Chromium(caseName);
        var engine = Printed(caseName, CutRowAlignment.TopOnEveryPage);
        var words = engine.Pages.SelectMany(page => page.Words).ToList();
        var measured = chromium.Pages.SelectMany(page => page.Words).ToList();
        words.Select(word => (word.Page, word.Text)).Should().Equal(measured.Select(word => (word.Page, word.Text)));
        words.Zip(measured, (word, other) => Math.Abs(word.Baseline - other.Baseline)).Should().OnlyContain(apart => apart <= 0.375, "every baseline in the same whole pixel");
    }

    [Fact]
    public void Under_RestAlignedInItsPart_the_rest_of_a_cut_cell_is_aligned_in_the_rest_of_its_row()
    {
        // The default: the rest of the bottom-aligned cell sits at the bottom of the row's rest, six lines lower than in
        // Chromium (a17 beside b23, Chromium beside b17), and the cut row's first page and the uncut rows do not change.
        var chromium = PrintedPages.Chromium("fr-cut-row-three-pages-bottom");
        var engine = Printed("fr-cut-row-three-pages-bottom", CutRowAlignment.RestAlignedInItsPart);
        (Baseline(chromium, 2, "a17") - Baseline(chromium, 2, "b17")).Should().BeApproximately(0, 0.01);
        (Baseline(engine, 2, "a17") - Baseline(engine, 2, "b17")).Should().BeApproximately(72, 0.01);
        Baseline(engine, 0, "a0").Should().BeApproximately(Baseline(chromium, 0, "a0"), 0.375, "the part on the first page starts at the top");
        Baseline(engine, 1, "a5").Should().BeApproximately(Baseline(chromium, 1, "a5"), 0.375, "so does the part on a page the row goes on past");
        var between = Printed("fr-cut-row-between-aligned-rows", CutRowAlignment.RestAlignedInItsPart);
        (Baseline(between, 1, "e0") - Baseline(between, 1, "f2")).Should().BeApproximately(0, 0.01, "a row that is not cut is aligned as styled");
    }

    [Theory]
    [InlineData("middle", 36)]
    [InlineData("bottom", 72)]
    public void Under_TopOnEveryPage_the_rest_of_a_cut_middle_or_bottom_cell_starts_at_the_top_as_in_Chromium(string align, double lowerByDefault)
    {
        // Lato 12 pt, line height 18 pt (cases fr-old-cut-cell-middle, -bottom): a3, the rest of the cell, beside b3 on
        // page 2, where the default puts it 36 or 72 pt lower (ListedDifferenceTests).
        var alignment = align == "middle" ? Align.Middle : Align.Bottom;
        var table = Grid(TableRow([Cell([Lines("a", 4)]).VerticalAlign(alignment), Cell([Lines("b", 8)])]));
        PrintedPages Run(CutRowAlignment policy)
        {
            var document = Document([Paragraph("i").MarginBottom(72.Pt()).Orphans(1).Widows(1), table.Orphans(1).Widows(1), Paragraph("z")], Page(300.Pt(), 174.Pt()).Margin(12.Pt()), title: "Title", language: "en")
                .FontFamily("Lato").FontSize(12.Pt()).LineHeight(18.Pt()).CutRowAlignment(policy);
            return DocumentTranslator.ToModel(document).OnSuccessBind(model => DocumentLayout.Run(model, TestFonts.SetWithItalic)).OnSuccessMap(PrintedPages.Engine).Value;
        }

        var chromium = PrintedPages.Chromium($"fr-old-cut-cell-{align}");
        double A3(PrintedPages pages) => Baseline(pages, 1, "a3") - Baseline(pages, 1, "b3");
        A3(chromium).Should().BeApproximately(0, 0.01);
        A3(Run(CutRowAlignment.TopOnEveryPage)).Should().BeApproximately(0, 0.01);
        A3(Run(CutRowAlignment.RestAlignedInItsPart)).Should().BeApproximately(lowerByDefault, 0.01);
    }
}
