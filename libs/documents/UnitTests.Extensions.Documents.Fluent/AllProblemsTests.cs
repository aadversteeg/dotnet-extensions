using System.Runtime.CompilerServices;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Fluent;
using static Ave.Extensions.Documents.Fluent.Elements;

namespace UnitTests.Extensions.Documents.Fluent;

/// <summary>
/// The translator reports every problem of the document at once — in the body and in every page area — so an author
/// fixes them in one go, each with the line of the call that caused it.
/// </summary>
public class AllProblemsTests
{
    [Fact]
    public void Problems_in_the_body_the_header_and_the_footer_are_reported_together()
    {
        // Four bad font sizes, in the body, the header, the footer and the first-page header: all four are reported, the
        // ones in the page areas too, which used to wait until the body was clean.
        var line = CurrentLine() + 2;
        var page = PageSetup.A4.Margin(20.Pt())
            .Footer(Paragraph("footer").FontSize((-1).Pt()))
            .Header(Paragraph("header").FontSize((-2).Pt()));
        var document = Document([Paragraph("body").FontSize((-3).Pt()), Paragraph("body2").FontSize((-4).Pt())], page).FontFamily("Lato");

        var problems = DocumentTranslator.ToModel(document).Error.Parts();

        problems.Should().HaveCount(4).And.OnlyContain(problem => problem.Code == DocumentErrorCodes.Input.Invalid.FontSize);
        problems.Select(problem => (int)problem.Metadata![ErrorMetadataKeys.SourceLine]).Order().Should().Equal(line, line + 1, line + 2, line + 2);
        problems.Select(problem => (string)problem.Metadata![ErrorMetadataKeys.Value]).Order().Should().Equal("-1pt", "-2pt", "-3pt", "-4pt");
    }

    private static int CurrentLine([CallerLineNumber] int line = 0) => line;
}
