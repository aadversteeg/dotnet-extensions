using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Ave.Documents;
using Ave.Documents.Core;
using Ave.Documents.Fluent;
using Ave.Documents.Pdf;
using Ave.Documents.Render.Pdf;
using Ave.Documents.TestSupport;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;
using static Ave.Documents.Fluent.Elements;

namespace UnitTests.Ave.Documents.Fluent;

/// <summary>
/// Every refusal of a conformance profile: the profile names the rule in the error code and
/// the author's line in the metadata; it never weakens the document. In each test the offending call is on the line
/// right after <c>CurrentLine()</c>. Also: the front-end's checks of the new inputs (language, attachments, image
/// descriptions).
/// </summary>
public class ConformanceRefusalTests
{
    private static readonly PdfRenderOptions Plain = new(new DateTimeOffset(2026, 11, 15, 9, 22, 0, TimeSpan.FromHours(1)));

    private static PdfRenderOptions With(PdfProfile profile) => Plain with { Profile = profile };

    private static readonly byte[] Logo = TestImages.Bytes("rgb8.png");

    private static Document Accessible(ValueList<Element> body) =>
        Document(body, title: "Title", language: "en").FontFamily("Lato");

    private static Result<ImmutableArray<byte>, Error> Render(Document document, PdfProfile profile) =>
        document.ToPdf(TestFonts.Set, With(profile));

    private static Error Refused(Document document, PdfProfile profile)
    {
        var result = Render(document, profile);
        result.IsFailure.Should().BeTrue($"{profile} must refuse this document");
        return result.Error;
    }

    private static void ShouldPointAt(Error error, ErrorCode code, int line)
    {
        error.Code.Should().Be(code);
        error.Metadata![ErrorMetadataKeys.SourceFile].Should().Be(ThisFile());
        error.Metadata![ErrorMetadataKeys.SourceLine].Should().Be(line);
    }

    [Fact]
    public void PDF_UA_refuses_an_image_without_alternative_text_at_the_image()
    {
        var line = CurrentLine() + 1;
        var image = Image(Logo);

        var error = Refused(Accessible([image]), PdfProfile.PdfUa1);

        ShouldPointAt(error, PdfRenderErrorCodes.Conformance.PdfUa1.FigureWithoutAlternativeText, line);
        error.Metadata![ErrorMetadataKeys.Node].Should().Be("#1");
    }

    [Fact]
    public void An_image_with_alternative_text_or_marked_decorative_is_accepted_and_only_the_described_one_is_a_figure()
    {
        var pdf = Render(Accessible([Image(Logo).Alt("A red square"), Image(Logo).Decorative()]), PdfProfile.PdfA3a.AndPdfUa1()).Value;

        var figures = PdfStructureReadBack.Read(pdf).Single().DepthFirst().Where(element => element.Type == "Figure").ToList();
        figures.Should().ContainSingle().Which.Details.Should().Be("Alt=\"A red square\"");
    }

    [Fact]
    public void PDF_UA_refuses_a_document_without_a_title_at_the_document()
    {
        var line = CurrentLine() + 1;
        var document = Document([Paragraph("x")], language: "en").FontFamily("Lato");

        ShouldPointAt(Refused(document, PdfProfile.PdfUa1), PdfRenderErrorCodes.Conformance.PdfUa1.MissingTitle, line);
    }

    [Fact]
    public void PDF_UA_refuses_a_document_without_a_language_at_the_document()
    {
        var line = CurrentLine() + 1;
        var document = Document([Paragraph("x")], title: "Title").FontFamily("Lato");

        ShouldPointAt(Refused(document, PdfProfile.PdfA3a.AndPdfUa1()), PdfRenderErrorCodes.Conformance.PdfUa1.MissingLanguage, line);
    }

    [Fact]
    public void PDF_UA_refuses_a_first_heading_that_is_not_level_one()
    {
        var line = CurrentLine() + 1;
        var heading = Heading(2, "Second level first");

        var error = Refused(Accessible([heading, Paragraph("x")]), PdfProfile.PdfUa1);

        ShouldPointAt(error, PdfRenderErrorCodes.Conformance.PdfUa1.FirstHeadingNotH1, line);
        error.Metadata![ErrorMetadataKeys.Value].Should().Be("2");
    }

