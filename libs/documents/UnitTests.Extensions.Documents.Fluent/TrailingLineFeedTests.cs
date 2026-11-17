using Ave.Documents.Core;
using Ave.Documents.Fluent;
using static Ave.Documents.Fluent.Elements;

namespace UnitTests.Ave.Documents.Fluent;

/// <summary>
/// A trailing line feed in every string the front-end validates. The two values checked against a grammar — a
/// language tag (RFC 3066 §2.1) and a media type (RFC 6838 §4.2) — must match all of the string: a line feed is not
/// part of either grammar. Free text (title, author, file name, description) may hold a line feed, which XML allows.
/// </summary>
public class TrailingLineFeedTests
{
    [Theory]
    [InlineData("en\n")]
    [InlineData("nl-NL\n")]
    [InlineData("en\r\n")]
    public void A_language_with_a_trailing_line_feed_is_not_a_language_tag(string language) =>
        DocumentTranslator.ToModel(Document([Paragraph("x")], language: language).FontFamily("Lato")).Error.Code
            .Should().Be(DocumentErrorCodes.Input.Invalid.Language);

    [Theory]
    [InlineData("application/xml\n")]
    [InlineData("text/plain\r\n")]
    public void A_media_type_with_a_trailing_line_feed_is_not_a_media_type(string mediaType)
    {
        var error = DocumentTranslator.ToModel(Document([Paragraph("x")]).FontFamily("Lato").Attach(Attachment("a.xml", "<a/>"u8, mediaType))).Error;

        error.Code.Should().Be(DocumentErrorCodes.Input.Invalid.Attachment);
        error.Metadata![ErrorMetadataKeys.Property].Should().Be("media-type");
    }

    [Fact]
    public void Free_text_may_end_in_a_line_feed() =>
        DocumentTranslator.ToModel(Document([Paragraph("x")], title: "Title\n", author: "Author\n").FontFamily("Lato")
                .Attach(Attachment("a.xml\n", "<a/>"u8, "application/xml").WithDescription("Data\n")))
            .IsSuccess.Should().BeTrue();
}
