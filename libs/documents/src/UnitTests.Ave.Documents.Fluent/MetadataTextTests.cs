using System.Runtime.CompilerServices;
using Ave.Documents;
using Ave.Documents.Core;
using Ave.Documents.Fluent;
using Ave.Documents.Pdf;
using Ave.Documents.Render.Pdf;
using Ave.Documents.TestSupport;
using Ave.Extensions.ErrorPaths;
using static Ave.Documents.Fluent.Elements;

namespace UnitTests.Ave.Documents.Fluent;

/// <summary>
/// Metadata text goes into the XMP packet, which is XML: a character XML 1.0 does not allow (§2.2, production [2]
/// Char) is an input error at the author's line, whatever the profile, never an exception.
/// </summary>
public class MetadataTextTests
{
    private static readonly PdfRenderOptions Options = new(new DateTimeOffset(2026, 11, 15, 9, 22, 0, TimeSpan.FromHours(1)));

    /// <summary>A control character, a form feed, a lone high surrogate, a lone low surrogate and U+FFFE.</summary>
    public static TheoryData<int> Forbidden => [0x01, 0x0C, 0xD800, 0xDC00, 0xFFFE];

    private static string With(int forbidden) => $"Report {(char)forbidden} draft";

    [Theory]
    [MemberData(nameof(Forbidden))]
    public void A_title_with_a_character_XML_forbids_is_an_input_error_at_the_document(int forbidden)
    {
        var line = CurrentLine() + 1;
        var document = Document([Paragraph("x")], title: With(forbidden), language: "en").FontFamily("Lato");

        var error = DocumentTranslator.ToModel(document).Error;

        ShouldPointAt(error, line);
        error.Metadata![ErrorMetadataKeys.Property].Should().Be("title");
        error.Metadata![ErrorMetadataKeys.Offset].Should().Be(7);
    }

    [Theory]
    [MemberData(nameof(Forbidden))]
    public void An_author_with_a_character_XML_forbids_is_an_input_error_at_the_document(int forbidden)
    {
        var line = CurrentLine() + 1;
        var document = Document([Paragraph("x")], author: With(forbidden)).FontFamily("Lato");

        var error = DocumentTranslator.ToModel(document).Error;

        ShouldPointAt(error, line);
        error.Metadata![ErrorMetadataKeys.Property].Should().Be("author");
    }

    [Fact]
    public void An_attachment_name_or_description_with_a_character_XML_forbids_is_an_input_error_at_the_attachment()
    {
        var line = CurrentLine() + 1;
        var named = Attachment(With(0x01), "<a/>"u8, "application/xml");
        var described = Attachment("b.xml", "<b/>"u8, "application/xml").WithDescription(With(0x02));

        var errors = DocumentTranslator.ToModel(Document([Paragraph("x")]).FontFamily("Lato").Attach(named).Attach(described)).Error.Parts();

        errors.Should().HaveCount(2);
        ShouldPointAt(errors[0], line);
        errors[0].Metadata![ErrorMetadataKeys.Property].Should().Be("file-name");
        ShouldPointAt(errors[1], line + 1);
        errors[1].Metadata![ErrorMetadataKeys.Property].Should().Be("description");
    }

    [Fact]
    public void Tab_line_feed_carriage_return_and_characters_outside_the_basic_plane_are_allowed()
    {
        var title = "A\tB\nC\rD " + char.ConvertFromUtf32(0x1F600);

        DocumentTranslator.ToModel(Document([Paragraph("x")], title: title).FontFamily("Lato")).IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData("plain")]
    [InlineData("3b")]
    [InlineData("3a+ua1")]
    [InlineData("ua1")]
    public void Every_profile_returns_the_error_instead_of_throwing(string name)
    {
        var profile = name switch
        {
            "plain" => PdfProfile.Plain,
            "3b" => PdfProfile.PdfA3b,
            "ua1" => PdfProfile.PdfUa1,
            _ => PdfProfile.PdfA3a.AndPdfUa1(),
        };
        var document = Document([Paragraph("x")], title: With(0x01), language: "en").FontFamily("Lato");

        var result = document.ToPdf(TestFonts.Set, Options with { Profile = profile });

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DocumentErrorCodes.Input.Invalid.MetadataText);
    }

    private static void ShouldPointAt(Error error, int line)
    {
        error.Code.Should().Be(DocumentErrorCodes.Input.Invalid.MetadataText);
        error.Metadata![ErrorMetadataKeys.SourceFile].Should().Be(ThisFile());
        error.Metadata![ErrorMetadataKeys.SourceLine].Should().Be(line);
    }

    private static int CurrentLine([CallerLineNumber] int line = 0) => line;

    private static string ThisFile([CallerFilePath] string file = "") => file;
}
