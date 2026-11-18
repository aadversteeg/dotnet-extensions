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
/// <c>Documents.Layout.DoesNotFit</c>. The cases are the review's (m…: page width × height, margin 12 pt; r…: A4, margin
/// 12 pt, page area 818 pt), each with the outcome it has.
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
        // A box at the top of a page whose bottom padding or border does not fit below its content, with content after
        // it: refused (the content after it used to be lost).
        { "m5", Refused },
        { "m9", Refused },
        { "m10", Refused },
        { "m11", Refused },
        { "m12", Refused },
        { "m42", Refused },
        { "m43", Refused },
        { "m44", Refused },
        { "m45", Refused },
        { "r2", Refused },
        { "r3", Refused },
        { "r9", Refused },
        { "r10", Refused },

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
    public void The_refusal_names_the_box_that_overflows_and_the_room_it_had()
    {
        var (width, height, body) = Case("r3");
        var error = DocumentTranslator.ToModel(Document([.. body], Page(width.Pt(), height.Pt()).Margin(12.Pt()), title: "Title", language: "en").FontFamily("Lato"))
            .OnSuccessBind(model => DocumentLayout.Run(model, TestFonts.SetWithItalic))
            .Error;

        // The framed figure (800 pt plus 24 pt of padding) starts page 2, whose area is 818 pt.
        error.Code.Should().Be(LayoutErrorCodes.DoesNotFit);
        error.Message.Should().StartWith("The generic made at ContentAfterOverflowTests.cs");
        error.Metadata![ErrorMetadataKeys.Value].Should().Be("818");
    }
}
