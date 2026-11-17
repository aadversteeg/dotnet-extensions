using System.Collections.Immutable;
using Ave.Extensions.Documents;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Fluent;
using Ave.Extensions.Documents.Pdf;
using Ave.Extensions.Documents.Render.Pdf;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.ErrorPaths;
using static Ave.Extensions.Documents.Fluent.Elements;

namespace UnitTests.Extensions.Documents.Fluent;

/// <summary>
/// No length the front-end accepts is longer than <c>InputLimits.MaxLength</c> (14 400 pt, the largest page
/// side): every length property at the limit is accepted and just above it is refused with the property's code, in
/// every profile. Without the limit a margin of 3 000 000 000 pt was laid out off the page and written as an integer
/// PDF/A does not allow (veraPDF rule 6.1.13-1).
/// </summary>
public class LengthLimitTests
{
    private const double Limit = 14_400;

    private static readonly PdfRenderOptions Options = new(new DateTimeOffset(2026, 11, 16, 6, 30, 0, TimeSpan.Zero));

    private static Document Doc(params Element[] body) => Document([.. body]).FontFamily("Lato");

    private static ImmutableArray<ErrorCode> Codes(Document document)
    {
        var result = DocumentTranslator.ToModel(document);
        return result.IsSuccess ? [] : [.. result.Error.Parts().Select(part => part.Code)];
    }

    /// <summary>Each length property, set to a value, in a document that is otherwise valid; with its error code.</summary>
    public static TheoryData<string> Properties =>
    [
        "margin", "padding", "border", "font-size", "column", "image-width", "image-height", "area-height", "page-width",
        "page-height", "page-margin",
    ];

    private static (Document Document, ErrorCode Code) WithLength(string property, double points)
    {
        var length = points.Pt();
        return property switch
        {
            "margin" => (Doc(Paragraph("x").MarginLeft(length)), DocumentErrorCodes.Input.Invalid.Length),
            "padding" => (Doc(Block([Paragraph("x")]).PaddingLeft(length)), DocumentErrorCodes.Input.Invalid.Length),
            "border" => (Doc(Block([Paragraph("x")]).BorderLeft(length, Color.Black)), DocumentErrorCodes.Input.Invalid.Length),
            "font-size" => (Doc(Paragraph("x").FontSize(length)), DocumentErrorCodes.Input.Invalid.FontSize),
            "column" => (Doc(Table(Columns(length), [TableRow([Cell("x")])])), DocumentErrorCodes.Input.Invalid.Columns),
            "image-width" => (Doc(Image(TestImages.Bytes("rgba8.png")).Width(length).Decorative()), DocumentErrorCodes.Input.Invalid.ImageSize),
            "image-height" => (Doc(Image(TestImages.Bytes("rgba8.png")).Height(length).Decorative()), DocumentErrorCodes.Input.Invalid.ImageSize),
            "area-height" => (Document([Paragraph("x")], PageSetup.A4.Header(Paragraph("h"), length)).FontFamily("Lato"), DocumentErrorCodes.Input.Invalid.Length),
            "page-width" => (Document([Paragraph("x")], Page(length, 500.Pt())).FontFamily("Lato"), DocumentErrorCodes.Input.Invalid.PageSize),
            "page-height" => (Document([Paragraph("x")], Page(500.Pt(), length)).FontFamily("Lato"), DocumentErrorCodes.Input.Invalid.PageSize),
            _ => (Document([Paragraph("x")], Page(Limit.Pt(), Limit.Pt()).Margin((points / 2).Pt())).FontFamily("Lato"), DocumentErrorCodes.Input.Invalid.Length),
        };
    }

    [Theory]
    [MemberData(nameof(Properties))]
    public void A_length_of_14400_pt_is_accepted(string property)
    {
        // The page margin is set to half the length on each side, so the limit leaves no page area; test it a little lower.
        var (document, code) = WithLength(property, property == "page-margin" ? Limit - 2 : Limit);

        Codes(document).Should().NotContain(code);
    }

    [Theory]
    [MemberData(nameof(Properties))]
    public void A_length_above_14400_pt_is_refused_with_the_property_s_code(string property)
    {
        var (document, code) = WithLength(property, property == "page-margin" ? 2 * Limit + 2 : Limit + 0.01);

        Codes(document).Should().Contain(code);
    }

    [Theory]
    [InlineData(3_000_000_000.0)]
    [InlineData(2_147_483_648.0)]
    [InlineData(1e20)]
    public void The_margin_of_three_billion_points_is_an_input_error_in_every_profile(double points)
    {
        var document = Document([Paragraph("x").MarginLeft(points.Pt())], Page(300.Pt(), 200.Pt()).Margin(10.Pt()), title: "Title", language: "en")
            .FontFamily("Lato");

        foreach (var profile in new[] { PdfProfile.Plain, PdfProfile.PdfA3b, PdfProfile.PdfA3a, PdfProfile.PdfUa1 })
        {
            var result = document.ToPdf(TestFonts.Set, Options with { Profile = profile });

            result.IsFailure.Should().BeTrue($"{profile} must refuse the margin");
            result.Error.Code.Should().Be(DocumentErrorCodes.Input.Invalid.Length);
            result.Error.Metadata![ErrorMetadataKeys.Property].Should().Be("margin-left");
        }
    }
}
