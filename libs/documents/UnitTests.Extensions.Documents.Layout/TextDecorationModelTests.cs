using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.DocumentModel;
using Ave.Extensions.Documents.Layout;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;
using static UnitTests.Extensions.Documents.Layout.Models;

namespace UnitTests.Extensions.Documents.Layout;

/// <summary>The model's own checks of the decorations of a run, whichever front-end made it.</summary>
public class TextDecorationModelTests
{
    private static TextDecoration Underline() => new(TextDecorationLine.Underline, Color.Black, "Lato", 12.Pt(), FontWeight.Regular);

    private static Result<Ave.Extensions.Documents.PageModel.PageDocument, Error> Run(ValueList<TextDecoration> decorations)
    {
        var paragraph = new Box(new NodeId(1), SemanticRole.Paragraph, LayoutAlgorithmId.Inline, BoxStyle.Initial,
            [new TextRun(new NodeId(2), "a", Text() with { Decorations = decorations }, At(0))], At(0));
        return DocumentLayout.Run(Document(paragraph), TestFonts.Set);
    }

    [Fact]
    public void A_run_may_carry_as_many_decorations_as_the_limit_and_not_one_more()
    {
        Run([.. Enumerable.Repeat(Underline(), ModelLimits.MaxDecorations)]).IsSuccess.Should().BeTrue();

        var error = Run([.. Enumerable.Repeat(Underline(), ModelLimits.MaxDecorations + 1)]).Error;

        error.Code.Should().Be(DocumentErrorCodes.Input.Invalid.TextDecoration);
        error.Metadata![ErrorMetadataKeys.Value].Should().Be("17");
    }

    public static TheoryData<string, TextDecoration?> Invalid => new()
    {
        { "no decoration", null },
        { "a line outside its enum", new TextDecoration((TextDecorationLine)9, Color.Black, "Lato", 12.Pt(), FontWeight.Regular) },
        { "a blank family", new TextDecoration(TextDecorationLine.Underline, Color.Black, " ", 12.Pt(), FontWeight.Regular) },
        { "no font size", new TextDecoration(TextDecorationLine.Underline, Color.Black, "Lato", 0.Pt(), FontWeight.Regular) },
        { "a font size past the limit", new TextDecoration(TextDecorationLine.Underline, Color.Black, "Lato", 14_401.Pt(), FontWeight.Regular) },
        { "a weight outside 1-1000", new TextDecoration(TextDecorationLine.Underline, Color.Black, "Lato", 12.Pt(), new FontWeight(1001)) },
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public void A_decoration_that_is_not_valid_is_refused(string what, TextDecoration? decoration)
    {
        var error = Run([decoration!]).Error;

        error.Code.Should().Be(DocumentErrorCodes.Input.Invalid.TextDecoration, what);
        error.Metadata![ErrorMetadataKeys.Property].Should().Be("text-decoration");
    }
}