    [Fact]
    public void PDF_UA_refuses_a_heading_that_skips_a_level_going_down()
    {
        var line = CurrentLine() + 2;
        var body = new Element[] { Heading(1, "One"),
            Heading(3, "Three") };

        var error = Refused(Accessible([.. body]), PdfProfile.PdfUa1);

        ShouldPointAt(error, PdfRenderErrorCodes.Conformance.PdfUa1.HeadingLevelSkipped, line);
        error.Metadata![ErrorMetadataKeys.Value].Should().Be("1->3");
    }

    [Fact]
    public void Headings_may_go_back_up_any_number_of_levels() =>
        Render(Accessible([Heading(1, "a"), Heading(2, "b"), Heading(3, "c"), Heading(1, "d"), Heading(2, "e")]), PdfProfile.PdfUa1)
            .IsSuccess.Should().BeTrue();

    [Fact]
    public void PDF_UA_refuses_a_table_row_that_does_not_fill_every_column_at_the_row()
    {
        var line = CurrentLine() + 3;
        var table = Table(Columns(Fill(1), Fill(1), Fill(1)), [
            HeaderRow([Cell("a"), Cell("b"), Cell("c")]),
            TableRow([Cell("1"), Cell("2")]),
            TableRow([Cell("3").ColumnSpan(2), Cell("4")]),
        ]);

        var error = Refused(Accessible([table]), PdfProfile.PdfUa1);

        ShouldPointAt(error, PdfRenderErrorCodes.Conformance.PdfUa1.IrregularTable, line);
        error.Metadata![ErrorMetadataKeys.Value].Should().Be("2/3");
        Render(Accessible([table]), PdfProfile.PdfA3a).IsSuccess.Should().BeTrue("PDF/A-3a does not have this rule");
    }

