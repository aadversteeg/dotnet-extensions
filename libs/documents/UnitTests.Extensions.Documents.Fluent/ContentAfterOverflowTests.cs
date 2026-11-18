using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Fluent;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.Functional;
using static Ave.Extensions.Documents.Fluent.Elements;

namespace UnitTests.Extensions.Documents.Fluent;

/// <summary>
/// Content after a box that does not fit the page with its bottom padding or border (review 40, blocker B1). A layout is
/// never a success while content is left that no page shows: every case prints all its words, or it is refused with
/// <c>Documents.Layout.DoesNotFit</c>. Where the box's content fits and its bottom padding or border does not, the box
/// breaks before them and they start the next page, as Chromium prints it (SlicedBox; the cases of
/// <see cref="BottomPaddingTests"/>). The cases are the review's (m…: page width × height, margin 12 pt; r…: A4,
/// margin 12 pt, page area 818 pt), each with the outcome it has.
/// </summary>
public class ContentAfterOverflowTests
{
    private const string Refused = "refused";

    private static ParagraphElement Lines(params string[] lines) =>
        Paragraph([.. lines.SelectMany((line, index) => index == 0 ? new[] { Text(line) } : [LineBreak(), Text(line)])]);

    private static ImageElement Figure(double height) => Image(TestImages.Bytes("rgb8-all-filters.png")).Alt("chart").Height(height.Pt());

