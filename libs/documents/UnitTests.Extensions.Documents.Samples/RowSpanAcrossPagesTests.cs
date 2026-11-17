using System.Collections.Immutable;
using System.IO;
using Ave.Extensions.Documents;
using Ave.Extensions.Documents.Core;
using Ave.Extensions.Documents.Fluent;
using Ave.Extensions.Documents.Samples;
using Ave.Extensions.Documents.TestSupport;
using static Ave.Extensions.Documents.Fluent.Elements;

namespace UnitTests.Extensions.Documents.Samples;

/// <summary>
/// A cell that spans rows and is cut by a page end, in a tagged PDF. In the structure tree a cell is a kid of the row it
/// starts in (ISO 32000-1 §14.8.4.3.4, table elements: a TD is a child of its TR), on every page it appears on; a cell
/// that continued under another row on the next page made the file impossible to write.
/// </summary>
public class RowSpanAcrossPagesTests
{
    private static string Words(int count) => string.Join(" ", Enumerable.Range(1, count).Select(index => $"word{index}"));

    /// <summary>A two-column table whose first cell spans both rows, after <paramref name="filler"/> paragraphs, on a
    /// page so small that the second row (60 words) does not fit on the first page.</summary>
    private static Document SmallPageTable(int filler) =>
        Document(
            [
                .. Enumerable.Range(0, filler).Select(index => (Element)Paragraph($"filler {index}")),
                Table(Columns(80.Pt(), Fill(1)),
                [
                    TableRow([Cell("span").RowSpan(2), Cell("first row")]),
                    TableRow([Cell(Words(60))]),
                ]).GridLines(1.Pt(), Color.Black),
            ],
            Page(300.Pt(), 200.Pt()).Margin(10.Pt()),
            title: "Row span across pages",
            language: "en").FontFamily("Lato");

    /// <summary>The table example of docs/code-front-end.md with a description longer than a page.</summary>
    private static Document GuideExample(int words) =>
        Document(
            [
                Table(Columns(Fill(2.5), Fill(3), Fill(10), Fill(2)),
                [
                    HeaderRow([Cell("Date"), Cell("Task"), Cell("Title"), Cell("Hours")]).Bold(),
                    RowGroup(
                    [
                        TableRow([Cell("2026-01-01").RowSpan(2), Cell("T-1"), Cell("Title"), Cell("8.00").RowSpan(2)]),
                        TableRow([Cell(Words(words)).ColumnSpan(2).Italic().FontSize(8.Pt())]),
                    ]).KeepTogether(),
                ]).GridLines(1.Pt(), Color.Black).CellPadding(4.Pt(), 2.Pt()),
            ],
            PageSetup.A4.Margin(1.Cm()),
            title: "Guide example",
            language: "en").FontFamily("Lato");

    private static ImmutableArray<(string Name, Document Document)> Cases { get; } =
    [
        .. new[] { 0, 3, 6, 8 }.Select(filler => ($"small-page-filler-{filler}", SmallPageTable(filler))),
        ("guide-example-1500-words", GuideExample(1500)),
    ];

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(6)]
    [InlineData(8)]
    public void A_spanning_cell_cut_by_a_page_stays_a_kid_of_the_row_it_starts_in(int filler)
    {
        var pdf = SmallPageTable(filler).ToPdf(TestFonts.SetWithItalic, SampleOutput.Options);

        pdf.IsSuccess.Should().BeTrue(pdf.IsFailure ? pdf.Error.ToString() : string.Empty);
        var table = PdfStructureReadBack.Read(pdf.Value).Single().DepthFirst().Single(element => element.Type == "Table");
        var rows = table.DepthFirst().Where(element => element.Type == "TR").ToList();
        rows.Should().HaveCount(2);
        rows[0].Children.Select(cell => (cell.Type, cell.Details)).Should().Equal(("TD", "RowSpan=2"), ("TD", string.Empty));
        rows[0].Children[0].DepthFirst().Last().Text.Should().Be("span");
        rows[1].Children.Should().ContainSingle().Which.DepthFirst().Should().Contain(element => element.Text.Contains("word60"));
    }

    [Fact]
    public void The_guide_s_table_example_with_a_description_longer_than_a_page_is_written_tagged()
    {
        var pdf = GuideExample(1500).ToPdf(TestFonts.SetWithItalic, SampleOutput.Options);

        pdf.IsSuccess.Should().BeTrue(pdf.IsFailure ? pdf.Error.ToString() : string.Empty);
        var rows = PdfStructureReadBack.Read(pdf.Value).Single().DepthFirst().Where(element => element.Type == "TR").ToList();
        rows.Should().HaveCount(3, "one header row, an entry and its description; a row continued on a later page is one element");
        rows[1].Children.Select(cell => cell.Details).Should().Equal("RowSpan=2", string.Empty, string.Empty, "RowSpan=2");
    }

    [DockerTheory]
    [Trait("Category", "Reference")]
    [InlineData("3a")]
    [InlineData("ua1")]
    public void Tables_with_a_spanning_cell_cut_by_a_page_pass_veraPDF(string flavour)
    {
        var folder = Directory.CreateTempSubdirectory("ave-extensions-documents-rowspan-").FullName;
        try
        {
            foreach (var (name, document) in Cases)
            {
                File.WriteAllBytes(Path.Combine(folder, name + ".pdf"), [.. SampleFiles.Pdf(document)]);
            }

            var results = VeraPdf.Validate(folder, flavour);

            results.Should().HaveCount(Cases.Length);
            results.Should().OnlyContain(result => result.IsCompliant && result.PassedChecks > 0, VeraPdf.Describe(results));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