    [Fact]
    public void Row_spans_count_as_filled_slots_in_the_rows_below()
    {
        var table = Table(Columns(Fill(1), Fill(1)), [
            HeaderRow([Cell("a"), Cell("b")]),
            TableRow([Cell("tall").RowSpan(2), Cell("1")]),
            TableRow([Cell("2")]),
        ]);

        Render(Accessible([table]), PdfProfile.PdfUa1).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void PDF_A_3_refuses_an_attachment_without_a_relationship_at_the_attachment()
    {
        var line = CurrentLine() + 1;
        var attachment = Attachment("data.xml", "<a/>"u8, "application/xml");

        var error = Refused(Accessible([Paragraph("x")]).Attach(attachment), PdfProfile.PdfA3b);

        ShouldPointAt(error, PdfRenderErrorCodes.Conformance.PdfA3.AttachmentWithoutRelationship, line);
        error.Metadata![ErrorMetadataKeys.Value].Should().Be("data.xml");
    }

    [Fact]
    public void An_attachment_without_a_relationship_is_fine_outside_PDF_A()
    {
        var document = Accessible([Paragraph("x")]).Attach(Attachment("data.xml", "<a/>"u8, "application/xml"));

        Render(document, PdfProfile.Plain).IsSuccess.Should().BeTrue();
        Render(document, PdfProfile.PdfUa1).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Every_violation_is_reported_at_once()
    {
        var document = Document([Heading(2, "x"), Image(Logo)]).FontFamily("Lato")
            .Attach(Attachment("data.xml", "<a/>"u8, "application/xml"));

        var error = Refused(document, PdfProfile.PdfA3a.AndPdfUa1());

        error.Code.Should().Be(ErrorCodes.Aggregate);
        error.Parts().Select(part => part.Code).Should().BeEquivalentTo(
        [
            PdfRenderErrorCodes.Conformance.PdfA3.AttachmentWithoutRelationship,
            PdfRenderErrorCodes.Conformance.PdfUa1.MissingTitle,
            PdfRenderErrorCodes.Conformance.PdfUa1.MissingLanguage,
            PdfRenderErrorCodes.Conformance.PdfUa1.FigureWithoutAlternativeText,
            PdfRenderErrorCodes.Conformance.PdfUa1.FirstHeadingNotH1,
        ]);
        error.Parts().Should().OnlyContain(part => part.Is(PdfRenderErrorCodes.Conformance._));
    }

    [Fact]
    public void The_plain_profile_refuses_none_of_it() =>
        Render(Document([Heading(2, "x"), Image(Logo)]).FontFamily("Lato"), PdfProfile.Plain).IsSuccess.Should().BeTrue();

    [Fact]
    public void A_language_that_is_not_a_language_tag_is_an_input_error_at_the_document()
    {
        var line = CurrentLine() + 1;
        var document = Document([Paragraph("x")], language: "nl_NL").FontFamily("Lato");

        var error = DocumentTranslator.ToModel(document).Error;

        ShouldPointAt(error, DocumentErrorCodes.Input.Invalid.Language, line);
        error.Metadata![ErrorMetadataKeys.Value].Should().Be("nl_NL");
    }

    [Theory]
    [InlineData("", "application/xml", "file-name")]
    [InlineData("data.xml", "xml", "media-type")]
    [InlineData("data.xml", "application/", "media-type")]
    public void An_attachment_needs_a_name_and_a_media_type(string fileName, string mediaType, string property)
    {
        var line = CurrentLine() + 1;
        var attachment = Attachment(fileName, "<a/>"u8, mediaType).WithRelationship(AttachmentRelationship.Data);

        var error = DocumentTranslator.ToModel(Document([Paragraph("x")]).FontFamily("Lato").Attach(attachment)).Error;

        ShouldPointAt(error, DocumentErrorCodes.Input.Invalid.Attachment, line);
        error.Metadata![ErrorMetadataKeys.Property].Should().Be(property);
    }

    [Fact]
    public void Two_attachments_with_the_same_name_are_an_input_error_at_the_second()
    {
        var first = Attachment("data.xml", "<a/>"u8, "application/xml");
        var line = CurrentLine() + 1;
        var second = Attachment("data.xml", "<b/>"u8, "application/xml");

        var error = DocumentTranslator.ToModel(Document([Paragraph("x")]).FontFamily("Lato").Attach(first).Attach(second)).Error;

        ShouldPointAt(error, DocumentErrorCodes.Input.Invalid.Attachment, line);
    }

    [Fact]
    public void An_image_is_described_or_decorative_not_both()
    {
        var line = CurrentLine() + 1;
        var both = Image(Logo).Alt("Logo").Decorative();

        var error = DocumentTranslator.ToModel(Document([both]).FontFamily("Lato")).Error;

        ShouldPointAt(error, DocumentErrorCodes.Input.Invalid.ImageDescription, line);
    }

    [Fact]
    public void A_blank_description_is_an_input_error_at_the_image_s_line_in_every_profile()
    {
        // It says nothing to a reader: outside PDF/UA it was written as /Alt ( ).
        var line = CurrentLine() + 1;
        var blank = Image(Logo).Alt(" ");

        ShouldPointAt(Refused(Accessible([blank]), PdfProfile.PdfUa1), DocumentErrorCodes.Input.Invalid.ImageDescription, line);
        ShouldPointAt(Refused(Accessible([blank]), PdfProfile.Plain), DocumentErrorCodes.Input.Invalid.ImageDescription, line);
    }

    [Fact]
    public void Language_and_attachments_reach_the_document_model_unchanged()
    {
        var attachment = Attachment("data.xml", "<a/>"u8, "application/xml").WithRelationship(AttachmentRelationship.Source).WithDescription("d");

        var metadata = DocumentTranslator.ToModel(Document([Paragraph("x")], language: "nl-NL").FontFamily("Lato").Attach(attachment)).Value.Metadata;

        metadata.Language.Should().Be("nl-NL");
        metadata.Attachments.Should().Equal([attachment]);
    }

    private static int CurrentLine([CallerLineNumber] int line = 0) => line;

    private static string ThisFile([CallerFilePath] string file = "") => file;
}
