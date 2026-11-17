using System.Collections.Immutable;
using Ave.Extensions.Documents;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Fluent;
using Ave.Extensions.Documents.Pdf;
using Ave.Extensions.Documents.Render.Pdf;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;
using static Ave.Extensions.Documents.Fluent.Elements;

namespace UnitTests.Extensions.Documents.Fluent;

/// <summary>
/// Ordinary input through the front-end that a profile cannot carry: the file is refused with the profile's code instead
/// of being written with a conformance claim veraPDF rejects. These are the limits of PDF/A-3 (veraPDF rules 6.1.13-3,
/// -4, -11 and 6.8-1); docs/conformance-matrix.md lists how every other rule is met.
/// </summary>
public class ProfileLimitTests
{
    private static readonly PdfRenderOptions Options = new(new DateTimeOffset(2026, 11, 15, 9, 22, 0, TimeSpan.FromHours(1)));

    private static Result<ImmutableArray<byte>, Error> Render(Document document, PdfProfile profile) =>
        document.ToPdf(TestFonts.Set, Options with { Profile = profile });

    private static string Describe(Error error) => string.Join("; ", error.Parts().Select(part => $"{part.Code}: {part.Message}"));

    private static ErrorCode CodeOf(Document document, PdfProfile profile)
    {
        var result = Render(document, profile);
        result.IsFailure.Should().BeTrue($"{profile} must refuse this document");
        return result.Error.Code;
    }

    private static Document With(Attachment attachment) =>
        Document([Paragraph("x")], title: "Limits", language: "en").FontFamily("Lato").Attach(attachment.WithRelationship(AttachmentRelationship.Data));

    [Theory]
    [InlineData("application/x!y")]
    [InlineData("application/x#y")]
    public void A_media_type_RFC_6838_allows_but_PDF_A_3_does_not_is_refused(string mediaType)
    {
        var document = With(Attachment("data.bin", "1"u8, mediaType));

        CodeOf(document, PdfProfile.PdfA3b).Should().Be(PdfRenderErrorCodes.Conformance.PdfA3.MediaType);
        var plain = Render(document, PdfProfile.Plain);
        plain.IsSuccess.Should().BeTrue(plain.IsFailure ? Describe(plain.Error) : string.Empty);
    }

    [Fact]
    public void A_media_type_longer_than_a_PDF_A_name_is_refused()
    {
        var document = With(Attachment("data.bin", "1"u8, "application/" + new string('x', 120)));

        CodeOf(document, PdfProfile.PdfA3b).Should().Be(PdfRenderErrorCodes.Conformance.PdfA3.NameTooLong);
    }

    [Theory]
    [InlineData(2, 2)]
    [InlineData(2.99, 500)]
    [InlineData(500, 1)]
    public void A_page_side_below_3_units_is_refused_under_PDF_A(double width, double height)
    {
        // No content, so that a 2pt page fails on its size, not on content that does not fit. A side above 14 400 units
        // never reaches the back-end: the front-end refuses any length above it in every profile (LengthLimitTests).
        var document = Document([], Page(width.Pt(), height.Pt()), title: "Limits", language: "en").FontFamily("Lato");

        var result = Render(document, PdfProfile.PdfA3b);

        result.IsFailure.Should().BeTrue();
        result.Error.Parts().Select(part => part.Code).Should().Contain(PdfRenderErrorCodes.Conformance.PdfA3.PageSize);
        var plain = Render(document, PdfProfile.Plain);
        plain.IsSuccess.Should().BeTrue(plain.IsFailure ? Describe(plain.Error) : string.Empty);
    }

    [Theory]
    [InlineData(3, 3)]
    [InlineData(14400, 14400)]
    public void Page_sides_of_exactly_3_and_14400_units_are_allowed(double width, double height)
    {
        var document = Document([], Page(width.Pt(), height.Pt()), title: "Limits", language: "en").FontFamily("Lato");

        var result = Render(document, PdfProfile.PdfA3b);

        result.IsFailure.Should().BeFalse(result.IsFailure ? Describe(result.Error) : string.Empty);
    }

    [Fact]
    public void A_title_longer_than_a_PDF_A_string_is_refused()
    {
        // 17 000 characters with one outside ASCII: UTF-16 with a byte-order mark, 34 002 bytes.
        var document = Document([Paragraph("x")], title: "é" + new string('t', 16999), language: "en").FontFamily("Lato");

        CodeOf(document, PdfProfile.PdfA3b).Should().Be(PdfRenderErrorCodes.Conformance.PdfA3.TextTooLong);
    }

    [Fact]
    public void A_title_of_exactly_32767_bytes_is_allowed()
    {
        var document = Document([Paragraph("x")], title: new string('t', ProfileLimitsValues.MaxStringBytes), language: "en").FontFamily("Lato");

        var result = Render(document, PdfProfile.PdfA3b);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? Describe(result.Error) : string.Empty);
    }

    /// <summary>The limit as the tests know it, so a change of the constant shows up here.</summary>
    private static class ProfileLimitsValues
    {
        public const int MaxStringBytes = 32767;
    }
}
