using System.Collections.Immutable;
using System.IO;
using Ave.Documents;
using Ave.Documents.Core;
using Ave.Documents.Fluent;
using Ave.Documents.Pdf;
using Ave.Documents.Render.Pdf;
using Ave.Documents.Samples;
using Ave.Documents.TestSupport;
using static Ave.Documents.Fluent.Elements;

namespace UnitTests.Ave.Documents.Samples;

/// <summary>
/// The text a reader gets from the PDF equals the document's text, run by run. One glyph can stand for several
/// characters (in Lato, hyphen-minus and U+2010 HYPHEN share a glyph; space and no-break space too), while a ToUnicode
/// map holds one text per glyph (ISO 32000-1 §9.10.3). So the map carries the glyph's most frequent text, and every
/// occurrence whose text differs is wrapped in marked content with <c>/ActualText</c> (§14.9.4). The hyphen shown at a
/// soft-hyphen break reads as the soft hyphen U+00AD it stands for. Characters that are not drawn (a soft hyphen where
/// the line does not break, the space where a line wraps) are not in the read-back text.
/// </summary>
public class TextReadBackTests
{
    private static PdfRenderOptions Plain { get; } = SampleOutput.Options with { Profile = PdfProfile.Plain };

    public static TheoryData<string> Profiles => ["plain", "tagged"];

    private static PdfRenderOptions OptionsFor(string profile) => profile == "plain" ? Plain : SampleOutput.Options;

    /// <summary>A page narrow enough that "an extra" fits on a line but "an extraordinary" does not.</summary>
    private static Document Narrow(params Element[] body) =>
        Document([.. body], Page(90.Pt(), 400.Pt()).Margin(10.Pt()), title: "Read-back", language: "en").FontFamily("Lato");

    private static ImmutableArray<string> TextOf(Document document, string profile)
    {
        var pdf = document.ToPdf(TestFonts.SetWithItalic, OptionsFor(profile));
        pdf.IsSuccess.Should().BeTrue(pdf.IsFailure ? pdf.Error.ToString() : string.Empty);
        return PdfReadBack.Text(pdf.Value);
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void Hyphens_after_a_soft_hyphen_break_still_read_as_hyphen_minus(string profile)
    {
        var text = TextOf(Narrow(Paragraph("an extra\u00ADordinary thing"), Paragraph("2026-01-15 a-b"), Paragraph("-12.50")), profile);

        text.Should().ContainSingle();
        text[0].Replace(" ", string.Empty).Should().Be("anextra\u00ADordinarything2026-01-15a-b-12.50",
            "the break hyphen reads as the soft hyphen, every hyphen-minus as hyphen-minus");
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void A_hyphen_before_a_soft_hyphen_break_reads_as_hyphen_minus_too(string profile)
    {
        // The first use of the shared glyph is a hyphen-minus; the break hyphen comes after it.
        var text = TextOf(Narrow(Paragraph("x-y"), Paragraph("an extra\u00ADordinary thing")), profile);

        text[0].Replace(" ", string.Empty).Should().Be("x-yanextra\u00ADordinarything");
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void A_no_break_space_reads_as_a_no_break_space(string profile)
    {
        var text = TextOf(Narrow(Paragraph("a\u00A0b c d\u00A0e")), profile);

        text.Should().Equal("a\u00A0b c d\u00A0e");
    }

    [DockerTheory]
    [Trait("Category", "Reference")]
    [InlineData("3a")]
    [InlineData("ua1")]
    public void Glyphs_with_replacement_text_pass_veraPDF(string flavour)
    {
        var folder = Directory.CreateTempSubdirectory("ave-documents-actualtext-").FullName;
        try
        {
            var document = Narrow(Paragraph("x-y"), Paragraph("an extra\u00ADordinary thing"), Paragraph("a\u00A0b c"));
            File.WriteAllBytes(Path.Combine(folder, "actualtext.pdf"), [.. SampleFiles.Pdf(document)]);

            var results = VeraPdf.Validate(folder, flavour);

            results.Should().ContainSingle().Which.IsCompliant.Should().BeTrue(VeraPdf.Describe(results));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
