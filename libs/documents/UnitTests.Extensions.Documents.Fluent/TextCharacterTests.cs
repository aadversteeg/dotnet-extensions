using System.Runtime.CompilerServices;
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
/// A character text cannot hold (<see cref="TextCharacters"/>) is an input error at the element's line in every profile,
/// before anything is laid out or written: without it, <c>Paragraph("a\0b")</c> became a PDF/A-3a and PDF/UA-1 file
/// with U+0000 in its ToUnicode map, which both profiles forbid.
/// </summary>
public class TextCharacterTests
{
    private static readonly PdfRenderOptions Options = new(new DateTimeOffset(2026, 11, 15, 9, 22, 0, TimeSpan.FromHours(1)));

    public static TheoryData<string> Profiles => ["plain", "3b", "3a", "ua1"];

    private static PdfProfile ProfileNamed(string name) => name switch
    {
        "plain" => PdfProfile.Plain,
        "3b" => PdfProfile.PdfA3b,
        "3a" => PdfProfile.PdfA3a,
        _ => PdfProfile.PdfUa1,
    };

    /// <summary>NUL, a C0 control, DEL, a C1 control, U+FFFE, U+FDD0, U+1FFFF (a surrogate pair), a lone high and a
    /// lone low surrogate, as code points: a lone surrogate in a string argument does not survive the test framework's
    /// serialisation of theory data.</summary>
    public static TheoryData<int> Forbidden => [0x00, 0x01, 0x7F, 0x90, 0xFFFE, 0xFDD0, 0x1FFFF, 0xD800, 0xDC00];

    private static string Text(int codePoint) =>
        codePoint is >= 0xD800 and <= 0xDFFF ? ((char)codePoint).ToString() : char.ConvertFromUtf32(codePoint);

    private static Error ErrorOf(Element element, string profile = "plain")
    {
        var result = Document([element], title: "Characters", language: "en").FontFamily("Lato")
            .ToPdf(TestFonts.SetWithItalic, Options with { Profile = ProfileNamed(profile) });
        result.IsFailure.Should().BeTrue();
        return result.Error;
    }

    [Theory]
    [InlineData(0xD800)]
    [InlineData(0xDBFF)]
    [InlineData(0xDC00)]
    [InlineData(0xDFFF)]
    public void Half_of_a_surrogate_pair_alone_is_an_input_error(int half)
    {
        // A lone surrogate is no character at all: in UTF-16 the code units 0xD800-0xDFFF stand for a character only in pairs.
        var error = ErrorOf(Paragraph("a" + (char)half + "b"));

        error.Code.Should().Be(DocumentErrorCodes.Input.Invalid.Text);
        error.Metadata![ErrorMetadataKeys.Offset].Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(Forbidden))]
    public void A_character_text_cannot_hold_is_an_input_error_at_the_paragraph(int forbidden)
    {
        var line = CurrentLine() + 1;
        var paragraph = Paragraph($"a{Text(forbidden)}b");

        var error = ErrorOf(paragraph);

        error.Code.Should().Be(DocumentErrorCodes.Input.Invalid.Text);
        error.Metadata![ErrorMetadataKeys.SourceLine].Should().Be(line);
        error.Metadata![ErrorMetadataKeys.Offset].Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void NUL_is_refused_in_every_profile(string profile)
    {
        ErrorOf(Paragraph("a\0b"), profile).Code.Should().Be(DocumentErrorCodes.Input.Invalid.Text);
    }

    [Fact]
    public void Alternative_text_follows_the_same_rule()
    {
        var image = Image(TestImages.Bytes("rgb8.png")).Alt("a\u0002b");

        ErrorOf(image).Code.Should().Be(DocumentErrorCodes.Input.Invalid.Text);
    }

    [Fact]
    public void Tab_and_the_forced_line_breaks_are_text()
    {
        var result = Document([Paragraph("a\tb\nc\vd\fe\rf\u0085g é")], title: "Characters", language: "en").FontFamily("Lato")
            .ToPdf(TestFonts.SetWithItalic, Options);

        result.IsFailure.Should().BeFalse(result.IsFailure ? result.Error.ToString() : string.Empty);
    }

    private static int CurrentLine([CallerLineNumber] int line = 0) => line;
}
