using System.Collections.Immutable;
using System.Text;
using System.Xml.Linq;
using Ave.Documents.Samples;
using Ave.Documents.TestSupport;
using UglyToad.PdfPig;

namespace UnitTests.Ave.Documents.Samples;

/// <summary>
/// The samples as PDF/A-3a + PDF/UA-1, read back with PdfPig (an independent reader): the structure tree (roles, order,
/// table attributes), the marking of every glyph as tagged content or artifact, the language and XMP identification,
/// and the attachment byte for byte. veraPDF's verdict is in <see cref="ReferenceValidationTests"/>.
/// </summary>
public class TaggedPdfTests
{
    private static ImmutableArray<byte> NormalDutch() => SampleFiles.Pdf(SampleFiles.Timesheet("normal", "nl-NL"));

    [Fact]
    public void The_timesheet_structure_tree_matches_its_golden_file() =>
        Golden.Compare("timesheet-normal.nl.structure.txt", PdfStructureReadBack.Outline(PdfStructureReadBack.Read(NormalDutch()))).Should().BeNull();

    [Fact]
    public void The_table_has_one_header_row_of_column_headers_even_though_it_repeats_on_page_two()
    {
        var elements = PdfStructureReadBack.Read(NormalDutch()).Single().DepthFirst().ToList();

        var head = elements.Should().ContainSingle(element => element.Type == "THead", "the repeated header row is a pagination artifact").Which;
        var headers = head.Children.Single().Children;
        headers.Select(cell => (cell.Type, cell.Details)).Should().AllBeEquivalentTo(("TH", "Scope=Column"));
        headers.Select(cell => cell.DepthFirst().Last().Text).Should().Equal("Uitgevoerd op", "Taak", "Titel", "Duur (uur)");
    }

    [Fact]
    public void Cells_that_span_rows_or_columns_carry_RowSpan_and_ColSpan()
    {
        var cells = PdfStructureReadBack.Read(NormalDutch()).Single().DepthFirst().Where(element => element.Type == "TD").ToList();

        cells.Should().Contain(cell => cell.Details == "RowSpan=2", "the date and the hours span an entry and its description");
        cells.Should().Contain(cell => cell.Details == "ColSpan=2", "a description spans the task and item columns");
        cells.Should().Contain(cell => cell.Details == "ColSpan=3", "the total label spans three columns");
    }

    [Theory]
    [InlineData("sample-a-letter")]
    [InlineData("timesheet-long.en")]
    public void Every_glyph_is_either_tagged_content_or_an_artifact(string name)
    {
        using var pdf = PdfDocument.Open([.. SampleFiles.Pdf(SampleFiles.Documents.Single(sample => sample.Name == name).Document)]);

        foreach (var page in pdf.GetPages())
        {
            var marked = page.GetMarkedContents().Sum(content => content.Letters.Count);
            marked.Should().Be(page.Letters.Count, $"every glyph on page {page.Number} must be inside a marked-content sequence (ISO 14289-1 §7.1)");
        }
    }

    [Fact]
    public void The_letter_s_first_heading_is_level_one_after_its_address_lines()
    {
        var document = PdfStructureReadBack.Read(SampleFiles.Pdf(SampleFiles.Documents[0].Document)).Single();

        document.Type.Should().Be("Document");
        document.Children.Select(child => child.Type).Take(3).Should().Equal("P", "P", "H1");
        document.DepthFirst().Count(element => element.Type == "P").Should().BeGreaterThan(5);
    }

    [Theory]
    [InlineData("timesheet-normal.nl")]
    [InlineData("timesheet-long.en")]
    [InlineData("sample-a-letter")]
    public void The_parent_tree_sends_every_marked_content_to_the_element_that_owns_it(string name) =>
        PdfStructureReadBack.ParentTreeDifferences(SampleFiles.Pdf(SampleFiles.Documents.Single(sample => sample.Name == name).Document)).Should().BeEmpty();

    [Fact]
    public void The_attachment_reads_back_byte_for_byte()
    {
        using var pdf = PdfDocument.Open([.. NormalDutch()]);

        pdf.Advanced.TryGetEmbeddedFiles(out var files).Should().BeTrue();
        var file = files!.Should().ContainSingle().Which;
        file.Name.Should().Be(TimesheetXml.FileName);
        file.Bytes.ToArray().Should().Equal(Encoding.UTF8.GetBytes(TimesheetXml.Summary(SampleFiles.Data("normal"))));
    }

    [Fact]
    public void The_metadata_names_the_language_and_both_standards()
    {
        using var pdf = PdfDocument.Open([.. NormalDutch()]);

        pdf.TryGetXmpMetadata(out var metadata).Should().BeTrue();
        var xmp = XDocument.Parse(Encoding.UTF8.GetString(metadata!.GetXmlBytes().ToArray()));
        string Value(string ns, string name) => xmp.Descendants(XName.Get(name, ns)).Single().Value;
        Value("http://www.aiim.org/pdfa/ns/id/", "part").Should().Be("3");
        Value("http://www.aiim.org/pdfa/ns/id/", "conformance").Should().Be("A");
        Value("http://www.aiim.org/pdfua/ns/id/", "part").Should().Be("1");
        Value("http://purl.org/dc/elements/1.1/", "language").Should().Be("nl-NL");
        Value("http://ns.adobe.com/xap/1.0/", "CreateDate").Should().Be("2026-11-15T09:22:00+01:00");
    }

    [Fact]
    public void A_plain_file_has_no_structure_and_no_metadata()
    {
        var plain = SampleFiles.Pdf(SampleFiles.Timesheet("edge", "en-US"), SampleOutput.Options with { Profile = global::Ave.Documents.Pdf.PdfProfile.Plain });

        PdfStructureReadBack.Read(plain).Should().BeEmpty();
        using var pdf = PdfDocument.Open([.. plain]);
        pdf.TryGetXmpMetadata(out _).Should().BeFalse();
    }
}
