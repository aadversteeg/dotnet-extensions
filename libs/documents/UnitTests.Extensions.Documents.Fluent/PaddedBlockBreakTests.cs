using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Fluent;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.Functional;
using static Ave.Extensions.Documents.Fluent.Elements;

namespace UnitTests.Extensions.Documents.Fluent;

/// <summary>
/// A block with padding whose content fits the page but whose bottom padding does not fit under it: the block cannot end
/// on the page, so it breaks at the best breakpoint inside its content (an early break), and the fragment that breaks has
/// no bottom padding (<c>box-decoration-break: slice</c>, CSS Fragmentation 3 §5.4). Against Chromium's print of the
/// same blocks (<c>tools/reference/chromium</c>, cases <c>a3-pad-*</c>): the two first-on-page cases of review 36, which
/// milestone 1 refused or broke, print as Chromium prints them. Page area 276 × 180 pt; 20 pt padding all round.
/// </summary>
public class PaddedBlockBreakTests
{
    private static PageSetup Area => Page(300.Pt(), 204.Pt()).Margin(12.Pt());

    private static readonly Color Grey = new(0xCC, 0xCC, 0xCC);

    private static ParagraphElement Kept() =>
        Paragraph([.. Enumerable.Range(0, 10).SelectMany(i => i == 0 ? new[] { Text($"k{i}") } : [LineBreak(), Text($"k{i}")])]).KeepTogether();

    private static BlockElement Padded(Element first) => Block([first, Paragraph("tail")]).Padding(20.Pt()).Background(Grey);

    private static void ShouldPrintLikeChromium(string caseName, params Element[] body)
    {
        var engine = DocumentTranslator.ToModel(Document([.. body], Area, title: "Title", language: "en").FontFamily("Lato"))
            .OnSuccessBind(model => DocumentLayout.Run(model, TestFonts.SetWithItalic))
            .OnSuccessMap(PrintedPages.Engine);

        engine.IsSuccess.Should().BeTrue(engine.IsFailure ? engine.Error.ToString() : string.Empty);
        engine.Value.PageTexts.Should().Equal(PrintedPages.Chromium(caseName).PageTexts, $"the words of each page as Chromium prints {caseName}");
    }

    [Fact]
    public void A_padded_block_at_the_top_of_a_page_keeps_a_line_that_fits_with_its_top_padding_only()
    {
        // The 118 pt line fits under the top padding, but not with the bottom padding too: milestone 1 refused this.
        // The break goes before "tail", where no bottom padding is needed.
        ShouldPrintLikeChromium("a3-pad-line", Padded(Paragraph("W").FontSize(118.Pt())), Paragraph("after"));
    }

    [Fact]
    public void A_paragraph_kept_together_at_the_top_of_a_page_inside_a_padded_block_is_not_broken()
    {
        // Milestone 1 broke the kept paragraph although it fits; the break goes before "tail" instead, at the top of the
        // page and lower on it alike.
        ShouldPrintLikeChromium("a3-pad-kept", Padded(Kept()), Paragraph("after"));
        ShouldPrintLikeChromium("a3-pad-kept-lower", Paragraph("intro"), Padded(Kept()), Paragraph("after"));
    }
}