    private static (double Width, double Height, Element[] Body) Case(string name) => name switch
    {
        "m5" => (150, 84, [Block([Lines("a", "b", "c")]).PaddingBottom(50.Pt()), Paragraph("AFTER")]),
        "m9" => (150, 84, [Paragraph("c").PaddingBottom(50.Pt()), Paragraph("AFTER")]),
        "m10" => (150, 84, [Block([Paragraph("c").PaddingBottom(50.Pt()), Paragraph("IN")]), Paragraph("AFTER")]),
        "m11" => (150, 84, [Paragraph("c").PaddingTop(30.Pt()).PaddingBottom(30.Pt()), Paragraph("AFTER")]),
        "m12" => (150, 84, [Paragraph("c").Border(25.Pt(), Color.Black), Paragraph("AFTER")]),
        "m42" => (120, 120, [Block([Paragraph("a").MarginBottom(40.Pt())]).PaddingBottom(60.Pt()), Paragraph("AFTER")]),
        "m43" => (120, 120, [Paragraph("x"), Block([Paragraph("a").MarginBottom(40.Pt())]).PaddingBottom(60.Pt()), Paragraph("AFTER")]),
        "m44" => (120, 120, [Lines("1", "2", "3", "4", "5"), Block([Lines("a", "b", "c").MarginBottom(40.Pt())]).PaddingBottom(60.Pt()), Paragraph("AFTER")]),
        "m45" => (120, 120, [Block([Paragraph("a").MarginBottom(30.Pt())]).PaddingBottom(60.Pt()), Paragraph("AFTER")]),
        "r1" => (595, 842, [Paragraph("intro"), Block([Figure(700)]).PaddingTop(12.Pt()).PaddingBottom(12.Pt()), Paragraph("AFTER")]),
        "r2" => (595, 842, [Block([Figure(800)]).PaddingTop(12.Pt()).PaddingBottom(12.Pt()), Paragraph("AFTER")]),
        "r3" => (595, 842, [Paragraph("intro"), Block([Figure(800)]).PaddingTop(12.Pt()).PaddingBottom(12.Pt()), Paragraph("AFTER")]),
        "r4" => (595, 842, [Paragraph("intro"), Block([Figure(790), Paragraph("Figure 1")]).PaddingTop(12.Pt()).PaddingBottom(12.Pt()), Paragraph("AFTER")]),
        "r5" => (595, 842, [Paragraph("intro"), Block([Figure(800)]).PaddingBottom(6.Pt()).MarginBottom(24.Pt()), Paragraph("AFTER")]),
        "r6" => (595, 842, [Paragraph("intro"), Figure(810), Paragraph("AFTER")]),
        "r7" => (595, 842, [Paragraph("intro"), Figure(830), Paragraph("AFTER")]),
        "r8" => (595, 842, [Paragraph("intro"), Table([Fill()], [TableRow([Cell([Figure(800)]).PaddingTop(12.Pt()).PaddingBottom(12.Pt())])]), Paragraph("AFTER")]),
        "r9" => (595, 842, [Paragraph("intro"), Block([Figure(800)]).PaddingTop(9.Pt()).PaddingBottom(9.Pt()).Border(1.Pt(), Color.Black), Paragraph("AFTER")]),
        "r10" => (595, 842, [Paragraph("intro"), Block([Figure(800)]).PaddingTop(12.Pt()).PaddingBottom(12.Pt()), .. Enumerable.Range(0, 200).Select(index => Paragraph($"p{index}"))]),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "No such case."),
    };

    /// <summary>The words of every page, pages separated by <c> | </c>, or <see cref="Refused"/> for a DoesNotFit
    /// refusal.</summary>
    private static string Outcome(string name)
    {
        var (width, height, body) = Case(name);
        var result = DocumentTranslator.ToModel(Document([.. body], Page(width.Pt(), height.Pt()).Margin(12.Pt()), title: "Title", language: "en").FontFamily("Lato"))
            .OnSuccessBind(model => DocumentLayout.Run(model, TestFonts.SetWithItalic));
        if (result.IsFailure)
        {
            result.Error.Code.Should().Be(LayoutErrorCodes.DoesNotFit, result.Error.ToString());
            return Refused;
        }

        return string.Join(" | ", PrintedPages.Engine(result.Value).PageTexts);
    }

    public static TheoryData<string, string> Cases => new()
    {
        // A box whose bottom padding or border does not fit below its content: they start the next page, and what follows
        // comes after them (the content after such a box used to be lost).
        { "m5", "a b | c |  | AFTER" },
        { "m9", "c |  | AFTER" },
        { "m10", "c |  | IN AFTER" },
        { "m11", "c | AFTER" },
        { "m12", "c | AFTER" },
        { "m42", "a | AFTER" },
        { "m43", "x | a | AFTER" },
        { "m44", "1 2 3 4 5 | a b | c | AFTER" },
        { "m45", "a | AFTER" },
        { "r2", " | AFTER" },
        { "r3", "intro |  | AFTER" },
        { "r9", "intro |  | AFTER" },

        // Controls: what fits prints whole.
        { "r1", "intro AFTER" },
        { "r4", "intro | Figure 1 AFTER" },
        { "r5", "intro |  | AFTER" },
        { "r6", "intro |  | AFTER" },

        // Taller than any page without a breakpoint: refused, as before.
        { "r7", Refused },
        { "r8", Refused },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Content_after_a_box_that_overflows_the_page_is_printed_or_the_document_is_refused(string name, string expected)
    {
        Outcome(name).Should().Be(expected, $"case {name} of review 40");
    }

    [Fact]
    public void Two_hundred_paragraphs_after_a_framed_figure_are_all_printed_once()
    {
        // Review case r10: the figure's bottom padding starts page 3, the 200 paragraphs follow it.
        var pages = Outcome("r10").Split(" | ");

        pages[..2].Should().Equal("intro", string.Empty);
        string.Join(" ", pages[2..]).Split(' ').Should().Equal(Enumerable.Range(0, 200).Select(index => $"p{index}"));
    }

    [Fact]
    public void Bottom_padding_taller_than_the_page_with_content_after_it_is_refused_and_names_its_box()
    {
        // The padding (70 pt) starts page 2 alone and still does not fit the 60 pt page area: it overflows there (the
        // limit of SlicedBox), so "AFTER" has no page to go to, and the document is refused instead of losing it.
        var line = CallerLine() + 1;
        var body = new Element[] { Paragraph("c").PaddingBottom(70.Pt()), Paragraph("AFTER") };
        var error = DocumentTranslator.ToModel(Document([.. body], Page(150.Pt(), 84.Pt()).Margin(12.Pt()), title: "Title", language: "en").FontFamily("Lato"))
            .OnSuccessBind(model => DocumentLayout.Run(model, TestFonts.SetWithItalic))
            .Error;

        error.Code.Should().Be(LayoutErrorCodes.DoesNotFit);
        error.Metadata![ErrorMetadataKeys.SourceLine].Should().Be(line, "the paragraph whose padding overflows");
        error.Metadata![ErrorMetadataKeys.Value].Should().Be("60", "the room it had on page 2");
    }

    private static int CallerLine([System.Runtime.CompilerServices.CallerLineNumber] int line = 0) => line;
}
