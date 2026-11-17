using System.Runtime.CompilerServices;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Fluent;
using Ave.Extensions.ErrorPaths;
using static Ave.Extensions.Documents.Fluent.Elements;

namespace UnitTests.Extensions.Documents.Fluent;

/// <summary>
/// Every invalid value is reported with its documented code and with the file and line of the call that set it.
/// In each test the offending call is on the line right after <c>CurrentLine()</c>.
/// </summary>
public class InputErrorTests
{
    private static Error Fail(Document document)
    {
        var result = DocumentTranslator.ToModel(document);
        result.IsFailure.Should().BeTrue();
        return result.Error;
    }

    private static void ShouldPointAt(Error error, ErrorCode code, int line)
    {
        error.Code.Should().Be(code);
        error.Metadata![ErrorMetadataKeys.SourceFile].Should().Be(ThisFile());
        error.Metadata![ErrorMetadataKeys.SourceLine].Should().Be(line);
    }

    [Fact]
    public void Negative_padding_is_an_invalid_length()
    {
        var line = CurrentLine() + 1;
        var paragraph = Paragraph("x").Padding(1.Pt()).PaddingBottom((-2).Pt());

        var error = Fail(Document([paragraph]).FontFamily("Lato"));

        ShouldPointAt(error, DocumentErrorCodes.Input.Invalid.Length, line);
        error.Metadata![ErrorMetadataKeys.Property].Should().Be("padding-bottom");
        error.Metadata![ErrorMetadataKeys.Value].Should().Be("-2pt");
    }

    [Fact]
    public void A_length_that_is_not_a_number_is_invalid()
    {
        var line = CurrentLine() + 1;
        var block = Block([Paragraph("x")]).Margin(Length.FromPoints(double.NaN));

        ShouldPointAt(FirstPart(Fail(Document([block]).FontFamily("Lato"))), DocumentErrorCodes.Input.Invalid.Length, line);
    }

    [Fact]
    public void A_negative_margin_is_reported_as_not_supported_yet()
    {
        var line = CurrentLine() + 1;
        var block = Block([Paragraph("x")]).MarginTop((-1).Pt());

        ShouldPointAt(Fail(Document([block]).FontFamily("Lato")), DocumentErrorCodes.Input.Unsupported.NegativeMargin, line);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-5.0)]
    [InlineData(double.PositiveInfinity)]
    public void A_font_size_that_is_not_positive_is_invalid(double size)
    {
        var line = CurrentLine() + 1;
        var span = Text("x").FontSize(size.Pt());

        ShouldPointAt(Fail(Document([Paragraph([span])]).FontFamily("Lato")), DocumentErrorCodes.Input.Invalid.FontSize, line);
    }

    [Fact]
    public void A_font_weight_outside_1_to_1000_is_invalid()
    {
        var line = CurrentLine() + 1;
        var paragraph = Paragraph("x").FontWeight(new FontWeight(0));

        ShouldPointAt(Fail(Document([paragraph]).FontFamily("Lato")), DocumentErrorCodes.Input.Invalid.FontWeight, line);
    }

    [Fact]
    public void An_empty_font_family_is_invalid()
    {
        var line = CurrentLine() + 1;
        var block = Block([Paragraph("x")]).FontFamily(" ");

        ShouldPointAt(Fail(Document([block]).FontFamily("Lato")), DocumentErrorCodes.Input.Invalid.FontFamily, line);
    }

    [Fact]
    public void A_document_without_a_font_family_is_invalid_because_fonts_are_explicit_input()
    {
        var line = CurrentLine() + 1;
        var document = Document([Paragraph("x")]).FontSize(11.Pt());

        ShouldPointAt(Fail(document), DocumentErrorCodes.Input.Invalid.FontFamily, line);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public void A_heading_level_outside_1_to_6_is_invalid(int level)
    {
        var line = CurrentLine() + 1;
        var heading = Heading(level, "Title");

        ShouldPointAt(Fail(Document([heading]).FontFamily("Lato")), DocumentErrorCodes.Input.Invalid.HeadingLevel, line);
    }

    [Fact]
    public void A_page_break_with_nothing_after_it_is_invalid()
    {
        var line = CurrentLine() + 1;
        var pageBreak = PageBreak();

        ShouldPointAt(Fail(Document([Paragraph("x"), pageBreak]).FontFamily("Lato")), DocumentErrorCodes.Input.Invalid.PageBreak, line);
    }

    [Fact]
    public void Page_margins_that_leave_no_room_are_an_invalid_page_size()
    {
        var line = CurrentLine() + 1;
        var document = Document([Paragraph("x")], PageSetup.A4 with { Margins = Edges.All(11.Cm()) }).FontFamily("Lato");

        ShouldPointAt(Fail(document), DocumentErrorCodes.Input.Invalid.PageSize, line);
    }

    [Fact]
    public void All_problems_are_reported_together_each_with_its_own_line()
    {
        var first = CurrentLine() + 1;
        var badHeading = Heading(9, "Title");
        var second = CurrentLine() + 1;
        var badSpan = Text("x").FontSize(0.Pt());

        var error = Fail(Document([badHeading, Paragraph([badSpan])]).FontFamily("Lato"));

        error.Code.Should().Be(ErrorCodes.Aggregate);
        var parts = error.Parts();
        parts.Select(part => part.Metadata![ErrorMetadataKeys.SourceLine]).Should().Equal(first, second);
        parts.Select(part => part.Code).Should().Equal(DocumentErrorCodes.Input.Invalid.HeadingLevel, DocumentErrorCodes.Input.Invalid.FontSize);
    }

    [Fact]
    public void A_shared_style_with_box_properties_on_a_span_is_not_applicable()
    {
        var line = CurrentLine() + 1;
        var span = Text("x").With(Style.Empty.MarginTop(2.Pt()));

        var error = Fail(Document([Paragraph([span])]).FontFamily("Lato"));

        ShouldPointAt(error, DocumentErrorCodes.Input.Invalid.NotApplicable, line);
    }

    [Fact]
    public void A_shared_style_with_a_text_alignment_on_a_span_is_not_applicable()
    {
        var line = CurrentLine() + 1;
        var span = Text("x").With(Style.Empty.TextAlign(TextAlign.Center));

        ShouldPointAt(Fail(Document([Paragraph([span])]).FontFamily("Lato")), DocumentErrorCodes.Input.Invalid.NotApplicable, line);
    }

    [Fact]
    public void An_invalid_value_from_a_shared_style_is_reported_at_the_element_that_uses_it()
    {
        var style = Style.Empty.FontSize((-1).Pt());
        var line = CurrentLine() + 1;
        var paragraph = Paragraph("x").With(style);

        ShouldPointAt(Fail(Document([paragraph]).FontFamily("Lato")), DocumentErrorCodes.Input.Invalid.FontSize, line);
    }

    private static Error FirstPart(Error error) => error.Parts()[0];

    private static int CurrentLine([CallerLineNumber] int line = 0) => line;

    private static string ThisFile([CallerFilePath] string file = "") => file;
}
