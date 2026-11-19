using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;
using static UnitTests.Extensions.Documents.Layout.Models;

namespace UnitTests.Extensions.Documents.Layout;

/// <summary>
/// Content after a box whose bottom padding or border is taller than the page area (FlowLayout, StackChildren). The box
/// breaks before its bottom inset, which starts the next page and still does not fit there: it overflows that page (the
/// limit of SlicedBox), and what follows the box has no page to go to. Cutting the page to the area would drop it, so
/// the layout is refused (<c>Documents.Layout.DoesNotFit</c>, naming the box). A layout is never a success while
/// content is left that no page shows.
/// </summary>
public class OverflowRefusalTests
{
    /// <summary>The page area of <see cref="Models.Document"/>: 80 pt high.</summary>
    private const double AreaHeight = 80;

    private static BoxStyle PaddingBottom(double padding) => BoxStyle.Initial with { Padding = new Edges(0.Pt(), 0.Pt(), padding.Pt(), 0.Pt()) };

    private static BoxStyle BorderBottom(double width) =>
        BoxStyle.Initial with { Border = new Border(BorderSide.None, BorderSide.None, new BorderSide(width.Pt(), Color.Black), BorderSide.None) };

    [Fact]
    public void Bottom_padding_taller_than_the_page_area_with_a_paragraph_after_it_is_refused_and_names_its_box()
    {
        var error = DocumentLayout.Run(Document(Paragraph(1, "c", PaddingBottom(90)), Paragraph(2, "after")), TestFonts.Set).Error;

        error.Code.Should().Be(LayoutErrorCodes.DoesNotFit);
        error.Metadata![ErrorMetadataKeys.Node].Should().Be("#1", "the paragraph whose padding overflows page 2");
        error.Metadata![ErrorMetadataKeys.Value].Should().Be("80", "the room it had there: the whole page area");
    }

    [Fact]
    public void The_same_box_that_ends_the_document_overflows_its_page_and_is_printed()
    {
        // Nothing follows it, so nothing is lost: the listed limit (bottom padding taller than the page area overflows the
        // page it starts when the box ends the document).
        var pages = DocumentLayout.Run(Document(Paragraph(1, "c", PaddingBottom(90))), TestFonts.Set);

        pages.IsSuccess.Should().BeTrue(pages.IsFailure ? pages.Error.ToString() : string.Empty);
        PrintedPages.Engine(pages.Value).PageTexts.Should().Equal("c", string.Empty);
    }

    /// <summary>
    /// Bottom insets just below, at and just above the page area's height, on a paragraph, a block, two nested blocks, and
    /// a paragraph inside a block with a sibling after it, with and without a paragraph before them, and a paragraph after
    /// them: every document prints each of its words exactly once, or is refused with <c>Documents.Layout.DoesNotFit</c>.
    /// The counts are pinned, so a rule that turns a refusal into pages (or the other way) is a deliberate edit here.
    /// </summary>
    [Fact]
    public void Bottom_insets_near_the_page_area_s_height_print_every_word_once_or_are_refused()
    {
        double[] insets = [AreaHeight - 12, AreaHeight - 0.75, AreaHeight - 0.05, AreaHeight, AreaHeight + 0.05, AreaHeight + 0.75, AreaHeight + 12];
        string[] shapes = ["paragraph padding", "paragraph border", "block padding", "nested blocks", "paragraph in a block", "three lines"];
        var (printed, refused) = (0, 0);
        foreach (var inset in insets)
        {
            foreach (var shape in shapes)
            {
                foreach (var leadIn in new[] { false, true })
                {
                    Node box = shape switch
                    {
                        "paragraph padding" => Paragraph(1, "c", PaddingBottom(inset)),
                        "paragraph border" => Paragraph(1, "c", BorderBottom(inset)),
                        "block padding" => Flow(1, [Paragraph(2, "c")], PaddingBottom(inset)),
                        "nested blocks" => Flow(1, [Flow(2, [Paragraph(3, "c")], PaddingBottom(inset / 2))], PaddingBottom(inset / 2)),
                        "paragraph in a block" => Flow(1, [Paragraph(2, "c", PaddingBottom(inset)), Paragraph(3, "in")]),
                        _ => Paragraph(1, "l0\u2028l1\u2028l2", PaddingBottom(inset)),
                    };
                    Node[] children = leadIn ? [Paragraph(10, "lead"), box, Paragraph(20, "after")] : [box, Paragraph(20, "after")];
                    var document = Document(children);
                    var result = DocumentLayout.Run(document, TestFonts.Set);
                    var name = $"{shape}, inset {inset}, {(leadIn ? "after a paragraph" : "at the page start")}";
                    if (result.IsFailure)
                    {
                        result.Error.Code.Should().Be(LayoutErrorCodes.DoesNotFit, name);
                        refused++;
                        continue;
                    }

                    var words = PrintedPages.Engine(result.Value).Pages.SelectMany(page => page.Words).Select(word => word.Text).Order(StringComparer.Ordinal);
                    words.Should().Equal(Words(document.Root).Order(StringComparer.Ordinal), $"every word once: {name}");
                    printed++;
                }
            }
        }

        (printed, refused).Should().Be((54, 30));
    }

    private static IEnumerable<string> Words(Node node) => node switch
    {
        Box box => box.Children.SelectMany(Words),
        TextRun run => run.Text.Split([' ', '\u2028'], StringSplitOptions.RemoveEmptyEntries),
        _ => [],
    };
}
