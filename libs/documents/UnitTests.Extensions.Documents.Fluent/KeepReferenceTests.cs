using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Fluent;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.Functional;
using static Ave.Extensions.Documents.Fluent.Elements;

namespace UnitTests.Extensions.Documents.Fluent;

/// <summary>
/// Keep rules (<c>break-inside: avoid</c>) in tables and on content taller than a page, against Chromium's print of the
/// same content (<c>tools/reference/chromium</c>, cases <c>a5-*</c>; the reviewer's open notes of review 37). Page area
/// 276 × 180 pt (<c>@page { size: 300pt 204pt; margin: 12pt }</c>), 0.75 pt grid lines split at breaks, as Chromium draws
/// them. Which words are on which page is compared exactly; the cases keep every decision clear of the difference in
/// line height (Chromium 14.25 pt, the engine 14.4: README there, "Print positions").
/// </summary>
public class KeepReferenceTests
{
    private static PageSetup Area => Page(300.Pt(), 204.Pt()).Margin(12.Pt());

    private static Element[] Intro(int count) => [.. Enumerable.Range(0, count).Select(i => (Element)Paragraph($"i{i}"))];

    private static ParagraphElement Lines(int count, string prefix) =>
        Paragraph([.. Enumerable.Range(0, count).SelectMany(i => i == 0 ? new[] { Text($"{prefix}{i}") } : [LineBreak(), Text($"{prefix}{i}")])]);

    private static TableElement Table(TableRowElement row) =>
        Elements.Table(Columns(Fill(1), Fill(1)), [row, TableRow([Cell("after"), Cell("z")])]).GridLines(0.75.Pt(), Color.Black).SplitLinesAtBreaks();

    private static void ShouldPrintLikeChromium(string caseName, params Element[] body)
    {
        var engine = DocumentTranslator.ToModel(Document([.. body], Area, title: "Title", language: "en").FontFamily("Lato"))
            .OnSuccessBind(model => DocumentLayout.Run(model, TestFonts.SetWithItalic))
            .OnSuccessMap(PrintedPages.Engine);

        engine.IsSuccess.Should().BeTrue(engine.IsFailure ? engine.Error.ToString() : string.Empty);
        engine.Value.PageTexts.Should().Equal(PrintedPages.Chromium(caseName).PageTexts, $"the words of each page as Chromium prints {caseName}");
    }

    [Fact]
    public void A_cell_kept_together_is_not_cut_inside_its_own_row_while_the_row_can_move()
    {
        // Review 37, note 1: the rule had a Chromium case only between rows (b5). Three lines kept together beside nine:
        // the row does not fit under the five lines of introduction; the cut inside the row would cut the kept cell, the
        // break before the row is perfect, so the row moves to page 2 whole. Without the keep the row is cut.
        ShouldPrintLikeChromium("a5-kept-cell-in-row", [.. Intro(5), Table(TableRow([Cell([Lines(3, "a")]).KeepTogether(), Cell([Lines(9, "b")])]))]);
        ShouldPrintLikeChromium("a5-cell-in-row", [.. Intro(5), Table(TableRow([Cell([Lines(3, "a")]), Cell([Lines(9, "b")])]))]);
    }

    [Fact]
    public void A_row_kept_together_moves_to_the_next_page_whole()
    {
        // Rule S7 (the row's own keep at a cut inside it), which review 37 found pinned by one test only, in another file.
        ShouldPrintLikeChromium("a5-kept-row", [.. Intro(5), Table(TableRow([Cell([Lines(3, "a")]), Cell([Lines(9, "b")])]).KeepTogether())]);
    }

    [Fact]
    public void A_kept_cell_is_counted_by_its_box_so_a_short_kept_cell_beside_a_tall_one_moves_the_row()
    {
        // Review 37, note 2: two kept lines beside twenty. The kept cell's box is as tall as the row, so every cut of the
        // row cuts it: the row moves to page 2 and is cut there, below the kept text. Chromium prints the same.
        ShouldPrintLikeChromium("a5-kept-cell-by-box", [.. Intro(2), Table(TableRow([Cell([Lines(2, "a")]).KeepTogether(), Cell([Lines(20, "b")])]))]);
    }

    [Fact]
    public void Kept_content_taller_than_a_page_moves_to_a_fresh_page_first_and_breaks_there()
    {
        // Review 37, note 3: fourteen lines kept together on a 180 pt page area. The break before them is perfect, so they
        // move to page 2 (page 1 holds only the introduction) and break there as they must. Chromium prints the same.
        ShouldPrintLikeChromium("a5-kept-taller-than-page", Paragraph("intro"), Block([Lines(14, "k")]).KeepTogether(), Paragraph("after"));
    }
}
