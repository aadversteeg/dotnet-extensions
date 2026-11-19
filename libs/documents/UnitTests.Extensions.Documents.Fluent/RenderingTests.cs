using System.Runtime.CompilerServices;
using System.Text;
using Ave.Extensions.Documents;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Fluent;
using Ave.Extensions.Documents.Render.Pdf;
using Ave.Extensions.Documents.TestSupport;
using Ave.Extensions.Documents.Fonts;
using Ave.Extensions.Documents.Layout;
using static Ave.Extensions.Documents.Fluent.Elements;

namespace UnitTests.Extensions.Documents.Fluent;

/// <summary>The whole pipeline through <see cref="DocumentRendering.ToPdf(Document, FontSet, PdfRenderOptions)"/>: errors from later layers still
/// point at the caller's line.</summary>
public class RenderingTests
{
    private static readonly PdfRenderOptions Options = new(new DateTimeOffset(2026, 11, 15, 9, 22, 0, TimeSpan.Zero));

    [Fact]
    public void A_valid_document_becomes_a_pdf()
    {
        var pdf = Document([Heading(1, "Hello"), Paragraph("World")]).FontFamily("Lato").FontSize(11.Pt()).ToPdf(TestFonts.Set, Options);

        pdf.IsSuccess.Should().BeTrue();
        Encoding.ASCII.GetString(pdf.Value.AsSpan(0, 8)).Should().Be("%PDF-1.7");
        PdfReadBack.Letters(pdf.Value).Single().Select(letter => letter.Value).Should().Equal("HelloWorld".Select(character => character.ToString()));
    }

    [Fact]
    public void A_character_the_font_lacks_is_an_error_at_the_line_of_its_text()
    {
        var line = CurrentLine() + 1;
        var span = Text("Price in yen: ¥ and kanji 漢");

        var error = Document([Paragraph([Bold("Note: "), span])]).FontFamily("Lato").ToPdf(TestFonts.Set, Options).Error;

        error.Code.Should().Be(FontErrorCodes.GlyphMissing);
        error.Metadata![ErrorMetadataKeys.Character].Should().Be("漢");
        error.Metadata![ErrorMetadataKeys.SourceFile].Should().Be(ThisFile());
        error.Metadata![ErrorMetadataKeys.SourceLine].Should().Be(line);
    }

    [Fact]
    public void A_block_that_can_never_fit_a_page_is_an_error_at_the_line_of_the_block()
    {
        var line = CurrentLine() + 1;
        var tall = Block([Paragraph("x")]).PaddingTop(30.Cm());

        var error = Document([Paragraph("Before"), tall]).FontFamily("Lato").ToPdf(TestFonts.Set, Options).Error;

        error.Code.Should().Be(LayoutErrorCodes.DoesNotFit);
        error.Metadata![ErrorMetadataKeys.SourceFile].Should().Be(ThisFile());
        error.Metadata![ErrorMetadataKeys.SourceLine].Should().Be(line);
    }

    [Fact]
    public void A_weight_the_font_set_does_not_have_is_an_error_at_the_line_of_the_text()
    {
        var line = CurrentLine() + 1;
        var span = Text("semi-bold").FontWeight(new FontWeight(600));

        var error = Document([Paragraph([span])]).FontFamily("Lato").ToPdf(TestFonts.Set, Options).Error;

        error.Code.Should().Be(FontErrorCodes.NotFound);
        error.Metadata![ErrorMetadataKeys.SourceLine].Should().Be(line);
    }

    [Fact]
    public void A_default_or_incomplete_font_set_is_an_error_not_an_exception()
    {
        var document = Document([Paragraph("World")]).FontFamily("Lato");

        document.ToPdf(new FontSet(default), Options).Error.Code.Should().Be(FontErrorCodes.NotFound);
        document.ToPdf(new FontSet([TestFonts.Regular, null!]), Options).Error.Code.Should().Be(FontErrorCodes.NotFound);
        document.ToPng(new FontSet([null!]), 1, new()).Error.Code.Should().Be(FontErrorCodes.NotFound);
    }

    private static int CurrentLine([CallerLineNumber] int line = 0) => line;

    private static string ThisFile([CallerFilePath] string file = "") => file;
}
